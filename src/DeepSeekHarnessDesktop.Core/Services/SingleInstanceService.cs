namespace DeepSeekHarnessDesktop.Core.Services;

/// <summary>
/// 单实例服务：
/// - 命名 Mutex 保证只有一个 Desktop 实例（原子、跨进程可靠）；
/// - 第二个实例通过写入“激活请求文件”请求已有实例激活窗口（纯文件 IO，确定性、可测试）；
/// - 第一个实例用轻量 Timer 每 500ms 轮询请求文件，消费后回调。
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    public const string MutexName = @"Local\DeepSeekHarnessDesktop.SingleInstance.v1";
    public const string ActivationRequestFileName = "activate.request";

    private const int PollIntervalMs = 500;

    private readonly Mutex _mutex;
    private readonly Timer _activationTimer;
    private readonly string _requestFilePath;
    private readonly Action? _onActivationRequested;
    private volatile bool _disposed;

    private SingleInstanceService(Mutex mutex, string requestFilePath, Action? onActivationRequested)
    {
        _mutex = mutex;
        _requestFilePath = requestFilePath;
        // 回调在构造时注入；Timer 首次触发在启动之后，读取安全。
        _onActivationRequested = onActivationRequested;
        _activationTimer = new Timer(CheckRequest, null, PollIntervalMs, PollIntervalMs);
    }

    /// <summary>
    /// 尝试成为第一个实例。
    /// 返回 false 表示已有实例在运行：此时会写入激活请求文件（幂等），调用方应安全退出。
    /// onActivationRequested 在消费到激活请求时于线程池线程被调用（调用方负责调度回 UI 线程）。
    /// </summary>
    public static bool TryAcquire(string activationRequestFilePath, Action? onActivationRequested, out SingleInstanceService? instance)
        => TryAcquire(activationRequestFilePath, onActivationRequested, MutexName, out instance);

    /// <summary>测试专用重载：可注入唯一 Mutex 名，避免与环境中同名内核对象互相干扰。</summary>
    internal static bool TryAcquire(string activationRequestFilePath, Action? onActivationRequested, string mutexName, out SingleInstanceService? instance)
    {
        var mutex = new Mutex(false, mutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            instance = null;
            try
            {
                var dir = Path.GetDirectoryName(activationRequestFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(activationRequestFilePath, DateTime.UtcNow.ToString("o"));
            }
            catch { /* 激活请求只是辅助体验，失败不致命 */ }
            return false;
        }

        try
        {
            mutex.WaitOne(0); // AbandonedMutexException：上一实例异常退出，视为可继续接管
        }
        catch (AbandonedMutexException) { }

        // 清理可能残留的旧请求文件（例如上一个实例崩溃前留下的）
        try
        {
            if (File.Exists(activationRequestFilePath))
                File.Delete(activationRequestFilePath);
        }
        catch { }

        instance = new SingleInstanceService(mutex, activationRequestFilePath, onActivationRequested);
        return true;
    }

    private void CheckRequest(object? state)
    {
        if (_disposed) return;
        try
        {
            if (!File.Exists(_requestFilePath)) return;
            try { File.Delete(_requestFilePath); } catch { }
            _onActivationRequested?.Invoke();
        }
        catch
        {
            // 任何 IO 异常都不影响主流程
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _activationTimer.Dispose(); } catch { }
        try { _mutex.ReleaseMutex(); } catch { }
        try { _mutex.Dispose(); } catch { }
    }
}
