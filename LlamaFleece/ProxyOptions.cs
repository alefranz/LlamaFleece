using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";
    internal const string DefaultUpstreamUrl = "http://localhost:8123";
    internal const int DefaultListenPort = 5000;
    internal const string DefaultListenHost = "localhost";

    private static readonly HashSet<string> RestrictedInjectedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Content-Length",
        "Host",
        "Keep-Alive",
        "Proxy-Connection",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade"
    };

    public string? UpstreamUrl { get; set; }

    public int? ListenPort { get; set; }

    public string? ListenHost { get; set; }

    public ProxyTimeoutOptions Timeouts { get; set; } = new();

    public ProxyUpstreamAuthOptions? UpstreamAuth { get; set; }

    public Dictionary<string, string> UpstreamHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ProxyPricingOptions Pricing { get; set; } = new();

    public ProxyPersistenceOptions Persistence { get; set; } = new();

    public static ProxyOptions LoadAndValidate(IConfiguration configuration)
    {
        var options = new ProxyOptions();
        configuration.GetSection(SectionName).Bind(options);

        ApplyLegacyFallbacks(configuration, options);
        ApplyDefaults(options);

        var failures = Validate(options);
        if (failures.Count > 0)
        {
            throw new OptionsValidationException(SectionName, typeof(ProxyOptions), failures);
        }

        return options;
    }

    public Uri GetUpstreamUri()
    {
        return new Uri(UpstreamUrl!, UriKind.Absolute);
    }

    internal string GetNormalizedListenHost()
    {
        return NormalizeListenHost(ListenHost ?? DefaultListenHost);
    }

    internal string GetDisplayListenHost()
    {
        var listenHost = GetNormalizedListenHost();
        return listenHost.Contains(':', StringComparison.Ordinal)
            ? $"[{listenHost}]"
            : listenHost;
    }

    internal bool IsLoopbackOnlyBinding()
    {
        var listenHost = GetNormalizedListenHost();
        return listenHost.Equals(DefaultListenHost, StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(listenHost, out var listenAddress) && IPAddress.IsLoopback(listenAddress));
    }

    public TimeSpan? GetTrackedRequestTimeout()
    {
        return Timeouts.TrackedRequestSeconds is int seconds
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    public TimeSpan? GetShutdownTimeout()
    {
        return Timeouts.ShutdownSeconds is int seconds
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static void ApplyLegacyFallbacks(IConfiguration configuration, ProxyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.UpstreamUrl))
        {
            options.UpstreamUrl = configuration["TargetUrl"];
        }

        if (options.ListenPort is null)
        {
            var legacyPort = configuration["Port"];
            if (!string.IsNullOrWhiteSpace(legacyPort))
            {
                options.ListenPort = int.TryParse(legacyPort, out var parsedPort)
                    ? parsedPort
                    : 0;
            }
        }
    }

    private static void ApplyDefaults(ProxyOptions options)
    {
        options.UpstreamUrl ??= DefaultUpstreamUrl;
        options.ListenPort ??= DefaultListenPort;
        options.ListenHost = options.ListenHost is null
            ? DefaultListenHost
            : NormalizeListenHost(options.ListenHost);
        options.Timeouts ??= new ProxyTimeoutOptions();
        options.UpstreamHeaders = options.UpstreamHeaders.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(options.UpstreamHeaders, StringComparer.OrdinalIgnoreCase);
        options.Pricing ??= new ProxyPricingOptions();
        options.Pricing.Models = options.Pricing.Models.Count == 0
            ? new Dictionary<string, ProxyTokenPricingOptions>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ProxyTokenPricingOptions>(options.Pricing.Models, StringComparer.OrdinalIgnoreCase);
        options.Persistence ??= new ProxyPersistenceOptions();
    }

    private static List<string> Validate(ProxyOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.UpstreamUrl))
        {
            failures.Add("Proxy:UpstreamUrl is required.");
        }
        else if (!Uri.TryCreate(options.UpstreamUrl, UriKind.Absolute, out var upstreamUri))
        {
            failures.Add("Proxy:UpstreamUrl must be an absolute URI.");
        }
        else
        {
            if (!upstreamUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !upstreamUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("Proxy:UpstreamUrl must use http or https.");
            }

            if (!string.IsNullOrEmpty(upstreamUri.UserInfo))
            {
                failures.Add("Proxy:UpstreamUrl cannot embed user info. Use Proxy:UpstreamAuth or Proxy:UpstreamHeaders instead.");
            }

            if (!string.IsNullOrEmpty(upstreamUri.Query) || !string.IsNullOrEmpty(upstreamUri.Fragment))
            {
                failures.Add("Proxy:UpstreamUrl cannot include a query string or fragment.");
            }
        }

        if (options.ListenPort is null or < 1 or > 65535)
        {
            failures.Add("Proxy:ListenPort must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.ListenHost))
        {
            failures.Add("Proxy:ListenHost is required.");
        }
        else if (!options.ListenHost.Equals(DefaultListenHost, StringComparison.OrdinalIgnoreCase) &&
                 !IPAddress.TryParse(options.ListenHost, out _))
        {
            failures.Add("Proxy:ListenHost must be 'localhost' or a literal IPv4/IPv6 address.");
        }

        if (options.Timeouts.TrackedRequestSeconds is <= 0)
        {
            failures.Add("Proxy:Timeouts:TrackedRequestSeconds must be greater than zero when set.");
        }

        if (options.Timeouts.ShutdownSeconds is <= 0)
        {
            failures.Add("Proxy:Timeouts:ShutdownSeconds must be greater than zero when set.");
        }

        ValidateUpstreamAuth(options.UpstreamAuth, failures);
        ValidateUpstreamHeaders(options.UpstreamHeaders, options.UpstreamAuth, failures);
        ValidatePricing(options.Pricing, failures);
        ValidatePersistence(options.Persistence, failures);

        return failures;
    }

    private static string NormalizeListenHost(string listenHost)
    {
        var normalizedHost = listenHost.Trim();
        if (normalizedHost.Length >= 2 && normalizedHost[0] == '[' && normalizedHost[^1] == ']')
        {
            normalizedHost = normalizedHost[1..^1].Trim();
        }

        return normalizedHost;
    }

    private static void ValidatePersistence(ProxyPersistenceOptions persistence, ICollection<string> failures)
    {
        ArgumentNullException.ThrowIfNull(persistence);

        try
        {
            var fullPath = Path.GetFullPath(persistence.GetResolvedSessionFilePath());
            if (string.IsNullOrWhiteSpace(Path.GetFileName(fullPath)))
            {
                failures.Add("Proxy:Persistence:SessionFilePath must point to a file.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            failures.Add("Proxy:Persistence:SessionFilePath must be a valid file path.");
        }
    }

    private static void ValidateUpstreamAuth(ProxyUpstreamAuthOptions? upstreamAuth, ICollection<string> failures)
    {
        if (upstreamAuth is null)
        {
            return;
        }

        var hasScheme = !string.IsNullOrWhiteSpace(upstreamAuth.Scheme);
        var hasParameter = !string.IsNullOrWhiteSpace(upstreamAuth.Parameter);
        if (hasScheme != hasParameter)
        {
            failures.Add("Proxy:UpstreamAuth requires both Scheme and Parameter when configured.");
        }

        if (hasScheme && upstreamAuth!.Scheme!.Contains(' ', StringComparison.Ordinal))
        {
            failures.Add("Proxy:UpstreamAuth:Scheme cannot contain whitespace.");
        }
    }

    private static void ValidateUpstreamHeaders(
        IReadOnlyDictionary<string, string> upstreamHeaders,
        ProxyUpstreamAuthOptions? upstreamAuth,
        ICollection<string> failures)
    {
        foreach (var header in upstreamHeaders)
        {
            if (string.IsNullOrWhiteSpace(header.Key))
            {
                failures.Add("Proxy:UpstreamHeaders cannot contain an empty header name.");
                continue;
            }

            if (RestrictedInjectedHeaders.Contains(header.Key))
            {
                failures.Add($"Proxy:UpstreamHeaders cannot override reserved header '{header.Key}'.");
            }

            if (string.IsNullOrWhiteSpace(header.Value))
            {
                failures.Add($"Proxy:UpstreamHeaders:{header.Key} must not be empty.");
            }

            using var probeRequest = new System.Net.Http.HttpRequestMessage();
            if (!probeRequest.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                failures.Add($"Proxy:UpstreamHeaders:{header.Key} must be valid as an HTTP request header.");
            }
        }

        if (upstreamAuth is { Scheme: { Length: > 0 }, Parameter: { Length: > 0 } } &&
            upstreamHeaders.ContainsKey("Authorization"))
        {
            failures.Add("Proxy:UpstreamHeaders cannot include Authorization when Proxy:UpstreamAuth is configured.");
        }
    }

    private static void ValidatePricing(ProxyPricingOptions pricing, ICollection<string> failures)
    {
        ArgumentNullException.ThrowIfNull(pricing);

        ValidatePricingEntry("Proxy:Pricing:Default", pricing.Default, failures);

        foreach (var modelPricing in pricing.Models)
        {
            if (string.IsNullOrWhiteSpace(modelPricing.Key))
            {
                failures.Add("Proxy:Pricing:Models cannot contain an empty model key.");
                continue;
            }

            ValidatePricingEntry($"Proxy:Pricing:Models:{modelPricing.Key}", modelPricing.Value, failures);
        }
    }

    private static void ValidatePricingEntry(string prefix, ProxyTokenPricingOptions? pricing, ICollection<string> failures)
    {
        if (pricing is null)
        {
            return;
        }

        var hasPromptRate = pricing.PromptUsdPer1MTokens.HasValue;
        var hasCompletionRate = pricing.CompletionUsdPer1MTokens.HasValue;

        if (hasPromptRate != hasCompletionRate)
        {
            failures.Add($"{prefix} requires both PromptUsdPer1MTokens and CompletionUsdPer1MTokens when configured.");
        }

        if (pricing.PromptUsdPer1MTokens < 0)
        {
            failures.Add($"{prefix}:PromptUsdPer1MTokens must be zero or greater when set.");
        }

        if (pricing.CompletionUsdPer1MTokens < 0)
        {
            failures.Add($"{prefix}:CompletionUsdPer1MTokens must be zero or greater when set.");
        }
    }
}

public sealed class ProxyTimeoutOptions
{
    public int? TrackedRequestSeconds { get; set; }

    public int? ShutdownSeconds { get; set; }
}

public sealed class ProxyUpstreamAuthOptions
{
    public string? Scheme { get; set; }

    public string? Parameter { get; set; }
}

public sealed class ProxyPricingOptions
{
    public ProxyTokenPricingOptions? Default { get; set; }

    public Dictionary<string, ProxyTokenPricingOptions> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal bool HasAnyRatesConfigured()
    {
        if (Default?.IsConfigured == true)
        {
            return true;
        }

        foreach (var modelPricing in Models.Values)
        {
            if (modelPricing?.IsConfigured == true)
            {
                return true;
            }
        }

        return false;
    }

    internal ProxyTokenPricingOptions? ResolveRates(string? model)
    {
        if (!string.IsNullOrWhiteSpace(model) && Models.TryGetValue(model, out var modelPricing) && modelPricing.IsConfigured)
        {
            return modelPricing;
        }

        return Default is { IsConfigured: true }
            ? Default
            : null;
    }

    internal ProxyPricingOptions Clone()
    {
        return new ProxyPricingOptions
        {
            Default = Default?.Clone(),
            Models = Models.Count == 0
                ? new Dictionary<string, ProxyTokenPricingOptions>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ProxyTokenPricingOptions>(Models, StringComparer.OrdinalIgnoreCase)
        };
    }
}

public sealed class ProxyPersistenceOptions
{
    public bool Enabled { get; set; }

    public string? SessionFilePath { get; set; }

    internal string GetResolvedSessionFilePath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(SessionFilePath)
            ? Path.Combine(AppContext.BaseDirectory, "state", "session-history.json")
            : SessionFilePath.Trim();

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);
    }
}

public sealed class ProxyTokenPricingOptions
{
    public decimal? PromptUsdPer1MTokens { get; set; }

    public decimal? CompletionUsdPer1MTokens { get; set; }

    internal bool IsConfigured => PromptUsdPer1MTokens.HasValue || CompletionUsdPer1MTokens.HasValue;

    internal ProxyTokenPricingOptions Clone()
    {
        return new ProxyTokenPricingOptions
        {
            PromptUsdPer1MTokens = PromptUsdPer1MTokens,
            CompletionUsdPer1MTokens = CompletionUsdPer1MTokens
        };
    }
}