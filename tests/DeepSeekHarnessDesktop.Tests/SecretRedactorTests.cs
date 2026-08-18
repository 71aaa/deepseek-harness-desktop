using DeepSeekHarnessDesktop.Core.Logging;
using Xunit;

namespace DeepSeekHarnessDesktop.Tests;

public class SecretRedactorTests
{
    [Theory]
    [InlineData("Authorization: Bearer abc123")]
    [InlineData("authorization = abc123")]
    [InlineData("api_key=sk-1234567890")]
    [InlineData("API-KEY: abc")]
    [InlineData("token=secretvalue")]
    [InlineData("Set-Cookie: sessionid=abc; path=/")]
    [InlineData("password= hunter2")]
    public void RedactsKnownSecretShapes(string input)
    {
        var output = SecretRedactor.Redact(input);
        Assert.Contains("[REDACTED]", output);
    }

    [Fact]
    public void RedactsBareDeepSeekStyleKeys()
    {
        var output = SecretRedactor.Redact("key is sk-abcdef1234567890 end");
        Assert.DoesNotContain("sk-abcdef1234567890", output);
        Assert.Contains("[REDACTED]", output);
    }

    [Fact]
    public void KeepsNormalTextIntact()
    {
        const string text = "正在启动 DeepSeek Harness，等待服务就绪（3080 端口）。";
        Assert.Equal(text, SecretRedactor.Redact(text));
    }

    [Fact]
    public void HandlesNullAndEmpty()
    {
        Assert.Equal("", SecretRedactor.Redact(null));
        Assert.Equal("", SecretRedactor.Redact(""));
    }

    [Fact]
    public void RedactsSecretsEmbeddedInLongerLines()
    {
        var output = SecretRedactor.Redact("请求失败 Authorization: Bearer xyz789，重试中");
        Assert.DoesNotContain("xyz789", output);
        Assert.Contains("[REDACTED]", output);
    }
}
