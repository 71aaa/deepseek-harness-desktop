using DeepSeekHarnessDesktop.Core.Models;
using DeepSeekHarnessDesktop.Core.Services;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

/// <summary>崩溃恢复判定（纯模拟数据）。</summary>
public class RecoveryLogicTests
{
    private const long LauncherTicks = 600_000_000_000_000_000;
    private const long HarnessTicks = 600_000_000_000_001_000;

    private static HarnessRuntimeState RunningState(int pid, long ticks, bool owned = true) => new()
    {
        SchemaVersion = 1,
        Phase = HarnessRuntimePhase.Running,
        Command = @"dsh-runtime\node_modules\.bin\dsh.cmd web",
        OwnedByDesktop = owned,
        HarnessPid = pid,
        HarnessStartTimeTicksUtc = ticks,
        LauncherPid = pid - 1,
        LauncherStartTimeTicksUtc = LauncherTicks,
    };

    private static HarnessRuntimeState StartingState(int launcherPid) => new()
    {
        SchemaVersion = 1,
        Phase = HarnessRuntimePhase.Starting,
        Command = @"dsh-runtime\node_modules\.bin\dsh.cmd web",
        OwnedByDesktop = true,
        LauncherPid = launcherPid,
        LauncherStartTimeTicksUtc = LauncherTicks,
    };

    [Fact]
    public void RunningState_ExactPidAndTicksMatch_Adopts()
    {
        var decision = RecoveryLogic.Decide(RunningState(500, HarnessTicks), 500, HarnessTicks, false, false, false);
        Assert.Equal(RecoveryDecision.AdoptOwned, decision);
    }

    [Fact]
    public void RunningState_PidMatchesButTicksDiffer_TreatsAsExternal()
    {
        var decision = RecoveryLogic.Decide(RunningState(500, HarnessTicks), 500, HarnessTicks + 1, false, false, false);
        Assert.Equal(RecoveryDecision.External, decision);
    }

    [Fact]
    public void RunningState_DifferentPid_TreatsAsExternal()
    {
        var decision = RecoveryLogic.Decide(RunningState(500, HarnessTicks), 501, HarnessTicks, false, false, false);
        Assert.Equal(RecoveryDecision.External, decision);
    }

    [Fact]
    public void NoStateFile_TreatsAsExternal()
    {
        Assert.Equal(RecoveryDecision.External, RecoveryLogic.Decide(null, 500, HarnessTicks, false, false, false));
    }

    [Fact]
    public void NoListener_TreatsAsStaleState()
    {
        Assert.Equal(RecoveryDecision.StaleState, RecoveryLogic.Decide(RunningState(500, HarnessTicks), null, null, false, false, false));
        Assert.Equal(RecoveryDecision.None, RecoveryLogic.Decide(null, null, null, false, false, false));
    }

    [Fact]
    public void StartingState_LauncherAliveMatchedAndListenerIsDescendant_Adopts()
    {
        var decision = RecoveryLogic.Decide(StartingState(400), 500, HarnessTicks, true, true, true);
        Assert.Equal(RecoveryDecision.AdoptOwned, decision);
    }

    [Fact]
    public void StartingState_LauncherDead_TreatsAsExternal()
    {
        var decision = RecoveryLogic.Decide(StartingState(400), 500, HarnessTicks, false, false, false);
        Assert.Equal(RecoveryDecision.External, decision);
    }

    [Fact]
    public void StartingState_LauncherAliveButListenerNotDescendant_TreatsAsExternal()
    {
        var decision = RecoveryLogic.Decide(StartingState(400), 500, HarnessTicks, true, true, false);
        Assert.Equal(RecoveryDecision.External, decision);
    }

    [Fact]
    public void StartingState_ListenerStartedBeforeLauncher_TreatsAsExternal()
    {
        var decision = RecoveryLogic.Decide(StartingState(400), 500, LauncherTicks - 1, true, true, true);
        Assert.Equal(RecoveryDecision.External, decision);
    }

    [Fact]
    public void StateNotOwnedByDesktop_NeverAdopts()
    {
        var decision = RecoveryLogic.Decide(RunningState(500, HarnessTicks, owned: false), 500, HarnessTicks, false, false, false);
        Assert.Equal(RecoveryDecision.External, decision);
    }

    [Fact]
    public void UnsupportedSchemaVersion_TreatsAsStale()
    {
        var state = RunningState(500, HarnessTicks);
        state.SchemaVersion = 2;
        Assert.Equal(RecoveryDecision.StaleState, RecoveryLogic.Decide(state, 500, HarnessTicks, false, false, false));
    }

    [Fact]
    public void UnknownPhase_TreatsAsStale()
    {
        var state = RunningState(500, HarnessTicks);
        state.Phase = HarnessRuntimePhase.None;
        Assert.Equal(RecoveryDecision.StaleState, RecoveryLogic.Decide(state, 500, HarnessTicks, false, false, false));
    }
}
