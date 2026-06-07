using System.Text.Json.Nodes;

internal sealed class TrackedRequestNormalizationPolicy
{
    public IReadOnlyList<ForwardedRequestMutation> Apply(JsonObject root, InteractionEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(root);

        var forwardedRequestMutations = new List<ForwardedRequestMutation>();

        if (!InteractionEndpointClassifier.UsesCompletionsNormalization(endpoint))
        {
            return forwardedRequestMutations;
        }

        JsonObject streamOptions;
        if (root["stream_options"] is JsonObject existingStreamOptions)
        {
            streamOptions = existingStreamOptions;
        }
        else
        {
            streamOptions = new JsonObject();
            root["stream_options"] = streamOptions;
        }

        if (!streamOptions.ContainsKey("include_usage"))
        {
            streamOptions["include_usage"] = true;
            forwardedRequestMutations.Add(ForwardedRequestMutation.EnableIncludeUsage());
        }

        return forwardedRequestMutations.ToArray();
    }
}