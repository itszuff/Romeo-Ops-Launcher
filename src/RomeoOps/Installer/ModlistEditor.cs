using System.Text;
using System.Text.RegularExpressions;

namespace RomeoOps.Installer;

public static class ModlistEditor
{
    public static int CountEnabled(string path)
    {
        return File.ReadAllLines(path).Count(line => line.StartsWith('+'));
    }

    public static string? FirstMod(string path)
    {
        var encoding = EncodingFor(path);
        return File.ReadAllLines(path, encoding).FirstOrDefault(IsModLine);
    }

    // MO2 modlist.txt is reverse display order: the first mod line is the highest priority.
    public static void SetHighestPriority(string path, string name) => SetPriorityOrder(path, new[] { name });

    public static void SetPriorityOrder(string path, IReadOnlyList<string> names)
    {
        var encoding = EncodingFor(path);
        var lines = File.ReadAllLines(path, encoding);
        var patterns = names
            .Select(name => new Regex("^[+\\-*]" + Regex.Escape(name) + "$", RegexOptions.CultureInvariant))
            .ToArray();
        var remaining = lines.Where(line => !patterns.Any(pattern => pattern.IsMatch(line))).ToList();

        var headers = new List<string>();
        var index = 0;
        while (index < remaining.Count && remaining[index].StartsWith('#'))
        {
            headers.Add(remaining[index]);
            index++;
        }

        var updated = new List<string>(headers);
        updated.AddRange(names.Select(name => "+" + name));
        if (index < remaining.Count)
            updated.AddRange(remaining.Skip(index));

        File.WriteAllLines(path, updated, encoding);
        var actual = File.ReadAllLines(path, encoding).Where(IsModLine).Take(names.Count).ToArray();
        for (var n = 0; n < names.Count; n++)
        {
            var expected = "+" + names[n];
            if (n >= actual.Length || !string.Equals(actual[n], expected, StringComparison.Ordinal))
                throw new InvalidOperationException("Priority verification failed for " + names[n]);
        }
    }

    public static IReadOnlyList<string> TopMods(string path, int count)
    {
        var encoding = EncodingFor(path);
        return File.ReadAllLines(path, encoding).Where(IsModLine).Take(count).ToArray();
    }

    private static bool IsModLine(string line)
    {
        return line.Length > 0 && (line[0] == '+' || line[0] == '-' || line[0] == '*');
    }

    internal static UTF8Encoding EncodingFor(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var withBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom);
    }
}

