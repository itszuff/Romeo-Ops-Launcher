using System.Runtime.InteropServices;

namespace RomeoOps.Installer;

public static class InstallationScan
{
    [DllImport("ntdll.dll")]
    private static extern uint RtlComputeCrc32(uint initial, byte[] buffer, int length);

    public static string Inspect(ExistingStack stack, CancellationToken token, Action<int,string> progress)
    {
        var layout = stack.Layout;
        var issues = new List<string>();
        var overlays = new List<string>();
        var buffer = new byte[1024 * 1024];
        using var stream = typeof(InstallationScan).Assembly.GetManifestResourceStream("anomaly-153.tsv")
            ?? throw new InvalidDataException("Anomaly 1.5.3 reference table is missing.");
        using var reader = new StreamReader(stream);
        var rows = reader.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries);
        int stock = 0;
        for (int i=0; i<rows.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var fields = rows[i].TrimEnd('\r').Split('\t');
            var relative = fields[2];
            var path = Path.Combine(layout.Anomaly, relative);
            progress(i * 100 / rows.Length,relative);
            if (!File.Exists(path)) { issues.Add("Restore missing Anomaly file: "+relative); continue; }
            if (AnomalyCheck.KnownOverlay(layout.Anomaly,path,stack.CoOp))
            {
                overlays.Add(relative);
                continue;
            }
            using var file = File.OpenRead(path);
            uint crc = 0;
            bool matches = file.Length == long.Parse(fields[1]);
            if (matches)
            {
                int n;
                while ((n=file.Read(buffer)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    crc=RtlComputeCrc32(crc,buffer,n);
                }
                matches = crc==Convert.ToUInt32(fields[0],16);
            }
            if (!matches) issues.Add("Repair unrecognized Anomaly file: "+relative);
            else if (stack.CoOp && relative.StartsWith(@"bin\AnomalyDX",StringComparison.OrdinalIgnoreCase) && relative.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))
                issues.Add("Reapply xrRazom engine over stock file: "+relative);
            else stock++;
        }
        foreach (var path in new[]{Path.Combine(layout.Anomaly,"AnomalyLauncher.exe"),Path.Combine(layout.Gamma,"ModOrganizer.exe")})
            if (!File.Exists(path)) issues.Add("Restore missing executable: "+Path.GetFileName(path));
        if (!File.Exists(stack.Profile)) issues.Add("Restore the ROMEO profile from its archive.");
        else
        {
            var enabled = File.ReadLines(stack.Profile).Where(s=>s.StartsWith('+')).Select(s=>s[1..]).ToArray();
            bool xrEnabled = enabled.Contains(Pins.XrModName, StringComparer.OrdinalIgnoreCase);
            if (xrEnabled != stack.CoOp)
                issues.Add(stack.CoOp ? "Enable xrRazom in the ROMEO profile." : "Disable xrRazom in the ROMEO profile.");
            foreach(var name in enabled)
                if(!Directory.Exists(Path.Combine(layout.Gamma,"mods",name))) issues.Add("Restore missing enabled mod folder: "+name);
            if (!File.ReadLines(stack.Profile).Any(line => line.Length > 1 && line[1..].Equals(CoopAddon.LauncherSeparator, StringComparison.OrdinalIgnoreCase))
                || !Directory.Exists(Path.Combine(layout.Gamma, "mods", CoopAddon.LauncherSeparator)))
                issues.Add("Create the Romeo Ops Launcher separator for launcher-managed mods.");
            var priority=stack.CoOp ? Pins.PriorityMods : new[]{Pins.SpeedFixName};
            if(!ModlistEditor.TopMods(stack.Profile,priority.Length).SequenceEqual(priority.Select(n=>"+"+n)))
                issues.Add("Restore ROMEO mod priorities.");
        }
        if(stack.CoOp && !Directory.Exists(Path.Combine(layout.XrModDir,"gamedata"))) issues.Add("Restore xrRazom mod data.");
        var ini=Path.Combine(layout.Gamma,"ModOrganizer.ini");
        if(!File.Exists(ini) || !File.ReadLines(ini).Contains("selected_profile=@ByteArray("+Pins.RomeoProfile+")"))
            issues.Add("Select the ROMEO profile in Mod Organizer.");
        return string.Join(Environment.NewLine,new[]{ $"Checked {rows.Length} base-game files. {stock} stock matches, {overlays.Count} modded matches.",
            overlays.Count==0 ? "" : "Verified mod replacements: "+string.Join(", ",overlays),
            issues.Count==0 ? "No repairs indicated by these checks." : $"{issues.Count} repair finding(s):" }
            .Where(s=>s.Length>0).Concat(issues));
    }
}
