namespace RomeoOps.Installer;

// Reuse checks only within one operation, and only while both files are unchanged.
public sealed class ArchiveRetentionSession
{
    private readonly Dictionary<(string Source, string Target), (long, long, long, long)> verified = new();

    public async Task<bool> RetainAsync(string source, string downloads, string name,
        CancellationToken token, Action<string, TransferSnapshot>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        var key = (Path.GetFullPath(source).ToUpperInvariant(), Path.GetFullPath(Path.Combine(downloads, name)).ToUpperInvariant());
        var before = Stamp(source, key.Item2);
        if (before != null && verified.TryGetValue(key, out var previous) && previous == before.Value)
            return false;
        var copied = await RomeoDownloads.RetainAsync(source, downloads, name, token, progress).ConfigureAwait(false);
        var after = Stamp(source, key.Item2);
        if (after != null && (before == after || (copied && before == null)))
            verified[key] = after.Value;
        return copied;
    }

    private static (long, long, long, long)? Stamp(string source, string target)
    {
        var a = new FileInfo(source);
        var b = new FileInfo(target);
        return a.Exists && b.Exists ? (a.Length, a.LastWriteTimeUtc.Ticks, b.Length, b.LastWriteTimeUtc.Ticks) : null;
    }
}
