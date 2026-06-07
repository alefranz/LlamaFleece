using Xunit;
using Spectre.Console;

public class TuiStateTests
{
    [Fact]
    public async Task WaitForNextFrameAsync_ReturnsFalseWhenCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var shouldContinue = await TuiManager.WaitForNextFrameAsync(TimeSpan.Zero, cts.Token);

        Assert.False(shouldContinue);
    }

    [Fact]
    public async Task WaitForNextFrameAsync_ReturnsTrueWhenDelayCompletes()
    {
        using var cts = new CancellationTokenSource();

        var shouldContinue = await TuiManager.WaitForNextFrameAsync(TimeSpan.Zero, cts.Token);

        Assert.True(shouldContinue);
    }

    [Fact]
    public void TakeSnapshot_AggregatesTokenTotalsAcrossSessions()
    {
        var state = new TuiState();

        state.NewSession();
        state.SetLatestPromptTokens(10);
        state.SetLatestCompletionTokens(5);
        state.SetLatestTotalTokens(15);

        state.NewSession();
        state.SetLatestPromptTokens(7);
        state.SetLatestCompletionTokens(3);
        state.SetLatestTotalTokens(10);

        var snapshot = state.TakeSnapshot();

        Assert.Equal(17, snapshot.TotalPromptTokens);
        Assert.Equal(8, snapshot.TotalCompletionTokens);
        Assert.Equal(25, snapshot.OverallTotalTokens);
        Assert.Equal(17, snapshot.SessionSummary.Tokens.PromptTokens);
        Assert.Equal(8, snapshot.SessionSummary.Tokens.CompletionTokens);
        Assert.Equal(25, snapshot.SessionSummary.Tokens.TotalTokens);
        Assert.False(snapshot.SessionSummary.Cost.HasPricingConfigured);
        Assert.NotNull(snapshot.VisibleInteraction);
        Assert.Equal(7, snapshot.VisibleInteraction!.PromptTokens);
        Assert.Equal(3, snapshot.VisibleInteraction.CompletionTokens);
        Assert.Equal(10, snapshot.VisibleInteraction.TotalTokens);
    }

    [Fact]
    public void TakeSnapshot_IncludesEstimatedCostWhenPricingIsConfigured()
    {
        var state = new TuiState(
            sessionSummaryService: new SessionSummaryService(new ProxyPricingOptions
            {
                Default = new ProxyTokenPricingOptions
                {
                    PromptUsdPer1MTokens = 1m,
                    CompletionUsdPer1MTokens = 2m
                }
            }));

        state.SetLatestModel("gpt-costed");
        state.SetLatestPromptTokens(2_000);
        state.SetLatestCompletionTokens(3_000);
        state.SetLatestTotalTokens(5_000);

        var snapshot = state.TakeSnapshot();

        Assert.True(snapshot.SessionSummary.Cost.HasPricingConfigured);
        Assert.Equal(0.008m, snapshot.SessionSummary.Cost.EstimatedUsd);
    }

    [Fact]
    public void AppendOutputMarkup_FlushesInProgressRawLineBeforeMarkupLine()
    {
        var state = new TuiState();

        state.AppendOutputRaw("partial raw line");
        state.AppendOutputMarkup("[bold magenta]tool[/]");

        var snapshot = state.TakeSnapshot();

        Assert.NotNull(snapshot.VisibleInteraction);
        Assert.Equal(new[]
        {
            new OutputSegment(OutputSegmentKind.Text, "partial raw line"),
            new OutputSegment(OutputSegmentKind.Markup, "[bold magenta]tool[/]")
        }, snapshot.VisibleInteraction!.OutputLines);
        Assert.Equal(string.Empty, snapshot.VisibleInteraction.CurrentOutputLine);
    }

    [Fact]
    public void UpsertOutputSegment_UpdatesExistingToolCallLine()
    {
        var state = new TuiState();

        state.UpsertOutputSegment("tool-call:0:name", OutputSegmentKind.ToolCallName, "search");
        state.UpsertOutputSegment("tool-call:0:arguments", OutputSegmentKind.ToolCallArguments, "{\"q\":\"ll");
        state.UpsertOutputSegment("tool-call:0:arguments", OutputSegmentKind.ToolCallArguments, "{\"q\":\"llama\"}");

        var snapshot = state.TakeSnapshot();

        Assert.NotNull(snapshot.VisibleInteraction);
        Assert.Equal(new[]
        {
            new OutputSegment(OutputSegmentKind.ToolCallName, "search", "tool-call:0:name"),
            new OutputSegment(OutputSegmentKind.ToolCallArguments, "{\"q\":\"llama\"}", "tool-call:0:arguments")
        }, snapshot.VisibleInteraction!.OutputLines);
    }

    [Fact]
    public void TakeSnapshot_ReturnsCopiesOfInteractionLists()
    {
        var state = new TuiState();

        state.AppendInput("first");
        var firstSnapshot = state.TakeSnapshot();

        state.AppendInput("second");
        var secondSnapshot = state.TakeSnapshot();

        Assert.NotNull(firstSnapshot.VisibleInteraction);
        Assert.NotNull(secondSnapshot.VisibleInteraction);
        Assert.Single(firstSnapshot.VisibleInteraction!.InputLines);
        Assert.Equal(2, secondSnapshot.VisibleInteraction!.InputLines.Count);
        Assert.Equal("first", firstSnapshot.VisibleInteraction.InputLines[0]);
        Assert.Equal("second", secondSnapshot.VisibleInteraction.InputLines[1]);
    }

    [Fact]
    public void ToggleLogMode_PreservesExistingLogEntries()
    {
        var state = new TuiState();

        state.AppendLog("first request");
        state.ToggleLogMode();
        state.CloseLogMode();
        state.ToggleLogMode();

        var snapshot = state.TakeSnapshot();

        Assert.True(snapshot.LogMode);
        Assert.Single(snapshot.LogEntries);
        Assert.Contains("first request", snapshot.LogEntries[0]);
    }

    [Fact]
    public void AppendLog_StoresPlainTextEntries()
    {
        var state = new TuiState();

        state.AppendLog("Proxying to http://localhost:8123 [source=abc].");

        var logSnapshot = state.GetLogSnapshot();

        Assert.Single(logSnapshot.Entries);
        Assert.StartsWith("[", logSnapshot.Entries[0], StringComparison.Ordinal);
        Assert.Contains("Proxying to http://localhost:8123 [source=abc].", logSnapshot.Entries[0], StringComparison.Ordinal);
        Assert.DoesNotContain("[[", logSnapshot.Entries[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AppendInputMessage_ProducesValidSpectreMarkup()
    {
        var state = new TuiState();

        state.AppendInput("[bold magenta]>[/] [bold white]New POST Request[/] to [cyan]/v1/chat/completions[/]");
        state.AppendInputMessage("yellow", "system", "You are a helpful assistant. Please think step by step.");
        state.AppendInputMessage("green", "user", "Write a short 3-sentence story about a brave compiler.");

        var snapshot = state.TakeSnapshot();

        Assert.NotNull(snapshot.VisibleInteraction);
        var inputMarkup = string.Join(Environment.NewLine, snapshot.VisibleInteraction!.InputLines);
        var exception = Record.Exception(() => _ = new Markup(inputMarkup));
        Assert.Null(exception);
    }

    [Fact]
    public void TakeSnapshot_ProjectsFilteredInteractionsAndCounts()
    {
        var state = new TuiState();

        state.SetLatestModel("gpt-a");
        state.SetLatestRequestEnvelope(new InteractionRequestEnvelope
        {
            Method = "POST",
            Path = "/v1/chat/completions",
            ContentType = "application/json"
        });
        state.SetLatestResponseStatusCode(200);

        state.NewSession();
        state.SetLatestModel("gpt-b");
        state.SetLatestRequestEnvelope(new InteractionRequestEnvelope
        {
            Method = "POST",
            Path = "/v1/responses",
            ContentType = "application/json"
        });
        state.SetLatestResponseStatusCode(500);
        state.SetLatestFinishReason("failed");

        state.ApplyInteractionFilterQuery("status=200");

        var snapshot = state.TakeSnapshot();

        Assert.True(snapshot.HasActiveFilter);
        Assert.Equal(2, snapshot.TotalInteractionCount);
        Assert.Equal(1, snapshot.FilteredInteractionCount);
        Assert.Single(snapshot.Interactions);
        Assert.NotNull(snapshot.VisibleInteraction);
        Assert.Equal(200, snapshot.VisibleInteraction!.ResponseStatusCode);
    }

    [Fact]
    public void TakeSnapshot_ProjectsForwardedRequestMutations()
    {
        var state = new TuiState();

        state.AddLatestForwardedRequestMutations(new[]
        {
            ForwardedRequestMutation.EnableIncludeUsage(),
            ForwardedRequestMutation.InjectUpstreamAuthorization()
        });

        var snapshot = state.TakeSnapshot();

        Assert.Single(snapshot.Interactions);
        Assert.True(snapshot.Interactions[0].HasForwardedRequestMutations);
        Assert.True(snapshot.Interactions[0].HasAttentionWorthyForwardedRequestMutations);
        Assert.NotNull(snapshot.VisibleInteraction);
        Assert.Collection(
            snapshot.VisibleInteraction!.ForwardedRequestMutations,
            mutation => Assert.Equal(ForwardedRequestMutationKind.RequestBodyNormalization, mutation.Kind),
            mutation => Assert.Equal(ForwardedRequestMutationKind.UpstreamAuthorizationInjection, mutation.Kind));
    }

    [Fact]
    public void TakeSnapshot_DoesNotPromoteLowSignalForwardedMutationToInteractionBadge()
    {
        var state = new TuiState();

        state.AddLatestForwardedRequestMutations(new[]
        {
            ForwardedRequestMutation.EnableIncludeUsage()
        });

        var snapshot = state.TakeSnapshot();

        Assert.Single(snapshot.Interactions);
        Assert.True(snapshot.Interactions[0].HasForwardedRequestMutations);
        Assert.False(snapshot.Interactions[0].HasAttentionWorthyForwardedRequestMutations);
        Assert.NotNull(snapshot.VisibleInteraction);
        Assert.Equal(
            "usage reporting enabled",
            ForwardedRequestMutation.SummarizeCompact(snapshot.VisibleInteraction!.ForwardedRequestMutations));
    }

    [Fact]
    public void RestorePersistedSession_RestoresHistoryIntoStateSnapshot()
    {
        using var exportDirectory = new TestExportDirectory();
        var persistenceService = new InteractionPersistenceService(Path.Combine(exportDirectory.Path, "state", "session-history.json"), TimeSpan.Zero);

        var first = new Interaction { Id = 2, Model = "gpt-first", PromptTokens = 4, CompletionTokens = 6, TotalTokens = 10 };
        var second = new Interaction { Id = 5, Model = "gpt-second", PromptTokens = 1, CompletionTokens = 2, TotalTokens = 3, ResponseStatusCode = 200 };
        var summary = new SessionSummaryService().BuildSummary(new[] { first, second }, DateTime.MinValue, DateTime.MinValue);
        var persisted = InteractionExportService.SnapshotSession(
            new[] { first, second },
            visibleInteractionIndex: 1,
            logEntries: new[] { "[12:00:00.000] persisted" },
            activeFixes: new[] { "force_continue" },
            summary: summary);

        persistenceService.SaveSession(persisted, force: true);

        var state = new TuiState(persistenceService: persistenceService);

        state.RestorePersistedSession();

        var snapshot = state.TakeSnapshot();
        Assert.Equal(2, snapshot.TotalInteractionCount);
        Assert.Equal(2, snapshot.FilteredInteractionCount);
        Assert.Equal(5, snapshot.TotalPromptTokens);
        Assert.Equal(8, snapshot.TotalCompletionTokens);
        Assert.Equal(13, snapshot.OverallTotalTokens);
        Assert.NotNull(snapshot.VisibleInteraction);
        Assert.Equal("gpt-second", snapshot.VisibleInteraction!.Model);
        Assert.Equal(200, snapshot.VisibleInteraction.ResponseStatusCode);
        Assert.Contains("Restored 2 persisted interactions", snapshot.StatusMessage, StringComparison.Ordinal);

        state.NewSession();
        state.FlushPersistedSession();

        var reloaded = persistenceService.LoadSession();
        Assert.True(reloaded.Found);
        Assert.Equal(3, reloaded.Session!.Interactions.Count);
        Assert.Equal(7, reloaded.Session.NextInteractionId);
    }
}