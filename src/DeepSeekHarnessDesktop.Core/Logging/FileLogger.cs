using System.Text;

namespace DeepSeekHarnessDesktop.Core.Logging;

/// <summary>
/// 追加写、多进程共享读的文件日志。所有输出自动脱敏；任何写失败都被吞掉（日志绝不拖垮主流程）。
/// </summary>
public sealed class FileLogger : ILog, IDisposable
{
    private readonly object _gate = new();
    private readonly string _filePath;
    private StreamWriter? _writer;
    private bool _disposed;

    public string FilePath => _filePath;

    public FileLogger(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        _writer = new StreamWriter(
            new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(false)) { AutoFlush = true };
    }

    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);
    public void Debug(string message) => Write("DEBUG", message, null);

    private void Write(string level, string message, Exception? exception)
    {
        if (_disposed) return;
        var text = message ?? "";
        var exText = exception is null ? "" : " :: " + exception;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {SecretRedactor.Redact(text)}{SecretRedactor.Redact(exText)}";
        lock (_gate)
        {
            if (_disposed || _writer is null) return;
            try { _writer.WriteLine(line); }
            catch { /* 日志写失败不影响主流程 */ }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _writer?.Flush(); _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }
}
