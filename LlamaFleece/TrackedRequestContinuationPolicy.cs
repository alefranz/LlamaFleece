using System.Text.Json.Nodes;

internal sealed class TrackedRequestContinuationPolicy
{
    private const string ForceContinueInstruction = "Continue the answer if the previous response stopped unexpectedly and returned no content. If you were already done, reply only with a brief confirmation.";

    public bool Supports(InteractionEndpoint endpoint, JsonObject? root)
    {
        if (root is null)
        {
            return false;
        }

        if (UsesMessageArrayForceContinue(endpoint))
        {
            return root["messages"] is JsonArray;
        }

        if (endpoint == InteractionEndpoint.Completions)
        {
            return root["prompt"] is JsonValue promptNode && promptNode.TryGetValue<string>(out _);
        }

        if (endpoint == InteractionEndpoint.Responses)
        {
            return CanCreateResponsesForceContinuePayload(root);
        }

        return false;
    }

    public bool ShouldIssueFollowUp(
        int attempt,
        bool forceContinueEnabled,
        bool upstreamRequestSucceeded,
        ProxyLoggingResult result)
    {
        return attempt == 0 &&
               forceContinueEnabled &&
               upstreamRequestSucceeded &&
               result.SawDone &&
               !result.GotContent;
    }

    public bool TryCreatePayload(InteractionEndpoint endpoint, JsonObject normalizedRoot, out JsonObject payloadRoot)
    {
        ArgumentNullException.ThrowIfNull(normalizedRoot);

        payloadRoot = null!;
        if (!Supports(endpoint, normalizedRoot))
        {
            return false;
        }

        payloadRoot = (JsonObject)normalizedRoot.DeepClone()!;

        if (UsesMessageArrayForceContinue(endpoint) && payloadRoot["messages"] is JsonArray messages)
        {
            messages.Add(CreateMessageArrayForceContinueItem());
            return true;
        }

        if (endpoint == InteractionEndpoint.Completions &&
            payloadRoot["prompt"] is JsonValue promptNode &&
            promptNode.TryGetValue<string>(out var prompt))
        {
            payloadRoot["prompt"] = string.IsNullOrWhiteSpace(prompt)
                ? ForceContinueInstruction
                : $"{prompt}\n\n{ForceContinueInstruction}";
            return true;
        }

        if (endpoint == InteractionEndpoint.Responses)
        {
            return TryAppendResponsesForceContinueInput(payloadRoot);
        }

        return false;
    }

    private static bool TryAppendResponsesForceContinueInput(JsonObject root)
    {
        if (root["input"] is JsonValue inputValue && inputValue.TryGetValue<string>(out var inputText))
        {
            root["input"] = string.IsNullOrWhiteSpace(inputText)
                ? ForceContinueInstruction
                : $"{inputText}\n\n{ForceContinueInstruction}";
            return true;
        }

        if (root["input"] is JsonArray inputArray)
        {
            inputArray.Add(CreateResponsesForceContinueInputItem(inputArray));
            return true;
        }

        if (root["input"] is JsonObject inputObject)
        {
            root["input"] = new JsonArray
            {
                (JsonNode)inputObject.DeepClone()!,
                CreateResponsesForceContinueMessageItem()
            };
            return true;
        }

        if (HasResponsesInstructions(root))
        {
            root["input"] = ForceContinueInstruction;
            return true;
        }

        return false;
    }

    private static JsonNode CreateResponsesForceContinueInputItem(JsonArray inputArray)
    {
        foreach (var item in inputArray)
        {
            if (item is JsonValue stringItem && stringItem.TryGetValue<string>(out _))
            {
                return JsonValue.Create(ForceContinueInstruction)!;
            }

            if (item is not null)
            {
                break;
            }
        }

        return CreateResponsesForceContinueMessageItem();
    }

    private static JsonObject CreateResponsesForceContinueMessageItem()
    {
        return new JsonObject
        {
            ["type"] = "message",
            ["role"] = "user",
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "input_text",
                    ["text"] = ForceContinueInstruction
                }
            }
        };
    }

    private static bool CanCreateResponsesForceContinuePayload(JsonObject root)
    {
        if (root["input"] is JsonArray or JsonObject)
        {
            return true;
        }

        if (root["input"] is JsonValue inputValue && inputValue.TryGetValue<string>(out _))
        {
            return true;
        }

        return HasResponsesInstructions(root);
    }

    private static bool UsesMessageArrayForceContinue(InteractionEndpoint endpoint)
    {
        return endpoint is InteractionEndpoint.ChatCompletions or InteractionEndpoint.AnthropicMessages;
    }

    private static JsonObject CreateMessageArrayForceContinueItem()
    {
        return new JsonObject
        {
            ["role"] = "user",
            ["content"] = ForceContinueInstruction
        };
    }

    private static bool HasResponsesInstructions(JsonObject root)
    {
        return root["instructions"] is JsonValue instructionsValue && instructionsValue.TryGetValue<string>(out _);
    }
}