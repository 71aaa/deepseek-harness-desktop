using DeepSeekHarnessDesktop.Core.Services;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

/// <summary>PID + StartTime 所有权判定（纯模拟数据，不涉及真实进程）。</summary>
public class OwnershipValidatorTests
{
    private static readonly DateTime LaunchedAt = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static OwnershipEvidence Evidence(
        int pid = 300,
        bool startTimeUnknown = false,
        long? ticks = null,
        IReadOnlySet<int>? preExisting = null,
        bool page = true,
        bool launcherAlive = true,
        bool descendant = true)
        => new()
        {
            CandidatePid = pid,
            CandidateStartTimeTicksUtc = startTimeUnknown ? null : (ticks ?? LaunchedAt.AddSeconds(1).Ticks),
            LaunchedAtUtc = LaunchedAt,
            PreExistingPids = preExisting ?? new HashSet<int> { 100, 200 },
            PageIsHarness = page,
            LauncherAlive = launcherAlive,
            IsDescendantOfLauncher = descendant,
        };

    [Fact]
    public void Owned_WhenAllChecksPass()
    {
        Assert.True(OwnershipValidator.IsOwnedByDesktop(Evidence()));
    }

    [Fact]
    public void NotOwned_WhenPidExistedBeforeLaunch()
    {
        Assert.False(OwnershipValidator.IsOwnedByDesktop(Evidence(pid: 100)));
    }

    [Fact]
    public void NotOwned_WhenStartedBeforeLaunch()
    {
        Assert.False(OwnershipValidator.IsOwnedByDesktop(Evidence(ticks: LaunchedAt.Ticks)));
        Assert.False(OwnershipValidator.IsOwnedByDesktop(Evidence(ticks: LaunchedAt.AddSeconds(-1).Ticks)));
    }

    [Fact]
    public void NotOwned_WhenStartTimeUnknown()
    {
        // 无法读取创建时间 → 不可验证 → 判为非所有（安全优先）
        Assert.False(OwnershipValidator.IsOwnedByDesktop(Evidence(startTimeUnknown: true)));
    }

    [Fact]
    public void NotOwned_WhenPageIsNotHarness()
    {
        Assert.False(OwnershipValidator.IsOwnedByDesktop(Evidence(page: false)));
    }

    [Fact]
    public void NotOwned_WhenLauncherAliveButNotAncestor()
    {
        Assert.False(OwnershipValidator.IsOwnedByDesktop(Evidence(launcherAlive: true, descendant: false)));
    }

    [Fact]
    public void Owned_WhenLauncherDeadAndTimeChecksPass()
    {
        // launcher 已退出时无法做进程树检查，其余强证据通过即可
        Assert.True(OwnershipValidator.IsOwnedByDesktop(Evidence(launcherAlive: false, descendant: false)));
    }

    [Fact]
    public void NotOwned_WhenPidInvalid()
    {
        Assert.False(OwnershipValidator.IsOwnedByDesktop(Evidence(pid: 0)));
        Assert.False(OwnershipValidator.IsOwnedByDesktop(Evidence(pid: -1)));
    }
}
