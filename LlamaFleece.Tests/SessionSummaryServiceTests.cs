using Xunit;

public class SessionSummaryServiceTests
{
    [Fact]
    public void BuildSummary_AggregatesTokensLatencyAndEstimatedCost()
    {
        var service = new SessionSummaryService(new ProxyPricingOptions
        {
            Default = new ProxyTokenPricingOptions
            {
                PromptUsdPer1MTokens = 2.5m,
                CompletionUsdPer1MTokens = 10m
            }
        });

        var interactions = new[]
        {
            new Interaction
            {
                Model = "gpt-a",
                PromptTokens = 1_000,
                CompletionTokens = 2_000,
                TotalTokens = 3_000,
                CachedPromptTokens = 100,
                ReasoningTokens = 300,
                StartTime = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc),
                FirstTokenTime = new DateTime(2026, 5, 19, 12, 0, 0, 500, DateTimeKind.Utc),
                EndTime = new DateTime(2026, 5, 19, 12, 0, 2, 0, DateTimeKind.Utc),
                ApiTotalDuration = 1_000_000_000d
            },
            new Interaction
            {
                Model = "gpt-b",
                PromptTokens = 500,
                CompletionTokens = 500,
                TotalTokens = 1_000,
                ReasoningTokens = 50,
                StartTime = new DateTime(2026, 5, 19, 12, 0, 10, DateTimeKind.Utc),
                FirstTokenTime = new DateTime(2026, 5, 19, 12, 0, 11, DateTimeKind.Utc),
                EndTime = new DateTime(2026, 5, 19, 12, 0, 13, DateTimeKind.Utc)
            }
        };

        var summary = service.BuildSummary(
            interactions,
            new DateTime(2026, 5, 19, 12, 0, 0, 500, DateTimeKind.Utc),
            new DateTime(2026, 5, 19, 12, 0, 13, DateTimeKind.Utc));

        Assert.Equal(2, summary.InteractionCount);
        Assert.Equal(1_500, summary.Tokens.PromptTokens);
        Assert.Equal(2_500, summary.Tokens.CompletionTokens);
        Assert.Equal(4_000, summary.Tokens.TotalTokens);
        Assert.Equal(100, summary.Tokens.CachedPromptTokens);
        Assert.Equal(350, summary.Tokens.ReasoningTokens);
        Assert.Equal(12.5d, summary.Latency.ActiveSpanSeconds!.Value, 3);
        Assert.Equal(0.75d, summary.Latency.AverageTimeToFirstTokenSeconds!.Value, 3);
        Assert.Equal(2.5d, summary.Latency.AverageWallClockDurationSeconds!.Value, 3);
        Assert.Equal(1.0d, summary.Latency.AverageApiTotalDurationSeconds!.Value, 3);
        Assert.True(summary.Cost.HasPricingConfigured);
        Assert.False(summary.Cost.IsPartial);
        Assert.Equal(0.02875m, summary.Cost.EstimatedUsd);
    }

    [Fact]
    public void BuildSummary_ReportsPartialCostWhenPricingDoesNotCoverAllModels()
    {
        var service = new SessionSummaryService(new ProxyPricingOptions
        {
            Models = new()
            {
                ["priced-model"] = new ProxyTokenPricingOptions
                {
                    PromptUsdPer1MTokens = 1m,
                    CompletionUsdPer1MTokens = 2m
                }
            }
        });

        var summary = service.BuildSummary(
            new[]
            {
                new Interaction
                {
                    Model = "priced-model",
                    PromptTokens = 1_000,
                    CompletionTokens = 1_000,
                    StartTime = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc)
                },
                new Interaction
                {
                    Model = "unpriced-model",
                    PromptTokens = 500,
                    CompletionTokens = 500,
                    StartTime = new DateTime(2026, 5, 19, 12, 0, 1, DateTimeKind.Utc)
                }
            },
            DateTime.MinValue,
            DateTime.MinValue);

        Assert.True(summary.Cost.HasPricingConfigured);
        Assert.True(summary.Cost.IsPartial);
        Assert.Equal(1, summary.Cost.PricedInteractionCount);
        Assert.Equal(1, summary.Cost.UnpricedInteractionCount);
        Assert.Equal(0.003m, summary.Cost.EstimatedUsd);
        Assert.Contains("unpriced-model", summary.Cost.MissingModels);
    }
}