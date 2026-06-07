using System.Text.Json.Nodes;
using Xunit;

public class TrackedRequestPayloadTests
{
    [Fact]
    public void Create_NormalizesCompletionPayload_AndAppendsContinuationMessageToMessages()
    {
        var payload = TrackedRequestPayload.Create(
            "/v1/chat/completions",
            "application/json",
            """
            {
              "model": "gpt-test",
              "stream": true,
              "messages": [
                { "role": "user", "content": "hello" }
              ]
            }
            """);

        Assert.True(payload.SupportsForceContinue);
        Assert.True(payload.TryCreateForceContinuePayload(out var continuationPayload));

        var normalizedRoot = JsonNode.Parse(payload.NormalizedJson)!.AsObject();
        var continuationRoot = JsonNode.Parse(continuationPayload.NormalizedJson)!.AsObject();

        Assert.Collection(
            payload.ForwardedRequestMutations,
            mutation =>
            {
                Assert.Equal(ForwardedRequestMutationKind.RequestBodyNormalization, mutation.Kind);
                Assert.Equal("Enabled stream_options.include_usage for usage reporting.", mutation.Summary);
            });
        Assert.True(normalizedRoot["stream_options"]!["include_usage"]!.GetValue<bool>());
        Assert.Contains(
            continuationPayload.ForwardedRequestMutations,
            mutation => mutation.Kind == ForwardedRequestMutationKind.ForceContinueFollowUp);

        var messages = continuationRoot["messages"]!.AsArray();
        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[1]!["role"]!.GetValue<string>());
        Assert.Contains("Continue the answer", messages[1]!["content"]!.GetValue<string>());
        Assert.True(continuationRoot["stream_options"]!["include_usage"]!.GetValue<bool>());
    }

    [Fact]
    public void Create_DoesNotRecordNormalizationMutationWhenIncludeUsageAlreadyPresent()
    {
        var payload = TrackedRequestPayload.Create(
            "/v1/chat/completions",
            "application/json",
            """
            {
              "model": "gpt-test",
              "stream": true,
              "stream_options": {
                "include_usage": true
              },
              "messages": [
                { "role": "user", "content": "hello" }
              ]
            }
            """);

        Assert.Empty(payload.ForwardedRequestMutations);
    }

    [Fact]
    public void Create_NormalizesAliasChatCompletionPayloadLikeV1Route()
    {
        var payload = TrackedRequestPayload.Create(
            "/chat/completions",
            "application/json",
            """
            {
              "model": "gpt-test",
              "stream": true,
              "messages": [
                { "role": "user", "content": "hello" }
              ]
            }
            """);

        var normalizedRoot = JsonNode.Parse(payload.NormalizedJson)!.AsObject();

        Assert.True(payload.SupportsForceContinue);
        Assert.True(payload.TryCreateForceContinuePayload(out var continuationPayload));
        Assert.True(normalizedRoot["stream_options"]!["include_usage"]!.GetValue<bool>());
        Assert.Contains(
            continuationPayload.ForwardedRequestMutations,
            mutation => mutation.Kind == ForwardedRequestMutationKind.ForceContinueFollowUp);
    }

    [Fact]
    public void TryCreateForceContinuePayload_AppendsInstructionToPromptRequests()
    {
        var payload = TrackedRequestPayload.Create(
            "/v1/completions",
            "application/json",
            """
            {
              "prompt": "Write a haiku about tests.",
              "stream": true
            }
            """);

        Assert.True(payload.TryCreateForceContinuePayload(out var continuationPayload));

        var continuationRoot = JsonNode.Parse(continuationPayload.NormalizedJson)!.AsObject();
        var prompt = continuationRoot["prompt"]!.GetValue<string>();

        Assert.Contains("Write a haiku about tests.", prompt);
        Assert.Contains("Continue the answer", prompt);
        Assert.True(continuationRoot["stream_options"]!["include_usage"]!.GetValue<bool>());
    }

    [Fact]
    public void TryCreateForceContinuePayload_AppendsResponsesInputItemForInstructionsAndArrayInput()
    {
        var payload = TrackedRequestPayload.Create(
            "/v1/responses",
            "application/json",
            """
            {
              "model": "gpt-test",
              "instructions": "Keep answers concise.",
              "input": [
                {
                  "type": "message",
                  "role": "user",
                  "content": [
                    { "type": "input_text", "text": "hello" }
                  ]
                }
              ],
              "stream": true
            }
            """);

        Assert.True(payload.SupportsForceContinue);
        Assert.True(payload.TryCreateForceContinuePayload(out var continuationPayload));

        var continuationRoot = JsonNode.Parse(continuationPayload.NormalizedJson)!.AsObject();
        var input = continuationRoot["input"]!.AsArray();

        Assert.Equal("Keep answers concise.", continuationRoot["instructions"]!.GetValue<string>());
        Assert.Equal(2, input.Count);
        Assert.Equal("message", input[1]!["type"]!.GetValue<string>());
        Assert.Equal("user", input[1]!["role"]!.GetValue<string>());
        Assert.Contains("Continue the answer", input[1]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void TryCreateForceContinuePayload_AppendsInstructionToAnthropicMessages()
    {
        var payload = TrackedRequestPayload.Create(
            "/v1/messages",
            "application/json",
            """
            {
              "model": "claude-test",
              "max_tokens": 256,
              "stream": true,
              "messages": [
                {
                  "role": "user",
                  "content": [
                    { "type": "text", "text": "hello" }
                  ]
                }
              ]
            }
            """);

        Assert.True(payload.SupportsForceContinue);
        Assert.True(payload.TryCreateForceContinuePayload(out var continuationPayload));

        var continuationRoot = JsonNode.Parse(continuationPayload.NormalizedJson)!.AsObject();
        var messages = continuationRoot["messages"]!.AsArray();

        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[1]!["role"]!.GetValue<string>());
        Assert.Contains("Continue the answer", messages[1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void TryCreateForceContinuePayload_AppendsInstructionToResponsesStringInput()
    {
        var payload = TrackedRequestPayload.Create(
            "/v1/responses",
            "application/json",
            """
            {
              "instructions": "Be terse.",
              "input": "Summarize the failure.",
              "stream": true
            }
            """);

        Assert.True(payload.SupportsForceContinue);
        Assert.True(payload.TryCreateForceContinuePayload(out var continuationPayload));

        var continuationRoot = JsonNode.Parse(continuationPayload.NormalizedJson)!.AsObject();
        var input = continuationRoot["input"]!.GetValue<string>();

        Assert.Contains("Summarize the failure.", input);
        Assert.Contains("Continue the answer", input);
        Assert.Equal("Be terse.", continuationRoot["instructions"]!.GetValue<string>());
    }

    [Fact]
    public void TryCreateForceContinuePayload_CreatesResponsesInputWhenOnlyInstructionsExist()
    {
        var payload = TrackedRequestPayload.Create(
            "/v1/responses",
            "application/json",
            """
            {
              "instructions": "Answer like a release engineer.",
              "stream": true
            }
            """);

        Assert.True(payload.SupportsForceContinue);
        Assert.True(payload.TryCreateForceContinuePayload(out var continuationPayload));

        var continuationRoot = JsonNode.Parse(continuationPayload.NormalizedJson)!.AsObject();

        Assert.Equal("Answer like a release engineer.", continuationRoot["instructions"]!.GetValue<string>());
        Assert.Contains("Continue the answer", continuationRoot["input"]!.GetValue<string>());
    }

    [Fact]
    public void TryCreateForceContinuePayload_ReturnsFalseWithoutPromptState()
    {
        var payload = TrackedRequestPayload.Create(
            "/v1/chat/completions",
            "application/json",
            """
            {
              "model": "gpt-test",
              "stream": true,
              "input": "hello"
            }
            """);

        Assert.False(payload.SupportsForceContinue);
        Assert.False(payload.TryCreateForceContinuePayload(out _));
    }

  [Fact]
  public void Create_DoesNotNormalizeOrEnableContinuationForLookalikeCompletionsRoutes()
  {
    var payload = TrackedRequestPayload.Create(
      "/v1/completions/metrics",
      "application/json",
      """
      {
        "prompt": "Summarize the incident.",
        "stream": true
      }
      """);

    var normalizedRoot = JsonNode.Parse(payload.NormalizedJson)!.AsObject();

    Assert.False(payload.SupportsForceContinue);
    Assert.False(payload.TryCreateForceContinuePayload(out _));
    Assert.Null(normalizedRoot["stream_options"]);
  }

  [Fact]
  public void Create_DoesNotEnableResponsesContinuationForLookalikeResponsesRoutes()
  {
    var payload = TrackedRequestPayload.Create(
      "/v1/responses/export",
      "application/json",
      """
      {
        "instructions": "Be terse.",
        "input": "Summarize the failure.",
        "stream": true
      }
      """);

    Assert.False(payload.SupportsForceContinue);
    Assert.False(payload.TryCreateForceContinuePayload(out _));
  }
}