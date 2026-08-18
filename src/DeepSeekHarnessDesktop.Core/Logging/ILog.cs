namespace DeepSeekHarnessDesktop.Core.Logging;

/// <summary>轻量日志接口。所有实现必须保证：日志失败绝不影响主流程。</summary>
public interface ILog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
    void Debug(string message);
}
