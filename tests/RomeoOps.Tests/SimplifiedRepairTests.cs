using RomeoOps.Installer;

namespace RomeoOps.Tests;

public sealed class SimplifiedRepairTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "romeo-repair-" + Guid.NewGuid().ToString("N"));
    private InstallLayout Layout => InstallLayout.Create(Path.Combine(root, "Anomaly"), Path.Combine(root, "GAMMA"));

    public SimplifiedRepairTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData(PipelineMode.Install)]
    [InlineData(PipelineMode.Verify)]
    public void LegacyCheckpointRestartsRepairAndRetainsOriginalBackup(PipelineMode mode)
    {
        var layout = Layout;
        var oldRequest = new PipelineRequest { Mode = mode, AnomalyPath = layout.Anomaly,
            GammaPath = layout.Gamma, RomeoArchive = "romeo.7z" };
        var old = new SavedRun { Request = oldRequest, Done = { "GAMMA files", "ROMEO files", "xrRazom files" } };
        Directory.CreateDirectory(Path.GetDirectoryName(PlayerOptions.User(layout))!);
        File.WriteAllText(PlayerOptions.User(layout), "original settings");
        var backup = PlayerOptions.Capture(layout, old.Id);
        File.WriteAllText(PlayerOptions.User(layout), "interrupted install settings");

        var migrated = SavedRun.ForRequest(VerifyPlan.FromSettings(new AppSettings(), layout), old);

        Assert.Empty(migrated.Done);
        Assert.True(migrated.RefreshGammaResume);
        Assert.Equal(old.Id, migrated.Id);
        Assert.Equal(backup, PlayerOptions.Capture(layout, migrated.Id));
        Assert.Equal("original settings", File.ReadAllText(Path.Combine(backup, "user.ltx")));
        Assert.Same(migrated, SavedRun.ForRequest(migrated.Request, migrated));
    }

    [Fact]
    public void RepairCannotAdoptAnotherInstallationsCheckpoint()
    {
        var layout = Layout;
        var request = VerifyPlan.FromSettings(new AppSettings(), layout);
        var other = new SavedRun { Request = request with { AnomalyPath = Path.Combine(root, "other", "Anomaly") } };
        Assert.Throws<PipelineException>(() => SavedRun.ForRequest(request, other));
    }

    [Theory]
    [InlineData("Local")]
    [InlineData("Installed")]
    [InlineData("Download")]
    public void RepairUsesCliRegardlessOfInstallSources(string source)
    {
        var layout = Layout;
        var archive = Path.Combine(root, "romeo.7z");
        File.WriteAllText(archive, "archive");
        var settings = new AppSettings { AnomalySource = source, GammaSource = source,
            AnomalyZip = "old-anomaly.7z", GammaZip = "old-gamma.7z", RomeoArchive = archive,
            PreserveUser = false, PreserveMcm = true };
        var request = VerifyPlan.FromSettings(settings, layout);
        Assert.True(request.DownloadAnomaly);
        Assert.True(request.DownloadGamma);
        Assert.Null(request.AnomalyZip);
        Assert.Null(request.GammaZip);
        Assert.Equal(archive, request.RomeoArchive);
        Assert.False(request.PreserveUser);
        Assert.True(request.PreserveMcm);
        Assert.False(request.CreateSnapshots);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SettingsChoicesAreIndependentAndFailureRestoresBothOriginals(bool user, bool mcm)
    {
        var layout = Layout;
        Directory.CreateDirectory(Path.GetDirectoryName(PlayerOptions.User(layout))!);
        Directory.CreateDirectory(Path.GetDirectoryName(PlayerOptions.Override(layout))!);
        File.WriteAllText(PlayerOptions.User(layout), "original user");
        File.WriteAllText(PlayerOptions.Override(layout), "original mcm");
        var backup = PlayerOptions.Capture(layout, Guid.NewGuid());
        File.WriteAllText(PlayerOptions.User(layout), "CLI user");
        File.WriteAllText(PlayerOptions.Override(layout), "CLI mcm");

        PlayerOptions.Apply(layout, backup, user, mcm, success: true);
        Assert.Equal(user ? "original user" : Packaged("user.ltx"), File.ReadAllText(PlayerOptions.User(layout)));
        Assert.Equal(mcm ? "original mcm" : Packaged("axr_options.ltx"), File.ReadAllText(PlayerOptions.Override(layout)));

        PlayerOptions.Apply(layout, backup, user, mcm, success: false);
        Assert.Equal("original user", File.ReadAllText(PlayerOptions.User(layout)));
        Assert.Equal("original mcm", File.ReadAllText(PlayerOptions.Override(layout)));
    }

    [Fact]
    public void UnrelatedCliResumeFileIsNotUsed()
    {
        Directory.CreateDirectory(Layout.Gamma);
        var path = Path.Combine(Layout.Gamma, "resume.txt");
        File.WriteAllText(path, "# command: anomaly install\nComplete\told\n");
        Assert.False(VerifyResume.Prepare(Layout.Gamma, fresh: false));
        Assert.False(VerifyResume.Prepare(Layout.Gamma, fresh: true));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void RepairCanDetectFoldersWithMissingExecutablesAndProfile()
    {
        Directory.CreateDirectory(Layout.Anomaly);
        Directory.CreateDirectory(Layout.Gamma);
        Assert.Null(ExistingStack.Detect(root));
        var repair = ExistingStack.Detect(root, allowIncomplete: true);
        Assert.NotNull(repair);
        Assert.Equal(Layout.Anomaly, repair.Layout.Anomaly);
        Assert.Equal(Layout.Gamma, repair.Layout.Gamma);
    }

    private static string Packaged(string name)
    {
        using var stream = typeof(PlayerOptions).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
