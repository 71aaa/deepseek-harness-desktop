namespace DeepSeekHarnessDesktop.Core.Models;

public enum HarnessRuntimePhase
{
    None = 0,
    /// <summary>launcher 已启动，Harness 尚未就绪（用于启动中途崩溃后的恢复）。</summary>
    Starting = 1,
    /// <summary>Harness 已就绪并完成所有权记录。</summary>
    Running = 2,
}

/// <summary>
/// runtime.json 的完整记录。绝不只存一个整数 PID：
/// 同时记录 launcher / Harness 的 PID + StartTime（FILETIME 精度 Ticks）+ 端口 + 命令 + 时间戳 + 所有权标记。
/// 不含任何 API Key / Token / Cookie / 会话内容。
/// </summary>
public sealed class HarnessRuntimeState
{
    public int SchemaVersion { get; set; } = 1;
    public HarnessRuntimePhase Phase { get; set; }
    public string SessionId { get; set; } = "";

    public int Port { get; set; }
    public string Command { get; set; } = "";
    public string Url { get; set; } = "";

    public int LauncherPid { get; set; }
    public long LauncherStartTimeTicksUtc { get; set; }
    public string? LauncherStartTimeIsoUtc { get; set; }
    public string? LauncherProcessName { get; set; }

    public int HarnessPid { get; set; }
    public long HarnessStartTimeTicksUtc { get; set; }
    public string? HarnessStartTimeIsoUtc { get; set; }
    public string? HarnessProcessName { get; set; }

    /// <summary>true 仅当已通过严格所有权验证（PID + StartTime + 进程树 + 页面特征）。</summary>
    public bool OwnedByDesktop { get; set; }

    public string? RecordedAtIsoUtc { get; set; }
    public string? AdoptedAtIsoUtc { get; set; }
}
