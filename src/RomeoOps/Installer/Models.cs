namespace RomeoOps.Installer;

public enum LogKind
{
    Info,
    Success,
    Warning,
    Error,
    Continuation
}

public enum PipelineMode
{
    Install,
    Verify
}

public sealed class LogLine
{
    public LogLine(DateTime time, string message, LogKind kind)
    {
        Time = time;
        Message = message;
        Kind = kind;
    }

    public DateTime Time { get; }
    public string Message { get; }
    public LogKind Kind { get; }

    public string Display => Kind == LogKind.Continuation
        ? "                  \\_ " + Message
        : $"[{Time:M/d/yy HH:mm:ss}] {Message}";
}

public sealed class ProgressReport
{
    public ProgressReport(string step, int percent, string detail)
    {
        Step = step;
        Percent = percent;
        Detail = detail;
    }

    public string Step { get; }
    public int Percent { get; }
    public string Detail { get; }
}

public sealed class PipelineException : Exception
{
    public PipelineException(string message) : base(message) { }
    public PipelineException(string message, Exception inner) : base(message, inner) { }
}

public sealed record PipelineRequest
{
    public required string AnomalyPath { get; init; }
    public required string GammaPath { get; init; }
    public required string RomeoArchive { get; init; }
    public string? AnomalyZip { get; init; }
    public string? GammaZip { get; init; }
    public string? XrArchive { get; init; }
    public bool DownloadAnomaly { get; init; } = true;
    public bool DownloadGamma { get; init; } = true;
    public bool InstallCoOp { get; init; } = true;
    public bool CreateSnapshots { get; set; } = true;
    public bool PreserveUser { get; set; } = true;
    public bool PreserveMcm { get; set; } = true;
    public bool VerifyCheckMd5 { get; init; } = true;
    public bool VerifyForceGit { get; init; }
    public bool VerifyForceExtract { get; init; }
    public bool VerifyLargeFiles { get; init; } = true;
    public PipelineMode Mode { get; init; }
}

public sealed record PipelineResult
{
    public bool Success { get; init; }
    public bool Cancelled { get; init; }
    public string? Error { get; init; }
    public string? LogFile { get; init; }
}

public sealed class PipelineCallbacks
{
    public Func<Task<string?>>? PickXrArchive { get; init; }
    public required Action<LogLine> Log { get; init; }
    public required Action<ProgressReport> Progress { get; init; }
    public required Func<string, bool> Confirm { get; init; }
}

