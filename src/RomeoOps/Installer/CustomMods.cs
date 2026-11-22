namespace RomeoOps.Installer;

/// <summary>Preserves extra ROMEO-profile entries across an in-place repair.</summary>
public static class CustomMods
{
    public static string Capture(InstallLayout layout, Guid runId)
    {
        var folder = Path.Combine(layout.Backups, "custom-mods-" + runId.ToString("N"));
        var snapshot = Path.Combine(folder, "modlist.txt");
        if (Directory.Exists(folder)) return snapshot;

        // Publish the snapshot directory atomically, including an empty list when no
        // profile exists. Resuming must never capture a partially repaired profile.
        var staging = folder + "." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            string? profile = File.Exists(layout.Modlist) ? layout.Modlist : null;
            if (profile == null && Directory.Exists(Path.Combine(layout.Gamma, "profiles")))
            {
                var source = Mo2Profile.FindSource(layout.Gamma);
                if (source != null) profile = Path.Combine(source, "modlist.txt");
            }
            var target = Path.Combine(staging, "modlist.txt");
            if (profile != null) File.Copy(profile, target);
            else File.WriteAllText(target, "");
            Directory.Move(staging, folder);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                File.Delete(Path.Combine(staging, "modlist.txt"));
                Directory.Delete(staging);
            }
        }
        return snapshot;
    }

    public static void Restore(InstallLayout layout, string snapshot, Action<string> warning)
    {
        var repaired = File.ReadAllLines(layout.Modlist).ToList();
        var saved = File.ReadAllLines(snapshot);
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < repaired.Count; index++)
            if (Name(repaired[index]) is string name) positions.TryAdd(name, index);

        var managed = CoopAddon.Catalog.Select(addon => addon.Folder)
            .Concat(Pins.PriorityMods).Append(CoopAddon.LauncherSeparator)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(CoopAddon.Ledger(layout)))
            foreach (var line in File.ReadLines(CoopAddon.Ledger(layout)))
                managed.Add(line.TrimStart('+', '-'));

        // The list is stored in reverse MO2 display order. Required launcher mods
        // must stay first; extras retain order and sit before their next base entry.
        var minimum = repaired.TakeWhile(line => Name(line) == null).Count();
        foreach (var name in Pins.PriorityMods)
            if (positions.TryGetValue(name, out var index) && repaired[index].StartsWith('+'))
                minimum = Math.Max(minimum, index + 1);

        var additions = new Dictionary<int, List<string>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < saved.Length; index++)
        {
            var name = Name(saved[index]);
            if (name == null || !seen.Add(name) || positions.ContainsKey(name) || managed.Contains(name)) continue;
            var entry = saved[index];
            var folder = Path.Combine(layout.Gamma, "mods", name);
            if (!Directory.Exists(folder))
            {
                if (RomeoDownloads.IsSeparator(name)) Directory.CreateDirectory(folder);
                else
                {
                    entry = "-" + name;
                    warning("Custom mod kept disabled because its folder is missing: " + name);
                }
            }

            var position = repaired.Count;
            for (var next = index + 1; next < saved.Length; next++)
                if (Name(saved[next]) is string anchor && positions.TryGetValue(anchor, out var found))
                {
                    position = found;
                    break;
                }
            // Base entries can change order between releases. Do not let that reverse
            // the user's extras, or move an extra above xrRazom / Teepo's Speed Fix.
            position = Math.Max(position, minimum);
            minimum = position;
            if (!additions.TryGetValue(position, out var group)) additions[position] = group = [];
            group.Add(entry);
        }

        if (additions.Count == 0) return;
        var merged = new List<string>();
        for (var index = 0; index <= repaired.Count; index++)
        {
            if (additions.TryGetValue(index, out var group)) merged.AddRange(group);
            if (index < repaired.Count) merged.Add(repaired[index]);
        }
        var temporary = layout.Modlist + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllLines(temporary, merged, ModlistEditor.EncodingFor(layout.Modlist));
            File.Move(temporary, layout.Modlist, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private static string? Name(string line)
    {
        if (line.Length < 2 || line[0] is not ('+' or '-' or '*')) return null;
        var name = line[1..];
        return name is not ("." or "..") && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && name == Path.GetFileName(name) ? name : null;
    }
}
