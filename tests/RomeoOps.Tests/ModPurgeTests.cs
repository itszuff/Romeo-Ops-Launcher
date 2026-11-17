using RomeoOps.Installer;

namespace RomeoOps.Tests;

public sealed class ModPurgeTests : IDisposable
{
    private readonly string gamma = Directory.CreateTempSubdirectory("RomeoPurge").FullName;
    private string Mods => Path.Combine(gamma, "mods");
    private string List(string profile) => Path.Combine(gamma, "profiles", profile, "modlist.txt");

    public ModPurgeTests()
    {
        File.WriteAllText(Path.Combine(gamma, "ModOrganizer.ini"), "[Settings]\nmod_directory=%BASE_DIR%/mods");
        foreach (var name in new[] { "Solo", "Shared", "Active", "Unlisted", "UI_separator" })
            File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(Mods, name)).FullName, "data"), new byte[42]);
        foreach (var name in new[] { "ROMEO", "GAMMA" })
            Directory.CreateDirectory(Path.GetDirectoryName(List(name))!);
        File.WriteAllText(List("ROMEO"), "-solo\n-Shared\n+Active\n-Uninstalled\n-UI_separator\n-../profiles");
        File.WriteAllText(List("GAMMA"), "+SHARED\n-Solo");
    }

    [Fact]
    public void DefaultCleanupRemovesOnlyReviewedUnusedFolders()
    {
        var mods = ModPurge.Preview(gamma, "ROMEO", false);
        var only = Assert.Single(mods);
        Assert.Equal("Solo", only.Name);
        Assert.Equal(42L, only.Bytes);
        var original = File.ReadAllText(List("ROMEO"));
        Assert.Contains("Removed 1 of 1", ModPurge.Delete(gamma, "ROMEO", false, mods));
        Assert.False(Directory.Exists(Path.Combine(Mods, "Solo")));
        Assert.Equal(4, Directory.GetDirectories(Mods).Length);
        Assert.Equal(original, File.ReadAllText(List("ROMEO")));
        Assert.Empty(ModPurge.Preview(gamma, "ROMEO", false));
    }

    [Fact]
    public void SharedCleanupNamesAffectedProfileAndRechecksBeforeRemoving()
    {
        var mods = ModPurge.Preview(gamma, "ROMEO", true);
        Assert.Equal("GAMMA", Assert.Single(mods.Single(m => m.Name == "Shared").UsedBy));
        File.AppendAllText(List("ROMEO"), "\n+Solo");
        Assert.Contains("Removed 1 of 2", ModPurge.Delete(gamma, "ROMEO", true, mods));
        Assert.True(Directory.Exists(Path.Combine(Mods, "Solo")));
        Assert.False(Directory.Exists(Path.Combine(Mods, "Shared")));
    }

    [Fact]
    public void MissingProfileListBlocksDeletion()
    {
        var mods = ModPurge.Preview(gamma, "ROMEO", false);
        File.Delete(List("GAMMA"));
        Assert.Throws<FileNotFoundException>(() => ModPurge.Delete(gamma, "ROMEO", false, mods));
        Assert.True(Directory.Exists(Path.Combine(Mods, "Solo")));
    }

    public void Dispose() => Directory.Delete(gamma, true);
}
