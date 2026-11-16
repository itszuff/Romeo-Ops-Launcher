namespace RomeoOps.Installer;

public sealed record ExistingStack(InstallLayout Layout, string Profile, bool CoOp)
{
    public static bool HasInstallation(InstallLayout layout) =>
        File.Exists(Path.Combine(layout.Root,"AnomalyLauncher.exe")) ||
        File.Exists(Path.Combine(layout.Root,"ModOrganizer.exe")) ||
        (File.Exists(Path.Combine(layout.Anomaly,"AnomalyLauncher.exe")) && File.Exists(Path.Combine(layout.Gamma,"ModOrganizer.exe"))) ||
        Detect(layout.Root,allowIncomplete:true) != null;

    public string RomeoArchive(string selected)
    {
        var retained = Path.Combine(Layout.Gamma, "downloads", Pins.RomeoArchiveFile);
        if (File.Exists(retained)) return retained;
        var cached = Path.Combine(Layout.Cache, Pins.RomeoArchiveFile);
        if (File.Exists(cached)) return cached;
        return File.Exists(selected) ? selected : "";
    }

    public static ExistingStack? Detect(string root,bool allowIncomplete = false)
    {
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) return null;
        try
        {
            var children = Directory.GetDirectories(root);
            string? Component(string preferred, string marker)
            {
                var usual = Path.Combine(root, preferred);
                if (File.Exists(Path.Combine(usual, marker))) return usual;
                var matches = children.Where(d => File.Exists(Path.Combine(d, marker))).Take(2).ToArray();
                return matches.Length == 1 ? matches[0] : null;
            }
            var anomaly = Component("Anomaly", "AnomalyLauncher.exe");
            var gamma = Component("GAMMA", "ModOrganizer.exe");
            if (anomaly == null || gamma == null) return null;
            var layout = InstallLayout.Create(anomaly, gamma);
            var profile = File.Exists(layout.Modlist) ? layout.Modlist : null;
            if (profile == null)
            {
                var source = Directory.Exists(Path.Combine(gamma,"profiles")) ? Mo2Profile.FindSource(gamma) : null;
                if (source == null && allowIncomplete)
                    return new ExistingStack(layout,layout.Modlist,Directory.Exists(Path.Combine(layout.XrModDir,"gamedata")));
                if (source == null) return null;
                profile = Path.Combine(source, "modlist.txt");
            }
            var coop = File.ReadLines(profile).Any(line => line.Trim().Equals("+" + Pins.XrModName, StringComparison.OrdinalIgnoreCase));
            return new ExistingStack(layout, profile, coop);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PipelineException or ArgumentException)
        {
            return null;
        }
    }
}
