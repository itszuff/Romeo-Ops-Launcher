using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RomeoOps.Installer;

public static class RomeoDownloads
{
    private static readonly Regex UnsafeName = new(@"[^A-Za-z0-9._-]", RegexOptions.Compiled);
    private static readonly Regex SeparatorName = new(@"_separator$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Folder(string gamma) => Path.Combine(gamma, "downloads");

    public static string Ensure(string gamma, string cache)
    {
        var downloads = Folder(gamma);
        if (Directory.Exists(downloads))
            return downloads;
        if (File.Exists(downloads))
            throw new PipelineException(@"GAMMA\downloads exists and is not a folder.");

        Directory.CreateDirectory(cache);
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = "/c mklink /J \"" + downloads + "\" \"" + cache + "\""
        };
        using var process = Process.Start(start) ?? throw new PipelineException("Could not create the GAMMA downloads link.");
        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(downloads))
        {
            var error = process.StandardError.ReadToEnd().Trim();
            throw new PipelineException("Could not link GAMMA\\downloads to the cache. " + error);
        }

        return downloads;
    }

    public static async Task<bool> RetainAsync(string source, string downloads, string name,
        CancellationToken token, Action<string, TransferSnapshot>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(source)) throw new PipelineException("Source archive missing: " + source);
        Directory.CreateDirectory(downloads);
        var target = Path.Combine(downloads, name);
        if (SamePath(source, target)) return false;
        await using var input = OpenRead(source);
        if (File.Exists(target))
        {
            await using var existing = OpenRead(target);
            bool equal = input.Length == existing.Length;
            if (equal)
            {
                var sourceHash = await Hash(input, "Checking source archive");
                var targetHash = await Hash(existing, "Checking retained archive");
                equal = sourceHash.SequenceEqual(targetHash);
            }
            if (!equal) throw new PipelineException("A different or incomplete file already uses the archive name '" + target + "'. It was not overwritten.");
            return false;
        }

        var partial = target + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            progress?.Invoke("Copying archive to downloads", new(0, input.Length, 0));
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await Downloader.CopyWithProgress(input, output, input.Length, 0,
                    snap => progress?.Invoke("Copying archive to downloads", snap), token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            File.Move(partial, target);
            return true;
        }
        finally { File.Delete(partial); }

        async Task<byte[]> Hash(FileStream stream, string phase)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            var watch = Stopwatch.StartNew();
            var last = TimeSpan.Zero;
            long got = 0;
            progress?.Invoke(phase, new(0, stream.Length, 0));
            while (true)
            {
                int read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                got += read;
                if (watch.Elapsed - last >= TimeSpan.FromMilliseconds(200))
                {
                    progress?.Invoke(phase, new(got, stream.Length, got / Math.Max(watch.Elapsed.TotalSeconds, .001)));
                    last = watch.Elapsed;
                }
            }
            token.ThrowIfCancellationRequested();
            progress?.Invoke(phase, new(got, stream.Length, got / Math.Max(watch.Elapsed.TotalSeconds, .001)));
            return hash.GetHashAndReset();
        }
    }

    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
        128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public static string SnapshotFileName(string modName)
    {
        var safe = UnsafeName.Replace(modName, "_");
        if (safe.Length > 75)
            safe = safe.Substring(0, 75);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(modName)))[..10];
        return "ROMEO-" + Pins.RomeoRelease + "-" + safe + "-" + digest + ".7z";
    }

    public static bool IsSeparator(string modName) => SeparatorName.IsMatch(modName);

    public static string? ExistingInstaller(string modFolder, string downloads)
    {
        var meta = Path.Combine(modFolder, "meta.ini");
        if (!File.Exists(meta))
            return null;
        var match = Regex.Match(File.ReadAllText(meta, Encoding.UTF8), @"(?m)^installationFile=(.*)$");
        if (!match.Success)
            return null;
        var recorded = match.Groups[1].Value.Trim().Trim('"');
        if (recorded.Length == 0)
            return null;
        var path = Path.IsPathRooted(recorded) ? recorded : Path.Combine(downloads, recorded);
        return File.Exists(path) ? path : null;
    }

    public static void SetInstallationFile(string modFolder, string fileName, string backupDir)
    {
        Directory.CreateDirectory(modFolder);
        var meta = Path.Combine(modFolder, "meta.ini");
        if (!File.Exists(meta))
        {
            File.WriteAllText(meta, "[General]\r\ninstallationFile=" + fileName + "\r\n", new UTF8Encoding(false));
            return;
        }

        var original = File.ReadAllText(meta, Encoding.UTF8);
        string updated;
        if (Regex.IsMatch(original, @"(?m)^installationFile="))
            updated = Regex.Replace(original, @"(?m)^installationFile=.*$", _ => "installationFile=" + fileName);
        else if (Regex.IsMatch(original, @"(?m)^\[General\]"))
        {
            var general = Regex.Match(original, @"(?m)^\[General\]\r?\n");
            if (!general.Success)
                throw new PipelineException("Invalid meta.ini [General] section: " + meta);
            updated = original.Insert(general.Index + general.Length, "installationFile=" + fileName + "\r\n");
        }
        else
            updated = "[General]\r\ninstallationFile=" + fileName + "\r\n" + original;

        if (string.Equals(original, updated, StringComparison.Ordinal))
            return;
        Directory.CreateDirectory(backupDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        File.Copy(meta, Path.Combine(backupDir, "RomeoMod-Meta-" + stamp + ".bak"), overwrite: false);
        var bytes = File.ReadAllBytes(meta);
        var withBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        File.WriteAllText(meta, updated, new UTF8Encoding(withBom));
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
