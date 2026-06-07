using Microsoft.AspNetCore.Http;
using Spectre.Console;

internal readonly record struct TrackedRequestExecutionResult(int StatusCode, bool SawCompletion, bool SawInitialResponse);

public sealed class TrackedRequestCoordinator
{
    private static readonly TrackedRequestContinuationPolicy ContinuationPolicy = new();

    private static readonly HashSet<string> IgnoredRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept-Encoding",
        "Connection",
        "Content-Length",
        "Host",
        "Transfer-Encoding"
    };

    private static readonly HashSet<string> IgnoredResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Content-Length",
        "Transfer-Encoding"
    };

    private readonly HttpClient _httpClient;
    private readonly TimeSpan? _trackedRequestTimeout;
    private readonly UpstreamRequestHeaderInjection _upstreamRequestHeaderInjection;

    public TrackedRequestCoordinator(
        HttpClient httpClient,
        ProxyOptions? proxyOptions = null,
        UpstreamRequestHeaderInjection? upstreamRequestHeaderInjection = null)
    {
        _httpClient = httpClient;
        _trackedRequestTimeout = proxyOptions?.GetTrackedRequestTimeout();
        _upstreamRequestHeaderInjection = upstreamRequestHeaderInjection ?? UpstreamRequestHeaderInjection.None;
    }

    internal async Task ProxyAsync(HttpContext context, TrackedRequestPayload initialPayload)
    {
        using var trackedRequestTimeoutSource = CreateTrackedRequestTimeoutSource(context.RequestAborted);
        var requestCancellationToken = trackedRequestTimeoutSource?.Token ?? context.RequestAborted;

        var result = await ExecuteAsync(
            initialPayload,
            context.Request,
            context.Response.Body,
            context.RequestAborted,
            requestCancellationToken,
            trackedRequestTimeoutSource,
            upstreamResponse => CopyResponseStatusAndHeaders(context, upstreamResponse));

        if (!result.SawInitialResponse)
        {
            context.Response.StatusCode = result.StatusCode;
            context.Response.Headers.Remove("transfer-encoding");
            context.Response.Headers.Remove("content-length");
        }
    }

    internal async Task<TrackedRequestExecutionResult> ReplayAsync(
        TrackedRequestPayload initialPayload,
        CancellationToken cancellationToken = default)
    {
        using var trackedRequestTimeoutSource = CreateTrackedRequestTimeoutSource(cancellationToken);
        var requestCancellationToken = trackedRequestTimeoutSource?.Token ?? cancellationToken;

        return await ExecuteAsync(
            initialPayload,
            sourceRequest: null,
            downstream: Stream.Null,
            requestAborted: cancellationToken,
            requestCancellationToken,
            trackedRequestTimeoutSource,
            onInitialResponse: null);
    }

    private async Task<TrackedRequestExecutionResult> ExecuteAsync(
        TrackedRequestPayload initialPayload,
        HttpRequest? sourceRequest,
        Stream downstream,
        CancellationToken requestAborted,
        CancellationToken requestCancellationToken,
        CancellationTokenSource? trackedRequestTimeoutSource,
        Action<HttpResponseMessage>? onInitialResponse)
    {
        var attempt = 0;
        var sawCompletion = false;
        var currentPayload = initialPayload;
        var forceContinueEnabled = IsForceContinueEnabled() && initialPayload.SupportsForceContinue;
        var statusCode = StatusCodes.Status200OK;
        var sawInitialResponse = false;

        try
        {
            while (true)
            {
                TuiManager.AddLatestForwardedRequestMutations(GetForwardedRequestMutations(currentPayload));

                using var upstreamRequest = CreateUpstreamRequest(sourceRequest, currentPayload);
                using var upstreamResponse = await _httpClient.SendAsync(
                    upstreamRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestCancellationToken);

                if (attempt == 0)
                {
                    statusCode = (int)upstreamResponse.StatusCode;
                    TuiManager.SetLatestResponseStatusCode(statusCode);
                    sawInitialResponse = true;
                    onInitialResponse?.Invoke(upstreamResponse);

                    if (!upstreamResponse.IsSuccessStatusCode)
                    {
                        TuiManager.AddLatestInteractionDiagnostics(new[] { InteractionDiagnostic.UpstreamHttpFailure(statusCode) });
                    }
                }

                var isContinuationAttempt = attempt > 0;
                if (!ProxyLoggingStream.CanProcessAsSse(upstreamResponse))
                {
                    if (isContinuationAttempt)
                    {
                        TuiManager.AddLatestInteractionDiagnostics(new[] { InteractionDiagnostic.ContinuationOutcomeNonSse(attempt) });
                        TuiManager.AppendOutputMarkup("[dim][[Fix: Force Continue]] Follow-up response was not an SSE stream; preserving the original completion.[/]");
                        break;
                    }

                    await upstreamResponse.Content.CopyToAsync(downstream, requestCancellationToken);
                    await downstream.FlushAsync(requestCancellationToken);
                    TuiManager.SetLatestResponseStatusCode(statusCode);
                    TuiManager.MarkDone();
                    TuiManager.SetStreaming(false);
                    return new TrackedRequestExecutionResult(statusCode, sawCompletion, sawInitialResponse);
                }

                if (isContinuationAttempt && !upstreamResponse.IsSuccessStatusCode)
                {
                    TuiManager.AddLatestInteractionDiagnostics(new[] { InteractionDiagnostic.ContinuationOutcomeHttpStatus(attempt, (int)upstreamResponse.StatusCode) });
                    TuiManager.AppendOutputMarkup($"[dim][[Fix: Force Continue]] Follow-up request returned {(int)upstreamResponse.StatusCode}; preserving the original completion.[/]");
                    break;
                }

                using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(requestCancellationToken);
                var proxyStream = new ProxyLoggingStream(downstream);
                var result = await proxyStream.CopySseAsync(upstreamStream, requestCancellationToken);
                sawCompletion |= result.SawDone;

                if (ContinuationPolicy.ShouldIssueFollowUp(
                        attempt,
                        forceContinueEnabled,
                        upstreamResponse.IsSuccessStatusCode,
                        result) &&
                    currentPayload.TryCreateForceContinuePayload(out var continuationPayload))
                {
                    var finishInfo = string.IsNullOrWhiteSpace(result.FinishReason)
                        ? string.Empty
                        : $" (finish: {Markup.Escape(result.FinishReason)})";
                    var continuationAttempt = attempt + 1;

                    TuiManager.AppendOutputMarkup($"[dim][[Fix: Force Continue]] Empty response{finishInfo}. Sending follow-up continue request.[/]");
                    TuiManager.MarkForceContinueApplied();
                    TuiManager.AddLatestInteractionDiagnostics(new[]
                    {
                        InteractionDiagnostic.ContinuationAttemptSent(continuationAttempt, result.FinishReason)
                    });

                    currentPayload = continuationPayload;
                    attempt++;
                    continue;
                }

                if (isContinuationAttempt)
                {
                    TuiManager.AddLatestInteractionDiagnostics(new[] { InteractionDiagnostic.ContinuationOutcomeMerged(attempt) });
                }

                if (sawCompletion)
                {
                    await ProxyLoggingStream.WriteCompletionAsync(downstream, requestCancellationToken);
                }

                TuiManager.SetLatestResponseStatusCode(statusCode);
                TuiManager.MarkDone();
                TuiManager.SetStreaming(false);
                return new TrackedRequestExecutionResult(statusCode, sawCompletion, sawInitialResponse);
            }
        }
        catch (OperationCanceledException) when (HasTrackedRequestTimedOut(requestAborted, trackedRequestTimeoutSource))
        {
            if (attempt > 0)
            {
                TuiManager.AddLatestInteractionDiagnostics(new[] { InteractionDiagnostic.ContinuationOutcomeTimeout(attempt) });
                TuiManager.AppendOutputMarkup("[dim][[Fix: Force Continue]] Follow-up request timed out; preserving the original completion.[/]");
            }
            else
            {
                TuiManager.AddLatestInteractionDiagnostics(new[] { InteractionDiagnostic.UpstreamTimeout() });
                TuiManager.AppendOutputMarkup("[dim][[Timeout]] Tracked upstream request timed out.[/]");
            }

            if (!sawInitialResponse)
            {
                statusCode = StatusCodes.Status504GatewayTimeout;
            }

            TuiManager.SetLatestResponseStatusCode(statusCode);
            TuiManager.MarkDone();
            TuiManager.SetStreaming(false);
            return new TrackedRequestExecutionResult(statusCode, sawCompletion, sawInitialResponse);
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (attempt == 0)
        {
            if (!sawInitialResponse)
            {
                statusCode = StatusCodes.Status502BadGateway;
            }

            ReportInitialRequestFailure(currentPayload.RequestEnvelope, ex, sawInitialResponse);
            TuiManager.SetLatestResponseStatusCode(statusCode);
            TuiManager.MarkDone();
            TuiManager.SetStreaming(false);
            return new TrackedRequestExecutionResult(statusCode, sawCompletion, sawInitialResponse);
        }
        catch (Exception ex) when (attempt == 0 && sawInitialResponse)
        {
            ReportInitialRequestFailure(currentPayload.RequestEnvelope, ex, sawInitialResponse: true);
            TuiManager.SetLatestResponseStatusCode(statusCode);
            TuiManager.MarkDone();
            TuiManager.SetStreaming(false);
            return new TrackedRequestExecutionResult(statusCode, sawCompletion, sawInitialResponse);
        }
        catch (Exception ex) when (attempt > 0)
        {
            TuiManager.AddLatestInteractionDiagnostics(new[] { InteractionDiagnostic.ContinuationOutcomeFailure(attempt, ex.Message) });
            TuiManager.AppendOutputMarkup($"[dim][[Fix: Force Continue]] Follow-up request failed: {Markup.Escape(ex.Message)}[/]");
        }

        if (sawCompletion)
        {
            await ProxyLoggingStream.WriteCompletionAsync(downstream, requestCancellationToken);
        }

        TuiManager.SetLatestResponseStatusCode(statusCode);
        TuiManager.MarkDone();
        TuiManager.SetStreaming(false);
        return new TrackedRequestExecutionResult(statusCode, sawCompletion, sawInitialResponse);
    }

    private static void ReportInitialRequestFailure(
        InteractionRequestEnvelope requestEnvelope,
        Exception exception,
        bool sawInitialResponse)
    {
        var requestTarget = requestEnvelope.GetRedactedDisplayTarget();
        var detail = GetFailureDetail(exception);

        string finishReason;
        string message;

        if (sawInitialResponse)
        {
            finishReason = "upstream_stream_failed";
            message = string.IsNullOrWhiteSpace(detail)
                ? $"Upstream stream failed for {requestTarget}."
                : $"Upstream stream failed for {requestTarget}: {detail}";

            TuiManager.AddLatestInteractionDiagnostics(new[]
            {
                InteractionDiagnostic.UpstreamStreamFailed(string.IsNullOrWhiteSpace(detail) ? null : detail)
            });
        }
        else
        {
            finishReason = "upstream_unavailable";
            message = string.IsNullOrWhiteSpace(detail)
                ? $"Upstream provider unreachable for {requestTarget}. Check that the provider is running and reachable."
                : $"Upstream provider unreachable for {requestTarget}. Check that the provider is running and reachable. {detail}";

            TuiManager.AddLatestInteractionDiagnostics(new[]
            {
                InteractionDiagnostic.UpstreamUnavailable(string.IsNullOrWhiteSpace(detail) ? null : detail)
            });
        }

        TuiManager.SetLatestFinishReason(finishReason);
        TuiManager.AppendOutputMarkup($"[bold red]Upstream Error:[/] {Markup.Escape(message)}");
        TuiManager.RecordStatusMessage(message, isError: true);
    }

    private static string GetFailureDetail(Exception exception)
    {
        var detail = string.Empty;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            var currentDetail = current.Message.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
            if (!string.IsNullOrWhiteSpace(currentDetail))
            {
                detail = currentDetail;
            }
        }

        return detail;
    }

    private IReadOnlyList<ForwardedRequestMutation> GetForwardedRequestMutations(TrackedRequestPayload payload)
    {
        return payload.ForwardedRequestMutations
            .Concat(_upstreamRequestHeaderInjection.GetForwardedRequestMutations())
            .ToArray();
    }

    private HttpRequestMessage CreateUpstreamRequest(HttpRequest? sourceRequest, TrackedRequestPayload payload)
    {
        var request = new HttpRequestMessage(new HttpMethod(payload.RequestEnvelope.Method), BuildUpstreamUri(payload.RequestEnvelope))
        {
            Content = new ByteArrayContent(payload.NormalizedBodyBytes)
        };

        if (sourceRequest is not null)
        {
            CopyRequestHeaders(sourceRequest, request);
        }

        _upstreamRequestHeaderInjection.Apply(request);

        if (!request.Content.Headers.Contains("Content-Type"))
        {
            request.Content.Headers.TryAddWithoutValidation("Content-Type", payload.ContentType);
        }

        return request;
    }

    private Uri BuildUpstreamUri(InteractionRequestEnvelope requestEnvelope)
    {
        var baseUri = _httpClient.BaseAddress ?? throw new InvalidOperationException("Tracked request coordinator requires a base address.");
        var builder = new UriBuilder(baseUri);
        var basePath = string.IsNullOrEmpty(builder.Path) || builder.Path == "/"
            ? string.Empty
            : builder.Path.TrimEnd('/');
        var requestPath = string.IsNullOrWhiteSpace(requestEnvelope.Path) ? "/" : requestEnvelope.Path;

        builder.Path = string.IsNullOrEmpty(basePath)
            ? requestPath
            : $"{basePath}{requestPath}";
        builder.Query = requestEnvelope.GetNormalizedQueryString().TrimStart('?');

        return builder.Uri;
    }

    private static void CopyRequestHeaders(HttpRequest source, HttpRequestMessage destination)
    {
        foreach (var header in source.Headers)
        {
            if (IgnoredRequestHeaders.Contains(header.Key))
            {
                continue;
            }

            var values = header.Value.ToArray();
            if (!destination.Headers.TryAddWithoutValidation(header.Key, values))
            {
                destination.Content?.Headers.TryAddWithoutValidation(header.Key, values);
            }
        }
    }

    private static void CopyResponseStatusAndHeaders(HttpContext context, HttpResponseMessage upstreamResponse)
    {
        context.Response.StatusCode = (int)upstreamResponse.StatusCode;

        foreach (var header in upstreamResponse.Headers)
        {
            if (IgnoredResponseHeaders.Contains(header.Key))
            {
                continue;
            }

            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in upstreamResponse.Content.Headers)
        {
            if (IgnoredResponseHeaders.Contains(header.Key))
            {
                continue;
            }

            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        context.Response.Headers.Remove("transfer-encoding");
        context.Response.Headers.Remove("content-length");
    }

    private static bool IsForceContinueEnabled()
    {
        return TuiManager.ActiveFixes.TryGetValue("force_continue", out var fix) && fix.Enabled;
    }

    private CancellationTokenSource? CreateTrackedRequestTimeoutSource(CancellationToken requestAborted)
    {
        if (_trackedRequestTimeout is not { } trackedRequestTimeout)
        {
            return null;
        }

        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        timeoutSource.CancelAfter(trackedRequestTimeout);
        return timeoutSource;
    }

    private static bool HasTrackedRequestTimedOut(
        CancellationToken requestAborted,
        CancellationTokenSource? trackedRequestTimeoutSource)
    {
        return !requestAborted.IsCancellationRequested &&
               trackedRequestTimeoutSource is { IsCancellationRequested: true };
    }
}