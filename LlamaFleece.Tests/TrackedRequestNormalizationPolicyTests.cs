using System.Text.Json.Nodes;
using Xunit;

public class TrackedRequestNormalizationPolicyTests
{
    [Fact]
    public void Apply_AddsIncludeUsageForChatCompletions()
    {
        var policy = new TrackedRequestNormalizationPolicy();
        var root = JsonNode.Parse(
            """
            {
              "model": "gpt-test",
              "messages": [
                { "role": "user", "content": "hello" }
              ],
              "stream": true
            }
            """)!.AsObject();

        var mutations = policy.Apply(root, InteractionEndpoint.ChatCompletions);

        Assert.True(root["stream_options"]!["include_usage"]!.GetValue<bool>());

        var mutation = Assert.Single(mutations);
        Assert.Equal(ForwardedRequestMutationKind.RequestBodyNormalization, mutation.Kind);
        Assert.Equal("Enabled stream_options.include_usage for usage reporting.", mutation.Summary);
    }

    [Fact]
    public void Apply_DoesNotAddMutationWhenIncludeUsageAlreadyPresent()
    {
        var policy = new TrackedRequestNormalizationPolicy();
        var root = JsonNode.Parse(
            """
            {
              "prompt": "Summarize the failure.",
              "stream": true,
              "stream_options": {
                "include_usage": true
              }
            }
            """)!.AsObject();

        var mutations = policy.Apply(root, InteractionEndpoint.Completions);

        Assert.Empty(mutations);
        Assert.True(root["stream_options"]!["include_usage"]!.GetValue<bool>());
    }

    [Fact]
    public void Apply_SkipsResponsesPayloads()
    {
        var policy = new TrackedRequestNormalizationPolicy();
        var root = JsonNode.Parse(
            """
            {
              "instructions": "Keep answers concise.",
              "input": "hello",
              "stream": true
            }
            """)!.AsObject();

        var mutations = policy.Apply(root, InteractionEndpoint.Responses);

        Assert.Empty(mutations);
        Assert.Null(root["stream_options"]);
    }
}