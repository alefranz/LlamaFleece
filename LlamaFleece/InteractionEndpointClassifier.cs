internal enum InteractionEndpoint
{
    None,
    ChatCompletions,
    Completions,
    Responses,
    AnthropicMessages
}

internal static class InteractionEndpointClassifier
{
    private sealed record SupportedEndpoint(InteractionEndpoint Endpoint, string[] Paths);

    private static readonly SupportedEndpoint[] SupportedEndpoints =
    {
        new(InteractionEndpoint.ChatCompletions, new[] { "/v1/chat/completions", "/chat/completions" }),
        new(InteractionEndpoint.Completions, new[] { "/v1/completions", "/completions" }),
        new(InteractionEndpoint.Responses, new[] { "/v1/responses", "/responses" }),
        new(InteractionEndpoint.AnthropicMessages, new[] { "/v1/messages", "/messages" })
    };

    private static readonly Dictionary<string, InteractionEndpoint> PathToEndpointMap = BuildPathToEndpointMap();

    public static InteractionEndpoint Classify(string? requestPath)
    {
        var normalizedPath = NormalizePath(requestPath);

        return PathToEndpointMap.TryGetValue(normalizedPath, out var endpoint)
            ? endpoint
            : InteractionEndpoint.None;
    }

    public static bool IsTracked(string? requestPath)
    {
        return Classify(requestPath) != InteractionEndpoint.None;
    }

    public static bool UsesCompletionsNormalization(InteractionEndpoint endpoint)
    {
        return endpoint is InteractionEndpoint.ChatCompletions or InteractionEndpoint.Completions;
    }

    public static IReadOnlyList<string> GetSupportedPaths(InteractionEndpoint endpoint)
    {
        foreach (var supportedEndpoint in SupportedEndpoints)
        {
            if (supportedEndpoint.Endpoint == endpoint)
            {
                return supportedEndpoint.Paths;
            }
        }

        return Array.Empty<string>();
    }

    private static string NormalizePath(string? requestPath)
    {
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            return "/";
        }

        var normalizedPath = requestPath.Trim();
        if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
        {
            normalizedPath = "/" + normalizedPath;
        }

        if (normalizedPath.Length > 1)
        {
            normalizedPath = normalizedPath.TrimEnd('/');
        }

        return normalizedPath;
    }

    private static Dictionary<string, InteractionEndpoint> BuildPathToEndpointMap()
    {
        var pathToEndpointMap = new Dictionary<string, InteractionEndpoint>(StringComparer.OrdinalIgnoreCase);

        foreach (var supportedEndpoint in SupportedEndpoints)
        {
            foreach (var path in supportedEndpoint.Paths)
            {
                pathToEndpointMap[path] = supportedEndpoint.Endpoint;
            }
        }

        return pathToEndpointMap;
    }
}