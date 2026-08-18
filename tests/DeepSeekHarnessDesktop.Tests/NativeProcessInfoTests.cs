using System.Diagnostics;
using DeepSeekHarnessDesktop.Core.Services;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

/// <summary>
/// 进程信息 API 冒烟测试：只读取“测试进程自身”的创建时间与父进程链，
/// 不查询、不触碰 3080 / Harness / 任何 node 进程。
/// </summary>
public class NativeProcessInfoTests
{
    [Fact]
    public void OwnProcessStartTimeIsSane()
    {
        var pid = Process.GetCurrentProcess().Id;
        var ticks = NativeProcessInfo.GetStartTimeTicksUtc(pid);

        Assert.NotNull(ticks);
        var start = new DateTime(ticks!.Value, DateTimeKind.Utc);
        Assert.True(start > DateTime.UtcNow.AddHours(-1));
        Assert.True(start <= DateTime.UtcNow);
    }

    [Fact]
    public void OwnProcessParentIsResolvable()
    {
        var pid = Process.GetCurrentProcess().Id;
        var parent = ProcessTreeHelper.GetParentPid(pid);

        Assert.NotNull(parent);
        Assert.True(parent!.Value > 0);
        Assert.True(ProcessTreeHelper.IsDescendantOf(pid, parent.Value));
    }

    [Fact]
    public void InvalidPidReturnsNull()
    {
        Assert.Null(NativeProcessInfo.GetStartTimeTicksUtc(0));
        Assert.Null(NativeProcessInfo.GetStartTimeTicksUtc(-5));
        Assert.Null(ProcessTreeHelper.GetParentPid(0));
        Assert.False(ProcessTreeHelper.IsDescendantOf(0, 1));
        Assert.False(ProcessTreeHelper.IsDescendantOf(1, 1));
    }

    [Fact]
    public void ToIsoUtc_RoundTripsExactly()
    {
        const long ticks = 638_000_000_000_000_000;
        var iso = NativeProcessInfo.ToIsoUtc(ticks);
        var back = DateTime.Parse(iso).ToUniversalTime().Ticks;
        Assert.Equal(ticks, back);
    }
}
