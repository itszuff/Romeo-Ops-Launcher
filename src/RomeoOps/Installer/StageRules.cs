namespace RomeoOps.Installer;

public static class StageRules
{
    public static bool AnomalyComplete(string stateDir, string anomalyPath)
    {
        return File.Exists(Path.Combine(stateDir, "01-anomaly.done"))
            && File.Exists(Path.Combine(anomalyPath, "AnomalyLauncher.exe"));
    }

    public static bool GammaComplete(string stateDir, string gammaPath)
    {
        return File.Exists(Path.Combine(stateDir, "02-gamma.done"))
            && File.Exists(Path.Combine(gammaPath, "ModOrganizer.exe"));
    }

    public static bool RomeoComplete(string stateDir, bool reapply)
    {
        return !reapply && File.Exists(Path.Combine(stateDir, "03-romeo.done"));
    }

    public static bool XrComplete(string stateDir, string xrModDir, bool reapply)
    {
        return !reapply
            && File.Exists(Path.Combine(stateDir, "04-xrrazom.done"))
            && Directory.Exists(Path.Combine(xrModDir, "gamedata"));
    }
}
