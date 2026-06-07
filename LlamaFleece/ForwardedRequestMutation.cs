internal enum ForwardedRequestMutationKind
{
    RequestBodyNormalization,
    UpstreamAuthorizationInjection,
    UpstreamHeaderOverrides,
    ForceContinueFollowUp
}

internal sealed record class ForwardedRequestMutation
{
    public ForwardedRequestMutationKind Kind { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string CompactSummary { get; init; } = string.Empty;

    public bool PromotesInteractionBadge { get; init; } = true;

    public int? Count { get; init; }

    public static ForwardedRequestMutation EnableIncludeUsage()
    {
        return new ForwardedRequestMutation
        {
            Kind = ForwardedRequestMutationKind.RequestBodyNormalization,
            Summary = "Enabled stream_options.include_usage for usage reporting.",
            CompactSummary = "usage reporting enabled",
            PromotesInteractionBadge = false
        };
    }

    public static ForwardedRequestMutation InjectUpstreamAuthorization()
    {
        return new ForwardedRequestMutation
        {
            Kind = ForwardedRequestMutationKind.UpstreamAuthorizationInjection,
            Summary = "Injected configured upstream Authorization header.",
            CompactSummary = "auth header injected"
        };
    }

    public static ForwardedRequestMutation ApplyUpstreamHeaderOverrides(int count)
    {
        var safeCount = Math.Max(1, count);
        var noun = safeCount == 1 ? "override" : "overrides";

        return new ForwardedRequestMutation
        {
            Kind = ForwardedRequestMutationKind.UpstreamHeaderOverrides,
            Count = safeCount,
            Summary = $"Applied {safeCount} configured upstream header {noun}.",
            CompactSummary = safeCount == 1 ? "1 header override" : $"{safeCount} header overrides"
        };
    }

    public static ForwardedRequestMutation SendForceContinueFollowUp()
    {
        return new ForwardedRequestMutation
        {
            Kind = ForwardedRequestMutationKind.ForceContinueFollowUp,
            Summary = "Sent a follow-up force_continue request after an empty streamed response.",
            CompactSummary = "force_continue follow-up"
        };
    }

    public static bool HasAttentionWorthyChanges(IEnumerable<ForwardedRequestMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);

        foreach (var mutation in mutations)
        {
            if (mutation?.PromotesInteractionBadge == true)
            {
                return true;
            }
        }

        return false;
    }

    public static string Summarize(IEnumerable<ForwardedRequestMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parts = new List<string>();

        foreach (var mutation in mutations)
        {
            if (mutation is null)
            {
                continue;
            }

            var normalized = NormalizeSummary(mutation.Summary);
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            parts.Add(normalized);
        }

        return parts.Count == 0
            ? "No forwarded-request changes."
            : string.Join("; ", parts) + ".";
    }

    public static string SummarizeCompact(IEnumerable<ForwardedRequestMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parts = new List<string>();

        foreach (var mutation in mutations)
        {
            if (mutation is null)
            {
                continue;
            }

            var normalized = NormalizeSummary(string.IsNullOrWhiteSpace(mutation.CompactSummary)
                ? mutation.Summary
                : mutation.CompactSummary);

            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            parts.Add(normalized);
        }

        return string.Join(", ", parts);
    }

    private static string NormalizeSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return string.Empty;
        }

        return summary.Trim().TrimEnd('.');
    }
}