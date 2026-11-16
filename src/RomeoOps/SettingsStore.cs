using System.Text.Json;

namespace RomeoOps;

public sealed class AppSettings
{
    public string InstallFolder { get; set; } = "";
    public string AnomalyZip { get; set; } = "";
    public string AnomalyPath { get; set; } = "";
    public string GammaZip { get; set; } = "";
    public string GammaPath { get; set; } = "";
    public string RomeoArchive { get; set; } = "";
    public string XrArchive { get; set; } = "";
    public string AnomalySource { get; set; } = "Download";
    public string GammaSource { get; set; } = "Download";
    public string XrSource { get; set; } = "Download";
    public bool CoOp { get; set; } = true;
    public bool SetupComplete { get; set; }
    public string SetupFingerprint { get; set; } = "";
    public string InstalledFingerprint { get; set; } = "";
    public bool CreateSnapshots { get; set; } = true;
    public bool PreserveUser { get; set; } = true;
    public bool PreserveMcm { get; set; } = true;
    public bool VerifyCheckMd5 { get; set; }
    public bool VerifyForceGit { get; set; }
    public bool VerifyForceExtract { get; set; }
    public bool VerifyLargeFiles { get; set; }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RomeoOpsV2",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return Fresh();
            var text = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(text, JsonOptions) ?? Fresh();
            loaded.AnomalyPath ??= "";
            loaded.GammaPath ??= "";
            loaded.InstallFolder ??= "";
            if (text.IndexOf("\"installFolder\"", StringComparison.OrdinalIgnoreCase) < 0)
                loaded.InstallFolder = InferInstallFolder(loaded);
            loaded.RomeoArchive ??= "";
            loaded.XrArchive ??= "";
            loaded.SetupFingerprint ??= "";
            loaded.InstalledFingerprint ??= "";
            loaded.AnomalySource = loaded.AnomalySource is "Local" or "Installed" ? loaded.AnomalySource : "Download";
            loaded.GammaSource = loaded.GammaSource is "Local" or "Installed" ? loaded.GammaSource : "Download";
            if (text.IndexOf("\"xrSource\"", StringComparison.OrdinalIgnoreCase) < 0)
                loaded.XrSource = string.IsNullOrWhiteSpace(loaded.XrArchive) ? "Download" : "Local";
            else
                loaded.XrSource = loaded.XrSource == "Local" ? "Local" : "Download";
            if (text.IndexOf("\"coOp\"", StringComparison.OrdinalIgnoreCase) < 0)
                loaded.CoOp = true;
            if (text.IndexOf("\"verifyCheckMd5\"", StringComparison.OrdinalIgnoreCase) < 0)
                loaded.VerifyCheckMd5 = true;
            if (text.IndexOf("\"verifyLargeFiles\"", StringComparison.OrdinalIgnoreCase) < 0)
                loaded.VerifyLargeFiles = true;
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    private static string InferInstallFolder(AppSettings settings)
    {
        string? Parent(string path, string child)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            try
            {
                var full = Path.GetFullPath(path.Trim());
                if (!string.Equals(Path.GetFileName(full), child, StringComparison.OrdinalIgnoreCase))
                    return null;
                return Path.GetDirectoryName(full) ?? "";
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        var anomaly = settings.AnomalySource != "Installed" ? Parent(settings.AnomalyPath, "Anomaly") : null;
        var gamma = settings.GammaSource != "Installed" ? Parent(settings.GammaPath, "GAMMA") : null;
        if (!string.IsNullOrEmpty(anomaly) && !string.IsNullOrEmpty(gamma)
            && string.Equals(anomaly, gamma, StringComparison.OrdinalIgnoreCase))
            return anomaly;
        if (!string.IsNullOrEmpty(anomaly))
            return anomaly;
        if (!string.IsNullOrEmpty(gamma))
            return gamma;
        return "";
    }

    private static AppSettings Fresh() => new()
    {
        InstallFolder = @"C:\StalkerRomeo",
        CoOp = false,
        VerifyCheckMd5 = true,
        VerifyLargeFiles = true
    };

    public static void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}


