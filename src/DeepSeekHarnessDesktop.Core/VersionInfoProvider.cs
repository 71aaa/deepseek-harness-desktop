using System.Text.Json;

namespace DeepSeekHarnessDesktop.Core;

/// <summary>读取桌面程序与随发布包提供的 Harness runtime 版本信息。</summary>
public static class VersionInfoProvider
{
    public const string RuntimeManifestFileName = "runtime-manifest-rc8.json";
    private const string UnknownVersion = "Unknown";

    public static DesktopVersionInfo Read(Version? desktopAssemblyVersion, string manifestPath)
    {
        var desktopVersion = desktopAssemblyVersion?.ToString(3) ?? UnknownVersion;
        var harnessVersion = ReadHarnessVersion(manifestPath);
        return new DesktopVersionInfo(desktopVersion, harnessVersion, "Embedded");
    }

    internal static string ReadHarnessVersion(string manifestPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (document.RootElement.TryGetProperty("harnessVersion", out var version) &&
                version.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(version.GetString()))
            {
                return version.GetString()!;
            }
        }
        catch (IOException)
        {
            // 发布包缺少 manifest 时仍应正常启动，只显示未知版本。
        }
        catch (JsonException)
        {
            // manifest 损坏不影响桌面宿主的正常启动。
        }

        return UnknownVersion;
    }
}

public sealed record DesktopVersionInfo(string DesktopVersion, string HarnessVersion, string Runtime);
