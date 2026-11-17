using System.Net.Http;

namespace RomeoOps.Installer;

public readonly record struct TransferSnapshot(long Got, long? Total, double BytesPerSecond)
{
    public string Format()
    {
        var speed = $"{BytesPerSecond / (1024d * 1024d):0.0} MB/s";
        var eta = "ETA --";
        if (Total is > 0 && BytesPerSecond > 1)
        {
            var seconds = (Total.Value - Got) / BytesPerSecond;
            eta = seconds >= 60 ? $"ETA {(int)(seconds / 60)} MIN" : $"ETA {(int)seconds} SEC";
        }

        var totalText = Total is null ? "?" : FormatSize(Total.Value);
        return $"{FormatSize(Got)} / {totalText}  -  {speed}  -  {eta}";
    }

    public static string FormatSize(long bytes)
    {
        var gb = bytes / 1024d / 1024d / 1024d;
        if (gb >= 1)
            return $"{gb:0.00} GB";
        var mb = bytes / 1024d / 1024d;
        if (mb >= 1)
            return $"{mb:0.0} MB";
        return $"{bytes / 1024d:0.0} KB";
    }
}

public static class Downloader
{
    public static async Task DownloadAsync(
        HttpClient http,
        string url,
        string destination,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers,
        Action<TransferSnapshot>? progress)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers != null)
        {
            foreach (var header in headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var partial = destination + ".partial";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await CopyWithProgress(source, target, total, 0, progress, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(destination))
                File.Delete(destination);
            File.Move(partial, destination);
        }
        catch
        {
            if (File.Exists(partial))
            {
                try { File.Delete(partial); } catch { }
            }
            throw;
        }
    }

    internal static async Task CopyWithProgress(Stream source, Stream target, long? total, long got,
        Action<TransferSnapshot>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            got += read;
            if (clock.Elapsed - lastReport > TimeSpan.FromMilliseconds(200))
            {
                lastReport = clock.Elapsed;
                progress?.Invoke(new TransferSnapshot(got, total, got / Math.Max(clock.Elapsed.TotalSeconds, 0.001)));
            }
        }

        progress?.Invoke(new TransferSnapshot(got, total, got / Math.Max(clock.Elapsed.TotalSeconds, 0.001)));
    }
}
