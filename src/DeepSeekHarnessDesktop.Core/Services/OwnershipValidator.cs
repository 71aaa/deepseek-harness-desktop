namespace DeepSeekHarnessDesktop.Core.Services;

public sealed class OwnershipEvidence
{
    public required int CandidatePid { get; init; }
    public required long? CandidateStartTimeTicksUtc { get; init; }
    public required DateTime LaunchedAtUtc { get; init; }
    public required IReadOnlySet<int> PreExistingPids { get; init; }
    public required bool PageIsHarness { get; init; }
    public required bool LauncherAlive { get; init; }
    public required bool IsDescendantOfLauncher { get; init; }
}

/// <summary>
/// 所有权验证（纯函数，可测试）。安全优先：任何一条不满足都判为“非本程序所有”。
/// 1. 3080 页面必须是 Harness 页面；
/// 2. 监听 PID 不在启动前进程快照中（必须是新进程）；
/// 3. 监听进程创建时间严格晚于本次 Desktop 启动 Harness 的时刻（防 PID 复用）；
/// 4. 若 launcher 仍存活，监听进程必须是 launcher 进程树的后代。
/// </summary>
public static class OwnershipValidator
{
    public static bool IsOwnedByDesktop(OwnershipEvidence e)
    {
        if (!e.PageIsHarness) return false;
        if (e.CandidatePid <= 0) return false;
        if (e.PreExistingPids.Contains(e.CandidatePid)) return false;
        if (e.CandidateStartTimeTicksUtc is not long ticks || ticks <= e.LaunchedAtUtc.Ticks) return false;
        if (e.LauncherAlive && !e.IsDescendantOfLauncher) return false;
        return true;
    }
}
