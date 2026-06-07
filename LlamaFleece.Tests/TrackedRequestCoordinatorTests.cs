using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

[Collection("TuiManager serial")]
public class TrackedRequestCoordinatorTests
{
        private const string ChatCompletionsRequestJson = """
        {
            "model": "gpt-test",
            "messages": [
                { "role": "user", "content": "hello" }
            ],
            "stream": true
        }
        """;

        [Fact]
        public async Task ProxyAsync_WhenUpstreamResponseIsNotSse_CopiesBodyWithoutDoneSentinel()
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

                const string upstreamBody = "{\"id\":\"resp_1\",\"status\":\"ok\"}";

                TuiManager.ResetForTests();
                TuiManager.NewSession();

                await using var upstream = await TinyLlamaCppMockServer.StartAsync(TinyLlamaCppMockResponse.Json(upstreamBody));
                using var client = upstream.CreateClient();

                var coordinator = new TrackedRequestCoordinator(client);

                var context = new DefaultHttpContext();
                context.Request.Method = HttpMethods.Post;
                context.Request.Path = "/v1/chat/completions";
                context.Request.ContentType = "application/json";
                context.Response.Body = new MemoryStream();

                var payload = TrackedRequestPayload.Create(
                        context.Request.Path.Value!,
                        context.Request.ContentType,
                        requestJson);

                await coordinator.ProxyAsync(context, payload);

                Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

                context.Response.Body.Position = 0;
                using var responseReader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
                var downstreamBody = await responseReader.ReadToEndAsync();

                Assert.Equal(upstreamBody, downstreamBody);
                Assert.DoesNotContain("data: [DONE]", downstreamBody, StringComparison.Ordinal);

                var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
                Assert.NotNull(interaction);
                Assert.Equal(200, interaction!.ResponseStatusCode);
        }

    [Fact]
    public async Task ProxyAsync_WhenInitialSseReadThrowsAfterHeaders_PreservesPartialResponseAndMarksInteractionFailed()
    {
        const string forwardedEvent = "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n";
        const string upstreamBody = forwardedEvent + "data: {\"choices\":[{\"delta\":{\"content\":\"ignored\"}}]}\n\n";

        TuiManager.ResetForTests();
        TuiManager.NewSession();

        await using var upstream = new ThrowAfterBytesStream(
            upstreamBody,
            Encoding.UTF8.GetByteCount(forwardedEvent),
            "upstream read failed");

        using var client = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(CreateSseResponse(upstream))))
        {
            BaseAddress = new Uri("http://upstream.test")
        };

        var coordinator = new TrackedRequestCoordinator(client);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/chat/completions";
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();

        var payload = TrackedRequestPayload.Create(
            context.Request.Path.Value!,
            context.Request.ContentType,
            ChatCompletionsRequestJson);

        await coordinator.ProxyAsync(context, payload);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var responseReader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var downstreamBody = await responseReader.ReadToEndAsync();

        Assert.Contains("hello", downstreamBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored", downstreamBody, StringComparison.Ordinal);
        Assert.DoesNotContain("data: [DONE]", downstreamBody, StringComparison.Ordinal);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Equal(200, interaction!.ResponseStatusCode);
        Assert.Equal("upstream_stream_failed", interaction.FinishReason);
        Assert.False(interaction.IsStreaming);
        Assert.Contains(interaction.Diagnostics, diagnostic =>
            diagnostic.Kind == InteractionDiagnosticKind.UpstreamFailure &&
            diagnostic.Code == "upstream_stream_failed" &&
            diagnostic.Detail == "upstream read failed");
        Assert.Contains(interaction.OutputLines, segment =>
            segment.Kind == OutputSegmentKind.Markup &&
            segment.Text.Contains("Upstream stream failed", StringComparison.Ordinal) &&
            segment.Text.Contains("upstream read failed", StringComparison.Ordinal));

        var status = TuiManager.GetStatusSnapshotForTests();
        Assert.True(status.IsError);
        Assert.Contains("Upstream stream failed", status.Message, StringComparison.Ordinal);
        Assert.Contains("upstream read failed", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProxyAsync_WhenInitialBodyCopyThrowsAfterHeaders_PreservesPartialResponseAndMarksInteractionFailed()
    {
        const string forwardedPrefix = "{\"id\":\"resp_1\",";
        const string upstreamBody = forwardedPrefix + "\"status\":\"ok\",\"output\":\"hello\"}";

        TuiManager.ResetForTests();
        TuiManager.NewSession();

        await using var upstream = new ThrowAfterBytesStream(
            upstreamBody,
            Encoding.UTF8.GetByteCount(forwardedPrefix),
            "response copy failed");

        using var client = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(upstream)
                {
                    Headers =
                    {
                        ContentType = new("application/json")
                    }
                }
            })))
        {
            BaseAddress = new Uri("http://upstream.test")
        };

        var coordinator = new TrackedRequestCoordinator(client);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/chat/completions";
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();

        var payload = TrackedRequestPayload.Create(
            context.Request.Path.Value!,
            context.Request.ContentType,
            ChatCompletionsRequestJson);

        await coordinator.ProxyAsync(context, payload);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var responseReader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var downstreamBody = await responseReader.ReadToEndAsync();

        Assert.Equal(forwardedPrefix, downstreamBody);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Equal(200, interaction!.ResponseStatusCode);
        Assert.Equal("upstream_stream_failed", interaction.FinishReason);
        Assert.False(interaction.IsStreaming);
        Assert.Contains(interaction.Diagnostics, diagnostic =>
            diagnostic.Kind == InteractionDiagnosticKind.UpstreamFailure &&
            diagnostic.Code == "upstream_stream_failed" &&
            diagnostic.Detail == "response copy failed");

        var status = TuiManager.GetStatusSnapshotForTests();
        Assert.True(status.IsError);
        Assert.Contains("response copy failed", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayAsync_WhenFollowUpRequestThrowsDuringForceContinue_ReturnsInitialStatusAndCompletion()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();

        var requests = new List<HttpRequestMessage>();
        var sendCount = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requests.Add(CloneRequest(request));
            sendCount++;

            if (sendCount == 1)
            {
                return CreateSseResponse("data: [DONE]\n\n");
            }

            await Task.Yield();
            throw new HttpRequestException("follow-up stream failed");
        }))
        {
            BaseAddress = new Uri("http://upstream.test")
        };

        var coordinator = new TrackedRequestCoordinator(client);
        var payload = TrackedRequestPayload.Create("/v1/chat/completions", "application/json", ChatCompletionsRequestJson);

        var result = await coordinator.ReplayAsync(payload);

        Assert.Equal(200, result.StatusCode);
        Assert.True(result.SawCompletion);
        Assert.True(result.SawInitialResponse);
        Assert.Equal(2, sendCount);

        var secondBody = await requests[1].Content!.ReadAsStringAsync();
        Assert.Contains("Continue the answer if the previous response stopped unexpectedly", secondBody, StringComparison.Ordinal);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Contains(interaction!.Diagnostics, diagnostic =>
            diagnostic.Code == "force_continue_sent" &&
            diagnostic.Attempt == 1);
        Assert.Contains(interaction.Diagnostics, diagnostic =>
            diagnostic.Code == "force_continue_failed" &&
            diagnostic.Attempt == 1 &&
            diagnostic.Detail == "follow-up stream failed");
    }

    [Fact]
    public async Task ReplayAsync_WhenFollowUpRequestReturnsNonSuccessSse_PreservesInitialStatusAndCompletion()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();

        var sendCount = 0;
        using var client = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            sendCount++;

            return Task.FromResult(sendCount == 1
                ? CreateSseResponse("data: [DONE]\n\n")
                : CreateSseResponse("data: {\"error\":\"busy\"}\n\n", HttpStatusCode.TooManyRequests));
        }))
        {
            BaseAddress = new Uri("http://upstream.test")
        };

        var coordinator = new TrackedRequestCoordinator(client);
        var payload = TrackedRequestPayload.Create("/v1/chat/completions", "application/json", ChatCompletionsRequestJson);

        var result = await coordinator.ReplayAsync(payload);

        Assert.Equal(200, result.StatusCode);
        Assert.True(result.SawCompletion);
        Assert.True(result.SawInitialResponse);
        Assert.Equal(2, sendCount);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Contains(interaction!.Diagnostics, diagnostic =>
            diagnostic.Code == "force_continue_http_status" &&
            diagnostic.Attempt == 1 &&
            diagnostic.StatusCode == 429);
    }

    [Fact]
    public async Task ReplayAsync_WhenFollowUpRequestSucceeds_RecordsStructuredContinuationAttemptAndOutcome()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();

        var sendCount = 0;
        using var client = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            sendCount++;

            return Task.FromResult(sendCount == 1
                ? CreateSseResponse("data: [DONE]\n\n")
                : CreateSseResponse(
                    "data: {\"choices\":[{\"delta\":{\"content\":\"continued\"},\"finish_reason\":\"stop\"}]}\n\n" +
                    "data: [DONE]\n\n"));
        }))
        {
            BaseAddress = new Uri("http://upstream.test")
        };

        var coordinator = new TrackedRequestCoordinator(client);
        var payload = TrackedRequestPayload.Create("/v1/chat/completions", "application/json", ChatCompletionsRequestJson);

        var result = await coordinator.ReplayAsync(payload);

        Assert.Equal(200, result.StatusCode);
        Assert.True(result.SawCompletion);
        Assert.True(result.SawInitialResponse);
        Assert.Equal(2, sendCount);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Contains(interaction!.Diagnostics, diagnostic =>
            diagnostic.Code == "force_continue_sent" &&
            diagnostic.Attempt == 1);
        Assert.Contains(interaction.Diagnostics, diagnostic =>
            diagnostic.Code == "force_continue_merged" &&
            diagnostic.Attempt == 1);
    }

    [Fact]
    public async Task ReplayAsync_WhenTrackedTimeoutHitsBeforeInitialResponse_ReturnsGatewayTimeout()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();

        using var client = new HttpClient(new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }))
        {
            BaseAddress = new Uri("http://upstream.test")
        };

        var coordinator = new TrackedRequestCoordinator(client, CreateProxyOptionsWithTrackedTimeoutSeconds(1));
        var payload = TrackedRequestPayload.Create("/v1/chat/completions", "application/json", ChatCompletionsRequestJson);

        var result = await coordinator.ReplayAsync(payload);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, result.StatusCode);
        Assert.False(result.SawCompletion);
        Assert.False(result.SawInitialResponse);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Contains(interaction!.Diagnostics, diagnostic =>
            diagnostic.Kind == InteractionDiagnosticKind.UpstreamFailure &&
            diagnostic.Code == "tracked_request_timeout");
    }

    [Fact]
    public async Task ReplayAsync_WhenTrackedTimeoutHitsDuringStalledSseReadAfterHeaders_CompletesWithoutHanging()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();

        using var upstream = new StalledSseStream(
            "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]");

        using var client = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(CreateSseResponse(upstream))))
        {
            BaseAddress = new Uri("http://upstream.test")
        };

        var coordinator = new TrackedRequestCoordinator(client, CreateProxyOptionsWithTrackedTimeoutSeconds(1));
        var payload = TrackedRequestPayload.Create("/v1/chat/completions", "application/json", ChatCompletionsRequestJson);
        var replayTask = coordinator.ReplayAsync(payload);

        try
        {
            var stalledReadTask = upstream.WaitForStalledReadAsync();
            var stalledReadCompletedTask = await Task.WhenAny(stalledReadTask, Task.Delay(TimeSpan.FromSeconds(1)));

            Assert.Same(stalledReadTask, stalledReadCompletedTask);

            var completedTask = await Task.WhenAny(replayTask, Task.Delay(TimeSpan.FromSeconds(3)));

            Assert.Same(replayTask, completedTask);

            var result = await replayTask;

            var cancellationObservedTask = upstream.WaitForCancellationAsync();
            var cancellationObservedCompletedTask = await Task.WhenAny(cancellationObservedTask, Task.Delay(TimeSpan.FromSeconds(1)));

            Assert.Same(cancellationObservedTask, cancellationObservedCompletedTask);

            Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
            Assert.False(result.SawCompletion);
            Assert.True(result.SawInitialResponse);

            var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
            Assert.NotNull(interaction);
            Assert.Contains("hello", interaction!.RawOutput.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("data: [DONE]", interaction.RawOutput.ToString(), StringComparison.Ordinal);
            Assert.Contains(interaction.Diagnostics, diagnostic =>
                diagnostic.Kind == InteractionDiagnosticKind.UpstreamFailure &&
                diagnostic.Code == "tracked_request_timeout");
        }
        finally
        {
            upstream.Release();
        }
    }

    [Fact]
    public async Task ProxyAsync_WhenRequestAbortedDuringStalledSseReadAfterHeaders_PropagatesCancellationWithoutTimeoutDiagnostic()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();

        using var upstream = new StalledSseStream(
            "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]");

        using var client = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(CreateSseResponse(upstream))))
        {
            BaseAddress = new Uri("http://upstream.test")
        };

        var coordinator = new TrackedRequestCoordinator(client, CreateProxyOptionsWithTrackedTimeoutSeconds(30));

        using var requestAbortedSource = new CancellationTokenSource();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/chat/completions";
        context.Request.ContentType = "application/json";
        context.RequestAborted = requestAbortedSource.Token;
        context.Response.Body = new MemoryStream();

        var payload = TrackedRequestPayload.Create(
            context.Request.Path.Value!,
            context.Request.ContentType,
            ChatCompletionsRequestJson);

        var proxyTask = coordinator.ProxyAsync(context, payload);

        try
        {
            var stalledReadTask = upstream.WaitForStalledReadAsync();
            var stalledReadCompletedTask = await Task.WhenAny(stalledReadTask, Task.Delay(TimeSpan.FromSeconds(1)));

            Assert.Same(stalledReadTask, stalledReadCompletedTask);

            requestAbortedSource.Cancel();

            var cancellationObservedTask = upstream.WaitForCancellationAsync();
            var cancellationObservedCompletedTask = await Task.WhenAny(cancellationObservedTask, Task.Delay(TimeSpan.FromSeconds(1)));

            Assert.Same(cancellationObservedTask, cancellationObservedCompletedTask);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => proxyTask);
        }
        finally
        {
            upstream.Release();
        }

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var responseReader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var downstreamBody = await responseReader.ReadToEndAsync();

        Assert.Contains("hello", downstreamBody, StringComparison.Ordinal);
        Assert.DoesNotContain("partial", downstreamBody, StringComparison.Ordinal);
        Assert.DoesNotContain("data: [DONE]", downstreamBody, StringComparison.Ordinal);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Equal(200, interaction!.ResponseStatusCode);
        Assert.Contains("hello", interaction.RawOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(interaction.Diagnostics, diagnostic =>
            diagnostic.Code == "tracked_request_timeout" ||
            diagnostic.Code == "upstream_stream_failed");
    }

    [Fact]
    public async Task ReplayAsync_WhenTrackedTimeoutHitsDuringFollowUp_PreservesInitialStatusAndCompletion()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();

        var sendCount = 0;
        using var client = new HttpClient(new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            sendCount++;

            if (sendCount == 1)
            {
                return CreateSseResponse("data: [DONE]\n\n");
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }))
        {
            BaseAddress = new Uri("http://upstream.test")
        };

        var coordinator = new TrackedRequestCoordinator(client, CreateProxyOptionsWithTrackedTimeoutSeconds(1));
        var payload = TrackedRequestPayload.Create("/v1/chat/completions", "application/json", ChatCompletionsRequestJson);

        var result = await coordinator.ReplayAsync(payload);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.True(result.SawCompletion);
        Assert.True(result.SawInitialResponse);
        Assert.Equal(2, sendCount);
    }

    [Fact]
    public async Task ProxyAsync_OverridesTrackedUpstreamHeadersFromConfiguration()
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

        const string upstreamBody = "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\n" +
                       "data: [DONE]\n\n";

        TuiManager.ResetForTests();

        await using var upstream = await TinyLlamaCppMockServer.StartAsync(TinyLlamaCppMockResponse.Sse(upstreamBody));
        var upstreamBaseAddress = new Uri(upstream.BaseAddress, "base/");

        using var client = upstream.CreateClient();
        client.BaseAddress = upstreamBaseAddress;

        var options = ProxyOptions.LoadAndValidate(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Proxy:UpstreamUrl"] = upstreamBaseAddress.ToString(),
                ["Proxy:UpstreamAuth:Scheme"] = "Bearer",
                ["Proxy:UpstreamAuth:Parameter"] = "test-token",
                ["Proxy:UpstreamHeaders:X-Workspace"] = "llamafleece"
            })
            .Build());

        var coordinator = new TrackedRequestCoordinator(
            client,
            options,
            UpstreamRequestHeaderInjection.Create(options));

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/chat/completions";
        context.Request.ContentType = "application/json";
        context.Request.Headers["Authorization"] = "Bearer caller-token";
        context.Request.Headers["X-Workspace"] = "caller";
        context.Response.Body = new MemoryStream();

        var payload = TrackedRequestPayload.Create(
            context.Request.Path.Value!,
            context.Request.ContentType,
            requestJson);

        await coordinator.ProxyAsync(context, payload);

    var capturedRequest = Assert.Single(upstream.Requests);
    Assert.Equal("POST", capturedRequest.Method);
    Assert.Equal("/base/v1/chat/completions", capturedRequest.Path);
    Assert.True(capturedRequest.Headers.TryGetValue("Authorization", out var authorizationValues));
    Assert.Equal("Bearer test-token", Assert.Single(authorizationValues));
    Assert.True(capturedRequest.Headers.TryGetValue("X-Workspace", out var workspaceValues));
    Assert.Equal("llamafleece", Assert.Single(workspaceValues));

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Contains(
            interaction!.ForwardedRequestMutations,
            mutation => mutation.Kind == ForwardedRequestMutationKind.UpstreamAuthorizationInjection);
        Assert.Contains(
            interaction.ForwardedRequestMutations,
            mutation => mutation.Kind == ForwardedRequestMutationKind.UpstreamHeaderOverrides && mutation.Count == 1);

        var inputText = string.Join(Environment.NewLine, interaction.InputLines);
        Assert.DoesNotContain("Forwarded request changed", inputText, StringComparison.Ordinal);
        Assert.DoesNotContain("test-token", inputText, StringComparison.Ordinal);
        Assert.DoesNotContain("llamafleece", inputText, StringComparison.Ordinal);
    }

    private static HttpResponseMessage CreateSseResponse(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
        };
    }

    private static HttpResponseMessage CreateSseResponse(Stream body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StreamContent(body)
            {
                Headers =
                {
                    ContentType = new("text/event-stream")
                }
            }
        };
    }

    private static ProxyOptions CreateProxyOptionsWithTrackedTimeoutSeconds(int trackedTimeoutSeconds)
    {
        return ProxyOptions.LoadAndValidate(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Proxy:UpstreamUrl"] = "http://upstream.test",
                ["Proxy:Timeouts:TrackedRequestSeconds"] = trackedTimeoutSeconds.ToString(),
            })
            .Build());
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        if (request.Content is not null)
        {
            var content = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            clone.Content = new StringContent(content, Encoding.UTF8);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        return clone;
    }

    private sealed class StalledSseStream : Stream
    {
        private readonly byte[] _initialBytes;
        private readonly TaskCompletionSource _stalledReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _position;

        public StalledSseStream(string initialBody)
        {
            _initialBytes = Encoding.UTF8.GetBytes(initialBody);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _initialBytes.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public Task WaitForStalledReadAsync()
        {
            return _stalledReadStarted.Task;
        }

        public Task WaitForCancellationAsync()
        {
            return _cancellationObserved.Task;
        }

        public void Release()
        {
            _releaseReads.TrySetResult();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position < _initialBytes.Length)
            {
                var bytesToCopy = Math.Min(count, _initialBytes.Length - _position);
                _initialBytes.AsSpan(_position, bytesToCopy).CopyTo(buffer.AsSpan(offset, bytesToCopy));
                _position += bytesToCopy;
                return bytesToCopy;
            }

            _stalledReadStarted.TrySetResult();
            _releaseReads.Task.GetAwaiter().GetResult();
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < _initialBytes.Length)
            {
                var bytesToCopy = Math.Min(buffer.Length, _initialBytes.Length - _position);
                _initialBytes.AsMemory(_position, bytesToCopy).CopyTo(buffer);
                _position += bytesToCopy;
                return bytesToCopy;
            }

            _stalledReadStarted.TrySetResult();
            await WaitForReleaseOrCancellationAsync(cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        private async Task WaitForReleaseOrCancellationAsync(CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                await _releaseReads.Task;
                return;
            }

            var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var completedTask = await Task.WhenAny(_releaseReads.Task, cancellationTask);

            if (completedTask == cancellationTask)
            {
                _cancellationObserved.TrySetResult();
                await cancellationTask;
            }

            await _releaseReads.Task;
        }
    }

    private sealed class ThrowAfterBytesStream : Stream
    {
        private readonly byte[] _bytes;
        private readonly int _throwAfterBytes;
        private readonly string _message;
        private int _position;

        public ThrowAfterBytesStream(string body, int throwAfterBytes, string message)
        {
            _bytes = Encoding.UTF8.GetBytes(body);
            _throwAfterBytes = Math.Clamp(throwAfterBytes, 0, _bytes.Length);
            _message = message;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _bytes.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).GetAwaiter().GetResult();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_position < _throwAfterBytes)
            {
                var bytesToCopy = Math.Min(buffer.Length, _throwAfterBytes - _position);
                _bytes.AsMemory(_position, bytesToCopy).CopyTo(buffer);
                _position += bytesToCopy;
                return ValueTask.FromResult(bytesToCopy);
            }

            throw new IOException(_message);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}