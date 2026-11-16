using System.IO.Compression;

namespace RomeoOps.Installer;

public static class LocalZip
{
    // Extract into a private staging directory. Only the identified game tree is merged.
    public static async Task<string> UnpackAsync(string archive, string staging, string marker, CancellationToken token)
    {
        using var zip = ZipFile.OpenRead(archive);
        var candidates = zip.Entries.Where(e => Path.GetFileName(e.FullName).Equals(marker, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count != 1)
            throw new InvalidDataException($"{Path.GetFileName(archive)} must contain one game folder with {marker}. Found {candidates.Count}.");
        var prefix = candidates[0].FullName[..^marker.Length];
        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = entry.FullName[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            if (relative.Length == 0) continue;
            var parts = relative.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
            if (parts.Any(p => p.Length == 0 || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new InvalidDataException("Invalid ZIP entry: " + entry.FullName);
            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid ZIP entry: " + entry.FullName);
            if (relative.EndsWith(Path.DirectorySeparatorChar)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, token).ConfigureAwait(false);
        }
        return staging;
    }
}
