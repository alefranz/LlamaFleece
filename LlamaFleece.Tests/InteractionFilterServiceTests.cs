using Xunit;

public class InteractionFilterServiceTests
{
    [Fact]
    public void Matches_SupportsModelEndpointStatusFinishTokenCountsAndTime()
    {
        var service = new InteractionFilterService();
        var filter = service.Parse("model=qwen endpoint=/v1/responses status=200 finish=completed prompt>=10 completion<=20 total=30 after=2026-05-19T10:00:00Z before=2026-05-19T12:00:00Z");

        var matchingInteraction = CreateInteraction(
            id: 1,
            model: "Qwen/Qwen3.6-27B:coding",
            endpoint: "/v1/responses",
            statusCode: 200,
            finishReason: "completed",
            promptTokens: 10,
            completionTokens: 20,
            totalTokens: 30,
            startTimeUtc: new DateTime(2026, 5, 19, 11, 0, 0, DateTimeKind.Utc));

        var nonMatchingInteraction = CreateInteraction(
            id: 2,
            model: "Qwen/Qwen3.6-27B:coding",
            endpoint: "/v1/responses",
            statusCode: 500,
            finishReason: "failed",
            promptTokens: 10,
            completionTokens: 20,
            totalTokens: 30,
            startTimeUtc: new DateTime(2026, 5, 19, 11, 0, 0, DateTimeKind.Utc));

        Assert.True(service.Matches(matchingInteraction, filter));
        Assert.False(service.Matches(nonMatchingInteraction, filter));
    }

    [Fact]
    public void GetMatchingIndices_SupportsPlainTextSearchAcrossModelEndpointStatusAndFinishReason()
    {
        var service = new InteractionFilterService();
        var filter = service.Parse("responses 500 failed");

        var interactions = new[]
        {
            CreateInteraction(
                id: 0,
                model: "gpt-a",
                endpoint: "/v1/chat/completions",
                statusCode: 200,
                finishReason: "stop",
                promptTokens: 5,
                completionTokens: 5,
                totalTokens: 10,
                startTimeUtc: new DateTime(2026, 5, 19, 9, 0, 0, DateTimeKind.Utc)),
            CreateInteraction(
                id: 1,
                model: "gpt-b",
                endpoint: "/v1/responses",
                statusCode: 500,
                finishReason: "failed",
                promptTokens: 5,
                completionTokens: 5,
                totalTokens: 10,
                startTimeUtc: new DateTime(2026, 5, 19, 9, 5, 0, DateTimeKind.Utc))
        };

        var matches = service.GetMatchingIndices(interactions, filter);

        Assert.Single(matches);
        Assert.Equal(1, matches[0]);
    }

    [Fact]
    public void Parse_RejectsInvalidTimeRange()
    {
        var service = new InteractionFilterService();

        var exception = Assert.Throws<InteractionFilterParseException>(() =>
            service.Parse("after=2026-05-19T12:00:00Z before=2026-05-19T10:00:00Z"));

        Assert.Contains("after= time", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Interaction CreateInteraction(
        int id,
        string model,
        string endpoint,
        int statusCode,
        string finishReason,
        int promptTokens,
        int completionTokens,
        int totalTokens,
        DateTime startTimeUtc)
    {
        return new Interaction
        {
            Id = id,
            Model = model,
            RequestEnvelope = new InteractionRequestEnvelope
            {
                Method = "POST",
                Path = endpoint,
                ContentType = "application/json"
            },
            ResponseStatusCode = statusCode,
            FinishReason = finishReason,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            StartTime = startTimeUtc
        };
    }
}