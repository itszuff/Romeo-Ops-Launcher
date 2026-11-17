namespace RomeoOps.Installer;

public sealed class CoopMatch
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private readonly List<CoopAddon> downloads = [];
    private readonly List<string> actions = [];
    private readonly List<string> manual = [];
    private string[] profile = [];
    private CoopMatch() {}
    public bool CanApply => actions.Count > 0;
    public string Description => string.Join(Environment.NewLine, actions.Concat(manual));

    public static CoopMatch Plan(ExistingStack stack, CoopSetup target)
    {
        var result = new CoopMatch();
        var original = File.ReadAllLines(stack.Profile);
        var mods = Path.Combine(stack.Layout.Gamma,"mods");
        var installed = (Directory.Exists(mods) ? Directory.EnumerateDirectories(mods) : []).Select(p=>new DirectoryInfo(p).Name).ToHashSet(Names);
        var enabled = original.Where(s=>s.StartsWith('+')).Select(s=>s[1..]).ToHashSet(Names);
        var wanted = target.LoadOrder.ToHashSet(Names);
        var protectedEntries = original.Where(s=>s.StartsWith('*')).Select(s=>s[1..]).ToHashSet(Names);
        var desired = new List<string>();
        var catalog = CoopAddon.Catalog.ToDictionary(a=>a.Folder,Names);
        foreach (var name in target.LoadOrder)
        {
            if (protectedEntries.Contains(name)) { result.manual.Add("MO2 manages this entry: "+name); continue; }
            if (Names.Equals(name,Pins.XrModName) && !stack.CoOp)
            {
                result.manual.Add("Install xrRazom through the launcher to enable co-op.");
                continue;
            }
            bool present = installed.TryGetValue(name,out var localName);
            var release = target.Releases?.GetValueOrDefault(name);
            if (stack.CoOp && catalog.TryGetValue(name,out var addon) && target.Addons.Contains(addon.Name,Names))
            {
                var local = InstalledAddon.Detect(stack,addon);
                bool different = release?.Sha256 != null && !Names.Equals(release.Sha256,local?.Sha256);
                if (!present || local == null || different || (release?.Sha256 == null && release?.Version != null && !Names.Equals(release.Version,local.Version)))
                {
                    if (local != null && !Names.Equals(Path.GetFileName(local.Folder),name))
                        result.manual.Add("Resolve the alternate MO2 folder for "+addon.Name+".");
                    else if (addon.NeedsCore && !target.Addons.Contains(CoopAddon.Catalog[0].Name,Names))
                        result.manual.Add(addon.Name+" requires Master Core.");
                    else
                    {
                        result.downloads.Add(addon);
                    }
                }
            }
            if (present || result.downloads.Any(a=>Names.Equals(a.Folder,name)))
            {
                desired.Add(localName ?? name);
                if (!enabled.Contains(name)) result.actions.Add("Enable "+name);
            }
            else result.manual.Add("Missing mod folder: "+name+". Install the matching copy through MO2.");
        }
        if (stack.CoOp && !wanted.Contains(Pins.XrModName))
        {
            desired.Insert(0,Pins.XrModName);
            result.manual.Add("Change the xrRazom installation separately to match co-op mode.");
        }

        bool Supplies(string name, CoopAddon addon) =>
            result.downloads.Any(a=>a==addon && Names.Equals(name,a.Folder)) ||
            (installed.Contains(name) && File.Exists(Path.Combine(mods,name,"gamedata","scripts",addon.Script)));
        if (!desired.Any(name=>Supplies(name,CoopAddon.Catalog[0])))
        {
            foreach (var name in desired.Where(n=>CoopAddon.Catalog.Any(a=>a.NeedsCore && Supplies(n,a))).ToArray())
            {
                desired.Remove(name);
                result.downloads.RemoveAll(a=>Names.Equals(a.Folder,name));
                result.actions.Remove("Enable "+name);
                result.manual.Add("Master Core must be available before enabling "+name+".");
            }
        }

        foreach (var addon in result.downloads)
        {
            var release=target.Releases?.GetValueOrDefault(addon.Folder);
            string check=" (current ModDB release).";
            if (release?.Sha256!=null) check=" and verify the exported SHA-256 before installing.";
            else if (release?.Version!=null) check=" and verify version "+release.Version+".";
            result.actions.Add("Download "+addon.Name+check);
        }
        var active = desired.ToHashSet(Names);
        var ordered = new Queue<string>(desired);
        var output = new List<string>();
        var usedSlots = new HashSet<string>(Names);
        int lastSlot = -1;
        foreach (var line in original)
        {
            if (line.Length > 1 && line[0] is '+' or '-')
            {
                if (active.Contains(line[1..]))
                {
                    if (!usedSlots.Add(line[1..])) continue;
                    output.Add("+"+ordered.Dequeue());
                    lastSlot = output.Count;
                    continue;
                }
                if (line[0]=='+')
                {
                    output.Add("-"+line[1..]);
                    result.actions.Add("Disable "+line[1..]);
                    continue;
                }
            }
            output.Add(line);
        }
        output.InsertRange(lastSlot < 0 ? output.TakeWhile(s=>s.StartsWith('#')).Count() : lastSlot,ordered.Select(n=>"+"+n));
        result.profile=output.ToArray();
        if (!original.SequenceEqual(result.profile))
            result.actions.Add("Back up the profile and apply the available mods in the loaded order.");
        return result;
    }

    private static IEnumerable<string> ReadLines(StreamReader reader)
    {
        while (reader.ReadLine() is string line) yield return line;
    }

    public static async Task Apply(ExistingStack stack, CoopSetup target,
        Func<CoopAddon,string,CancellationToken,Task> fetch, Action<string> report, CancellationToken token)
    {
        var plan=Plan(stack,target);
        if (plan.downloads.Count>0)
            await CoopAddon.Install(stack,plan.downloads,async (addon,path,ct)=>
            {
                await fetch(addon,path,ct);
                var expected=target.Releases?.GetValueOrDefault(addon.Folder);
                if (expected?.Sha256!=null) FileHasher.AssertMatch(path,"SHA256",expected.Sha256);
                else if (expected?.Version!=null)
                {
                    using var zip=System.IO.Compression.ZipFile.OpenRead(path);
                    var marker="gamedata/scripts/"+addon.Script;
                    var script=zip.Entries.SingleOrDefault(e=>("/"+e.FullName).EndsWith("/"+marker,StringComparison.OrdinalIgnoreCase));
                    var meta=script==null ? null : zip.Entries.FirstOrDefault(e=>Names.Equals(e.FullName,script.FullName[..^marker.Length]+"meta.ini"));
                    using var reader=meta==null ? null : new StreamReader(meta.Open());
                    if (reader==null || !Names.Equals(InstalledAddon.ReadVersion(ReadLines(reader)),expected.Version))
                        throw new InvalidDataException($"The download could not be verified as {addon.Name} version {expected.Version}. Install that release through MO2.");
                }
            },report,token,matchSelection:true);

        using var gate=SavedRun.Lock(stack.Layout);
        CoopAddon.RequireIdle(stack.Layout);
        token.ThrowIfCancellationRequested();
        var fresh=Plan(stack,target);
        if (fresh.downloads.Count>0) throw new IOException("A requested release is still missing. Compare the setup again.");
        var original=File.ReadAllLines(stack.Profile);
        if (original.SequenceEqual(fresh.profile)) return;
        Directory.CreateDirectory(stack.Layout.Backups);
        string backup=Path.Combine(stack.Layout.Backups,"modlist-coop-"+Guid.NewGuid().ToString("N")+".txt");
        var ledger=CoopAddon.Ledger(stack.Layout);
        var active=fresh.profile.Where(s=>s.StartsWith('+')).Select(s=>s[1..]).ToHashSet(Names);
        File.WriteAllLines(stack.Profile+".new",fresh.profile,ModlistEditor.EncodingFor(stack.Profile));
        File.Replace(stack.Profile+".new",stack.Profile,backup);
        try
        {
            if (File.Exists(ledger))
            {
                var entries=File.ReadAllLines(ledger).Select(s=>s.StartsWith('-') ? s[1..] : s)
                    .Select(n=>active.Contains(n) ? n : "-"+n);
                File.WriteAllLines(ledger+".new",entries);
                File.Move(ledger+".new",ledger,true);
            }
        }
        catch { File.Copy(backup,stack.Profile,true); throw; }
    }
}
