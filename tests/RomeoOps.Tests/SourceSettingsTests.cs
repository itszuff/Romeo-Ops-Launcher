using RomeoOps;

namespace RomeoOps.Tests;

public class SourceSettingsTests
{
    [Fact]
    public void FreshSourcesUseExistingFoldersAndAutomaticCoopArchive()
    {
        var settings = new AppSettings { InstallFolder = @"C:\Games\Romeo" };
        Assert.Equal("Installed", settings.AnomalySource);
        Assert.Equal("Installed", settings.GammaSource);
        Assert.Equal("Download", settings.XrSource);
        Assert.False(settings.CoOp);
        Assert.Equal(@"C:\Games\Romeo\Anomaly", SetupGate.AnomalyFolder(settings));
        Assert.Equal(@"C:\Games\Romeo\GAMMA", SetupGate.GammaFolder(settings));
        settings.AnomalyPath = @"D:\ExistingAnomaly";
        Assert.Equal(settings.AnomalyPath, SetupGate.AnomalyFolder(settings));
    }

    [Fact]
    public void AutomaticCoopDoesNotRequireLocalZip()
    {
        var settings = new AppSettings { InstallFolder = @"C:\Games\Romeo", CoOp = true };
        Assert.DoesNotContain(SetupGate.Validate(settings), p => p.Contains("xrRazom"));
    }

    [Fact]
    public void LocalCoopRequiresDownloadedZipBeforeStarting()
    {
        var settings = new AppSettings { InstallFolder = @"C:\Games\Romeo", CoOp = true, XrSource = "Local" };
        Assert.Contains(SetupGate.Validate(settings), p => p.Contains("downloaded ZIP"));
        var file = Path.GetTempFileName();
        try
        {
            settings.XrArchive = file;
            Assert.Contains("The xrRazom archive must be a .zip file.", SetupGate.Validate(settings));
        }
        finally { File.Delete(file); }
    }
}
