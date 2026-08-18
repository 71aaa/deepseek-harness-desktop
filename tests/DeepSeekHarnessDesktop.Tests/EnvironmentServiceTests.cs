using DeepSeekHarnessDesktop.Core.Services;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

/// <summary>环境检测（使用临时假目录，不读真实 PATH）。</summary>
public class EnvironmentServiceTests : IDisposable
{
    private readonly string _dir;

    public EnvironmentServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dshd-envtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void DetectsNode_WhenPresent()
    {
        File.WriteAllText(Path.Combine(_dir, "node.exe"), "");

        var result = EnvironmentService.Check(new[] { _dir });

        Assert.True(result.IsOk);
        Assert.True(result.NodeFound);
        Assert.Equal(Path.Combine(_dir, "node.exe"), result.NodePath);
    }

    [Fact]
    public void FailsWhenEverythingMissing()
    {
        var result = EnvironmentService.Check(new[] { _dir });
        Assert.False(result.IsOk);
        Assert.Null(result.NodePath);
    }

    [Fact]
    public void SearchDirectories_ContainPathEntries()
    {
        var dirs = EnvironmentService.GetSearchDirectories().ToList();
        Assert.NotEmpty(dirs);
        Assert.All(dirs, d => Assert.True(Directory.Exists(d)));
    }
}
