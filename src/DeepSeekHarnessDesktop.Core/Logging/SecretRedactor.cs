using System.Text.RegularExpressions;

namespace DeepSeekHarnessDesktop.Core.Logging;

/// <summary>
/// 日志脱敏：任何写入日志的文本都会先经过 Redact 处理，
/// 确保 DeepSeek API Key / Authorization / Token / Cookie / Secret 不落盘。
/// </summary>
public static partial class SecretRedactor
{
    private static readonly Regex[] Patterns =
    {
        AuthHeaderRegex(),
        BearerTokenRegex(),
        KeyValueRegex(),
        DeepSeekKeyRegex(),
        CookieRegex(),
    };

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var result = text;
        foreach (var pattern in Patterns)
            result = pattern.Replace(result, "[REDACTED]");
        return result;
    }

    /// <summary>Authorization: Bearer xxx / authorization = xxx（可选 Bearer 前缀 + 令牌，排除中英文标点）。</summary>
    [GeneratedRegex(@"(?i)\b(authorization|proxy-authorization)\s*[:=]\s*(?:bearer\s+)?[^\s""',;，。：；！？（）【】]+")]
    private static partial Regex AuthHeaderRegex();

    /// <summary>单独出现的 Bearer 令牌。</summary>
    [GeneratedRegex(@"(?i)\bbearer\s+[A-Za-z0-9._~+\/-]{6,}\b")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)\b(api[_-]?key|apikey|access[_-]?token|refresh[_-]?token|token|secret|password)\s*[:=]\s*[^\s""',;，。：；！？（）【】]+")]
    private static partial Regex KeyValueRegex();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_-]{6,}\b")]
    private static partial Regex DeepSeekKeyRegex();

    [GeneratedRegex(@"(?i)\b(cookie|set-cookie)\s*[:=]\s*[^\r\n]+")]
    private static partial Regex CookieRegex();
}
