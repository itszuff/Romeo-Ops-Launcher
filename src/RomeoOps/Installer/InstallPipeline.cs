using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace RomeoOps.Installer;

public sealed class InstallPipeline
{
    private static readonly Regex AnomalyProcessRegex = new(@"^Anomaly(DX\d+)?(AVX)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly int[] RobocopySuccess = { 0, 1, 2, 3, 4, 5, 6, 7 };

    private readonly PipelineCallbacks _callbacks;
    private readonly ProcessRunner _runner = new();
    private readonly CliProgress _cliProgress = new();
    private SavedRun _run = null!;
    private InstallLog _log = null!;
    private string _step = "";
    private string _detail = "";
    private int _rangeStart;
    private int _rangeEnd;
    private int _shownPercent;
    private bool _idleLogged;
    private string? _romeoSourceGamma;
    private long _lastDetailTick;
    private string[] _anomalyArgs = { "anomaly-install", "--use-anomaly-mirror", "--json" };
    private string[] _anomalyCheckArgs = { "anomaly", "check", "--json" };

    public InstallPipeline(PipelineCallbacks callbacks) => _callbacks = callbacks;

    public async Task<PipelineResult> RunAsync(PipelineRequest request, CancellationToken cancellationToken)
    {
        _log = new InstallLog(_callbacks.Log);
        try
        {
            await Task.Yield();
            var layout = InstallLayout.Create(request.AnomalyPath,request.GammaPath);
            using var gate = SavedRun.Lock(layout);
            var pending = SavedRun.Read(layout.Root);
            _run = SavedRun.ForRequest(request, pending);
            if (pending == null && request.Mode == PipelineMode.Install && ExistingStack.HasInstallation(layout))
                throw new PipelineException("An installation already exists here. Use Verify / Fix.");
            EnsureGameIsClosed();
            if (request.Mode == PipelineMode.Install || !string.IsNullOrEmpty(request.RomeoArchive))
            {
                RequireMachine();
                RequireRomeoArchive(request.RomeoArchive);
            }
            var backup = PlayerOptions.Capture(layout,_run.Id);
            _run.Save(layout);
            PipelineResult result;
            try
            {
                result = await RunCoreAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                PlayerOptions.Apply(layout,backup,request.PreserveUser,request.PreserveMcm,false);
                throw;
            }
            if (request.Mode == PipelineMode.Install)
                PlayerOptions.RecordFresh(layout,request.InstallCoOp,backup);
            PlayerOptions.Apply(layout,backup,request.PreserveUser,request.PreserveMcm,true,request.InstallCoOp);
            CoopAddon.Restore(layout, layout.Modlist, request.InstallCoOp ? null : []);
            _run.Finish(layout);
            return result with { LogFile = _log.FilePath };
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Stopped. Completed stages were saved.");
            _log.FileOnly("Completed stages are skipped the next time you run.");
            return new PipelineResult { Cancelled = true, Error = "Stopped. Use Resume to continue.", LogFile = _log.FilePath };
        }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
            _log.FileOnly(ex.ToString());
            _log.Info("Rerun the same operation after resolving the issue. Completed stages are skipped, and the GAMMA installer can resume.");
            return new PipelineResult { Error = ex.Message, LogFile = _log.FilePath };
        }
        finally
        {
            _log.Dispose();
        }
    }

    private async Task<PipelineResult> RunCoreAsync(PipelineRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var layout = InstallLayout.Create(request.AnomalyPath, request.GammaPath);

        bool profileOnly = request.Mode == PipelineMode.Verify && string.IsNullOrEmpty(request.RomeoArchive);
        if (!profileOnly) { RequireMachine(); RequireRomeoArchive(request.RomeoArchive); }
        if (!profileOnly && request.InstallCoOp && request.XrArchive != null && !File.Exists(request.XrArchive))
            throw new PipelineException("The xrRazom zip was not found. Switch xrRazom to Download, or choose the file again.");

        foreach (var dir in new[] { layout.Root, layout.Tools, layout.Staging, layout.Logs, layout.State, layout.Backups, layout.Cache })
            Directory.CreateDirectory(dir);
        _log.Open(layout.Logs);

        var mode = request.Mode;
        BeginStep(mode == PipelineMode.Verify ? "STEP 1 - CHECKING INSTALLATION" : "STEP 1 - PREFLIGHT", 0, 3);

        _log.Info(mode == PipelineMode.Verify
            ? "Initializing ROMEO verify..."
            : "Initializing ROMEO installation...");
        _log.Info("Detecting installation directories...");
        _log.Detail("Anomaly: " + layout.Anomaly + "\\");
        _log.Detail("GAMMA: " + layout.Gamma + "\\");
        _log.Detail(request.AnomalyZip != null ? "Anomaly source: " + request.AnomalyZip : request.DownloadAnomaly ? "Anomaly source: download" : "Anomaly source: installed folder");
        _log.Detail(request.GammaZip != null ? "GAMMA source: " + request.GammaZip : request.DownloadGamma ? "GAMMA source: download" : "GAMMA source: installed folder");
        _log.Detail(request.InstallCoOp
            ? (string.IsNullOrWhiteSpace(request.XrArchive) ? "xrRazom source: download" : "xrRazom source: local archive")
            : "Co-op: off");
        _log.Detail("Staging: " + layout.Staging + "\\");
        _log.Detail("Log: " + _log.FilePath);
        _log.Success("Installation directories verified.");
        EnsureGameIsClosed();

        if (profileOnly)
        {
            var detected = ExistingStack.Detect(layout.Root, allowIncomplete: true);
            if (detected != null && detected.CoOp != request.InstallCoOp)
                throw new PipelineException("Select the ROMEO .7z in Settings for a full repair to apply your xrRazom setting.");
            _log.Info("Checking the installed stack and repairing MO2 configuration using the files on disk.");
            await RepairProfileAsync(null, layout, request, cancellationToken).ConfigureAwait(false);
            var missing = File.ReadLines(layout.Modlist)
                .Where(line => line.StartsWith('+') && line.Length > 1)
                .Select(line => line[1..]).Where(name => !Directory.Exists(Path.Combine(layout.Gamma, "mods", name))).ToList();
            if (missing.Count > 0)
                throw new PipelineException("Profile repaired. Missing enabled mod folders: " + string.Join(", ", missing) + ". Select the ROMEO .7z for a full repair.");
            _log.Success("Installed executables, enabled mod folders and MO2 profile checked.");
            return new PipelineResult { Success = true };
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };

        BeginStep("STEP 2 - BOOTSTRAPPING TOOLS", 3, 8);
        var cli = await EnsureCliAsync(http, layout, cancellationToken).ConfigureAwait(false);
        var sevenZip = await EnsureSevenZipAsync(cli.Home, cancellationToken).ConfigureAwait(false);
        await ConfigureCliAsync(cli.Exe, layout, cancellationToken).ConfigureAwait(false);

        switch (mode)
        {
            case PipelineMode.Install:
                await _run.Step("Anomaly files",layout,async () =>
                {
                    if (request.AnomalyZip != null && !StageRules.AnomalyComplete(layout.State, layout.Anomaly))
                        await ImportGameArchiveAsync(sevenZip,request.AnomalyZip,layout,layout.Anomaly,"AnomalyLauncher.exe",cancellationToken);
                    await InstallAnomalyAsync(cli.Exe,layout,request.DownloadAnomaly,false,cancellationToken);
                },_log.Info,cancellationToken);
                await _run.Step("GAMMA files",layout,async () =>
                {
                    if (request.GammaZip != null && !StageRules.GammaComplete(layout.State, layout.Gamma))
                        await ImportGameArchiveAsync(sevenZip,request.GammaZip,layout,layout.Gamma,"ModOrganizer.exe",cancellationToken);
                    await InstallGammaAsync(cli.Exe,layout,request.DownloadGamma,false,cancellationToken);
                },_log.Info,cancellationToken);
                await _run.Step("ROMEO files",layout,() => ApplyRomeoAsync(sevenZip,layout,request.RomeoArchive,false,cancellationToken),_log.Info,cancellationToken);
                if (request.InstallCoOp)
                    await _run.Step("xrRazom files",layout,() => ApplyXrAsync(http,sevenZip,layout,request.XrArchive,false,cancellationToken),_log.Info,cancellationToken);
                else
                    _log.Info("Co-op is off. xrRazom will not be installed.");
                break;
            case PipelineMode.Verify:
                if (!File.Exists(Path.Combine(layout.Gamma, "ModOrganizer.exe")))
                    throw new PipelineException("GAMMA is not installed. Run Install before Verify.");
                await VerifyAsync(cli.Exe, http, sevenZip, layout, request, cancellationToken).ConfigureAwait(false);
                break;
        }

        await FinishAsync(cli.Exe, sevenZip, layout, request, cancellationToken).ConfigureAwait(false);
        Report(100, "");
        _log.Success(request.InstallCoOp
            ? "SUCCESS: Anomaly, GAMMA, ROMEO, and xrRazom stages completed."
            : "SUCCESS: Anomaly, GAMMA, and ROMEO stages completed.");
        _log.Info("Mod Organizer: " + Path.Combine(layout.Gamma, "ModOrganizer.exe"));
        _log.Info("MO2 profile: " + Pins.RomeoProfile);
        _log.Info(request.InstallCoOp
            ? "Priority: 1. " + Pins.XrModName + "; 2. " + Pins.SpeedFixName
            : "Priority: 1. " + Pins.SpeedFixName);
        _log.Info("Steam AppID override: 480 for every Mod Organizer executable.");
        _log.Info("Reusable installers: " + RomeoDownloads.Folder(layout.Gamma));
        _log.Info("ROMEO reinstall snapshots restore the installed files. They do not reproduce original FOMOD choices.");
        _log.Info(Pins.ManualStep);
        return new PipelineResult { Success = true };
    }

    private async Task ImportGameArchiveAsync(string sevenZip, string archive, InstallLayout layout, string destination, string marker, CancellationToken token)
    {
        var temp = Path.Combine(layout.Staging, "local-" + Guid.NewGuid().ToString("N"));
        _log.Info("Extracting local game archive: " + archive);
        try
        {
            string source = temp;
            if (archive.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractAsync(sevenZip, archive, temp, token).ConfigureAwait(false);
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
                var markers = Directory.EnumerateFiles(temp, marker, options).Take(2).ToArray();
                if (markers.Length != 1)
                    throw new InvalidDataException($"{Path.GetFileName(archive)} must contain one game folder with {marker}.");
                source = Path.GetDirectoryName(markers[0])!;
            }
            else
            {
                await LocalZip.UnpackAsync(archive, temp, marker, token).ConfigureAwait(false);
            }
            CloseOwnMo2(layout.Gamma);
            await CopyTreeAsync(source, destination, token).ConfigureAwait(false);
        }
        finally { CleanUpLocalArchive(temp); }
    }

    private void CleanUpLocalArchive(string temp)
    {
        try
        {
            try { TryDeleteDirectory(temp); }
            catch (UnauthorizedAccessException)
            {
                // Archives can contain read-only files. Only change staged entries, skipping links.
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = false
                };
                foreach (var entry in Directory.EnumerateFileSystemEntries(temp, "*", options))
                {
                    var attributes = File.GetAttributes(entry);
                    if (attributes.HasFlag(FileAttributes.ReadOnly))
                        File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                }
                TryDeleteDirectory(temp);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning($"Could not remove temporary extraction folder '{temp}': {ex.Message}");
        }
    }

    private static void RequireMachine()
    {
        if (!Environment.Is64BitOperatingSystem)
            throw new PipelineException("A 64-bit Windows installation is required.");
        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new PipelineException("Romeo Ops must be run as Administrator.");
    }

    private static void RequireRomeoArchive(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new PipelineException("The ROMEO archive was not found. Choose the local .7z and run again.");
    }

    private void EnsureGameIsClosed()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (AnomalyProcessRegex.IsMatch(process.ProcessName))
                    throw new PipelineException("Anomaly is running. Exit the game before changing ROMEO or xrRazom files, then run again.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void CloseOwnMo2(string gamma)
    {
        var prefix = gamma.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var process in Process.GetProcessesByName("ModOrganizer"))
        {
            try
            {
                string? path = null;
                try { path = process.MainModule?.FileName; } catch { }
                if (path == null || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                _log.Warning($"Closing Mod Organizer (PID {process.Id}) before modifying its profile.");
                try { process.CloseMainWindow(); } catch { }
                if (!process.WaitForExit(12_000))
                {
                    _log.Warning($"Mod Organizer did not exit cleanly. Stopping PID {process.Id}.");
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private async Task<(string Exe, string Home)> EnsureCliAsync(HttpClient http, InstallLayout layout, CancellationToken cancellationToken)
    {
        var home = Path.Combine(layout.Tools, "stalker-gamma-cli-" + Pins.CliVersion);
        var zip = Path.Combine(layout.Tools, Pins.CliAsset);
        var existing = ToolLocator.FindNamed(home, "stalker-gamma.exe");
        if (existing == null)
        {
            if (!File.Exists(zip))
            {
                _log.Info("Downloading stalker-gamma-cli " + Pins.CliVersion + ".");
                await Downloader.DownloadAsync(http, Pins.CliUrl, zip, cancellationToken, null, snap =>
                {
                    var pct = snap.Total is > 0 ? (int)(snap.Got * 100 / snap.Total.Value) : 0;
                    ReportChild(pct, snap.Format());
                }).ConfigureAwait(false);
            }

            FileHasher.AssertMatch(zip, "SHA256", Pins.CliHash);
            if (Directory.Exists(home))
                Directory.Delete(home, true);
            Directory.CreateDirectory(home);
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, home);
            existing = ToolLocator.FindNamed(home, "stalker-gamma.exe");
            if (existing == null)
                throw new PipelineException("No stalker-gamma.exe found in " + Pins.CliAsset);
        }

        _log.Success("stalker-gamma-cli " + Pins.CliVersion + " is ready.");
        await RunAsync(existing, new[] { "--version" }, cancellationToken).ConfigureAwait(false);
        var help = new StringBuilder();
        await _runner.RunAsync(existing, new[] { "--help" }, cancellationToken, line => help.AppendLine(line), throwOnError: false).ConfigureAwait(false);
        _anomalyArgs = GammaCommands.AnomalyInstallArgs(help.ToString()).Concat(new[] { "--json" }).ToArray();
        _anomalyCheckArgs = GammaCommands.AnomalyCheckArgs(help.ToString());
        _log.Detail("Anomaly command: " + string.Join(' ', _anomalyArgs));
        return (existing, home);
    }

    private async Task<string> EnsureSevenZipAsync(string cliHome, CancellationToken cancellationToken)
    {
        var found = ToolLocator.FindSevenZip(cliHome);
        if (found != null)
        {
            _log.Info("7-Zip: " + found);
            return found;
        }

        _log.Info("7-Zip was not found. Installing it with winget.");
        var winget = ToolLocator.FindOnPath("winget.exe");
        if (winget == null)
            throw new PipelineException("7-Zip was not found and winget is unavailable. Install 7-Zip, then run again.");
        await RunAsync(winget, new[]
        {
            "install", "--id", "7zip.7zip", "--exact", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"
        }, cancellationToken).ConfigureAwait(false);

        var expected = @"C:\Program Files\7-Zip\7z.exe";
        if (!File.Exists(expected))
            throw new PipelineException("winget reported success, but 7z.exe was not found in its expected location.");
        _log.Info("7-Zip: " + expected);
        return expected;
    }

    private async Task ConfigureCliAsync(string cli, InstallLayout layout, CancellationToken cancellationToken)
    {
        _log.Info("Configuring stalker-gamma-cli paths.");
        var create = new[]
        {
            "config", "create", "--name", Pins.CliProfile,
            "--anomaly", layout.Anomaly, "--gamma", layout.Gamma, "--cache", layout.Cache,
            "--download-threads", "4"
        };
        var code = await RunAsync(cli, create, cancellationToken, throwOnError: false).ConfigureAwait(false);
        if (code != 0)
        {
            _log.Warning("The CLI profile already exists. Recreating it so the selected folders are used.");
            await RunAsync(cli, new[] { "config", "delete", "--name", Pins.CliProfile }, cancellationToken, throwOnError: false).ConfigureAwait(false);
            await RunAsync(cli, new[] { "config", "delete", Pins.CliProfile }, cancellationToken, throwOnError: false).ConfigureAwait(false);
            await RunAsync(cli, create, cancellationToken).ConfigureAwait(false);
        }

        await RunAsync(cli, new[] { "config", "info" }, cancellationToken).ConfigureAwait(false);
    }

    private async Task InstallAnomalyAsync(string cli, InstallLayout layout, bool download, bool force, CancellationToken cancellationToken)
    {
        BeginStep(force ? "REPAIRING ANOMALY" : "STEP 3 - INSTALLING ANOMALY", 8, 30);
        var launcher = Path.Combine(layout.Anomaly, "AnomalyLauncher.exe");
        if (!download)
        {
            if (!File.Exists(launcher))
                throw new PipelineException("Anomaly download is turned off, and AnomalyLauncher.exe was not found. Choose Download, or point the folder at an existing Anomaly install.");
            if (!StageRules.AnomalyComplete(layout.State, layout.Anomaly))
                new StateStore(layout.State).Complete("01-anomaly");
            _log.Success("Using the installed Anomaly. Download skipped.");
            Report(_rangeEnd);
            return;
        }

        if (!force && StageRules.AnomalyComplete(layout.State, layout.Anomaly))
        {
            _log.Info("Stage 1/4: Anomaly 1.5.3 already completed. Skipping.");
            Report(_rangeEnd);
            return;
        }

        _log.Info("Installing Anomaly 1.5.3.");
        await RunAsync(cli, _anomalyArgs, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(Path.Combine(layout.Anomaly, "AnomalyLauncher.exe")))
            throw new PipelineException("Anomaly install returned success, but AnomalyLauncher.exe is missing.");
        new StateStore(layout.State).Complete("01-anomaly");
        _log.Success("Anomaly 1.5.3 installed.");
        Report(_rangeEnd);
    }

    private async Task InstallGammaAsync(string cli, InstallLayout layout, bool download, bool force, CancellationToken cancellationToken)
    {
        BeginStep(force ? "REPAIRING GAMMA" : "STEP 4 - INSTALLING GAMMA", 30, 78);
        var organizer = Path.Combine(layout.Gamma, "ModOrganizer.exe");
        if (!download)
        {
            if (!File.Exists(organizer))
                throw new PipelineException("GAMMA download is turned off, and ModOrganizer.exe was not found. Choose Download, or point the folder at an existing GAMMA install.");
            if (!StageRules.GammaComplete(layout.State, layout.Gamma))
                new StateStore(layout.State).Complete("02-gamma");
            _log.Success("Using the installed GAMMA. Download skipped.");
            Report(_rangeEnd);
            return;
        }

        if (!force && StageRules.GammaComplete(layout.State, layout.Gamma))
        {
            _log.Info("Stage 2/4: GAMMA already completed. Skipping.");
            Report(_rangeEnd);
            return;
        }

        EnsureFreeSpace(layout);
        CloseOwnMo2(layout.Gamma);
        var args = new List<string> { "full-install", "--use-anomaly-mirror", "--enable-long-paths", "--skip-extract-on-hash-match", "--json" };
        var resumePath = Path.Combine(layout.Gamma, "resume.txt");
        if (File.Exists(resumePath))
        {
            args.Add("--resume");
            var (done, total) = GammaResume.ReadProgress(layout.Gamma);
            if (total > 0)
            {
                _cliProgress.Seed(done, total);
                ReportChild((int)Math.Round(done / (double)total * 100d), $"Mods {done}/{total} already complete");
                _log.Info($"Resuming the previous GAMMA install ({done}/{total} mods already complete).");
            }
            else
            {
                _log.Info("Resuming the previous GAMMA install.");
            }
        }
        else
        {
            _log.Info("Installing GAMMA. Addon MD5 matches skip extraction.");
        }

        await RunAsync(cli, args, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(Path.Combine(layout.Gamma, "ModOrganizer.exe")))
            throw new PipelineException("GAMMA install returned success, but ModOrganizer.exe is missing.");
        new StateStore(layout.State).Complete("02-gamma");
        _log.Success("GAMMA installed.");
        Report(_rangeEnd);
    }

    private async Task VerifyAsync(
        string cli,
        HttpClient http,
        string sevenZip,
        InstallLayout layout,
        PipelineRequest request,
        CancellationToken cancellationToken)
    {
        var plan = new VerifyPlan(request.VerifyCheckMd5, request.VerifyForceGit, request.VerifyForceExtract, request.VerifyLargeFiles);
        foreach (var archive in new[] { request.AnomalyZip, request.GammaZip })
            if (archive != null && !File.Exists(archive))
                throw new PipelineException("Local game archive missing. Update the archive path in Settings: " + archive);
        if (request.AnomalyZip != null)
            await _run.Step("Local Anomaly files",layout,() => ImportGameArchiveAsync(sevenZip,request.AnomalyZip,layout,layout.Anomaly,"AnomalyLauncher.exe",cancellationToken),_log.Info,cancellationToken);
        if (request.GammaZip != null)
            await _run.Step("Local GAMMA files",layout,() => ImportGameArchiveAsync(sevenZip,request.GammaZip,layout,layout.Gamma,"ModOrganizer.exe",cancellationToken),_log.Info,cancellationToken);
        await _run.Step("Anomaly integrity",layout,
            () => CheckAnomalyAsync(cli, layout, request.DownloadAnomaly, request.InstallCoOp, cancellationToken),_log.Info,cancellationToken).ConfigureAwait(false);

        await _run.Step("GAMMA verification",layout,async () =>
        {
        if (plan.RunsInstaller)
        {
            BeginStep("VERIFYING GAMMA ADDONS", 30, 70);
            if (!plan.CheckMd5)
                _log.Warning("Check MD5 is off. stalker-gamma-cli still hashes an archive before it replaces that file.");
            else
                _log.Info("Check MD5 is on. Every addon archive is hashed. A mismatch is downloaded again.");
            _log.Info(plan.ForceGitDownload
                ? "Force git download is on. GitHub addon archives are downloaded again."
                : "Force git download is off. GitHub archives already on disk are kept, and the three setup repositories are left in place.");
            _log.Info(plan.ForceZipExtraction
                ? "Force zip extraction is on. Archives are extracted again."
                : "Force zip extraction is off. An archive whose MD5 matches is not extracted again.");
            _log.Info(plan.UpdateLargeFiles
                ? "Update large files is on. gamma_large_files is refreshed."
                : "Update large files is off. gamma_large_files is left in place.");

            var resume = VerifyResume.Prepare(layout.Gamma, plan.ReposToSkip(), _run.RefreshGammaResume);
            if (_run.RefreshGammaResume)
            {
                _run.RefreshGammaResume = false;
                _run.Save(layout);
            }
            CloseOwnMo2(layout.Gamma);
            var args = plan.FullInstallArgs();
            if (request.DownloadAnomaly)
                args.Insert(1, "--use-anomaly-mirror");
            if (resume)
            {
                args.Add("--resume");
                var (done, total) = GammaResume.ReadProgress(layout.Gamma);
                if (total > 0)
                {
                    _cliProgress.Seed(done, total);
                    ReportChild((int)Math.Round(done / (double)total * 100d), $"Mods {done}/{total} already complete");
                    _log.Info($"Resuming from GAMMA\\resume.txt ({done}/{total} mods already complete).");
                }
                else
                {
                    _log.Info("Resuming from GAMMA\\resume.txt.");
                }
            }

            await RunAsync(cli, args, cancellationToken).ConfigureAwait(false);
            _log.Success("GAMMA addon verification finished.");
        }
        else
        {
            _log.Info("Check MD5, Force git download, Force zip extraction, and Update large files are off. GAMMA addons are not downloaded.");
        }

        }, _log.Info,cancellationToken).ConfigureAwait(false);

        await _run.Step("ROMEO files",layout,() => ApplyRomeoAsync(sevenZip, layout, request.RomeoArchive, reapply: true, cancellationToken),_log.Info,cancellationToken).ConfigureAwait(false);
        if (request.InstallCoOp)
            await _run.Step("xrRazom files",layout,() => ApplyXrAsync(http, sevenZip, layout, request.XrArchive, reapply: true, cancellationToken),_log.Info,cancellationToken).ConfigureAwait(false);
        else
            _log.Info("Co-op is off. The repaired profile will have xrRazom disabled.");
    }

    private async Task CheckAnomalyAsync(string cli, InstallLayout layout, bool download, bool coop, CancellationToken cancellationToken)
    {
        BeginStep("CHECKING ANOMALY INTEGRITY", 8, 30);
        var launcher = Path.Combine(layout.Anomaly, "AnomalyLauncher.exe");
        if (!File.Exists(launcher))
        {
            if (!download)
                throw new PipelineException("AnomalyLauncher.exe was not found, and download is turned off. Choose Download on the Settings page, or run Install.");
            _log.Warning("Anomaly launcher is missing. Installing Anomaly.");
            await InstallAnomalyAsync(cli, layout, download: true, force: true, cancellationToken).ConfigureAwait(false);
            return;
        }

        _log.Info("Running the stalker-gamma-cli Anomaly integrity check.");
        var check = new AnomalyCheck();
        var code = await _runner.RunAsync(cli, _anomalyCheckArgs, cancellationToken, line =>
        {
            lock (check)
            {
                check.Observe(line);
                _log.FileOnly(line);
            }
        }, throwOnError: false).ConfigureAwait(false);

        if (code == 0)
        {
            _log.Success("Anomaly integrity check passed.");
            Report(_rangeEnd);
            return;
        }

        if (!check.IsCompletedMismatch(code))
            throw new PipelineException("Anomaly integrity check could not complete. " +
                (check.Error.Length > 0 ? check.Error : $"CLI exit code {code}. See the log for details."));

        var unexpected = new List<string>();
        foreach (var (path, status) in check.Differences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(layout.Anomaly, path);
            if (status == "Corrupt" && AnomalyCheck.KnownOverlay(layout.Anomaly, path, coop))
                _log.Info("Verified modded Anomaly file: " + relative);
            else
            {
                unexpected.Add(relative);
                _log.Warning($"Anomaly file {status}: {relative}");
            }
        }

        if (unexpected.Count == 0)
        {
            _log.Success("Anomaly integrity verified against stock and installed mod checksums.");
            Report(_rangeEnd);
            return;
        }

        if (!download) throw new PipelineException("Base Anomaly repair required: " + string.Join(", ", unexpected));
        _log.Warning($"Repairing Anomaly after {unexpected.Count} unrecognized or missing file(s).");
        await InstallAnomalyAsync(cli, layout, download: true, force: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyRomeoAsync(
        string sevenZip,
        InstallLayout layout,
        string archive,
        bool reapply,
        CancellationToken cancellationToken)
    {
        BeginStep(reapply ? "REAPPLYING ROMEO" : "STEP 5 - APPLYING ROMEO ARCHIVE", reapply ? 70 : 78, reapply ? 86 : 90);
        var downloads = RomeoDownloads.Ensure(layout.Gamma, layout.Cache);
        if (RomeoDownloads.Retain(archive, downloads, Pins.RomeoArchiveFile))
            _log.Info("Retained the ROMEO archive in GAMMA\\downloads.");

        if (StageRules.RomeoComplete(layout.State, reapply))
        {
            _log.Info("Stage 3/4: ROMEO already applied. Skipping.");
            Report(_rangeEnd);
            return;
        }

        EnsureGameIsClosed();
        CloseOwnMo2(layout.Gamma);
        var extract = Path.Combine(layout.Staging, "Romeo-2026-11-09-001");
        _log.Info("Applying ROMEO from " + Path.GetFileName(archive) + ".");
        await ExtractAsync(sevenZip, archive, extract, cancellationToken).ConfigureAwait(false);
        var root = ArchiveLayout.FindBundleRoot(extract, new[] { "GAMMA", "Anomaly" });
        _log.Info("Identified ROMEO archive root.");
        _log.Detail(root);
        await CopyTreeAsync(Path.Combine(root, "GAMMA"), layout.Gamma, cancellationToken).ConfigureAwait(false);
        await CopyTreeAsync(Path.Combine(root, "Anomaly"), layout.Anomaly, cancellationToken).ConfigureAwait(false);
        await EnsureRomeoProfileAsync(layout, refresh: true, cancellationToken).ConfigureAwait(false);
        _romeoSourceGamma = Path.Combine(root, "GAMMA");

        var count = ModlistEditor.CountEnabled(layout.Modlist);
        _log.Info($"ROMEO profile enabled entries before xrRazom: {count} (supplied expected count: {Pins.ExpectedRomeoMods}).");
        if (count != Pins.ExpectedRomeoMods)
            _log.Warning($"ROMEO profile has {count} enabled entries, not {Pins.ExpectedRomeoMods}. GAMMA and ROMEO versions may differ. Inspect Mod Organizer if this is unexpected.");

        ClearCaches(layout.Anomaly);
        new StateStore(layout.State).Complete("03-romeo");
        _log.Success("ROMEO applied.");
        Report(_rangeEnd);
    }

    private async Task ApplyXrAsync(
        HttpClient http,
        string sevenZip,
        InstallLayout layout,
        string? archive,
        bool reapply,
        CancellationToken cancellationToken)
    {
        BeginStep(reapply ? "REAPPLYING XRRAZOM" : "STEP 6 - INSTALLING XRRAZOM", reapply ? 86 : 90, 97);
        var downloads = RomeoDownloads.Ensure(layout.Gamma, layout.Cache);
        if (StageRules.XrComplete(layout.State, layout.XrModDir, reapply))
        {
            _log.Info("Stage 4/4: xrRazom already completed. Skipping.");
            Report(_rangeEnd);
            return;
        }

        await EnsureRomeoProfileAsync(layout, refresh: false, cancellationToken).ConfigureAwait(false);
        EnsureGameIsClosed();
        CloseOwnMo2(layout.Gamma);
        var zip = await GetXrArchiveAsync(http, layout, archive, cancellationToken).ConfigureAwait(false);
        if (RomeoDownloads.Retain(zip, downloads, Pins.XrFileName))
            _log.Info("Retained xrRazom in GAMMA\\downloads.");
        var extract = Path.Combine(layout.Staging, "xrRazom-1.5-Slim");
        await ExtractAsync(sevenZip, zip, extract, cancellationToken).ConfigureAwait(false);
        var root = ArchiveLayout.FindBundleRoot(extract, new[] { "Anomaly", "gamedata" });
        foreach (var sub in new[] { "bin", "gamedata" })
        {
            var folder = Path.Combine(root, "Anomaly", sub);
            if (!Directory.Exists(folder))
                throw new PipelineException(@"xrRazom archive is missing Anomaly\" + sub + ".");
        }

        var before = ModlistEditor.CountEnabled(layout.Modlist);
        _log.Info("MO2 enabled entries before xrRazom: " + before);
        RemoveLegacy(layout.Anomaly);
        if (Directory.Exists(layout.XrModDir))
        {
            RemoveLegacy(layout.XrModDir);
            var backup = Path.Combine(layout.Backups, "xrRazom-Co-op-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.Move(layout.XrModDir, backup);
            _log.Info("Previous xrRazom MO2 mod backed up.");
            _log.Detail(backup);
        }

        await CopyTreeAsync(Path.Combine(root, "Anomaly", "bin"), Path.Combine(layout.Anomaly, "bin"), cancellationToken).ConfigureAwait(false);
        await CopyTreeAsync(Path.Combine(root, "Anomaly", "gamedata"), Path.Combine(layout.Anomaly, "gamedata"), cancellationToken).ConfigureAwait(false);
        await CopyTreeAsync(Path.Combine(root, "gamedata"), Path.Combine(layout.XrModDir, "gamedata"), cancellationToken).ConfigureAwait(false);
        RemoveLegacy(layout.XrModDir);

        Directory.CreateDirectory(layout.Backups);
        var modlistBackup = Path.Combine(layout.Backups, "ROMEO-modlist-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        File.Copy(layout.Modlist, modlistBackup, overwrite: true);
        RequirePriorityMods(layout, Pins.PriorityMods);
        ModlistEditor.SetPriorityOrder(layout.Modlist, Pins.PriorityMods);

        var after = ModlistEditor.CountEnabled(layout.Modlist);
        _log.Info("MO2 enabled entries after xrRazom: " + after);
        if (after < before || after > before + 1)
            _log.Warning("Enabled count changed unexpectedly. Inspect the saved modlist before playing.");

        ClearCaches(layout.Anomaly);
        new StateStore(layout.State).Complete("04-xrrazom");
        TryDeleteDirectory(extract);
        _log.Success("xrRazom 1.5 Slim installed. Priority is xrRazom, then Teepo's Speed Fix.");
        Report(_rangeEnd);
    }

    private async Task<string> GetXrArchiveAsync(HttpClient http, InstallLayout layout, string? requested, CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(layout.Staging, Pins.XrFileName)
            : requested;
        var retained = Path.Combine(layout.Gamma, "downloads", Pins.XrFileName);
        if (requested == null && File.Exists(retained)) path = retained;
        if (File.Exists(path))
        {
            _log.Info("Using existing xrRazom archive.");
            _log.Detail(path);
            FileHasher.AssertMatch(path, "MD5", Pins.XrMd5);
            return path;
        }

        if (requested != null) throw new PipelineException("Selected xrRazom ZIP was not found: " + requested);

        _log.Info("Attempting the official ModDB download for xrRazom 1.5 Slim.");
        try
        {
            await ModDbRelease.FetchPinned(http, Pins.XrReferer, path, Pins.XrMd5, cancellationToken,
                snap =>
                {
                    var pct = snap.Total is > 0 ? (int)(snap.Got * 100 / snap.Total.Value) : 0;
                    ReportChild(pct, snap.Format());
                }).ConfigureAwait(false);
            return path;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && _callbacks.PickXrArchive != null &&
            ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _log.Warning("Automated xrRazom download failed. Select the browser-downloaded ZIP to continue this run. Completed stages are saved.");
            _log.Detail(ex.Message);
            var local = await _callbacks.PickXrArchive().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (local == null) throw new OperationCanceledException("Browser download cancelled.");
            FileHasher.AssertMatch(local, "MD5", Pins.XrMd5);
            _log.Info("Verified the selected xrRazom archive.");
            return local;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PipelineException(
                $"Automated ModDB download failed or returned an unexpected file. Download xrRazom-1.5-Slim.zip from {Pins.XrReferer}, select Settings > xrRazom > Local, choose the ZIP, then Resume Install. Completed stages are saved. Details: {ex.Message}",
                ex);
        }
    }

    private async Task FinishAsync(
        string cli,
        string sevenZip,
        InstallLayout layout,
        PipelineRequest request,
        CancellationToken cancellationToken)
    {
        BeginStep("FINAL MO2 REPAIRS", 97, 100);
        CloseOwnMo2(layout.Gamma);
        var downloads = RomeoDownloads.Ensure(layout.Gamma, layout.Cache);
        if (RomeoDownloads.Retain(request.RomeoArchive, downloads, Pins.RomeoArchiveFile))
            _log.Info("Retained the ROMEO archive in GAMMA\\downloads.");

        await EnsureRomeoProfileAsync(layout, refresh: false, cancellationToken).ConfigureAwait(false);
        await _run.Step("Reinstall archives",layout,async () =>
        {
            var extract = Path.Combine(layout.Staging,"Romeo-2026-11-09-001");
            var source = _romeoSourceGamma;
            bool create = request.Mode == PipelineMode.Install || request.CreateSnapshots;
            if (source == null && _run.Done.Contains("ROMEO files") && Directory.Exists(extract))
                source = Path.Combine(ArchiveLayout.FindBundleRoot(extract,new[] {"GAMMA","Anomaly"}),"GAMMA");
            if (source == null && create)
            {
                _log.Info("Reading the ROMEO archive to identify its mods.");
                await ExtractAsync(sevenZip,request.RomeoArchive,extract,cancellationToken,reportProgress:false).ConfigureAwait(false);
                source = Path.Combine(ArchiveLayout.FindBundleRoot(extract,new[] {"GAMMA","Anomaly"}),"GAMMA");
            }
            if (!create) _log.Info("Reusing available reinstall archives. Creating new snapshots is off.");
            if (source != null)
                await PreserveArchivesAsync(sevenZip,layout,source,downloads,cancellationToken,create).ConfigureAwait(false);
            TryDeleteDirectory(extract);
        }, _log.Info,cancellationToken).ConfigureAwait(false);

        await RepairProfileAsync(cli, layout, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task RepairProfileAsync(string? cli, InstallLayout layout, PipelineRequest request, CancellationToken cancellationToken)
    {
        CloseOwnMo2(layout.Gamma);
        await EnsureRomeoProfileAsync(layout, refresh: false, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(layout.Backups);
        File.Copy(layout.Modlist, Path.Combine(layout.Backups, "ROMEO-modlist-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"), overwrite: true);
        var priority = request.InstallCoOp ? Pins.PriorityMods : new[] { Pins.SpeedFixName };
        RequirePriorityMods(layout, priority);
        if (!request.InstallCoOp)
            ModlistEditor.Disable(layout.Modlist, Pins.XrModName);
        ModlistEditor.SetPriorityOrder(layout.Modlist, priority);
        if (cli != null) await SelectRomeoProfileAsync(cli, cancellationToken).ConfigureAwait(false);
        var ini = Path.Combine(layout.Gamma, "ModOrganizer.ini");
        Mo2Profile.SetSelectedProfile(ini, layout.Backups);
        var executables = Mo2Profile.SetSteamAppId(ini, layout.Backups);
        _log.Info("Steam AppID 480 verified for " + executables + " MO2 custom executables.");

        if (!File.Exists(Path.Combine(layout.Anomaly, "AnomalyLauncher.exe")))
            throw new PipelineException("Missing Anomaly launcher.");
        if (!File.Exists(Path.Combine(layout.Gamma, "ModOrganizer.exe")))
            throw new PipelineException("Missing Mod Organizer.");
        if (request.InstallCoOp && !Directory.Exists(Path.Combine(layout.XrModDir, "gamedata")))
            throw new PipelineException("Missing xrRazom MO2 mod.");
        if (!File.Exists(layout.Modlist))
            throw new PipelineException("ROMEO profile is missing.");

        if (request.InstallCoOp)
        {
            var top = ModlistEditor.TopMods(layout.Modlist, 2);
            if (top.Count != 2
                || !string.Equals(top[0], "+" + Pins.XrModName, StringComparison.Ordinal)
                || !string.Equals(top[1], "+" + Pins.SpeedFixName, StringComparison.Ordinal))
                throw new PipelineException("MO2 priority verification failed: xrRazom must be first and Teepo's Speed Fix second.");
        }
        else
        {
            var top = ModlistEditor.TopMods(layout.Modlist, 1);
            if (top.Count != 1 || !string.Equals(top[0], "+" + Pins.SpeedFixName, StringComparison.Ordinal))
                throw new PipelineException("MO2 priority verification failed: Teepo's Speed Fix must be first when co-op is off.");
        }
        _log.Success("Final verification passed.");
        Report(_rangeEnd);
    }

    private async Task EnsureRomeoProfileAsync(InstallLayout layout, bool refresh, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(layout.Gamma, "profiles", Pins.RomeoProfile);
        var source = refresh || !File.Exists(layout.Modlist) ? Mo2Profile.FindSource(layout.Gamma) : null;
        if (!Directory.Exists(destination))
        {
            if (source == null)
                throw new PipelineException("No ROMEO profile was found under " + Path.Combine(layout.Gamma, "profiles") + ". Check the archive folder names.");
            _log.Info("Creating ASCII-only MO2 profile '" + Pins.RomeoProfile + "'.");
            _log.Detail(Path.GetFileName(source));
            await CopyTreeAsync(source, destination, cancellationToken).ConfigureAwait(false);
        }
        else if ((refresh || !File.Exists(layout.Modlist)) && source != null)
        {
            _log.Info("Refreshing '" + Pins.RomeoProfile + "' from the original ROMEO profile.");
            _log.Detail(Path.GetFileName(source));
            await CopyTreeAsync(source, destination, cancellationToken).ConfigureAwait(false);
        }

        if (!File.Exists(layout.Modlist))
            throw new PipelineException("ASCII-safe ROMEO profile '" + Pins.RomeoProfile + "' has no modlist.txt.");
    }

    private async Task PreserveArchivesAsync(
        string sevenZip,
        InstallLayout layout,
        string sourceGamma,
        string downloads,
        CancellationToken cancellationToken, bool create)
    {
        var sourceMods = Path.Combine(sourceGamma, "mods");
        if (!Directory.Exists(sourceMods))
        {
            _log.Warning(@"ROMEO bundle has no GAMMA\mods folder. No individual mod archives were created.");
            return;
        }

        var mods = Directory.GetDirectories(sourceMods)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name) && !RomeoDownloads.IsSeparator(name!))
            .Cast<string>()
            .ToList();
        _log.Info("Preserving reinstall archives for " + mods.Count + " ROMEO mods in GAMMA\\downloads.");
        for (var i = 0; i < mods.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = mods[i];
            ReportChild((int)Math.Round((i + 1) * 100d / Math.Max(mods.Count, 1)), name);
            var installed = Path.Combine(layout.Gamma, "mods", name);
            if (!Directory.Exists(installed))
                continue;

            var existing = RomeoDownloads.ExistingInstaller(installed, downloads);
            if (existing != null)
            {
                var fileName = Path.GetFileName(existing);
                var inDownloads = Path.Combine(downloads, fileName);
                if (!string.Equals(Path.GetFullPath(existing), Path.GetFullPath(inDownloads), StringComparison.OrdinalIgnoreCase)
                    && RomeoDownloads.Retain(existing, downloads, fileName))
                    _log.Info("Retained installer archive in GAMMA downloads.");
                RomeoDownloads.SetInstallationFile(installed, fileName, layout.Backups);
                continue;
            }

            var hasPayload = Directory.EnumerateFileSystemEntries(installed)
                .Any(entry => !string.Equals(Path.GetFileName(entry), "meta.ini", StringComparison.OrdinalIgnoreCase));
            if (!hasPayload)
                continue;

            var snapshot = RomeoDownloads.SnapshotFileName(name);
            var destination = Path.Combine(downloads, snapshot);
            if (!File.Exists(destination))
            {
                if (!create) continue;
                _log.Info("Packing reinstall snapshot for ROMEO mod: " + name);
                var partial = Path.Combine(layout.Staging, snapshot + ".partial");
                if (File.Exists(partial))
                    File.Delete(partial);
                try
                {
                    await RunAsync(
                        sevenZip,
                        new[] { "a", "-t7z", "-mx=1", partial, "*", "-xr!meta.ini" },
                        cancellationToken,
                        workingDirectory: installed,
                        reportProgress: false,
                        announce: false).ConfigureAwait(false);
                    await RunAsync(sevenZip, new[] { "t", partial }, cancellationToken, reportProgress: false, announce: false).ConfigureAwait(false);
                    File.Move(partial, destination);
                }
                catch
                {
                    if (File.Exists(partial))
                    {
                        try { File.Delete(partial); } catch { }
                    }
                    throw;
                }
            }

            RomeoDownloads.SetInstallationFile(installed, snapshot, layout.Backups);
        }
    }

    private static void RequirePriorityMods(InstallLayout layout, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            var folder = Path.Combine(layout.Gamma, "mods", name);
            if (!Directory.Exists(folder))
                throw new PipelineException("Required MO2 mod '" + name + "' is not installed at '" + folder + "'.");
        }
    }

    private async Task SelectRomeoProfileAsync(string cli, CancellationToken cancellationToken)
    {
        await RunAsync(cli, new[] { "config", "set", "Mo2Profile", Pins.RomeoProfile }, cancellationToken).ConfigureAwait(false);
        await RunAsync(cli, new[] { "mo2", "config", "set", "selected-profile", Pins.RomeoProfile }, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureFreeSpace(InstallLayout layout)
    {
        var root = Path.GetPathRoot(layout.Gamma);
        if (string.IsNullOrEmpty(root))
            return;
        DriveInfo drive;
        try { drive = new DriveInfo(root); }
        catch (ArgumentException) { return; }
        if (!drive.IsReady)
            return;

        var gb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
        var letter = drive.Name.TrimEnd('\\');
        _log.Info($"{letter} has {gb:N1} GB free.");
        if (drive.AvailableFreeSpace >= Pins.MinimumFreeBytes)
            return;

        var ok = _callbacks.Confirm($"{letter} has {gb:N1} GB free; a full GAMMA install normally needs ~150 GB free. Continue anyway?");
        if (!ok)
            throw new PipelineException($"{letter} does not have enough free space for GAMMA.");
    }

    private async Task ExtractAsync(string sevenZip, string archive, string destination, CancellationToken cancellationToken, bool reportProgress = true)
    {
        if (!File.Exists(archive))
            throw new PipelineException("Archive not found: " + archive);
        TryDeleteDirectory(destination);
        Directory.CreateDirectory(destination);
        var target = destination.TrimEnd(Path.DirectorySeparatorChar);
        await RunArchivePhase("Testing", new[] { "t", "-bsp1", archive }, 0).ConfigureAwait(false);
        await RunArchivePhase("Extracting", new[] { "x", "-bsp1", archive, "-o" + target, "-y", "-aoa" }, 50).ConfigureAwait(false);

        async Task RunArchivePhase(string phase, string[] args, int start)
        {
            var label = phase + " " + Path.GetFileName(archive);
            _log.Info(label + ".");
            int lastPercent = -1;
            var lastLog = Stopwatch.StartNew();
            int lastLoggedPercent = 0;

            void Output(string line)
            {
                var text = line.Trim();
                var end = text.IndexOf('%');
                if (end < 1 || !int.TryParse(text.AsSpan(0, end), out int percent) || percent is < 0 or > 100)
                {
                    if (text.Length > 0) _log.Info(text);
                    return;
                }
                if (percent <= lastPercent) return;
                lastPercent = percent;
                var detail = $"{label} — {percent}%";
                if (reportProgress) ReportChild(start + percent / 2, detail);
                if (percent >= lastLoggedPercent + 5 || lastLog.Elapsed >= TimeSpan.FromSeconds(30))
                {
                    _log.Info(detail);
                    lastLoggedPercent = percent;
                    lastLog.Restart();
                }
            }

            await RunAsync(sevenZip, args, cancellationToken, output: Output,
                idle: silent =>
                {
                    if (lastLog.Elapsed < TimeSpan.FromMinutes(1)) return;
                    var last = lastPercent >= 0 ? $" Last reported progress was {lastPercent}%." : "";
                    _log.Info($"{label} — waiting for a progress update ({(int)silent.TotalMinutes} min).{last}");
                    lastLog.Restart();
                }).ConfigureAwait(false);
            _log.Info(label + " complete.");
        }
    }

    private async Task CopyTreeAsync(string from, string to, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(from))
            throw new PipelineException("Source folder missing: " + from);
        Directory.CreateDirectory(to);
        _log.Info("Copying files.");
        _log.Detail(from + "  ->  " + to);
        var robocopy = Path.Combine(Environment.SystemDirectory, "robocopy.exe");
        var destination = Path.GetFileName(Path.TrimEndingDirectorySeparator(to));
        var lastPercent = -1;
        var lastLoggedPercent = -10;
        var lastLog = Stopwatch.StartNew();
        void CopyOutput(string line)
        {
            _idleLogged = false;
            var match = Regex.Match(line, @"\b(?<percent>\d{1,3})%\s*$");
            if (!match.Success || !int.TryParse(match.Groups["percent"].Value, out var percent)
                || percent is < 0 or > 100)
            {
                if (!string.IsNullOrWhiteSpace(line)) _log.Info(line.Trim());
                return;
            }

            if (percent < lastPercent && percent <= 10) lastPercent = -1;
            if (percent <= lastPercent) return;
            lastPercent = percent;
            var detail = $"Copying files to {destination} - current file {percent}%";
            Report(_shownPercent, detail);
            if (percent >= lastLoggedPercent + 10 || lastLog.Elapsed >= TimeSpan.FromSeconds(30))
            {
                _log.Info(detail);
                lastLoggedPercent = percent;
                lastLog.Restart();
            }
        }

        void CopyIdle(TimeSpan silent)
        {
            var last = lastPercent >= 0 ? $" Last reported file progress was {lastPercent}%." : " Waiting for Robocopy output.";
            var detail = $"Still copying files to {destination}.{last}";
            _log.Info(detail);
            Report(_shownPercent, $"Copying files to {destination}. No progress output for {(int)silent.TotalMinutes} min.");
        }

        await RunAsync(
            robocopy,
            new[] { from, to, "/E", "/XJ", "/R:2", "/W:2", "/NFL", "/NDL" },
            cancellationToken,
            RobocopySuccess,
            output: CopyOutput,
            idle: CopyIdle).ConfigureAwait(false);
    }

    private void RemoveLegacy(string baseDir)
    {
        foreach (var relative in Pins.LegacyXrFiles)
        {
            var path = Path.Combine(baseDir, relative);
            if (!File.Exists(path))
                continue;
            _log.Info("Removing obsolete xrRazom file.");
            _log.Detail(path);
            File.Delete(path);
        }
    }

    private void ClearCaches(string anomaly)
    {
        var appdata = Path.Combine(anomaly, "appdata");
        if (!Directory.Exists(appdata))
            return;
        var shaders = Path.Combine(appdata, "shaders_cache");
        if (Directory.Exists(shaders))
        {
            _log.Info("Clearing shaders_cache.");
            Directory.Delete(shaders, recursive: true);
        }

        IEnumerable<string> atmos;
        try
        {
            atmos = Directory.EnumerateFiles(appdata, "*atmos*.ltx", SearchOption.AllDirectories).ToList();
        }
        catch (IOException)
        {
            return;
        }

        foreach (var file in atmos)
        {
            _log.Info("Deleting Atmos settings file.");
            _log.Detail(file);
            File.Delete(file);
        }
    }

    private async Task<int> RunAsync(
        string program,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken,
        IReadOnlyCollection<int>? allowed = null,
        bool throwOnError = true,
        string? workingDirectory = null,
        bool reportProgress = true,
        bool announce = true,
        Action<string>? output = null,
        Action<TimeSpan>? idle = null)
    {
        _idleLogged = false;
        var shown = Path.GetFileName(program) + " " + string.Join(' ', args.Select(Quote));
        if (announce)
            _log.Info("Running " + shown);
        _log.FileOnly("RUN: " + program + " " + string.Join(' ', args));
        return await _runner.RunAsync(
            program,
            args,
            cancellationToken,
            output ?? (reportProgress ? OnProcessLine : null),
            allowed,
            throwOnError,
            idle ?? OnIdle,
            workingDirectory).ConfigureAwait(false);
    }

    private void OnIdle(TimeSpan silent)
    {
        var minutes = Math.Max(2, (int)silent.TotalMinutes);
        if (!_idleLogged)
        {
            _idleLogged = true;
            _log.Warning("No installer output for " + minutes + " minutes. The current step may be waiting or taking longer than expected. Check the operation before cancelling.");
        }

        Report(_shownPercent, "No output for " + minutes + " min. Cancel and run Install again to resume.");
    }

    private void OnProcessLine(string line)
    {
        _idleLogged = false;
        var update = _cliProgress.Apply(line);
        if (update.LogText == null && !update.IsProgress)
            return;
        if (update.IsProgress)
        {
            ReportChild(update.Percent, update.Detail);
            if (update.LogText != null)
                _log.Info(update.LogText);
            else if (update.Detail != null && Environment.TickCount64 - _lastDetailTick > 1000)
            {
                _lastDetailTick = Environment.TickCount64;
                _log.FileOnly(update.Detail);
            }

            return;
        }

        WriteLog(update.Kind, update.LogText!);
    }

    private void WriteLog(LogKind kind, string message)
    {
        switch (kind)
        {
            case LogKind.Warning:
                _log.Warning(message);
                break;
            case LogKind.Error:
                _log.Error(message);
                break;
            default:
                _log.Info(message);
                break;
        }
    }

    private void BeginStep(string step, int start, int end)
    {
        _step = step;
        _detail = "";
        _rangeStart = start;
        _rangeEnd = end;
        if (_shownPercent < start)
            _shownPercent = start;
        _cliProgress.Reset();
        Report(_shownPercent);
    }

    private void Report(int percent, string? detail = null)
    {
        if (detail != null)
            _detail = detail;
        percent = Math.Clamp(percent, 0, 100);
        if (percent < _shownPercent)
            percent = _shownPercent;
        _shownPercent = percent;
        _callbacks.Progress(new ProgressReport(_step, percent, _detail));
    }

    private void ReportChild(int childPercent, string? detail = null)
    {
        var span = _rangeEnd - _rangeStart;
        var overall = _rangeStart + (int)Math.Round(span * (Math.Clamp(childPercent, 0, 100) / 100d));
        Report(Math.Clamp(overall, _rangeStart, _rangeEnd), detail);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;
        Directory.Delete(path, recursive: true);
    }

    private static string Quote(string arg) => arg.Any(char.IsWhiteSpace) ? "\"" + arg + "\"" : arg;
}


