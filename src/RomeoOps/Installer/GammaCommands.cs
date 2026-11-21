using System.Text.RegularExpressions;

namespace RomeoOps.Installer;

public static class GammaCommands
{
    public static string[] AnomalyInstallArgs(string helpText)
    {
        if (Regex.IsMatch(helpText, @"\banomaly\s+install\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new[] { "anomaly", "install", "--use-anomaly-mirror" };
        return new[] { "anomaly-install", "--use-anomaly-mirror" };
    }
}
