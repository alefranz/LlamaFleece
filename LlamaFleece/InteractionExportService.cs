using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

internal sealed class InteractionExportService
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _outputRoot;

    public InteractionExportService(string? outputRoot = null)
    {
        _outputRoot = Path.GetFullPath(outputRoot ?? Path.Combine(AppContext.BaseDirectory, "exports"));
    }

    public string OutputRoot => _outputRoot;

    internal static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions(JsonOptions);
    }

    public InteractionExportResult ExportInteraction(InteractionExportRecord interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var outputDirectory = Path.Combine(_outputRoot, "interactions");
        Directory.CreateDirectory(outputDirectory);

        var exportedAtUtc = DateTime.UtcNow;
        var stem = $"interaction-{interaction.Id:D4}-{exportedAtUtc:yyyyMMdd-HHmmss-fff}";
        var jsonPath = Path.Combine(outputDirectory, stem + ".json");
        var markdownPath = Path.Combine(outputDirectory, stem + ".md");
        var rawRequestPath = Path.Combine(outputDirectory, stem + ".request.txt");
        var rawResponsePath = Path.Combine(outputDirectory, stem + ".response.txt");

        File.WriteAllText(jsonPath, SerializeInteractionMetadataArtifact(interaction, exportedAtUtc), Utf8NoBom);
        File.WriteAllText(markdownPath, BuildInteractionMarkdownDocument(interaction, exportedAtUtc), Utf8NoBom);
        File.WriteAllText(rawRequestPath, interaction.RawInput, Utf8NoBom);
        File.WriteAllText(rawResponsePath, interaction.RawOutput, Utf8NoBom);

        return new InteractionExportResult("interaction", interaction.Id, jsonPath, markdownPath, rawRequestPath, rawResponsePath);
    }

    public InteractionExportResult ExportSession(InteractionExportSessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var exportedAtUtc = DateTime.UtcNow;
        var outputDirectory = Path.Combine(_outputRoot, "sessions");
        Directory.CreateDirectory(outputDirectory);

        var stem = $"session-{exportedAtUtc:yyyyMMdd-HHmmss-fff}";
        var jsonPath = Path.Combine(outputDirectory, stem + ".json");
        var markdownPath = Path.Combine(outputDirectory, stem + ".md");
        var visibleInteractionId = session.VisibleInteractionIndex >= 0 && session.VisibleInteractionIndex < session.Interactions.Count
            ? (int?)session.Interactions[session.VisibleInteractionIndex].Id
            : null;

        var document = new SessionExportDocument
        {
            Type = "session",
            ExportedAtUtc = exportedAtUtc,
            InteractionCount = session.Interactions.Count,
            VisibleInteractionId = visibleInteractionId,
            ActiveFixes = new List<string>(session.ActiveFixes),
            RequestLog = new List<string>(session.LogEntries),
            Summary = session.Summary,
            Interactions = new List<InteractionExportRecord>(session.Interactions)
        };

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(document, JsonOptions), Utf8NoBom);
        File.WriteAllText(markdownPath, BuildSessionMarkdown(document), Utf8NoBom);

        return new InteractionExportResult("session", visibleInteractionId, jsonPath, markdownPath);
    }

    public NamedSaveArtifactResult SaveNamedArtifact(string category, string requestedFileName, string extension, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(content);

        var normalizedExtension = NormalizeExtension(extension);
        var safeFileName = SanitizeRequestedFileName(requestedFileName, normalizedExtension);
        var outputDirectory = Path.Combine(_outputRoot, "saved", category);
        Directory.CreateDirectory(outputDirectory);

        var filePath = Path.Combine(outputDirectory, safeFileName + normalizedExtension);
        if (File.Exists(filePath))
        {
            throw new IOException($"A saved file named '{Path.GetFileName(filePath)}' already exists in saved/{category}.");
        }

        File.WriteAllText(filePath, content, Utf8NoBom);
        return new NamedSaveArtifactResult(category, filePath);
    }

    public NamedSaveInteractionArtifactsResult SaveNamedInteractionArtifacts(string requestedFileName, InteractionExportRecord interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var safeFileName = SanitizeRequestedFileStem(requestedFileName, ".json", ".md", ".txt");
        var outputDirectory = Path.Combine(_outputRoot, "saved", "interactions");
        Directory.CreateDirectory(outputDirectory);

        var jsonPath = Path.Combine(outputDirectory, safeFileName + ".json");
        var markdownPath = Path.Combine(outputDirectory, safeFileName + ".md");
        var rawRequestPath = Path.Combine(outputDirectory, safeFileName + ".request.txt");
        var rawResponsePath = Path.Combine(outputDirectory, safeFileName + ".response.txt");
        var existingPath = new[] { jsonPath, markdownPath, rawRequestPath, rawResponsePath }.FirstOrDefault(File.Exists);
        if (existingPath is not null)
        {
            throw new IOException($"A saved interaction named '{safeFileName}' already exists in saved/interactions.");
        }

        var savedAtUtc = DateTime.UtcNow;
        File.WriteAllText(jsonPath, SerializeInteractionMetadataArtifact(interaction, savedAtUtc), Utf8NoBom);
        File.WriteAllText(markdownPath, BuildSavedInteractionMarkdown(interaction), Utf8NoBom);
        File.WriteAllText(rawRequestPath, interaction.RawInput, Utf8NoBom);
        File.WriteAllText(rawResponsePath, interaction.RawOutput, Utf8NoBom);

        return new NamedSaveInteractionArtifactsResult("interactions", jsonPath, markdownPath, rawRequestPath, rawResponsePath);
    }

    public static InteractionExportRecord SnapshotInteraction(Interaction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        return new InteractionExportRecord
        {
            Id = interaction.Id,
            RequestEnvelope = interaction.RequestEnvelope?.CloneRedacted(),
            Model = interaction.Model,
            ResponseStatusCode = interaction.ResponseStatusCode,
            FinishReason = interaction.FinishReason,
            ForwardedRequestMutations = new List<ForwardedRequestMutation>(interaction.ForwardedRequestMutations),
            Diagnostics = interaction.Diagnostics.Select(diagnostic => diagnostic.Redact()).ToList(),
            PromptTokens = interaction.PromptTokens,
            CompletionTokens = interaction.CompletionTokens,
            TotalTokens = interaction.TotalTokens,
            StreamedTokenCount = interaction.StreamedTokenCount,
            ForceContinueApplied = interaction.ForceContinueApplied,
            CachedPromptTokens = interaction.CachedPromptTokens,
            ReasoningTokens = interaction.ReasoningTokens,
            HasApiMetrics = interaction.HasApiMetrics,
            ApiPrefillSpeed = interaction.ApiPrefillSpeed,
            ApiDecodeSpeed = interaction.ApiDecodeSpeed,
            ApiLoadDuration = interaction.ApiLoadDuration,
            ApiPromptEvalDuration = interaction.ApiPromptEvalDuration,
            ApiEvalDuration = interaction.ApiEvalDuration,
            ApiTotalDuration = interaction.ApiTotalDuration,
            IsStreaming = interaction.IsStreaming,
            StartTimeUtc = NormalizeTimestamp(interaction.StartTime),
            FirstTokenTimeUtc = NormalizeTimestamp(interaction.FirstTokenTime),
            EndTimeUtc = NormalizeTimestamp(interaction.EndTime),
            InputScroll = interaction.InputScroll,
            OutputScroll = interaction.OutputScroll,
            InputLines = interaction.InputLines.Select(InteractionSecretRedactor.RedactText).ToList(),
            OutputLines = interaction.OutputLines.Select(segment => segment with { Text = InteractionSecretRedactor.RedactText(segment.Text) }).ToList(),
            CurrentInputLine = InteractionSecretRedactor.RedactText(interaction.CurrentInputLine),
            CurrentOutputLine = InteractionSecretRedactor.RedactText(interaction.CurrentOutputLine),
            CurrentOutputKind = interaction.CurrentOutputKind,
            InputSectionStarts = new List<int>(interaction.InputSectionStarts),
            OutputSectionStarts = new List<int>(interaction.OutputSectionStarts),
            RawInput = InteractionSecretRedactor.RedactRequestBody(interaction.RawInput.ToString()),
            RawOutput = InteractionSecretRedactor.RedactResponseBody(interaction.RawOutput.ToString())
        };
    }

    internal static RestoredInteractionSession RestoreSession(InteractionExportSessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var interactions = session.Interactions.Select(RestoreInteraction).ToList();
        var visibleInteractionIndex = interactions.Count == 0
            ? -1
            : session.VisibleInteractionIndex >= 0 && session.VisibleInteractionIndex < interactions.Count
                ? session.VisibleInteractionIndex
                : interactions.Count - 1;

        return new RestoredInteractionSession
        {
            VisibleInteractionIndex = visibleInteractionIndex,
            NextInteractionId = interactions.Count == 0 ? 0 : interactions.Max(interaction => interaction.Id) + 1,
            TotalPromptTokens = interactions.Sum(interaction => Math.Max(0, interaction.PromptTokens)),
            TotalCompletionTokens = interactions.Sum(interaction => Math.Max(0, interaction.CompletionTokens)),
            OverallTotalTokens = interactions.Sum(ResolveTotalTokens),
            FirstTokenTimeAll = ResolveFirstTokenTimeAll(session, interactions),
            LastTokenTime = ResolveLastTokenTime(session, interactions),
            Interactions = interactions,
            LogEntries = new List<string>(session.LogEntries),
            ActiveFixes = new HashSet<string>(session.ActiveFixes, StringComparer.Ordinal)
        };
    }

    public static InteractionExportSessionSnapshot SnapshotSession(
        IReadOnlyList<Interaction> interactions,
        int visibleInteractionIndex,
        IReadOnlyList<string> logEntries,
        IReadOnlyList<string> activeFixes,
        SessionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(interactions);
        ArgumentNullException.ThrowIfNull(logEntries);
        ArgumentNullException.ThrowIfNull(activeFixes);
        ArgumentNullException.ThrowIfNull(summary);

        return new InteractionExportSessionSnapshot
        {
            VisibleInteractionIndex = interactions.Count == 0 || visibleInteractionIndex < 0 || visibleInteractionIndex >= interactions.Count
                ? -1
                : visibleInteractionIndex,
            Interactions = interactions.Select(SnapshotInteraction).ToList(),
            LogEntries = logEntries.Select(InteractionSecretRedactor.RedactText).ToList(),
            ActiveFixes = new List<string>(activeFixes),
            Summary = summary
        };
    }

    internal static string SerializeInteractionMetadataArtifact(InteractionExportRecord interaction, DateTime? writtenAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var metadata = new InteractionMetadataArtifactDocument
        {
            Type = "interaction-metadata",
            WrittenAtUtc = writtenAtUtc ?? DateTime.UtcNow,
            Id = interaction.Id,
            RequestEnvelope = interaction.RequestEnvelope,
            Model = interaction.Model,
            ResponseStatusCode = interaction.ResponseStatusCode,
            FinishReason = interaction.FinishReason,
            IsStreaming = interaction.IsStreaming,
            ForceContinueApplied = interaction.ForceContinueApplied,
            ForwardedRequestMutations = new List<ForwardedRequestMutation>(interaction.ForwardedRequestMutations),
            Diagnostics = new List<InteractionDiagnostic>(interaction.Diagnostics),
            Tokens = new InteractionMetadataTokens
            {
                PromptTokens = interaction.PromptTokens,
                CompletionTokens = interaction.CompletionTokens,
                TotalTokens = interaction.TotalTokens,
                StreamedTokenCount = interaction.StreamedTokenCount,
                CachedPromptTokens = interaction.CachedPromptTokens,
                ReasoningTokens = interaction.ReasoningTokens
            },
            Timing = new InteractionMetadataTiming
            {
                StartTimeUtc = interaction.StartTimeUtc,
                FirstTokenTimeUtc = interaction.FirstTokenTimeUtc,
                EndTimeUtc = interaction.EndTimeUtc
            },
            ApiMetrics = BuildInteractionMetadataApiMetrics(interaction)
        };

        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    internal static string BuildInteractionMarkdownDocument(InteractionExportRecord interaction, DateTime? exportedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var document = new InteractionExportDocument
        {
            Type = "interaction",
            ExportedAtUtc = exportedAtUtc ?? DateTime.UtcNow,
            Interaction = interaction
        };

        return BuildInteractionMarkdown(document);
    }

    internal static string BuildSavedInteractionMarkdown(InteractionExportRecord interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var builder = new StringBuilder();
        builder.AppendLine("# LlamaFleece Interaction View");
        builder.AppendLine();
        builder.AppendLine($"- Interaction: {interaction.Id}");
        builder.AppendLine($"- Request: {(interaction.RequestEnvelope is null ? "unknown" : $"{interaction.RequestEnvelope.Method} {interaction.RequestEnvelope.GetDisplayTarget()}")}");
        builder.AppendLine($"- Content type: {(interaction.RequestEnvelope?.ContentType ?? "unknown")}");
        builder.AppendLine($"- Model: {interaction.Model}");
        builder.AppendLine($"- Response status: {(interaction.ResponseStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a")}");
        builder.AppendLine($"- Finish reason: {(string.IsNullOrWhiteSpace(interaction.FinishReason) ? "n/a" : interaction.FinishReason)}");
        builder.AppendLine();
        AppendCodeSection(builder, headingLevel: 2, title: "Input", content: BuildInputPreview(interaction), language: "text");
        AppendCodeSection(builder, headingLevel: 2, title: "Output", content: BuildOutputPreview(interaction), language: "text");
        return builder.ToString();
    }

    internal static string BuildSavedPaneMarkdown(InteractionExportRecord interaction, NamedSavePane pane)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var builder = new StringBuilder();
        var paneTitle = pane == NamedSavePane.Input ? "Input" : "Output";
        var content = pane == NamedSavePane.Input
            ? BuildInputPreview(interaction)
            : BuildOutputPreview(interaction);

        builder.AppendLine($"# LlamaFleece {paneTitle} View");
        builder.AppendLine();
        builder.AppendLine($"- Interaction: {interaction.Id}");
        builder.AppendLine($"- Request: {(interaction.RequestEnvelope is null ? "unknown" : $"{interaction.RequestEnvelope.Method} {interaction.RequestEnvelope.GetDisplayTarget()}")}");
        builder.AppendLine($"- Model: {interaction.Model}");
        builder.AppendLine($"- Response status: {(interaction.ResponseStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a")}");
        builder.AppendLine();
        AppendCodeSection(builder, headingLevel: 2, title: paneTitle, content: content, language: "text");
        return builder.ToString();
    }

    internal static string GuessRawArtifactExtension(string content, string defaultExtension = ".txt")
    {
        var normalizedDefault = NormalizeExtension(defaultExtension);
        var trimmed = (content ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return normalizedDefault;
        }

        try
        {
            using var _ = JsonDocument.Parse(trimmed);
            return ".json";
        }
        catch (JsonException)
        {
        }

        var lines = trimmed.Split('\n');
        if (lines.Any(line => line.StartsWith("data:", StringComparison.Ordinal) || line.StartsWith("event:", StringComparison.Ordinal) || line.StartsWith("id:", StringComparison.Ordinal)))
        {
            return ".sse";
        }

        return normalizedDefault;
    }

    internal static string SanitizeRequestedFileName(string requestedFileName, string extension)
    {
        return SanitizeRequestedFileStem(requestedFileName, extension);
    }

    internal static string SanitizeRequestedFileStem(string requestedFileName, params string[] extensions)
    {
        var normalizedExtensions = extensions.Select(NormalizeExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var trimmed = (requestedFileName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("Enter a file name before saving.");
        }

        var candidate = trimmed;
        var candidateExtension = Path.GetExtension(candidate);
        if (!string.IsNullOrEmpty(candidateExtension) && normalizedExtensions.Contains(candidateExtension, StringComparer.OrdinalIgnoreCase))
        {
            candidate = candidate[..^candidateExtension.Length];
        }

        return SanitizeFileStem(candidate);
    }

    private static string SanitizeFileStem(string candidate)
    {
        candidate = candidate.Replace(Path.DirectorySeparatorChar, '-')
                             .Replace(Path.AltDirectorySeparatorChar, '-');

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(candidate.Length);
        var previousSeparator = false;

        foreach (var character in candidate)
        {
            var normalizedCharacter = character;
            if (Array.IndexOf(invalidCharacters, normalizedCharacter) >= 0 || char.IsControl(normalizedCharacter))
            {
                normalizedCharacter = '-';
            }
            else if (char.IsWhiteSpace(normalizedCharacter))
            {
                normalizedCharacter = '-';
            }

            if (normalizedCharacter == '-')
            {
                if (previousSeparator)
                {
                    continue;
                }

                previousSeparator = true;
                builder.Append(normalizedCharacter);
                continue;
            }

            previousSeparator = false;
            builder.Append(normalizedCharacter);
        }

        var sanitized = builder.ToString().Trim(' ', '.', '-', '_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new InvalidOperationException("Enter a file name with letters or numbers.");
        }

        if (OperatingSystem.IsWindows() && IsReservedWindowsFileName(sanitized))
        {
            sanitized += "-file";
        }

        return sanitized;
    }

    private static string BuildInteractionMarkdown(InteractionExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# LlamaFleece Interaction Export");
        builder.AppendLine();
        builder.AppendLine($"- Exported at (UTC): {FormatTimestamp(document.ExportedAtUtc)}");
        builder.AppendLine();

        AppendInteractionMarkdown(builder, document.Interaction, headingLevel: 2, includeRawSections: false);
        return builder.ToString();
    }

    private static string BuildSessionMarkdown(SessionExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# LlamaFleece Session Export");
        builder.AppendLine();
        builder.AppendLine($"- Exported at (UTC): {FormatTimestamp(document.ExportedAtUtc)}");
        builder.AppendLine($"- Interactions: {document.InteractionCount}");
        builder.AppendLine($"- Visible interaction: {(document.VisibleInteractionId.HasValue ? document.VisibleInteractionId.Value.ToString(CultureInfo.InvariantCulture) : "none")}");
        builder.AppendLine($"- Active fixes: {(document.ActiveFixes.Count > 0 ? string.Join(", ", document.ActiveFixes) : "none")}");
        builder.AppendLine($"- Tokens: prompt {document.Summary.Tokens.PromptTokens}, completion {document.Summary.Tokens.CompletionTokens}, total {document.Summary.Tokens.TotalTokens}, cached {document.Summary.Tokens.CachedPromptTokens}, reasoning {document.Summary.Tokens.ReasoningTokens}");
        builder.AppendLine($"- Latency: {BuildLatencySummary(document.Summary.Latency)}");
        builder.AppendLine($"- Estimated cost: {BuildCostSummary(document.Summary.Cost)}");

        builder.AppendLine();
        AppendCodeSection(builder, headingLevel: 2, title: "Request Log", content: string.Join(Environment.NewLine, document.RequestLog), language: "text");

        if (document.Interactions.Count == 0)
        {
            builder.AppendLine("## Interactions");
            builder.AppendLine();
            builder.AppendLine("No interactions were captured in memory at export time.");
            builder.AppendLine();
            return builder.ToString();
        }

        builder.AppendLine("## Interactions");
        builder.AppendLine();
        foreach (var interaction in document.Interactions)
        {
            AppendInteractionMarkdown(builder, interaction, headingLevel: 3, includeRawSections: true);
        }

        return builder.ToString();
    }

    private static void AppendInteractionMarkdown(StringBuilder builder, InteractionExportRecord interaction, int headingLevel, bool includeRawSections)
    {
        builder.AppendLine($"{new string('#', headingLevel)} Interaction {interaction.Id}");
        builder.AppendLine();
        builder.AppendLine($"- Request: {(interaction.RequestEnvelope is null ? "unknown" : $"{interaction.RequestEnvelope.Method} {interaction.RequestEnvelope.GetDisplayTarget()}")}");
        builder.AppendLine($"- Content type: {(interaction.RequestEnvelope?.ContentType ?? "unknown")}");
        builder.AppendLine($"- Model: {interaction.Model}");
        builder.AppendLine($"- Response status: {(interaction.ResponseStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a")}");
        builder.AppendLine($"- Finish reason: {(string.IsNullOrWhiteSpace(interaction.FinishReason) ? "n/a" : interaction.FinishReason)}");
        builder.AppendLine($"- Forwarded request changed: {FormatBoolean(interaction.ForwardedRequestMutations.Count > 0 || interaction.ForceContinueApplied)}");
        builder.AppendLine($"- Forwarded request mutations: {BuildForwardedRequestMutationSummary(interaction)}");
        builder.AppendLine($"- Diagnostics recorded: {FormatBoolean(interaction.Diagnostics.Count > 0)}");
        builder.AppendLine($"- Diagnostics: {BuildInteractionDiagnosticSummary(interaction)}");
        builder.AppendLine($"- Started (UTC): {FormatTimestamp(interaction.StartTimeUtc)}");
        builder.AppendLine($"- First token (UTC): {FormatTimestamp(interaction.FirstTokenTimeUtc)}");
        builder.AppendLine($"- Ended (UTC): {FormatTimestamp(interaction.EndTimeUtc)}");
        builder.AppendLine($"- Duration: {FormatDuration(interaction.StartTimeUtc, interaction.EndTimeUtc)}");
        builder.AppendLine($"- Streaming at export: {FormatBoolean(interaction.IsStreaming)}");
        builder.AppendLine($"- Force continue applied: {FormatBoolean(interaction.ForceContinueApplied)}");
        builder.AppendLine($"- Tokens: prompt {interaction.PromptTokens}, completion {interaction.CompletionTokens}, total {interaction.TotalTokens}, streamed {interaction.StreamedTokenCount}, cached {interaction.CachedPromptTokens}, reasoning {interaction.ReasoningTokens}");
        builder.AppendLine($"- Scroll offsets: input {interaction.InputScroll}, output {interaction.OutputScroll}");

        var apiMetricsSummary = BuildApiMetricsSummary(interaction);
        if (!string.IsNullOrEmpty(apiMetricsSummary))
        {
            builder.AppendLine($"- API metrics: {apiMetricsSummary}");
        }

        builder.AppendLine();
        AppendCodeSection(builder, headingLevel + 1, "Input Preview", BuildInputPreview(interaction), "text");
        AppendCodeSection(builder, headingLevel + 1, "Output Preview", BuildOutputPreview(interaction), "text");
        if (includeRawSections)
        {
            AppendCodeSection(builder, headingLevel + 1, "Raw Request", interaction.RawInput, "json");
            AppendCodeSection(builder, headingLevel + 1, "Raw Response", interaction.RawOutput, "text");
        }
    }

    private static void AppendCodeSection(StringBuilder builder, int headingLevel, string title, string content, string language)
    {
        builder.AppendLine($"{new string('#', headingLevel)} {title}");
        builder.AppendLine();
        builder.AppendLine($"```{language}");
        if (string.IsNullOrEmpty(content))
        {
            builder.AppendLine("<empty>");
        }
        else
        {
            builder.AppendLine(content.TrimEnd());
        }
        builder.AppendLine("```");
        builder.AppendLine();
    }

    private static string BuildInputPreview(InteractionExportRecord interaction)
    {
        var lines = interaction.InputLines.Select(ToPlainTextLine).ToList();
        if (!string.IsNullOrEmpty(interaction.CurrentInputLine))
        {
            lines.Add(ToPlainTextLine(interaction.CurrentInputLine));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOutputPreview(InteractionExportRecord interaction)
    {
        var lines = interaction.OutputLines.Select(ToPlainTextLine).ToList();
        if (!string.IsNullOrEmpty(interaction.CurrentOutputLine))
        {
            lines.Add(ToPlainTextLine(new OutputSegment(interaction.CurrentOutputKind, interaction.CurrentOutputLine)));
        }

        return string.Join(Environment.NewLine, lines);
    }

    internal static string ToPlainTextLine(OutputSegment segment)
    {
        return segment.Kind switch
        {
            OutputSegmentKind.Markup => RemoveMarkup(segment.Text),
            OutputSegmentKind.Reasoning => $"(reasoning) {segment.Text}",
            OutputSegmentKind.ToolCallName => $"Tool Call: {segment.Text}",
            OutputSegmentKind.ToolCallArguments => $"Arguments: {segment.Text}",
            _ => segment.Text
        };
    }

    internal static string ToPlainTextLine(string line)
    {
        return RemoveMarkup(line);
    }

    internal static string RemoveMarkup(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        try
        {
            return Markup.Remove(value);
        }
        catch
        {
            return value.Replace("[[", "[", StringComparison.Ordinal).Replace("]]", "]", StringComparison.Ordinal);
        }
    }

    private static string BuildApiMetricsSummary(InteractionExportRecord interaction)
    {
        var parts = new List<string>();

        if (interaction.ApiLoadDuration is > 0)
        {
            parts.Add($"load {FormatNanoseconds(interaction.ApiLoadDuration.Value)}");
        }

        if (interaction.ApiPromptEvalDuration is > 0)
        {
            parts.Add($"prefill {FormatNanoseconds(interaction.ApiPromptEvalDuration.Value)}");
        }

        if (interaction.ApiEvalDuration is > 0)
        {
            parts.Add($"decode {FormatNanoseconds(interaction.ApiEvalDuration.Value)}");
        }

        if (interaction.ApiTotalDuration is > 0)
        {
            parts.Add($"total {FormatNanoseconds(interaction.ApiTotalDuration.Value)}");
        }

        if (interaction.ApiPrefillSpeed is > 0)
        {
            parts.Add($"prefill speed {interaction.ApiPrefillSpeed.Value.ToString("F1", CultureInfo.InvariantCulture)} t/s");
        }

        if (interaction.ApiDecodeSpeed is > 0)
        {
            parts.Add($"decode speed {interaction.ApiDecodeSpeed.Value.ToString("F1", CultureInfo.InvariantCulture)} t/s");
        }

        return string.Join(", ", parts);
    }

    private static InteractionMetadataApiMetrics? BuildInteractionMetadataApiMetrics(InteractionExportRecord interaction)
    {
        if (!interaction.HasApiMetrics &&
            interaction.ApiPrefillSpeed is null &&
            interaction.ApiDecodeSpeed is null &&
            interaction.ApiLoadDuration is null &&
            interaction.ApiPromptEvalDuration is null &&
            interaction.ApiEvalDuration is null &&
            interaction.ApiTotalDuration is null)
        {
            return null;
        }

        return new InteractionMetadataApiMetrics
        {
            HasApiMetrics = interaction.HasApiMetrics,
            PrefillSpeed = interaction.ApiPrefillSpeed,
            DecodeSpeed = interaction.ApiDecodeSpeed,
            LoadDuration = interaction.ApiLoadDuration,
            PromptEvalDuration = interaction.ApiPromptEvalDuration,
            EvalDuration = interaction.ApiEvalDuration,
            TotalDuration = interaction.ApiTotalDuration
        };
    }

    private static string BuildForwardedRequestMutationSummary(InteractionExportRecord interaction)
    {
        if (interaction.ForwardedRequestMutations.Count > 0)
        {
            return ForwardedRequestMutation.Summarize(interaction.ForwardedRequestMutations);
        }

        return interaction.ForceContinueApplied
            ? ForwardedRequestMutation.SendForceContinueFollowUp().Summary
            : "none";
    }

    private static string BuildInteractionDiagnosticSummary(InteractionExportRecord interaction)
    {
        return interaction.Diagnostics.Count > 0
            ? InteractionDiagnostic.Summarize(interaction.Diagnostics)
            : "none";
    }

    private static string BuildLatencySummary(SessionLatencySummary latency)
    {
        var parts = new List<string>();

        if (latency.FirstTokenTimeAllUtc.HasValue || latency.LastTokenTimeUtc.HasValue)
        {
            parts.Add($"active span UTC {FormatTimestamp(latency.FirstTokenTimeAllUtc)} -> {FormatTimestamp(latency.LastTokenTimeUtc)}");
        }

        if (latency.ActiveSpanSeconds.HasValue)
        {
            parts.Add($"active {latency.ActiveSpanSeconds.Value.ToString("F3", CultureInfo.InvariantCulture)}s");
        }

        if (latency.AverageTimeToFirstTokenSeconds.HasValue)
        {
            parts.Add($"avg TTFT {latency.AverageTimeToFirstTokenSeconds.Value.ToString("F3", CultureInfo.InvariantCulture)}s over {latency.TimeToFirstTokenSampleCount}");
        }

        if (latency.AverageWallClockDurationSeconds.HasValue)
        {
            parts.Add($"avg duration {latency.AverageWallClockDurationSeconds.Value.ToString("F3", CultureInfo.InvariantCulture)}s over {latency.WallClockDurationSampleCount}");
        }

        if (latency.AverageApiTotalDurationSeconds.HasValue)
        {
            parts.Add($"avg API total {latency.AverageApiTotalDurationSeconds.Value.ToString("F3", CultureInfo.InvariantCulture)}s over {latency.ApiTotalDurationSampleCount}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "n/a";
    }

    private static string BuildCostSummary(SessionCostSummary cost)
    {
        if (!cost.HasPricingConfigured)
        {
            return "n/a (pricing not configured)";
        }

        if (!cost.EstimatedUsd.HasValue)
        {
            return cost.UnpricedInteractionCount > 0
                ? $"n/a (missing rates for {string.Join(", ", cost.MissingModels)})"
                : "n/a";
        }

        var formatted = "$" + cost.EstimatedUsd.Value.ToString("F6", CultureInfo.InvariantCulture);
        if (!cost.IsPartial)
        {
            return formatted;
        }

        return $"partial {formatted} ({cost.PricedInteractionCount} priced, missing rates for {string.Join(", ", cost.MissingModels)})";
    }

    private static Interaction RestoreInteraction(InteractionExportRecord interaction)
    {
        var restoredInteraction = new Interaction
        {
            Id = interaction.Id,
            RequestEnvelope = interaction.RequestEnvelope?.Clone(),
            Model = string.IsNullOrWhiteSpace(interaction.Model) ? "unknown" : interaction.Model,
            ResponseStatusCode = interaction.ResponseStatusCode,
            FinishReason = string.IsNullOrWhiteSpace(interaction.FinishReason) ? null : interaction.FinishReason,
            ForwardedRequestMutations = new List<ForwardedRequestMutation>(interaction.ForwardedRequestMutations),
            Diagnostics = new List<InteractionDiagnostic>(interaction.Diagnostics),
            PromptTokens = interaction.PromptTokens,
            CompletionTokens = interaction.CompletionTokens,
            TotalTokens = interaction.TotalTokens,
            StreamedTokenCount = interaction.StreamedTokenCount,
            ForceContinueApplied = interaction.ForceContinueApplied,
            CachedPromptTokens = interaction.CachedPromptTokens,
            ReasoningTokens = interaction.ReasoningTokens,
            HasApiMetrics = interaction.HasApiMetrics,
            ApiPrefillSpeed = interaction.ApiPrefillSpeed,
            ApiDecodeSpeed = interaction.ApiDecodeSpeed,
            ApiLoadDuration = interaction.ApiLoadDuration,
            ApiPromptEvalDuration = interaction.ApiPromptEvalDuration,
            ApiEvalDuration = interaction.ApiEvalDuration,
            ApiTotalDuration = interaction.ApiTotalDuration,
            IsStreaming = interaction.IsStreaming,
            StartTime = NormalizeTimestamp(interaction.StartTimeUtc),
            FirstTokenTime = NormalizeTimestamp(interaction.FirstTokenTimeUtc),
            EndTime = NormalizeTimestamp(interaction.EndTimeUtc),
            InputScroll = Math.Max(0, interaction.InputScroll),
            OutputScroll = Math.Max(0, interaction.OutputScroll),
            InputLines = new List<string>(interaction.InputLines),
            OutputLines = new List<OutputSegment>(interaction.OutputLines),
            CurrentInputLine = interaction.CurrentInputLine ?? string.Empty,
            CurrentOutputLine = interaction.CurrentOutputLine ?? string.Empty,
            CurrentOutputKind = interaction.CurrentOutputKind,
            InputSectionStarts = new List<int>(interaction.InputSectionStarts),
            OutputSectionStarts = new List<int>(interaction.OutputSectionStarts),
            RawInput = new StringBuilder(interaction.RawInput ?? string.Empty),
            ReplayRequestBody = new StringBuilder(interaction.RawInput ?? string.Empty),
            RawOutput = new StringBuilder(interaction.RawOutput ?? string.Empty)
        };

        for (var i = 0; i < restoredInteraction.OutputLines.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(restoredInteraction.OutputLines[i].Key))
            {
                restoredInteraction.OutputSegmentIndices[restoredInteraction.OutputLines[i].Key!] = i;
            }
        }

        return restoredInteraction;
    }

    private static int ResolveTotalTokens(Interaction interaction)
    {
        if (interaction.TotalTokens > 0)
        {
            return interaction.TotalTokens;
        }

        return Math.Max(0, interaction.PromptTokens) + Math.Max(0, interaction.CompletionTokens);
    }

    private static DateTime ResolveFirstTokenTimeAll(InteractionExportSessionSnapshot session, IReadOnlyList<Interaction> interactions)
    {
        if (session.Summary.Latency.FirstTokenTimeAllUtc is { } firstTokenTimeAllUtc)
        {
            return NormalizeTimestamp(firstTokenTimeAllUtc);
        }

        return interactions
            .Where(interaction => interaction.FirstTokenTime.HasValue)
            .Select(interaction => NormalizeTimestamp(interaction.FirstTokenTime!.Value))
            .DefaultIfEmpty(DateTime.MinValue)
            .Min();
    }

    private static DateTime ResolveLastTokenTime(InteractionExportSessionSnapshot session, IReadOnlyList<Interaction> interactions)
    {
        if (session.Summary.Latency.LastTokenTimeUtc is { } lastTokenTimeUtc)
        {
            return NormalizeTimestamp(lastTokenTimeUtc);
        }

        return interactions
            .Select(interaction => interaction.EndTime ?? interaction.FirstTokenTime)
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => NormalizeTimestamp(timestamp!.Value))
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
    }

    private static string FormatBoolean(bool value)
    {
        return value ? "yes" : "no";
    }

    private static string FormatDuration(DateTime startTimeUtc, DateTime? endTimeUtc)
    {
        if (!endTimeUtc.HasValue)
        {
            return "in progress";
        }

        var duration = endTimeUtc.Value - startTimeUtc;
        return duration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + "s";
    }

    private static string FormatNanoseconds(double nanoseconds)
    {
        return (nanoseconds / 1_000_000_000.0).ToString("F3", CultureInfo.InvariantCulture) + "s";
    }

    private static string FormatTimestamp(DateTime value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string FormatTimestamp(DateTime? value)
    {
        return value.HasValue ? FormatTimestamp(value.Value) : "n/a";
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

    private static DateTime? NormalizeTimestamp(DateTime? value)
    {
        return value.HasValue ? NormalizeTimestamp(value.Value) : null;
    }

    private static DateTime? NormalizeSessionTimestamp(DateTime value)
    {
        return value == DateTime.MinValue ? null : NormalizeTimestamp(value);
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new InvalidOperationException("A file extension is required for named saves.");
        }

        return extension[0] == '.' ? extension : "." + extension;
    }

    private static bool IsReservedWindowsFileName(string fileName)
    {
        return fileName.ToUpperInvariant() switch
        {
            "CON" or "PRN" or "AUX" or "NUL" or
            "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or
            "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9" => true,
            _ => false
        };
    }
}

internal sealed record class InteractionExportResult(string Scope, int? InteractionId, string JsonPath, string MarkdownPath, string? RawRequestPath = null, string? RawResponsePath = null)
{
    public string ArtifactPattern
    {
        get
        {
            var directoryName = Path.GetFileName(Path.GetDirectoryName(JsonPath) ?? string.Empty);
            var stem = Path.GetFileNameWithoutExtension(JsonPath);
            var suffixPattern = RawRequestPath is not null && RawResponsePath is not null
                ? $"{stem}.{{json,md,request.txt,response.txt}}"
                : stem + ".{json,md}";
            return string.IsNullOrEmpty(directoryName)
                ? suffixPattern
                : $"{directoryName}/{suffixPattern}";
        }
    }
}

internal sealed record class NamedSaveArtifactResult(string Category, string FilePath)
{
    public string DisplayPath
    {
        get
        {
            var fileName = Path.GetFileName(FilePath);
            var categoryDirectory = Path.GetFileName(Path.GetDirectoryName(FilePath) ?? string.Empty);
            var rootDirectory = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(FilePath) ?? string.Empty) ?? string.Empty);

            if (string.IsNullOrEmpty(rootDirectory) || string.IsNullOrEmpty(categoryDirectory))
            {
                return fileName;
            }

            return $"{rootDirectory}/{categoryDirectory}/{fileName}";
        }
    }
}

internal sealed record class NamedSaveInteractionArtifactsResult(string Category, string JsonPath, string MarkdownPath, string RawRequestPath, string RawResponsePath)
{
    public string DisplayPattern
    {
        get
        {
            var stem = Path.GetFileNameWithoutExtension(JsonPath);
            return $"saved/{Category}/{stem}.{{json,md,request.txt,response.txt}}";
        }
    }
}

internal enum NamedSavePane
{
    Input,
    Output
}

internal sealed record class InteractionExportRecord
{
    public int Id { get; init; }
    public InteractionRequestEnvelope? RequestEnvelope { get; init; }
    public string Model { get; init; } = "unknown";
    public int? ResponseStatusCode { get; init; }
    public string? FinishReason { get; init; }
    public List<ForwardedRequestMutation> ForwardedRequestMutations { get; init; } = new();
    public List<InteractionDiagnostic> Diagnostics { get; init; } = new();
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
    public int StreamedTokenCount { get; init; }
    public bool ForceContinueApplied { get; init; }
    public int CachedPromptTokens { get; init; }
    public int ReasoningTokens { get; init; }
    public bool HasApiMetrics { get; init; }
    public double? ApiPrefillSpeed { get; init; }
    public double? ApiDecodeSpeed { get; init; }
    public double? ApiLoadDuration { get; init; }
    public double? ApiPromptEvalDuration { get; init; }
    public double? ApiEvalDuration { get; init; }
    public double? ApiTotalDuration { get; init; }
    public bool IsStreaming { get; init; }
    public DateTime StartTimeUtc { get; init; }
    public DateTime? FirstTokenTimeUtc { get; init; }
    public DateTime? EndTimeUtc { get; init; }
    public string RawInput { get; init; } = string.Empty;
    public string RawOutput { get; init; } = string.Empty;
    public int InputScroll { get; init; }
    public int OutputScroll { get; init; }
    public List<string> InputLines { get; init; } = new();
    public List<OutputSegment> OutputLines { get; init; } = new();
    public string CurrentInputLine { get; init; } = string.Empty;
    public string CurrentOutputLine { get; init; } = string.Empty;
    public OutputSegmentKind CurrentOutputKind { get; init; } = OutputSegmentKind.Text;
    public List<int> InputSectionStarts { get; init; } = new();
    public List<int> OutputSectionStarts { get; init; } = new();
}

internal sealed record class InteractionExportSessionSnapshot
{
    public int VisibleInteractionIndex { get; init; } = -1;
    public List<InteractionExportRecord> Interactions { get; init; } = new();
    public List<string> LogEntries { get; init; } = new();
    public List<string> ActiveFixes { get; init; } = new();
    public SessionSummary Summary { get; init; } = new();
}

internal sealed record class RestoredInteractionSession
{
    public int VisibleInteractionIndex { get; init; } = -1;
    public int NextInteractionId { get; init; }
    public int TotalPromptTokens { get; init; }
    public int TotalCompletionTokens { get; init; }
    public int OverallTotalTokens { get; init; }
    public DateTime FirstTokenTimeAll { get; init; } = DateTime.MinValue;
    public DateTime LastTokenTime { get; init; } = DateTime.MinValue;
    public List<Interaction> Interactions { get; init; } = new();
    public List<string> LogEntries { get; init; } = new();
    public HashSet<string> ActiveFixes { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record class InteractionExportDocument
{
    public string Type { get; init; } = string.Empty;
    public DateTime ExportedAtUtc { get; init; }
    public InteractionExportRecord Interaction { get; init; } = new();
}

internal sealed record class SessionExportDocument
{
    public string Type { get; init; } = string.Empty;
    public DateTime ExportedAtUtc { get; init; }
    public int InteractionCount { get; init; }
    public int? VisibleInteractionId { get; init; }
    public List<string> ActiveFixes { get; init; } = new();
    public List<string> RequestLog { get; init; } = new();
    public SessionSummary Summary { get; init; } = new();
    public List<InteractionExportRecord> Interactions { get; init; } = new();
}

internal sealed record class InteractionMetadataArtifactDocument
{
    public string Type { get; init; } = "interaction-metadata";
    public DateTime WrittenAtUtc { get; init; }
    public int Id { get; init; }
    public InteractionRequestEnvelope? RequestEnvelope { get; init; }
    public string Model { get; init; } = "unknown";
    public int? ResponseStatusCode { get; init; }
    public string? FinishReason { get; init; }
    public bool IsStreaming { get; init; }
    public bool ForceContinueApplied { get; init; }
    public List<ForwardedRequestMutation> ForwardedRequestMutations { get; init; } = new();
    public List<InteractionDiagnostic> Diagnostics { get; init; } = new();
    public InteractionMetadataTokens Tokens { get; init; } = new();
    public InteractionMetadataTiming Timing { get; init; } = new();
    public InteractionMetadataApiMetrics? ApiMetrics { get; init; }
}

internal sealed record class InteractionMetadataTokens
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
    public int StreamedTokenCount { get; init; }
    public int CachedPromptTokens { get; init; }
    public int ReasoningTokens { get; init; }
}

internal sealed record class InteractionMetadataTiming
{
    public DateTime StartTimeUtc { get; init; }
    public DateTime? FirstTokenTimeUtc { get; init; }
    public DateTime? EndTimeUtc { get; init; }
}

internal sealed record class InteractionMetadataApiMetrics
{
    public bool HasApiMetrics { get; init; }
    public double? PrefillSpeed { get; init; }
    public double? DecodeSpeed { get; init; }
    public double? LoadDuration { get; init; }
    public double? PromptEvalDuration { get; init; }
    public double? EvalDuration { get; init; }
    public double? TotalDuration { get; init; }
}
