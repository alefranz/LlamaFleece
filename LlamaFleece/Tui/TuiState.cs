using System.Text;
using System.Text.Json;
using Spectre.Console;

internal sealed class TuiState
{
    private readonly object _lock = new();
    private readonly InteractionFilterService _interactionFilterService = new();
    private InteractionExportService _exportService;
    private IInteractionReplayService? _interactionReplayService;
    private SessionSummaryService _sessionSummaryService;
    private InteractionPersistenceService? _persistenceService;
    private readonly List<Interaction> _interactions = new();
    private readonly List<string> _logEntries = new();
    private readonly Dictionary<string, FixInfo> _activeFixes = new()
    {
        ["force_continue"] = new FixInfo("force_continue", "Force Continue on Empty Response", "FC", true)
    };

    private int _visibleInteractionIndex = -1;
    private int _nextId;
    private int _totalPromptTokens;
    private int _totalCompletionTokens;
    private int _overallTotalTokens;
    private int _activePane;
    private int _logScroll;
    private bool _rawMode;
    private bool _locked;
    private bool _fullscreenMode;
    private bool _logMode;
    private DateTime _firstTokenTimeAll = DateTime.MinValue;
    private DateTime _lastTokenTime = DateTime.MinValue;
    private string _statusMessage = string.Empty;
    private bool _statusIsError;
    private InteractionFilter _activeInteractionFilter = InteractionFilter.None;
    private bool _isInteractionFilterPromptActive;
    private string _pendingInteractionFilterQuery = string.Empty;
    private bool _isNamedSavePromptActive;
    private string _pendingSaveFileName = string.Empty;
    private bool _isFixSelectionPromptActive;
    private int _fixSelectionIndex = -1;
    private readonly HashSet<string> _pendingFixSelection = new(StringComparer.Ordinal);
    private long _persistenceRevision;
    private long _lastPersistedRevision;
    private bool _isPersistingSession;
    private string? _lastPersistenceErrorMessage;

    public TuiState(
        InteractionExportService? exportService = null,
        IInteractionReplayService? interactionReplayService = null,
        SessionSummaryService? sessionSummaryService = null,
        InteractionPersistenceService? persistenceService = null)
    {
        _exportService = exportService ?? new InteractionExportService();
        _interactionReplayService = interactionReplayService;
        _sessionSummaryService = sessionSummaryService ?? new SessionSummaryService();
        _persistenceService = persistenceService;
    }

    public void ConfigurePersistence(InteractionPersistenceService? persistenceService, bool clearStatus = false)
    {
        lock (_lock)
        {
            _persistenceService = persistenceService;
            _isPersistingSession = false;
            _lastPersistenceErrorMessage = null;
            if (clearStatus)
            {
                _statusMessage = string.Empty;
                _statusIsError = false;
            }
        }
    }

    public void ConfigureExportService(InteractionExportService exportService, bool clearStatus = false)
    {
        ArgumentNullException.ThrowIfNull(exportService);

        lock (_lock)
        {
            _exportService = exportService;
            if (clearStatus)
            {
                _statusMessage = string.Empty;
                _statusIsError = false;
            }
        }
    }

    public void ConfigureReplayService(IInteractionReplayService? interactionReplayService, bool clearStatus = false)
    {
        lock (_lock)
        {
            _interactionReplayService = interactionReplayService;
            if (clearStatus)
            {
                _statusMessage = string.Empty;
                _statusIsError = false;
            }
        }
    }

    public void ConfigureSessionSummaryService(SessionSummaryService sessionSummaryService, bool clearStatus = false)
    {
        ArgumentNullException.ThrowIfNull(sessionSummaryService);

        lock (_lock)
        {
            _sessionSummaryService = sessionSummaryService;
            if (clearStatus)
            {
                _statusMessage = string.Empty;
                _statusIsError = false;
            }
        }
    }

    public void ConfigureSessionSummaryPricing(ProxyPricingOptions? pricing)
    {
        ConfigureSessionSummaryService(new SessionSummaryService(pricing));
    }

    public void PersistPendingSession()
    {
        TryPersistPendingSession(force: false);
    }

    public int GetInteractionCount()
    {
        lock (_lock)
        {
            return _interactions.Count;
        }
    }

    public Interaction? GetVisibleInteractionSnapshot()
    {
        lock (_lock)
        {
            return TryGetVisibleInteractionLocked() is { } interaction
                ? CloneInteraction(interaction)
                : null;
        }
    }

    public (bool LogMode, int LogScroll, IReadOnlyList<string> Entries) GetLogSnapshot()
    {
        lock (_lock)
        {
            return (_logMode, _logScroll, new List<string>(_logEntries));
        }
    }

    public (string Message, bool IsError) GetStatusSnapshot()
    {
        lock (_lock)
        {
            return (_statusMessage, _statusIsError);
        }
    }

    public void FlushPersistedSession()
    {
        TryPersistPendingSession(force: true);
    }

    public void RestorePersistedSession()
    {
        InteractionPersistenceService? persistenceService;
        lock (_lock)
        {
            persistenceService = _persistenceService;
        }

        if (persistenceService is null)
        {
            return;
        }

        var result = persistenceService.LoadSession();
        if (!result.Found)
        {
            RecordStatusMessageInternal(
                $"Session persistence enabled at {persistenceService.SessionFilePath}; no saved history found.",
                isError: false,
                appendToLog: true,
                trackForPersistence: false);
            return;
        }

        lock (_lock)
        {
            ApplyRestoredSessionLocked(result.Session!);
            _persistenceRevision = 0;
            _lastPersistedRevision = 0;
            _lastPersistenceErrorMessage = null;
        }

        var interactionCount = result.Session!.Interactions.Count;
        var suffix = interactionCount == 1 ? string.Empty : "s";
        RecordStatusMessageInternal(
            $"Restored {interactionCount} persisted interaction{suffix} from {result.FilePath}.",
            isError: false,
            appendToLog: true,
            trackForPersistence: false);
    }

    private InteractionExportSessionSnapshot CreateSessionSnapshotLocked()
    {
        var summary = _sessionSummaryService.BuildSummary(_interactions, _firstTokenTimeAll, _lastTokenTime);
        return InteractionExportService.SnapshotSession(
            _interactions,
            _visibleInteractionIndex,
            _logEntries,
            _activeFixes.Where(kv => kv.Value.Enabled).Select(kv => kv.Key).ToList(),
            summary);
    }

    private void ApplyRestoredSessionLocked(RestoredInteractionSession session)
    {
        _interactions.Clear();
        _interactions.AddRange(session.Interactions);
        _logEntries.Clear();
        _logEntries.AddRange(session.LogEntries);
        _visibleInteractionIndex = session.VisibleInteractionIndex;
        _nextId = session.NextInteractionId;
        _totalPromptTokens = session.TotalPromptTokens;
        _totalCompletionTokens = session.TotalCompletionTokens;
        _overallTotalTokens = session.OverallTotalTokens;
        _firstTokenTimeAll = session.FirstTokenTimeAll;
        _lastTokenTime = session.LastTokenTime;
        _activeInteractionFilter = InteractionFilter.None;

        foreach (var fix in _activeFixes.Values)
        {
            fix.Enabled = session.ActiveFixes.Contains(fix.Key);
        }

        NormalizeVisibleInteractionLocked();
    }

    private void MarkPersistenceDirtyLocked()
    {
        _persistenceRevision++;
    }

    private void TryPersistPendingSession(bool force)
    {
        InteractionPersistenceService? persistenceService;
        InteractionExportSessionSnapshot? snapshot = null;
        long snapshotRevision = 0;

        lock (_lock)
        {
            persistenceService = _persistenceService;
            if (persistenceService is null || _isPersistingSession)
            {
                return;
            }

            if (!force && _persistenceRevision <= _lastPersistedRevision)
            {
                return;
            }

            snapshot = CreateSessionSnapshotLocked();
            snapshotRevision = _persistenceRevision;
            _isPersistingSession = true;
        }

        try
        {
            var result = persistenceService.SaveSession(snapshot, force);
            if (!result.Persisted)
            {
                return;
            }

            lock (_lock)
            {
                _lastPersistedRevision = Math.Max(_lastPersistedRevision, snapshotRevision);
                _lastPersistenceErrorMessage = null;
            }
        }
        catch (Exception ex)
        {
            var message = $"Session persistence failed: {ex.Message}";
            bool shouldReport;

            lock (_lock)
            {
                shouldReport = !string.Equals(_lastPersistenceErrorMessage, message, StringComparison.Ordinal);
                _lastPersistenceErrorMessage = message;
            }

            if (shouldReport)
            {
                RecordStatusMessageInternal(message, isError: true, appendToLog: true, trackForPersistence: false);
            }
        }
        finally
        {
            lock (_lock)
            {
                _isPersistingSession = false;
            }
        }
    }

    public IReadOnlyDictionary<string, FixInfo> ActiveFixes => _activeFixes;

    public int TotalPromptTokens
    {
        get { lock (_lock) { return _totalPromptTokens; } }
        set { lock (_lock) { _totalPromptTokens = value; } }
    }

    public int TotalCompletionTokens
    {
        get { lock (_lock) { return _totalCompletionTokens; } }
        set { lock (_lock) { _totalCompletionTokens = value; } }
    }

    public int OverallTotalTokens
    {
        get { lock (_lock) { return _overallTotalTokens; } }
        set { lock (_lock) { _overallTotalTokens = value; } }
    }

    public int ActivePane
    {
        get { lock (_lock) { return _activePane; } }
        set { lock (_lock) { _activePane = Math.Clamp(value, 0, 2); } }
    }

    public bool RawMode
    {
        get { lock (_lock) { return _rawMode; } }
        set { lock (_lock) { _rawMode = value; } }
    }

    public bool Locked
    {
        get { lock (_lock) { return _locked; } }
        set { lock (_lock) { _locked = value; } }
    }

    public bool FullscreenMode
    {
        get { lock (_lock) { return _fullscreenMode; } }
        set { lock (_lock) { _fullscreenMode = value; } }
    }

    public bool LogMode
    {
        get { lock (_lock) { return _logMode; } }
        set { lock (_lock) { _logMode = value; } }
    }

    public int GetLatestPromptTokens()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().PromptTokens;
        }
    }

    public void SetLatestPromptTokens(int value)
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            _totalPromptTokens += value - latest.PromptTokens;
            latest.PromptTokens = value;
            TrySelectLatestInteractionWhenUnlockedLocked();
            MarkPersistenceDirtyLocked();
        }
    }

    public int GetLatestCompletionTokens()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().CompletionTokens;
        }
    }

    public void SetLatestCompletionTokens(int value)
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            _totalCompletionTokens += value - latest.CompletionTokens;
            latest.CompletionTokens = value;
            TrySelectLatestInteractionWhenUnlockedLocked();
            MarkPersistenceDirtyLocked();
        }
    }

    public int GetLatestTotalTokens()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().TotalTokens;
        }
    }

    public void SetLatestTotalTokens(int value)
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            _overallTotalTokens += value - latest.TotalTokens;
            latest.TotalTokens = value;
            TrySelectLatestInteractionWhenUnlockedLocked();
            MarkPersistenceDirtyLocked();
        }
    }

    public int GetLatestCachedPromptTokens()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().CachedPromptTokens;
        }
    }

    public void SetLatestCachedPromptTokens(int value)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().CachedPromptTokens = value;
            MarkPersistenceDirtyLocked();
        }
    }

    public int GetLatestReasoningTokens()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().ReasoningTokens;
        }
    }

    public void SetLatestReasoningTokens(int value)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().ReasoningTokens = value;
            MarkPersistenceDirtyLocked();
        }
    }

    public bool GetLatestHasApiMetrics()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().HasApiMetrics;
        }
    }

    public void SetLatestHasApiMetrics(bool value)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().HasApiMetrics = value;
            MarkPersistenceDirtyLocked();
        }
    }

    public double? GetLatestApiPrefillSpeed()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().ApiPrefillSpeed;
        }
    }

    public void SetLatestApiPrefillSpeed(double? value)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().ApiPrefillSpeed = value;
            MarkPersistenceDirtyLocked();
        }
    }

    public double? GetLatestApiDecodeSpeed()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().ApiDecodeSpeed;
        }
    }

    public void SetLatestApiDecodeSpeed(double? value)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().ApiDecodeSpeed = value;
            MarkPersistenceDirtyLocked();
        }
    }

    public double? GetLatestApiLoadDuration()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().ApiLoadDuration;
        }
    }

    public void SetLatestApiLoadDuration(double? value)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().ApiLoadDuration = value;
            MarkPersistenceDirtyLocked();
        }
    }

    public double? GetLatestApiTotalDuration()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().ApiTotalDuration;
        }
    }

    public void SetLatestApiTotalDuration(double? value)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().ApiTotalDuration = value;
            MarkPersistenceDirtyLocked();
        }
    }

    public int GetLatestStreamedTokenCount()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().StreamedTokenCount;
        }
    }

    public void SetLatestStreamedTokenCount(int value)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().StreamedTokenCount = value;
            MarkPersistenceDirtyLocked();
        }
    }

    public bool GetLatestIsStreaming()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().IsStreaming;
        }
    }

    public void SetLatestIsStreaming(bool value)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().IsStreaming = value;
            MarkPersistenceDirtyLocked();
        }
    }

    public string GetLatestModel()
    {
        lock (_lock)
        {
            return GetLatestInteractionLocked().Model;
        }
    }

    public void SetLatestModel(string value)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().Model = value;
            TrySelectLatestInteractionWhenUnlockedLocked();
            MarkPersistenceDirtyLocked();
        }
    }

    public void SetLatestResponseStatusCode(int? statusCode)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().ResponseStatusCode = statusCode;
            TrySelectLatestInteractionWhenUnlockedLocked();
            MarkPersistenceDirtyLocked();
        }
    }

    public void SetLatestFinishReason(string? finishReason)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().FinishReason = string.IsNullOrWhiteSpace(finishReason) ? null : finishReason;
            TrySelectLatestInteractionWhenUnlockedLocked();
            MarkPersistenceDirtyLocked();
        }
    }

    public void SetLatestRequestEnvelope(InteractionRequestEnvelope requestEnvelope)
    {
        ArgumentNullException.ThrowIfNull(requestEnvelope);

        lock (_lock)
        {
            GetLatestInteractionLocked().RequestEnvelope = requestEnvelope.Clone();
            TrySelectLatestInteractionWhenUnlockedLocked();
            MarkPersistenceDirtyLocked();
        }
    }

    public IReadOnlyList<ForwardedRequestMutation> AddLatestForwardedRequestMutations(IEnumerable<ForwardedRequestMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);

        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            var added = AddForwardedRequestMutationsLocked(latest, mutations);
            if (added.Count > 0)
            {
                TrySelectLatestInteractionWhenUnlockedLocked();
                MarkPersistenceDirtyLocked();
            }

            return added;
        }
    }

    public IReadOnlyList<InteractionDiagnostic> AddLatestInteractionDiagnostics(IEnumerable<InteractionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            var added = AddInteractionDiagnosticsLocked(latest, diagnostics);
            if (added.Count > 0)
            {
                TrySelectLatestInteractionWhenUnlockedLocked();
                MarkPersistenceDirtyLocked();
            }

            return added;
        }
    }

    public string GetActiveFixesShorthand()
    {
        lock (_lock)
        {
            return GetActiveFixesShorthandLocked();
        }
    }

    public void NewSession()
    {
        lock (_lock)
        {
            var interaction = new Interaction
            {
                Id = _nextId++,
                StartTime = DateTime.UtcNow
            };

            _interactions.Add(interaction);
            TrySelectLatestInteractionWhenUnlockedLocked();
            MarkPersistenceDirtyLocked();
        }
    }

    public void MarkDone()
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().EndTime = DateTime.UtcNow;
            MarkPersistenceDirtyLocked();
        }
    }

    public void AppendInputMessage(string color, string role, string content)
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            FlushCurrentInputLine(latest);
            latest.InputSectionStarts.Add(latest.InputLines.Count);

            var firstLinePrefix = $"[bold {color}]{Markup.Escape(role ?? string.Empty)}:[/] ";
            var parts = InteractionSecretRedactor.RedactText(content ?? string.Empty).Split('\n');
            var isFirst = true;

            foreach (var part in parts)
            {
                var linePrefix = isFirst ? firstLinePrefix : string.Empty;
                isFirst = false;
                latest.InputLines.Add(linePrefix + $"[{color}]" + Markup.Escape(part) + "[/]");
            }

            MarkPersistenceDirtyLocked();
        }
    }

    public void AppendRawInput(string text)
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            latest.ReplayRequestBody.Append(text);
            latest.RawInput.Append(InteractionSecretRedactor.RedactRequestBody(text));
            MarkPersistenceDirtyLocked();
        }
    }

    public void AppendRawOutput(string text)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            GetLatestInteractionLocked().RawOutput.Append(InteractionSecretRedactor.RedactResponseBody(text));
            MarkPersistenceDirtyLocked();
        }
    }

    public void AppendInput(string markupLine)
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            FlushCurrentInputLine(latest);
            latest.InputLines.Add(InteractionSecretRedactor.RedactText(markupLine));
            MarkPersistenceDirtyLocked();
        }
    }

    public void AppendOutputRaw(string text)
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            TouchOutputTimingLocked(latest);
            AppendOutputTextLocked(latest, text, OutputSegmentKind.Text);
            MarkPersistenceDirtyLocked();
        }
    }

    public void AppendReasoningOutput(string text)
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            TouchOutputTimingLocked(latest);
            AppendOutputTextLocked(latest, text, OutputSegmentKind.Reasoning);
            MarkPersistenceDirtyLocked();
        }
    }

    public void AppendOutputMarkup(string markupLine)
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            TouchOutputTimingLocked(latest);
            FlushCurrentOutputLine(latest);
            latest.OutputLines.Add(new OutputSegment(OutputSegmentKind.Markup, InteractionSecretRedactor.RedactText(markupLine)));
            MarkPersistenceDirtyLocked();
        }
    }

    public void UpsertOutputSegment(string key, OutputSegmentKind kind, string text)
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            TouchOutputTimingLocked(latest);
            FlushCurrentOutputLine(latest);

            if (latest.OutputSegmentIndices.TryGetValue(key, out var index))
            {
                latest.OutputLines[index] = new OutputSegment(kind, InteractionSecretRedactor.RedactText(text), key);
                MarkPersistenceDirtyLocked();
                return;
            }

            latest.OutputSegmentIndices[key] = latest.OutputLines.Count;
            latest.OutputLines.Add(new OutputSegment(kind, InteractionSecretRedactor.RedactText(text), key));
            MarkPersistenceDirtyLocked();
        }
    }

    public void MarkOutputSectionStart()
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            latest.OutputSectionStarts.Add(latest.OutputLines.Count);
        }
    }

    public void AppendLog(string message)
    {
        lock (_lock)
        {
            AppendLogLocked(message);
            MarkPersistenceDirtyLocked();
        }
    }

    public void TrySetApiMetrics(JsonDocument doc)
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            if (latest.HasApiMetrics)
            {
                return;
            }

            var gotAny = false;

            if (doc.RootElement.TryGetProperty("prompt_eval_duration", out var promptEvalDuration))
            {
                latest.ApiPromptEvalDuration = promptEvalDuration.GetDouble();
                gotAny = true;
            }

            if (doc.RootElement.TryGetProperty("eval_duration", out var evalDuration))
            {
                latest.ApiEvalDuration = evalDuration.GetDouble();
                gotAny = true;
            }

            if (doc.RootElement.TryGetProperty("load_duration", out var loadDuration))
            {
                latest.ApiLoadDuration = loadDuration.GetDouble();
                gotAny = true;
            }

            if (doc.RootElement.TryGetProperty("total_duration", out var totalDuration))
            {
                latest.ApiTotalDuration = totalDuration.GetDouble();
                gotAny = true;
            }

            if (doc.RootElement.TryGetProperty("cached_prompt_count", out var cachedPromptCount))
            {
                latest.CachedPromptTokens = cachedPromptCount.GetInt32();
                gotAny = true;
            }

            if (latest.ApiPromptEvalDuration.HasValue && latest.ApiPromptEvalDuration.Value > 0 && latest.PromptTokens > 0)
            {
                latest.ApiPrefillSpeed = latest.PromptTokens / (latest.ApiPromptEvalDuration.Value / 1_000_000_000.0);
            }

            if (latest.ApiEvalDuration.HasValue && latest.ApiEvalDuration.Value > 0 && latest.CompletionTokens > 0)
            {
                latest.ApiDecodeSpeed = latest.CompletionTokens / (latest.ApiEvalDuration.Value / 1_000_000_000.0);
            }

            if (gotAny)
            {
                latest.HasApiMetrics = true;
                MarkPersistenceDirtyLocked();
            }
        }
    }

    public void MarkForceContinueApplied()
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().ForceContinueApplied = true;
            MarkPersistenceDirtyLocked();
        }
    }

    public void IncrementStreamedToken()
    {
        lock (_lock)
        {
            var latest = GetLatestInteractionLocked();
            latest.StreamedTokenCount++;
            latest.IsStreaming = true;
            MarkPersistenceDirtyLocked();
        }
    }

    public void SetStreaming(bool streaming)
    {
        lock (_lock)
        {
            GetLatestInteractionLocked().IsStreaming = streaming;
            MarkPersistenceDirtyLocked();
        }
    }

    public void ToggleRawMode()
    {
        lock (_lock)
        {
            _rawMode = !_rawMode;
        }
    }

    public void ToggleLocked()
    {
        lock (_lock)
        {
            _locked = !_locked;
        }
    }

    public void ToggleFullscreenMode()
    {
        lock (_lock)
        {
            _fullscreenMode = !_fullscreenMode;
        }
    }

    public void DisableFullscreenMode()
    {
        lock (_lock)
        {
            _fullscreenMode = false;
        }
    }

    public void ToggleLogMode()
    {
        lock (_lock)
        {
            _logMode = !_logMode;
            if (_logMode)
            {
                _logScroll = 0;
            }
        }
    }

    public void CloseLogMode()
    {
        lock (_lock)
        {
            _logMode = false;
        }
    }

    public void CycleActivePane()
    {
        lock (_lock)
        {
            _activePane = (_activePane + 1) % 3;
        }
    }

    public void SelectPreviousInteraction()
    {
        lock (_lock)
        {
            var activeIndices = GetActiveInteractionIndicesLocked();
            if (activeIndices.Count == 0)
            {
                _visibleInteractionIndex = -1;
                return;
            }

            NormalizeVisibleInteractionLocked();
            var currentPosition = GetVisiblePosition(activeIndices, _visibleInteractionIndex);
            _visibleInteractionIndex = activeIndices[Math.Max(0, currentPosition - 1)];
        }
    }

    public void SelectNextInteraction()
    {
        lock (_lock)
        {
            var activeIndices = GetActiveInteractionIndicesLocked();
            if (activeIndices.Count == 0)
            {
                _visibleInteractionIndex = -1;
                return;
            }

            NormalizeVisibleInteractionLocked();
            var currentPosition = GetVisiblePosition(activeIndices, _visibleInteractionIndex);
            _visibleInteractionIndex = activeIndices[Math.Min(activeIndices.Count - 1, currentPosition + 1)];
        }
    }

    public void SelectCurrentInteraction()
    {
        lock (_lock)
        {
            var activeIndices = GetActiveInteractionIndicesLocked();
            if (activeIndices.Count > 0)
            {
                _visibleInteractionIndex = activeIndices[^1];
            }
            else
            {
                _visibleInteractionIndex = -1;
            }
        }
    }

    public void PromptForInteractionFilter()
    {
        lock (_lock)
        {
            _isInteractionFilterPromptActive = true;
            _pendingInteractionFilterQuery = _activeInteractionFilter.QueryText;
        }

        RecordStatusMessage(
            "Editing interaction filter. Type a query and press Enter to apply, or Esc to cancel.",
            isError: false,
            appendToLog: false);
    }

    public void PromptForNamedSave()
    {
        var hasVisibleInteraction = false;

        lock (_lock)
        {
            if (TryGetVisibleInteractionLocked() is not null)
            {
                _isNamedSavePromptActive = true;
                _pendingSaveFileName = string.Empty;
                hasVisibleInteraction = true;
            }
        }

        if (!hasVisibleInteraction)
        {
            RecordStatusMessage("Named save failed: no visible interaction to save.", isError: true, appendToLog: false);
            return;
        }

        RecordStatusMessage(
            "Editing save file name. Invalid filename characters will be replaced with '-'. Press Enter to save, or Esc to cancel.",
            isError: false,
            appendToLog: false);
    }

    internal bool TryHandleNamedSavePromptKey(ConsoleKeyInfo key)
    {
        string? fileNameToSave = null;
        var canceled = false;

        lock (_lock)
        {
            if (!_isNamedSavePromptActive)
            {
                return false;
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    fileNameToSave = _pendingSaveFileName;
                    break;
                case ConsoleKey.Escape:
                    _isNamedSavePromptActive = false;
                    _pendingSaveFileName = string.Empty;
                    canceled = true;
                    break;
                case ConsoleKey.Backspace:
                    if (_pendingSaveFileName.Length > 0)
                    {
                        _pendingSaveFileName = _pendingSaveFileName[..^1];
                    }

                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        _pendingSaveFileName += key.KeyChar;
                    }

                    break;
            }
        }

        if (canceled)
        {
            RecordStatusMessage("Canceled named save.", isError: false, appendToLog: false);
            return true;
        }

        if (fileNameToSave is null)
        {
            return true;
        }

        if (TrySaveVisibleArtifact(fileNameToSave))
        {
            lock (_lock)
            {
                _isNamedSavePromptActive = false;
                _pendingSaveFileName = string.Empty;
            }
        }

        return true;
    }

    internal bool TryHandleInteractionFilterPromptKey(ConsoleKeyInfo key)
    {
        string? queryToApply = null;
        var canceled = false;

        lock (_lock)
        {
            if (!_isInteractionFilterPromptActive)
            {
                return false;
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    queryToApply = _pendingInteractionFilterQuery;
                    break;
                case ConsoleKey.Escape:
                    _isInteractionFilterPromptActive = false;
                    _pendingInteractionFilterQuery = string.Empty;
                    canceled = true;
                    break;
                case ConsoleKey.Backspace:
                    if (_pendingInteractionFilterQuery.Length > 0)
                    {
                        _pendingInteractionFilterQuery = _pendingInteractionFilterQuery[..^1];
                    }

                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        _pendingInteractionFilterQuery += key.KeyChar;
                    }

                    break;
            }
        }

        if (canceled)
        {
            RecordStatusMessage("Canceled interaction filter edit.", isError: false, appendToLog: false);
            return true;
        }

        if (queryToApply is null)
        {
            return true;
        }

        if (ApplyInteractionFilterQuery(queryToApply))
        {
            lock (_lock)
            {
                _isInteractionFilterPromptActive = false;
                _pendingInteractionFilterQuery = string.Empty;
            }
        }

        return true;
    }

    public bool ApplyInteractionFilterQuery(string query)
    {
        try
        {
            int matches;
            int total;
            var filter = _interactionFilterService.Parse(query);

            lock (_lock)
            {
                _activeInteractionFilter = filter;
                ApplyVisibleSelectionAfterFilterChangeLocked();
                matches = GetActiveInteractionIndicesLocked().Count;
                total = _interactions.Count;
            }

            if (filter.IsActive)
            {
                RecordStatusMessage(
                    $"Applied interaction filter ({matches}/{total} matches): {filter.Summary}",
                    isError: false,
                    appendToLog: false);
            }
            else
            {
                RecordStatusMessage(
                    $"Cleared interaction filter. Showing {total} interactions.",
                    isError: false,
                    appendToLog: false);
            }

            return true;
        }
        catch (InteractionFilterParseException ex)
        {
            RecordStatusMessage($"Interaction filter not applied: {ex.Message}", isError: true, appendToLog: false);
            return false;
        }
    }

    public void ClearInteractionFilter()
    {
        int total;
        lock (_lock)
        {
            _activeInteractionFilter = InteractionFilter.None;
            _isInteractionFilterPromptActive = false;
            _pendingInteractionFilterQuery = string.Empty;
            ApplyVisibleSelectionAfterFilterChangeLocked();
            total = _interactions.Count;
        }

        RecordStatusMessage($"Cleared interaction filter. Showing {total} interactions.", isError: false, appendToLog: false);
    }

    public void PromptForFixSelection()
    {
        var hasFixes = false;

        lock (_lock)
        {
            if (_activeFixes.Count == 0)
            {
                _isFixSelectionPromptActive = false;
                _fixSelectionIndex = -1;
                _pendingFixSelection.Clear();
            }
            else
            {
                _isFixSelectionPromptActive = true;
                _fixSelectionIndex = GetInitialFixSelectionIndexLocked();
                _pendingFixSelection.Clear();

                foreach (var fix in _activeFixes.Values.Where(fix => fix.Enabled))
                {
                    _pendingFixSelection.Add(fix.Key);
                }

                hasFixes = true;
            }
        }

        RecordStatusMessage(
            hasFixes
                ? "Editing fixes. Up/Down select, Space toggle, Enter apply, or Esc cancel."
                : "No fixes are available to edit.",
            isError: false,
            appendToLog: false);
    }

    internal bool TryHandleFixSelectionPromptKey(ConsoleKeyInfo key)
    {
        string? statusMessage = null;

        lock (_lock)
        {
            if (!_isFixSelectionPromptActive)
            {
                return false;
            }

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    MoveFixSelectionLocked(-1);
                    break;
                case ConsoleKey.DownArrow:
                    MoveFixSelectionLocked(1);
                    break;
                case ConsoleKey.Spacebar:
                    ToggleSelectedFixPendingStateLocked();
                    break;
                case ConsoleKey.Enter:
                    ApplyFixSelectionLocked(_pendingFixSelection);
                    _isFixSelectionPromptActive = false;
                    _fixSelectionIndex = -1;
                    _pendingFixSelection.Clear();
                    statusMessage = BuildAppliedFixSelectionMessageLocked();
                    break;
                case ConsoleKey.Escape:
                    _isFixSelectionPromptActive = false;
                    _fixSelectionIndex = -1;
                    _pendingFixSelection.Clear();
                    statusMessage = "Canceled fixes edit.";
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            RecordStatusMessage(statusMessage, isError: false, appendToLog: false);
        }

        return true;
    }

    public void ExportVisibleInteraction()
    {
        InteractionExportRecord? interaction = null;

        lock (_lock)
        {
            if (TryGetVisibleInteractionLocked() is { } visible)
            {
                interaction = InteractionExportService.SnapshotInteraction(visible);
            }
        }

        if (interaction is null)
        {
            RecordStatusMessage("Interaction export failed: no visible interaction to export.", isError: true);
            return;
        }

        try
        {
            var result = _exportService.ExportInteraction(interaction);
            RecordStatusMessage($"Exported interaction {interaction.Id} to {result.ArtifactPattern}.", isError: false);
        }
        catch (Exception ex)
        {
            RecordStatusMessage($"Interaction export failed: {ex.Message}", isError: true);
        }
    }

    private bool TrySaveVisibleArtifact(string requestedFileName)
    {
        InteractionExportRecord? interaction = null;
        var activePane = 0;
        var rawMode = false;

        lock (_lock)
        {
            if (TryGetVisibleInteractionLocked() is { } visible)
            {
                interaction = InteractionExportService.SnapshotInteraction(visible);
                activePane = _activePane;
                rawMode = _rawMode;
            }
        }

        if (interaction is null)
        {
            RecordStatusMessage("Named save failed: no visible interaction to save.", isError: true, appendToLog: false);
            return false;
        }

        try
        {
            if (activePane == 0)
            {
                var bundle = _exportService.SaveNamedInteractionArtifacts(requestedFileName, interaction);
                RecordStatusMessage($"Saved interaction slot to {bundle.DisplayPattern}.", isError: false);
                return true;
            }

            NamedSaveArtifactResult result;
            string description;

            if (!rawMode)
            {
                switch (activePane)
                {
                    case 1:
                        result = _exportService.SaveNamedArtifact(
                            category: "input",
                            requestedFileName,
                            extension: ".md",
                            content: InteractionExportService.BuildSavedPaneMarkdown(interaction, NamedSavePane.Input));
                        description = "input pane";
                        break;
                    case 2:
                        result = _exportService.SaveNamedArtifact(
                            category: "output",
                            requestedFileName,
                            extension: ".md",
                            content: InteractionExportService.BuildSavedPaneMarkdown(interaction, NamedSavePane.Output));
                        description = "output pane";
                        break;
                    default:
                        result = _exportService.SaveNamedArtifact(
                            category: "interactions",
                            requestedFileName,
                            extension: ".md",
                            content: InteractionExportService.BuildSavedInteractionMarkdown(interaction));
                        description = "interaction view";
                        break;
                }
            }
            else
            {
                switch (activePane)
                {
                    case 1:
                    {
                        var extension = InteractionExportService.GuessRawArtifactExtension(interaction.RawInput, ".json");
                        result = _exportService.SaveNamedArtifact(
                            category: "input",
                            requestedFileName,
                            extension,
                            interaction.RawInput);
                        description = "raw request";
                        break;
                    }
                    case 2:
                    {
                        var extension = InteractionExportService.GuessRawArtifactExtension(interaction.RawOutput, ".txt");
                        result = _exportService.SaveNamedArtifact(
                            category: "output",
                            requestedFileName,
                            extension,
                            interaction.RawOutput);
                        description = "raw response";
                        break;
                    }
                    default:
                        result = _exportService.SaveNamedArtifact(
                            category: "interactions",
                            requestedFileName,
                            extension: ".md",
                            content: InteractionExportService.BuildSavedInteractionMarkdown(interaction));
                        description = "interaction view";
                        break;
                }
            }

            RecordStatusMessage($"Saved {description} to {result.DisplayPath}.", isError: false);
            return true;
        }
        catch (Exception ex)
        {
            RecordStatusMessage($"Named save failed: {ex.Message}", isError: true, appendToLog: false);
            return false;
        }
    }

    public void ExportSession()
    {
        InteractionExportSessionSnapshot session;

        lock (_lock)
        {
            session = CreateSessionSnapshotLocked();
        }

        try
        {
            var result = _exportService.ExportSession(session);
            RecordStatusMessage($"Exported session to {result.ArtifactPattern}.", isError: false);
        }
        catch (Exception ex)
        {
            RecordStatusMessage($"Session export failed: {ex.Message}", isError: true);
        }
    }

    public void StartReplayVisibleInteraction()
    {
        if (_interactionReplayService is null)
        {
            RecordStatusMessage("Replay unavailable: no replay service configured.", isError: true);
            return;
        }

        _interactionReplayService.StartReplayVisibleInteraction();
    }

    public void ScrollActivePaneUp()
    {
        lock (_lock)
        {
            var visible = TryGetVisibleInteractionLocked();
            if (visible is null)
            {
                return;
            }

            if (_activePane == 1)
            {
                visible.InputScroll++;
            }
            else if (_activePane == 2)
            {
                visible.OutputScroll++;
            }
        }
    }

    public void ScrollActivePaneDown()
    {
        lock (_lock)
        {
            var visible = TryGetVisibleInteractionLocked();
            if (visible is null)
            {
                return;
            }

            if (_activePane == 1)
            {
                visible.InputScroll = Math.Max(0, visible.InputScroll - 1);
            }
            else if (_activePane == 2)
            {
                visible.OutputScroll = Math.Max(0, visible.OutputScroll - 1);
            }
        }
    }

    public void MoveActivePaneToPreviousSection(int viewportLineCount)
    {
        lock (_lock)
        {
            var visible = TryGetVisibleInteractionLocked();
            if (visible is null)
            {
                return;
            }

            if (_activePane == 1)
            {
                var totalLineCount = visible.InputLines.Count + (string.IsNullOrEmpty(visible.CurrentInputLine) ? 0 : 1);
                visible.InputScroll = TuiSectionNavigator.MoveToPreviousSection(totalLineCount, visible.InputScroll, visible.InputSectionStarts, viewportLineCount);
            }
            else if (_activePane == 2)
            {
                var totalLineCount = visible.OutputLines.Count + (string.IsNullOrEmpty(visible.CurrentOutputLine) ? 0 : 1);
                visible.OutputScroll = TuiSectionNavigator.MoveToPreviousSection(totalLineCount, visible.OutputScroll, visible.OutputSectionStarts, viewportLineCount);
            }
        }
    }

    public void MoveActivePaneToNextSection(int viewportLineCount)
    {
        lock (_lock)
        {
            var visible = TryGetVisibleInteractionLocked();
            if (visible is null)
            {
                return;
            }

            if (_activePane == 1)
            {
                var totalLineCount = visible.InputLines.Count + (string.IsNullOrEmpty(visible.CurrentInputLine) ? 0 : 1);
                visible.InputScroll = TuiSectionNavigator.MoveToNextSection(totalLineCount, visible.InputScroll, visible.InputSectionStarts, viewportLineCount);
            }
            else if (_activePane == 2)
            {
                var totalLineCount = visible.OutputLines.Count + (string.IsNullOrEmpty(visible.CurrentOutputLine) ? 0 : 1);
                visible.OutputScroll = TuiSectionNavigator.MoveToNextSection(totalLineCount, visible.OutputScroll, visible.OutputSectionStarts, viewportLineCount);
            }
        }
    }

    public void ScrollLogUp()
    {
        lock (_lock)
        {
            _logScroll++;
        }
    }

    public void ScrollLogDown()
    {
        lock (_lock)
        {
            _logScroll = Math.Max(0, _logScroll - 1);
        }
    }

    public List<FixInfo> GetFixesSnapshot()
    {
        lock (_lock)
        {
            return _activeFixes.Values.Select(fix => fix.Clone()).ToList();
        }
    }

    public void ApplyFixSelection(IReadOnlyCollection<string> selectedKeys)
    {
        lock (_lock)
        {
            ApplyFixSelectionLocked(selectedKeys);
        }
    }

    public TuiSnapshot TakeSnapshot()
    {
        var consoleWidth = TuiLayoutMetrics.ReadConsoleWidth();
        var consoleHeight = TuiLayoutMetrics.ReadConsoleHeight();

        lock (_lock)
        {
            var activeIndices = GetActiveInteractionIndicesLocked();
            NormalizeVisibleInteractionLocked();
            var visibleIndex = _visibleInteractionIndex >= 0
                ? GetVisiblePosition(activeIndices, _visibleInteractionIndex)
                : -1;
            var sessionSummary = _sessionSummaryService.BuildSummary(_interactions, _firstTokenTimeAll, _lastTokenTime);
            var snapshot = new TuiSnapshot
            {
                LogMode = _logMode,
                FullscreenMode = _fullscreenMode,
                RawMode = _rawMode,
                ActivePane = _activePane,
                Locked = _locked,
                VisibleIndex = visibleIndex,
                ConsoleWidth = consoleWidth,
                ConsoleHeight = consoleHeight,
                LogScroll = _logScroll,
                LogEntries = _logMode ? new List<string>(_logEntries) : new List<string>(),
                Interactions = activeIndices.Select(index => _interactions[index]).Select(interaction => new TuiInteractionSummary
                {
                    Id = interaction.Id,
                    ForceContinueApplied = interaction.ForceContinueApplied,
                    HasForwardedRequestMutations = interaction.ForwardedRequestMutations.Count > 0,
                    HasAttentionWorthyForwardedRequestMutations = ForwardedRequestMutation.HasAttentionWorthyChanges(interaction.ForwardedRequestMutations),
                    HasDiagnostics = interaction.Diagnostics.Count > 0,
                    HasAttentionWorthyDiagnostics = InteractionDiagnostic.HasAttentionWorthyEntries(interaction.Diagnostics)
                }).ToList(),
                TotalPromptTokens = _totalPromptTokens,
                TotalCompletionTokens = _totalCompletionTokens,
                OverallTotalTokens = _overallTotalTokens,
                FirstTokenTimeAll = _firstTokenTimeAll,
                LastTokenTime = _lastTokenTime,
                TotalInteractionCount = _interactions.Count,
                FilteredInteractionCount = activeIndices.Count,
                HasActiveFilter = _activeInteractionFilter.IsActive,
                IsInteractionFilterPromptActive = _isInteractionFilterPromptActive,
                ActiveFilterSummary = _activeInteractionFilter.Summary,
                PendingInteractionFilterQuery = _pendingInteractionFilterQuery,
                IsNamedSavePromptActive = _isNamedSavePromptActive,
                PendingSaveFileName = _pendingSaveFileName,
                IsFixSelectionPromptActive = _isFixSelectionPromptActive,
                FixSelectionIndex = _isFixSelectionPromptActive && _activeFixes.Count > 0
                    ? Math.Clamp(_fixSelectionIndex, 0, _activeFixes.Count - 1)
                    : -1,
                FixSelectionItems = _isFixSelectionPromptActive
                    ? _activeFixes.Select(kvp => new TuiFixSelectionItem
                    {
                        Key = kvp.Key,
                        Name = kvp.Value.Name,
                        Shorthand = kvp.Value.Shorthand,
                        Enabled = _pendingFixSelection.Contains(kvp.Key)
                    }).ToList()
                    : new List<TuiFixSelectionItem>(),
                ActiveFixesShorthand = GetActiveFixesShorthandLocked(),
                StatusMessage = _statusMessage,
                StatusIsError = _statusIsError,
                SessionSummary = sessionSummary
            };

            if (_visibleInteractionIndex < 0 || _visibleInteractionIndex >= _interactions.Count)
            {
                return snapshot;
            }

            var visible = _interactions[_visibleInteractionIndex];
            snapshot = snapshot with
            {
                VisibleInteraction = new TuiVisibleInteractionSnapshot
                {
                    Model = visible.Model,
                    RequestTarget = visible.RequestEnvelope?.GetRedactedDisplayTarget() ?? visible.RequestEnvelope?.Path ?? "unknown",
                    ResponseStatusCode = visible.ResponseStatusCode,
                    FinishReason = visible.FinishReason ?? string.Empty,
                    ForwardedRequestMutations = new List<ForwardedRequestMutation>(visible.ForwardedRequestMutations),
                    Diagnostics = visible.Diagnostics.Select(diagnostic => diagnostic.Redact()).ToList(),
                    PromptTokens = visible.PromptTokens,
                    CompletionTokens = visible.CompletionTokens,
                    TotalTokens = visible.TotalTokens,
                    StreamedTokenCount = visible.StreamedTokenCount,
                    IsStreaming = visible.IsStreaming,
                    FirstTokenTime = visible.FirstTokenTime,
                    EndTime = visible.EndTime,
                    StartTime = visible.StartTime,
                    CachedPromptTokens = visible.CachedPromptTokens,
                    ReasoningTokens = visible.ReasoningTokens,
                    HasApiMetrics = visible.HasApiMetrics,
                    ApiPrefillSpeed = visible.ApiPrefillSpeed,
                    ApiDecodeSpeed = visible.ApiDecodeSpeed,
                    ApiLoadDuration = visible.ApiLoadDuration,
                    ApiPromptEvalDuration = visible.ApiPromptEvalDuration,
                    ApiEvalDuration = visible.ApiEvalDuration,
                    ApiTotalDuration = visible.ApiTotalDuration,
                    ForceContinueApplied = visible.ForceContinueApplied,
                    InputScroll = visible.InputScroll,
                    OutputScroll = visible.OutputScroll,
                    InputLines = visible.InputLines.Select(InteractionSecretRedactor.RedactText).ToList(),
                    OutputLines = visible.OutputLines.Select(RedactOutputSegment).ToList(),
                    CurrentInputLine = InteractionSecretRedactor.RedactText(visible.CurrentInputLine),
                    CurrentOutputLine = InteractionSecretRedactor.RedactText(visible.CurrentOutputLine),
                    CurrentOutputKind = visible.CurrentOutputKind,
                    RawInputText = InteractionSecretRedactor.RedactRequestBody(visible.RawInput.ToString()),
                    RawOutputText = InteractionSecretRedactor.RedactResponseBody(visible.RawOutput.ToString()),
                    InputSectionStarts = new List<int>(visible.InputSectionStarts),
                    OutputSectionStarts = new List<int>(visible.OutputSectionStarts)
                }
            };

            return snapshot;
        }
    }

    private Interaction GetLatestInteractionLocked()
    {
        if (_interactions.Count == 0)
        {
            var interaction = new Interaction { Id = _nextId++ };
            _interactions.Add(interaction);
            _visibleInteractionIndex = 0;
        }

        return _interactions[^1];
    }

    private static List<ForwardedRequestMutation> AddForwardedRequestMutationsLocked(
        Interaction interaction,
        IEnumerable<ForwardedRequestMutation> mutations)
    {
        var added = new List<ForwardedRequestMutation>();
        foreach (var mutation in mutations)
        {
            if (interaction.ForwardedRequestMutations.Contains(mutation))
            {
                continue;
            }

            interaction.ForwardedRequestMutations.Add(mutation);
            added.Add(mutation);
        }

        return added;
    }

    private static List<InteractionDiagnostic> AddInteractionDiagnosticsLocked(
        Interaction interaction,
        IEnumerable<InteractionDiagnostic> diagnostics)
    {
        var added = new List<InteractionDiagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic is null)
            {
                continue;
            }

            var normalized = diagnostic.Normalize();
            if (string.IsNullOrWhiteSpace(normalized.Summary) && string.IsNullOrWhiteSpace(normalized.CompactSummary))
            {
                continue;
            }

            var existingIndex = interaction.Diagnostics.FindIndex(existing => existing.CanAggregateWith(normalized));
            if (existingIndex >= 0)
            {
                var existing = interaction.Diagnostics[existingIndex];
                var updated = existing with { Count = Math.Max(1, existing.Count) + Math.Max(1, normalized.Count) };
                interaction.Diagnostics[existingIndex] = updated;
                added.Add(updated);
                continue;
            }

            interaction.Diagnostics.Add(normalized);
            added.Add(normalized);
        }

        return added;
    }

    private void ApplyFixSelectionLocked(IReadOnlyCollection<string> selectedKeys)
    {
        foreach (var kvp in _activeFixes)
        {
            kvp.Value.Enabled = selectedKeys.Contains(kvp.Key, StringComparer.Ordinal);
        }

        MarkPersistenceDirtyLocked();
    }

    private int GetInitialFixSelectionIndexLocked()
    {
        if (_activeFixes.Count == 0)
        {
            return -1;
        }

        var index = 0;
        foreach (var fix in _activeFixes.Values)
        {
            if (fix.Enabled)
            {
                return index;
            }

            index++;
        }

        return 0;
    }

    private void MoveFixSelectionLocked(int delta)
    {
        if (_activeFixes.Count == 0)
        {
            _fixSelectionIndex = -1;
            return;
        }

        if (_fixSelectionIndex < 0 || _fixSelectionIndex >= _activeFixes.Count)
        {
            _fixSelectionIndex = Math.Clamp(_fixSelectionIndex, 0, _activeFixes.Count - 1);
        }

        _fixSelectionIndex = (_fixSelectionIndex + delta + _activeFixes.Count) % _activeFixes.Count;
    }

    private void ToggleSelectedFixPendingStateLocked()
    {
        if (!TryGetSelectedFixKeyLocked(out var selectedFixKey))
        {
            return;
        }

        if (!_pendingFixSelection.Add(selectedFixKey))
        {
            _pendingFixSelection.Remove(selectedFixKey);
        }
    }

    private bool TryGetSelectedFixKeyLocked(out string selectedFixKey)
    {
        selectedFixKey = string.Empty;
        if (_activeFixes.Count == 0)
        {
            _fixSelectionIndex = -1;
            return false;
        }

        if (_fixSelectionIndex < 0 || _fixSelectionIndex >= _activeFixes.Count)
        {
            _fixSelectionIndex = Math.Clamp(_fixSelectionIndex, 0, _activeFixes.Count - 1);
        }

        selectedFixKey = _activeFixes.ElementAt(_fixSelectionIndex).Key;
        return true;
    }

    private string BuildAppliedFixSelectionMessageLocked()
    {
        var enabledFixNames = _activeFixes.Values
            .Where(fix => fix.Enabled)
            .Select(fix => fix.Name)
            .ToList();

        return enabledFixNames.Count > 0
            ? $"Applied fixes: {string.Join(", ", enabledFixNames)}."
            : "Applied fixes: none enabled.";
    }

    private Interaction? TryGetVisibleInteractionLocked()
    {
        if (_interactions.Count == 0)
        {
            return null;
        }

        NormalizeVisibleInteractionLocked();
        return _visibleInteractionIndex >= 0 && _visibleInteractionIndex < _interactions.Count
            ? _interactions[_visibleInteractionIndex]
            : null;
    }

    private Interaction GetVisibleInteractionLocked()
    {
        return TryGetVisibleInteractionLocked() ?? GetLatestInteractionLocked();
    }

    private static Interaction CloneInteraction(Interaction source)
    {
        return new Interaction
        {
            Id = source.Id,
            RequestEnvelope = source.RequestEnvelope?.Clone(),
            ForwardedRequestMutations = new List<ForwardedRequestMutation>(source.ForwardedRequestMutations),
            Diagnostics = new List<InteractionDiagnostic>(source.Diagnostics),
            InputLines = new List<string>(source.InputLines),
            OutputLines = new List<OutputSegment>(source.OutputLines),
            CurrentInputLine = source.CurrentInputLine,
            CurrentOutputLine = source.CurrentOutputLine,
            CurrentOutputKind = source.CurrentOutputKind,
            OutputSegmentIndices = new Dictionary<string, int>(source.OutputSegmentIndices),
            InputSectionStarts = new List<int>(source.InputSectionStarts),
            OutputSectionStarts = new List<int>(source.OutputSectionStarts),
            PromptTokens = source.PromptTokens,
            CompletionTokens = source.CompletionTokens,
            TotalTokens = source.TotalTokens,
            Model = source.Model,
            ResponseStatusCode = source.ResponseStatusCode,
            FinishReason = source.FinishReason,
            ForceContinueApplied = source.ForceContinueApplied,
            CachedPromptTokens = source.CachedPromptTokens,
            ReasoningTokens = source.ReasoningTokens,
            ApiPromptEvalDuration = source.ApiPromptEvalDuration,
            ApiEvalDuration = source.ApiEvalDuration,
            ApiLoadDuration = source.ApiLoadDuration,
            ApiTotalDuration = source.ApiTotalDuration,
            ApiPrefillSpeed = source.ApiPrefillSpeed,
            ApiDecodeSpeed = source.ApiDecodeSpeed,
            HasApiMetrics = source.HasApiMetrics,
            IsStreaming = source.IsStreaming,
            StreamedTokenCount = source.StreamedTokenCount,
            StartTime = source.StartTime,
            FirstTokenTime = source.FirstTokenTime,
            EndTime = source.EndTime,
            RawInput = new StringBuilder(source.RawInput.ToString()),
            ReplayRequestBody = new StringBuilder(source.ReplayRequestBody.ToString()),
            RawOutput = new StringBuilder(source.RawOutput.ToString()),
            InputScroll = source.InputScroll,
            OutputScroll = source.OutputScroll
        };
    }

    private IReadOnlyList<int> GetActiveInteractionIndicesLocked()
    {
        if (_interactions.Count == 0)
        {
            return Array.Empty<int>();
        }

        return _activeInteractionFilter.IsActive
            ? _interactionFilterService.GetMatchingIndices(_interactions, _activeInteractionFilter)
            : Enumerable.Range(0, _interactions.Count).ToList();
    }

    private void NormalizeVisibleInteractionLocked()
    {
        var activeIndices = GetActiveInteractionIndicesLocked();
        if (activeIndices.Count == 0)
        {
            _visibleInteractionIndex = -1;
            return;
        }

        if (_visibleInteractionIndex < 0 || !activeIndices.Contains(_visibleInteractionIndex))
        {
            _visibleInteractionIndex = activeIndices[^1];
        }
    }

    private void TrySelectLatestInteractionWhenUnlockedLocked()
    {
        if (_interactions.Count == 0)
        {
            _visibleInteractionIndex = -1;
            return;
        }

        if (_locked)
        {
            NormalizeVisibleInteractionLocked();
            return;
        }

        var latestIndex = _interactions.Count - 1;
        if (!_activeInteractionFilter.IsActive || _interactionFilterService.Matches(_interactions[latestIndex], _activeInteractionFilter))
        {
            _visibleInteractionIndex = latestIndex;
            return;
        }

        NormalizeVisibleInteractionLocked();
    }

    private void ApplyVisibleSelectionAfterFilterChangeLocked()
    {
        if (!_activeInteractionFilter.IsActive)
        {
            if (_interactions.Count == 0)
            {
                _visibleInteractionIndex = -1;
            }
            else if (!_locked || _visibleInteractionIndex < 0 || _visibleInteractionIndex >= _interactions.Count)
            {
                _visibleInteractionIndex = _interactions.Count - 1;
            }

            return;
        }

        NormalizeVisibleInteractionLocked();
    }

    private static int GetVisiblePosition(IReadOnlyList<int> activeIndices, int visibleIndex)
    {
        for (var i = 0; i < activeIndices.Count; i++)
        {
            if (activeIndices[i] == visibleIndex)
            {
                return i;
            }
        }

        return activeIndices.Count > 0 ? activeIndices.Count - 1 : -1;
    }

    public void RecordStatusMessage(string message, bool isError, bool appendToLog = true)
    {
        RecordStatusMessageInternal(message, isError, appendToLog, trackForPersistence: true);
    }

    private void RecordStatusMessageInternal(string message, bool isError, bool appendToLog, bool trackForPersistence)
    {
        lock (_lock)
        {
            var safeMessage = InteractionSecretRedactor.RedactText(message);
            _statusMessage = safeMessage;
            _statusIsError = isError;
            if (appendToLog)
            {
                AppendLogLocked(safeMessage);
            }

            if (trackForPersistence)
            {
                MarkPersistenceDirtyLocked();
            }
        }
    }

    private void AppendLogLocked(string message)
    {
        var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        _logEntries.Add($"[{timestamp}] {InteractionSecretRedactor.RedactText(message)}");
    }

    private void TouchOutputTimingLocked(Interaction interaction)
    {
        if (interaction.FirstTokenTime == null)
        {
            interaction.FirstTokenTime = DateTime.UtcNow;
        }

        _lastTokenTime = DateTime.UtcNow;
        if (_firstTokenTimeAll == DateTime.MinValue && interaction.FirstTokenTime.HasValue)
        {
            _firstTokenTimeAll = interaction.FirstTokenTime.Value;
        }
    }

    private static void FlushCurrentInputLine(Interaction interaction)
    {
        if (string.IsNullOrEmpty(interaction.CurrentInputLine))
        {
            return;
        }

        interaction.InputLines.Add(interaction.CurrentInputLine);
        interaction.CurrentInputLine = string.Empty;
    }

    private static void FlushCurrentOutputLine(Interaction interaction)
    {
        if (string.IsNullOrEmpty(interaction.CurrentOutputLine))
        {
            return;
        }

        interaction.OutputLines.Add(new OutputSegment(interaction.CurrentOutputKind, interaction.CurrentOutputLine));
        interaction.CurrentOutputLine = string.Empty;
        interaction.CurrentOutputKind = OutputSegmentKind.Text;
    }

    private static void AppendOutputTextLocked(Interaction interaction, string text, OutputSegmentKind kind)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!string.IsNullOrEmpty(interaction.CurrentOutputLine) && interaction.CurrentOutputKind != kind)
        {
            FlushCurrentOutputLine(interaction);
        }

        interaction.CurrentOutputKind = kind;

        var parts = text.Split('\n');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                FlushCurrentOutputLine(interaction);
                interaction.CurrentOutputKind = kind;
            }

            interaction.CurrentOutputLine += parts[i];
            interaction.CurrentOutputLine = InteractionSecretRedactor.RedactText(interaction.CurrentOutputLine);
        }
    }

    private static OutputSegment RedactOutputSegment(OutputSegment segment)
    {
        return segment with { Text = InteractionSecretRedactor.RedactText(segment.Text) };
    }

    private string GetActiveFixesShorthandLocked()
    {
        var activeShorthands = _activeFixes.Values
            .Where(fix => fix.Enabled)
            .Select(fix => fix.Shorthand)
            .ToList();

        return activeShorthands.Count > 0
            ? $" [bold cyan]Fixes: {string.Join(", ", activeShorthands)}[/]"
            : string.Empty;
    }
}
