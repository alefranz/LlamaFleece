using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Xunit;

[Collection("TuiManager serial")]
public class ResponsesApiIntegrationTests
{
    [Fact]
    public async Task InvokeAsync_TracksResponsesRequestsAndCapturesStructuredInput()
    {
        const string requestJson = """
        {
          "model": "gpt-test",
          "instructions": "Keep answers concise.",
          "input": [
            {
              "type": "message",
              "role": "system",
              "content": [
                { "type": "input_text", "text": "Follow repository conventions." }
              ]
            },
            {
              "type": "message",
              "role": "user",
              "content": [
                { "type": "input_text", "text": "Inspect the formatter regression." }
              ]
            },
            {
              "type": "function_call_output",
              "call_id": "call_1",
              "output": "{\"ok\":true}"
            }
          ],
          "stream": true
        }
        """;

        const string responseBody = """
        event: response.created
        data: {"type":"response.created","response":{"id":"resp_1"}}

        event: response.completed
        data: {"type":"response.completed","response":{"id":"resp_1"}}

        """;

        var interaction = await InvokeTrackedRequestAsync("/v1/responses", requestJson, responseBody);

        Assert.Equal(1, TuiManager.InteractionCountForTests());
        Assert.Equal("gpt-test", interaction.Model);
        Assert.Contains(interaction.InputLines, line => line.Contains("instructions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(interaction.InputLines, line => line.Contains("Follow repository conventions.", StringComparison.Ordinal));
        Assert.Contains(interaction.InputLines, line => line.Contains("Inspect the formatter regression.", StringComparison.Ordinal));
        Assert.Contains(interaction.InputLines, line => line.Contains("function_call_output", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"stream\":true", interaction.RawInput.ToString().Replace(" ", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_ParsesResponsesTextReasoningToolCallsAndUsage()
    {
        const string requestJson = """
        {
          "model": "gpt-test",
          "input": "hello",
          "stream": true
        }
        """;

        const string responseBody = """
        event: response.created
        data: {"type":"response.created","response":{"id":"resp_1"}}

        event: response.output_text.delta
        data: {"type":"response.output_text.delta","item_id":"msg_1","output_index":0,"content_index":0,"delta":"Hello"}

        event: response.reasoning_text.delta
        data: {"type":"response.reasoning_text.delta","item_id":"msg_1","output_index":0,"content_index":1,"delta":"thinking"}

        event: response.function_call_arguments.delta
        data: {"type":"response.function_call_arguments.delta","item_id":"fc_1","call_id":"call_1","output_index":1,"delta":"{\"path\":\""}

        event: response.output_item.done
        data: {"type":"response.output_item.done","output_index":0,"item":{"type":"message","role":"assistant","id":"msg_1","content":[{"type":"output_text","text":"Hello"},{"type":"reasoning_text","text":"thinking"}]}}

        event: response.output_item.done
        data: {"type":"response.output_item.done","output_index":1,"item":{"type":"function_call","id":"fc_1","call_id":"call_1","name":"apply_patch","arguments":"{\"path\":\"PLAN.md\"}"}}

        event: response.completed
        data: {"type":"response.completed","response":{"id":"resp_1","usage":{"input_tokens":11,"input_tokens_details":{"cached_tokens":2},"output_tokens":7,"output_tokens_details":{"reasoning_tokens":3},"total_tokens":18}}}

        """;

        var interaction = await InvokeTrackedRequestAsync("/v1/responses", requestJson, responseBody);

        Assert.Collection(interaction.OutputLines,
            segment =>
            {
                Assert.Equal(OutputSegmentKind.Text, segment.Kind);
                Assert.Equal("Hello", segment.Text);
            },
            segment =>
            {
                Assert.Equal(OutputSegmentKind.Reasoning, segment.Kind);
                Assert.Equal("thinking", segment.Text);
            },
            segment =>
            {
                Assert.Equal(OutputSegmentKind.ToolCallName, segment.Kind);
                Assert.Equal("apply_patch", segment.Text);
            },
            segment =>
            {
                Assert.Equal(OutputSegmentKind.ToolCallArguments, segment.Kind);
                Assert.Equal("{\"path\":\"PLAN.md\"}", segment.Text);
            });

        Assert.Equal(11, interaction.PromptTokens);
        Assert.Equal(7, interaction.CompletionTokens);
        Assert.Equal(18, interaction.TotalTokens);
        Assert.Equal(2, interaction.CachedPromptTokens);
        Assert.Equal(3, interaction.ReasoningTokens);
        Assert.Equal(200, interaction.ResponseStatusCode);
        Assert.Equal("completed", interaction.FinishReason);
        Assert.False(interaction.IsStreaming);
    }

      [Fact]
      public async Task InvokeAsync_ProjectsCompletedResponseOutputItemsWhenStreamOmitsPerItemDoneEvents()
      {
        const string requestJson = """
        {
          "model": "gpt-test",
          "input": "hello",
          "stream": true
        }
        """;

        const string responseBody = """
        event: response.created
        data: {"type":"response.created","response":{"id":"resp_1"}}

        event: response.completed
        data: {"type":"response.completed","response":{"id":"resp_1","status":"completed","output":[{"type":"message","role":"assistant","id":"msg_1","content":[{"type":"output_text","text":"Hello"}]},{"type":"function_call","id":"fc_1","call_id":"call_1","name":"apply_patch","arguments":"{\"path\":\"PLAN.md\"}"},{"type":"function_call","id":"fc_2","call_id":"call_2","name":"runTests","arguments":"{}"}],"usage":{"input_tokens":11,"input_tokens_details":{"cached_tokens":2},"output_tokens":7,"output_tokens_details":{"reasoning_tokens":3},"total_tokens":18}}}

        data: [DONE]

        """;

        var interaction = await InvokeTrackedRequestAsync("/v1/responses", requestJson, responseBody);

        Assert.Collection(interaction.OutputLines,
          segment =>
          {
            Assert.Equal(OutputSegmentKind.Text, segment.Kind);
            Assert.Equal("Hello", segment.Text);
          },
          segment =>
          {
            Assert.Equal(OutputSegmentKind.ToolCallName, segment.Kind);
            Assert.Equal("apply_patch", segment.Text);
          },
          segment =>
          {
            Assert.Equal(OutputSegmentKind.ToolCallArguments, segment.Kind);
            Assert.Equal("{\"path\":\"PLAN.md\"}", segment.Text);
          },
          segment =>
          {
            Assert.Equal(OutputSegmentKind.ToolCallName, segment.Kind);
            Assert.Equal("runTests", segment.Text);
          },
          segment =>
          {
            Assert.Equal(OutputSegmentKind.ToolCallArguments, segment.Kind);
            Assert.Equal("{}", segment.Text);
          });

        Assert.Equal(11, interaction.PromptTokens);
        Assert.Equal(7, interaction.CompletionTokens);
        Assert.Equal(18, interaction.TotalTokens);
        Assert.Equal(2, interaction.CachedPromptTokens);
        Assert.Equal(3, interaction.ReasoningTokens);
        Assert.Equal("completed", interaction.FinishReason);
      }

    [Fact]
    public async Task InvokeAsync_ForceContinueRetriesResponsesRequestsWithoutChangingSchema()
    {
        const string requestJson = """
        {
          "model": "gpt-test",
          "instructions": "Keep answers concise.",
          "input": [
            {
              "type": "message",
              "role": "user",
              "content": [
                { "type": "input_text", "text": "Summarize the issue." }
              ]
            }
          ],
          "stream": true
        }
        """;

        const string emptyResponseBody = """
        event: response.created
        data: {"type":"response.created","response":{"id":"resp_1"}}

        event: response.completed
        data: {"type":"response.completed","response":{"id":"resp_1"}}

        data: [DONE]

        """;

        const string continuedResponseBody = """
        event: response.created
        data: {"type":"response.created","response":{"id":"resp_2"}}

        event: response.output_text.delta
        data: {"type":"response.output_text.delta","item_id":"msg_2","output_index":0,"content_index":0,"delta":"Recovered answer."}

        event: response.completed
        data: {"type":"response.completed","response":{"id":"resp_2"}}

        data: [DONE]

        """;

        TuiManager.ResetForTests();

    await using var upstream = await TinyLlamaCppMockServer.StartAsync(
      TinyLlamaCppMockResponse.Sse(emptyResponseBody),
      TinyLlamaCppMockResponse.Sse(continuedResponseBody));

    using var client = upstream.CreateClient();

        var coordinator = new TrackedRequestCoordinator(client);
        var middleware = new LoggingMiddleware(_ => Task.CompletedTask, coordinator);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/responses";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.True(interaction!.ForceContinueApplied);
        Assert.Equal("Recovered answer.", interaction.CurrentOutputLine);

        Assert.Equal(2, upstream.Requests.Count);

        var originalRoot = JsonNode.Parse(upstream.Requests[0].Body)!.AsObject();
        var continuationRoot = JsonNode.Parse(upstream.Requests[1].Body)!.AsObject();
        var continuationInput = continuationRoot["input"]!.AsArray();

        Assert.Equal("Keep answers concise.", continuationRoot["instructions"]!.GetValue<string>());
        Assert.Single(originalRoot["input"]!.AsArray());
        Assert.Equal(2, continuationInput.Count);
        Assert.Equal("message", continuationInput[1]!["type"]!.GetValue<string>());
        Assert.Equal("user", continuationInput[1]!["role"]!.GetValue<string>());
        Assert.Contains("Continue the answer", continuationInput[1]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("/v1/responses", upstream.Requests[0].Path);
        Assert.Equal("/v1/responses", upstream.Requests[1].Path);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var downstreamBody = await reader.ReadToEndAsync();
        Assert.Contains("Recovered answer.", downstreamBody, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(downstreamBody, "data: [DONE]"));
    }

      [Fact]
      public async Task InvokeAsync_AppendsLogEntriesEvenWhenLogViewIsClosed()
      {
        const string requestJson = """
        {
          "model": "gpt-test",
          "input": "hello",
          "stream": true
        }
        """;

        const string responseBody = """
        event: response.created
        data: {"type":"response.created","response":{"id":"resp_1"}}

        event: response.completed
        data: {"type":"response.completed","response":{"id":"resp_1"}}

        """;

        await InvokeTrackedRequestAsync("/v1/responses", requestJson, responseBody);

        var logSnapshot = TuiManager.GetLogSnapshotForTests();
        Assert.True(logSnapshot.Entries.Count >= 2);
        Assert.Contains(logSnapshot.Entries, entry => entry.Contains(">>> POST", StringComparison.Ordinal));
        Assert.Contains(logSnapshot.Entries, entry => entry.Contains("<<< 200", StringComparison.Ordinal));
      }

    private static async Task<Interaction> InvokeTrackedRequestAsync(string path, string requestJson, string upstreamSseBody)
    {
        TuiManager.ResetForTests();

        using var client = new HttpClient(new StubHttpMessageHandler(_ => CreateSseResponse(upstreamSseBody)))
        {
            BaseAddress = new Uri("http://upstream.test")
        };

        var coordinator = new TrackedRequestCoordinator(client);
        var middleware = new LoggingMiddleware(_ => Task.CompletedTask, coordinator);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        return interaction!;
    }

    private static HttpResponseMessage CreateSseResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
        };
    }

    private static int CountOccurrences(string text, string value)
    {
      var count = 0;
      var index = 0;

      while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
      {
        count++;
        index += value.Length;
      }

      return count;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}