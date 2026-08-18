using DeepSeekHarnessDesktop.Core;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

public class AppConfigTests
{
    [Fact]
    public void LaunchCommandIsExactOfficialCommand()
    {
        Assert.Equal(@"dsh-runtime\node_modules\.bin\dsh.cmd web", AppConfig.HarnessLaunchCommand);
    }

    [Fact]
    public void LaunchCommandUsesLocalDshRuntime()
    {
        Assert.DoesNotContain("npx", AppConfig.HarnessLaunchCommand, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(@"dsh-runtime\node_modules\.bin\dsh.cmd", AppConfig.HarnessLaunchCommand);
        Assert.EndsWith("web", AppConfig.HarnessLaunchCommand);
    }

    [Fact]
    public void UrlPortAndMarkerAreConsistent()
    {
        Assert.Equal("http://127.0.0.1:3080", AppConfig.HarnessUrl);
        Assert.Equal(3080, AppConfig.HarnessPort);
        Assert.Equal("__DSH_BOOT__", AppConfig.HarnessBootMarker);
    }

    [Fact]
    public void PageKeywordsIncludeBootMarker()
    {
        Assert.Contains("__DSH_BOOT__", AppConfig.HarnessPageKeywords);
    }

    [Fact]
    public void StateAndLogPathsLiveUnderLocalAppData()
    {
        Assert.Contains(AppConfig.AppFolderName, AppConfig.RuntimeStatePath);
        Assert.Contains("logs", AppConfig.DesktopLogPath);
        Assert.EndsWith("runtime.json", AppConfig.RuntimeStatePath);
    }
}
