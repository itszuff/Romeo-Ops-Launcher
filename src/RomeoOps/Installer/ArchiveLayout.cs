namespace RomeoOps.Installer;

public static class ArchiveLayout
{
    public static string FindBundleRoot(string extracted, string[] children)
    {
        var candidates = new List<string> { extracted };
        if (Directory.Exists(extracted))
        {
            foreach (var dir in Directory.GetDirectories(extracted))
            {
                candidates.Add(dir);
                foreach (var child in Directory.GetDirectories(dir))
                    candidates.Add(child);
            }
        }

        var matches = candidates
            .Where(path => children.All(name => Directory.Exists(Path.Combine(path, name))))
            .ToList();
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"Cannot identify a unique archive root containing [{string.Join(", ", children)}]. Found {matches.Count} candidates under {extracted}. Inspect the archive layout.");
        }

        return matches[0];
    }
}
