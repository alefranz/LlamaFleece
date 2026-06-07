using System.Globalization;

internal enum InteractionDiagnosticKind
{
    ParseFallback,
    ContinuationAttempt,
    ContinuationOutcome,
    UpstreamFailure
}

internal enum InteractionDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

internal sealed record class InteractionDiagnostic
{
    public InteractionDiagnosticKind Kind { get; init; }

    public InteractionDiagnosticSeverity Severity { get; init; } = InteractionDiagnosticSeverity.Warning;

    public string Code { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string CompactSummary { get; init; } = string.Empty;

    public string? Detail { get; init; }

    public int Count { get; init; } = 1;

    public int? Attempt { get; init; }

    public int? StatusCode { get; init; }

    public bool PromotesInteractionBadge { get; init; } = true;

    public static InteractionDiagnostic ParseFallback()
    {
        return new InteractionDiagnostic
        {
            Kind = InteractionDiagnosticKind.ParseFallback,
            Severity = InteractionDiagnosticSeverity.Warning,
            Code = "stream_json_parse_fallback",
            Summary = "Ignored malformed SSE JSON event while continuing raw stream forwarding.",
            CompactSummary = "parse fallback"
        };
    }

    public static InteractionDiagnostic ContinuationAttemptSent(int attempt, string? finishReason = null)
    {
        return new InteractionDiagnostic
        {
            Kind = InteractionDiagnosticKind.ContinuationAttempt,
            Severity = InteractionDiagnosticSeverity.Info,
            Code = "force_continue_sent",
            Summary = "Sent a follow-up force_continue request after an empty streamed response.",
            CompactSummary = "force_continue sent",
            Attempt = NormalizeAttempt(attempt),
            Detail = string.IsNullOrWhiteSpace(finishReason)
                ? null
                : $"Original finish reason: {finishReason}.",
            PromotesInteractionBadge = false
        };
    }

    public static InteractionDiagnostic ContinuationOutcomeMerged(int attempt)
    {
        return new InteractionDiagnostic
        {
            Kind = InteractionDiagnosticKind.ContinuationOutcome,
            Severity = InteractionDiagnosticSeverity.Info,
            Code = "force_continue_merged",
            Summary = "Merged the force_continue follow-up response into the original streamed completion.",
            CompactSummary = "continuation merged",
            Attempt = NormalizeAttempt(attempt),
            PromotesInteractionBadge = false
        };
    }

    public static InteractionDiagnostic ContinuationOutcomeNonSse(int attempt)
    {
        return new InteractionDiagnostic
        {
            Kind = InteractionDiagnosticKind.ContinuationOutcome,
            Severity = InteractionDiagnosticSeverity.Warning,
            Code = "force_continue_non_sse",
            Summary = "Follow-up force_continue response was not an SSE stream; preserved the original completion.",
            CompactSummary = "continuation non-SSE",
            Attempt = NormalizeAttempt(attempt)
        };
    }

    public static InteractionDiagnostic ContinuationOutcomeHttpStatus(int attempt, int statusCode)
    {
        var normalizedStatusCode = NormalizeStatusCode(statusCode);

        return new InteractionDiagnostic
        {
            Kind = InteractionDiagnosticKind.ContinuationOutcome,
            Severity = InteractionDiagnosticSeverity.Warning,
            Code = "force_continue_http_status",
            Summary = normalizedStatusCode.HasValue
                ? $"Follow-up force_continue request returned HTTP {normalizedStatusCode.Value}; preserved the original completion."
                : "Follow-up force_continue request returned a non-success status; preserved the original completion.",
            CompactSummary = normalizedStatusCode.HasValue
                ? $"continuation HTTP {normalizedStatusCode.Value.ToString(CultureInfo.InvariantCulture)}"
                : "continuation HTTP failure",
            Attempt = NormalizeAttempt(attempt),
            StatusCode = normalizedStatusCode
        };
    }

    public static InteractionDiagnostic ContinuationOutcomeTimeout(int attempt)
    {
        return new InteractionDiagnostic
        {
            Kind = InteractionDiagnosticKind.ContinuationOutcome,
            Severity = InteractionDiagnosticSeverity.Warning,
            Code = "force_continue_timeout",
            Summary = "Follow-up force_continue request timed out; preserved the original completion.",
            CompactSummary = "continuation timed out",
            Attempt = NormalizeAttempt(attempt)
        };
    }

    public static InteractionDiagnostic ContinuationOutcomeFailure(int attempt, string? detail = null)
    {
        return new InteractionDiagnostic
        {
            Kind = InteractionDiagnosticKind.ContinuationOutcome,
            Severity = InteractionDiagnosticSeverity.Warning,
            Code = "force_continue_failed",
            Summary = "Follow-up force_continue request failed; preserved the original completion.",
            CompactSummary = "continuation failed",
            Attempt = NormalizeAttempt(attempt),
            Detail = detail
        };
    }

    public static InteractionDiagnostic UpstreamTimeout()
    {
        return new InteractionDiagnostic
        {
            Kind = InteractionDiagnosticKind.UpstreamFailure,
            Severity = InteractionDiagnosticSeverity.Error,
            Code = "tracked_request_timeout",
            Summary = "Tracked upstream request timed out.",
            CompactSummary = "upstream timed out"
        };
    }

    public static InteractionDiagnostic UpstreamUnavailable(string? detail = null)
    {
        return new InteractionDiagnostic
        {
            Kind = InteractionDiagnosticKind.UpstreamFailure,
            Severity = InteractionDiagnosticSeverity.Error,
            Code = "upstream_unavailable",
            Summary = "Upstream provider was unreachable.",
            CompactSummary = "upstream unavailable",
            Detail = detail
        };
    }

    public static InteractionDiagnostic UpstreamStreamFailed(string? detail = null)
    {
        return new InteractionDiagnostic
        {
            Kind = InteractionDiagnosticKind.UpstreamFailure,
            Severity = InteractionDiagnosticSeverity.Error,
            Code = "upstream_stream_failed",
            Summary = "Upstream stream failed after the response started.",
            CompactSummary = "upstream stream failed",
            Detail = detail
        };
    }

    public static InteractionDiagnostic UpstreamHttpFailure(int statusCode)
    {
        var normalizedStatusCode = NormalizeStatusCode(statusCode);

        return new InteractionDiagnostic
        {
            Kind = InteractionDiagnosticKind.UpstreamFailure,
            Severity = InteractionDiagnosticSeverity.Error,
            Code = "upstream_http_failure",
            Summary = normalizedStatusCode.HasValue
                ? $"Upstream response returned HTTP {normalizedStatusCode.Value}."
                : "Upstream response returned a non-success status.",
            CompactSummary = normalizedStatusCode.HasValue
                ? $"HTTP {normalizedStatusCode.Value.ToString(CultureInfo.InvariantCulture)}"
                : "upstream HTTP failure",
            StatusCode = normalizedStatusCode
        };
    }

    public static InteractionDiagnostic UpstreamResponseFailure(string? providerCode = null, string? detail = null)
    {
        var normalizedProviderCode = NormalizeText(providerCode);

        return new InteractionDiagnostic
        {
            Kind = InteractionDiagnosticKind.UpstreamFailure,
            Severity = InteractionDiagnosticSeverity.Error,
            Code = "upstream_response_failed",
            Summary = string.IsNullOrEmpty(normalizedProviderCode)
                ? "Upstream provider reported a failed response."
                : $"Upstream provider reported a failed response ({normalizedProviderCode}).",
            CompactSummary = string.IsNullOrEmpty(normalizedProviderCode)
                ? "response failed"
                : $"response failed ({normalizedProviderCode})",
            Detail = detail
        };
    }

    public static bool HasAttentionWorthyEntries(IEnumerable<InteractionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic?.PromotesInteractionBadge == true)
            {
                return true;
            }
        }

        return false;
    }

    public static string Summarize(IEnumerable<InteractionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parts = new List<string>();

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic is null)
            {
                continue;
            }

            var formatted = FormatSummary(diagnostic.Normalize(), compact: false);
            if (formatted.Length == 0 || !seen.Add(formatted))
            {
                continue;
            }

            parts.Add(formatted);
        }

        return parts.Count == 0
            ? "No diagnostics recorded."
            : string.Join("; ", parts) + ".";
    }

    public static string SummarizeCompact(IEnumerable<InteractionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parts = new List<string>();

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic is null)
            {
                continue;
            }

            var formatted = FormatSummary(diagnostic.Normalize(), compact: true);
            if (formatted.Length == 0 || !seen.Add(formatted))
            {
                continue;
            }

            parts.Add(formatted);
        }

        return string.Join(", ", parts);
    }

    internal bool CanAggregateWith(InteractionDiagnostic? other)
    {
        if (other is null)
        {
            return false;
        }

        var left = Normalize();
        var right = other.Normalize();

        return left.Kind == right.Kind &&
               left.Severity == right.Severity &&
               string.Equals(left.Code, right.Code, StringComparison.Ordinal) &&
               string.Equals(left.Summary, right.Summary, StringComparison.Ordinal) &&
               string.Equals(left.CompactSummary, right.CompactSummary, StringComparison.Ordinal) &&
               string.Equals(left.Detail, right.Detail, StringComparison.Ordinal) &&
               left.Attempt == right.Attempt &&
               left.StatusCode == right.StatusCode &&
               left.PromotesInteractionBadge == right.PromotesInteractionBadge;
    }

    internal InteractionDiagnostic Normalize()
    {
        return this with
        {
            Code = NormalizeText(Code),
            Summary = NormalizeText(Summary),
            CompactSummary = NormalizeText(string.IsNullOrWhiteSpace(CompactSummary) ? Summary : CompactSummary),
            Detail = NormalizeDetail(Detail),
            Count = Math.Max(1, Count),
            Attempt = NormalizeAttempt(Attempt),
            StatusCode = NormalizeStatusCode(StatusCode)
        };
    }

    internal InteractionDiagnostic Redact()
    {
        var normalized = Normalize();
        return normalized with
        {
            Summary = InteractionSecretRedactor.RedactText(normalized.Summary),
            CompactSummary = InteractionSecretRedactor.RedactText(normalized.CompactSummary),
            Detail = normalized.Detail is null ? null : InteractionSecretRedactor.RedactText(normalized.Detail)
        };
    }

    private static string FormatSummary(InteractionDiagnostic diagnostic, bool compact)
    {
        var summary = NormalizeSummary(compact && !string.IsNullOrWhiteSpace(diagnostic.CompactSummary)
            ? diagnostic.CompactSummary
            : diagnostic.Summary);

        if (summary.Length == 0)
        {
            return string.Empty;
        }

        if (diagnostic.Count > 1)
        {
            summary += $" (x{diagnostic.Count.ToString(CultureInfo.InvariantCulture)})";
        }

        if (!compact)
        {
            var detail = NormalizeDetail(diagnostic.Detail);
            if (!string.IsNullOrEmpty(detail))
            {
                summary += $": {detail}";
            }
        }

        return summary;
    }

    private static string NormalizeSummary(string? value)
    {
        return NormalizeText(value).TrimEnd('.');
    }

    private static string NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static string? NormalizeDetail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
    }

    private static int? NormalizeAttempt(int? attempt)
    {
        return attempt is > 0 ? attempt : null;
    }

    private static int? NormalizeStatusCode(int? statusCode)
    {
        return statusCode is > 0 ? statusCode : null;
    }
}