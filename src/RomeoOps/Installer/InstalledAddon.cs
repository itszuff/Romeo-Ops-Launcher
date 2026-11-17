using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace RomeoOps.Installer;

public sealed record InstalledAddon(string Folder,bool Enabled,string? Version,string? Sha256,string? Md5)
{
    public string Caption => (Enabled ? "Enabled" : "Installed, disabled") + (Version==null ? " · version unlisted" : " · v"+Version);

    public static InstalledAddon? Detect(ExistingStack stack,CoopAddon addon)
    {
        var mods=Path.Combine(stack.Layout.Gamma,"mods");
        var entries=File.ReadLines(stack.Profile).Where(s=>s.Length>1 && s[0] is '+' or '-').ToList();
        // Prefer the active copy when several MO2 folders supply the same script.
        foreach(var entry in entries.OrderByDescending(s=>s[0]=='+').Append("-"+addon.Folder))
        {
            var name=entry[1..];
            if(name!=Path.GetFileName(name) || name is "." or "..") continue;
            var folder=Path.Combine(mods,name);
            if(!File.Exists(Path.Combine(folder,"gamedata","scripts",addon.Script))) continue;
            var ini=Path.Combine(folder,"meta.ini");
            var version=File.Exists(ini) ? ReadVersion(File.ReadLines(ini)) : null;
            return new(folder,entry[0]=='+',version,Stamp(folder,".romeo-release",64),Stamp(folder,".romeo-release.md5",32));
        }
        return null;
    }

    internal static string? ReadVersion(IEnumerable<string> lines)
    {
        bool general=false;
        foreach(var raw in lines)
        {
            var line=raw.Trim();
            if(line.StartsWith('[')) general=line.Equals("[General]",StringComparison.OrdinalIgnoreCase);
            else if(general && Regex.Match(line,@"^version\s*=\s*(.{1,60})$",RegexOptions.IgnoreCase) is {Success:true} match)
                return match.Groups[1].Value.Trim();
        }
        return null;
    }

    internal static string? Stamp(string folder,string name,int length)
    {
        var path=Path.Combine(folder,name);
        if(!File.Exists(path)) return null;
        string text=File.ReadAllText(path).Trim();
        return text.Length==length && text.All(Uri.IsHexDigit) ? text : null;
    }

    public string CompareZip(CoopAddon addon,string zipPath)
    {
        using var zip=ZipFile.OpenRead(zipPath);
        string marker="gamedata/scripts/"+addon.Script;
        var roots=zip.Entries.Select(e=>e.FullName.Replace('\\','/'))
            .Where(n=>n.Equals(marker,StringComparison.OrdinalIgnoreCase) || n.EndsWith("/"+marker,StringComparison.OrdinalIgnoreCase)).ToList();
        if(roots.Count!=1) throw new InvalidDataException("Choose the "+addon.Name+" release ZIP.");
        if(Sha256!=null)
            return FileHasher.Compute(zipPath,"SHA256").Equals(Sha256,StringComparison.OrdinalIgnoreCase) ? "Matches release" : "Different release available";
        // Unmanaged MO2 installs have no archive receipt. Compare their game payload.
        var prefix=roots[0][..^marker.Length]+"gamedata/";
        var expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in zip.Entries)
        {
            var name=entry.FullName.Replace('\\','/');
            if(!name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) || name.EndsWith('/')) continue;
            var relative=name[prefix.Length..];
            if(relative.Split('/').Any(part=>part.Length==0 || part=="." || part==".." || part.IndexOfAny(Path.GetInvalidFileNameChars())>=0))
                throw new InvalidDataException("Invalid ZIP entry.");
            var file=Path.Combine(Folder,"gamedata",relative);
            expected.Add(Path.GetFullPath(file));
            if(!File.Exists(file) || new FileInfo(file).Length!=entry.Length) return "Installed files differ";
            using var input=entry.Open();
            if(!Convert.ToHexString(SHA256.HashData(input)).Equals(FileHasher.Compute(file,"SHA256"),StringComparison.OrdinalIgnoreCase)) return "Installed files differ";
        }
        var files=Directory.EnumerateFiles(Path.Combine(Folder,"gamedata"),"*",SearchOption.AllDirectories);
        return expected.SetEquals(files) ? "Matches release" : "Installed files differ";
    }
}
