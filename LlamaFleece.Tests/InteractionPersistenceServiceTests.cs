using System.Text.Json.Nodes;
using Xunit;

public class InteractionPersistenceServiceTests
{
    [Fact]
    public void PreflightSessionFile_CreatesParentDirectoryWithoutCreatingSessionFile()
    {
        using var exportDirectory = new TestExportDirectory();
        var stateDirectory = Path.Combine(exportDirectory.Path, "state");
        var persistencePath = Path.Combine(stateDirectory, "session-history.json");
        var service = new InteractionPersistenceService(persistencePath, TimeSpan.Zero);

        service.PreflightSessionFile();

        Assert.True(Directory.Exists(stateDirectory));
        Assert.False(File.Exists(persistencePath));
        Assert.Empty(Directory.GetFiles(stateDirectory));
    }

    [Fact]
    public void PreflightSessionFile_ThrowsWhenExistingSessionFileIsLocked()
    {
        using var exportDirectory = new TestExportDirectory();
        var persistencePath = Path.Combine(exportDirectory.Path, "state", "session-history.json");
        Directory.CreateDirectory(Path.GetDirectoryName(persistencePath)!);
        File.WriteAllText(persistencePath, "{}", System.Text.Encoding.UTF8);

        var service = new InteractionPersistenceService(persistencePath, TimeSpan.Zero);

        using var lockedStream = new FileStream(
            persistencePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read);

        Assert.ThrowsAny<IOException>(() => service.PreflightSessionFile());
    }

    [Fact]
    public void SaveSession_WritesJsonAndRestoresInteractions()
    {
        using var exportDirectory = new TestExportDirectory();
        var persistencePath = Path.Combine(exportDirectory.Path, "state", "session-history.json");
        var service = new InteractionPersistenceService(persistencePath, TimeSpan.Zero);

        var first = new Interaction
        {
            Id = 4,
            Model = "gpt-alpha",
            PromptTokens = 3,
            CompletionTokens = 5,
            TotalTokens = 8,
            StartTime = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc),
            FirstTokenTime = new DateTime(2026, 5, 19, 12, 0, 1, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 5, 19, 12, 0, 2, DateTimeKind.Utc)
        };
        first.InputLines.Add("[green]user:[/] [white]alpha[/]");
        first.RawInput.Append("{\"interaction\":4}");

        var second = new Interaction
        {
            Id = 9,
            Model = "gpt-beta",
            ResponseStatusCode = 200,
            FinishReason = "stop",
            Diagnostics =
            {
                InteractionDiagnostic.UpstreamUnavailable("connection refused")
            },
            PromptTokens = 7,
            CompletionTokens = 11,
            TotalTokens = 18,
            ForceContinueApplied = true,
            StartTime = new DateTime(2026, 5, 19, 12, 1, 0, DateTimeKind.Utc),
            FirstTokenTime = new DateTime(2026, 5, 19, 12, 1, 1, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 5, 19, 12, 1, 3, DateTimeKind.Utc)
        };
        second.OutputLines.Add(new OutputSegment(OutputSegmentKind.ToolCallName, "search", "tool:0:name"));
        second.OutputLines.Add(new OutputSegment(OutputSegmentKind.ToolCallArguments, "{\"q\":\"llama\"}", "tool:0:args"));
        second.RawOutput.Append("data: second output");

        var summary = new SessionSummaryService().BuildSummary(
            new[] { first, second },
            new DateTime(2026, 5, 19, 12, 0, 1, DateTimeKind.Utc),
            new DateTime(2026, 5, 19, 12, 1, 3, DateTimeKind.Utc));

        var snapshot = InteractionExportService.SnapshotSession(
            new[] { first, second },
            visibleInteractionIndex: 1,
            logEntries: new[] { "[12:00:00.000] persisted log" },
            activeFixes: new[] { "force_continue" },
            summary: summary);

        var saveResult = service.SaveSession(snapshot, force: true);

        Assert.True(saveResult.Persisted);
        Assert.True(File.Exists(persistencePath));

        var json = JsonNode.Parse(File.ReadAllText(persistencePath))!.AsObject();
        Assert.Equal("session-state", json["type"]!.GetValue<string>());
        Assert.Equal(1, json["version"]!.GetValue<int>());

        var loadResult = service.LoadSession();

        Assert.True(loadResult.Found);
        Assert.NotNull(loadResult.Session);
        Assert.Equal(2, loadResult.Session!.Interactions.Count);
        Assert.Equal(1, loadResult.Session.VisibleInteractionIndex);
        Assert.Equal(10, loadResult.Session.NextInteractionId);
        Assert.Equal(10, loadResult.Session.TotalPromptTokens);
        Assert.Equal(16, loadResult.Session.TotalCompletionTokens);
        Assert.Equal(26, loadResult.Session.OverallTotalTokens);
        Assert.Contains("force_continue", loadResult.Session.ActiveFixes);
        Assert.Single(loadResult.Session.LogEntries);
        Assert.Equal(2, loadResult.Session.Interactions[1].OutputSegmentIndices.Count);
        Assert.Equal(200, loadResult.Session.Interactions[1].ResponseStatusCode);
        Assert.Equal("stop", loadResult.Session.Interactions[1].FinishReason);
        Assert.Single(loadResult.Session.Interactions[1].Diagnostics);
        Assert.Equal("upstream_unavailable", loadResult.Session.Interactions[1].Diagnostics[0].Code);
        Assert.Equal("connection refused", loadResult.Session.Interactions[1].Diagnostics[0].Detail);
    }

    [Fact]
    public void SaveSession_RedactsSensitiveDataInPersistedJson()
    {
        using var exportDirectory = new TestExportDirectory();
        var persistencePath = Path.Combine(exportDirectory.Path, "state", "session-history.json");
        var service = new InteractionPersistenceService(persistencePath, TimeSpan.Zero);

        var interaction = new Interaction
        {
            Id = 1,
            RequestEnvelope = new InteractionRequestEnvelope
            {
                Method = "POST",
                Path = "/v1/chat/completions",
                QueryString = "?api-key=query-secret-token&api-version=2026-05-01",
                ContentType = "application/json"
            },
            Diagnostics =
            {
                InteractionDiagnostic.UpstreamUnavailable("Bearer sk-diagnostic-secret")
            }
        };

        interaction.RawInput.Append("{\"authorization\":\"Bearer sk-request-secret-token\"}");
        interaction.RawOutput.Append("data: {\"choices\":[{\"delta\":{\"content\":\"Response token sk-response-secret-token\"}}]}");

        var summary = new SessionSummaryService().BuildSummary(new[] { interaction }, DateTime.MinValue, DateTime.MinValue);
        var snapshot = InteractionExportService.SnapshotSession(
            new[] { interaction },
            visibleInteractionIndex: 0,
            logEntries: new[] { "[12:00:00.000] >>> POST /v1/chat/completions?api-key=query-secret-token&api-version=2026-05-01" },
            activeFixes: Array.Empty<string>(),
            summary: summary);

        service.SaveSession(snapshot, force: true);

        var jsonText = File.ReadAllText(persistencePath);
        var json = JsonNode.Parse(jsonText)!.AsObject();
        var persistedInteraction = json["session"]!["interactions"]![0]!.AsObject();
        Assert.DoesNotContain("query-secret-token", jsonText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-request-secret-token", jsonText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-response-secret-token", jsonText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-diagnostic-secret", jsonText, StringComparison.Ordinal);
        Assert.Contains("api-key=REDACTED", jsonText, StringComparison.Ordinal);
        Assert.Contains(InteractionSecretRedactor.RedactionToken, jsonText, StringComparison.Ordinal);
        Assert.Equal(
            "?api-key=REDACTED&api-version=2026-05-01",
            persistedInteraction["requestEnvelope"]!["queryString"]!.GetValue<string>());
        Assert.Equal(
            "Bearer REDACTED",
            persistedInteraction["diagnostics"]![0]!["detail"]!.GetValue<string>());
        Assert.Equal(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Response token REDACTED\"}}]}",
            persistedInteraction["rawOutput"]!.GetValue<string>());
        Assert.Equal(
            "[12:00:00.000] >>> POST /v1/chat/completions?api-key=REDACTED&api-version=2026-05-01",
            json["session"]!["logEntries"]![0]!.GetValue<string>());
    }
}