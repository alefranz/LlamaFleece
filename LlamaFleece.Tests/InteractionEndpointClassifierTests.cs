using Xunit;

public class InteractionEndpointClassifierTests
{
    [Theory]
    [InlineData("/v1/chat/completions", nameof(InteractionEndpoint.ChatCompletions))]
    [InlineData("/v1/chat/completions/", nameof(InteractionEndpoint.ChatCompletions))]
    [InlineData("/chat/completions", nameof(InteractionEndpoint.ChatCompletions))]
    [InlineData("/v1/completions", nameof(InteractionEndpoint.Completions))]
    [InlineData("/completions", nameof(InteractionEndpoint.Completions))]
    [InlineData("/v1/responses", nameof(InteractionEndpoint.Responses))]
    [InlineData("/responses", nameof(InteractionEndpoint.Responses))]
    [InlineData("/v1/messages", nameof(InteractionEndpoint.AnthropicMessages))]
    [InlineData("/messages", nameof(InteractionEndpoint.AnthropicMessages))]
    [InlineData("/V1/RESPONSES", nameof(InteractionEndpoint.Responses))]
    [InlineData("/CHAT/COMPLETIONS", nameof(InteractionEndpoint.ChatCompletions))]
    public void Classify_MatchesSupportedEndpointsAndAliasesExactly(string path, string expectedEndpoint)
    {
        Assert.Equal(expectedEndpoint, InteractionEndpointClassifier.Classify(path).ToString());
        Assert.True(InteractionEndpointClassifier.IsTracked(path));
    }

    [Theory]
    [InlineData("/chat/completions/metrics")]
    [InlineData("/completions/metrics")]
    [InlineData("/responses/export")]
    [InlineData("/messages/batches")]
    [InlineData("/v2/chat/completions")]
    [InlineData("/v1/admin/chat/metrics")]
    [InlineData("/v1/completions/metrics")]
    [InlineData("/v1/responses/export")]
    [InlineData("/v1/messages/batches")]
    [InlineData("/v1/custom-responses")]
    public void Classify_DoesNotMatchSubstringRoutes(string path)
    {
        Assert.Equal(InteractionEndpoint.None, InteractionEndpointClassifier.Classify(path));
        Assert.False(InteractionEndpointClassifier.IsTracked(path));
    }
}