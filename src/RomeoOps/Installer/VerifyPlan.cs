namespace RomeoOps.Installer;

public static class VerifyPlan
{
    // Increment when an older checkpoint cannot safely resume this repair sequence.
    public const int CurrentVersion = 1;

    public static PipelineRequest FromSettings(AppSettings settings, InstallLayout layout) => new()
    {
        Mode = PipelineMode.Verify,
        RepairVersion = CurrentVersion,
        AnomalyPath = layout.Anomaly,
        GammaPath = layout.Gamma,
        RomeoArchive = ExistingStack.ResolveRomeoArchive(layout, settings.RomeoArchive),
        InstallCoOp = settings.CoOp,
        XrArchive = settings.CoOp && settings.XrSource == "Local" ? settings.XrArchive : null,
        CreateSnapshots = false,
        PreserveUser = settings.PreserveUser,
        PreserveMcm = settings.PreserveMcm
    };

    public static List<string> FullInstallArgs(bool resume)
    {
        var args = new List<string> { "full-install", "--use-anomaly-mirror", "--enable-long-paths", "--json" };
        if (resume) args.Add("--resume");
        return args;
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
    public static bool Prepare(string gammaDir, bool fresh)
    {
        var path = Path.Combine(gammaDir, "resume.txt");
        if (fresh)
        {
            // Only the CLI owns per-mod progress. Never synthesize completed records.
            File.Delete(path);
            return false;
        }
        return File.Exists(path) && File.ReadLines(path).Any(line => line.Trim() == "# command: full-install");
    }
}
