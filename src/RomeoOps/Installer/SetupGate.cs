using RomeoOps.Installer;

namespace RomeoOps;

public static class SetupGate
{
    public static string Fingerprint(AppSettings settings)
    {
        string anomaly;
        string gamma;
        try
        {
            var layout = InstallLayout.Create(AnomalyFolder(settings), GammaFolder(settings));
            anomaly = layout.Anomaly;
            gamma = layout.Gamma;
        }
        catch (ArgumentException)
        {
            anomaly = AnomalyFolder(settings);
            gamma = GammaFolder(settings);
        }

        return string.Join(
            "\n",
            anomaly,
            gamma,
            settings.AnomalySource,
            settings.GammaSource,
            FullOrTrim(settings.RomeoArchive),
            settings.CoOp ? "coop" : "solo",
            settings.CoOp && settings.XrSource == "Local" ? "local" : settings.CoOp ? "download" : "off",
            settings.CoOp && settings.XrSource == "Local" ? FullOrTrim(settings.XrArchive) : "");
    }

    public static List<string> Validate(AppSettings settings)
    {
        var problems = new List<string>();
        InstallLayout? layout = null;
        try
        {
            layout = InstallLayout.Create(AnomalyFolder(settings), GammaFolder(settings));
        }
        catch (ArgumentException ex)
        {
            problems.Add(ex.Message);
        }

        foreach (var (name, mode, zip) in new[] { ("Anomaly", settings.AnomalySource, settings.AnomalyZip), ("GAMMA", settings.GammaSource, settings.GammaZip) })
        {
            if (mode == "Local" && (!File.Exists(zip) || !(zip.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || zip.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))))
                problems.Add($"Choose an existing {name} ZIP or 7z archive.");
            if (mode == "Local" && string.IsNullOrWhiteSpace(settings.InstallFolder))
                problems.Add("Choose the installation folder for " + name + ".");
        }

        if (layout != null && settings.AnomalySource == "Installed"
            && !File.Exists(Path.Combine(layout.Anomaly, "AnomalyLauncher.exe")))
            problems.Add("Use Installed is selected, but AnomalyLauncher.exe is not in the Anomaly folder.");
        if (layout != null && settings.GammaSource == "Installed"
            && !File.Exists(Path.Combine(layout.Gamma, "ModOrganizer.exe")))
            problems.Add("Use Installed is selected, but ModOrganizer.exe is not in the GAMMA folder.");

        if (NeedsInstallFolder(settings) && string.IsNullOrWhiteSpace(settings.InstallFolder))
            problems.Add("Choose the installation folder.");

        if (string.IsNullOrWhiteSpace(settings.RomeoArchive))
            problems.Add("Choose the ROMEO .7z archive.");
        else if (!File.Exists(settings.RomeoArchive))
            problems.Add("The ROMEO archive was not found.");
        else if (!settings.RomeoArchive.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            problems.Add("The ROMEO archive must be a .7z file.");

        if (settings.CoOp && settings.XrSource == "Local")
        {
            if (string.IsNullOrWhiteSpace(settings.XrArchive))
                problems.Add("Choose the xrRazom zip, or switch xrRazom to Download.");
            else if (!File.Exists(settings.XrArchive))
                problems.Add("The xrRazom zip was not found.");
        }

        return problems;
    }

    public static bool LooksInstalled(AppSettings settings)
    {
        try
        {
            var layout = InstallLayout.Create(AnomalyFolder(settings), GammaFolder(settings));
            var xrReady = !settings.CoOp || Directory.Exists(Path.Combine(layout.XrModDir, "gamedata"));
            return File.Exists(Path.Combine(layout.Anomaly, "AnomalyLauncher.exe"))
                && File.Exists(Path.Combine(layout.Gamma, "ModOrganizer.exe"))
                && File.Exists(layout.Modlist)
                && xrReady;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool SettingsReady(AppSettings settings) =>
        settings.SetupComplete
        && string.Equals(settings.SetupFingerprint, Fingerprint(settings), StringComparison.Ordinal);

    public static bool InstallUnlocked(AppSettings settings) =>
        SettingsReady(settings)
        && !string.Equals(settings.InstalledFingerprint, Fingerprint(settings), StringComparison.Ordinal);

    public static bool VerifyUnlocked(AppSettings settings) =>
        SettingsReady(settings)
        && settings.InstalledFingerprint.Length > 0
        && string.Equals(settings.InstalledFingerprint, Fingerprint(settings), StringComparison.Ordinal);

    public static string AnomalyFolder(AppSettings settings) =>
        settings.AnomalySource == "Local" ? Path.Combine(settings.InstallFolder, "Anomaly") : GameFolder(settings.AnomalySource, "Installed", settings.AnomalyPath, settings.InstallFolder, "Anomaly");

    public static string GammaFolder(AppSettings settings) =>
        settings.GammaSource == "Local" ? Path.Combine(settings.InstallFolder, "GAMMA") : GameFolder(settings.GammaSource, "Installed", settings.GammaPath, settings.InstallFolder, "GAMMA");

    private static bool NeedsInstallFolder(AppSettings settings) =>
        (settings.AnomalySource != "Installed" && string.IsNullOrWhiteSpace(settings.AnomalyPath))
        || (settings.GammaSource != "Installed" && string.IsNullOrWhiteSpace(settings.GammaPath));

    private static string GameFolder(string source, string localToken, string localPath, string installFolder, string child)
    {
        var stored = localPath.Trim();
        var root = installFolder.Trim();
        if (source == localToken || root.Length == 0)
            return stored;

        var destination = Path.Combine(root, child);
        if (stored.Length == 0)
            return destination;

        try
        {
            var fullStored = Path.GetFullPath(stored);
            var fullRoot = Path.GetFullPath(root);
            if (string.Equals(Path.GetDirectoryName(fullStored), fullRoot, StringComparison.OrdinalIgnoreCase))
                return fullStored;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        return destination;
    }

    private static string FullOrTrim(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length == 0)
            return "";
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return trimmed;
        }
    }
}
