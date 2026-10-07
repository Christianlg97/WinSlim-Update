using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

class AppLog { public static void Line(string format, params object[] args) { } }

sealed class RangeServer : IDisposable
{
    internal const long Length = 64L * 1024 * 1024;
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new CancellationTokenSource();
    private readonly ConcurrentBag<Task> clients = new ConcurrentBag<Task>();
    private readonly Task accepting;
    internal readonly int Port;
    internal string Mode = "valid";
    internal int Ranges, FullRequests, Active, Maximum;
    internal int Delay = 1;
    internal RangeServer()
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        accepting = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    clients.Add(Serve(client));
                }
                catch { if (stop.IsCancellationRequested) return; throw; }
            }
        });
    }
    private async Task Serve(TcpClient client)
    {
        using (client)
        using (stop.Token.Register(() => client.Close()))
        {
            bool counted = false;
            try
            {
                NetworkStream stream = client.GetStream();
                StringBuilder headers = new StringBuilder();
                byte[] single = new byte[1];
                while (!headers.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (await stream.ReadAsync(single, 0, 1, stop.Token) == 0) return;
                    headers.Append((char)single[0]);
                    if (headers.Length > 16384) throw new IOException();
                }
                Match range = Regex.Match(headers.ToString(), @"Range: bytes=(\d+)-(\d+)", RegexOptions.IgnoreCase);
                long first = 0, last = Length - 1;
                string status = "200 OK";
                string extra = "ETag: \"test-v1\"\r\n";
                if (range.Success)
                {
                    Interlocked.Increment(ref Ranges);
                    int active = Interlocked.Increment(ref Active);
                    counted = true;
                    int previous;
                    do { previous = Maximum; if (active <= previous) break; }
                    while (Interlocked.CompareExchange(ref Maximum, active, previous) != previous);
                    first = long.Parse(range.Groups[1].Value);
                    last = long.Parse(range.Groups[2].Value);
                    status = Mode == "ignore" ? "200 OK" : "206 Partial Content";
                    extra += "Content-Range: bytes " + (Mode == "wrong" ? first + 1 : first) + "-" + last + "/" + Length + "\r\n";
                    if (Mode == "etag") extra = extra.Replace("test-v1", "test-v2");
                    if (Mode == "limit") { status = "429 Too Many Requests"; extra = "Retry-After: 70\r\n"; }
                    if (Mode == "ignore") { first = 0; last = Length - 1; }
                }
                else Interlocked.Increment(ref FullRequests);
                if (Mode == "limit-short" && range.Success) { status = "429 Too Many Requests"; extra = "Retry-After: 5\r\n"; }
                if (Mode == "http-error") status = "500 Internal Server Error";
                long count = (Mode == "limit" || Mode == "limit-short") && range.Success || Mode == "http-error" ? 0 : last - first + 1;
                byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nConnection: close\r\nContent-Length: " + count + "\r\n" + extra + "\r\n");
                await stream.WriteAsync(head, 0, head.Length, stop.Token);
                if (Mode == "truncated" && range.Success) count = Math.Min(count, 1024);
                byte[] buffer = new byte[65536];
                long offset = first;
                while (count > 0)
                {
                    int size = (int)Math.Min(count, buffer.Length);
                    for (int i = 0; i < size; i++) buffer[i] = (byte)((offset + i) % 251);
                    // Once the last body buffer is sent the client can finish
                    // before the server task's disposal continuation runs.
                    if (count == size && counted) { Interlocked.Decrement(ref Active); counted = false; }
                    await stream.WriteAsync(buffer, 0, size, stop.Token);
                    count -= size; offset += size;
                    if (Delay > 0 && range.Success && count > 0) await Task.Delay(Delay, stop.Token);
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) { }
            finally { if (counted) Interlocked.Decrement(ref Active); }
        }
    }
    public void Dispose()
    {
        stop.Cancel(); listener.Stop();
        accepting.GetAwaiter().GetResult();
        Task.WhenAll(clients).GetAwaiter().GetResult();
        stop.Dispose();
    }
}

static class SegmentedDownloadTests
{
    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
    private static void Verify(string path)
    {
        using (FileStream file = File.OpenRead(path))
        {
            Assert(file.Length == RangeServer.Length, "complete output length");
            byte[] buffer = new byte[81920]; long offset = 0; int read;
            while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                    if (buffer[i] != (byte)((offset + i) % 251)) throw new Exception("Incorrect assembled data");
                offset += read;
            }
        }
        Assert(true, "all output bytes match the source across segment boundaries");
    }
    private static void Integration(string directory, bool ignore, bool cancel, bool error = false, bool backoff = false)
    {
        using (RangeServer server = new RangeServer { Mode = backoff ? "limit-short" : error ? "http-error" : ignore ? "ignore" : "valid", Delay = cancel ? 10 : 0 })
        {
            IWebProxy original = WebRequest.DefaultWebProxy;
            WebRequest.DefaultWebProxy = new WebProxy("http://127.0.0.1:" + server.Port, false);
            try
            {
                HttpTask task = new HttpTask("http://download.windowsupdate.com/test.cab", directory, "integration.cab", true);
                HttpTask.FinishedEventArgs result = null;
                task.Finished += (sender, args) => result = args;
                DispatcherFrame frame = new DispatcherFrame();
                DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                Stopwatch watch = Stopwatch.StartNew(); bool cancelled = false;
                timer.Tick += delegate
                {
                    if (cancel && !cancelled && (backoff ? server.Ranges > 0 && watch.ElapsedMilliseconds > 300 : server.Ranges > 2)) { cancelled = true; task.Cancel(); }
                    if (result != null || watch.Elapsed.TotalSeconds > 30) frame.Continue = false;
                };
                Assert(task.Start(), "HTTP integration starts through local test proxy");
                timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
                Assert(result != null && result.Cancelled == cancel && result.Success == (!cancel && !error), "integration completion/cancellation status");
                Assert(!File.Exists(Path.Combine(directory, "integration.cab.tmp")), "integration removes partial temp file");
                if (!cancel) Verify(Path.Combine(directory, "integration.cab"));
                if (error) Assert(!result.Success, "HTTP error never treats an older existing file as a successful download");
                if (backoff) Assert(server.FullRequests == 1, "cancellation during Retry-After prevents the fallback GET");
                if (ignore) Assert(server.FullRequests >= 2 && server.Ranges == 1, "unsupported ranges fall back once to full GET");
                Assert(server.Maximum <= 4, "concurrency stays at or below four");
            }
            finally { WebRequest.DefaultWebProxy = original; }
        }
    }
    [STAThread]
    public static int Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "WinSlimSegments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert(SegmentedHttpDownload.ChooseConnections(100, 110, 1, 2) == 1 &&
                SegmentedHttpDownload.ChooseConnections(100, 140, 1, 2) == 2 &&
                SegmentedHttpDownload.ChooseConnections(200, 250, 2, 4) == 4, "increase concurrency only for measured improvement");
            Assert(!SegmentedHttpDownload.HasStrongETag("W/\"v1\"") &&
                !SegmentedHttpDownload.IsMicrosoftHost(new Uri("https://windowsupdate.com.example.org/file")), "reject weak validators and lookalike hosts");
            foreach (string mode in new[] { "valid", "ignore", "wrong", "etag", "truncated", "limit" })
            using (RangeServer server = new RangeServer { Mode = mode })
            {
                string path = Path.Combine(directory, "range.tmp");
                SegmentedDownloadResult result = SegmentedHttpDownload.DownloadAsync(new Uri("http://127.0.0.1:" + server.Port + "/file"),
                    path, RangeServer.Length, "\"test-v1\"", CancellationToken.None, delegate { }).GetAwaiter().GetResult();
                Assert(result.Downloaded == (mode == "valid"), "range validation: " + mode);
                if (result.Downloaded) Verify(path);
                else Assert(!File.Exists(path), "failed ranges remove preallocated partial file");
                if (mode == "limit") Assert(result.RetryAfter.TotalSeconds == 70, "respect server Retry-After on throttling");
                Assert(server.Maximum <= 4, "bounded parallel range requests");
            }
            Integration(directory, false, false);
            Integration(directory, true, false);
            Integration(directory, false, true);
            Integration(directory, false, false, true);
            Integration(directory, false, true, false, true);
            SegmentedHttpDownload.RegisterCooldown(new Uri("http://catalog.s.download.windowsupdate.com/file"), TimeSpan.FromSeconds(70));
            Assert(SegmentedHttpDownload.RetryDelay(new Uri("http://download.windowsupdate.com/next-file")).TotalSeconds > 60,
                "server cooldown also covers subsequent files on the Microsoft CDN family");
            HttpTask limited = new HttpTask("http://download.windowsupdate.com/next-file", directory, "limited.cab", true);
            Assert(!limited.Start(), "long Retry-After does not send another network request");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(directory, true); }
    }
}
