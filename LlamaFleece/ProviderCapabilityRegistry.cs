using System.Text.Json;

[Flags]
internal enum ProviderCapability
{
    None = 0,
    RequestPreview = 1 << 0,
    StreamText = 1 << 1,
    StreamReasoning = 1 << 2,
    StreamToolCalls = 1 << 3,
    Usage = 1 << 4,
    ProviderTimingMetrics = 1 << 5,
    ForceContinue = 1 << 6,
    SseTransport = 1 << 7
}

internal enum ProviderEventFamily
{
    Unknown,
    OpenAiCompatible,
    ResponsesApi,
    AnthropicMessages
}

internal sealed record ProviderCapabilityProfile(
    string Id,
    string DisplayName,
    IReadOnlyList<string> EndpointPaths,
    ProviderEventFamily EventFamily,
    ProviderCapability Capabilities,
    string Notes);

internal static class ProviderCapabilityRegistry
{
    private sealed record EventFamilyDetector(ProviderEventFamily Family, Func<JsonElement, bool> Matches);

    private static readonly HashSet<string> AnthropicEventTypes = new(StringComparer.Ordinal)
    {
        "message_start",
        "content_block_start",
        "content_block_delta",
        "content_block_stop",
        "message_delta",
        "message_stop",
        "ping",
        "error"
    };

    private static readonly ProviderCapabilityProfile[] Profiles =
    {
        new(
            "openai-compatible",
            "OpenAI-compatible chat/completions SSE",
            GetSupportedPaths(InteractionEndpoint.ChatCompletions, InteractionEndpoint.Completions),
            ProviderEventFamily.OpenAiCompatible,
            ProviderCapability.RequestPreview |
            ProviderCapability.StreamText |
            ProviderCapability.StreamReasoning |
            ProviderCapability.StreamToolCalls |
            ProviderCapability.Usage |
            ProviderCapability.ForceContinue |
            ProviderCapability.SseTransport,
            "Parses choices[].delta content, reasoning_content, tool_calls, and usage blocks."),

        new(
            "openai-responses",
            "OpenAI Responses API SSE",
            GetSupportedPaths(InteractionEndpoint.Responses),
            ProviderEventFamily.ResponsesApi,
            ProviderCapability.RequestPreview |
            ProviderCapability.StreamText |
            ProviderCapability.StreamReasoning |
            ProviderCapability.StreamToolCalls |
            ProviderCapability.Usage |
            ProviderCapability.ForceContinue |
            ProviderCapability.SseTransport,
            "Parses response.* events for output text, reasoning, tool calls, and usage."),

        new(
            "ollama-openai-compatible",
            "Ollama OpenAI-compatible SSE metadata",
            GetSupportedPaths(InteractionEndpoint.ChatCompletions, InteractionEndpoint.Completions),
            ProviderEventFamily.OpenAiCompatible,
            ProviderCapability.RequestPreview |
            ProviderCapability.StreamText |
            ProviderCapability.StreamToolCalls |
            ProviderCapability.Usage |
            ProviderCapability.ProviderTimingMetrics |
            ProviderCapability.ForceContinue |
            ProviderCapability.SseTransport,
            "Reuses OpenAI-compatible delta parsing and also consumes Ollama timing or cache fields when present."),

        new(
            "anthropic-messages",
            "Anthropic Messages SSE",
            GetSupportedPaths(InteractionEndpoint.AnthropicMessages),
            ProviderEventFamily.AnthropicMessages,
            ProviderCapability.RequestPreview |
            ProviderCapability.StreamText |
            ProviderCapability.StreamReasoning |
            ProviderCapability.StreamToolCalls |
            ProviderCapability.Usage |
            ProviderCapability.ForceContinue |
            ProviderCapability.SseTransport,
            "Parses message_start/message_delta plus content_block_* events for text, thinking, and tool use."),
    };

    private static readonly EventFamilyDetector[] EventFamilyDetectors =
    {
        new(ProviderEventFamily.ResponsesApi, HasResponsesShape),
        new(ProviderEventFamily.AnthropicMessages, HasAnthropicShape),
        new(ProviderEventFamily.OpenAiCompatible, HasOpenAiCompatibleShape)
    };

    public static IReadOnlyList<ProviderCapabilityProfile> All => Profiles;

    public static ProviderEventFamily ClassifyStreamFamily(JsonElement root)
    {
        foreach (var detector in EventFamilyDetectors)
        {
            if (detector.Matches(root))
            {
                return detector.Family;
            }
        }

        return ProviderEventFamily.Unknown;
    }

    private static IReadOnlyList<string> GetSupportedPaths(params InteractionEndpoint[] endpoints)
    {
        var paths = new List<string>();

        foreach (var endpoint in endpoints)
        {
            paths.AddRange(InteractionEndpointClassifier.GetSupportedPaths(endpoint));
        }

        return paths;
    }

    private static bool HasResponsesShape(JsonElement root)
    {
        return root.TryGetProperty("type", out var typeProperty) &&
               typeProperty.ValueKind == JsonValueKind.String &&
               (typeProperty.GetString() ?? string.Empty).StartsWith("response.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAnthropicShape(JsonElement root)
    {
        return root.TryGetProperty("type", out var typeProperty) &&
               typeProperty.ValueKind == JsonValueKind.String &&
               AnthropicEventTypes.Contains(typeProperty.GetString() ?? string.Empty);
    }

    private static bool HasOpenAiCompatibleShape(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (root.TryGetProperty("usage", out var usage) &&
            usage.ValueKind == JsonValueKind.Object &&
            HasAnyProperty(usage, "prompt_tokens", "completion_tokens", "total_tokens"))
        {
            return true;
        }

        return root.TryGetProperty("prompt_eval_duration", out _) ||
               root.TryGetProperty("eval_duration", out _) ||
               root.TryGetProperty("total_duration", out _) ||
               root.TryGetProperty("cached_prompt_count", out _);
    }

    private static bool HasAnyProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out _))
            {
                return true;
            }
        }

        return false;
    }
}