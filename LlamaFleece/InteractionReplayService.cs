using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Spectre.Console;

internal interface IInteractionReplayService
{
    void StartReplayVisibleInteraction();
}

internal sealed class InteractionReplayService : IInteractionReplayService
{
    private readonly TrackedRequestCoordinator _trackedRequestCoordinator;
    private readonly string _upstreamDisplay;
    private int _replayInProgress;

    public InteractionReplayService(TrackedRequestCoordinator trackedRequestCoordinator, ProxyOptions proxyOptions)
    {
        ArgumentNullException.ThrowIfNull(trackedRequestCoordinator);
        ArgumentNullException.ThrowIfNull(proxyOptions);

        _trackedRequestCoordinator = trackedRequestCoordinator;
        _upstreamDisplay = proxyOptions.UpstreamUrl ?? proxyOptions.GetUpstreamUri().ToString();
    }

    public void StartReplayVisibleInteraction()
    {
        var sourceInteraction = TuiManager.GetVisibleInteractionSnapshot();
        _ = Task.Run(() => ReplayInteractionAsync(sourceInteraction));
    }

    internal async Task ReplayInteractionAsync(
        Interaction? sourceInteraction,
        CancellationToken cancellationToken = default)
    {
        if (sourceInteraction is null)
        {
            TuiManager.RecordStatusMessage("Replay failed: no visible interaction to replay.", isError: true);
            return;
        }

        if (sourceInteraction.RequestEnvelope is null)
        {
            TuiManager.RecordStatusMessage($"Replay failed: interaction {sourceInteraction.Id} has no captured request envelope.", isError: true);
            return;
        }

        var requestBody = sourceInteraction.ReplayRequestBody.Length > 0
            ? sourceInteraction.ReplayRequestBody.ToString()
            : sourceInteraction.RawInput.ToString();
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            TuiManager.RecordStatusMessage($"Replay failed: interaction {sourceInteraction.Id} has no captured request body.", isError: true);
            return;
        }

        if (Interlocked.CompareExchange(ref _replayInProgress, 1, 0) != 0)
        {
            TuiManager.RecordStatusMessage("Replay already in progress.", isError: true);
            return;
        }

        var requestEnvelope = sourceInteraction.RequestEnvelope.Clone();
        var requestTarget = requestEnvelope.GetRedactedDisplayTarget();
        var stopwatch = Stopwatch.StartNew();
        var replayStarted = false;

        try
        {
            TuiManager.AppendLog($">>> REPLAY {requestEnvelope.Method,-7} {requestTarget} [source={sourceInteraction.Id}]");
            TuiManager.RecordStatusMessage(
                $"Replaying interaction {sourceInteraction.Id} to current upstream {requestTarget}.",
                isError: false,
                appendToLog: false);

            TuiManager.NewSession();
            TuiManager.SelectCurrentInteraction();
            replayStarted = true;

            var replayInteractionId = TuiManager.GetVisibleInteractionSnapshot()?.Id ?? sourceInteraction.Id;

            TuiManager.SetLatestRequestEnvelope(requestEnvelope);
            TuiManager.AppendInput($"[bold magenta]>[/] [bold white]Replay {Markup.Escape(requestEnvelope.Method)} Request[/] to [cyan]{Markup.Escape(requestTarget)}[/]");
            TuiManager.AppendInput($"[dim]Source interaction:[/] [bold]{sourceInteraction.Id}[/] [dim]Current upstream:[/] [blue]{Markup.Escape(_upstreamDisplay)}[/]");
            TuiManager.AppendRawInput(requestBody);
            LoggingMiddleware.AppendStructuredInputPreview(requestTarget, requestBody);

            var payload = TrackedRequestPayload.Create(requestEnvelope, requestBody);
            var result = await _trackedRequestCoordinator.ReplayAsync(payload, cancellationToken);
            stopwatch.Stop();

            var isError = result.StatusCode >= StatusCodes.Status400BadRequest;
            TuiManager.RecordStatusMessage(
                isError
                    ? $"Replay failed with {result.StatusCode} for interaction {sourceInteraction.Id}."
                    : $"Replayed interaction {sourceInteraction.Id} as interaction {replayInteractionId} with {result.StatusCode}.",
                isError,
                appendToLog: false);

            TuiManager.AppendLog($"<<< REPLAY {result.StatusCode,-6} {stopwatch.Elapsed.TotalMilliseconds,8:F0} ms  {requestEnvelope.Method,-7} {requestTarget} [source={sourceInteraction.Id}]");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            if (replayStarted)
            {
                TuiManager.AppendOutputMarkup($"[red]Replay failed: {Markup.Escape(ex.Message)}[/]");
                TuiManager.MarkDone();
                TuiManager.SetStreaming(false);
            }

            TuiManager.RecordStatusMessage($"Replay failed: {ex.Message}", isError: true, appendToLog: false);
            TuiManager.AppendLog($"<<< REPLAY ERROR {requestEnvelope.Method,-7} {requestTarget} [source={sourceInteraction.Id}] {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _replayInProgress, 0);
        }
    }
}