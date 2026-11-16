using System.IO;
using System.Text;

namespace RomeoOps.Installer;

public sealed class InstallLog : IDisposable
{
    private readonly Action<LogLine> _publish;
    private StreamWriter? _writer;

    public InstallLog(Action<LogLine> publish) => _publish = publish;

    public string? FilePath { get; private set; }

    public void Open(string logsDir)
    {
        Directory.CreateDirectory(logsDir);
        FilePath = Path.Combine(logsDir, "install-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
        _writer = new StreamWriter(FilePath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    public void Info(string message) => Write(message, LogKind.Info);
    public void Success(string message) => Write(message, LogKind.Success);
    public void Warning(string message) => Write(message, LogKind.Warning);
    public void Error(string message) => Write(message, LogKind.Error);
    public void Detail(string message) => Write(message, LogKind.Continuation);
    public void FileOnly(string message) => _writer?.WriteLine(message);

    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private void Write(string message, LogKind kind)
    {
        var line = new LogLine(DateTime.Now, message, kind);
        try { _publish(line); }
        catch { }
        _writer?.WriteLine(line.Display);
    }
}

public sealed class StateStore
{
    private readonly string _directory;

    public StateStore(string directory) => _directory = directory;

    public void Complete(string name)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, name + ".done"), DateTimeOffset.Now.ToString("o"), Encoding.ASCII);
    }
}
