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

    public static string[] AnomalyCheckArgs(string helpText)
    {
        if (Regex.IsMatch(helpText, @"\banomaly\s+check\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new[] { "anomaly", "check", "--json" };
        if (Regex.IsMatch(helpText, @"\bcheck-anomaly\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new[] { "check-anomaly", "--json" };
        return new[] { "anomaly", "check", "--json" };
    }
}
