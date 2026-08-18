using DeepSeekHarnessDesktop.Core.Models;

namespace DeepSeekHarnessDesktop.Core.Services;

public enum RecoveryDecision
{
    None = 0,
    /// <summary>确认是上一次 Desktop 遗留的 Harness，可重新接管（owned）。</summary>
    AdoptOwned = 1,
    /// <summary>是 Harness 但与本 Desktop 无关，按外部实例处理，绝不关闭。</summary>
    External = 2,
    /// <summary>状态文件已失效（对应进程不存在 / 版本不支持），可清理后全新启动。</summary>
    StaleState = 3,
}

/// <summary>
/// 崩溃恢复判定（纯函数，可测试）。
/// 只有 runtime.json 中的 PID + StartTime 与 3080 监听进程完全一致时才允许重新接管；
/// 任何不一致都降级为 External（绝不杀），安全优先于“自动清理”。
/// </summary>
public static class RecoveryLogic
{
    public static RecoveryDecision Decide(
        HarnessRuntimeState? state,
        int? listenerPid,
        long? listenerStartTimeTicksUtc,
        bool launcherAlive,
        bool launcherStartTimeMatches,
        bool listenerIsDescendantOfLauncher)
    {
        if (listenerPid is null)
            return state is null ? RecoveryDecision.None : RecoveryDecision.StaleState;

        if (state is null)
            return RecoveryDecision.External;

        if (state.SchemaVersion != 1)
            return RecoveryDecision.StaleState;

        // 上一轮未确认为本程序所有的记录，绝不自动接管。
        if (!state.OwnedByDesktop)
            return RecoveryDecision.External;

        if (state.Phase == HarnessRuntimePhase.Running)
        {
            return state.HarnessPid == listenerPid && state.HarnessStartTimeTicksUtc == listenerStartTimeTicksUtc
                ? RecoveryDecision.AdoptOwned
                : RecoveryDecision.External;
        }

        if (state.Phase == HarnessRuntimePhase.Starting)
        {
            // 上一次 Desktop 在启动阶段崩溃：launcher 仍活着且时间匹配，
            // 且 3080 监听进程晚于 launcher 启动、且是 launcher 的后代 → 可接管。
            bool adoptable = launcherAlive
                && launcherStartTimeMatches
                && listenerStartTimeTicksUtc is long t
                && state.LauncherStartTimeTicksUtc > 0
                && t > state.LauncherStartTimeTicksUtc
                && listenerIsDescendantOfLauncher;
            return adoptable ? RecoveryDecision.AdoptOwned : RecoveryDecision.External;
        }

        return RecoveryDecision.StaleState;
    }
}
