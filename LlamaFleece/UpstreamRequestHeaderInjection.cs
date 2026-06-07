using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

public sealed class UpstreamRequestHeaderInjection
{
    public static readonly UpstreamRequestHeaderInjection None = new(Array.Empty<KeyValuePair<string, string>>(), hasAuthorization: false, customHeaderCount: 0);

    private readonly KeyValuePair<string, string>[] _headers;
    private readonly bool _hasAuthorization;
    private readonly int _customHeaderCount;

    private UpstreamRequestHeaderInjection(KeyValuePair<string, string>[] headers, bool hasAuthorization, int customHeaderCount)
    {
        _headers = headers;
        _hasAuthorization = hasAuthorization;
        _customHeaderCount = Math.Max(0, customHeaderCount);
    }

    public int Count => _headers.Length;

    public static UpstreamRequestHeaderInjection Create(ProxyOptions options)
    {
        var headers = new List<KeyValuePair<string, string>>();
        var hasAuthorization = false;
        if (options.UpstreamAuth is { Scheme: { Length: > 0 } scheme, Parameter: { Length: > 0 } parameter })
        {
            headers.Add(new KeyValuePair<string, string>("Authorization", $"{scheme} {parameter}"));
            hasAuthorization = true;
        }

        headers.AddRange(options.UpstreamHeaders.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase));

        return headers.Count == 0
            ? None
            : new UpstreamRequestHeaderInjection(headers.ToArray(), hasAuthorization, options.UpstreamHeaders.Count);
    }

    internal IReadOnlyList<ForwardedRequestMutation> GetForwardedRequestMutations()
    {
        if (!_hasAuthorization && _customHeaderCount == 0)
        {
            return Array.Empty<ForwardedRequestMutation>();
        }

        var mutations = new List<ForwardedRequestMutation>();
        if (_hasAuthorization)
        {
            mutations.Add(ForwardedRequestMutation.InjectUpstreamAuthorization());
        }

        if (_customHeaderCount > 0)
        {
            mutations.Add(ForwardedRequestMutation.ApplyUpstreamHeaderOverrides(_customHeaderCount));
        }

        return mutations;
    }

    public void Apply(HttpRequestMessage request)
    {
        foreach (var header in _headers)
        {
            request.Headers.Remove(header.Key);
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                throw new InvalidOperationException($"Configured upstream header '{header.Key}' is not valid as an HTTP request header.");
            }
        }
    }

    public void Apply(TransformBuilderContext transformBuilderContext)
    {
        foreach (var header in _headers)
        {
            transformBuilderContext.AddRequestHeader(header.Key, header.Value, append: false);
        }
    }
}