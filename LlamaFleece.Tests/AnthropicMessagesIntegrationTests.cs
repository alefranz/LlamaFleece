using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Xunit;

[Collection("TuiManager serial")]
public class AnthropicMessagesIntegrationTests
{
    [Fact]
    public async Task InvokeAsync_TracksAnthropicMessagesRequestsAndParsesStreamingEvents()
    {
        const string requestJson = """
        {
          "model": "claude-3-7-sonnet",
          "system": [
            { "type": "text", "text": "Be terse." }
          ],
          "messages": [
            {
              "role": "user",
              "content": [
                { "type": "text", "text": "Check the failing parser." }
              ]
            }
          ],
          "stream": true,
          "max_tokens": 256
        }
        """;

        const string responseBody = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-3-7-sonnet","usage":{"input_tokens":19}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking"}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Inspecting stream frames."}}

        event: content_block_start
        data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_1","name":"apply_patch","input":{}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"path\":\"PLAN"}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":".md\"}"}}

        event: content_block_start
        data: {"type":"content_block_start","index":2,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":2,"delta":{"type":"text_delta","text":"Done."}}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":7}}

        event: message_stop
        data: {"type":"message_stop"}

        data: [DONE]

        """;

        var interaction = await InvokeTrackedRequestAsync(requestJson, responseBody);

        Assert.Equal(1, TuiManager.InteractionCountForTests());
        Assert.Equal("claude-3-7-sonnet", interaction.Model);
        Assert.Contains(interaction.InputLines, line => line.Contains("Be terse.", StringComparison.Ordinal));
        Assert.Contains(interaction.InputLines, line => line.Contains("Check the failing parser.", StringComparison.Ordinal));

        Assert.Collection(
            interaction.OutputLines,
            segment =>
            {
                Assert.Equal(OutputSegmentKind.Reasoning, segment.Kind);
                Assert.Equal("Inspecting stream frames.", segment.Text);
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

        Assert.Equal(OutputSegmentKind.Text, interaction.CurrentOutputKind);
        Assert.Equal("Done.", interaction.CurrentOutputLine);

        Assert.Equal(19, interaction.PromptTokens);
        Assert.Equal(7, interaction.CompletionTokens);
        Assert.Equal(26, interaction.TotalTokens);
        Assert.Equal(200, interaction.ResponseStatusCode);
        Assert.Equal("end_turn", interaction.FinishReason);
        Assert.False(interaction.IsStreaming);
    }

    private static async Task<Interaction> InvokeTrackedRequestAsync(string requestJson, string upstreamSseBody)
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
        context.Request.Path = "/v1/messages";
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