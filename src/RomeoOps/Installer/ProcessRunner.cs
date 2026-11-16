using System.Diagnostics;
using System.IO;
using System.Text;

namespace RomeoOps.Installer;

public sealed class ProcessRunner
{
    private long _lastLineTick;

    public async Task<int> RunAsync(
        string program,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken,
        Action<string>? onLine = null,
        IReadOnlyCollection<int>? allowedExitCodes = null,
        bool throwOnError = true,
        Action<TimeSpan>? onIdle = null,
        string? workingDirectory = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _lastLineTick, Environment.TickCount64);
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? "" : workingDirectory
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new PipelineException("Could not start " + program);

        var pumpOut = PumpAsync(process.StandardOutput, onLine);
        var pumpErr = PumpAsync(process.StandardError, onLine);
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var idle = onIdle == null ? Task.CompletedTask : WatchIdleAsync(idleCts.Token, onIdle);
        using var registration = cancellationToken.Register(() => KillTree(process));
        try
        {
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillTree(process);
                throw;
            }
        }
        finally
        {
            idleCts.Cancel();
            await Quiet(pumpOut).ConfigureAwait(false);
            await Quiet(pumpErr).ConfigureAwait(false);
            await Quiet(idle).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var code = process.ExitCode;
        var allowed = allowedExitCodes ?? new[] { 0 };
        if (throwOnError && !allowed.Contains(code))
        {
            throw new PipelineException(
                $"Command failed with exit code {code} : {Path.GetFileName(program)} {string.Join(' ', args)}");
        }

        return code;
    }

    private async Task PumpAsync(StreamReader reader, Action<string>? onLine)
    {
        var pending = new StringBuilder();
        var buffer = new char[512];
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                if (read == 0)
                    break;
                for (var i = 0; i < read; i++)
                {
                    var ch = buffer[i];
                    if (ch is '\r' or '\n')
                    {
                        if (pending.Length > 0)
                        {
                            Interlocked.Exchange(ref _lastLineTick, Environment.TickCount64);
                            try { onLine?.Invoke(pending.ToString()); } catch { }
                            pending.Clear();
                        }
                    }
                    else
                    {
                        pending.Append(ch);
                    }
                }
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        if (pending.Length > 0)
        {
            Interlocked.Exchange(ref _lastLineTick, Environment.TickCount64);
            try { onLine?.Invoke(pending.ToString()); } catch { }
        }
    }

    private async Task WatchIdleAsync(CancellationToken cancellationToken, Action<TimeSpan> onIdle)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var silent = TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastLineTick));
            if (silent >= TimeSpan.FromMinutes(2))
            {
                try { onIdle(silent); } catch { }
            }
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static async Task Quiet(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch { }
    }
}
