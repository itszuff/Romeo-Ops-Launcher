namespace RomeoOps.Installer;

public sealed class InstallLayout
{
    private InstallLayout(string anomaly, string gamma, string root)
    {
        Anomaly = anomaly;
        Gamma = gamma;
        Root = root;
    }

    public string Anomaly { get; }
    public string Gamma { get; }
    public string Root { get; }
    public string Cache => Path.Combine(Root, "Cache");
    public string Tools => Path.Combine(Root, "_tools");
    public string Staging => Path.Combine(Root, "_staging");
    public string Logs => Path.Combine(Root, "_logs");
    public string State => Path.Combine(Root, "_state");
    public string Backups => Path.Combine(Root, "_backups");
    public string Modlist => Path.Combine(Gamma, "profiles", Pins.RomeoProfile, "modlist.txt");
    public string XrModDir => Path.Combine(Gamma, "mods", Pins.XrModName);

    public static InstallLayout Create(string anomaly, string gamma)
    {
        if (string.IsNullOrWhiteSpace(anomaly) || string.IsNullOrWhiteSpace(gamma))
            throw new ArgumentException("Anomaly and GAMMA folders are required.");

        string a;
        string g;
        try
        {
            a = Normalize(anomaly);
            g = Normalize(gamma);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("A folder path is not valid. " + ex.Message, ex);
        }

        if (string.Equals(a, g, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Anomaly and GAMMA must be different folders.");
        if (IsInside(a, g) || IsInside(g, a))
            throw new ArgumentException("Anomaly and GAMMA folders must be separate, not one inside the other.");

        var anomalyParent = Path.GetDirectoryName(a);
        var gammaParent = Path.GetDirectoryName(g);
        string root;
        if (!string.IsNullOrEmpty(anomalyParent)
            && !string.IsNullOrEmpty(gammaParent)
            && string.Equals(anomalyParent, gammaParent, StringComparison.OrdinalIgnoreCase))
        {
            root = anomalyParent;
        }
        else
        {
            root = !string.IsNullOrEmpty(gammaParent) ? gammaParent : g;
        }

        return new InstallLayout(a, g, root);
    }

    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        var root = Path.GetPathRoot(full) ?? "";
        if (full.Length > root.Length)
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    private static bool IsInside(string parent, string child)
    {
        var prefix = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = child.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
