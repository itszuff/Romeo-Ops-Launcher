using System.Text;
using System.Text.RegularExpressions;

namespace RomeoOps.Installer;

public static class Mo2Profile
{
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
