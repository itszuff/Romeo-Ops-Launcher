using System.Text.Json;

namespace RomeoOps.Installer;

public sealed record CoopSetup(int Format,string XrVersion,string EngineHash,string[] Addons,string[] LoadOrder)
{
    public static CoopSetup Capture(ExistingStack stack)
    {
        var engine = Path.Combine(stack.Layout.Anomaly,"bin","AnomalyDX11AVX.exe");
        var hash = File.Exists(engine) ? FileHasher.Compute(engine,"SHA256") : "Missing";
        var known = stack.CoOp && File.Exists(engine) && AnomalyCheck.KnownOverlay(stack.Layout.Anomaly,engine,true);
        var version = !stack.CoOp ? "Disabled" : known ? "1.5 Slim" : "Unrecognized build";
        var enabled = File.ReadLines(stack.Profile).Where(s=>s.Length>1 && s.StartsWith('+')).Select(s=>s[1..]).ToArray();
        return new(1,version,hash,CoopAddon.Catalog.Where(a=>CoopAddon.Enabled(stack,a)).Select(a=>a.Name).ToArray(),enabled);
    }

    public static CoopSetup Read(string path)
    {
        if(new FileInfo(path).Length>4_000_000) throw new InvalidDataException("Setup file exceeds 4 MB.");
        var setup=JsonSerializer.Deserialize<CoopSetup>(File.ReadAllText(path));
        if(setup is not {Format:1, XrVersion:not null, EngineHash:not null, Addons:not null, LoadOrder:not null} ||
            setup.Addons.Concat(setup.LoadOrder).Any(string.IsNullOrWhiteSpace) ||
            setup.LoadOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=setup.LoadOrder.Length)
            throw new InvalidDataException("Invalid ROMEO setup export.");
        return setup;
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
        var theirPositions=friend.LoadOrder.Where(shared.Contains).Select((name,index)=>(name,index))
            .ToDictionary(p=>p.name,p=>p.index,comparer);
        int pos=0;
        foreach(var name in LoadOrder.Where(shared.Contains))
        {
            var other=theirPositions[name];
            if(other!=pos) changes.Add($"Shared mod priority: {name} (yours {pos+1}, theirs {other+1})");
            pos++;
        }
        return changes.Count==0 ? "xrRazom build, enabled add-ons and mod order match." : string.Join(Environment.NewLine,changes);
    }
}
