using System.Text;
using System.Text.Json;

internal sealed class ProxyProviderEventParser
{
    private readonly ProxyStreamUiProjector _uiProjector;
    private readonly StreamedToolCallAssembler _toolCallAssembler = new();
    private readonly Dictionary<int, AnthropicContentBlockState> _anthropicContentBlocks = new();
    private readonly Dictionary<int, string> _chatChoiceFinishReasons = new();
    private readonly HashSet<string> _responseTextItemIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _responseReasoningItemIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _responseAppliedMessageItemIds = new(StringComparer.Ordinal);
    private bool _sawAnyResponseTextDelta;
    private bool _sawAnyResponseReasoningDelta;
    private bool _reportedParseFallbackNotice;

    public ProxyProviderEventParser(ProxyStreamUiProjector uiProjector)
    {
        _uiProjector = uiProjector;
    }

    public bool SawDone { get; private set; }

    public bool GotContent { get; private set; }

    public string? FinishReason { get; private set; }

    public void ProcessLine(string line)
    {
        var trimmed = line.TrimEnd();
        if (!trimmed.StartsWith("data: ", StringComparison.Ordinal))
        {
            return;
        }

        var json = trimmed.Substring(6);
        if (json == "[DONE]")
        {
            SawDone = true;
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            _uiProjector.UpdateUsage(doc.RootElement);
            _uiProjector.TrySetApiMetrics(doc);
            var streamFamily = ProviderCapabilityRegistry.ClassifyStreamFamily(doc.RootElement);

            if (streamFamily == ProviderEventFamily.ResponsesApi && TryProcessResponsesEvent(doc.RootElement))
            {
                return;
            }

            if (streamFamily == ProviderEventFamily.AnthropicMessages)
            {
                ProcessAnthropicEvent(doc.RootElement);
                return;
            }

            ProcessChatCompletionsChunk(doc.RootElement);
        }
        catch (JsonException)
        {
            _uiProjector.RecordDiagnostic(InteractionDiagnostic.ParseFallback());
            if (_reportedParseFallbackNotice)
            {
                return;
            }

            _reportedParseFallbackNotice = true;
            _uiProjector.AppendParseFallbackNotice();
        }
    }

    private void ProcessChatCompletionsChunk(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return;
        }

        var fallbackIndex = 0;
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

            ApplyChatCompletionsChoice(choice, choiceIndex);
        }
    }

    private void ApplyChatCompletionsChoice(JsonElement choice, int choiceIndex)
    {
        if (choice.TryGetProperty("finish_reason", out var finishReason) && finishReason.ValueKind == JsonValueKind.String)
        {
            var finishReasonValue = finishReason.GetString();
            if (!string.IsNullOrEmpty(finishReasonValue))
            {
                SetChatChoiceFinishReason(choiceIndex, finishReasonValue);
            }
        }

        if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (delta.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
        {
            AppendReasoningDelta(reasoning.GetString(), null, isResponsesReasoning: false);
        }

        if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
        {
            AppendTextDelta(content.GetString(), null, isResponsesText: false);
        }

        if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            ApplyToolCallUpdates(_toolCallAssembler.Apply(toolCalls, BuildChatChoiceScope(choiceIndex)));
        }
    }

    private bool TryProcessResponsesEvent(JsonElement root)
    {
        if (!TryGetString(root, "type", out var eventType) ||
            !eventType.StartsWith("response.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        switch (eventType)
        {
            case "response.completed":
                ApplyResponseOutputItemsFromResponse(root);
                UpdateResponsesFinishReason(root, defaultReason: "completed");
                return true;

            case "response.incomplete":
                ApplyResponseOutputItemsFromResponse(root);
                UpdateResponsesFinishReason(root, defaultReason: "incomplete");
                return true;

            case "response.output_text.delta":
                AppendTextDelta(GetString(root, "delta"), GetOptionalString(root, "item_id"), isResponsesText: true);
                return true;

            case "response.reasoning_text.delta":
            case "response.reasoning_summary_text.delta":
                AppendReasoningDelta(GetString(root, "delta"), GetOptionalString(root, "item_id"), isResponsesReasoning: true);
                return true;

            case "response.function_call_arguments.delta":
            case "response.custom_tool_call_input.delta":
            case "response.mcp_call_arguments.delta":
                ApplyResponseToolDelta(root);
                return true;

            case "response.output_item.added":
            case "response.output_item.done":
                if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
                {
                    ApplyResponseOutputItem(item, eventType == "response.output_item.done");
                }

                return true;

            case "response.failed":
                ApplyResponseOutputItemsFromResponse(root);
                UpdateResponsesFinishReason(root, defaultReason: "failed");
                AppendResponseFailure(root);
                return true;

            default:
                return true;
        }
    }

    private void ProcessAnthropicEvent(JsonElement root)
    {
        _uiProjector.UpdateAnthropicUsage(root);

        var eventType = GetOptionalString(root, "type") ?? string.Empty;
        switch (eventType)
        {
            case "message_start":
                ApplyAnthropicMessageStart(root);
                return;

            case "content_block_start":
                ApplyAnthropicContentBlockStart(root);
                return;

            case "content_block_delta":
                ApplyAnthropicContentBlockDelta(root);
                return;

            case "message_delta":
                ApplyAnthropicMessageDelta(root);
                return;

            case "message_stop":
                EnsureAnthropicFinishReason("completed");
                return;

            case "error":
                AppendAnthropicError(root);
                return;
        }
    }

    private void ApplyAnthropicMessageStart(JsonElement root)
    {
        if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
        {
            _uiProjector.UpdateAnthropicUsage(message);
        }
    }

    private void ApplyAnthropicContentBlockStart(JsonElement root)
    {
        if (!TryGetInt32(root, "index", out var index) ||
            !root.TryGetProperty("content_block", out var contentBlock) ||
            contentBlock.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var state = new AnthropicContentBlockState
        {
            Id = GetOptionalString(contentBlock, "id"),
            Name = GetOptionalString(contentBlock, "name")
        };

        _anthropicContentBlocks[index] = state;

        var blockType = GetOptionalString(contentBlock, "type") ?? string.Empty;
        switch (blockType)
        {
            case "text":
                AppendTextDelta(GetOptionalString(contentBlock, "text"), null, isResponsesText: false);
                return;

            case "thinking":
                AppendReasoningDelta(
                    GetOptionalString(contentBlock, "thinking") ?? GetOptionalString(contentBlock, "text"),
                    null,
                    isResponsesReasoning: false);
                return;

            case "tool_use":
                var toolName = !string.IsNullOrWhiteSpace(state.Name) ? state.Name : "tool use";
                var toolId = !string.IsNullOrWhiteSpace(state.Id) ? state.Id : $"anthropic-tool-{index}";
                var toolInput = GetOptionalNonEmptyPropertyText(contentBlock, "input");
                ApplyToolCallUpdates(_toolCallAssembler.ApplyResponseToolSnapshot(toolId, null, toolName, toolInput));
                return;
        }
    }

    private void ApplyAnthropicContentBlockDelta(JsonElement root)
    {
        if (!TryGetInt32(root, "index", out var index) ||
            !root.TryGetProperty("delta", out var delta) ||
            delta.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var deltaType = GetOptionalString(delta, "type") ?? string.Empty;
        switch (deltaType)
        {
            case "text_delta":
                AppendTextDelta(GetOptionalString(delta, "text"), null, isResponsesText: false);
                return;

            case "thinking_delta":
                AppendReasoningDelta(GetOptionalString(delta, "thinking"), null, isResponsesReasoning: false);
                return;

            case "input_json_delta":
                var blockState = _anthropicContentBlocks.TryGetValue(index, out var existingState)
                    ? existingState
                    : null;
                var toolId = !string.IsNullOrWhiteSpace(blockState?.Id) ? blockState.Id : $"anthropic-tool-{index}";
                var toolName = !string.IsNullOrWhiteSpace(blockState?.Name) ? blockState.Name : "tool use";
                ApplyToolCallUpdates(_toolCallAssembler.ApplyResponseToolDelta(
                    toolId,
                    null,
                    GetString(delta, "partial_json"),
                    placeholderName: toolName));
                return;
        }
    }

    private void ApplyAnthropicMessageDelta(JsonElement root)
    {
        _uiProjector.UpdateAnthropicUsage(root);

        if (!root.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var stopReason = GetOptionalString(delta, "stop_reason");
        if (!string.IsNullOrWhiteSpace(stopReason))
        {
            SetFinishReason(stopReason);
        }
    }

    private void AppendAnthropicError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var errorType = GetOptionalString(error, "type");
        var message = GetOptionalString(error, "message");

        if (!string.IsNullOrWhiteSpace(errorType))
        {
            SetFinishReason(errorType);
        }

        _uiProjector.RecordDiagnostic(InteractionDiagnostic.UpstreamResponseFailure(errorType, message));

        if (_uiProjector.AppendProviderError(message ?? string.Empty))
        {
            GotContent = true;
        }
    }

    private void EnsureAnthropicFinishReason(string defaultReason)
    {
        if (!string.IsNullOrWhiteSpace(FinishReason))
        {
            return;
        }

        SetFinishReason(defaultReason);
    }

    private void AppendTextDelta(string? delta, string? itemId, bool isResponsesText)
    {
        if (!_uiProjector.AppendTextDelta(delta))
        {
            return;
        }

        GotContent = true;
        if (isResponsesText)
        {
            _sawAnyResponseTextDelta = true;
            if (!string.IsNullOrEmpty(itemId))
            {
                _responseTextItemIds.Add(itemId);
            }
        }
    }

    private void AppendReasoningDelta(string? delta, string? itemId, bool isResponsesReasoning)
    {
        if (!_uiProjector.AppendReasoningDelta(delta))
        {
            return;
        }

        GotContent = true;
        if (isResponsesReasoning)
        {
            _sawAnyResponseReasoningDelta = true;
            if (!string.IsNullOrEmpty(itemId))
            {
                _responseReasoningItemIds.Add(itemId);
            }
        }
    }

    private void ApplyResponseToolDelta(JsonElement root)
    {
        var delta = GetString(root, "delta");
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        var itemId = GetOptionalString(root, "item_id");
        var callId = GetOptionalString(root, "call_id");
        ApplyToolCallUpdates(_toolCallAssembler.ApplyResponseToolDelta(itemId, callId, delta, placeholderName: "tool call"));
    }

    private void ApplyResponseOutputItem(JsonElement item, bool isDone)
    {
        var itemType = GetOptionalString(item, "type") ?? string.Empty;
        switch (itemType)
        {
            case "message" when isDone:
                ApplyResponseMessageItem(item);
                return;

            case "function_call":
                ApplyResponseToolSnapshot(item, "name", "arguments", "function_call");
                return;

            case "custom_tool_call":
                ApplyResponseToolSnapshot(item, "name", "input", "custom_tool_call");
                return;

            case "mcp_call":
                ApplyResponseToolSnapshot(item, "name", "arguments", "mcp_call");
                return;

            case "tool_search_call":
                ApplyResponseToolSnapshot(item, null, "arguments", "tool_search_call");
                return;

            case "local_shell_call":
            case "shell_call":
                ApplyResponseToolSnapshot(item, null, "action", "shell_call");
                return;
        }
    }

    private void ApplyResponseMessageItem(JsonElement item)
    {
        if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var itemId = GetOptionalString(item, "id");
        if (!string.IsNullOrEmpty(itemId) && !_responseAppliedMessageItemIds.Add(itemId))
        {
            return;
        }

        var textParts = new List<string>();
        var reasoningParts = new List<string>();

        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var partType = GetOptionalString(part, "type") ?? string.Empty;
            var text = GetOptionalString(part, "text");
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            switch (partType)
            {
                case "reasoning_text":
                case "summary_text":
                    reasoningParts.Add(text);
                    break;

                default:
                    textParts.Add(text);
                    break;
            }
        }

        if (reasoningParts.Count > 0 && !ShouldSkipResponseReasoning(itemId) &&
            _uiProjector.AppendReasoningBlock(string.Join("\n", reasoningParts)))
        {
            GotContent = true;
        }

        if (textParts.Count > 0 && !ShouldSkipResponseText(itemId) &&
            _uiProjector.AppendTextBlock(string.Join("\n", textParts)))
        {
            GotContent = true;
        }
    }

    private void ApplyResponseOutputItemsFromResponse(JsonElement root)
    {
        if (!root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                ApplyResponseOutputItem(item, isDone: true);
            }
        }
    }

    private bool ShouldSkipResponseText(string? itemId)
    {
        return !string.IsNullOrEmpty(itemId)
            ? _responseTextItemIds.Contains(itemId)
            : _sawAnyResponseTextDelta;
    }

    private bool ShouldSkipResponseReasoning(string? itemId)
    {
        return !string.IsNullOrEmpty(itemId)
            ? _responseReasoningItemIds.Contains(itemId)
            : _sawAnyResponseReasoningDelta;
    }

    private void ApplyResponseToolSnapshot(JsonElement item, string? nameProperty, string argumentsProperty, string fallbackName)
    {
        var itemId = GetOptionalString(item, "id");
        var callId = GetOptionalString(item, "call_id");
        var name = nameProperty is null
            ? fallbackName
            : GetOptionalString(item, nameProperty) ?? fallbackName;
        var arguments = GetOptionalPropertyText(item, argumentsProperty);
        ApplyToolCallUpdates(_toolCallAssembler.ApplyResponseToolSnapshot(itemId, callId, name, arguments));
    }

    private void ApplyToolCallUpdates(IReadOnlyList<OutputSegment> updates)
    {
        if (_uiProjector.ApplyToolCallUpdates(updates))
        {
            GotContent = true;
        }
    }

    private void AppendResponseFailure(JsonElement root)
    {
        if (!root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var errorCode = GetOptionalString(error, "code");
        var message = GetOptionalString(error, "message");
        _uiProjector.RecordDiagnostic(InteractionDiagnostic.UpstreamResponseFailure(errorCode, message));

        if (_uiProjector.AppendResponseFailure(message ?? string.Empty))
        {
            GotContent = true;
        }
    }

    private void UpdateResponsesFinishReason(JsonElement root, string defaultReason)
    {
        if (!root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string? reason = null;

        if (response.TryGetProperty("incomplete_details", out var incompleteDetails) &&
            incompleteDetails.ValueKind == JsonValueKind.Object &&
            TryGetString(incompleteDetails, "reason", out var incompleteReason) &&
            !string.IsNullOrWhiteSpace(incompleteReason))
        {
            reason = incompleteReason;
        }

        if (string.IsNullOrWhiteSpace(reason) &&
            response.TryGetProperty("error", out var error) &&
            error.ValueKind == JsonValueKind.Object &&
            TryGetString(error, "code", out var errorCode) &&
            !string.IsNullOrWhiteSpace(errorCode))
        {
            reason = errorCode;
        }

        if (string.IsNullOrWhiteSpace(reason) &&
            TryGetString(response, "status", out var status) &&
            !string.IsNullOrWhiteSpace(status))
        {
            reason = status;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = defaultReason;
        }

        SetFinishReason(reason);
    }

    private void SetChatChoiceFinishReason(int choiceIndex, string reason)
    {
        _chatChoiceFinishReasons[choiceIndex] = reason;
        SetFinishReason(FormatChatCompletionFinishReason());
    }

    private string FormatChatCompletionFinishReason()
    {
        if (_chatChoiceFinishReasons.Count == 1)
        {
            foreach (var entry in _chatChoiceFinishReasons)
            {
                return entry.Value;
            }
        }

        var orderedChoiceIndices = new List<int>(_chatChoiceFinishReasons.Keys);
        orderedChoiceIndices.Sort();

        var builder = new StringBuilder();
        for (var i = 0; i < orderedChoiceIndices.Count; i++)
        {
            if (i > 0)
            {
                builder.Append("; ");
            }

            var choiceIndex = orderedChoiceIndices[i];
            builder.Append("choice ");
            builder.Append(choiceIndex);
            builder.Append(": ");
            builder.Append(_chatChoiceFinishReasons[choiceIndex]);
        }

        return builder.ToString();
    }

    private void SetFinishReason(string reason)
    {
        FinishReason = reason;
        _uiProjector.SetFinishReason(reason);
    }

    private static string BuildChatChoiceScope(int choiceIndex)
    {
        return $"choice-{choiceIndex}";
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

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var value) ? value : string.Empty;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var value) ? value : null;
    }

    private static string? GetOptionalPropertyText(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Object or JsonValueKind.Array => property.GetRawText(),
            _ => null
        };
    }

    private static string? GetOptionalNonEmptyPropertyText(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(property.GetString()) ? null : property.GetString(),
            JsonValueKind.Object => HasProperties(property) ? property.GetRawText() : null,
            JsonValueKind.Array => property.GetArrayLength() > 0 ? property.GetRawText() : null,
            _ => null
        };
    }

    private static bool HasProperties(JsonElement property)
    {
        var enumerator = property.EnumerateObject();
        return enumerator.MoveNext();
    }

    private sealed class AnthropicContentBlockState
    {
        public string? Id { get; init; }

        public string? Name { get; init; }
    }
}
