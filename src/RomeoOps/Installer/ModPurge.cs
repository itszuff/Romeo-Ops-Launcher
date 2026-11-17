namespace RomeoOps.Installer;

public sealed record PurgeCandidate(string Name, long Bytes, string[] UsedBy);

public static class ModPurge
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    // Only use the portable layout managed by this launcher.
    private static void CheckLayout(string gamma)
    {
        var section = "";
        foreach (var raw in File.ReadLines(Path.Combine(gamma, "ModOrganizer.ini")))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) section = line;
            if (!Names.Equals(section, "[Settings]")) continue;
            var pair = line.Split('=', 2);
            if (pair.Length != 2) continue;
            var expected = pair[0].Trim().ToLowerInvariant() switch {
                "base_directory" => gamma, "mod_directory" => Path.Combine(gamma, "mods"),
                "profiles_directory" => Path.Combine(gamma, "profiles"), _ => null
            };
            var value = pair[1].Trim().Trim('"');
            if (expected == null || value.Length == 0) continue;
            value = value.Replace("%BASE_DIR%", gamma, StringComparison.OrdinalIgnoreCase);
            if (!Names.Equals(InstallLayout.Normalize(Path.GetFullPath(value, gamma)), expected))
                throw new IOException("Cleanup supports the portable GAMMA mods and profiles folders. Check the Paths settings in MO2.");
        }
        foreach (var path in new[] { gamma, Path.Combine(gamma, "profiles"), Path.Combine(gamma, "mods") })
            if (Linked(path)) throw new IOException("Cleanup cannot use the linked folder " + path);
    }

    private static bool Linked(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    public static string[] Profiles(string gamma)
    {
        CheckLayout(gamma);
        return Directory.GetDirectories(Path.Combine(gamma, "profiles"))
            .Select(p => Path.GetFileName(p)).OrderBy(p => p, Names).ToArray();
    }

    private static Dictionary<string, List<string>> Eligible(string gamma, string selected, bool includeShared)
    {
        var disabled = new HashSet<string>(Names);
        var enabled = new Dictionary<string, List<string>>(Names);
        var profiles = Profiles(gamma);
        if (!profiles.Contains(selected, Names)) throw new IOException("The selected MO2 profile is missing.");
        foreach (var profile in profiles)
        {
            var list = Path.Combine(gamma, "profiles", profile, "modlist.txt");
            foreach (var raw in File.ReadLines(list))
            {
                var line = raw.Trim();
                if (line.Length < 2) continue;
                var name = line[1..];
                if (line[0] == '-' && Names.Equals(profile, selected)) disabled.Add(name);
                if (line[0] is '+' or '*')
                {
                    if (!enabled.TryGetValue(name, out var users)) enabled[name] = users = new();
                    users.Add(profile);
                }
            }
        }
        var result = new Dictionary<string, List<string>>(Names);
        foreach (var path in Directory.GetDirectories(Path.Combine(gamma, "mods")))
        {
            var name = Path.GetFileName(path);
            if (!disabled.Contains(name) || RomeoDownloads.IsSeparator(name) || Linked(path)) continue;
            var users = enabled.GetValueOrDefault(name) ?? new();
            if (users.Contains(selected, Names) || (!includeShared && users.Count > 0)) continue;
            result[name] = users;
        }
        return result;
    }

    private static long? Size(string directory)
    {
        long size = 0;
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
            if (entry is FileInfo file) size += file.Length;
            else if (Size(entry.FullName) is long nested) size += nested;
            else return null;
        }
        return size;
    }

    public static List<PurgeCandidate> Preview(string gamma, string profile, bool shared)
    {
        var result = new List<PurgeCandidate>();
        foreach (var (name, users) in Eligible(gamma, profile, shared).OrderBy(k => k.Key, Names))
            if (Size(Path.Combine(gamma, "mods", name)) is long bytes)
                result.Add(new(name, bytes, users.ToArray()));
        return result;
    }

    public static string Delete(string gamma, string profile, bool shared, IReadOnlyList<PurgeCandidate> reviewed)
    {
        var current = Eligible(gamma, profile, shared);
        int removed = 0;
        var issues = new List<string>();
        foreach (var mod in reviewed)
        {
            if (!current.TryGetValue(mod.Name, out var users) || users.Except(mod.UsedBy, Names).Any())
            {
                issues.Add(mod.Name + " — profile usage changed");
                continue;
            }
            var folder = Path.Combine(gamma, "mods", mod.Name);
            try
            {
                if (Linked(folder) || Size(folder) != mod.Bytes)
                {
                    issues.Add(mod.Name + " — folder changed");
                    continue;
                }
                Directory.Delete(folder, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(mod.Name + " — " + ex.Message);
            }
        }
        return $"Removed {removed} of {reviewed.Count} mod folders." + (issues.Count == 0 ? "" :
            "\n\nFolders skipped or incompletely removed\n" + string.Join("\n", issues));
    }
}
