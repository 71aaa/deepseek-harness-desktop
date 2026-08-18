using DeepSeekHarnessDesktop.Core.Logging;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

/// <summary>文件日志：写入、目录自动创建、异常容忍、自动脱敏。</summary>
public class FileLoggerTests : IDisposable
{
    private readonly string _dir;

    public FileLoggerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dshd-logtest-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void CreatesDirectoryAndWritesLines()
    {
        var path = Path.Combine(_dir, "sub", "test.log");
        using (var logger = new FileLogger(path))
        {
            logger.Info("hello 中文");
            logger.Warn("warning line");
            logger.Error("boom", new InvalidOperationException("细节说明"));
        }

        var text = File.ReadAllText(path);
        Assert.Contains("INFO", text);
        Assert.Contains("WARN", text);
        Assert.Contains("ERROR", text);
        Assert.Contains("hello 中文", text);
        Assert.Contains("InvalidOperationException", text);
    }

    [Fact]
    public void RedactsSecretsInAllLines()
    {
        var path = Path.Combine(_dir, "redact.log");
        using (var logger = new FileLogger(path))
        {
            logger.Info("Authorization: Bearer sk-abcdef1234567890");
            logger.Error("failed", new Exception("token=supersecret"));
        }

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("sk-abcdef1234567890", text);
        Assert.DoesNotContain("supersecret", text);
        Assert.Contains("[REDACTED]", text);
    }

    [Fact]
    public void LoggingAfterDisposeIsSilent()
    {
        var path = Path.Combine(_dir, "dispose.log");
        var logger = new FileLogger(path);
        logger.Info("first");
        logger.Dispose();
        logger.Info("second"); // 不应抛出

        var text = File.ReadAllText(path);
        Assert.Contains("first", text);
        Assert.DoesNotContain("second", text);
    }
}
