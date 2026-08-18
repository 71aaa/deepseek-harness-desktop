using System.Diagnostics;
using DeepSeekHarnessDesktop.Core.Services;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

/// <summary>就绪探测循环（注入假探测函数，不发任何真实 HTTP 请求）。</summary>
public class ReadinessProberTests
{
    private static readonly Uri ProbeUri = new("http://127.0.0.1:3080");

    [Fact]
    public async Task WaitsUntilHarnessPageAppears()
    {
        int calls = 0;
        var probe = new ReadinessProber.ProbeAsync((_, _) =>
        {
            calls++;
            return calls < 3
                ? Task.FromResult(new HttpProbeResult { HttpOk = false, Error = "refused" })
                : Task.FromResult(new HttpProbeResult { HttpOk = true, Body = "<html><script>window.__DSH_BOOT__ = {};</script></html>" });
        });

        var result = await ReadinessProber.WaitUntilReadyAsync(
            ProbeUri, probe, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(5), null, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.LooksLikeHarness);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task TimesOut_WhenNeverReady()
    {
        var probe = new ReadinessProber.ProbeAsync((_, _) =>
            Task.FromResult(new HttpProbeResult { HttpOk = false, Error = "refused" }));

        var sw = Stopwatch.StartNew();
        var result = await ReadinessProber.WaitUntilReadyAsync(
            ProbeUri, probe, TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(250), null, null, CancellationToken.None);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2));
        Assert.True(result is null || !result.LooksLikeHarness);
    }

    [Fact]
    public async Task ReturnsRespondingButNotHarnessPage_AtTimeout()
    {
        var probe = new ReadinessProber.ProbeAsync((_, _) =>
            Task.FromResult(new HttpProbeResult { HttpOk = true, Body = "hello world, not harness" }));

        var result = await ReadinessProber.WaitUntilReadyAsync(
            ProbeUri, probe, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(200), null, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.HttpOk);
        Assert.False(result.LooksLikeHarness); // 调用方据此区分“端口被别的程序占用”
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        var probe = new ReadinessProber.ProbeAsync(async (_, ct) =>
        {
            await Task.Delay(200, ct);
            return new HttpProbeResult { HttpOk = false, Error = "refused" };
        });
        using var cts = new CancellationTokenSource(150);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ReadinessProber.WaitUntilReadyAsync(
                ProbeUri, probe, TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(30), null, null, cts.Token));
    }

    [Fact]
    public async Task EarlyExit_StopsWaiting()
    {
        var probe = new ReadinessProber.ProbeAsync((_, _) =>
            Task.FromResult(new HttpProbeResult { HttpOk = false, Error = "refused" }));

        var sw = Stopwatch.StartNew();
        var result = await ReadinessProber.WaitUntilReadyAsync(
            ProbeUri, probe, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(30), null, () => true, CancellationToken.None);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Null(result);
    }

    [Fact]
    public async Task ConnectionErrorsAreSwallowedAndRetried()
    {
        int calls = 0;
        var probe = new ReadinessProber.ProbeAsync((_, _) =>
        {
            calls++;
            if (calls < 4) throw new HttpRequestException("connection refused");
            return Task.FromResult(new HttpProbeResult { HttpOk = true, Body = "<html>deepseek</html>" });
        });

        var result = await ReadinessProber.WaitUntilReadyAsync(
            ProbeUri, probe, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(5), null, null, CancellationToken.None);

        Assert.True(result!.LooksLikeHarness);
        Assert.Equal(4, calls);
    }
}
