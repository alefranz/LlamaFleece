using System.Text;
using System.Text.Json;

internal sealed class StreamedToolCallAssembler
{
    private readonly Dictionary<string, ToolCallState> _toolCalls = new();
    private readonly Dictionary<string, ToolCallState> _responseToolCalls = new(StringComparer.Ordinal);

    public IReadOnlyList<OutputSegment> Apply(JsonElement toolCalls, string? scopeKey = null)
    {
        var updates = new List<OutputSegment>();
        var fallbackIndex = 0;

        foreach (var toolCall in toolCalls.EnumerateArray())
        {
            var index = toolCall.TryGetProperty("index", out var indexProp) && indexProp.ValueKind == JsonValueKind.Number
                ? indexProp.GetInt32()
                : fallbackIndex;
            fallbackIndex++;

            var state = GetOrCreateState(
                BuildStateKey(scopeKey, index),
                BuildPreferredKey(scopeKey, index, TryGetToolCallId(toolCall)));

            if (toolCall.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                state.Id.Append(idProp.GetString());
            }

            if (toolCall.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object)
            {
                if (function.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                {
                    state.Name.Append(nameProp.GetString());
                }

                if (function.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.String)
                {
                    state.Arguments.Append(argsProp.GetString());
                }
            }

            updates.AddRange(BuildUpdates(state.PreferredKey, state));
        }

        return updates;
    }

    public IReadOnlyList<OutputSegment> ApplyResponseToolDelta(string? itemId, string? callId, string delta, string? placeholderName = null)
    {
        var state = GetOrCreateResponseState(itemId, callId);
        if (state.Name.Length == 0 && !string.IsNullOrWhiteSpace(placeholderName))
        {
            state.Name.Append(placeholderName);
        }

        if (!string.IsNullOrEmpty(delta))
        {
            state.Arguments.Append(delta);
        }

        return BuildUpdates(state.PreferredKey, state);
    }

    public IReadOnlyList<OutputSegment> ApplyResponseToolSnapshot(string? itemId, string? callId, string? name, string? arguments)
    {
        var state = GetOrCreateResponseState(itemId, callId);
        if (!string.IsNullOrWhiteSpace(name))
        {
            state.Name.Clear();
            state.Name.Append(name);
        }

        if (arguments is not null)
        {
            state.Arguments.Clear();
            state.Arguments.Append(arguments);
        }

        return BuildUpdates(state.PreferredKey, state);
    }

    private ToolCallState GetOrCreateState(string stateKey, string preferredKey)
    {
        if (_toolCalls.TryGetValue(stateKey, out var state))
        {
            return state;
        }

        state = new ToolCallState
        {
            PreferredKey = preferredKey
        };
        _toolCalls[stateKey] = state;
        return state;
    }

    private ToolCallState GetOrCreateResponseState(string? itemId, string? callId)
    {
        if (TryGetResponseState(itemId, callId, out var state))
        {
            RegisterResponseState(state, itemId, callId);
            return state;
        }

        state = new ToolCallState
        {
            PreferredKey = !string.IsNullOrWhiteSpace(callId)
                ? $"tool-call:{callId}"
                : !string.IsNullOrWhiteSpace(itemId)
                    ? $"tool-call:{itemId}"
                    : "tool-call:response"
        };

        RegisterResponseState(state, itemId, callId);
        return state;
    }

    private bool TryGetResponseState(string? itemId, string? callId, out ToolCallState state)
    {
        foreach (var key in EnumerateResponseKeys(itemId, callId))
        {
            if (_responseToolCalls.TryGetValue(key, out var existingState))
            {
                state = existingState;
                return true;
            }
        }

        state = new ToolCallState();
        return false;
    }

    private void RegisterResponseState(ToolCallState state, string? itemId, string? callId)
    {
        foreach (var key in EnumerateResponseKeys(itemId, callId))
        {
            _responseToolCalls[key] = state;
        }
    }

    private static IEnumerable<string> EnumerateResponseKeys(string? itemId, string? callId)
    {
        if (!string.IsNullOrWhiteSpace(itemId))
        {
            yield return $"response-tool-item:{itemId}";
        }

        if (!string.IsNullOrWhiteSpace(callId))
        {
            yield return $"response-tool-call:{callId}";
        }
    }

    private static string BuildStateKey(string? scopeKey, int index)
    {
        return string.IsNullOrWhiteSpace(scopeKey)
            ? $"tool-call:{index}"
            : $"tool-call:{scopeKey}:{index}";
    }

    private static string BuildPreferredKey(string? scopeKey, int index, string? id)
    {
        var suffix = string.IsNullOrWhiteSpace(id) ? index.ToString() : id;
        return string.IsNullOrWhiteSpace(scopeKey)
            ? $"tool-call:{suffix}"
            : $"tool-call:{scopeKey}:{suffix}";
    }

    private static string? TryGetToolCallId(JsonElement toolCall)
    {
        return toolCall.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
            ? idProp.GetString()
            : null;
    }

    private static IReadOnlyList<OutputSegment> BuildUpdates(string keyBase, ToolCallState state)
    {
        var updates = new List<OutputSegment>(capacity: 2);
        if (state.Name.Length > 0)
        {
            updates.Add(new OutputSegment(OutputSegmentKind.ToolCallName, state.Name.ToString(), $"{keyBase}:name"));
        }

        if (state.Arguments.Length > 0)
        {
            updates.Add(new OutputSegment(OutputSegmentKind.ToolCallArguments, state.Arguments.ToString(), $"{keyBase}:arguments"));
        }

        return updates;
    }

    private sealed class ToolCallState
    {
        public StringBuilder Id { get; } = new();
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();
        public string PreferredKey { get; set; } = "tool-call:response";
    }
}