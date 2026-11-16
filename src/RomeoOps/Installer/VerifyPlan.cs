namespace RomeoOps.Installer;

public sealed class VerifyPlan
{
    public VerifyPlan(bool checkMd5, bool forceGitDownload, bool forceZipExtraction, bool updateLargeFiles)
    {
        CheckMd5 = checkMd5;
        ForceGitDownload = forceGitDownload;
        ForceZipExtraction = forceZipExtraction;
        UpdateLargeFiles = updateLargeFiles;
    }

    public bool CheckMd5 { get; }
    public bool ForceGitDownload { get; }
    public bool ForceZipExtraction { get; }
    public bool UpdateLargeFiles { get; }

    public bool RunsInstaller => CheckMd5 || ForceGitDownload || ForceZipExtraction || UpdateLargeFiles;

    public List<string> FullInstallArgs()
    {
        var args = new List<string> { "full-install", "--enable-long-paths", "--json" };
        if (!ForceGitDownload)
            args.Add("--skip-github-downloads");
        if (!ForceZipExtraction)
            args.Add("--skip-extract-on-hash-match");
        return args;
    }

    public List<string> ReposToSkip()
    {
        var skip = new List<string>();
        if (!ForceGitDownload)
        {
            skip.Add("gamma_setup | ");
            skip.Add("Stalker_GAMMA | ");
            skip.Add("teivaz_anomaly_gunslinger | ");
        }

        if (!UpdateLargeFiles)
            skip.Add("gamma_large_files_v2 | ");
        return skip;
    }
}

public static class GammaResume
{
    public static (int Complete, int Total) ReadProgress(string gammaDir)
    {
        var path = Path.Combine(gammaDir, "resume.txt");
        if (!File.Exists(path))
            return (0, 0);

        var complete = 0;
        var total = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var tab = line.IndexOf('\t');
            if (tab <= 0)
                continue;
            total++;
            var status = line[..tab];
            if (status.Equals("Complete", StringComparison.OrdinalIgnoreCase))
                complete++;
        }

        return (complete, total);
    }
}

public static class VerifyResume
{
    public static bool Prepare(string gammaDir, IReadOnlyList<string> skipKeys)
    {
        Directory.CreateDirectory(gammaDir);
        var path = Path.Combine(gammaDir, "resume.txt");
        if (skipKeys.Count == 0)
            return File.Exists(path) && ReadCommand(path) == "full-install";

        var statuses = new Dictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(path) && ReadCommand(path) == "full-install")
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#')
                    continue;
                var split = line.Split('\t', 2);
                if (split.Length == 2)
                    statuses[split[1]] = split[0];
            }
        }

        foreach (var key in skipKeys)
            statuses[key] = "Complete";

        var lines = new List<string>
        {
            "# stalker-gamma resume file. Run the same command with --resume to continue.",
            "# command: full-install",
            "# status\trecord"
        };
        foreach (var pair in statuses.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            lines.Add(pair.Value + "\t" + pair.Key);
        File.WriteAllLines(path, lines);
        return true;
    }

    private static string? ReadCommand(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            const string prefix = "# command: ";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line[prefix.Length..].Trim();
        }

        return null;
    }
}
