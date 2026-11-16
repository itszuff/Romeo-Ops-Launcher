using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RomeoOps.Installer;

namespace RomeoOps;

public partial class MainWindow
{
    private const long RepositoryId = 1460555611;
    private const string AssetName = "Romeo-Ops-Launcher.exe";
    private static readonly Version InstalledVersion = typeof(MainWindow).Assembly.GetName().Version!;
    private static readonly HttpClient Releases = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Romeo-Ops-Launcher/" + InstalledVersion.ToString(3) } }
    };
    private bool checkingRelease;
    private static bool IsPortableRelease => Convert.ToBoolean(AppContext.GetData("RomeoOps.PortableRelease"));
    private bool installingRelease;
    private bool restartingForUpdate;

    private async Task CheckLauncherUpdate(bool manual)
    {
        if (checkingRelease || operation != null) return;
        checkingRelease = true;
        Border? overlay = null;
        string? staged = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var release = JsonDocument.Parse(await Releases.GetStringAsync(
                $"https://api.github.com/repositories/{RepositoryId}/releases/latest", timeout.Token));
            if (!IsVisible || !IsEnabled || operation != null) return;
            var root = release.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v'), out var version) || tag != $"v{version}" ||
                version.Build < 0 || version.Revision >= 0)
                throw new InvalidDataException("Unsupported release version: " + tag);
            if (version <= InstalledVersion)
            {
                if (manual) MessageBox.Show(this, $"Romeo Ops Launcher {InstalledVersion.ToString(3)} is up to date.", "Launcher updates");
                return;
            }
            var asset = root.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == AssetName);
            if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var assetUri)
                || assetUri.Scheme != Uri.UriSchemeHttps || assetUri.Host != "github.com" || !assetUri.IsDefaultPort
                || !assetUri.AbsolutePath.EndsWith($"/releases/download/{tag}/{AssetName}", StringComparison.Ordinal))
                throw new InvalidDataException("GitHub returned an unexpected release download URL.");
            string url = assetUri.AbsoluteUri;
            string digest = asset.GetProperty("digest").GetString() ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(digest, @"^sha256:[A-Fa-f0-9]{64}$"))
                throw new InvalidDataException("GitHub has not provided a SHA-256 digest for this release.");
            if (MessageBox.Show(this, $"Romeo Ops Launcher {version} is available. Download it and restart now?",
                "Launcher update", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            installingRelease = true;
            Frame.IsEnabled = false;
            var label = new TextBlock { Text = "Downloading launcher update…", Margin = new Thickness(12), Foreground = Brushes.White };
            overlay = Put(Frame, new Border { Background = Ink, Child = label }, 170, 640, 540, 50);
            string target = Environment.ProcessPath!;
            staged = target + "." + Guid.NewGuid().ToString("N") + ".download";
            using var downloadTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            IProgress<TransferSnapshot> progress = new Progress<TransferSnapshot>(p => label.Text = "Updating · " + p.Format());
            await Downloader.DownloadAsync(Releases, url, staged, downloadTimeout.Token, null, progress.Report);
            using (var verified = File.OpenRead(staged))
                await VerifyDigest(verified, digest[7..]);
            var binary = FileVersionInfo.GetVersionInfo(staged);
            if (binary.ProductName != "Romeo Ops Launcher" || Version.Parse(binary.FileVersion!).ToString(3) != version.ToString())
                throw new InvalidDataException("The downloaded launcher version does not match the release tag.");
            SettingsStore.Save(settings);
            using var script = new StreamReader(typeof(MainWindow).Assembly.GetManifestResourceStream("FinishUpdate.ps1")!);
            var helper = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.SystemDirectory
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script.ReadToEnd() })
                helper.ArgumentList.Add(argument);
            helper.Environment["ROMEO_UPDATE_PID"] = Environment.ProcessId.ToString();
            helper.Environment["ROMEO_UPDATE_TARGET"] = target;
            helper.Environment["ROMEO_UPDATE_STAGE"] = staged;
            helper.Environment["ROMEO_UPDATE_FROM"] = InstalledVersion.ToString();
            helper.Environment["ROMEO_UPDATE_CWD"] = Environment.CurrentDirectory;
            helper.Environment["ROMEO_UPDATE_DIGEST"] = digest[7..];
            Process.Start(helper)!.Dispose();
            staged = null;
            restartingForUpdate = true;
            installingRelease = false;
            Close();
        }
        catch (Exception ex)
        {
            if ((manual || installingRelease) && IsVisible) MessageBox.Show(this, ex.Message, "Launcher update unavailable");
        }
        finally
        {
            checkingRelease = installingRelease = false;
            Frame.IsEnabled = true;
            if (overlay != null) Frame.Children.Remove(overlay);
            if (staged != null) try { File.Delete(staged); } catch (IOException) { }
        }
    }

    private static async Task VerifyDigest(Stream file, string expected)
    {
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded launcher failed its SHA-256 check.");
    }
}

