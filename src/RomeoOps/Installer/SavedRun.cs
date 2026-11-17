using System.Text.Json;

namespace RomeoOps.Installer;

public sealed class SavedRun
{
    public required PipelineRequest Request { get; init; }
    public HashSet<string> Done { get; init; } = new();
    public Guid Id { get; init; } = Guid.NewGuid();
    public bool RefreshGammaResume { get; set; }
    private static string Record(string root) => Path.Combine(root, "_state", "pending-run.json");
    public static SavedRun? Read(string root)
    {
        var file = Record(root);
        if (!Path.IsPathFullyQualified(root) || !File.Exists(file)) return null;
        var run = JsonSerializer.Deserialize<SavedRun>(File.ReadAllText(file)) ?? throw new IOException("Cannot read the saved run.");
        var layout = InstallLayout.Create(run.Request.AnomalyPath, run.Request.GammaPath);
        if (!string.Equals(InstallLayout.Normalize(root),layout.Root,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Saved progress belongs to a different installation directory.");
        return run;
    }
    public PipelineRequest ResumeInstall(AppSettings settings)
    {
        if (Request.Mode != PipelineMode.Install || !Request.InstallCoOp || !settings.CoOp || Done.Contains("xrRazom files"))
            return Request;
        return Request with { XrArchive = settings.XrSource == "Local" ? settings.XrArchive : null };
    }

    public static SavedRun ForRequest(PipelineRequest request, SavedRun? pending)
    {
        if (pending == null) return new SavedRun { Request = request };
        if (request == pending.Request) return pending;
        if (request.Mode == PipelineMode.Install && request.InstallCoOp && !pending.Done.Contains("xrRazom files") &&
            request == (pending.Request with { XrArchive = request.XrArchive }))
            return new SavedRun { Request = request, Id = pending.Id, Done = new(pending.Done) };
        if (request.Mode != PipelineMode.Verify || pending.Request.Mode != PipelineMode.Verify ||
            !string.Equals(request.AnomalyPath, pending.Request.AnomalyPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.GammaPath, pending.Request.GammaPath, StringComparison.OrdinalIgnoreCase))
            throw new PipelineException("Resume the saved operation from its original page.");

        // Recheck completed stages under the changed settings, retaining the original player backup.
        var previous = pending.Request;
        bool gammaChanged = request.VerifyCheckMd5 != previous.VerifyCheckMd5
            || request.VerifyForceGit != previous.VerifyForceGit || request.VerifyForceExtract != previous.VerifyForceExtract
            || request.VerifyLargeFiles != previous.VerifyLargeFiles || request.GammaZip != previous.GammaZip
            || request.DownloadGamma != previous.DownloadGamma;
        return new SavedRun { Request = request, Id = pending.Id, RefreshGammaResume = pending.RefreshGammaResume || gammaChanged };
    }

    public static FileStream Lock(InstallLayout layout)
    {
        Directory.CreateDirectory(layout.State);
        try { return new FileStream(Path.Combine(layout.State,"launcher.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("Another launcher operation is using this installation.",ex); }
    }
    public void Save(InstallLayout layout)
    {
        var path = Record(layout.Root);
        File.WriteAllText(path + ".new",JsonSerializer.Serialize(this));
        File.Move(path + ".new",path,true);
    }
    public async Task Step(string label, InstallLayout layout, Func<Task> action, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Done.Contains(label)) { log("Resumed past completed step: " + label); return; }
        await action().ConfigureAwait(false);
        Done.Add(label);
        Save(layout);
    }
    public void Finish(InstallLayout layout) => File.Delete(Record(layout.Root));
}
