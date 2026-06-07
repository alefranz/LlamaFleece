using System.Text.Json;
using Spectre.Console;

public static class TuiManager
{
    private sealed class ScopedStateLease : IDisposable
    {
        private readonly TuiState? _previous;
        private bool _disposed;

        public ScopedStateLease(TuiState? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _scopedState.Value = _previous;
            _disposed = true;
        }
    }

    private static readonly TuiRenderer Renderer = new();
    private static readonly TuiKeyboardController KeyboardController = new();
    private static TuiState _globalState = new();
    private static readonly AsyncLocal<TuiState?> _scopedState = new();

    private static TuiState State
    {
        get => _scopedState.Value ?? _globalState;
        set
        {
            if (_scopedState.Value is not null)
            {
                _scopedState.Value = value;
                return;
            }

            _globalState = value;
        }
    }

    internal static IDisposable BeginIsolatedScopeForTests()
    {
        var previous = _scopedState.Value;
        _scopedState.Value = new TuiState();
        return new ScopedStateLease(previous);
    }

    public static IReadOnlyDictionary<string, FixInfo> ActiveFixes => State.ActiveFixes;

    public static int TotalPromptTokens
    {
        get => State.TotalPromptTokens;
        set => State.TotalPromptTokens = value;
    }

    public static int TotalCompletionTokens
    {
        get => State.TotalCompletionTokens;
        set => State.TotalCompletionTokens = value;
    }

    public static int OverallTotalTokens
    {
        get => State.OverallTotalTokens;
        set => State.OverallTotalTokens = value;
    }

    public static int ActivePane
    {
        get => State.ActivePane;
        set => State.ActivePane = value;
    }

    public static bool RawMode
    {
        get => State.RawMode;
        set => State.RawMode = value;
    }

    public static bool Locked
    {
        get => State.Locked;
        set => State.Locked = value;
    }

    public static bool FullscreenMode
    {
        get => State.FullscreenMode;
        set => State.FullscreenMode = value;
    }

    public static bool LogMode
    {
        get => State.LogMode;
        set => State.LogMode = value;
    }

    public static int PromptTokens
    {
        get => State.GetLatestPromptTokens();
        set => State.SetLatestPromptTokens(value);
    }

    public static int CompletionTokens
    {
        get => State.GetLatestCompletionTokens();
        set => State.SetLatestCompletionTokens(value);
    }

    public static int TotalTokens
    {
        get => State.GetLatestTotalTokens();
        set => State.SetLatestTotalTokens(value);
    }

    public static int CachedPromptTokens
    {
        get => State.GetLatestCachedPromptTokens();
        set => State.SetLatestCachedPromptTokens(value);
    }

    public static int ReasoningTokens
    {
        get => State.GetLatestReasoningTokens();
        set => State.SetLatestReasoningTokens(value);
    }

    public static bool HasApiMetrics
    {
        get => State.GetLatestHasApiMetrics();
        set => State.SetLatestHasApiMetrics(value);
    }

    public static double? ApiPrefillSpeed
    {
        get => State.GetLatestApiPrefillSpeed();
        set => State.SetLatestApiPrefillSpeed(value);
    }

    public static double? ApiDecodeSpeed
    {
        get => State.GetLatestApiDecodeSpeed();
        set => State.SetLatestApiDecodeSpeed(value);
    }

    public static double? ApiLoadDuration
    {
        get => State.GetLatestApiLoadDuration();
        set => State.SetLatestApiLoadDuration(value);
    }

    public static double? ApiTotalDuration
    {
        get => State.GetLatestApiTotalDuration();
        set => State.SetLatestApiTotalDuration(value);
    }

    public static int StreamedTokenCount
    {
        get => State.GetLatestStreamedTokenCount();
        set => State.SetLatestStreamedTokenCount(value);
    }

    public static bool IsStreaming
    {
        get => State.GetLatestIsStreaming();
        set => State.SetLatestIsStreaming(value);
    }

    public static string CurrentModel
    {
        get => State.GetLatestModel();
        set => State.SetLatestModel(value);
    }

    public static string GetActiveFixesShorthand()
    {
        return State.GetActiveFixesShorthand();
    }

    internal static void FlushPersistedSession()
    {
        State.FlushPersistedSession();
    }

    internal static void RestorePersistedSession()
    {
        State.RestorePersistedSession();
    }

    public static void SetLatestResponseStatusCode(int? statusCode)
    {
        State.SetLatestResponseStatusCode(statusCode);
    }

    public static void SetLatestFinishReason(string? finishReason)
    {
        State.SetLatestFinishReason(finishReason);
    }

    public static void SetLatestRequestEnvelope(InteractionRequestEnvelope requestEnvelope)
    {
        State.SetLatestRequestEnvelope(requestEnvelope);
    }

    internal static IReadOnlyList<ForwardedRequestMutation> AddLatestForwardedRequestMutations(IEnumerable<ForwardedRequestMutation> mutations)
    {
        return State.AddLatestForwardedRequestMutations(mutations);
    }

    internal static IReadOnlyList<InteractionDiagnostic> AddLatestInteractionDiagnostics(IEnumerable<InteractionDiagnostic> diagnostics)
    {
        return State.AddLatestInteractionDiagnostics(diagnostics);
    }

    public static void NewSession()
    {
        State.NewSession();
    }

    public static void MarkDone()
    {
        State.MarkDone();
    }

    public static void SelectCurrentInteraction()
    {
        State.SelectCurrentInteraction();
    }

    public static void AppendInputMessage(string color, string role, string content)
    {
        State.AppendInputMessage(color, role, content);
    }

    public static void AppendRawInput(string text)
    {
        State.AppendRawInput(text);
    }

    public static void AppendRawOutput(string text)
    {
        State.AppendRawOutput(text);
    }

    public static void AppendRawOutput(char c)
    {
        State.AppendRawOutput(c.ToString());
    }

    public static void AppendInput(string markupLine)
    {
        State.AppendInput(markupLine);
    }

    public static void AppendOutputRaw(string text)
    {
        State.AppendOutputRaw(text);
    }

    public static void AppendReasoningOutput(string text)
    {
        State.AppendReasoningOutput(text);
    }

    public static void AppendOutputMarkup(string markupLine)
    {
        State.AppendOutputMarkup(markupLine);
    }

    public static void UpsertOutputSegment(string key, OutputSegmentKind kind, string text)
    {
        State.UpsertOutputSegment(key, kind, text);
    }

    public static void MarkOutputSectionStart()
    {
        State.MarkOutputSectionStart();
    }

    public static void AppendLog(string message)
    {
        State.AppendLog(message);
    }

    public static void TrySetApiMetrics(JsonDocument doc)
    {
        State.TrySetApiMetrics(doc);
    }

    public static void MarkForceContinueApplied()
    {
        State.MarkForceContinueApplied();
    }

    public static void IncrementStreamedToken()
    {
        State.IncrementStreamedToken();
    }

    public static void SetStreaming(bool streaming)
    {
        State.SetStreaming(streaming);
    }

    internal static void ResetForTests()
    {
        State = new TuiState();
        ApplicationShutdownCoordinator.ResetForTests();
        InteractionFilterPrompt.SetPromptOverrideForTests(null);
    }

    internal static int InteractionCountForTests()
    {
        return State.GetInteractionCount();
    }

    internal static Interaction? GetVisibleInteractionSnapshot()
    {
        return State.GetVisibleInteractionSnapshot();
    }

    internal static Interaction? GetVisibleInteractionSnapshotForTests()
    {
        return GetVisibleInteractionSnapshot();
    }

    internal static TuiSnapshot TakeSnapshotForTests()
    {
        return State.TakeSnapshot();
    }

    internal static async Task<bool> WaitForNextFrameAsync(TimeSpan frameDelay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(frameDelay, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
    }

    public static async Task RunAsync(CancellationToken ct)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Interactions").Size(3),
                new Layout("Input").Size(10),
                new Layout("Output"),
                new Layout("Stats").Size(TuiLayoutMetrics.GetStatsPanelHeight(logMode: false, fullscreenMode: false, fixSelectionPromptActive: false, TuiLayoutMetrics.ReadConsoleWidth()))
            );

        try
        {
            await AnsiConsole.Live(layout)
                .StartAsync(async ctx =>
                {
                    while (!ct.IsCancellationRequested && !ApplicationShutdownCoordinator.IsShutdownRequested)
                    {
                        try
                        {
                            // Keep console reads and Spectre writes on the same thread.
                            // Concurrent console I/O on Windows can leave the live frame stale
                            // until a later keypress forces another terminal update.
                            ProcessPendingKeys();

                            if (ct.IsCancellationRequested || ApplicationShutdownCoordinator.IsShutdownRequested)
                            {
                                break;
                            }

                            Renderer.BuildFrame(layout, State.TakeSnapshot());
                            ctx.Refresh();
                            State.PersistPendingSession();
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            ShowRenderError(layout, ex);
                            ctx.Refresh();
                        }

                        if (!await WaitForNextFrameAsync(TimeSpan.FromMilliseconds(100), ct))
                        {
                            break;
                        }
                    }
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            throw new StartupTerminalInitializationException(ex);
        }
    }

    internal static void HandleKeyForTests(ConsoleKeyInfo key)
    {
        KeyboardController.HandleKeyForTests(key, State);
    }

    internal static void ProcessPendingKeysForTests(IEnumerable<ConsoleKeyInfo> keys, int maxKeysPerFrame = 8)
    {
        ArgumentNullException.ThrowIfNull(keys);

        using var enumerator = keys.GetEnumerator();
        KeyboardController.ProcessPendingKeys(
            State,
            () => enumerator.MoveNext()
                ? (true, enumerator.Current)
                : (false, default),
            maxKeysPerFrame);
    }

    private static void ProcessPendingKeys(int maxKeysPerFrame = 8)
    {
        KeyboardController.ProcessPendingKeys(State, maxKeysPerFrame);
    }

    private static void ShowRenderError(Layout layout, Exception ex)
    {
        var snapshot = State.TakeSnapshot();
        var errorText = Markup.Escape($"{ex.GetType().Name}: {ex.Message}");

        layout["Interactions"].Update(new Panel(new Markup("[bold red]TUI render error[/]"))
            .BorderColor(Color.Red)
            .Expand());
        layout["Input"].Update(new Panel(new Markup("[dim]The live loop caught a frame error and is retrying on the next tick.[/]"))
            .BorderColor(Color.Red)
            .Expand());
        layout["Output"].Update(new Panel(new Markup(errorText))
            .Header("[bold red]Last Error[/]")
            .BorderColor(Color.Red)
            .Expand());
        layout["Stats"].Update(new Panel(new Markup(TuiRenderer.BuildErrorControlsLine(snapshot)))
            .Header("[bold red]TUI Error[/]")
            .Expand());
    }

    internal static (bool LogMode, int LogScroll, IReadOnlyList<string> Entries) GetLogSnapshotForTests()
    {
        return State.GetLogSnapshot();
    }

    internal static (string Message, bool IsError) GetStatusSnapshotForTests()
    {
        return State.GetStatusSnapshot();
    }

    internal static void SetExportServiceForTests(InteractionExportService exportService)
    {
        State.ConfigureExportService(exportService, clearStatus: true);
    }

    internal static void SetSessionSummaryServiceForTests(SessionSummaryService sessionSummaryService)
    {
        State.ConfigureSessionSummaryService(sessionSummaryService, clearStatus: true);
    }

    internal static void SetReplayServiceForTests(IInteractionReplayService? replayService)
    {
        State.ConfigureReplayService(replayService, clearStatus: true);
    }

    internal static void SetReplayService(IInteractionReplayService? replayService)
    {
        State.ConfigureReplayService(replayService);
    }

    internal static void SetPersistenceServiceForTests(InteractionPersistenceService? persistenceService)
    {
        State.ConfigurePersistence(persistenceService, clearStatus: true);
    }

    internal static void ConfigurePersistence(InteractionPersistenceService? persistenceService)
    {
        State.ConfigurePersistence(persistenceService);
    }

    internal static void ConfigureSessionSummaryPricing(ProxyPricingOptions? pricing)
    {
        State.ConfigureSessionSummaryPricing(pricing);
    }

    public static void RecordStatusMessage(string message, bool isError, bool appendToLog = true)
    {
        State.RecordStatusMessage(message, isError, appendToLog);
    }
}
