using RomeoOps.Installer;

namespace RomeoOps.Tests;

public sealed class CustomModsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "romeo-custom-mods-" + Guid.NewGuid().ToString("N"));
    private InstallLayout Layout => InstallLayout.Create(Path.Combine(root, "Anomaly"), Path.Combine(root, "GAMMA"));

    public CustomModsTests() => Directory.CreateDirectory(Path.GetDirectoryName(Layout.Modlist)!);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void RepairKeepsExtraStatesAndAnchorsWithoutOverridingBaseOrRequiredPriority()
    {
        var saved = Snapshot("# original", "+Extra A", "-Base A", "-Extra B", "+Base B", "+Extra C");
        Install("Extra A", "Extra B", "Extra C");
        Write("# repaired", "+" + Pins.XrModName, "+" + Pins.SpeedFixName, "+Base A", "-Base B", "+New base");
        CustomMods.Restore(Layout, saved, _ => Assert.Fail("Unexpected warning"));
        Assert.Equal(new[] { "# repaired", "+" + Pins.XrModName, "+" + Pins.SpeedFixName,
            "+Extra A", "+Base A", "-Extra B", "-Base B", "+New base", "+Extra C" }, File.ReadAllLines(Layout.Modlist));
        var first = File.ReadAllBytes(Layout.Modlist);
        CustomMods.Restore(Layout, saved, _ => Assert.Fail("Unexpected warning"));
        Assert.Equal(first, File.ReadAllBytes(Layout.Modlist));
    }

    [Fact]
    public void ReorderedBaseDoesNotReverseExtras()
    {
        var saved = Snapshot("+First extra", "+Base A", "+Second extra", "+Base B");
        Install("First extra", "Second extra");
        Write("+" + Pins.SpeedFixName, "+Base B", "+Base A");
        CustomMods.Restore(Layout, saved, _ => { });
        Assert.Equal(new[] { "+" + Pins.SpeedFixName, "+Base B", "+First extra", "+Second extra", "+Base A" }, File.ReadAllLines(Layout.Modlist));
    }

    [Fact]
    public void MissingExtrasRemainDisabledWithWarningAndOriginalBackup()
    {
        var saved = Snapshot("+Missing extra", "+Base", "-Already disabled", "-My group_separator");
        Write("+Base");
        var warnings = new List<string>();
        CustomMods.Restore(Layout, saved, warnings.Add);
        Assert.Equal(new[] { "-Missing extra", "+Base", "-Already disabled", "-My group_separator" }, File.ReadAllLines(Layout.Modlist));
        Assert.Equal(2, warnings.Count);
        Assert.Contains("+Missing extra", File.ReadAllLines(saved));
        Assert.True(Directory.Exists(Path.Combine(Layout.Gamma, "mods", "My group_separator")));
    }

    [Fact]
    public void SnapshotIsNotReplacedOnResumeOrAfterProfileWasInitiallyMissing()
    {
        var id = Guid.NewGuid();
        Write("+Original extra");
        var saved = CustomMods.Capture(Layout, id);
        Write("+Repaired base");
        Assert.Equal(saved, CustomMods.Capture(Layout, id));
        Assert.Equal(new[] { "+Original extra" }, File.ReadAllLines(saved));
        File.Delete(Layout.Modlist);
        var emptyId = Guid.NewGuid();
        var empty = CustomMods.Capture(Layout, emptyId);
        Write("+New profile");
        CustomMods.Capture(Layout, emptyId);
        Assert.Empty(File.ReadAllLines(empty));
    }

    [Fact]
    public void SnapshotFindsOriginalRomeoProfileEvenWhenExecutablesAreMissing()
    {
        var source = Path.Combine(Layout.Gamma, "profiles", "CLIKR - ROMEO original", "modlist.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "+Extra");
        Assert.Equal("+Extra", File.ReadAllText(CustomMods.Capture(Layout, Guid.NewGuid())));
    }

    [Fact]
    public void ManagedCoopModsAreNotReenabledWhenCoopIsOff()
    {
        var addon = CoopAddon.Catalog[0].Folder;
        var saved = Snapshot("+" + Pins.XrModName, "+" + addon, "+Renamed managed", "+Extra", "+Base");
        Install(Pins.XrModName, addon, "Renamed managed", "Extra");
        Directory.CreateDirectory(Layout.State);
        File.WriteAllText(Path.Combine(Layout.State, "meemaster-enabled.txt"), "Renamed managed");
        Write("+" + Pins.SpeedFixName, "+Base", "-" + Pins.XrModName);
        CustomMods.Restore(Layout, saved, _ => { });
        Assert.Equal(new[] { "+" + Pins.SpeedFixName, "+Extra", "+Base", "-" + Pins.XrModName }, File.ReadAllLines(Layout.Modlist));
    }

    [Fact]
    public void InvalidPathsAndDuplicateNamesCannotCreateEscapedOrDuplicateEntries()
    {
        var saved = Snapshot("+../outside", "+C:\\outside", "+..", "+Extra", "-EXTRA", "-base");
        Install("Extra");
        Write("+Base");
        CustomMods.Restore(Layout, saved, _ => { });
        Assert.Equal(new[] { "+Extra", "+Base" }, File.ReadAllLines(Layout.Modlist));
        Assert.False(Directory.Exists(Path.Combine(Layout.Gamma, "outside")));
    }

    private string Snapshot(params string[] lines)
    {
        Write(lines);
        return CustomMods.Capture(Layout, Guid.NewGuid());
    }

    private void Write(params string[] lines) => File.WriteAllLines(Layout.Modlist, lines);
    private void Install(params string[] names)
    {
        foreach (var name in names) Directory.CreateDirectory(Path.Combine(Layout.Gamma, "mods", name));
    }
}
