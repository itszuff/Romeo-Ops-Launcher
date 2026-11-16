namespace RomeoOps.Installer;

public static class ToolLocator
{
    public static string? FindSevenZip(string? cliHome)
    {
        if (!string.IsNullOrEmpty(cliHome))
        {
            var bundled = FindNamed(cliHome, "7zz.exe", "7z.exe");
            if (bundled != null)
                return bundled;
        }

        foreach (var installed in new[] { @"C:\Program Files\7-Zip\7z.exe", @"C:\Program Files (x86)\7-Zip\7z.exe" })
        {
            if (File.Exists(installed))
                return installed;
        }

        return FindOnPath("7zz.exe") ?? FindOnPath("7z.exe");
    }

    public static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }

    public static string? FindNamed(string root, params string[] names)
    {
        if (!Directory.Exists(root))
            return null;
        try
        {
            return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(path => names.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => path.Length)
                .FirstOrDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
