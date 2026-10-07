using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

// Explicit, bounded network measurement: 24.25 MiB, no installation/full package.
static class MicrosoftRangeBenchmark
{
    private static readonly Uri Source = new Uri("https://catalog.s.download.windowsupdate.com/microsoftupdate/v6/wsusscan/wsusscn2.cab");
    private const string Group = "WinSlimBoundedBenchmark";
    private static async Task Read(long start, long count)
    {
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Source);
        request.ConnectionGroupName = Group;
        request.ServicePoint.ConnectionLimit = 4;
        request.AddRange(start, start + count - 1);
        using (CancellationTokenSource timeout = new CancellationTokenSource(15000))
        using (timeout.Token.Register(() => request.Abort()))
        using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
        {
            string expected = "bytes " + start + "-" + (start + count - 1) + "/";
            if (response.StatusCode != HttpStatusCode.PartialContent || response.ContentLength != count ||
                !(response.Headers[HttpResponseHeader.ContentRange] ?? "").StartsWith(expected, StringComparison.Ordinal))
                throw new IOException("The server did not accept the exact bounded range; benchmark stopped.");
            using (Stream stream = response.GetResponseStream())
            {
                byte[] buffer = new byte[81920]; long bytes = 0; int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) > 0) bytes += read;
                if (bytes != count) throw new IOException("Truncated benchmark range.");
            }
        }
    }
    private static async Task Run()
    {
        try
        {
            await Read(0, 262144);
            long offset = 262144;
            Console.WriteLine("connections,seconds,MiB_per_second");
            foreach (int connections in new[] { 1, 2, 4, 4, 2, 1 })
            {
                const long bytes = 4 * 1024 * 1024;
                Stopwatch watch = Stopwatch.StartNew();
                Task[] tasks = new Task[connections];
                for (int i = 0; i < connections; i++) tasks[i] = Read(offset + i * bytes / connections, bytes / connections);
                await Task.WhenAll(tasks);
                offset += bytes;
                Console.WriteLine(connections + "," + watch.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + "," +
                    (4 / watch.Elapsed.TotalSeconds).ToString("F2", CultureInfo.InvariantCulture));
            }
        }
        finally { ServicePointManager.FindServicePoint(Source).CloseConnectionGroup(Group); }
    }
    public static int Main()
    {
        try { Run().GetAwaiter().GetResult(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
