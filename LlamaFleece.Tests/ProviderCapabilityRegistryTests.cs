using System.Text.Json;
using Xunit;

public class ProviderCapabilityRegistryTests
{
    [Fact]
    public void All_DeclaresAnthropicMessagesSupport()
    {
        var profile = Assert.Single(ProviderCapabilityRegistry.All.Where(candidate => candidate.Id == "anthropic-messages"));

        Assert.Contains("/v1/messages", profile.EndpointPaths);
        Assert.Contains("/messages", profile.EndpointPaths);
        Assert.Equal(ProviderEventFamily.AnthropicMessages, profile.EventFamily);
        Assert.True(profile.Capabilities.HasFlag(ProviderCapability.RequestPreview));
        Assert.True(profile.Capabilities.HasFlag(ProviderCapability.StreamText));
        Assert.True(profile.Capabilities.HasFlag(ProviderCapability.StreamReasoning));
        Assert.True(profile.Capabilities.HasFlag(ProviderCapability.StreamToolCalls));
        Assert.True(profile.Capabilities.HasFlag(ProviderCapability.Usage));
        Assert.True(profile.Capabilities.HasFlag(ProviderCapability.ForceContinue));
        Assert.True(profile.Capabilities.HasFlag(ProviderCapability.SseTransport));
    }

    [Theory]
    [InlineData("openai-compatible", "/chat/completions")]
    [InlineData("openai-compatible", "/completions")]
    [InlineData("openai-responses", "/responses")]
    [InlineData("ollama-openai-compatible", "/chat/completions")]
    [InlineData("ollama-openai-compatible", "/completions")]
    [InlineData("anthropic-messages", "/messages")]
    public void All_DeclaresNonV1AliasesForTrackedEndpoints(string profileId, string expectedAlias)
    {
        var profile = Assert.Single(ProviderCapabilityRegistry.All.Where(candidate => candidate.Id == profileId));

        Assert.Contains(expectedAlias, profile.EndpointPaths);
    }

    [Theory]
    [InlineData("openai-compatible")]
    [InlineData("openai-responses")]
    [InlineData("ollama-openai-compatible")]
    [InlineData("anthropic-messages")]
    public void All_EndpointPathsRemainTrackedRoutes(string profileId)
    {
        var profile = Assert.Single(ProviderCapabilityRegistry.All.Where(candidate => candidate.Id == profileId));

        Assert.All(profile.EndpointPaths, path => Assert.True(InteractionEndpointClassifier.IsTracked(path)));
    }

    [Theory]
    [InlineData("""{"choices":[{"delta":{"content":"hi"}}]}""", nameof(ProviderEventFamily.OpenAiCompatible))]
    [InlineData("""{"type":"response.output_text.delta","delta":"hi"}""", nameof(ProviderEventFamily.ResponsesApi))]
    [InlineData("""{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"hi"}}""", nameof(ProviderEventFamily.AnthropicMessages))]
    public void ClassifyStreamFamily_RecognizesKnownFamilies(string json, string expectedFamily)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expectedFamily, ProviderCapabilityRegistry.ClassifyStreamFamily(document.RootElement).ToString());
    }
}