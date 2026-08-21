using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DeepSeekHarnessDesktop.Core;
using DeepSeekHarnessDesktop.Core.Logging;
using DeepSeekHarnessDesktop.Core.Services;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekHarnessDesktop;

public partial class MainWindow : Window
{
    private readonly HarnessService _harness;
    private readonly ILog _log;

    private bool _startFlowRunning;
    private bool _closing;
    private bool _allowClose;

    public MainWindow(HarnessService harness, ILog log)
    {
        InitializeComponent();
        _harness = harness;
        _log = log;
        ShowVersionInfo();
        Closing += OnWindowClosing;
    }

    private void ShowVersionInfo()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, VersionInfoProvider.RuntimeManifestFileName);
        var versionInfo = VersionInfoProvider.Read(typeof(MainWindow).Assembly.GetName().Version, manifestPath);
        Title = $"DeepSeek Harness Desktop v{versionInfo.DesktopVersion}";
    }

    public void StartFlow()
    {
        if (_startFlowRunning) return;
        _startFlowRunning = true;
        _ = RunStartFlowAsync();
    }

    private async Task RunStartFlowAsync()
    {
        try
        {
            ShowStartupView();
            var session = await _harness.StartAsync(UpdateStatus, CancellationToken.None);
            if (!await InitializeWebViewAsync())
                return; // 错误视图已显示
            ShowWebView(session.Url);
            _log.Info($"Harness 已就绪并载入 WebView2: {session.Url} (Owned={session.OwnedByDesktop})");
        }
        catch (HarnessStartException ex)
        {
            _log.Error("Harness 启动失败: " + ex.FriendlyMessage, ex);
            ShowError(ex.FriendlyMessage, showWebView2Help: false);
        }
        catch (OperationCanceledException)
        {
            _log.Info("启动流程已取消。");
        }
        catch (Exception ex)
        {
            _log.Error("启动流程发生未预期异常", ex);
            ShowError("启动过程中发生未知错误。\n请点击“打开日志文件夹”查看详情。", showWebView2Help: false);
        }
        finally
        {
            _startFlowRunning = false;
        }
    }

    private void UpdateStatus(string message)
    {
        try { Dispatcher.BeginInvoke(() => StartupStatusText.Text = message); }
        catch { }
    }

    private async Task<bool> InitializeWebViewAsync()
    {
        try
        {
            _log.Info("开始初始化 WebView2…");
            string? version = null;
            try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (Exception ex) { _log.Warn("查询 WebView2 Runtime 版本失败: " + ex.Message); }

            if (string.IsNullOrWhiteSpace(version))
            {
                _log.Error("未检测到可用的 Microsoft Edge WebView2 Runtime。");
                ShowError("未检测到可用的 Microsoft Edge WebView2 Runtime。\n请安装 WebView2 Runtime 后重试。", showWebView2Help: true);
                return false;
            }

            _log.Info($"WebView2 Runtime 版本: {version}");
            var env = await CoreWebView2Environment.CreateAsync(null, AppConfig.WebView2UserDataDir);
            await WebView.EnsureCoreWebView2Async(env);
            WireWebViewEvents();
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("WebView2 初始化失败", ex);
            ShowError("未检测到可用的 Microsoft Edge WebView2 Runtime，或 WebView2 初始化失败。\n请安装 WebView2 Runtime 后重试。", showWebView2Help: true);
            return false;
        }
    }

    private void WireWebViewEvents()
    {
        WebView.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            // 新窗口请求一律留在本窗口内打开，保持“普通桌面软件”体验
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                WebView.CoreWebView2.Navigate(uri.AbsoluteUri);
            else
                _log.Warn($"已阻止打开不支持的新窗口: {e.Uri}");
        };
        WebView.CoreWebView2.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith(Uri.UriSchemeHttp + ":", StringComparison.OrdinalIgnoreCase) ||
                e.Uri.StartsWith(Uri.UriSchemeHttps + ":", StringComparison.OrdinalIgnoreCase))
                return;
            e.Cancel = true;
            _log.Warn($"已阻止不支持的导航: {e.Uri}");
        };
        WebView.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
                _log.Warn($"页面加载失败 ({e.WebErrorStatus}): {WebView.Source}");
        };
    }

    private void ShowStartupView()
    {
        StartupView.Visibility = Visibility.Visible;
        ErrorView.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Collapsed;
        ShutdownOverlay.Visibility = Visibility.Collapsed;
        StartupStatusText.Text = "正在启动 DeepSeek Harness…";
    }

    private void ShowError(string message, bool showWebView2Help)
    {
        StartupView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Visible;
        WebView.Visibility = Visibility.Collapsed;
        ShutdownOverlay.Visibility = Visibility.Collapsed;
        ErrorMessageText.Text = message;
        WebView2FixLink.Visibility = showWebView2Help ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowWebView(string url)
    {
        StartupView.Visibility = Visibility.Collapsed;
        ErrorView.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Visible;
        ShutdownOverlay.Visibility = Visibility.Collapsed;
        WebView.Source = new Uri(url, UriKind.Absolute);
    }

    private void ShowShutdownOverlay()
    {
        ShutdownText.Text = "正在关闭 DeepSeek Harness…";
        ShutdownOverlay.Visibility = Visibility.Visible;
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;

        _log.Info("用户点击关闭按钮，开始关闭流程。");
        ShowShutdownOverlay();
        try
        {
            await _harness.ShutdownAsync();
        }
        catch (Exception ex)
        {
            _log.Error("关闭流程异常", ex);
        }
        _allowClose = true;
        // 通过新的 Dispatcher 操作请求退出，避免在 Closing 事件上下文中重入 Close()
        // 触发 WPF 的 VerifyNotClosing 异常（真实测试日志中发现的问题）。
        _ = Dispatcher.BeginInvoke(() =>
        {
            try { Application.Current.Shutdown(); }
            catch (Exception ex) { _log.Error("最终退出请求异常", ex); }
        });
    }

    /// <summary>由单实例服务在第二个实例请求激活时（经 Dispatcher）调用，把已有窗口拉到前台。</summary>
    public void ActivateFromExternalRequest()
    {
        if (!IsLoaded) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => StartFlow();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.LogsDir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppConfig.LogsDir + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error("打开日志文件夹失败", ex);
        }
    }

    private void WebView2DownloadLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppConfig.WebView2RuntimeDownloadUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error("打开 WebView2 下载页面失败", ex);
        }
    }
}
