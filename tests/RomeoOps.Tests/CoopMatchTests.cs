using System.IO.Compression;
using System.Text.Json;
using RomeoOps.Installer;

namespace RomeoOps.Tests;

public class CoopMatchTests : IDisposable
{
    readonly string root=Directory.CreateTempSubdirectory("romeo-match-").FullName;
    readonly InstallLayout layout;
    public CoopMatchTests()
    {
        layout=InstallLayout.Create(Path.Combine(root,"Anomaly"),Path.Combine(root,"GAMMA"));
        Directory.CreateDirectory(Path.GetDirectoryName(layout.Modlist)!);
        Directory.CreateDirectory(Path.Combine(layout.Gamma,"mods"));
    }
    public void Dispose()=>Directory.Delete(root,true);

    [Fact]
    public async Task LegacyExportMatchesAvailableProfileEntriesAndPreservesOtherLines()
    {
        foreach (string name in new[]{"Alpha","Beta","Extra"})
            Directory.CreateDirectory(Path.Combine(layout.Gamma,"mods",name));
        string[] original=["# header","+Alpha","*Managed","-Beta","+Extra","-separator"];
        File.WriteAllLines(layout.Modlist,original);
        var stack=new ExistingStack(layout,layout.Modlist,false);
        var path=Path.Combine(root,"setup.json");
        File.WriteAllText(path,"""{"Format":1,"XrVersion":"Disabled","EngineHash":"Missing","Addons":[],"LoadOrder":["beta","Alpha","Unavailable"]}""");
        var target=CoopSetup.Read(path);
        await CoopMatch.Apply(stack,target,(_,_,_)=>throw new Exception(),_=>{},default);
        Assert.Equal(new[]{"# header","+Beta","*Managed","+Alpha","-Extra","-separator"},File.ReadAllLines(layout.Modlist));
        Assert.Equal(original,File.ReadAllLines(Directory.GetFiles(layout.Backups).Single()));
        Assert.False(CoopMatch.Plan(stack,target).CanApply);
        Assert.Contains("Unavailable",CoopMatch.Plan(stack,target).Description);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyInstallsTheExportedRelease(bool correct)
    {
        var addon=CoopAddon.Catalog[0];
        Directory.CreateDirectory(Path.Combine(layout.XrModDir,"gamedata"));
        File.WriteAllText(layout.Modlist,"+"+Pins.XrModName+Environment.NewLine);
        var stack=new ExistingStack(layout,layout.Modlist,true);
        var archive=Path.Combine(root,"addon.zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
        {
            zip.CreateEntry("gamedata/scripts/"+addon.Script);
            using var writer=new StreamWriter(zip.CreateEntry("meta.ini").Open());
            writer.WriteLine("[General]\nversion=1.2");
        }
        var hash=correct ? FileHasher.Compute(archive,"SHA256") : new string('0',64);
        var target=CoopSetup.Capture(stack) with {LoadOrder=[addon.Folder,Pins.XrModName], Addons=[addon.Name],
            Releases=new(){[addon.Folder]=new("1.2",hash)} };
        Task Apply()=>CoopMatch.Apply(stack,target,(_,dest,_)=>{File.Copy(archive,dest);return Task.CompletedTask;},_=>{},default);
        if (!correct)
        {
            await Assert.ThrowsAsync<InvalidDataException>(Apply);
            Assert.False(Directory.Exists(Path.Combine(layout.Gamma,"mods",addon.Folder)));
            Assert.Equal(new[]{"+"+Pins.XrModName},File.ReadAllLines(layout.Modlist));
            return;
        }
        await Apply();
        var path=Path.Combine(root,"export.json");
        File.WriteAllText(path,JsonSerializer.Serialize(CoopSetup.Capture(stack)));
        var captured=CoopSetup.Read(path);
        Assert.Equal(target.Releases[addon.Folder],captured.Releases![addon.Folder]);
        Assert.Equal(target.LoadOrder,captured.LoadOrder);
        Assert.False(CoopMatch.Plan(stack,target).CanApply);
        Assert.Contains("Mod version",captured.Compare(target with {Releases=new(){[addon.Folder]=new("2.0",hash)}}));
    }
}
