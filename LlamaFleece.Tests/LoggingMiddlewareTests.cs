using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Xunit;

[Collection("TuiManager serial")]
public class LoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_DoesNotTrackLookalikePostRoutes()
    {
        const string requestJson = "{\"prompt\":\"hello\"}";

        TuiManager.ResetForTests();

        string? forwardedBody = null;
        var nextCalled = false;
        var middleware = new LoggingMiddleware(
            async context =>
            {
                nextCalled = true;
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                forwardedBody = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;
                context.Response.StatusCode = StatusCodes.Status202Accepted;
            },
            new TrackedRequestCoordinator(new HttpClient(new ThrowIfCalledHandler())
            {
                BaseAddress = new Uri("http://upstream.test")
            }));

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/admin/chat/metrics";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Equal(requestJson, forwardedBody);
        Assert.Equal(0, TuiManager.InteractionCountForTests());
    }

    [Fact]
    public async Task InvokeAsync_TrackedMalformedRequestPreservesRawInputAndSurfacesPreviewFailure()
    {
        const string requestJson = "{\"model\":\"gpt-test\",\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}]";
        const string upstreamBody = "data: [DONE]\n\n";

        TuiManager.ResetForTests();

        string? forwardedBody = null;
        var middleware = new LoggingMiddleware(
            _ => Task.CompletedTask,
            new TrackedRequestCoordinator(new HttpClient(new StubHttpMessageHandler(request =>
            {
                forwardedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(upstreamBody, Encoding.UTF8, "text/event-stream")
                };
            }))
            {
                BaseAddress = new Uri("http://upstream.test")
            }));

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/chat/completions";
        context.Request.QueryString = new QueryString("?api-key=query-secret-token&api-version=2026-05-01");
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(requestJson, forwardedBody);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Equal(requestJson, interaction!.RawInput.ToString());
        Assert.Contains(interaction.InputLines, line => line.Contains("Structured preview unavailable", StringComparison.Ordinal));
        Assert.Contains(interaction.InputLines, line => line.Contains("request body is not valid JSON", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(interaction.InputLines, line => line.Contains("Showing redacted raw request body only.", StringComparison.Ordinal));

        var logSnapshot = TuiManager.GetLogSnapshotForTests();
        Assert.Contains(logSnapshot.Entries, entry => entry.Contains("Structured preview unavailable", StringComparison.Ordinal));
        Assert.DoesNotContain(logSnapshot.Entries, entry => entry.Contains("query-secret-token", StringComparison.Ordinal));
        Assert.Contains(logSnapshot.Entries, entry => entry.Contains("/v1/chat/completions?api-key=REDACTED&api-version=2026-05-01", StringComparison.Ordinal));

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var downstreamBody = await reader.ReadToEndAsync();
        Assert.Contains("data: [DONE]", downstreamBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_TrackedNormalizedRequestSurfacesForwardedRequestMutation()
    {
        const string requestJson = """
        {
          "model": "gpt-test",
          "stream": true,
          "messages": [
            { "role": "user", "content": "hello" }
          ]
        }
        """;

        const string upstreamBody = "data: [DONE]\n\n";

        TuiManager.ResetForTests();

        string? forwardedBody = null;
        var middleware = new LoggingMiddleware(
            _ => Task.CompletedTask,
            new TrackedRequestCoordinator(new HttpClient(new StubHttpMessageHandler(request =>
            {
                forwardedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(upstreamBody, Encoding.UTF8, "text/event-stream")
                };
            }))
            {
                BaseAddress = new Uri("http://upstream.test")
            }));

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/chat/completions";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.NotNull(forwardedBody);
        Assert.Contains("\"include_usage\":true", forwardedBody!, StringComparison.Ordinal);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Equal(requestJson, interaction!.RawInput.ToString());
        Assert.Contains(
            interaction.ForwardedRequestMutations,
            mutation => mutation.Kind == ForwardedRequestMutationKind.RequestBodyNormalization);
        Assert.DoesNotContain(
            interaction.InputLines,
            line => line.Contains("Forwarded request changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeAsync_TrackedInteractionRedactsSensitiveRequestAndResponseDataInTui()
    {
        const string requestJson = """
        {
          "model": "gpt-test",
          "authorization": "Bearer sk-request-secret-token",
          "messages": [
            { "role": "user", "content": "Use sk-request-secret-token in the follow-up." }
          ],
          "tool_config": {
            "api_key": "request-tool-secret"
          },
          "stream": true
        }
        """;

        const string upstreamBody = "data: {\"choices\":[{\"delta\":{\"content\":\"Response token sk-response-secret-token\"}}]}\n\n" +
                                    "data: [DONE]\n\n";

        TuiManager.ResetForTests();

        var middleware = new LoggingMiddleware(
            _ => Task.CompletedTask,
            new TrackedRequestCoordinator(new HttpClient(new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(upstreamBody, Encoding.UTF8, "text/event-stream")
                }))
            {
                BaseAddress = new Uri("http://upstream.test")
            }));

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/chat/completions";
        context.Request.QueryString = new QueryString("?api-key=query-secret-token&api-version=2026-05-01");
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.DoesNotContain("query-secret-token", interaction!.RawInput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sk-request-secret-token", interaction.RawInput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("request-tool-secret", interaction.RawInput.ToString(), StringComparison.Ordinal);
        Assert.Contains(InteractionSecretRedactor.RedactionToken, interaction.RawInput.ToString(), StringComparison.Ordinal);

        Assert.DoesNotContain("sk-response-secret-token", interaction.RawOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains(InteractionSecretRedactor.RedactionToken, interaction.RawOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains(interaction.InputLines, line => line.Contains(InteractionSecretRedactor.RedactionToken, StringComparison.Ordinal));
        Assert.Contains(InteractionSecretRedactor.RedactionToken, interaction.CurrentOutputLine, StringComparison.Ordinal);

        var snapshot = TuiManager.TakeSnapshotForTests();
        Assert.NotNull(snapshot.VisibleInteraction);
        Assert.Equal(
            "/v1/chat/completions?api-key=REDACTED&api-version=2026-05-01",
            snapshot.VisibleInteraction!.RequestTarget);

        var logSnapshot = TuiManager.GetLogSnapshotForTests();
        Assert.DoesNotContain(logSnapshot.Entries, entry => entry.Contains("query-secret-token", StringComparison.Ordinal));
        Assert.Contains(logSnapshot.Entries, entry => entry.Contains("api-key=REDACTED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeAsync_WhenUpstreamProviderIsUnreachable_SurfacesFailureInTuiAndReturnsBadGateway()
    {
        const string requestJson = """
        {
          "model": "gpt-test",
          "messages": [
            { "role": "user", "content": "hello" }
          ],
          "stream": true
        }
        """;

        TuiManager.ResetForTests();

        var middleware = new LoggingMiddleware(
            _ => Task.CompletedTask,
            new TrackedRequestCoordinator(new HttpClient(new StubHttpMessageHandler(_ =>
            {
                throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
            }))
            {
                BaseAddress = new Uri("http://upstream.test")
            }));

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/chat/completions";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Equal(502, interaction!.ResponseStatusCode);
        Assert.Equal("upstream_unavailable", interaction.FinishReason);
        Assert.False(interaction.IsStreaming);
        Assert.Contains(
            interaction.OutputLines,
            segment => segment.Kind == OutputSegmentKind.Markup &&
                       segment.Text.Contains("Upstream provider unreachable", StringComparison.Ordinal) &&
                       segment.Text.Contains("actively refused", StringComparison.Ordinal));

        var status = TuiManager.GetStatusSnapshotForTests();
        Assert.True(status.IsError);
        Assert.Contains("Upstream provider unreachable", status.Message, StringComparison.Ordinal);

        var logSnapshot = TuiManager.GetLogSnapshotForTests();
        Assert.Contains(logSnapshot.Entries, entry => entry.Contains("Upstream provider unreachable", StringComparison.Ordinal));
        Assert.Contains(logSnapshot.Entries, entry => entry.Contains("<<< 502", StringComparison.Ordinal));
    }

    private sealed class ThrowIfCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Untracked routes should not be proxied through TrackedRequestCoordinator.");
        }
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