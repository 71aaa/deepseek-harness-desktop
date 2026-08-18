using DeepSeekHarnessDesktop.Core.Services;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

/// <summary>
/// 单实例逻辑：Mutex 互斥 + 激活请求文件（临时目录内纯文件 IO 测试）。
/// 不涉及真实 Harness。
/// </summary>
public class SingleInstanceServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _requestPath;

    public SingleInstanceServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dshd-single-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _requestPath = Path.Combine(_dir, "activate.request");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>每个测试使用唯一的互斥名，避免与环境中已有的同名内核对象互相干扰（生产名不变）。</summary>
    private static string NewMutexName() => @"Local\DSHD.Test." + Guid.NewGuid().ToString("N");

    [Fact]
    public void FirstAcquireWins_SecondInstanceWritesRequestAndYields()
    {
        using var activated = new ManualResetEventSlim(false);
        var mutexName = NewMutexName();

        Assert.True(SingleInstanceService.TryAcquire(_requestPath, () => activated.Set(), mutexName, out var first));
        Assert.NotNull(first);

        Assert.False(SingleInstanceService.TryAcquire(_requestPath, null, mutexName, out var second));
        Assert.Null(second);

        // 第二个实例已写入激活请求文件
        Assert.True(File.Exists(_requestPath));

        // 第一个实例的轮询器应消费请求并回调（500ms 周期，5s 内必达）
        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(File.Exists(_requestPath));

        first!.Dispose(); // 清理，避免影响同类的后续测试
    }

    [Fact]
    public void StaleRequestFile_IsCleanedUpOnFirstAcquire()
    {
        File.WriteAllText(_requestPath, "stale");

        Assert.True(SingleInstanceService.TryAcquire(_requestPath, null, NewMutexName(), out var first));
        Assert.NotNull(first);

        Assert.False(File.Exists(_requestPath)); // 残留请求已被清理
        first!.Dispose();
    }

    [Fact]
    public void AfterDispose_MutexIsReleasable()
    {
        var mutexName = NewMutexName();
        Assert.True(SingleInstanceService.TryAcquire(_requestPath, null, mutexName, out var first));
        Assert.NotNull(first);

        first!.Dispose();

        // 释放后可再次成为第一个实例
        Assert.True(SingleInstanceService.TryAcquire(_requestPath, null, mutexName, out var third));
        Assert.NotNull(third);
        third!.Dispose();
    }

    [Fact]
    public void DoubleDisposeIsHarmless()
    {
        Assert.True(SingleInstanceService.TryAcquire(_requestPath, null, NewMutexName(), out var first));
        first!.Dispose();
        first.Dispose();
    }

    [Fact]
    public void MutexNameIsLocalSessionScoped()
    {
        Assert.StartsWith(@"Local\", SingleInstanceService.MutexName);
    }
}
