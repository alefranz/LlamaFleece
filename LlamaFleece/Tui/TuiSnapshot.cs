internal sealed record class TuiSnapshot
{
    public bool LogMode { get; init; }
    public bool FullscreenMode { get; init; }
    public bool RawMode { get; init; }
    public int ActivePane { get; init; }
    public bool Locked { get; init; }
    public int VisibleIndex { get; init; }
    public int ConsoleWidth { get; init; }
    public int ConsoleHeight { get; init; }
    public int LogScroll { get; init; }
    public List<string> LogEntries { get; init; } = new();
    public List<TuiInteractionSummary> Interactions { get; init; } = new();
    public int TotalPromptTokens { get; init; }
    public int TotalCompletionTokens { get; init; }
    public int OverallTotalTokens { get; init; }
    public DateTime FirstTokenTimeAll { get; init; }
    public DateTime LastTokenTime { get; init; }
    public int TotalInteractionCount { get; init; }
    public int FilteredInteractionCount { get; init; }
    public bool HasActiveFilter { get; init; }
    public bool IsInteractionFilterPromptActive { get; init; }
    public string ActiveFilterSummary { get; init; } = string.Empty;
    public string PendingInteractionFilterQuery { get; init; } = string.Empty;
    public bool IsNamedSavePromptActive { get; init; }
    public string PendingSaveFileName { get; init; } = string.Empty;
    public bool IsFixSelectionPromptActive { get; init; }
    public int FixSelectionIndex { get; init; } = -1;
    public List<TuiFixSelectionItem> FixSelectionItems { get; init; } = new();
    public string ActiveFixesShorthand { get; init; } = string.Empty;
    public string StatusMessage { get; init; } = string.Empty;
    public bool StatusIsError { get; init; }
    public SessionSummary SessionSummary { get; init; } = new();
    public TuiVisibleInteractionSnapshot? VisibleInteraction { get; init; }
}

internal sealed class TuiInteractionSummary
{
    public int Id { get; init; }
    public bool ForceContinueApplied { get; init; }
    public bool HasForwardedRequestMutations { get; init; }
    public bool HasAttentionWorthyForwardedRequestMutations { get; init; }
    public bool HasDiagnostics { get; init; }
    public bool HasAttentionWorthyDiagnostics { get; init; }
}

internal sealed class TuiFixSelectionItem
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Shorthand { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}

internal sealed class TuiVisibleInteractionSnapshot
{
    public string Model { get; init; } = "unknown";
    public string RequestTarget { get; init; } = "unknown";
    public int? ResponseStatusCode { get; init; }
    public string FinishReason { get; init; } = string.Empty;
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
    public int StreamedTokenCount { get; init; }
    public bool IsStreaming { get; init; }
    public DateTime? FirstTokenTime { get; init; }
    public DateTime? EndTime { get; init; }
    public DateTime StartTime { get; init; }
    public int CachedPromptTokens { get; init; }
    public int ReasoningTokens { get; init; }
    public bool HasApiMetrics { get; init; }
    public double? ApiPrefillSpeed { get; init; }
    public double? ApiDecodeSpeed { get; init; }
    public double? ApiLoadDuration { get; init; }
    public double? ApiPromptEvalDuration { get; init; }
    public double? ApiEvalDuration { get; init; }
    public double? ApiTotalDuration { get; init; }
    public bool ForceContinueApplied { get; init; }
    public List<ForwardedRequestMutation> ForwardedRequestMutations { get; init; } = new();
    public List<InteractionDiagnostic> Diagnostics { get; init; } = new();
    public int InputScroll { get; init; }
    public int OutputScroll { get; init; }
    public List<string> InputLines { get; init; } = new();
    public List<OutputSegment> OutputLines { get; init; } = new();
    public string CurrentInputLine { get; init; } = string.Empty;
    public string CurrentOutputLine { get; init; } = string.Empty;
    public OutputSegmentKind CurrentOutputKind { get; init; } = OutputSegmentKind.Text;
    public string RawInputText { get; init; } = string.Empty;
    public string RawOutputText { get; init; } = string.Empty;
    public List<int> InputSectionStarts { get; init; } = new();
    public List<int> OutputSectionStarts { get; init; } = new();
}
