namespace DeepSeekHarnessDesktop.Core.Logging;

/// <summary>空日志实现（日志文件初始化失败时的兜底，保证程序不因日志崩溃）。</summary>
public sealed class NullLogger : ILog
{
    public static readonly NullLogger Instance = new();

    private NullLogger() { }

    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
    public void Debug(string message) { }
}
