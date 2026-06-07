using System.Text.Json.Nodes;
using Xunit;

public class TrackedRequestContinuationPolicyTests
{
    [Fact]
    public void TryCreatePayload_AppendsContinuationMessageForChatCompletions()
    {
        var policy = new TrackedRequestContinuationPolicy();
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

        Assert.True(policy.Supports(InteractionEndpoint.ChatCompletions, root));
        Assert.True(policy.TryCreatePayload(InteractionEndpoint.ChatCompletions, root, out var continuationRoot));
        Assert.Single(root["messages"]!.AsArray());

        var messages = continuationRoot["messages"]!.AsArray();
        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[1]!["role"]!.GetValue<string>());
        Assert.Contains("Continue the answer", messages[1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void TryCreatePayload_AppendsInstructionToPrompt()
    {
        var policy = new TrackedRequestContinuationPolicy();
        var root = JsonNode.Parse(
            """
            {
              "prompt": "Write a haiku about tests.",
              "stream": true
            }
            """)!.AsObject();

        Assert.True(policy.Supports(InteractionEndpoint.Completions, root));
        Assert.True(policy.TryCreatePayload(InteractionEndpoint.Completions, root, out var continuationRoot));

        var prompt = continuationRoot["prompt"]!.GetValue<string>();
        Assert.Contains("Write a haiku about tests.", prompt);
        Assert.Contains("Continue the answer", prompt);
    }

    [Fact]
    public void TryCreatePayload_WrapsResponsesObjectInputAndAppendsMessage()
    {
        var policy = new TrackedRequestContinuationPolicy();
        var root = JsonNode.Parse(
            """
            {
              "instructions": "Keep answers concise.",
              "input": {
                "type": "message",
                "role": "user",
                "content": [
                  { "type": "input_text", "text": "hello" }
                ]
              },
              "stream": true
            }
            """)!.AsObject();

        Assert.True(policy.Supports(InteractionEndpoint.Responses, root));
        Assert.True(policy.TryCreatePayload(InteractionEndpoint.Responses, root, out var continuationRoot));
        Assert.IsType<JsonObject>(root["input"]);

        var input = continuationRoot["input"]!.AsArray();
        Assert.Equal(2, input.Count);
        Assert.Equal("message", input[1]!["type"]!.GetValue<string>());
        Assert.Equal("user", input[1]!["role"]!.GetValue<string>());
        Assert.Contains("Continue the answer", input[1]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void TryCreatePayload_CreatesResponsesInputWhenOnlyInstructionsExist()
    {
        var policy = new TrackedRequestContinuationPolicy();
        var root = JsonNode.Parse(
            """
            {
              "instructions": "Answer like a release engineer.",
              "stream": true
            }
            """)!.AsObject();

        Assert.True(policy.Supports(InteractionEndpoint.Responses, root));
        Assert.True(policy.TryCreatePayload(InteractionEndpoint.Responses, root, out var continuationRoot));

        Assert.Equal("Answer like a release engineer.", continuationRoot["instructions"]!.GetValue<string>());
        Assert.Contains("Continue the answer", continuationRoot["input"]!.GetValue<string>());
    }

    [Fact]
    public void ShouldIssueFollowUp_OnlyForInitialSuccessfulEmptyDoneResponse()
    {
        var policy = new TrackedRequestContinuationPolicy();

        Assert.True(policy.ShouldIssueFollowUp(0, true, true, new ProxyLoggingResult(true, false, null)));
        Assert.False(policy.ShouldIssueFollowUp(1, true, true, new ProxyLoggingResult(true, false, null)));
        Assert.False(policy.ShouldIssueFollowUp(0, false, true, new ProxyLoggingResult(true, false, null)));
        Assert.False(policy.ShouldIssueFollowUp(0, true, false, new ProxyLoggingResult(true, false, null)));
        Assert.False(policy.ShouldIssueFollowUp(0, true, true, new ProxyLoggingResult(false, false, null)));
        Assert.False(policy.ShouldIssueFollowUp(0, true, true, new ProxyLoggingResult(true, true, null)));
    }
}