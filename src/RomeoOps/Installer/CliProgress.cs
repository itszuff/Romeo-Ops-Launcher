using System.Text.Json;
using System.Text.RegularExpressions;

namespace RomeoOps.Installer;

public readonly record struct CliProgressUpdate(bool IsProgress, int Percent, string? Detail, string? LogText, LogKind Kind);

/// <summary>
/// Turns stalker-gamma-cli output into one forward-only percent.
/// Parallel mods each report their own file percent, so that number is only a fraction of the current mod.
/// </summary>
public sealed class CliProgress
{
    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex CounterRegex = new(@"\[(\d+)\s*/\s*(\d+)\]", RegexOptions.Compiled);
    private static readonly Regex PercentRegex = new(@"(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private double _high = -1;
    private int _loggedCount = -1;
    private int _seedComplete;
    private string? _lastActivityLog;

    public void Reset()
    {
        _high = -1;
        _loggedCount = -1;
        _seedComplete = 0;
        _lastActivityLog = null;
    }

    /// <summary>
    /// Seed forward-only progress from resume.txt so a resumed run does not flash Mods 0/N.
    /// </summary>
    public void Seed(int complete, int total)
    {
        if (total <= 0 || complete <= 0)
            return;
        _seedComplete = Math.Clamp(complete, 0, total);
        _loggedCount = Math.Max(_loggedCount, _seedComplete);
        var child = _seedComplete / (double)total * 100d;
        if (child > _high)
            _high = child;
    }

    public CliProgressUpdate Apply(string raw)
    {
        var line = AnsiRegex.Replace(raw, "").Trim();
        if (line.Length == 0)
            return default;
        if (line[0] == '{')
            return ApplyJson(line);
        return ApplyText(line);
    }

    private CliProgressUpdate ApplyJson(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl))
                return Text(line);
            switch (typeEl.GetString())
            {
                case "progress":
                    return ApplyProgress(root);
                case "checksum":
                    return ApplyCounter(root, FileName(root, "path"), "Check");
                case "updates":
                    return ApplyUpdates(root);
                case "hashProgress":
                    if (Fraction(root, "progress") is { } hash)
                        return Progress(hash * 100, "Checking files", null);
                    return default;
                case "log":
                    return ApplyLog(root);
                case "result":
                    if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                        return Text(StringOf(root, "error") ?? "Command failed.", LogKind.Error);
                    return default;
                default:
                    return default;
            }
        }
        catch (JsonException)
        {
            return Text(line);
        }
    }

    private CliProgressUpdate ApplyUpdates(JsonElement root)
    {
        if (!root.TryGetProperty("updates", out var updates) || updates.ValueKind != JsonValueKind.Array || updates.GetArrayLength() == 0)
            return Text("No GAMMA updates.");

        var names = new List<string>();
        foreach (var item in updates.EnumerateArray())
        {
            var name = StringOf(item, "addonName") ?? StringOf(item, "newZipName") ?? "addon";
            var status = StringOf(item, "status");
            names.Add(string.IsNullOrEmpty(status) ? name : status + " " + name);
        }

        var shown = string.Join(", ", names.Take(8));
        if (names.Count > 8)
            shown += ", ...";
        return Text("GAMMA updates (" + names.Count + "): " + shown);
    }

    private CliProgressUpdate ApplyProgress(JsonElement root)
    {
        var name = StringOf(root, "name") ?? "Mod";
        var operation = StringOf(root, "operation") ?? "";
        var rawComplete = IntOf(root, "complete");
        var complete = EffectiveComplete(rawComplete);
        var total = IntOf(root, "total");
        var fraction = Fraction(root, "progress");
        double child;
        if (total > 0)
            child = (complete + (fraction ?? 0)) / total * 100d;
        else if (fraction != null)
            child = fraction.Value * 100d;
        else
            child = Math.Max(_high, 0);

        var amount = fraction != null
            ? $"{fraction.Value * 100:0}%"
            : BytesOf(root);
        var counter = total > 0 ? $" ({complete}/{total})" : "";
        var detail = $"{name} — {operation} {amount}{counter}".Replace("  ", " ").Trim(' ', '—');
        return Progress(child, detail, BuildProgressLog(name, operation, rawComplete, complete, total));
    }

    private CliProgressUpdate ApplyCounter(JsonElement root, string name, string operation)
    {
        var rawComplete = IntOf(root, "complete");
        var complete = EffectiveComplete(rawComplete);
        var total = IntOf(root, "total");
        if (total <= 0)
            return default;
        var child = complete / (double)total * 100d;
        return Progress(child, $"{name} — {operation} ({complete}/{total})", BuildProgressLog(name, operation, rawComplete, complete, total));
    }

    private string? BuildProgressLog(string name, string operation, int rawComplete, int complete, int total)
    {
        if (total > 0 && complete > _loggedCount)
        {
            _loggedCount = complete;
            _lastActivityLog = null;
            return $"Mods {complete}/{total}";
        }

        // While the CLI walk is still below the resume seed, Mods N/N would not advance.
        // Log each new mod/operation so the UI log does not look frozen.
        if (_seedComplete > 0 && rawComplete < _seedComplete)
        {
            var activity = $"{name} — {operation}".Replace("  ", " ").Trim(' ', '—');
            if (!string.IsNullOrEmpty(activity)
                && !string.Equals(activity, _lastActivityLog, StringComparison.Ordinal))
            {
                _lastActivityLog = activity;
                return activity;
            }
        }

        return null;
    }

    private int EffectiveComplete(int complete) =>
        complete < _seedComplete ? _seedComplete : complete;

    private CliProgressUpdate ApplyLog(JsonElement root)
    {
        var level = StringOf(root, "level") ?? "";
        if (level is "Verbose" or "Debug")
            return default;
        var message = StringOf(root, "message");
        if (string.IsNullOrWhiteSpace(message))
            return default;
        var kind = level is "Error" or "Fatal" ? LogKind.Error : level == "Warning" ? LogKind.Warning : LogKind.Info;
        return Text(message, kind);
    }

    private CliProgressUpdate ApplyText(string line)
    {
        var counter = CounterRegex.Match(line);
        var percents = PercentRegex.Matches(line);
        double? file = null;
        if (percents.Count > 0 && double.TryParse(percents[^1].Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            file = parsed;

        if (counter.Success
            && int.TryParse(counter.Groups[1].Value, out var index)
            && int.TryParse(counter.Groups[2].Value, out var total)
            && total > 0)
        {
            var child = (index + Math.Clamp(file ?? 0, 0, 100) / 100d) / total * 100d;
            string? log = null;
            if (index > _loggedCount)
            {
                _loggedCount = index;
                log = $"Mods {index}/{total}";
            }

            return Progress(child, line, log);
        }

        if (file != null)
            return Progress(file.Value, line, null);
        return Text(line);
    }

    private CliProgressUpdate Progress(double child, string detail, string? log)
    {
        if (child < _high)
            child = _high;
        else
            _high = child;
        var percent = (int)Math.Clamp(Math.Round(child), 0, 100);
        return new CliProgressUpdate(true, percent, detail, log, LogKind.Info);
    }

    private static CliProgressUpdate Text(string message, LogKind kind = LogKind.Info) =>
        new(false, 0, null, message, kind);

    private static string FileName(JsonElement root, string property)
    {
        var value = StringOf(root, property);
        return string.IsNullOrWhiteSpace(value) ? "File" : Path.GetFileName(value);
    }

    private static string? StringOf(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int IntOf(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number)
            return 0;
        return value.TryGetInt32(out var number) ? number : 0;
    }

    private static double? Fraction(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
            return null;
        if (number > 1 && number <= 100)
            number /= 100d;
        return Math.Clamp(number, 0, 1);
    }

    private static string BytesOf(JsonElement root)
    {
        if (!root.TryGetProperty("bytesDownloaded", out var value) || !value.TryGetInt64(out var bytes))
            return "";
        return TransferSnapshot.FormatSize(bytes);
    }
}
