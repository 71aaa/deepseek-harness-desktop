using System.Diagnostics;

namespace DeepSeekHarnessDesktop.Core.Services;

public sealed class HttpProbeResult
{
    public required bool HttpOk { get; init; }
    public string? Body { get; init; }
    public string? Error { get; init; }

    public bool LooksLikeHarness => HttpOk && HarnessPageDetector.LooksLikeHarnessPage(Body);
}

/// <summary>
/// 就绪探测循环：每 interval 探测一次，直到 Harness 页面特征命中或超时/提前退出。
/// 支持 CancellationToken；探测函数可注入（便于不接触真实 Harness 的单元测试）。
/// </summary>
public static class ReadinessProber
{
    public delegate Task<HttpProbeResult> ProbeAsync(Uri uri, CancellationToken ct);

    /// <summary>
    /// 返回最后一次探测结果：命中 Harness 页面时 LooksLikeHarness=true；
    /// 超时/earlyExit 时返回最后结果（可能为 null，供调用方区分超时原因）。
    /// </summary>
    public static async Task<HttpProbeResult?> WaitUntilReadyAsync(
        Uri uri,
        ProbeAsync probe,
        TimeSpan interval,
        TimeSpan timeout,
        Action<string>? statusCallback,
        Func<bool>? earlyExit,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        HttpProbeResult? last = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (sw.Elapsed >= timeout || (earlyExit?.Invoke() ?? false))
                return last;

            statusCallback?.Invoke(
                $"正在等待 DeepSeek Harness 就绪（已等待 {(int)sw.Elapsed.TotalSeconds} 秒 / 最长 {(int)timeout.TotalSeconds} 秒）…");

            try
            {
                last = await probe(uri, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // 端口未就绪等连接类异常是正常等待过程，继续重试。
            }

            if (last is { HttpOk: true, LooksLikeHarness: true })
                return last;

            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
        }
    }
}
