namespace RomeoOps.Installer;

public sealed record PlayerBackup(string Folder,DateTime Date,bool User,bool Mcm)
{
    public string Label => $"{Date:G}  ·  {string.Join(" + ",new[]{User ? "user.ltx" : "", Mcm ? "MCM" : ""}.Where(x=>x.Length>0))}  ·  {Path.GetFileName(Folder)[^6..]}";
}

public static class PlayerOptions
{
    private const string Mcm = @"gamedata\configs\axr_options.ltx";
    public static string User(InstallLayout layout) => Path.Combine(layout.Anomaly, "appdata", "user.ltx");
    public static string Override(InstallLayout layout) => Path.Combine(layout.Gamma, "overwrite", Mcm);
    public static string? ActiveMcm(InstallLayout layout)
    {
        if (File.Exists(Override(layout))) return Override(layout);
        var profile = File.Exists(layout.Modlist) ? layout.Modlist : ExistingStack.Detect(layout.Root)?.Profile;
        if (profile != null)
        {
            foreach (var line in File.ReadLines(profile).Where(s => s.StartsWith('+')))
            {
                var name = line[1..];
                if (name.Length == 0 || name != Path.GetFileName(name) || name is "." or "..") continue;
                var file = Path.Combine(layout.Gamma, "mods", name, Mcm);
                if (File.Exists(file)) return file;
            }
        }
        var loose = Path.Combine(layout.Anomaly, Mcm);
        return File.Exists(loose) ? loose : null;
    }
    public static string Capture(InstallLayout layout, Guid id)
    {
        var folder = Path.Combine(layout.Backups,"player-options-" + id.ToString("N"));
        if (Directory.Exists(folder)) return folder;
        var staging = folder + "." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        if (File.Exists(User(layout))) File.Copy(User(layout),Path.Combine(staging,"user.ltx"));
        if (ActiveMcm(layout) is string file) File.Copy(file,Path.Combine(staging,"axr_options.ltx"));
        Directory.Move(staging,folder);
        return folder;
    }
    public static void Apply(InstallLayout layout, string backup, bool preserveUser, bool preserveMcm, bool success, bool coop = false)
    {
        ApplyOne("user.ltx", User(layout), preserveUser);
        ApplyOne("axr_options.ltx", Override(layout), preserveMcm);

        void ApplyOne(string name,string destination,bool preserve)
        {
            var original = Path.Combine(backup,name);
            if (!success && !File.Exists(original)) return;
            if (File.Exists(original) && (preserve || !success))
            {
                using var input = File.OpenRead(original);
                Write(destination,input);
            }
            else Reset(layout,coop,name);
        }
    }
    private static string Baseline(InstallLayout layout,bool coop,string name) =>
        Path.Combine(layout.State,coop ? "fresh-coop" : "fresh-solo",name);

    public static void RecordFresh(InstallLayout layout,bool coop,string backup)
    {
        foreach (var (name,file) in new[]{("user.ltx",User(layout)),("axr_options.ltx",ActiveMcm(layout))})
        {
            // An adopted/player-supplied file cannot serve as the factory default.
            if (file == null || !File.Exists(file) || File.Exists(Path.Combine(backup,name))) continue;
            using var input = File.OpenRead(file);
            Write(Baseline(layout,coop,name),input);
        }
    }

    public static void Reset(InstallLayout layout,bool coop,string name)
    {
        var destination = name == "user.ltx" ? User(layout) : Override(layout);
        var baseline = Baseline(layout,coop,name);
        if (File.Exists(baseline))
        {
            using var input = File.OpenRead(baseline);
            Write(destination,input);
            return;
        }
        using var resource = typeof(PlayerOptions).Assembly.GetManifestResourceStream(name)
            ?? throw new IOException("Missing packaged default: " + name);
        Write(destination,resource);
    }
    public static PlayerBackup[] Backups(InstallLayout layout)
    {
        if(!Directory.Exists(layout.Backups)) return [];
        return Directory.EnumerateDirectories(layout.Backups,"player-options-*")
            .Where(d=>Guid.TryParseExact(Path.GetFileName(d)["player-options-".Length..],"N",out _))
            .Select(d=>new PlayerBackup(d,Directory.GetCreationTime(d),File.Exists(Path.Combine(d,"user.ltx")),File.Exists(Path.Combine(d,"axr_options.ltx"))))
            .Where(b=>b.User || b.Mcm).OrderByDescending(b=>b.Date).ToArray();
    }
    public static void RestoreBackup(InstallLayout layout, PlayerBackup chosen,bool user,bool mcm)
    {
        using var gate=SavedRun.Lock(layout);
        CoopAddon.RequireIdle(layout);
        if(!Backups(layout).Any(b=>b.Folder==chosen.Folder && (!user || b.User) && (!mcm || b.Mcm)))
            throw new IOException("The selected backup is no longer available.");
        Capture(layout,Guid.NewGuid());
        if(user) Copy("user.ltx",User(layout));
        if(mcm) Copy("axr_options.ltx",Override(layout));
        void Copy(string name,string destination)
        {
            using var file=File.OpenRead(Path.Combine(chosen.Folder,name));
            Write(destination,file);
        }
    }
    private static void Write(string destination,Stream source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(temp)) source.CopyTo(file);
            File.Move(temp,destination,true);
        }
        finally { File.Delete(temp); }
    }
}
