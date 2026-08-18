namespace DeepSeekHarnessDesktop.Core.Models;

public enum HarnessSessionKind
{
    None = 0,
    /// <summary>本程序本次全新启动并确认为自己所有。</summary>
    FreshStarted = 1,
    /// <summary>上一次 Desktop 崩溃遗留、本次重新接管。</summary>
    AdoptedAfterCrash = 2,
    /// <summary>3080 上是外部 Harness，仅连接、不接管。</summary>
    External = 3,
}

/// <summary>一次已就绪的 Harness 会话（内存态）。</summary>
public sealed class HarnessSession
{
    public required HarnessSessionKind Kind { get; init; }
    public required string Url { get; init; }
    public required int Port { get; init; }
    public int? ListenerPid { get; init; }
    public long? ListenerStartTimeTicksUtc { get; init; }
    public bool OwnedByDesktop { get; init; }
    public HarnessRuntimeState? State { get; init; }
}
