namespace RomeoOps.Installer;

public static class InstallationValidation
{
    public static IReadOnlyList<string> MissingEnabledMods(string gamma, string profile) =>
        File.ReadLines(profile).Where(line => line.StartsWith('+') && line.Length > 1)
            .Select(line => line[1..]).Where(name => !Directory.Exists(Path.Combine(gamma, "mods", name)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static void Validate(InstallLayout layout, bool coop)
    {
        if (!File.Exists(Path.Combine(layout.Anomaly, "AnomalyLauncher.exe")))
            throw new PipelineException("Missing Anomaly launcher.");
        if (!File.Exists(Path.Combine(layout.Gamma, "ModOrganizer.exe")))
            throw new PipelineException("Missing Mod Organizer.");
        if (coop && !Directory.Exists(Path.Combine(layout.XrModDir, "gamedata")))
            throw new PipelineException("Missing xrRazom MO2 mod.");
        if (!File.Exists(layout.Modlist))
            throw new PipelineException("ROMEO profile is missing.");

        if (coop)
        {
            var top = ModlistEditor.TopMods(layout.Modlist, 2);
            if (top.Count != 2
                || !string.Equals(top[0], "+" + Pins.XrModName, StringComparison.Ordinal)
                || !string.Equals(top[1], "+" + Pins.SpeedFixName, StringComparison.Ordinal))
                throw new PipelineException("MO2 priority verification failed: xrRazom must be first and Teepo's Speed Fix second.");
        }
        else
        {
            var top = ModlistEditor.TopMods(layout.Modlist, 1);
            if (top.Count != 1 || !string.Equals(top[0], "+" + Pins.SpeedFixName, StringComparison.Ordinal))
                throw new PipelineException("MO2 priority verification failed: Teepo's Speed Fix must be first when co-op is off.");
        }
        var missing = MissingEnabledMods(layout.Gamma, layout.Modlist);
        if (missing.Count > 0)
            throw new PipelineException("Missing enabled mod folders: " + string.Join(", ", missing));
    }
}
