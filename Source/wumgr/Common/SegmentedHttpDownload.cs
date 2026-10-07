using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

internal sealed class SegmentedDownloadResult
{
    internal bool Downloaded;
    internal int Connections = 1;
    internal string FallbackReason;
    internal TimeSpan RetryAfter;
}

internal static class SegmentedHttpDownload
{
    internal const long MinimumLength = 64L * 1024 * 1024;
    private const long SampleLength = 2L * 1024 * 1024;
    private const long ChunkLength = 4L * 1024 * 1024;
    private static readonly SemaphoreSlim Slots = new SemaphoreSlim(4, 4);
    private static readonly ConcurrentDictionary<string, long> Cooldowns = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex ContentRange = new Regex(@"^bytes (\d+)-(\d+)/(\d+)$", RegexOptions.CultureInvariant);

    internal static bool IsMicrosoftHost(Uri uri)
    {
        string host = uri.DnsSafeHost;
        return (uri.Scheme == "http" || uri.Scheme == "https") &&
            (host == "download.microsoft.com" || host.EndsWith(".download.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
             host == "windowsupdate.com" || host.EndsWith(".windowsupdate.com", StringComparison.OrdinalIgnoreCase) ||
             host.EndsWith(".dl.delivery.mp.microsoft.com", StringComparison.OrdinalIgnoreCase) || host == "dl.delivery.mp.microsoft.com");
    }

    internal static bool HasStrongETag(string etag)
    {
        return !string.IsNullOrEmpty(etag) && etag.Length >= 2 && etag[0] == '"' && etag[etag.Length - 1] == '"';
    }

    internal static int ChooseConnections(double previousRate, double candidateRate, int previous, int candidate)
    {
        // Prefer the lower concurrency unless improvement exceeds measurement noise.
        return previousRate > 0 && candidateRate >= previousRate * 1.20 ? candidate : previous;
    }

    private static string CooldownKey(Uri uri)
    {
        string host = uri.DnsSafeHost;
        if (host == "windowsupdate.com" || host.EndsWith(".windowsupdate.com", StringComparison.OrdinalIgnoreCase)) return "windowsupdate.com";
        if (host == "download.microsoft.com" || host.EndsWith(".download.microsoft.com", StringComparison.OrdinalIgnoreCase)) return "download.microsoft.com";
        if (host.EndsWith(".dl.delivery.mp.microsoft.com", StringComparison.OrdinalIgnoreCase) || host == "dl.delivery.mp.microsoft.com") return "dl.delivery.mp.microsoft.com";
        return host;
    }

    internal static TimeSpan RetryDelay(Uri uri)
    {
        long until;
        return Cooldowns.TryGetValue(CooldownKey(uri), out until) && until > DateTime.UtcNow.Ticks
            ? TimeSpan.FromTicks(Math.Max(0, until - DateTime.UtcNow.Ticks)) : TimeSpan.Zero;
    }

    internal static void RegisterCooldown(Uri uri, TimeSpan delay)
    {
        long now = DateTime.UtcNow.Ticks;
        long until = now + Math.Min(delay.Ticks, DateTime.MaxValue.Ticks - now);
        Cooldowns.AddOrUpdate(CooldownKey(uri), until, (key, previous) => Math.Max(previous, until));
    }

    internal static async Task<SegmentedDownloadResult> DownloadAsync(Uri uri, string path, long length,
        string etag, CancellationToken cancellation, Action<long> progress)
    {
        SegmentedDownloadResult result = new SegmentedDownloadResult();
        if (length < MinimumLength || !HasStrongETag(etag))
            return result;
        string group = "WinSlimRanges-" + Guid.NewGuid().ToString("N");
        long received = 0;
        ConcurrentBag<TimeSpan> retryDelays = new ConcurrentBag<TimeSpan>();
        ServicePoint servicePoint = ServicePointManager.FindServicePoint(uri);
        servicePoint.ConnectionLimit = Math.Max(servicePoint.ConnectionLimit, 4);
        using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            try
            {
                using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                    output.SetLength(length);
                Action<int> report = count => progress(Interlocked.Add(ref received, count));
                long offset = 0;
                // Warm one connection, then compare useful, non-overlapping samples.
                await Round(uri, path, length, etag, group, offset, 1, SampleLength, linked, report, retryDelays).ConfigureAwait(false);
                offset += SampleLength;
                double one = await Round(uri, path, length, etag, group, offset, 1, SampleLength, linked, report, retryDelays).ConfigureAwait(false);
                offset += SampleLength;
                double two = await Round(uri, path, length, etag, group, offset, 2, SampleLength, linked, report, retryDelays).ConfigureAwait(false);
                offset += 2 * SampleLength;
                int connections = ChooseConnections(one, two, 1, 2);
                if (connections == 2)
                {
                    double four = await Round(uri, path, length, etag, group, offset, 4, SampleLength, linked, report, retryDelays).ConfigureAwait(false);
                    offset += 4 * SampleLength;
                    connections = ChooseConnections(two, four, 2, 4);
                }
                result.Connections = connections;
                if (connections == 1 && offset < length)
                {
                    // A single long response avoids per-chunk round trips when
                    // parallelism does not improve the measured throughput.
                    await Range(uri, path, length, etag, group, offset, length - 1, linked, report, retryDelays).ConfigureAwait(false);
                    offset = length;
                }
                while (offset < length)
                {
                    await Round(uri, path, length, etag, group, offset, connections, ChunkLength, linked, report, retryDelays).ConfigureAwait(false);
                    offset += Math.Min(length - offset, connections * ChunkLength);
                }
                if (received != length)
                    throw new EndOfStreamException("Incomplete ranged download");
                cancellation.ThrowIfCancellationRequested();
                result.Downloaded = true;
            }
            catch (Exception error)
            {
                if (cancellation.IsCancellationRequested)
                {
                    try { File.Delete(path); } catch { }
                    cancellation.ThrowIfCancellationRequested();
                }
                RangeDownloadException limit = error as RangeDownloadException;
                if (limit != null) result.RetryAfter = limit.RetryAfter;
                foreach (TimeSpan delay in retryDelays)
                    if (delay > result.RetryAfter) result.RetryAfter = delay;
                result.FallbackReason = "No se pudieron validar los rangos; se usará una conexión.";
                try { File.Delete(path); } catch { }
            }
            finally { servicePoint.CloseConnectionGroup(group); }
        }
        return result;
    }

    private static async Task<double> Round(Uri uri, string path, long total, string etag, string group,
        long offset, int connections, long chunk, CancellationTokenSource cancellation, Action<int> progress, ConcurrentBag<TimeSpan> retryDelays)
    {
        List<Task> tasks = new List<Task>();
        Stopwatch watch = Stopwatch.StartNew();
        long bytes = 0;
        for (int i = 0; i < connections && i * chunk < total - offset; i++)
        {
            long start = offset + i * chunk;
            long end = start + Math.Min(chunk, total - start) - 1;
            bytes += end - start + 1;
            tasks.Add(Range(uri, path, total, etag, group, start, end, cancellation, progress, retryDelays));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return bytes / Math.Max(0.001, watch.Elapsed.TotalSeconds);
    }

    private static async Task Range(Uri uri, string path, long total, string etag, string group, long start,
        long end, CancellationTokenSource cancellation, Action<int> progress, ConcurrentBag<TimeSpan> retryDelays)
    {
        await Slots.WaitAsync(cancellation.Token).ConfigureAwait(false);
        HttpWebRequest request = null;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            request = (HttpWebRequest)WebRequest.Create(uri);
            request.ConnectionGroupName = group;
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.None;
            request.Headers[HttpRequestHeader.AcceptEncoding] = "identity";
            request.Headers[HttpRequestHeader.IfRange] = etag;
            request.AddRange(start, end);
            request.ReadWriteTimeout = 30000;
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
            using (timeout.Token.Register(() => request.Abort()))
            {
                timeout.CancelAfter(30000);
                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                {
                    Match range = ContentRange.Match(response.Headers[HttpResponseHeader.ContentRange] ?? "");
                    long first, last, size;
                    if (response.StatusCode != HttpStatusCode.PartialContent || !range.Success ||
                        !long.TryParse(range.Groups[1].Value, out first) || !long.TryParse(range.Groups[2].Value, out last) ||
                        !long.TryParse(range.Groups[3].Value, out size) || first != start || last != end || size != total ||
                        response.ContentLength != end - start + 1 || response.Headers[HttpResponseHeader.ETag] != etag ||
                        !string.IsNullOrEmpty(response.ContentEncoding))
                        throw new InvalidDataException("Invalid range or changed representation");
                    using (Stream input = response.GetResponseStream())
                    using (FileStream output = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 81920, true))
                    {
                        output.Position = start;
                        byte[] buffer = new byte[81920];
                        long remaining = end - start + 1;
                        while (remaining > 0)
                        {
                            timeout.CancelAfter(30000);
                            int count = await input.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, remaining), timeout.Token).ConfigureAwait(false);
                            if (count == 0) throw new EndOfStreamException("Truncated range");
                            await output.WriteAsync(buffer, 0, count, cancellation.Token).ConfigureAwait(false);
                            remaining -= count;
                            progress(count);
                        }
                        timeout.CancelAfter(30000);
                        if (await input.ReadAsync(buffer, 0, 1, timeout.Token).ConfigureAwait(false) != 0)
                            throw new InvalidDataException("Range exceeded declared length");
                    }
                }
            }
        }
        catch (WebException error)
        {
            HttpWebResponse response = error.Response as HttpWebResponse;
            TimeSpan delay = TimeSpan.Zero;
            if (response != null)
            {
                if ((int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    delay = ParseRetryAfter(response.Headers[HttpResponseHeader.RetryAfter]);
                    if (delay == TimeSpan.Zero) delay = TimeSpan.FromSeconds(5);
                    retryDelays.Add(delay);
                    RegisterCooldown(uri, delay);
                }
                response.Dispose();
            }
            cancellation.Cancel();
            throw new RangeDownloadException(delay, error);
        }
        catch { cancellation.Cancel(); throw; }
        finally { Slots.Release(); }
    }

    internal static TimeSpan ParseRetryAfter(string value)
    {
        long seconds;
        if (long.TryParse(value, out seconds) && seconds >= 0)
            return TimeSpan.FromSeconds(Math.Min(seconds, 86400));
        DateTimeOffset date;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date))
            return date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero;
        return TimeSpan.Zero;
    }

    private sealed class RangeDownloadException : IOException
    {
        internal readonly TimeSpan RetryAfter;
        internal RangeDownloadException(TimeSpan delay, Exception error) : base("Range request failed", error) { RetryAfter = delay; }
    }
}
