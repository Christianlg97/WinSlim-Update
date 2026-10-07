using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using wumgr;

// Compile the real download/install classes with minimal collaborators.
class AppLog { public static void Line(string text, params object[] args) { } }
namespace wumgr
{
    class MsUpdate { public string KB; public string Title; }
    class WuAgent
    {
        public class ProgressArgs : EventArgs
        {
            public ProgressArgs(int total, int percent, int current, int itemPercent, string info) { }
        }
    }
}

class RegressionTests
{
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    static void PumpUntil(Func<bool> completed)
    {
        var frame = new DispatcherFrame();
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += delegate { if (completed() || watch.Elapsed.TotalSeconds > 15) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert(completed(), "asynchronous operation completes within timeout");
    }

    static void DownloadCase(string directory, string headers, string payload, bool success, string expectedName, bool cancel = false)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task server = Task.Run(delegate
        {
            using (TcpClient client = listener.AcceptTcpClient())
            using (NetworkStream stream = client.GetStream())
            {
                var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                while (!string.IsNullOrEmpty(reader.ReadLine())) { }
                byte[] data = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\n" + headers + "\r\n" + payload);
                stream.Write(data, 0, data.Length);
            }
        });
        try
        {
            var download = new HttpTask("http://127.0.0.1:" + port + "/update.cab?token=test", directory);
            HttpTask.FinishedEventArgs result = null;
            download.Finished += delegate(object sender, HttpTask.FinishedEventArgs args) { result = args; };
            if (cancel) download.Progress += delegate { download.Cancel(); };
            Assert(download.Start(), "HTTP download starts");
            PumpUntil(() => result != null);
            Assert(result.Success == success, "HTTP result matches expected integrity status");
            Assert(result.Cancelled == cancel, "HTTP cancellation status preserved");
            if (success)
            {
                Assert(download.DlName == expectedName, "filename excludes URL query and disposition parameters");
                Assert(File.ReadAllText(Path.Combine(directory, expectedName)) == payload, "download contents preserved");
            }
            else Assert(!File.Exists(Path.Combine(directory, download.DlName + ".tmp")), "partial download removed");
            Assert(server.Wait(5000), "local HTTP server finished");
        }
        finally { listener.Stop(); }
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--flood")
        {
            Console.Out.Write(new string('x', 262144));
            Console.Error.Write(new string('y', 262144));
            return 7;
        }
        string directory = Path.Combine(Path.GetTempPath(), "WinSlimRegression_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            UpdateDownloader.FinishedEventArgs result = null;
            var downloader = new UpdateDownloader();
            downloader.Finished += delegate(object sender, UpdateDownloader.FinishedEventArgs e) { result = e; };
            downloader.Download(new List<UpdateDownloader.Task> {
                new UpdateDownloader.Task { Url = "not a URL", Path = directory, KB = "KB1" }
            });
            Assert(result != null && !result.Success && result.Downloads[0].Failed && !downloader.IsBusy(),
                "failed request start is reported and downloader returns idle");
            Assert(!new UpdateDownloader.FinishedEventArgs {
                Downloads = new List<UpdateDownloader.Task>(), Cancelled = true
            }.Success, "cancellation is not reported as success");

            var installer = new UpdateInstaller();
            UpdateInstaller.FinishedEventArgs installed = null;
            installer.Finished += delegate(object sender, UpdateInstaller.FinishedEventArgs e) { installed = e; };
            installer.Install(new List<MsUpdate> { new MsUpdate { KB = "KB1", Title = "Missing files" } },
                new MultiValueDictionary<string, string>());
            PumpUntil(() => installed != null);
            Assert(!installed.Success && installed.ErrorCount == 1, "update without files is not marked installed");

            MethodInfo execute = typeof(UpdateInstaller).GetMethod("ExecTask", BindingFlags.Instance | BindingFlags.NonPublic);
            var start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--flood");
            int code = (int)execute.Invoke(installer, new object[] { start, true });
            Assert(code == 7, "large stdout and stderr drain without blocking and preserve exit code");

            DownloadCase(directory, "Content-Length: 4\r\n", "data", true, "update.cab");
            DownloadCase(directory, "Content-Length: 4\r\nContent-Disposition: attachment; filename=\"other.cab\"; size=4\r\n", "next", true, "other.cab");
            DownloadCase(directory, "Content-Length: 2147483648\r\nContent-Disposition: attachment; filename=\"large.cab\"\r\n", "short", false, null);
            DownloadCase(directory, "Content-Length: 4\r\nContent-Disposition: attachment; filename=\"cancel.cab\"\r\n", "stop", false, null, true);
            Console.WriteLine("All regression tests passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(directory, true); }
    }
}
