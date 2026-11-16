using System.Text.Json;
using System.Text.RegularExpressions;

namespace RomeoOps.Installer;

public sealed class AnomalyCheck
{
    // Stock 1.5.3 fsgame.ltx plus the MCM log directory mapping.
    private const string McmConfig = "5750e35aac2a9ad43235c792a047e7542ccd03c8d94ee1566398205af7b83eb1";
    // SHA-256 of GAMMA modpack_patches/bin and the pinned xrRazom 1.5 Slim payload.
    private static readonly Dictionary<string, (string Gamma, string Xr)> Engines = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"bin\AnomalyDX10.exe"] = ("25793f7beeffc204d16d68fd5d843a52b49a267b1556c1a6eecef9abae32e035", "61776b09f5155a550e262ccfde483caafc9e2add1fff712c776decaff48f2016"),
        [@"bin\AnomalyDX10AVX.exe"] = ("32ceb74aabdc6e5165d277b3dffd5a0c71faa4276cc3687e6d5c7803446bb20c", "78a3f4cec2b611ac7517c41617fce076f975b09b90d56f4c6c3001b5d3e85608"),
        [@"bin\AnomalyDX11.exe"] = ("a23aed1c66d4c5acb9d6d3bf7027dfcfcf9a924a7a5a450da9d84c57cf7da528", "5121128b64d80497a1945e4ee1c64667dab9fce78bd8ebfe2580aa3b7a7845f3"),
        [@"bin\AnomalyDX11AVX.exe"] = ("ade57887f93ca95e9398da60b374035b50e4c38295b9802a6f89bc33a0394bc6", "ac994860e30efc9ddf3dd7d398ca059cff49db22b273c1afe07490246f3cc10b"),
        [@"bin\AnomalyDX8.exe"] = ("8aead598b0707fc8fc542798766100a3d69bb7691a42b6a725e7998daf22ad86", "020b808d7cdd01d4575e6e9d6981664f5e7528b68c45171eb2466f367272bd60"),
        [@"bin\AnomalyDX8AVX.exe"] = ("3a1d730a253249539e953c7248e5c0f83a7177a9adbbe92073df2ae33dcd3eea", "abe748bed001bb40b34211b83de9dd79aa2bb89217a6ad078a7ed65f0175e6a3"),
        [@"bin\AnomalyDX9.exe"] = ("c3c728a80c395af8221f11775094e22ad8f4a48782c1958811bef3c1d62cab92", "56bf3a69c9d69e0a8a22a89c1ac46ab45ccaa0810f473dccfaf99d8578bc7c71"),
        [@"bin\AnomalyDX9AVX.exe"] = ("fd12fb9a18ce156839af75ae2419114800ad52d6e47336d5edcb725732f2b522", "6bfc5f85c29b9775845d8835c75c55fa6611fd06cddb6b6cacb1180a65306921"),
    };

    private readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
    private int total;
    public string Error { get; private set; } = "";
    public IEnumerable<KeyValuePair<string, string>> Differences => files.Where(f => f.Value != "Ok");

    public void Observe(string line)
    {
        if (!line.StartsWith('{')) return;
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var type)) return;
        switch (type.GetString())
        {
            case "checksum":
                total = root.GetProperty("total").GetInt32();
                files[root.GetProperty("path").GetString()!] = root.GetProperty("status").GetString()!;
                break;
            case "result" when root.TryGetProperty("error", out var error):
                Error = error.GetString() ?? "";
                break;
        }
    }

    public bool IsCompletedMismatch(int exitCode)
    {
        var match = Regex.Match(Error, @"^(\d+) file\(s\) corrupt or missing\.?$", RegexOptions.IgnoreCase);
        return exitCode == 1 && total > 0 && files.Count == total && match.Success
            && int.TryParse(match.Groups[1].Value, out var count) && count > 0 && count == Differences.Count();
    }

    public static bool KnownOverlay(string anomaly, string path, bool coop)
    {
        string relative = Path.GetRelativePath(anomaly, path);
        string expected;
        if (relative.Equals("fsgame.ltx", StringComparison.OrdinalIgnoreCase)) expected = McmConfig;
        else if (Engines.TryGetValue(relative, out var hashes)) expected = coop ? hashes.Xr : hashes.Gamma;
        else return false;
        return File.Exists(path) && FileHasher.Compute(path, "SHA256").Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
