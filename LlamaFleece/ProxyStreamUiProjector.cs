using System.Text.Json;
using Spectre.Console;

internal sealed class ProxyStreamUiProjector
{
    private bool _inReasoning;
    private bool _inToolCall;
    private readonly Dictionary<int, UsageSnapshot> _chatChoiceUsage = new();
    private UsageSnapshot _authoritativeUsage;
    private bool _hasAuthoritativeUsage;

    public static void AppendRawOutput(string text)
    {
        TuiManager.AppendRawOutput(text);
    }

    public void SetFinishReason(string reason)
    {
        TuiManager.SetLatestFinishReason(reason);
    }

    public void RecordDiagnostic(InteractionDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        TuiManager.AddLatestInteractionDiagnostics(new[] { diagnostic });
    }

    public void AppendParseFallbackNotice()
    {
        _inReasoning = false;
        _inToolCall = false;
        TuiManager.AppendOutputMarkup("[dim][[Parse Fallback]] Ignored malformed SSE JSON event; raw output forwarding continued.[/]");
    }

    public void TrySetApiMetrics(JsonDocument doc)
    {
        TuiManager.TrySetApiMetrics(doc);
    }

    public void UpdateUsage(JsonElement root)
    {
        if (!TryGetUsage(root, out var usage, out var responsesUsage))
        {
            if (_hasAuthoritativeUsage ||
                !root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            UpdateChoiceUsage(choices);
            return;
        }

        var snapshot = CreateUsageSnapshot(usage, responsesUsage);
        if (!snapshot.HasAnyValue)
        {
            return;
        }

        _authoritativeUsage = _authoritativeUsage.Merge(snapshot);
        _hasAuthoritativeUsage = _authoritativeUsage.HasAnyValue;
        ApplyUsageSnapshot(_authoritativeUsage);
    }

    public void UpdateAnthropicUsage(JsonElement root)
    {
        if (!TryGetAnthropicUsage(root, out var usage))
        {
            return;
        }

        var sawAnyUsage = false;

        if (TryGetInt32(usage, "input_tokens", out var promptTokens))
        {
            TuiManager.PromptTokens = promptTokens;
            sawAnyUsage = true;
        }

        if (TryGetInt32(usage, "output_tokens", out var completionTokens))
        {
            TuiManager.CompletionTokens = completionTokens;
            sawAnyUsage = true;
        }

        if (TryGetInt32(usage, "cache_read_input_tokens", out var cachedPromptTokens))
        {
            TuiManager.CachedPromptTokens = cachedPromptTokens;
            sawAnyUsage = true;
        }

        if (TryGetInt32(usage, "thinking_tokens", out var reasoningTokens))
        {
            TuiManager.ReasoningTokens = reasoningTokens;
            sawAnyUsage = true;
        }

        if (sawAnyUsage)
        {
            TuiManager.TotalTokens = Math.Max(0, TuiManager.PromptTokens) + Math.Max(0, TuiManager.CompletionTokens);
        }
    }

    public bool AppendTextDelta(string? delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return false;
        }

        TuiManager.IncrementStreamedToken();
        if (_inReasoning || _inToolCall)
        {
            TuiManager.MarkOutputSectionStart();
        }

        _inReasoning = false;
        _inToolCall = false;
        TuiManager.AppendOutputRaw(delta);
        return true;
    }

    public bool AppendReasoningDelta(string? delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return false;
        }

        TuiManager.IncrementStreamedToken();
        if (!_inReasoning || _inToolCall)
        {
            TuiManager.MarkOutputSectionStart();
        }

        _inReasoning = true;
        _inToolCall = false;
        TuiManager.AppendReasoningOutput(delta);
        return true;
    }

    public bool AppendReasoningBlock(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (!_inReasoning || _inToolCall)
        {
            TuiManager.MarkOutputSectionStart();
        }

        _inReasoning = true;
        _inToolCall = false;
        TuiManager.AppendReasoningOutput(text);
        return true;
    }

    public bool AppendTextBlock(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (_inReasoning || _inToolCall)
        {
            TuiManager.MarkOutputSectionStart();
        }

        _inReasoning = false;
        _inToolCall = false;
        TuiManager.AppendOutputRaw(text);
        return true;
    }

    public bool ApplyToolCallUpdates(IReadOnlyList<OutputSegment> updates)
    {
        if (updates.Count == 0)
        {
            return false;
        }

        if (!_inToolCall)
        {
            TuiManager.MarkOutputSectionStart();
        }

        _inReasoning = false;
        _inToolCall = true;

        foreach (var update in updates)
        {
            if (!string.IsNullOrEmpty(update.Key))
            {
                TuiManager.UpsertOutputSegment(update.Key!, update.Kind, update.Text);
            }
        }

        return true;
    }

    public bool AppendProviderError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        _inReasoning = false;
        _inToolCall = false;
        TuiManager.AppendOutputMarkup($"[red]Provider error: {Markup.Escape(message)}[/]");
        return true;
    }

    public bool AppendResponseFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        _inReasoning = false;
        _inToolCall = false;
        TuiManager.AppendOutputMarkup($"[red]Response failed: {Markup.Escape(message)}[/]");
        return true;
    }

    private void UpdateChoiceUsage(JsonElement choices)
    {
        var fallbackIndex = 0;
        var updated = false;

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.ValueKind != JsonValueKind.Object)
            {
                fallbackIndex++;
                continue;
            }

            var choiceIndex = TryGetInt32(choice, "index", out var parsedChoiceIndex)
                ? parsedChoiceIndex
                : fallbackIndex;
            fallbackIndex++;

            if (!choice.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var snapshot = CreateUsageSnapshot(usage, responsesUsage: false);
            if (!snapshot.HasAnyValue)
            {
                continue;
            }

            if (_chatChoiceUsage.TryGetValue(choiceIndex, out var existingSnapshot))
            {
                _chatChoiceUsage[choiceIndex] = existingSnapshot.Merge(snapshot);
            }
            else
            {
                _chatChoiceUsage[choiceIndex] = snapshot;
            }

            updated = true;
        }

        if (updated)
        {
            ApplyUsageSnapshot(AggregateUsageSnapshots(_chatChoiceUsage.Values));
        }
    }

    private static UsageSnapshot CreateUsageSnapshot(JsonElement usage, bool responsesUsage)
    {
        var hasPromptTokens = TryGetInt32(usage, responsesUsage ? "input_tokens" : "prompt_tokens", out var promptTokens);
        var hasCompletionTokens = TryGetInt32(usage, responsesUsage ? "output_tokens" : "completion_tokens", out var completionTokens);
        var hasTotalTokens = TryGetInt32(usage, "total_tokens", out var totalTokens);

        var cachedPromptTokens = 0;
        var reasoningTokens = 0;
        var hasCachedPromptTokens = false;
        var hasReasoningTokens = false;

        if (responsesUsage)
        {
            if (usage.TryGetProperty("input_tokens_details", out var inputDetails) && inputDetails.ValueKind == JsonValueKind.Object)
            {
                hasCachedPromptTokens = TryGetInt32(inputDetails, "cached_tokens", out cachedPromptTokens);
            }

            if (usage.TryGetProperty("output_tokens_details", out var outputDetails) && outputDetails.ValueKind == JsonValueKind.Object)
            {
                hasReasoningTokens = TryGetInt32(outputDetails, "reasoning_tokens", out reasoningTokens);
            }
        }
        else
        {
            if (usage.TryGetProperty("prompt_tokens_details", out var promptDetails) && promptDetails.ValueKind == JsonValueKind.Object)
            {
                hasCachedPromptTokens = TryGetInt32(promptDetails, "cached_tokens", out cachedPromptTokens);
            }

            if (usage.TryGetProperty("completion_tokens_details", out var completionDetails) && completionDetails.ValueKind == JsonValueKind.Object)
            {
                hasReasoningTokens = TryGetInt32(completionDetails, "reasoning_tokens", out reasoningTokens);
            }
        }

        return new UsageSnapshot(
            promptTokens,
            completionTokens,
            totalTokens,
            cachedPromptTokens,
            reasoningTokens,
            hasPromptTokens,
            hasCompletionTokens,
            hasTotalTokens,
            hasCachedPromptTokens,
            hasReasoningTokens);
    }

    private static UsageSnapshot AggregateUsageSnapshots(IEnumerable<UsageSnapshot> snapshots)
    {
        var promptTokens = 0;
        var completionTokens = 0;
        var totalTokens = 0;
        var cachedPromptTokens = 0;
        var reasoningTokens = 0;
        var hasPromptTokens = false;
        var hasCompletionTokens = false;
        var hasTotalTokens = false;
        var hasCachedPromptTokens = false;
        var hasReasoningTokens = false;

        foreach (var snapshot in snapshots)
        {
            if (snapshot.HasPromptTokens)
            {
                promptTokens += snapshot.PromptTokens;
                hasPromptTokens = true;
            }

            if (snapshot.HasCompletionTokens)
            {
                completionTokens += snapshot.CompletionTokens;
                hasCompletionTokens = true;
            }

            if (snapshot.HasTotalTokens)
            {
                totalTokens += snapshot.TotalTokens;
                hasTotalTokens = true;
            }

            if (snapshot.HasCachedPromptTokens)
            {
                cachedPromptTokens += snapshot.CachedPromptTokens;
                hasCachedPromptTokens = true;
            }

            if (snapshot.HasReasoningTokens)
            {
                reasoningTokens += snapshot.ReasoningTokens;
                hasReasoningTokens = true;
            }
        }

        if (!hasTotalTokens && (hasPromptTokens || hasCompletionTokens))
        {
            totalTokens = Math.Max(0, promptTokens) + Math.Max(0, completionTokens);
            hasTotalTokens = true;
        }

        return new UsageSnapshot(
            promptTokens,
            completionTokens,
            totalTokens,
            cachedPromptTokens,
            reasoningTokens,
            hasPromptTokens,
            hasCompletionTokens,
            hasTotalTokens,
            hasCachedPromptTokens,
            hasReasoningTokens);
    }

    private static void ApplyUsageSnapshot(UsageSnapshot snapshot)
    {
        if (!snapshot.HasAnyValue)
        {
            return;
        }

        if (snapshot.HasPromptTokens)
        {
            TuiManager.PromptTokens = snapshot.PromptTokens;
        }

        if (snapshot.HasCompletionTokens)
        {
            TuiManager.CompletionTokens = snapshot.CompletionTokens;
        }

        if (snapshot.HasTotalTokens)
        {
            TuiManager.TotalTokens = snapshot.TotalTokens;
        }
        else if (snapshot.HasPromptTokens || snapshot.HasCompletionTokens)
        {
            TuiManager.TotalTokens = Math.Max(0, snapshot.PromptTokens) + Math.Max(0, snapshot.CompletionTokens);
        }

        if (snapshot.HasCachedPromptTokens)
        {
            TuiManager.CachedPromptTokens = snapshot.CachedPromptTokens;
        }

        if (snapshot.HasReasoningTokens)
        {
            TuiManager.ReasoningTokens = snapshot.ReasoningTokens;
        }
    }

    private static bool TryGetUsage(JsonElement root, out JsonElement usage, out bool responsesUsage)
    {
        if (root.TryGetProperty("usage", out usage) && usage.ValueKind == JsonValueKind.Object)
        {
            responsesUsage = false;
            return true;
        }

        if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object &&
            response.TryGetProperty("usage", out usage) && usage.ValueKind == JsonValueKind.Object)
        {
            responsesUsage = true;
            return true;
        }

        usage = default;
        responsesUsage = false;
        return false;
    }

    private static bool TryGetAnthropicUsage(JsonElement root, out JsonElement usage)
    {
        if (root.TryGetProperty("usage", out usage) && usage.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("usage", out usage) && usage.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        usage = default;
        return false;
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number)
        {
            value = property.GetInt32();
            return true;
        }

        value = 0;
        return false;
    }

    private readonly record struct UsageSnapshot(
        int PromptTokens,
        int CompletionTokens,
        int TotalTokens,
        int CachedPromptTokens,
        int ReasoningTokens,
        bool HasPromptTokens,
        bool HasCompletionTokens,
        bool HasTotalTokens,
        bool HasCachedPromptTokens,
        bool HasReasoningTokens)
    {
        public bool HasAnyValue =>
            HasPromptTokens ||
            HasCompletionTokens ||
            HasTotalTokens ||
            HasCachedPromptTokens ||
            HasReasoningTokens;

        public UsageSnapshot Merge(UsageSnapshot other)
        {
            var promptTokens = other.HasPromptTokens ? other.PromptTokens : PromptTokens;
            var completionTokens = other.HasCompletionTokens ? other.CompletionTokens : CompletionTokens;
            var hasPromptTokens = HasPromptTokens || other.HasPromptTokens;
            var hasCompletionTokens = HasCompletionTokens || other.HasCompletionTokens;
            var hasTotalTokens = other.HasTotalTokens
                ? true
                : other.HasPromptTokens || other.HasCompletionTokens
                    ? false
                    : HasTotalTokens;
            var totalTokens = other.HasTotalTokens
                ? other.TotalTokens
                : other.HasPromptTokens || other.HasCompletionTokens
                    ? 0
                    : TotalTokens;

            return new UsageSnapshot(
                promptTokens,
                completionTokens,
                totalTokens,
                other.HasCachedPromptTokens ? other.CachedPromptTokens : CachedPromptTokens,
                other.HasReasoningTokens ? other.ReasoningTokens : ReasoningTokens,
                hasPromptTokens,
                hasCompletionTokens,
                hasTotalTokens,
                HasCachedPromptTokens || other.HasCachedPromptTokens,
                HasReasoningTokens || other.HasReasoningTokens);
        }
    }
}
