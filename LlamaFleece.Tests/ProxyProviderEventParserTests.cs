using Xunit;

[Collection("TuiManager serial")]
public class ProxyProviderEventParserTests
{
    [Fact]
    public void ProcessLine_ProjectsContentAndFinishReasonForChatCompletions()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();

        var parser = new ProxyProviderEventParser(new ProxyStreamUiProjector());

        parser.ProcessLine("data: {\"choices\":[{\"finish_reason\":\"stop\",\"delta\":{\"content\":\"hello\"}}]}");

        Assert.True(parser.GotContent);
        Assert.Equal("stop", parser.FinishReason);

        var snapshot = TuiManager.TakeSnapshotForTests();
        Assert.NotNull(snapshot.VisibleInteraction);
        Assert.Contains("hello", snapshot.VisibleInteraction!.CurrentOutputLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessLine_TracksDoneSentinelInDataPayload()
    {
        var parser = new ProxyProviderEventParser(new ProxyStreamUiProjector());

        parser.ProcessLine("data: [DONE]");

        Assert.True(parser.SawDone);
        Assert.False(parser.GotContent);
        Assert.Null(parser.FinishReason);
    }

    [Fact]
    public void ProcessLine_WhenJsonParseFails_RecordsAggregatedParseFallbackDiagnostic()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();

        var parser = new ProxyProviderEventParser(new ProxyStreamUiProjector());

        parser.ProcessLine("data: {\"choices\":[");
        parser.ProcessLine("data: {\"choices\":[");

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);

        var diagnostic = Assert.Single(interaction!.Diagnostics);
        Assert.Equal(InteractionDiagnosticKind.ParseFallback, diagnostic.Kind);
        Assert.Equal("stream_json_parse_fallback", diagnostic.Code);
        Assert.Equal(2, diagnostic.Count);
        Assert.Single(interaction.OutputLines.FindAll(segment =>
            segment.Kind == OutputSegmentKind.Markup &&
            segment.Text.Contains("Parse Fallback", StringComparison.Ordinal)));
    }

    [Fact]
    public void ProcessLine_ResponseFailed_RecordsStructuredUpstreamFailure()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();

        var parser = new ProxyProviderEventParser(new ProxyStreamUiProjector());

        parser.ProcessLine("data: {\"type\":\"response.failed\",\"response\":{\"status\":\"failed\",\"error\":{\"code\":\"server_error\",\"message\":\"model overloaded\"}}}");

        Assert.True(parser.GotContent);
        Assert.Equal("server_error", parser.FinishReason);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);

        var diagnostic = Assert.Single(interaction!.Diagnostics);
        Assert.Equal(InteractionDiagnosticKind.UpstreamFailure, diagnostic.Kind);
        Assert.Equal("upstream_response_failed", diagnostic.Code);
        Assert.Equal("model overloaded", diagnostic.Detail);
        Assert.Contains(interaction.OutputLines, segment =>
            segment.Kind == OutputSegmentKind.Markup &&
            segment.Text.Contains("Response failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ProcessLine_AggregatesUsageFinishReasonsAndToolCallsAcrossAllChoices()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();

        var parser = new ProxyProviderEventParser(new ProxyStreamUiProjector());

        parser.ProcessLine("data: {\"choices\":[{\"index\":0,\"finish_reason\":\"tool_calls\",\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":2,\"total_tokens\":7},\"delta\":{\"content\":\"alpha\",\"tool_calls\":[{\"index\":0,\"id\":\"call_a\",\"function\":{\"name\":\"apply_patch\",\"arguments\":\"{\\\"path\\\":\\\"PLAN.md\\\"}\"}}]}},{\"index\":1,\"finish_reason\":\"stop\",\"usage\":{\"prompt_tokens\":6,\"completion_tokens\":3,\"total_tokens\":9},\"delta\":{\"content\":\"beta\",\"tool_calls\":[{\"index\":0,\"id\":\"call_b\",\"function\":{\"name\":\"runTests\",\"arguments\":\"{}\"}}]}}]}");

        Assert.True(parser.GotContent);
        Assert.Equal("choice 0: tool_calls; choice 1: stop", parser.FinishReason);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Equal(11, interaction!.PromptTokens);
        Assert.Equal(5, interaction.CompletionTokens);
        Assert.Equal(16, interaction.TotalTokens);
        Assert.Equal("choice 0: tool_calls; choice 1: stop", interaction.FinishReason);
        Assert.Contains(interaction.OutputLines, segment =>
            segment.Kind == OutputSegmentKind.ToolCallName &&
            segment.Text == "apply_patch" &&
            segment.Key == "tool-call:choice-0:call_a:name");
        Assert.Contains(interaction.OutputLines, segment =>
            segment.Kind == OutputSegmentKind.ToolCallArguments &&
            segment.Text == "{\"path\":\"PLAN.md\"}" &&
            segment.Key == "tool-call:choice-0:call_a:arguments");
        Assert.Contains(interaction.OutputLines, segment =>
            segment.Kind == OutputSegmentKind.ToolCallName &&
            segment.Text == "runTests" &&
            segment.Key == "tool-call:choice-1:call_b:name");
        Assert.Contains(interaction.OutputLines, segment =>
            segment.Kind == OutputSegmentKind.ToolCallArguments &&
            segment.Text == "{}" &&
            segment.Key == "tool-call:choice-1:call_b:arguments");
    }
}
