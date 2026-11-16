namespace RomeoOps.Installer;

public static class FolderDetector
{
    private static readonly string[] Roots =
    {
        @"C:\Games",
        @"D:\Games",
        @"E:\Games",
        @"C:\Stalker",
        @"D:\Stalker"
    };

    public static string? FindAnomaly(string? current) => Find(current, "AnomalyLauncher.exe", "Anomaly");

    public static string? FindGamma(string? current) => Find(current, "ModOrganizer.exe", "GAMMA");

    public static string? Find(string? current, string exeName, string preferredFolder)
    {
        if (!string.IsNullOrWhiteSpace(current))
        {
            try
            {
                var direct = Path.Combine(current.Trim(), exeName);
                if (File.Exists(direct))
                    return InstallLayout.Normalize(current);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
            }
        }

        foreach (var root in Roots)
        {
            var preferred = Path.Combine(root, preferredFolder, exeName);
            if (File.Exists(preferred))
                return Path.GetDirectoryName(preferred);

            if (!Directory.Exists(root))
                continue;

            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    if (File.Exists(Path.Combine(dir, exeName)))
                        return dir;
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }
}
