namespace DeepSeekHarnessDesktop.Core;

public enum HarnessStartFailureReason
{
    Unknown = 0,
    /// <summary>Node.js 缺失。</summary>
    EnvironmentMissing = 1,
    /// <summary>3080 被其他程序占用且不是 Harness。</summary>
    PortOccupiedByOtherProgram = 2,
    /// <summary>launcher 提前退出，Harness 未能启动。</summary>
    LaunchFailed = 3,
    /// <summary>规定时间内未就绪。</summary>
    TimedOut = 4,
    /// <summary>无法证明监听进程属于本程序（安全优先，停止加载）。</summary>
    OwnershipVerificationFailed = 5,
}

/// <summary>带用户友好中文提示的启动异常。UI 直接展示 FriendlyMessage，细节进日志。</summary>
public sealed class HarnessStartException : Exception
{
    public HarnessStartFailureReason Reason { get; }
    public string FriendlyMessage { get; }

    public HarnessStartException(HarnessStartFailureReason reason, string friendlyMessage, string? detail = null, Exception? inner = null)
        : base(detail ?? friendlyMessage, inner)
    {
        Reason = reason;
        FriendlyMessage = friendlyMessage;
    }
}
