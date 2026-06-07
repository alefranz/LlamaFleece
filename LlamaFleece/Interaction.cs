using System.Text;

public class Interaction
{
    public int Id { get; set; }
    public InteractionRequestEnvelope? RequestEnvelope { get; set; }
    public List<string> InputLines { get; set; } = new();
    public List<OutputSegment> OutputLines { get; set; } = new();
    public string CurrentInputLine { get; set; } = "";
    public string CurrentOutputLine { get; set; } = "";
    public OutputSegmentKind CurrentOutputKind { get; set; } = OutputSegmentKind.Text;
    public Dictionary<string, int> OutputSegmentIndices { get; set; } = new();

    // Section boundaries for PgUp/PgDn navigation
    // Input: indices marking the start of each role block (user/system/tool messages)
    public List<int> InputSectionStarts { get; set; } = new();
    // Output: indices marking thinking/output section boundaries
    public List<int> OutputSectionStarts { get; set; } = new();

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public string Model { get; set; } = "unknown";
    public int? ResponseStatusCode { get; set; }
    public string? FinishReason { get; set; }
    internal List<ForwardedRequestMutation> ForwardedRequestMutations { get; set; } = new();
    internal List<InteractionDiagnostic> Diagnostics { get; set; } = new();

    // Whether the force-continue fix was applied to this interaction
    public bool ForceContinueApplied { get; set; }

    // API-derived metrics (from usage details in SSE stream)
    // Cached tokens (from OpenAI prompt_tokens_details.cached_tokens or Ollama cached_prompt_count)
    public int CachedPromptTokens { get; set; }
    // Reasoning tokens (from OpenAI completion_tokens_details.reasoning_tokens)
    public int ReasoningTokens { get; set; }

    // API-derived timing (from Ollama response metadata or OpenAI response)
    // Duration values in seconds, from API if available
    public double? ApiPromptEvalDuration { get; set; }
    public double? ApiEvalDuration { get; set; }
    public double? ApiLoadDuration { get; set; }
    public double? ApiTotalDuration { get; set; }

    // API-derived speeds (calculated from API duration + token counts)
    public double? ApiPrefillSpeed { get; set; }  // prompt_tokens / prompt_eval_duration
    public double? ApiDecodeSpeed { get; set; }   // completion_tokens / eval_duration

    // Whether we've received API metrics for this interaction
    public bool HasApiMetrics { get; set; }

    // Real-time streaming state
    public bool IsStreaming { get; set; }
    public int StreamedTokenCount { get; set; }

    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? FirstTokenTime { get; set; }
    public DateTime? EndTime { get; set; }

    public StringBuilder RawInput { get; set; } = new();
    public StringBuilder ReplayRequestBody { get; set; } = new();
    public StringBuilder RawOutput { get; set; } = new();

    public int InputScroll { get; set; }
    public int OutputScroll { get; set; }
}
