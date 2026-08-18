namespace DeepSeekHarnessDesktop.Core;

/// <summary>
/// 全局配置中心。
/// DeepSeek Harness 的启动命令、地址、端口、页面特征等“易变值”集中在此处 —— 这是全项目唯一修改点。
/// 只有当官方彻底改变 CLI 名称 / 子命令 / 端口 / Web 服务机制时才需要修改这里。
/// </summary>
public static class AppConfig
{
    // ==================== DeepSeek Harness 启动配置（唯一修改点） ====================
    /// <summary>随应用发布的本地 dsh 入口（已验证的 rc.6 完整依赖树）。</summary>
    public const string LocalDshRelativePath = @"dsh-runtime\node_modules\.bin\dsh.cmd";

    /// <summary>完整启动命令：静默启动随应用发布的本地 Harness 后台。</summary>
    public const string HarnessLaunchCommand = LocalDshRelativePath + " web";

    /// <summary>Harness Web 服务地址。</summary>
    public const string HarnessUrl = "http://127.0.0.1:3080";

    /// <summary>Harness Web 服务端口。</summary>
    public const int HarnessPort = 3080;

    /// <summary>Harness 页面强特征：官方 Web UI 注入 window.__DSH_BOOT__。</summary>
    public const string HarnessBootMarker = "__DSH_BOOT__";

    /// <summary>页面识别关键字（依次命中任一即视为 Harness 页面；强弱特征结合，防止误判）。</summary>
    public static readonly string[] HarnessPageKeywords = { "__DSH_BOOT__", "deepseek harness", "deepseek" };
    // ================================================================================

    // ==================== Desktop 自身配置 ====================
    public const string AppFolderName = "DeepSeekHarnessDesktop";
    public const string RuntimeStateFileName = "runtime.json";
    public const string DesktopLogFileName = "desktop.log";
    public const string HarnessOutputLogFileName = "dsh-output.log";
    public const string HarnessErrorLogFileName = "dsh-error.log";

    /// <summary>就绪探测间隔（毫秒）。</summary>
    public const int ReadyCheckIntervalMs = 600;

    /// <summary>就绪探测最长等待（秒）。</summary>
    public const int ReadyTimeoutSeconds = 300;

    /// <summary>关闭时等待端口释放的最长时间（秒）。</summary>
    public const int ShutdownWaitSeconds = 5;

    public const string WebView2RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    /// <summary>状态与日志根目录：%LOCALAPPDATA%\DeepSeekHarnessDesktop\</summary>
    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    public static string LogsDir => Path.Combine(AppDataDir, "logs");
    public static string RuntimeStatePath => Path.Combine(AppDataDir, RuntimeStateFileName);
    public static string DesktopLogPath => Path.Combine(LogsDir, DesktopLogFileName);
    public static string HarnessOutputLogPath => Path.Combine(LogsDir, HarnessOutputLogFileName);
    public static string HarnessErrorLogPath => Path.Combine(LogsDir, HarnessErrorLogFileName);

    /// <summary>单实例“激活已有窗口”请求文件路径。</summary>
    public static string ActivationRequestPath => Path.Combine(AppDataDir, "activate.request");

    /// <summary>WebView2 用户数据目录（与系统 Edge 浏览器数据隔离）。</summary>
    public static string WebView2UserDataDir => Path.Combine(AppDataDir, "webview2");
}
