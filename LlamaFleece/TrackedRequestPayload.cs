using System.Text;
using System.Text.Json.Nodes;

internal sealed class TrackedRequestPayload
{
    private static readonly TrackedRequestNormalizationPolicy NormalizationPolicy = new();
    private static readonly TrackedRequestContinuationPolicy ContinuationPolicy = new();

    private readonly InteractionEndpoint _endpoint;
    private readonly JsonObject? _normalizedRoot;

    private TrackedRequestPayload(
        InteractionRequestEnvelope requestEnvelope,
        InteractionEndpoint endpoint,
        string originalJson,
        string normalizedJson,
        JsonObject? normalizedRoot,
        IEnumerable<ForwardedRequestMutation>? forwardedRequestMutations)
    {
        RequestEnvelope = requestEnvelope.Clone();
        _endpoint = endpoint;
        _normalizedRoot = normalizedRoot;
        OriginalJson = originalJson;
        NormalizedJson = normalizedJson;
        NormalizedBodyBytes = Encoding.UTF8.GetBytes(normalizedJson);
        ContentType = string.IsNullOrWhiteSpace(requestEnvelope.ContentType) ? "application/json" : requestEnvelope.ContentType;
        SupportsForceContinue = ContinuationPolicy.Supports(endpoint, normalizedRoot);
        ForwardedRequestMutations = forwardedRequestMutations?.Distinct().ToArray() ?? Array.Empty<ForwardedRequestMutation>();
    }

    public InteractionRequestEnvelope RequestEnvelope { get; }

    public string OriginalJson { get; }

    public string NormalizedJson { get; }

    public byte[] NormalizedBodyBytes { get; }

    public string ContentType { get; }

    public bool SupportsForceContinue { get; }

    public IReadOnlyList<ForwardedRequestMutation> ForwardedRequestMutations { get; }

    public static TrackedRequestPayload Create(string requestPath, string? contentType, string requestJson)
    {
        return Create(
            new InteractionRequestEnvelope
            {
                Path = string.IsNullOrWhiteSpace(requestPath) ? "/" : requestPath,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType
            },
            requestJson);
    }

    public static TrackedRequestPayload Create(InteractionRequestEnvelope requestEnvelope, string requestJson)
    {
        ArgumentNullException.ThrowIfNull(requestEnvelope);

        var endpoint = InteractionEndpointClassifier.Classify(requestEnvelope.Path);
        JsonObject? normalizedRoot = null;
        var normalizedJson = requestJson;
        IReadOnlyList<ForwardedRequestMutation> forwardedRequestMutations = Array.Empty<ForwardedRequestMutation>();

        try
        {
            if (JsonNode.Parse(requestJson) is JsonObject root)
            {
                normalizedRoot = (JsonObject)root.DeepClone()!;
                forwardedRequestMutations = NormalizationPolicy.Apply(normalizedRoot, endpoint);
                normalizedJson = normalizedRoot.ToJsonString();
            }
        }
        catch
        {
        }

        return new TrackedRequestPayload(requestEnvelope, endpoint, requestJson, normalizedJson, normalizedRoot, forwardedRequestMutations);
    }

    public bool TryCreateForceContinuePayload(out TrackedRequestPayload payload)
    {
        payload = null!;

        if (_normalizedRoot is null ||
            !ContinuationPolicy.TryCreatePayload(_endpoint, _normalizedRoot, out var continuationRoot))
        {
            return false;
        }

        var continuationJson = continuationRoot.ToJsonString();
        payload = new TrackedRequestPayload(
            RequestEnvelope,
            _endpoint,
            OriginalJson,
            continuationJson,
            continuationRoot,
            ForwardedRequestMutations.Concat(new[] { ForwardedRequestMutation.SendForceContinueFollowUp() }));
        return true;
    }
}