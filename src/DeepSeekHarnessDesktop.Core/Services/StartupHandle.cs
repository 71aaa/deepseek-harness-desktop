using System.Diagnostics;

namespace DeepSeekHarnessDesktop.Core.Services;

/// <summary>本次启动流程的临时句柄：launcher 信息 + 取消源 + 启动前进程快照。</summary>
public sealed class StartupHandle
{
    public CancellationTokenSource? Cts { get; set; }
    public Process? LauncherProcess { get; set; }
    public int LauncherPid { get; set; }
    public long? LauncherStartTimeTicksUtc { get; set; }
    public DateTime LaunchedAtUtc { get; set; }
    public IReadOnlySet<int> PreExistingPids { get; set; } = new HashSet<int>();
}
