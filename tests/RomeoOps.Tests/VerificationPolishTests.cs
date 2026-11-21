using RomeoOps.Installer;

namespace RomeoOps.Tests;

public sealed class VerificationPolishTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "romeo-verify-" + Guid.NewGuid().ToString("N"));
    public VerificationPolishTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void FreshVerificationDropsOldProgressButResumeKeepsCurrentProgress()
    {
        var layout = InstallLayout.Create(Path.Combine(root, "Anomaly"), Path.Combine(root, "GAMMA"));
        Directory.CreateDirectory(layout.Gamma);
        Directory.CreateDirectory(layout.State);
        var resume = Path.Combine(layout.Gamma, "resume.txt");
        File.WriteAllText(resume, "# command: full-install\nComplete\told-mod\n");
        var request = new PipelineRequest { Mode = PipelineMode.Verify, RepairVersion = VerifyPlan.CurrentVersion, AnomalyPath = layout.Anomaly, GammaPath = layout.Gamma, RomeoArchive = "romeo.7z" };
        var run = SavedRun.ForRequest(request, null);
        VerifyResume.Prepare(layout.Gamma, run.RefreshGammaResume);
        Assert.Equal((0, 0), GammaResume.ReadProgress(layout.Gamma));
        run.RefreshGammaResume = false;
        run.Save(layout);
        File.WriteAllText(resume, "# command: full-install\nComplete\tcurrent-mod\n");
        var continued = SavedRun.ForRequest(request, SavedRun.Read(root));
        Assert.False(continued.RefreshGammaResume);
        VerifyResume.Prepare(layout.Gamma, continued.RefreshGammaResume);
        Assert.Equal((1, 1), GammaResume.ReadProgress(layout.Gamma));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetentionReusesChecksAndInvalidatesChangedFiles(bool changeTarget)
    {
        var source = Path.Combine(root, "source.7z");
        var downloads = Path.Combine(root, "downloads");
        File.WriteAllText(source, "original");
        var session = new ArchiveRetentionSession();
        Assert.True(await session.RetainAsync(source, downloads, "copy.7z", default));
        Assert.False(await session.RetainAsync(source, downloads, "copy.7z", default,
            (_, _) => throw new Exception("Unchanged archive was read again")));
        var changed = changeTarget ? Path.Combine(downloads, "copy.7z") : source;
        File.WriteAllText(changed, "modified");
        File.SetLastWriteTimeUtc(changed, DateTime.UtcNow.AddMinutes(1));
        await Assert.ThrowsAsync<PipelineException>(() => session.RetainAsync(source, downloads, "copy.7z", default));
    }

    [Fact]
    public void FinalValidationRejectsMissingEnabledModsButIgnoresDisabledMods()
    {
        var layout = InstallLayout.Create(Path.Combine(root, "Anomaly"), Path.Combine(root, "GAMMA"));
        Directory.CreateDirectory(layout.Anomaly);
        Directory.CreateDirectory(Path.GetDirectoryName(layout.Modlist)!);
        Directory.CreateDirectory(Path.Combine(layout.Gamma, "mods", Pins.SpeedFixName));
        File.WriteAllText(Path.Combine(layout.Anomaly, "AnomalyLauncher.exe"), "");
        File.WriteAllText(Path.Combine(layout.Gamma, "ModOrganizer.exe"), "");
        File.WriteAllLines(layout.Modlist, ["+" + Pins.SpeedFixName, "+Missing", "-Disabled"]);
        Assert.Contains("Missing", Assert.Throws<PipelineException>(() => InstallationValidation.Validate(layout, false)).Message);
        Directory.CreateDirectory(Path.Combine(layout.Gamma, "mods", "Missing"));
        InstallationValidation.Validate(layout, false);
    }

    [Fact]
    public async Task ConcurrentLogWritesAreComplete()
    {
        string path;
        using (var log = new InstallLog(_ => {}))
        {
            log.Open(root);
            path = log.FilePath!;
            await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
            {
                for (var i = 0; i < 200; i++) log.FileOnly($"{worker}:{i}");
            })));
        }
        var lines = File.ReadAllLines(path).Skip(1).ToArray();
        Assert.Equal(800, lines.Length);
        Assert.Equal(800, lines.Distinct().Count());
    }

}
