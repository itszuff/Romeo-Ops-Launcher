using System.Text;
using System.Text.RegularExpressions;

namespace RomeoOps.Installer;

public static class Mo2Profile
{
    // Local GAMMA archives can contain MO2 without its machine-specific configuration.
    public static bool EnsureConfiguration(InstallLayout layout)
    {
        var ini = Path.Combine(layout.Gamma, "ModOrganizer.ini");
        if (File.Exists(ini)) return false;
        if (!File.Exists(Path.Combine(layout.Gamma, "ModOrganizer.exe")))
            throw new PipelineException("Cannot configure Mod Organizer: ModOrganizer.exe is missing.");

        var anomaly = Path.GetFullPath(layout.Anomaly).Replace('\\', '/').TrimEnd('/');
        // Quote QSettings strings so commas and semicolons in folder names stay literal.
        static string Value(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        var entries = new List<(string Title, string Binary, string Directory)>
        {
            ("Anomaly Launcher", anomaly + "/AnomalyLauncher.exe", anomaly)
        };
        foreach (var renderer in new[] { "DX11AVX", "DX11", "DX10AVX", "DX10", "DX9AVX", "DX9", "DX8AVX", "DX8" })
            entries.Add(("Anomaly (" + renderer.Replace("AVX", "-AVX") + ")",
                anomaly + "/bin/Anomaly" + renderer + ".exe", anomaly + "/bin"));

        var text = new StringBuilder();
        text.AppendLine("[General]");
        text.AppendLine("gameName=STALKER Anomaly");
        text.AppendLine("gamePath=" + Value(anomaly));
        text.AppendLine("selected_profile=@ByteArray(" + Pins.RomeoProfile + ")");
        text.AppendLine("first_start=false");
        text.AppendLine();
        text.AppendLine("[customExecutables]");
        text.AppendLine("size=" + entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var prefix = (i + 1) + "\\";
            var entry = entries[i];
            text.AppendLine(prefix + "title=" + Value(entry.Title));
            text.AppendLine(prefix + "binary=" + Value(entry.Binary));
            text.AppendLine(prefix + "workingDirectory=" + Value(entry.Directory));
            text.AppendLine(prefix + "arguments=");
            text.AppendLine(prefix + "steamAppID=480");
            text.AppendLine(prefix + "hide=false");
            text.AppendLine(prefix + "ownicon=true");
            text.AppendLine(prefix + "toolbar=false");
        }
        using var stream = new FileStream(ini, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text.ToString());
        return true;
    }

    public static string? FindSource(string gamma)
    {
        var profiles = Path.Combine(gamma, "profiles");
        if (!Directory.Exists(profiles))
            throw new PipelineException(@"GAMMA\profiles is missing.");

        var matches = Directory.GetDirectories(profiles)
            .Where(dir =>
            {
                var name = Path.GetFileName(dir);
                return name.Contains("ROMEO", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, Pins.RomeoProfile, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(dir, "modlist.txt"));
            })
            .ToList();

        if (matches.Count == 1)
            return matches[0];
        if (matches.Count == 0)
            return null;

        var preferred = matches
            .Where(dir => Path.GetFileName(dir).StartsWith("CLIKR", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (preferred.Count == 1)
            return preferred[0];
        throw new PipelineException("Several ROMEO profiles exist, cannot choose a source safely: "
            + string.Join(", ", matches.Select(Path.GetFileName)));
    }

    public static void SetSelectedProfile(string iniPath, string backupDir)
    {
        var original = ReadText(iniPath);
        var selected = "selected_profile=@ByteArray(" + Pins.RomeoProfile + ")";
        if (!Regex.IsMatch(original, @"(?m)^selected_profile="))
            throw new PipelineException("Cannot find selected_profile in ModOrganizer.ini.");

        var updated = Regex.Replace(original, @"(?m)^selected_profile=.*$", selected);
        WriteIfChanged(iniPath, original, updated, backupDir, "ModOrganizer-Profile");
        if (!Regex.IsMatch(ReadText(iniPath), "(?m)^" + Regex.Escape(selected) + "$"))
            throw new PipelineException("Failed to set the selected profile in " + iniPath);
    }

    public static int SetSteamAppId(string iniPath, string backupDir)
    {
        var original = ReadText(iniPath);
        var section = Regex.Match(original, @"(?ms)^\[customExecutables\]\r?\n(?<content>.*?)(?=^\[|\z)");
        if (!section.Success)
            throw new PipelineException("No [customExecutables] section in ModOrganizer.ini.");

        var body = section.Groups["content"].Value;
        var ids = Regex.Matches(body, @"(?m)^(?<id>\d+)\\binary=")
            .Select(match => match.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
            throw new PipelineException("No custom executable binary entries in ModOrganizer.ini.");

        var lineEnding = original.Contains("\r\n") ? "\r\n" : "\n";
        foreach (var id in ids)
        {
            var assignment = id + @"\steamAppID=480";
            var pattern = "(?m)^" + Regex.Escape(id + @"\steamAppID") + "=.*$";
            if (Regex.IsMatch(body, pattern))
            {
                body = Regex.Replace(body, pattern, assignment);
                continue;
            }

            var binary = Regex.Match(body, "(?m)^" + Regex.Escape(id + @"\binary") + "=.*$");
            if (!binary.Success)
                throw new PipelineException("No executable for entry " + id);
            body = body.Insert(binary.Index + binary.Length, lineEnding + assignment);
        }

        var content = section.Groups["content"];
        var changed = original.Remove(content.Index, content.Length).Insert(content.Index, body);
        WriteIfChanged(iniPath, original, changed, backupDir, "ModOrganizer-SteamAppID");

        var verify = ReadText(iniPath);
        foreach (var id in ids)
        {
            if (!Regex.IsMatch(verify, "(?m)^" + Regex.Escape(id + @"\steamAppID=480") + "$"))
                throw new PipelineException("Steam AppID 480 was not saved for executable " + id);
        }

        return ids.Count;
    }

    private static string ReadText(string path)
    {
        if (!File.Exists(path))
            throw new PipelineException("ModOrganizer.ini is missing.");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static void WriteIfChanged(string path, string original, string updated, string backupDir, string tag)
    {
        if (string.Equals(original, updated, StringComparison.Ordinal))
            return;
        Directory.CreateDirectory(backupDir);
        var backup = Path.Combine(backupDir, tag + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak");
        File.Copy(path, backup, overwrite: true);
        var bytes = File.ReadAllBytes(path);
        var withBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        File.WriteAllText(path, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));
    }
}
