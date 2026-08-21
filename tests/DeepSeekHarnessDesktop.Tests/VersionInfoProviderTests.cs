using DeepSeekHarnessDesktop.Core;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

public class VersionInfoProviderTests
{
    [Fact]
    public void Read_UsesAssemblyVersionAndHarnessVersionFromManifest()
    {
        var manifestPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(manifestPath, "{\"harnessVersion\":\"0.1.0-rc.8\"}");

            var info = VersionInfoProvider.Read(new Version(1, 1, 1, 0), manifestPath);

            Assert.Equal("1.1.1", info.DesktopVersion);
            Assert.Equal("0.1.0-rc.8", info.HarnessVersion);
            Assert.Equal("Embedded", info.Runtime);
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    [Fact]
    public void Read_UsesUnknownHarnessVersionWhenManifestIsMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

        var info = VersionInfoProvider.Read(new Version(1, 1, 1, 0), missingPath);

        Assert.Equal("1.1.1", info.DesktopVersion);
        Assert.Equal("Unknown", info.HarnessVersion);
        Assert.Equal("Embedded", info.Runtime);
    }
}
