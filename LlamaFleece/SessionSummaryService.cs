internal sealed class SessionSummaryService
{
    private const decimal OneMillionTokens = 1_000_000m;
    private const double NanosecondsPerSecond = 1_000_000_000.0;

    private readonly ProxyPricingOptions _pricing;

    public SessionSummaryService(ProxyPricingOptions? pricing = null)
    {
        _pricing = pricing?.Clone() ?? new ProxyPricingOptions();
    }

    public SessionSummary BuildSummary(
        IReadOnlyList<Interaction> interactions,
        DateTime firstTokenTimeAll,
        DateTime lastTokenTime)
    {
        ArgumentNullException.ThrowIfNull(interactions);

        var promptTokens = 0;
        var completionTokens = 0;
        var totalTokens = 0;
        var cachedPromptTokens = 0;
        var reasoningTokens = 0;

        var timeToFirstTokenSeconds = 0d;
        var timeToFirstTokenCount = 0;
        var wallClockDurationSeconds = 0d;
        var wallClockDurationCount = 0;
        var apiTotalDurationSeconds = 0d;
        var apiTotalDurationCount = 0;

        var hasPricingConfigured = _pricing.HasAnyRatesConfigured();
        var estimatedCostUsd = 0m;
        var pricedInteractionCount = 0;
        var billableInteractionCount = 0;
        var missingModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var interaction in interactions)
        {
            promptTokens += Math.Max(0, interaction.PromptTokens);
            completionTokens += Math.Max(0, interaction.CompletionTokens);
            totalTokens += ResolveTotalTokens(interaction);
            cachedPromptTokens += Math.Max(0, interaction.CachedPromptTokens);
            reasoningTokens += Math.Max(0, interaction.ReasoningTokens);

            if (interaction.FirstTokenTime is { } firstTokenTime && firstTokenTime > interaction.StartTime)
            {
                timeToFirstTokenSeconds += (firstTokenTime - interaction.StartTime).TotalSeconds;
                timeToFirstTokenCount++;
            }

            if (interaction.EndTime is { } endTime && endTime > interaction.StartTime)
            {
                wallClockDurationSeconds += (endTime - interaction.StartTime).TotalSeconds;
                wallClockDurationCount++;
            }

            if (interaction.ApiTotalDuration is > 0)
            {
                apiTotalDurationSeconds += interaction.ApiTotalDuration.Value / NanosecondsPerSecond;
                apiTotalDurationCount++;
            }

            if (!HasBillableTokens(interaction))
            {
                continue;
            }

            billableInteractionCount++;

            if (TryEstimateInteractionCostUsd(interaction, out var interactionCostUsd))
            {
                estimatedCostUsd += interactionCostUsd;
                pricedInteractionCount++;
            }
            else
            {
                missingModels.Add(GetModelLabel(interaction.Model));
            }
        }

        var latency = new SessionLatencySummary
        {
            FirstTokenTimeAllUtc = firstTokenTimeAll == DateTime.MinValue ? null : NormalizeTimestamp(firstTokenTimeAll),
            LastTokenTimeUtc = lastTokenTime == DateTime.MinValue ? null : NormalizeTimestamp(lastTokenTime),
            ActiveSpanSeconds = firstTokenTimeAll != DateTime.MinValue && lastTokenTime > firstTokenTimeAll
                ? (lastTokenTime - firstTokenTimeAll).TotalSeconds
                : null,
            AverageTimeToFirstTokenSeconds = timeToFirstTokenCount > 0
                ? timeToFirstTokenSeconds / timeToFirstTokenCount
                : null,
            TimeToFirstTokenSampleCount = timeToFirstTokenCount,
            AverageWallClockDurationSeconds = wallClockDurationCount > 0
                ? wallClockDurationSeconds / wallClockDurationCount
                : null,
            WallClockDurationSampleCount = wallClockDurationCount,
            AverageApiTotalDurationSeconds = apiTotalDurationCount > 0
                ? apiTotalDurationSeconds / apiTotalDurationCount
                : null,
            ApiTotalDurationSampleCount = apiTotalDurationCount
        };

        var unpricedInteractionCount = Math.Max(0, billableInteractionCount - pricedInteractionCount);
        var missingModelList = new List<string>(missingModels);
        missingModelList.Sort(StringComparer.OrdinalIgnoreCase);
        var cost = new SessionCostSummary
        {
            HasPricingConfigured = hasPricingConfigured,
            IsPartial = hasPricingConfigured && unpricedInteractionCount > 0,
            EstimatedUsd = !hasPricingConfigured
                ? null
                : billableInteractionCount == 0
                    ? 0m
                    : pricedInteractionCount > 0
                        ? estimatedCostUsd
                        : null,
            PricedInteractionCount = pricedInteractionCount,
            UnpricedInteractionCount = unpricedInteractionCount,
            MissingModels = missingModelList
        };

        return new SessionSummary
        {
            InteractionCount = interactions.Count,
            Tokens = new SessionTokenSummary
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = totalTokens,
                CachedPromptTokens = cachedPromptTokens,
                ReasoningTokens = reasoningTokens
            },
            Latency = latency,
            Cost = cost
        };
    }

    private bool TryEstimateInteractionCostUsd(Interaction interaction, out decimal estimatedCostUsd)
    {
        estimatedCostUsd = 0m;

        var rates = _pricing.ResolveRates(interaction.Model);
        if (rates is null)
        {
            return false;
        }

        estimatedCostUsd = ((decimal)Math.Max(0, interaction.PromptTokens) * rates.PromptUsdPer1MTokens!.Value / OneMillionTokens) +
                           ((decimal)Math.Max(0, interaction.CompletionTokens) * rates.CompletionUsdPer1MTokens!.Value / OneMillionTokens);

        return true;
    }

    private static bool HasBillableTokens(Interaction interaction)
    {
        return interaction.PromptTokens > 0 || interaction.CompletionTokens > 0 || interaction.TotalTokens > 0;
    }

    private static int ResolveTotalTokens(Interaction interaction)
    {
        if (interaction.TotalTokens > 0)
        {
            return interaction.TotalTokens;
        }

        return Math.Max(0, interaction.PromptTokens) + Math.Max(0, interaction.CompletionTokens);
    }

    private static string GetModelLabel(string? model)
    {
        return string.IsNullOrWhiteSpace(model) ? "unknown" : model.Trim();
    }

    private static DateTime NormalizeTimestamp(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value.ToUniversalTime()
        };
    }
}

internal sealed record class SessionSummary
{
    public int InteractionCount { get; init; }
    public SessionTokenSummary Tokens { get; init; } = new();
    public SessionLatencySummary Latency { get; init; } = new();
    public SessionCostSummary Cost { get; init; } = new();
}

internal sealed record class SessionTokenSummary
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
    public int CachedPromptTokens { get; init; }
    public int ReasoningTokens { get; init; }
}

internal sealed record class SessionLatencySummary
{
    public DateTime? FirstTokenTimeAllUtc { get; init; }
    public DateTime? LastTokenTimeUtc { get; init; }
    public double? ActiveSpanSeconds { get; init; }
    public double? AverageTimeToFirstTokenSeconds { get; init; }
    public int TimeToFirstTokenSampleCount { get; init; }
    public double? AverageWallClockDurationSeconds { get; init; }
    public int WallClockDurationSampleCount { get; init; }
    public double? AverageApiTotalDurationSeconds { get; init; }
    public int ApiTotalDurationSampleCount { get; init; }
}

internal sealed record class SessionCostSummary
{
    public bool HasPricingConfigured { get; init; }
    public bool IsPartial { get; init; }
    public decimal? EstimatedUsd { get; init; }
    public int PricedInteractionCount { get; init; }
    public int UnpricedInteractionCount { get; init; }
    public List<string> MissingModels { get; init; } = new();
}