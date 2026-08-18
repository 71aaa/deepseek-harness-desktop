using DeepSeekHarnessDesktop.Core.Services;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

public class HarnessPageDetectorTests
{
    [Theory]
    [InlineData("<html><script>window.__DSH_BOOT__ = {};</script></html>")]
    [InlineData("<html><head><title>DeepSeek Harness</title></head></html>")]
    [InlineData("<HTML>deepseek</HTML>")]
    [InlineData("var x = '__DSH_BOOT__'")]
    public void DetectsHarnessPages(string html)
    {
        Assert.True(HarnessPageDetector.LooksLikeHarnessPage(html));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>hello world</body></html>")]
    [InlineData("Microsoft IIS welcome page")]
    [InlineData("404 Not Found")]
    public void RejectsNonHarnessPages(string? html)
    {
        Assert.False(HarnessPageDetector.LooksLikeHarnessPage(html));
    }
}
