using System.Text.Json;

namespace RomeoOps.Installer;

public sealed record ModRelease(string? Version, string? Sha256);

public sealed record CoopSetup(int Format,string XrVersion,string EngineHash,string[] Addons,string[] LoadOrder, Dictionary<string,ModRelease>? Releases = null)
{
    public static CoopSetup Capture(ExistingStack stack)
    {
        var engine = Path.Combine(stack.Layout.Anomaly,"bin","AnomalyDX11AVX.exe");
        var hash = File.Exists(engine) ? FileHasher.Compute(engine,"SHA256") : "Missing";
        var known = stack.CoOp && File.Exists(engine) && ModdedAnomaly.KnownOverlay(stack.Layout.Anomaly,engine,true);
        var version = !stack.CoOp ? "Disabled" : known ? "1.5 Slim" : "Unrecognized build";
        var enabled = File.ReadLines(stack.Profile).Where(s=>s.Length>1 && s.StartsWith('+')).Select(s=>s[1..]).ToArray();
        var mods = Path.Combine(stack.Layout.Gamma,"mods");
        var releases = new Dictionary<string,ModRelease>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in enabled.Where(ValidName))
        {
            var folder = Path.Combine(mods,name);
            var ini = Path.Combine(folder,"meta.ini");
            var label = File.Exists(ini) ? InstalledAddon.ReadVersion(File.ReadLines(ini)) : null;
            var digest = InstalledAddon.Stamp(folder,".romeo-release",64);
            if (label != null || digest != null) releases[name] = new(label,digest);
        }
        return new(2,version,hash,CoopAddon.Catalog.Where(a=>CoopAddon.Enabled(stack,a)).Select(a=>a.Name).ToArray(),enabled,releases);
    }

    internal static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 255 &&
        name != "." && name != ".." && !name.EndsWith('.') && !name.EndsWith(' ') &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Any(char.IsControl);

    public static CoopSetup Read(string path)
    {
        if(new FileInfo(path).Length>4_000_000) throw new InvalidDataException("Setup file exceeds 4 MB.");
        var setup=JsonSerializer.Deserialize<CoopSetup>(File.ReadAllText(path));
        if(setup is not {Format:1 or 2, XrVersion:not null, EngineHash:not null, Addons:not null, LoadOrder:not null} ||
            setup.Addons.Concat(setup.LoadOrder).Any(n=>!ValidName(n)) ||
            setup.LoadOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=setup.LoadOrder.Length)
            throw new InvalidDataException("Invalid ROMEO setup export.");
        var releases = new Dictionary<string,ModRelease>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name,release) in setup.Releases ?? [])
        {
            if (!ValidName(name) || release == null || !releases.TryAdd(name,release) ||
                release.Version is { } v && (v.Length > 60 || v.Any(char.IsControl)) ||
                release.Sha256 is { } h && (h.Length != 64 || !h.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Invalid mod release metadata.");
        }
        return setup with { Releases = releases };
    }
    public string Compare(CoopSetup friend)
    {
        var changes=new List<string>();
        var comparer=StringComparer.OrdinalIgnoreCase;
        if(XrVersion!=friend.XrVersion) changes.Add($"xrRazom: yours {XrVersion}; theirs {friend.XrVersion}");
        if(!comparer.Equals(EngineHash,friend.EngineHash)) changes.Add("DX11 AVX engine builds differ.");
        foreach(var (label,yours,theirs) in new[]{("add-on",Addons,friend.Addons),("mod",LoadOrder,friend.LoadOrder)})
        {
            changes.AddRange(yours.Except(theirs,comparer).Select(n=>"Only you have this "+label+": "+n));
            changes.AddRange(theirs.Except(yours,comparer).Select(n=>"Only they have this "+label+": "+n));
        }
        var shared=LoadOrder.Intersect(friend.LoadOrder,comparer).ToHashSet(comparer);
        int unknown=0;
        foreach (var name in shared)
        {
            var local=Releases?.GetValueOrDefault(name);
            var target=friend.Releases?.GetValueOrDefault(name);
            if (target?.Version != null && !comparer.Equals(local?.Version,target.Version))
                changes.Add($"Mod version: {name} (yours {local?.Version ?? "unlisted"}, theirs {target.Version})");
            if (target?.Sha256 != null && !comparer.Equals(local?.Sha256,target.Sha256))
                changes.Add($"Release checksum: {name} (yours {local?.Sha256 ?? "unlisted"}, theirs {target.Sha256})");
            if (target?.Version == null && target?.Sha256 == null) unknown++;
        }
        var theirPositions=friend.LoadOrder.Where(shared.Contains).Select((name,index)=>(name,index))
            .ToDictionary(p=>p.name,p=>p.index,comparer);
        int pos=0;
        foreach(var name in LoadOrder.Where(shared.Contains))
        {
            var other=theirPositions[name];
            if(other!=pos) changes.Add($"Shared mod priority: {name} (yours {pos+1}, theirs {other+1})");
            pos++;
        }
        if (changes.Count==0) changes.Add("The reported setup details match.");
        if (unknown>0) changes.Add($"The loaded JSON has no version or release checksum for {unknown} shared mod(s).");
        return string.Join(Environment.NewLine,changes);
    }
}
