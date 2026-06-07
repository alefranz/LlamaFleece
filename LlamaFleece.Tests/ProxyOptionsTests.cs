using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

public class ProxyOptionsTests
{
    [Fact]
    public void LoadAndValidate_BindsSectionValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TargetUrl"] = "http://legacy.test:8123",
                ["Port"] = "7000",
                ["Proxy:UpstreamUrl"] = "https://section.test/v1",
                ["Proxy:ListenPort"] = "5100",
                ["Proxy:ListenHost"] = "127.0.0.1",
                ["Proxy:Timeouts:TrackedRequestSeconds"] = "90",
                ["Proxy:Timeouts:ShutdownSeconds"] = "15",
                ["Proxy:UpstreamHeaders:X-Test-Header"] = "workspace",
                ["Proxy:Persistence:Enabled"] = "true",
                ["Proxy:Persistence:SessionFilePath"] = "state\\session-history.json",
                ["Proxy:Pricing:Default:PromptUsdPer1MTokens"] = "1.5",
                ["Proxy:Pricing:Default:CompletionUsdPer1MTokens"] = "3.0",
                ["Proxy:Pricing:Models:gpt-special:PromptUsdPer1MTokens"] = "2.0",
                ["Proxy:Pricing:Models:gpt-special:CompletionUsdPer1MTokens"] = "4.0"
            })
            .Build();

        var options = ProxyOptions.LoadAndValidate(configuration);

        Assert.Equal("https://section.test/v1", options.UpstreamUrl);
        Assert.Equal(5100, options.ListenPort);
    Assert.Equal("127.0.0.1", options.ListenHost);
        Assert.Equal(TimeSpan.FromSeconds(90), options.GetTrackedRequestTimeout());
        Assert.Equal(TimeSpan.FromSeconds(15), options.GetShutdownTimeout());
        Assert.Equal("workspace", options.UpstreamHeaders["X-Test-Header"]);
        Assert.True(options.Persistence.Enabled);
        Assert.Equal("state\\session-history.json", options.Persistence.SessionFilePath);
        Assert.EndsWith(Path.Combine("state", "session-history.json"), options.Persistence.GetResolvedSessionFilePath(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1.5m, options.Pricing.Default!.PromptUsdPer1MTokens);
        Assert.Equal(3.0m, options.Pricing.Default.CompletionUsdPer1MTokens);
        Assert.Equal(2.0m, options.Pricing.Models["gpt-special"].PromptUsdPer1MTokens);
        Assert.Equal(4.0m, options.Pricing.Models["gpt-special"].CompletionUsdPer1MTokens);
    }

    [Fact]
    public void LoadAndValidate_FallsBackToLegacyRootKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TargetUrl"] = "http://legacy.test:9000/base",
                ["Port"] = "7100"
            })
            .Build();

        var options = ProxyOptions.LoadAndValidate(configuration);

        Assert.Equal("http://legacy.test:9000/base", options.UpstreamUrl);
        Assert.Equal(7100, options.ListenPort);
        Assert.Equal("localhost", options.ListenHost);
        Assert.True(options.IsLoopbackOnlyBinding());
    }

    [Fact]
    public void LoadAndValidate_AllowsAnyIpListenHostOptIn()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Proxy:ListenHost"] = "0.0.0.0"
            })
            .Build();

        var options = ProxyOptions.LoadAndValidate(configuration);

        Assert.Equal("0.0.0.0", options.ListenHost);
        Assert.False(options.IsLoopbackOnlyBinding());
    }

    [Fact]
    public void LoadAndValidate_RejectsInvalidProxyConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Proxy:UpstreamUrl"] = "https://user:password@upstream.test/v1",
                ["Proxy:ListenHost"] = "example.test",
                ["Proxy:Timeouts:TrackedRequestSeconds"] = "0",
                ["Proxy:UpstreamAuth:Scheme"] = "Bearer",
                ["Proxy:UpstreamAuth:Parameter"] = "test-token",
                ["Proxy:UpstreamHeaders:Authorization"] = "Bearer duplicate-token",
                ["Proxy:UpstreamHeaders:Host"] = "upstream.test",
                ["Proxy:Persistence:SessionFilePath"] = "state\\",
                ["Proxy:Pricing:Default:PromptUsdPer1MTokens"] = "-1",
                ["Proxy:Pricing:Models:gpt-bad:PromptUsdPer1MTokens"] = "1.5"
            })
            .Build();

        var exception = Assert.Throws<OptionsValidationException>(() => ProxyOptions.LoadAndValidate(configuration));

        Assert.Contains(exception.Failures, failure => failure.Contains("cannot embed user info", StringComparison.Ordinal));
    Assert.Contains(exception.Failures, failure => failure.Contains("ListenHost", StringComparison.Ordinal));
        Assert.Contains(exception.Failures, failure => failure.Contains("TrackedRequestSeconds", StringComparison.Ordinal));
        Assert.Contains(exception.Failures, failure => failure.Contains("Authorization", StringComparison.Ordinal));
        Assert.Contains(exception.Failures, failure => failure.Contains("reserved header 'Host'", StringComparison.Ordinal));
        Assert.Contains(exception.Failures, failure => failure.Contains("SessionFilePath", StringComparison.Ordinal));
        Assert.Contains(exception.Failures, failure => failure.Contains("PromptUsdPer1MTokens", StringComparison.Ordinal));
        Assert.Contains(exception.Failures, failure => failure.Contains("requires both PromptUsdPer1MTokens and CompletionUsdPer1MTokens", StringComparison.Ordinal));
    }
}