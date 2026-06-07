using System.Text.Json.Nodes;
using Xunit;

public class InteractionExportServiceTests
{
    [Fact]
    public void ExportInteraction_WritesMetadataMarkdownAndRawArtifacts()
    {
        using var exportDirectory = new TestExportDirectory();
        var service = new InteractionExportService(exportDirectory.Path);
        var interaction = new Interaction
        {
            Id = 7,
            Model = "gpt-test",
            ResponseStatusCode = 200,
            FinishReason = "stop",
            ForwardedRequestMutations =
            {
                ForwardedRequestMutation.EnableIncludeUsage(),
                ForwardedRequestMutation.ApplyUpstreamHeaderOverrides(2)
            },
            Diagnostics =
            {
                InteractionDiagnostic.ParseFallback(),
                InteractionDiagnostic.UpstreamResponseFailure("server_error", "model overloaded")
            },
            PromptTokens = 11,
            CompletionTokens = 13,
            TotalTokens = 24,
            StreamedTokenCount = 13,
            CachedPromptTokens = 2,
            ReasoningTokens = 3,
            ForceContinueApplied = true,
            IsStreaming = false,
            StartTime = new DateTime(2026, 5, 19, 10, 0, 0, DateTimeKind.Utc),
            FirstTokenTime = new DateTime(2026, 5, 19, 10, 0, 1, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 5, 19, 10, 0, 2, DateTimeKind.Utc),
            CurrentInputLine = "[green]user:[/] [white]Hello export[/]",
            CurrentOutputLine = "final answer",
            CurrentOutputKind = OutputSegmentKind.Text
        };

        interaction.InputLines.Add("[bold yellow]system:[/] [yellow]Stay concise.[/]");
        interaction.OutputLines.Add(new OutputSegment(OutputSegmentKind.Reasoning, "thinking"));
        interaction.OutputLines.Add(new OutputSegment(OutputSegmentKind.ToolCallName, "search"));
        interaction.OutputLines.Add(new OutputSegment(OutputSegmentKind.ToolCallArguments, "{\"q\":\"llama\"}"));
        interaction.RawInput.Append("{\"model\":\"gpt-test\"}");
        interaction.RawOutput.Append("data: final answer");

        var result = service.ExportInteraction(InteractionExportService.SnapshotInteraction(interaction));

        Assert.True(File.Exists(result.JsonPath));
        Assert.True(File.Exists(result.MarkdownPath));
        Assert.True(File.Exists(result.RawRequestPath));
        Assert.True(File.Exists(result.RawResponsePath));

        var json = JsonNode.Parse(File.ReadAllText(result.JsonPath))!.AsObject();
        Assert.Equal("interaction-metadata", json["type"]!.GetValue<string>());
        Assert.Equal(7, json["id"]!.GetValue<int>());
        Assert.Equal("gpt-test", json["model"]!.GetValue<string>());
        Assert.Equal(200, json["responseStatusCode"]!.GetValue<int>());
        Assert.Equal("stop", json["finishReason"]!.GetValue<string>());
        Assert.Equal("requestBodyNormalization", json["forwardedRequestMutations"]![0]!["kind"]!.GetValue<string>());
        Assert.Equal("parseFallback", json["diagnostics"]![0]!["kind"]!.GetValue<string>());
        Assert.Equal(24, json["tokens"]!["totalTokens"]!.GetValue<int>());
        Assert.False(json.ContainsKey("rawInput"));
        Assert.False(json.ContainsKey("rawOutput"));
        Assert.False(json.ContainsKey("inputLines"));
        Assert.False(json.ContainsKey("outputLines"));

        var markdown = File.ReadAllText(result.MarkdownPath);
        Assert.Contains("# LlamaFleece Interaction Export", markdown, StringComparison.Ordinal);
        Assert.Contains("## Interaction 7", markdown, StringComparison.Ordinal);
        Assert.Contains("Response status: 200", markdown, StringComparison.Ordinal);
        Assert.Contains("Finish reason: stop", markdown, StringComparison.Ordinal);
        Assert.Contains("Forwarded request changed: yes", markdown, StringComparison.Ordinal);
        Assert.Contains("Diagnostics recorded: yes", markdown, StringComparison.Ordinal);
        Assert.Contains("Ignored malformed SSE JSON event while continuing raw stream forwarding", markdown, StringComparison.Ordinal);
        Assert.Contains("Upstream provider reported a failed response (server_error)", markdown, StringComparison.Ordinal);
        Assert.Contains("model overloaded", markdown, StringComparison.Ordinal);
        Assert.Contains("Enabled stream_options.include_usage for usage reporting", markdown, StringComparison.Ordinal);
        Assert.Contains("Applied 2 configured upstream header overrides.", markdown, StringComparison.Ordinal);
        Assert.Contains("Tool Call: search", markdown, StringComparison.Ordinal);
        Assert.Contains("Arguments: {\"q\":\"llama\"}", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Raw Request", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Raw Response", markdown, StringComparison.Ordinal);

        Assert.Equal("{\"model\":\"gpt-test\"}", File.ReadAllText(result.RawRequestPath));
        Assert.Equal("data: final answer", File.ReadAllText(result.RawResponsePath));
    }

    [Fact]
    public void ExportSession_WritesJsonAndMarkdownArtifacts()
    {
        using var exportDirectory = new TestExportDirectory();
        var service = new InteractionExportService(exportDirectory.Path);
        var summaryService = new SessionSummaryService(new ProxyPricingOptions
        {
            Default = new ProxyTokenPricingOptions
            {
                PromptUsdPer1MTokens = 1m,
                CompletionUsdPer1MTokens = 2m
            }
        });

        var first = new Interaction
        {
            Id = 1,
            Model = "gpt-a",
            PromptTokens = 2,
            CompletionTokens = 3,
            TotalTokens = 5,
            CachedPromptTokens = 1,
            StartTime = new DateTime(2026, 5, 19, 11, 0, 0, DateTimeKind.Utc),
            FirstTokenTime = new DateTime(2026, 5, 19, 11, 0, 0, 250, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 5, 19, 11, 0, 1, DateTimeKind.Utc)
        };
        first.InputLines.Add("[green]user:[/] [white]First interaction[/]");
        first.RawInput.Append("{\"interaction\":1}");

        var second = new Interaction
        {
            Id = 2,
            Model = "gpt-b",
            PromptTokens = 5,
            CompletionTokens = 8,
            TotalTokens = 13,
            ReasoningTokens = 4,
            ForceContinueApplied = true,
            StartTime = new DateTime(2026, 5, 19, 11, 1, 0, DateTimeKind.Utc),
            FirstTokenTime = new DateTime(2026, 5, 19, 11, 1, 0, 500, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 5, 19, 11, 1, 2, DateTimeKind.Utc)
        };
        second.OutputLines.Add(new OutputSegment(OutputSegmentKind.Text, "Second output"));
        second.RawOutput.Append("data: second output");

        var summary = summaryService.BuildSummary(
            new[] { first, second },
            new DateTime(2026, 5, 19, 11, 0, 0, 250, DateTimeKind.Utc),
            new DateTime(2026, 5, 19, 11, 1, 2, DateTimeKind.Utc));

        var snapshot = InteractionExportService.SnapshotSession(
            new[] { first, second },
            visibleInteractionIndex: 1,
            logEntries: new[] { "[12:00:00.000] >>> POST /v1/chat/completions?api-key=query-secret-token&api-version=2026-05-01" },
            activeFixes: new[] { "force_continue" },
            summary: summary);

        var result = service.ExportSession(snapshot);

        Assert.True(File.Exists(result.JsonPath));
        Assert.True(File.Exists(result.MarkdownPath));

        var jsonText = File.ReadAllText(result.JsonPath);
        var json = JsonNode.Parse(jsonText)!.AsObject();
        Assert.Equal("session", json["type"]!.GetValue<string>());
        Assert.Equal(2, json["interactionCount"]!.GetValue<int>());
        Assert.Equal(2, json["visibleInteractionId"]!.GetValue<int>());
        Assert.Equal("force_continue", json["activeFixes"]![0]!.GetValue<string>());
        Assert.Equal(2, json["interactions"]!.AsArray().Count);
        Assert.Equal(18, json["summary"]!["tokens"]!["totalTokens"]!.GetValue<int>());
        Assert.Equal(4, json["summary"]!["tokens"]!["reasoningTokens"]!.GetValue<int>());
        Assert.Equal(0.000029m, json["summary"]!["cost"]!["estimatedUsd"]!.GetValue<decimal>());
        Assert.DoesNotContain("query-secret-token", jsonText, StringComparison.Ordinal);
        Assert.Equal(
            "[12:00:00.000] >>> POST /v1/chat/completions?api-key=REDACTED&api-version=2026-05-01",
            json["requestLog"]![0]!.GetValue<string>());

        var markdown = File.ReadAllText(result.MarkdownPath);
        Assert.Contains("# LlamaFleece Session Export", markdown, StringComparison.Ordinal);
        Assert.Contains("Estimated cost: $0.000029", markdown, StringComparison.Ordinal);
        Assert.Contains("Latency:", markdown, StringComparison.Ordinal);
        Assert.Contains("## Request Log", markdown, StringComparison.Ordinal);
        Assert.Contains("### Interaction 2", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("query-secret-token", markdown, StringComparison.Ordinal);
        Assert.Contains("api-key=REDACTED&api-version=2026-05-01", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportInteraction_RedactsSensitiveRequestAndResponseData()
    {
        using var exportDirectory = new TestExportDirectory();
        var service = new InteractionExportService(exportDirectory.Path);
        var interaction = new Interaction
        {
            Id = 9,
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

        interaction.InputLines.Add("[green]user:[/] Share sk-request-secret-token");
        interaction.OutputLines.Add(new OutputSegment(OutputSegmentKind.Text, "Response token sk-response-secret-token"));
        interaction.RawInput.Append("{\"authorization\":\"Bearer sk-request-secret-token\",\"tool\":{\"api_key\":\"request-tool-secret\"}}");
        interaction.RawOutput.Append("data: {\"choices\":[{\"delta\":{\"content\":\"Response token sk-response-secret-token\"}}]}");

        var result = service.ExportInteraction(InteractionExportService.SnapshotInteraction(interaction));

        var jsonText = File.ReadAllText(result.JsonPath);
        var json = JsonNode.Parse(jsonText)!.AsObject();
        Assert.DoesNotContain("query-secret-token", jsonText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-request-secret-token", jsonText, StringComparison.Ordinal);
        Assert.DoesNotContain("request-tool-secret", jsonText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-response-secret-token", jsonText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-diagnostic-secret", jsonText, StringComparison.Ordinal);
        Assert.Contains(InteractionSecretRedactor.RedactionToken, jsonText, StringComparison.Ordinal);
        Assert.Equal(
            "?api-key=REDACTED&api-version=2026-05-01",
            json["requestEnvelope"]!["queryString"]!.GetValue<string>());
        Assert.Equal(
            "Bearer REDACTED",
            json["diagnostics"]![0]!["detail"]!.GetValue<string>());
        Assert.False(json.ContainsKey("rawInput"));
        Assert.False(json.ContainsKey("rawOutput"));
        Assert.False(json.ContainsKey("outputLines"));

        var markdown = File.ReadAllText(result.MarkdownPath);
        Assert.DoesNotContain("query-secret-token", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-request-secret-token", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("request-tool-secret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-response-secret-token", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-diagnostic-secret", markdown, StringComparison.Ordinal);
        Assert.Contains("api-key=REDACTED", markdown, StringComparison.Ordinal);
        Assert.Contains(InteractionSecretRedactor.RedactionToken, markdown, StringComparison.Ordinal);
        Assert.Contains("Bearer REDACTED", markdown, StringComparison.Ordinal);

        Assert.Equal(
            "{\"authorization\":\"REDACTED\",\"tool\":{\"api_key\":\"REDACTED\"}}",
            File.ReadAllText(result.RawRequestPath));
        Assert.Equal(
            "data: {\"choices\":[{\"delta\":{\"content\":\"Response token REDACTED\"}}]}",
            File.ReadAllText(result.RawResponsePath));
    }

    [Fact]
    public void SaveNamedArtifact_SanitizesNameAndRejectsDuplicates()
    {
        using var exportDirectory = new TestExportDirectory();
        var service = new InteractionExportService(exportDirectory.Path);

        var first = service.SaveNamedArtifact("interactions", "visible:save/name", ".md", "# saved");

        Assert.True(File.Exists(first.FilePath));
        Assert.EndsWith(Path.Combine("saved", "interactions", "visible-save-name.md"), first.FilePath, StringComparison.OrdinalIgnoreCase);

        var duplicate = Assert.Throws<IOException>(() => service.SaveNamedArtifact("interactions", "visible:save/name", ".md", "# saved"));
        Assert.Contains("already exists", duplicate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveNamedInteractionArtifacts_WritesMetadataMarkdownAndSeparateRawFiles()
    {
        using var exportDirectory = new TestExportDirectory();
        var service = new InteractionExportService(exportDirectory.Path);
        var interaction = new Interaction
        {
            Id = 17,
            RequestEnvelope = new InteractionRequestEnvelope
            {
                Method = "POST",
                Path = "/v1/chat/completions",
                QueryString = "?api-key=query-secret-token",
                ContentType = "application/json"
            },
            Model = "gpt-slot",
            ResponseStatusCode = 200,
            FinishReason = "stop",
            PromptTokens = 21,
            CompletionTokens = 34,
            TotalTokens = 55,
            StreamedTokenCount = 34,
            CachedPromptTokens = 3,
            ReasoningTokens = 5,
            StartTime = new DateTime(2026, 5, 29, 16, 0, 0, DateTimeKind.Utc),
            FirstTokenTime = new DateTime(2026, 5, 29, 16, 0, 1, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 5, 29, 16, 0, 2, DateTimeKind.Utc)
        };

        interaction.InputLines.Add("[green]user:[/] [white]Save this slot[/]");
        interaction.OutputLines.Add(new OutputSegment(OutputSegmentKind.Markup, "[yellow]Readable output[/]"));
        interaction.RawInput.Append("{\"authorization\":\"Bearer sk-request-secret-token\"}");
        interaction.RawOutput.Append("data: {\"delta\":\"chunk\"}\n");

        var result = service.SaveNamedInteractionArtifacts("copilot.json", InteractionExportService.SnapshotInteraction(interaction));

        Assert.True(File.Exists(result.JsonPath));
        Assert.True(File.Exists(result.MarkdownPath));
        Assert.True(File.Exists(result.RawRequestPath));
        Assert.True(File.Exists(result.RawResponsePath));
        Assert.Equal("saved/interactions/copilot.{json,md,request.txt,response.txt}", result.DisplayPattern);

        var metadataJsonText = File.ReadAllText(result.JsonPath);
        var metadataJson = JsonNode.Parse(metadataJsonText)!.AsObject();
        Assert.Equal("interaction-metadata", metadataJson["type"]!.GetValue<string>());
        Assert.Equal(17, metadataJson["id"]!.GetValue<int>());
        Assert.Equal("gpt-slot", metadataJson["model"]!.GetValue<string>());
        Assert.Equal(55, metadataJson["tokens"]!["totalTokens"]!.GetValue<int>());
        Assert.False(metadataJson.ContainsKey("rawInput"));
        Assert.False(metadataJson.ContainsKey("rawOutput"));
        Assert.False(metadataJson.ContainsKey("inputLines"));
        Assert.False(metadataJson.ContainsKey("outputLines"));
        Assert.DoesNotContain("sk-request-secret-token", metadataJsonText, StringComparison.Ordinal);
        Assert.Contains("api-key=REDACTED", metadataJsonText, StringComparison.Ordinal);

        var markdown = File.ReadAllText(result.MarkdownPath);
        Assert.Contains("# LlamaFleece Interaction View", markdown, StringComparison.Ordinal);
        Assert.Contains("Save this slot", markdown, StringComparison.Ordinal);
        Assert.Contains("Readable output", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[green]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[yellow]", markdown, StringComparison.Ordinal);

        Assert.Equal(
            "{\"authorization\":\"REDACTED\"}",
            File.ReadAllText(result.RawRequestPath));
        Assert.Equal(
            "data: {\"delta\":\"chunk\"}\n",
            File.ReadAllText(result.RawResponsePath));
    }
}