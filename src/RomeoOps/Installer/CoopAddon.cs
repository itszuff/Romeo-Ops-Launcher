using System.Diagnostics;
using System.IO.Compression;

namespace RomeoOps.Installer;

public sealed record CoopAddon(string Name, string Slug, string Script, bool NeedsCore = false)
{
    public string PageUrl => "https://www.moddb.com/mods/xrrazom-stalker-anomaly-co-op/addons/"+Slug;
    public string Folder => "MeeMaster - " + Name;
    public static readonly CoopAddon[] Catalog =
    [
        new("Master Core","master-core","master_core.script"),
        new("DANTE","dante2","zzz_dante.script",true),
        new("Persistent Stalkers","persistent-stalkers","persistent_stalkers.script",true),
        new("Med Bag","med-bag","med_bag.script",true),
        new("Multiplayer Settings","multiplayer-settings","mp_settings.script")
    ];

    private static string Ledger(InstallLayout layout) => Path.Combine(layout.State,"meemaster-enabled.txt");
    private static string Marker(string folder, CoopAddon addon) => Path.Combine(folder,"gamedata","scripts",addon.Script);
    public static bool Enabled(ExistingStack stack, CoopAddon addon) => ActiveFolder(stack,addon) != null;
    private static string? ActiveFolder(ExistingStack stack, CoopAddon addon)
    {
        var mods = Path.Combine(stack.Layout.Gamma,"mods");
        return File.ReadLines(stack.Profile).Where(line => line.StartsWith('+') && line.Length > 1
            && Path.GetFileName(line[1..]) == line[1..] && File.Exists(Marker(Path.Combine(mods,line[1..]),addon))).Select(line=>line[1..]).FirstOrDefault();
    }
    public static void RequireIdle(InstallLayout layout)
    {
        if (SavedRun.Read(layout.Root) != null) throw new IOException("Resume and finish the saved operation first.");
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.ProcessName.Equals("ModOrganizer",StringComparison.OrdinalIgnoreCase) ||
                    process.ProcessName.StartsWith("AnomalyDX",StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Close Mod Organizer and Anomaly before changing this installation.");
            }
        }
    }
    public static async Task Install(ExistingStack stack,IEnumerable<CoopAddon> selected,
        Func<CoopAddon,string,CancellationToken,Task> fetch,Action<string> report,CancellationToken token, bool? enable=null, bool update=false)
    {
        var layout=stack.Layout;
        using var gate=SavedRun.Lock(layout);
        RequireIdle(layout);
        if(!stack.CoOp || !Directory.Exists(Path.Combine(layout.XrModDir,"gamedata")))
            throw new IOException("Enable xrRazom in the ROMEO profile first.");
        var picks=selected.ToHashSet();
        if(enable==false && !update && picks.Contains(Catalog[0]) && Catalog.Any(a=>a.NeedsCore && Enabled(stack,a)))
            throw new IOException("Disable DANTE, Persistent Stalkers and Med Bag before disabling Master Core.");
        if(!update && enable!=false && picks.Any(a=>a.NeedsCore)) picks.Add(Catalog[0]);
        string work=Path.Combine(layout.Staging,"addon-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var mods=Path.Combine(layout.Gamma,"mods");
        var prepared=new List<(string Source,string Destination)>();
        var swapped=new List<(string Destination,string? Backup)>();
        string ledger=Ledger(layout);
        var previousLedger=File.Exists(ledger) ? File.ReadAllBytes(ledger) : null;
        var managed=File.Exists(ledger) ? File.ReadAllLines(ledger).ToHashSet() : new HashSet<string>();
        var changes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach(var addon in Catalog.Where(picks.Contains))
            {
                token.ThrowIfCancellationRequested();
                var local=InstalledAddon.Detect(stack,addon);
                var folder=local==null ? addon.Folder : Path.GetFileName(local.Folder);
                bool enabled=update ? local?.Enabled ?? false : enable ?? true;
                if((update || !enabled) && local==null) throw new IOException(addon.Name+" is no longer installed.");
                if(update && !folder.Equals(addon.Folder,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("This copy belongs to the MO2 folder '"+folder+"'. Update it through Mod Organizer.");
                var destination=Path.Combine(mods,folder);
                managed.RemoveWhere(entry=>entry.TrimStart('-').Equals(folder,StringComparison.OrdinalIgnoreCase));
                managed.Add(enabled ? folder : "-"+folder);
                changes.Add(folder);
                if(enable.HasValue && !update && local!=null) continue;
                if(Directory.Exists(destination) && !File.Exists(Marker(destination,addon)))
                    throw new IOException("An incompatible folder already exists: "+destination);
                report("Checking the current ModDB release for "+addon.Name);
                string archive=Path.Combine(work,addon.Slug+".zip");
                await fetch(addon,archive,token).ConfigureAwait(false);
                var digest=FileHasher.Compute(archive,"SHA256");
                var stamp=Path.Combine(destination,".romeo-release");
                var md5=FileHasher.Compute(archive,"MD5");
                if(File.Exists(stamp) && File.ReadAllText(stamp)==digest)
                {
                    File.WriteAllText(stamp+".md5",md5);
                    continue;
                }
                string extracted=Path.Combine(work,addon.Slug);
                ZipFile.ExtractToDirectory(archive,extracted);
                var roots=Directory.EnumerateFiles(extracted,addon.Script,SearchOption.AllDirectories)
                    .Where(f=>f.EndsWith(Path.Combine("gamedata","scripts",addon.Script),StringComparison.OrdinalIgnoreCase))
                    .Select(f=>Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(f)))!).ToArray();
                if(roots.Length!=1) throw new InvalidDataException("Unexpected "+addon.Name+" ZIP contents. Its release layout may have changed.");
                File.WriteAllText(Path.Combine(roots[0],".romeo-release"),digest);
                File.WriteAllText(Path.Combine(roots[0],".romeo-release.md5"),md5);
                prepared.Add((roots[0],destination));
            }
            token.ThrowIfCancellationRequested();
            RequireIdle(layout);
            Directory.CreateDirectory(mods);
            Directory.CreateDirectory(layout.Backups);
            byte[] profile=File.ReadAllBytes(stack.Profile);
            try
            {
                foreach(var (source,destination) in prepared)
                {
                    string? backup=null;
                    if(Directory.Exists(destination))
                    {
                        backup=Path.Combine(layout.Backups,Path.GetFileName(destination)+"-"+Guid.NewGuid().ToString("N"));
                        Directory.Move(destination,backup);
                    }
                    swapped.Add((destination,backup));
                    Directory.Move(source,destination);
                }
                File.WriteAllLines(ledger+".new",managed);
                File.Move(ledger+".new",ledger,true);
                Restore(layout,stack.Profile,changes);
            }
            catch
            {
                foreach(var (destination,backup) in swapped.AsEnumerable().Reverse())
                {
                    if(Directory.Exists(destination)) Directory.Move(destination,Path.Combine(work,Guid.NewGuid().ToString("N")));
                    if(backup!=null) Directory.Move(backup,destination);
                }
                File.WriteAllBytes(stack.Profile,profile);
                if(previousLedger==null) File.Delete(ledger); else File.WriteAllBytes(ledger,previousLedger);
                throw;
            }
        }
        finally { if(Directory.Exists(work)) Directory.Delete(work,true); }
    }

    public const string LauncherSeparator = "Romeo Ops Launcher_separator";

    public static void Restore(InstallLayout layout, string profile, HashSet<string>? changed = null)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var managed = Catalog.Select(a => a.Folder).Concat(Pins.PriorityMods).ToHashSet(comparer);
        var desired = new Dictionary<string,string>(comparer);
        var ledger = Ledger(layout);
        if (File.Exists(ledger))
        {
            foreach (var entry in File.ReadLines(ledger))
            {
                var name = entry.StartsWith('-') ? entry[1..] : entry;
                if (name.Length == 0 || name != Path.GetFileName(name) || name is "." or "..") continue;
                if (!Catalog.Any(a => File.Exists(Marker(Path.Combine(layout.Gamma, "mods", name), a)))) continue;
                managed.Add(name);
                if (changed == null || changed.Contains(name))
                    desired[name] = entry.StartsWith('-') ? entry : "+" + name;
            }
        }

        var original = File.ReadAllLines(profile);
        var other = new List<string>();
        var group = new Dictionary<string,string>(comparer);
        foreach (var line in original)
        {
            if (line.Length > 1 && line[0] is '+' or '-' or '*')
            {
                var name = line[1..];
                if (comparer.Equals(name, LauncherSeparator)) continue;
                if (managed.Contains(name))
                {
                    group.TryAdd(name, line);
                    continue;
                }
            }
            other.Add(line);
        }
        foreach (var (name, line) in desired) group[name] = line;
        if (group.Count == 0) return;

        // MO2 stores highest priority first, so the separator follows its members.
        // Keep disabled entries in the group without promoting them over active priority mods.
        var block = new List<string>();
        foreach (var name in Pins.PriorityMods)
            if (group.TryGetValue(name, out var line) && line.StartsWith('+'))
            {
                block.Add(line);
                group.Remove(name);
            }
        block.AddRange(group.Values);
        block.Add("-" + LauncherSeparator);
        other.InsertRange(other.TakeWhile(line => line.StartsWith('#')).Count(), block);
        Directory.CreateDirectory(Path.Combine(layout.Gamma, "mods", LauncherSeparator));
        if (original.SequenceEqual(other)) return;

        Directory.CreateDirectory(layout.Backups);
        File.Copy(profile, Path.Combine(layout.Backups, "modlist-addons-" + Guid.NewGuid().ToString("N") + ".txt"));
        var temp = profile + ".new";
        File.WriteAllLines(temp, other, ModlistEditor.EncodingFor(profile));
        File.Move(temp, profile, true);
    }
}
