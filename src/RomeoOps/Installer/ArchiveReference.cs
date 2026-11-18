namespace RomeoOps.Installer;

/// <summary>Records the exact archives used by a successful ROMEO stack install.</summary>
public static class ArchiveReference
{
    public static void Record(string stateDirectory, string component, string archive)
    {
        Directory.CreateDirectory(stateDirectory);
        var path = Path.Combine(stateDirectory, component + ".sha256");
        var digest = FileHasher.Compute(archive, "SHA256");
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Equals(digest, StringComparison.OrdinalIgnoreCase)) return;
        }
        File.WriteAllText(path, digest + Environment.NewLine);
    }

    public static string Check(string stateDirectory, string component, string archive)
    {
        if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive))
            return $"{component}: source archive is unavailable; choose it in Settings to verify.";
        var reference = Path.Combine(stateDirectory, component + ".sha256");
        if (!File.Exists(reference))
            return $"{component}: no clean-install archive reference exists yet.";
        var expected = File.ReadAllText(reference).Trim();
        var actual = FileHasher.Compute(archive, "SHA256");
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase)
            ? $"{component}: source archive matches the install reference (SHA-256)."
            : $"{component}: source archive differs from the install reference; redownload the matching release.";
    }
}
