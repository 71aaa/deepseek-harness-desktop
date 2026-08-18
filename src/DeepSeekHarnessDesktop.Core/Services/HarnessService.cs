using System.Diagnostics;
using System.Text;
using DeepSeekHarnessDesktop.Core.Logging;
using DeepSeekHarnessDesktop.Core.Models;

namespace DeepSeekHarnessDesktop.Core.Services;

/// <summary>
/// DeepSeek Harness 生命周期管理：
/// 静默启动（cmd → 本地 dsh.cmd → node）→ 就绪探测 → PID 定位 → 所有权验证
/// → runtime.json 持久化 → 崩溃恢复 → 安全关闭（严格 PID + StartTime 验证后才允许结束进程）。
/// </summary>
public sealed class HarnessService
{
    private readonly StateService _state;
    private readonly ILog _log;
    private readonly object _gate = new();

    private Task<HarnessSession>? _startTask;
    private StartupHandle? _startupHandle;
    private bool _stateWrittenThisRun;

    public HarnessSession? CurrentSession { get; private set; }

    public HarnessService(StateService stateService, ILog log)
    {
        _state = stateService;
        _log = log;
    }

    /// <summary>启动（或连接/接管已有）Harness。并发调用复用同一任务。</summary>
    public Task<HarnessSession> StartAsync(Action<string>? statusCallback = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_startTask is not null && !_startTask.IsCompleted)
            {
                _log.Debug("启动流程已在进行中，复用现有任务。");
                return _startTask;
            }
            _startTask = StartCoreAsync(statusCallback, cancellationToken);
            return _startTask;
        }
    }

    /// <summary>
    /// 关闭流程：
    /// 1) 若启动仍在进行 → 先取消并安全清理本次 launcher；
    /// 2) Owned 会话 → 重新验证 PID + StartTime + 端口对应关系 → 结束 Harness 进程树 → 清理 launcher → 等待端口释放 → 清理 runtime.json；
    /// 3) External 会话 → 只关闭 Desktop 自身，绝不结束外部 Harness。
    /// </summary>
    public async Task ShutdownAsync()
    {
        Task<HarnessSession>? startTask;
        StartupHandle? handle;
        lock (_gate)
        {
            startTask = _startTask;
            handle = _startupHandle;
        }

        if (startTask is not null && !startTask.IsCompleted)
        {
            _log.Info("启动尚未完成即收到关闭请求：先取消启动流程。");
            try { handle?.Cts?.Cancel(); } catch (ObjectDisposedException) { }
            try
            {
                await startTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (HarnessStartException ex) { _log.Warn($"启动流程以失败结束: {ex.FriendlyMessage}"); }
            catch (Exception ex) { _log.Error("启动流程以未预期异常结束", ex); }
        }

        var session = CurrentSession;
        if (session is null)
        {
            _log.Info("没有需要关闭的 Harness 会话。");
            return;
        }

        try
        {
            if (!session.OwnedByDesktop)
            {
                _log.Info($"3080 上的 Harness 不是本 Desktop 启动的（PID {session.ListenerPid?.ToString() ?? "?"}），只关闭 Desktop 自身。");
                if (_stateWrittenThisRun) _state.Clear();
                return;
            }
            await ShutdownOwnedAsync(session).ConfigureAwait(false);
        }
        finally
        {
            CurrentSession = null;
        }
    }

    // ============================ 启动主流程 ============================

    private async Task<HarnessSession> StartCoreAsync(Action<string>? statusCallback, CancellationToken cancellationToken)
    {
        _log.Info("===== Harness 启动流程开始 =====");
        _stateWrittenThisRun = false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = linked.Token;
        var handle = new StartupHandle
        {
            Cts = linked,
            LaunchedAtUtc = DateTime.UtcNow,
            PreExistingPids = SnapshotAllPids(),
        };
        _startupHandle = handle;

        try
        {
            // 1) 环境检测
            statusCallback?.Invoke("正在检测运行环境…");
            var env = EnvironmentService.Check();
            _log.Info($"环境检测: Node={(env.NodeFound ? env.NodePath : "未找到")}");
            if (!env.IsOk)
                throw new HarnessStartException(
                    HarnessStartFailureReason.EnvironmentMissing,
                    "未检测到 Node.js，无法启动 DeepSeek Harness。\n请先安装 Node.js（LTS 版本），安装完成后重新打开本程序。",
                    $"NodeFound={env.NodeFound}");

            // 2) 端口预检：3080 是否已被监听
            statusCallback?.Invoke("正在检查端口 3080…");
            var existing = PortProcessResolver.FindListener(AppConfig.HarnessPort);
            if (existing is not null)
            {
                _log.Info($"端口 {AppConfig.HarnessPort} 已被 PID {existing.OwningPid} 监听，识别页面内容…");
                bool pageIsHarness = await IsHarnessOnPortAsync(ct).ConfigureAwait(false);
                if (!pageIsHarness)
                {
                    _log.Error($"端口 {AppConfig.HarnessPort} 被其他程序占用（PID {existing.OwningPid}），且页面不是 DeepSeek Harness。");
                    throw new HarnessStartException(
                        HarnessStartFailureReason.PortOccupiedByOtherProgram,
                        "端口 3080 已被其他程序占用，DeepSeek Harness 无法启动。",
                        $"ListenerPid={existing.OwningPid}");
                }

                // 是 Harness：尝试按 runtime.json 恢复/复用
                var priorState = _state.Load();
                long? existingTicks = NativeProcessInfo.GetStartTimeTicksUtc(existing.OwningPid);
                bool launcherAlive = false, launcherMatch = false, isDescendant = false;
                if (priorState is not null && priorState.LauncherPid > 0)
                {
                    long? launcherTicks = NativeProcessInfo.GetStartTimeTicksUtc(priorState.LauncherPid);
                    launcherMatch = launcherTicks is long lt && priorState.LauncherStartTimeTicksUtc > 0 && lt == priorState.LauncherStartTimeTicksUtc;
                    launcherAlive = launcherMatch;
                    if (launcherAlive)
                        isDescendant = ProcessTreeHelper.IsDescendantOf(existing.OwningPid, priorState.LauncherPid);
                }

                var decision = RecoveryLogic.Decide(priorState, existing.OwningPid, existingTicks, launcherAlive, launcherMatch, isDescendant);
                _log.Info($"恢复判定: {decision}（状态文件存在={priorState is not null}）");

                if (decision == RecoveryDecision.AdoptOwned)
                {
                    _log.Info($"重新接管上一次 Desktop 遗留的 Harness：PID {existing.OwningPid}，StartTimeTicks={existingTicks}。");
                    var nowIso = DateTime.UtcNow.ToString("o");
                    var adopted = new HarnessRuntimeState
                    {
                        SchemaVersion = 1,
                        Phase = HarnessRuntimePhase.Running,
                        SessionId = priorState?.SessionId ?? Guid.NewGuid().ToString("N"),
                        Port = AppConfig.HarnessPort,
                        Command = AppConfig.HarnessLaunchCommand,
                        Url = AppConfig.HarnessUrl,
                        LauncherPid = priorState?.LauncherPid ?? 0,
                        LauncherStartTimeTicksUtc = priorState?.LauncherStartTimeTicksUtc ?? 0,
                        LauncherStartTimeIsoUtc = priorState?.LauncherStartTimeIsoUtc,
                        LauncherProcessName = priorState?.LauncherProcessName,
                        HarnessPid = existing.OwningPid,
                        HarnessStartTimeTicksUtc = existingTicks ?? 0,
                        HarnessStartTimeIsoUtc = existingTicks is long adoptedStart ? NativeProcessInfo.ToIsoUtc(adoptedStart) : null,
                        HarnessProcessName = SafeGetProcessName(existing.OwningPid),
                        OwnedByDesktop = true,
                        RecordedAtIsoUtc = nowIso,
                        AdoptedAtIsoUtc = nowIso,
                    };
                    _state.Save(adopted);
                    _stateWrittenThisRun = true;
                    CurrentSession = new HarnessSession
                    {
                        Kind = HarnessSessionKind.AdoptedAfterCrash,
                        Url = AppConfig.HarnessUrl,
                        Port = AppConfig.HarnessPort,
                        ListenerPid = existing.OwningPid,
                        ListenerStartTimeTicksUtc = existingTicks,
                        OwnedByDesktop = true,
                        State = adopted,
                    };
                    statusCallback?.Invoke("已连接到上一次启动的 DeepSeek Harness…");
                    return CurrentSession;
                }

                if (decision == RecoveryDecision.StaleState)
                {
                    _state.Clear();
                    _log.Info("状态文件对应的进程已不存在（陈旧记录），已清理，将全新启动。");
                }
                else
                {
                    _log.Info($"3080 上是外部 DeepSeek Harness（PID {existing.OwningPid}），Desktop 仅连接、不接管。");
                    CurrentSession = new HarnessSession
                    {
                        Kind = HarnessSessionKind.External,
                        Url = AppConfig.HarnessUrl,
                        Port = AppConfig.HarnessPort,
                        ListenerPid = existing.OwningPid,
                        ListenerStartTimeTicksUtc = existingTicks,
                        OwnedByDesktop = false,
                        State = null,
                    };
                    statusCallback?.Invoke("已连接到已在运行的 DeepSeek Harness…");
                    return CurrentSession;
                }
            }
            else
            {
                // 端口空闲：检查是否存在“仍在启动中”的上一次 launcher（Desktop 上次在启动阶段崩溃）
                var priorState = _state.Load();
                if (priorState is not null && priorState.SchemaVersion == 1 &&
                    priorState.Phase == HarnessRuntimePhase.Starting && priorState.LauncherPid > 0)
                {
                    long? launcherTicks = NativeProcessInfo.GetStartTimeTicksUtc(priorState.LauncherPid);
                    bool launcherAliveAndMatch = launcherTicks is long lt &&
                        priorState.LauncherStartTimeTicksUtc > 0 && lt == priorState.LauncherStartTimeTicksUtc;
                    if (launcherAliveAndMatch)
                    {
                        _log.Info($"上一次启动的 launcher 仍在运行（PID {priorState.LauncherPid}），接管并继续等待 Harness 就绪。");
                        handle.LauncherPid = priorState.LauncherPid;
                        handle.LauncherStartTimeTicksUtc = launcherTicks;
                        var adoptedStarting = await WaitAndVerifyAsync(
                            priorState, handle, statusCallback, ct,
                            new DateTime(priorState.LauncherStartTimeTicksUtc, DateTimeKind.Utc),
                            skipPreExistingCheck: true).ConfigureAwait(false);
                        return adoptedStarting;
                    }
                    _state.Clear();
                    _log.Info("状态文件中的 launcher 已不存在，视为陈旧记录并清理。");
                }
            }

            // 3) 全新启动
            statusCallback?.Invoke("正在启动 DeepSeek Harness 后台…");
            _log.Info("Starting local dsh runtime...");
            Process launcher = StartLauncherProcess(env.NodePath!);
            handle.LauncherProcess = launcher;
            handle.LauncherPid = launcher.Id;
            handle.LauncherStartTimeTicksUtc = NativeProcessInfo.GetStartTimeTicksUtc(launcher.Id);
            _log.Info($"launcher PID: {launcher.Id}，StartTimeTicks: {handle.LauncherStartTimeTicksUtc}，launchedAtUtc: {handle.LaunchedAtUtc:o}");

            var startingState = new HarnessRuntimeState
            {
                SchemaVersion = 1,
                Phase = HarnessRuntimePhase.Starting,
                SessionId = Guid.NewGuid().ToString("N"),
                Port = AppConfig.HarnessPort,
                Command = AppConfig.HarnessLaunchCommand,
                Url = AppConfig.HarnessUrl,
                LauncherPid = launcher.Id,
                LauncherStartTimeTicksUtc = handle.LauncherStartTimeTicksUtc ?? 0,
                LauncherStartTimeIsoUtc = handle.LauncherStartTimeTicksUtc is long launcherStart ? NativeProcessInfo.ToIsoUtc(launcherStart) : null,
                LauncherProcessName = SafeGetProcessName(launcher.Id),
                OwnedByDesktop = true,
                RecordedAtIsoUtc = DateTime.UtcNow.ToString("o"),
            };
            _state.Save(startingState);
            _stateWrittenThisRun = true;

            return await WaitAndVerifyAsync(startingState, handle, statusCallback, ct, handle.LaunchedAtUtc, skipPreExistingCheck: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log.Info("启动流程被取消，开始清理本次启动的 launcher。");
            await CancelAndCleanupAsync(handle).ConfigureAwait(false);
            _state.Clear();
            throw;
        }
        catch (HarnessStartException ex) when (ex.Reason is HarnessStartFailureReason.LaunchFailed or HarnessStartFailureReason.TimedOut)
        {
            await CancelAndCleanupAsync(handle).ConfigureAwait(false);
            _state.Clear();
            throw;
        }
        catch (HarnessStartException)
        {
            // PortOccupiedByOtherProgram / EnvironmentMissing / OwnershipVerificationFailed：
            // 不结束任何进程（安全优先）。
            throw;
        }
        finally
        {
            if (ReferenceEquals(_startupHandle, handle)) _startupHandle = null;
        }
    }

    /// <summary>等待 Harness 就绪，随后完成所有权验证并落盘状态。</summary>
    private async Task<HarnessSession> WaitAndVerifyAsync(
        HarnessRuntimeState startingState,
        StartupHandle handle,
        Action<string>? statusCallback,
        CancellationToken ct,
        DateTime launchedAtForVerification,
        bool skipPreExistingCheck)
    {
        var uri = new Uri(AppConfig.HarnessUrl);
        var last = await ReadinessProber.WaitUntilReadyAsync(
            uri,
            HarnessHttpProber.ProbeAsync,
            TimeSpan.FromMilliseconds(AppConfig.ReadyCheckIntervalMs),
            TimeSpan.FromSeconds(AppConfig.ReadyTimeoutSeconds),
            statusCallback,
            () => handle.LauncherPid > 0 && !IsProcessAlive(handle.LauncherPid),
            ct).ConfigureAwait(false);

        if (last?.LooksLikeHarness == true)
            return await VerifyAndFinishAsync(startingState, handle, statusCallback, launchedAtForVerification, skipPreExistingCheck).ConfigureAwait(false);

        if (handle.LauncherPid > 0 && !IsProcessAlive(handle.LauncherPid))
        {
            AppendLogTail(AppConfig.HarnessErrorLogPath);
            _log.Error($"launcher 进程（PID {handle.LauncherPid}）已提前退出，Harness 未能启动。");
            throw new HarnessStartException(
                HarnessStartFailureReason.LaunchFailed,
                "无法启动 DeepSeek Harness 后台进程。\n请点击“打开日志文件夹”查看详细原因。",
                $"LauncherPid={handle.LauncherPid}");
        }

        if (last is { HttpOk: true })
        {
            var foreign = PortProcessResolver.FindListener(AppConfig.HarnessPort);
            _log.Error($"等待超时前端口 {AppConfig.HarnessPort} 有响应，但不是 DeepSeek Harness 页面。");
            throw new HarnessStartException(
                HarnessStartFailureReason.PortOccupiedByOtherProgram,
                "端口 3080 已被其他程序占用，DeepSeek Harness 无法启动。",
                $"ListenerPid={foreign?.OwningPid}");
        }

        _log.Error($"等待 {AppConfig.ReadyTimeoutSeconds} 秒后 Harness 仍未就绪。");
        throw new HarnessStartException(
            HarnessStartFailureReason.TimedOut,
            "未能在规定时间内启动 Harness。\n请点击“打开日志文件夹”查看详细原因。");
    }

    /// <summary>页面已就绪：定位 3080 监听 PID，完成所有权验证，写 Running 状态。</summary>
    private Task<HarnessSession> VerifyAndFinishAsync(
        HarnessRuntimeState startingState,
        StartupHandle handle,
        Action<string>? statusCallback,
        DateTime launchedAtForVerification,
        bool skipPreExistingCheck)
    {
        statusCallback?.Invoke("DeepSeek Harness 已响应，正在验证进程…");
        var listener = PortProcessResolver.FindListener(AppConfig.HarnessPort);
        if (listener is null)
        {
            _log.Error("Harness 页面已响应，但未能解析 3080 的监听进程，无法完成所有权验证。");
            throw new HarnessStartException(
                HarnessStartFailureReason.OwnershipVerificationFailed,
                "无法确认 DeepSeek Harness 进程的所有权，已停止加载。\n请点击“打开日志文件夹”查看详情。");
        }

        long? listenerTicks = NativeProcessInfo.GetStartTimeTicksUtc(listener.OwningPid);
        bool launcherAlive = handle.LauncherPid > 0 && IsProcessAlive(handle.LauncherPid);
        bool isDescendant = false;
        if (launcherAlive)
            isDescendant = ProcessTreeHelper.IsDescendantOf(listener.OwningPid, handle.LauncherPid);

        var preExisting = skipPreExistingCheck ? new HashSet<int>() : handle.PreExistingPids;
        var evidence = new OwnershipEvidence
        {
            CandidatePid = listener.OwningPid,
            CandidateStartTimeTicksUtc = listenerTicks,
            LaunchedAtUtc = launchedAtForVerification,
            PreExistingPids = preExisting,
            PageIsHarness = true,
            LauncherAlive = launcherAlive,
            IsDescendantOfLauncher = isDescendant,
        };
        bool owned = OwnershipValidator.IsOwnedByDesktop(evidence);

        _log.Info(
            $"所有权验证: PID={listener.OwningPid}, StartTimeTicks={listenerTicks}, " +
            $"LaunchedAtTicks={launchedAtForVerification.Ticks}, 启动前已存在={handle.PreExistingPids.Contains(listener.OwningPid)}, " +
            $"launcherAlive={launcherAlive}, 是launcher后代={isDescendant}, 页面=Harness → Owned={owned}");

        var nowIso = DateTime.UtcNow.ToString("o");
        var runningState = new HarnessRuntimeState
        {
            SchemaVersion = 1,
            Phase = HarnessRuntimePhase.Running,
            SessionId = startingState.SessionId,
            Port = AppConfig.HarnessPort,
            Command = AppConfig.HarnessLaunchCommand,
            Url = AppConfig.HarnessUrl,
            LauncherPid = startingState.LauncherPid,
            LauncherStartTimeTicksUtc = startingState.LauncherStartTimeTicksUtc,
            LauncherStartTimeIsoUtc = startingState.LauncherStartTimeIsoUtc,
            LauncherProcessName = startingState.LauncherProcessName,
            HarnessPid = listener.OwningPid,
            HarnessStartTimeTicksUtc = listenerTicks ?? 0,
            HarnessStartTimeIsoUtc = listenerTicks is long listenerStart ? NativeProcessInfo.ToIsoUtc(listenerStart) : null,
            HarnessProcessName = SafeGetProcessName(listener.OwningPid),
            OwnedByDesktop = owned,
            RecordedAtIsoUtc = nowIso,
        };
        _state.Save(runningState);
        _stateWrittenThisRun = true;

        if (!owned)
            _log.Warn("所有权验证未通过：本次启动的 Harness 将按外部实例处理，关闭 Desktop 时不会结束该进程。");

        _log.Info($"Harness 服务就绪: {AppConfig.HarnessUrl}（ListenerPid={listener.OwningPid}, Owned={owned}）");
        CurrentSession = new HarnessSession
        {
            Kind = owned ? HarnessSessionKind.FreshStarted : HarnessSessionKind.External,
            Url = AppConfig.HarnessUrl,
            Port = AppConfig.HarnessPort,
            ListenerPid = listener.OwningPid,
            ListenerStartTimeTicksUtc = listenerTicks,
            OwnedByDesktop = owned,
            State = runningState,
        };
        return Task.FromResult(CurrentSession);
    }

    // ============================ 关闭流程 ============================

    private async Task ShutdownOwnedAsync(HarnessSession session)
    {
        _log.Info("===== Harness 关闭流程开始（OwnedByDesktop）=====");
        try
        {
            int? harnessPid = session.ListenerPid;
            long? harnessTicks = session.ListenerStartTimeTicksUtc;

            // 1) 关闭前再次验证：3080 监听关系 + PID + StartTime 完全一致才允许结束
            var listener = PortProcessResolver.FindListener(session.Port);
            long? currentTicks = listener is null ? null : NativeProcessInfo.GetStartTimeTicksUtc(listener.OwningPid);

            if (listener is null)
            {
                _log.Info("关闭时 3080 上已无监听进程，Harness 可能已自行退出。");
            }
            else if (harnessPid is int hp && listener.OwningPid == hp && harnessTicks is long ht && currentTicks == ht)
            {
                _log.Info($"PID + StartTime 双重验证通过，结束 Harness 进程树：PID {hp}，StartTimeTicks {ht}。");
                SafeKillTree(hp);
            }
            else
            {
                _log.Warn(
                    $"安全优先：当前监听进程（PID {listener.OwningPid}）与记录（PID {harnessPid?.ToString() ?? "?"}，" +
                    $"Ticks {harnessTicks?.ToString() ?? "?"}，当前 Ticks {currentTicks?.ToString() ?? "?"}）不一致，不结束任何进程。");
            }

            // 2) 清理属于本次启动的 launcher（以状态文件为准，同样先验证 StartTime）
            var st = _state.Load();
            if (st is { LauncherPid: > 0, LauncherStartTimeTicksUtc: > 0 })
            {
                long? launcherTicksNow = NativeProcessInfo.GetStartTimeTicksUtc(st.LauncherPid);
                if (launcherTicksNow == st.LauncherStartTimeTicksUtc)
                {
                    _log.Info($"结束 launcher 进程树（已验证 StartTime）：PID {st.LauncherPid}。");
                    SafeKillTree(st.LauncherPid);
                }
                else
                {
                    _log.Info($"launcher PID {st.LauncherPid} 已退出或与记录不匹配，跳过。");
                }
            }

            // 3) 等待端口释放
            await WaitForPortReleaseAsync(session.Port, TimeSpan.FromSeconds(AppConfig.ShutdownWaitSeconds)).ConfigureAwait(false);

            // 4) 清理状态文件
            _state.Clear();
            _log.Info("runtime.json 已清理。");
            _log.Info("===== Harness 关闭流程完成 =====");
        }
        catch (Exception ex)
        {
            _log.Error("关闭流程出现异常", ex);
        }
    }

    private async Task WaitForPortReleaseAsync(int port, TimeSpan maxWait)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < maxWait)
        {
            if (PortProcessResolver.FindListener(port) is null)
            {
                _log.Info($"端口 {port} 已释放。");
                return;
            }
            await Task.Delay(250).ConfigureAwait(false);
        }
        var leftover = PortProcessResolver.FindListener(port);
        if (leftover is not null)
            _log.Warn($"等待 {(int)maxWait.TotalSeconds} 秒后端口 {port} 仍被 PID {leftover.OwningPid} 监听（可能为外部进程，予以保留）。");
    }

    /// <summary>取消/失败清理：只清理“PID + StartTime 与记录一致”的 launcher 进程树。</summary>
    private async Task CancelAndCleanupAsync(StartupHandle handle)
    {
        try
        {
            int pid = handle.LauncherPid;
            long? expected = handle.LauncherStartTimeTicksUtc;
            long? actual = pid > 0 ? NativeProcessInfo.GetStartTimeTicksUtc(pid) : null;
            if (pid > 0 && expected is long e && actual == e)
            {
                _log.Warn($"结束 launcher 进程树：PID {pid}（已验证 StartTime）。");
                SafeKillTree(pid);
            }
            else
            {
                _log.Warn("launcher PID 与记录不匹配或已退出，跳过清理（安全优先）。");
            }

            try { handle.LauncherProcess?.Dispose(); } catch { }

            await Task.Delay(1500).ConfigureAwait(false);
            var still = PortProcessResolver.FindListener(AppConfig.HarnessPort);
            if (still is not null)
                _log.Warn($"清理完成后端口 {AppConfig.HarnessPort} 仍被 PID {still.OwningPid} 监听；该进程不满足安全清理条件，予以保留。");
        }
        catch (Exception ex)
        {
            _log.Error("取消启动清理异常", ex);
        }
    }

    // ============================ 底层工具 ============================

    /// <summary>静默启动随应用发布的 dsh.cmd（CreateNoWindow + 输出重定向到日志文件）。</summary>
    private Process StartLauncherProcess(string nodePath)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.AppDataDir);
            var dshPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, AppConfig.LocalDshRelativePath));
            if (!File.Exists(dshPath))
                throw new FileNotFoundException("未找到本地 dsh runtime 入口。", dshPath);
            if (!File.Exists(nodePath))
                throw new FileNotFoundException("未找到检测到的 Node.js。", nodePath);

            _log.Info($"本地 dsh 调用路径: {dshPath}");
            _log.Info($"Node.js 调用路径: {nodePath}");
            var psi = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = "/d /s /c \"\"" + dshPath + "\" web\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = AppConfig.AppDataDir,
            };
            var nodeDir = Path.GetDirectoryName(nodePath);
            if (!string.IsNullOrEmpty(nodeDir))
                psi.Environment["PATH"] = nodeDir + Path.PathSeparator + (psi.Environment.TryGetValue("PATH", out var path) ? path : "");
            var process = Process.Start(psi);
            if (process is null)
                throw new InvalidOperationException("Process.Start 返回 null。");
            try { process.StandardInput.Close(); } catch { }
            PumpOutput(process, process.StandardOutput, AppConfig.HarnessOutputLogPath);
            PumpOutput(process, process.StandardError, AppConfig.HarnessErrorLogPath);
            return process;
        }
        catch (Exception ex)
        {
            _log.Error("启动 launcher 进程失败", ex);
            throw new HarnessStartException(
                HarnessStartFailureReason.LaunchFailed,
                "无法启动 DeepSeek Harness 后台进程。\n请点击“打开日志文件夹”查看详细原因。",
                ex.Message,
                ex);
        }
    }

    /// <summary>把 Harness 输出转发到日志文件（每行脱敏；任务自生自灭）。</summary>
    private void PumpOutput(Process process, StreamReader reader, string logPath)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                await using var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                await using var writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
                string? line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
                {
                    await writer.WriteLineAsync(SecretRedactor.Redact(line)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"Harness 输出转发结束（{Path.GetFileName(logPath)}）: {ex.Message}");
            }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        });
    }

    private async Task<bool> IsHarnessOnPortAsync(CancellationToken ct)
    {
        var result = await HarnessHttpProber.ProbeAsync(new Uri(AppConfig.HarnessUrl), ct).ConfigureAwait(false);
        var isHarness = result.LooksLikeHarness;
        _log.Info($"3080 页面识别: httpOk={result.HttpOk}, isHarness={isHarness}" +
                  (result.HttpOk ? $", body={((result.Body ?? "").Length)} bytes" : $", error={result.Error}"));
        return isHarness;
    }

    private void AppendLogTail(string logPath)
    {
        try
        {
            if (!File.Exists(logPath)) return;
            var lines = File.ReadLines(logPath).Reverse().Take(30).Reverse().ToList();
            _log.Error($"—— {Path.GetFileName(logPath)} 末尾内容 ——");
            foreach (var line in lines)
                _log.Error("  " + line);
        }
        catch (Exception ex)
        {
            _log.Debug("读取 Harness 输出日志失败: " + ex.Message);
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string? SafeGetProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlySet<int> SnapshotAllPids()
    {
        try
        {
            var ids = new HashSet<int>();
            foreach (var p in Process.GetProcesses())
            {
                ids.Add(p.Id);
                p.Dispose();
            }
            return ids;
        }
        catch
        {
            return new HashSet<int>();
        }
    }

    /// <summary>结束进程树。调用前必须完成 PID + StartTime 所有权验证；只用于我们自己启动的进程。</summary>
    private void SafeKillTree(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: true);
            _log.Info($"已请求结束进程树：PID {pid}。");
        }
        catch (ArgumentException)
        {
            _log.Info($"进程 {pid} 已不存在。");
        }
        catch (Exception ex)
        {
            _log.Warn($"结束进程树 {pid} 失败（可能权限不足或已退出）: {ex.Message}");
        }
    }
}
