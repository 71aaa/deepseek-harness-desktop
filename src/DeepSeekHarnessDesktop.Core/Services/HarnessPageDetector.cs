namespace DeepSeekHarnessDesktop.Core.Services;

/// <summary>
/// Harness 页面识别：不要仅仅因为“3080 有东西响应”就认定它是 DeepSeek Harness。
/// 强特征 __DSH_BOOT__ + 弱特征关键字（见 AppConfig.HarnessPageKeywords）。
/// </summary>
public static class HarnessPageDetector
{
    public static bool LooksLikeHarnessPage(string? htmlOrText)
    {
        if (string.IsNullOrWhiteSpace(htmlOrText)) return false;
        foreach (var keyword in AppConfig.HarnessPageKeywords)
        {
            if (htmlOrText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
