using System.Windows;
using System.Windows.Threading;
using DeepSeekHarnessDesktop.Core;
using DeepSeekHarnessDesktop.Core.Logging;
using DeepSeekHarnessDesktop.Core.Services;

namespace DeepSeekHarnessDesktop;

public partial class App : Application
{
    private FileLogger? _logger;
    private SingleInstanceService? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ILog log;
        try
        {
            _logger = new FileLogger(AppConfig.DesktopLogPath);
            log = _logger;
        }
        catch (Exception ex)
        {
            log = NullLogger.Instance;
            try { _logger?.Dispose(); } catch { }
            _logger = null;
            try { System.Diagnostics.Trace.WriteLine("日志初始化失败: " + ex); } catch { }
        }

        log.Info("===== Desktop 启动 =====");
        log.Info($"版本: {typeof(App).Assembly.GetName().Version}");
        log.Info($"状态目录: {AppConfig.AppDataDir}");
        log.Info($"日志目录: {AppConfig.LogsDir}");

        HookGlobalExceptionLogging(log);

        // 单实例：第二个实例写入激活请求后安全退出，第一个实例轮询到请求后拉出窗口
        if (!SingleInstanceService.TryAcquire(
                AppConfig.ActivationRequestPath,
                () =>
                {
                    try { Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ActivateFromExternalRequest()); }
                    catch { }
                },
                out _singleInstance))
        {
            log.Info("检测到已有 Desktop 实例，当前实例安全退出。");
            Shutdown(0);
            return;
        }

        var stateService = new StateService(AppConfig.RuntimeStatePath, log);
        var harnessService = new HarnessService(stateService, log);
        var window = new MainWindow(harnessService, log);
        MainWindow = window;
        window.Show();
        window.StartFlow();
    }

    private void HookGlobalExceptionLogging(ILog log)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            try { log.Error("未处理的 UI 线程异常", args.Exception); } catch { }
            args.Handled = true; // 记录日志而不是直接崩溃
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try { log.Error("未处理的 AppDomain 异常", args.ExceptionObject as Exception); } catch { }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            try { log.Error("未观察到的任务异常", args.Exception); } catch { }
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _logger?.Info("===== Desktop 退出 ====="); } catch { }
        try { _singleInstance?.Dispose(); } catch { }
        try { _logger?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
