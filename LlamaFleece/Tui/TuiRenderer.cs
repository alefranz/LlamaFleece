using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

internal sealed class TuiRenderer
{
    private static readonly StatsControlLineGroup[] NormalStatsControlLineGroups =
    {
        new(
            "[bold yellow]Controls:[/] [gray]TAB[/] pane | [gray]L/R[/] select | [gray]U/D[/] scroll | [gray]PGUP/DN[/] sec",
            "Controls: TAB pane | L/R select | U/D scroll | PGUP/DN sec"),
        new(
            "[gray]SPC[/] lock | [gray]C[/] current | [gray]F[/]/[gray]SHIFT+F[/] filter | [gray]P[/] replay | [gray]R[/] raw",
            "SPC lock | C current | F/SHIFT+F filter | P replay | R raw"),
        new(
            "[gray]S[/] save | [gray]E[/]/[gray]SHIFT+E[/] export | [gray]L[/] log | [gray]X[/] fixes | [gray]ENTER[/] full | [gray]Q[/] quit",
            "S save | E/SHIFT+E export | L log | X fixes | ENTER full | Q quit")
    };

    public void BuildFrame(Layout layout, TuiSnapshot snapshot)
    {
        if (snapshot.LogMode)
        {
            ConfigureStandardLayout(layout, TuiLayoutMetrics.GetStatsPanelHeight(logMode: true, fullscreenMode: false, fixSelectionPromptActive: false, snapshot.ConsoleWidth));
            BuildLogFrame(layout, snapshot);
            return;
        }

        if (snapshot.FullscreenMode)
        {
            ConfigureFullscreenLayout(
                layout,
                snapshot.ActivePane,
                TuiLayoutMetrics.GetStatsPanelHeight(logMode: false, fullscreenMode: true, snapshot.IsFixSelectionPromptActive, snapshot.ConsoleWidth));
            BuildFullscreenFrame(layout, snapshot);
            return;
        }

        var statsPanelHeight = TuiLayoutMetrics.GetStatsPanelHeight(
            logMode: false,
            fullscreenMode: false,
            snapshot.IsFixSelectionPromptActive,
            snapshot.ConsoleWidth);

        ConfigureStandardLayout(layout, statsPanelHeight);
        BuildNormalFrame(layout, snapshot, statsPanelHeight);
    }

    private static void BuildLogFrame(Layout layout, TuiSnapshot snapshot)
    {
        var visibleLineCount = TuiLayoutMetrics.GetLogViewportLines(snapshot.ConsoleHeight);
        var startIndex = Math.Max(0, snapshot.LogEntries.Count - visibleLineCount - snapshot.LogScroll);
        var logText = string.Join(
            Environment.NewLine,
            snapshot.LogEntries
                .Skip(startIndex)
                .Take(visibleLineCount)
                .Select(Markup.Escape));
        if (string.IsNullOrWhiteSpace(logText))
        {
            logText = " [dim]No log entries yet...[/]";
        }

        layout["Interactions"].Update(new Markup(" "));
        layout["Input"].Update(new Panel(new Markup("[bold magenta]Request Log[/] [dim]All proxied traffic in realtime[/]"))
            .BorderColor(Color.Magenta)
            .Expand());
        layout["Output"].Update(new Panel(CreateMarkup(logText, "log output"))
            .Header("[bold magenta]Proxied Requests[/]")
            .BorderColor(Color.Magenta)
            .Expand());
        layout["Stats"].Update(new Panel(new Markup("[bold yellow]Controls:[/] [gray]L/ESC[/] close log, [gray]U/D[/] scroll"))
            .Header("[bold magenta]Log[/]")
            .Expand());
    }

    internal static string BuildErrorControlsLine(TuiSnapshot snapshot)
    {
        if (snapshot.LogMode)
        {
            return "[bold yellow]Controls:[/] [gray]L/ESC[/] close log, [gray]U/D[/] scroll, [gray]Q[/] quit";
        }

        if (snapshot.FullscreenMode)
        {
            return "[bold yellow]Controls:[/] [gray]ESC/ENTER[/] exit fullscreen, [gray]TAB[/] switch panel, [gray]U/D[/] scroll, [gray]L[/] log, [gray]Q[/] quit";
        }

        return "[bold yellow]Controls:[/] [gray]TAB[/] panes, [gray]P[/] replay, [gray]R[/] raw, [gray]L[/] log, [gray]Q[/] quit";
    }

    private static void BuildFullscreenFrame(Layout layout, TuiSnapshot snapshot)
    {
        var visible = snapshot.VisibleInteraction;
        var viewportLineCount = TuiLayoutMetrics.GetFullscreenViewportLines(snapshot.ConsoleHeight);

        if (snapshot.ActivePane == 0)
        {
            var header = "[bold cyan]Interactions[/]" + (snapshot.Locked ? " [bold red]🔒[/]" : string.Empty) + " *(Fullscreen)*";
            var body = BuildInteractionStrip(snapshot);
            layout["Interactions"].Update(new Panel(CreateMarkup(body, "fullscreen interactions"))
                .Header(header)
                .BorderColor(Color.Cyan)
                .Expand());
            layout["Input"].Update(new Markup(" "));
            layout["Output"].Update(new Markup(" "));
        }
        else if (snapshot.ActivePane == 1)
        {
            var header = "[bold green]Input[/]" + (snapshot.RawMode ? " [bold red](RAW)[/]" : string.Empty) + " *(Fullscreen)*";
            var body = visible == null ? " " : BuildInputText(visible, snapshot.RawMode, viewportLineCount);
            layout["Interactions"].Update(new Markup(" "));
            layout["Input"].Update(new Panel(CreateMarkup(body, "fullscreen input"))
                .Header(header)
                .BorderColor(Color.Green)
                .Expand());
            layout["Output"].Update(new Markup(" "));
        }
        else
        {
            var header = "[bold blue]Output[/]" + (snapshot.RawMode ? " [bold red](RAW)[/]" : string.Empty) + " *(Fullscreen)*";
            var body = visible == null ? " " : BuildOutputText(visible, snapshot.RawMode, viewportLineCount);
            layout["Interactions"].Update(new Markup(" "));
            layout["Input"].Update(new Markup(" "));
            layout["Output"].Update(new Panel(CreateMarkup(body, "fullscreen output"))
                .Header(header)
                .BorderColor(Color.Blue)
                .Expand());
        }

        var statsContent = snapshot.IsFixSelectionPromptActive
            ? BuildFixSelectionStatsContent(snapshot)
            : BuildFullscreenStatsContent(snapshot);
        var statsHeader = snapshot.IsFixSelectionPromptActive
            ? "[bold magenta]Fixes[/]"
            : "[bold magenta]Fullscreen[/]";

        layout["Stats"].Update(new Panel(statsContent)
            .Header(statsHeader)
            .Expand());
    }

    private static void BuildNormalFrame(Layout layout, TuiSnapshot snapshot, int statsPanelHeight)
    {
        var visible = snapshot.VisibleInteraction;
        var interactionsHeader = "[bold cyan]Interactions[/]" +
                                 (snapshot.HasActiveFilter
                                     ? $" [yellow]({snapshot.FilteredInteractionCount}/{snapshot.TotalInteractionCount} filtered)[/]"
                                     : string.Empty) +
                                 (snapshot.Locked ? " [bold red]🔒[/]" : string.Empty) +
                                 (snapshot.ActivePane == 0 ? " *(Selected)*" : string.Empty);

        layout["Interactions"].Update(new Panel(CreateMarkup(BuildInteractionStrip(snapshot), "interactions"))
            .Header(interactionsHeader)
            .BorderColor(snapshot.ActivePane == 0 ? Color.Cyan : Color.Grey)
            .Expand());

        var inputViewportLineCount = TuiLayoutMetrics.GetInputViewportLines(false, snapshot.ConsoleHeight);
        var inputText = visible == null ? " " : BuildInputText(visible, snapshot.RawMode, inputViewportLineCount);
        layout["Input"].Update(new Panel(CreateMarkup(inputText, "input"))
            .Header("[bold green]Input[/]" + (snapshot.RawMode ? " [bold red](RAW)[/]" : string.Empty) + (snapshot.ActivePane == 1 ? " *(Selected)*" : string.Empty))
            .BorderColor(snapshot.ActivePane == 1 ? Color.Green : Color.Grey)
            .Expand());

        var outputViewportLineCount = TuiLayoutMetrics.GetOutputViewportLines(false, snapshot.ConsoleHeight, statsPanelHeight);
        var outputText = visible == null ? " " : BuildOutputText(visible, snapshot.RawMode, outputViewportLineCount);
        layout["Output"].Update(new Panel(CreateMarkup(outputText, "output"))
            .Header("[bold blue]Output[/]" + (snapshot.RawMode ? " [bold red](RAW)[/]" : string.Empty) + (snapshot.ActivePane == 2 ? " *(Selected)*" : string.Empty))
            .BorderColor(snapshot.ActivePane == 2 ? Color.Blue : Color.Grey)
            .Expand());

        var statsContent = snapshot.IsFixSelectionPromptActive
            ? BuildFixSelectionStatsContent(snapshot)
            : BuildNormalStatsContent(snapshot);
        var statsHeader = snapshot.IsFixSelectionPromptActive
            ? "[bold magenta]Fixes[/]"
            : "[bold magenta]Stats[/]";

        layout["Stats"].Update(new Panel(statsContent)
            .Header(statsHeader)
            .Expand());
    }

    private static void ConfigureStandardLayout(Layout layout, int statsPanelHeight)
    {
        ConfigurePane(layout["Interactions"], isVisible: true, size: TuiLayoutMetrics.InteractionsPanelHeight);
        ConfigurePane(layout["Input"], isVisible: true, size: TuiLayoutMetrics.InputPanelHeight);
        ConfigurePane(layout["Output"], isVisible: true, size: null);
        ConfigurePane(layout["Stats"], isVisible: true, size: statsPanelHeight);
    }

    private static void ConfigureFullscreenLayout(Layout layout, int activePane, int statsPanelHeight)
    {
        ConfigurePane(layout["Interactions"], isVisible: activePane == 0, size: activePane == 0 ? null : TuiLayoutMetrics.InteractionsPanelHeight);
        ConfigurePane(layout["Input"], isVisible: activePane == 1, size: activePane == 1 ? null : TuiLayoutMetrics.InputPanelHeight);
        ConfigurePane(layout["Output"], isVisible: activePane == 2, size: null);
        ConfigurePane(layout["Stats"], isVisible: true, size: statsPanelHeight);
    }

    private static void ConfigurePane(Layout pane, bool isVisible, int? size)
    {
        pane.IsVisible = isVisible;
        pane.Size = size;
    }

    private static Markup CreateMarkup(string text, string context)
    {
        try
        {
            return new Markup(text);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid markup in {context}: {DescribeMarkup(text)}", ex);
        }
    }

    private static Markup CreateSingleLineMarkup(string text, string context)
    {
        var markup = CreateMarkup(text, context);
        markup.Overflow = Overflow.Ellipsis;
        return markup;
    }

    private static string DescribeMarkup(string text)
    {
        const int maxLength = 240;
        var normalized = (text ?? string.Empty)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        if (normalized.Length > maxLength)
        {
            normalized = normalized[..maxLength] + "...";
        }

        return normalized;
    }

    private static string BuildInteractionStrip(TuiSnapshot snapshot)
    {
        if (snapshot.TotalInteractionCount == 0)
        {
            return " ";
        }

        if (snapshot.Interactions.Count == 0)
        {
            return snapshot.HasActiveFilter
                ? "[dim]No interactions match the active filter. Press Shift+F to clear.[/]"
                : " ";
        }

        var maxVisibleCharacters = Math.Max(20, snapshot.ConsoleWidth - 6);
        const int slotWidth = 5;
        var visibleSlotCount = Math.Max(1, maxVisibleCharacters / slotWidth);
        var startIndex = Math.Max(0, snapshot.VisibleIndex - visibleSlotCount / 2);
        if (startIndex + visibleSlotCount > snapshot.Interactions.Count)
        {
            startIndex = Math.Max(0, snapshot.Interactions.Count - visibleSlotCount);
        }

        var endIndex = Math.Min(snapshot.Interactions.Count, startIndex + visibleSlotCount);
        var strip = string.Join(" ", snapshot.Interactions.Skip(startIndex).Take(endIndex - startIndex).Select((interaction, offset) =>
        {
            var actualIndex = startIndex + offset;
            var mutationBadge = HasForwardedRequestChange(interaction) ? "!" : string.Empty;
            if (actualIndex == snapshot.VisibleIndex)
            {
                return HasForwardedRequestChange(interaction)
                    ? $"[bold black on yellow] {interaction.Id}{mutationBadge} [/]"
                    : $"[bold black on white] {interaction.Id} [/]";
            }

            return HasForwardedRequestChange(interaction)
                ? $"[bold yellow on black] {interaction.Id}{mutationBadge} [/]"
                : $"[bold white on black] {interaction.Id} [/]";
        }));

        return string.IsNullOrWhiteSpace(strip) ? " " : strip;
    }

    private static string BuildInputText(TuiVisibleInteractionSnapshot visible, bool rawMode, int viewportLineCount)
    {
        if (rawMode)
        {
            return BuildRawTextWindow(visible.RawInputText, visible.InputScroll, viewportLineCount);
        }

        var inputLines = new List<string>(visible.InputLines);
        if (!string.IsNullOrEmpty(visible.CurrentInputLine))
        {
            inputLines.Add(visible.CurrentInputLine);
        }

        return BuildLineWindow(inputLines, visible.InputScroll, viewportLineCount);
    }

    private static string BuildOutputText(TuiVisibleInteractionSnapshot visible, bool rawMode, int viewportLineCount)
    {
        if (rawMode)
        {
            return BuildRawTextWindow(visible.RawOutputText, visible.OutputScroll, viewportLineCount);
        }

        var outputLines = new List<OutputSegment>(visible.OutputLines);
        if (!string.IsNullOrEmpty(visible.CurrentOutputLine))
        {
            outputLines.Add(new OutputSegment(visible.CurrentOutputKind, visible.CurrentOutputLine));
        }

        var window = GetWindow(outputLines, visible.OutputScroll, viewportLineCount);
        var formatted = TuiOutputFormatter.FormatLines(window);
        return string.IsNullOrWhiteSpace(formatted) ? " " : formatted;
    }

    private static string BuildRawTextWindow(string rawText, int scroll, int viewportLineCount)
    {
        var lines = (rawText ?? string.Empty).Split('\n');
        var text = string.Join(Environment.NewLine, GetWindow(lines, scroll, viewportLineCount).Select(Markup.Escape));
        return string.IsNullOrWhiteSpace(text) ? " " : text;
    }

    private static string BuildLineWindow(IReadOnlyList<string> lines, int scroll, int viewportLineCount)
    {
        var text = string.Join(Environment.NewLine, GetWindow(lines, scroll, viewportLineCount));
        return string.IsNullOrWhiteSpace(text) ? " " : text;
    }

    private static IEnumerable<T> GetWindow<T>(IReadOnlyList<T> lines, int scroll, int viewportLineCount)
    {
        var startIndex = Math.Max(0, lines.Count - Math.Max(1, viewportLineCount) - Math.Max(0, scroll));
        return lines.Skip(startIndex).Take(Math.Max(1, viewportLineCount));
    }

    private static IRenderable BuildNormalStatsContent(TuiSnapshot snapshot)
    {
        var summaryLines = BuildNormalStatsSummaryLines(snapshot);
        var controlsLines = BuildCompactNormalControlsLines(snapshot.ConsoleWidth);

        var lines = new List<IRenderable>(summaryLines.Count + controlsLines.Count);
        for (var index = 0; index < summaryLines.Count; index++)
        {
            lines.Add(CreateSingleLineMarkup(summaryLines[index], $"stats summary {index + 1}"));
        }

        for (var index = 0; index < controlsLines.Count; index++)
        {
            lines.Add(CreateSingleLineMarkup(controlsLines[index], $"stats controls {index + 1}"));
        }

        var contentLineCount = TuiLayoutMetrics.GetStatsPanelHeight(
            logMode: false,
            fullscreenMode: false,
            fixSelectionPromptActive: false,
            snapshot.ConsoleWidth) - TuiLayoutMetrics.PanelChromeHeight;

        while (lines.Count < contentLineCount)
        {
            lines.Add(new Markup(" "));
        }

        return new Rows(lines.ToArray())
        {
            Expand = true
        };
    }

    private static IRenderable BuildFullscreenStatsContent(TuiSnapshot snapshot)
    {
        var summaryLines = BuildStatsSummaryLines(snapshot, TuiLayoutMetrics.StatsPanelHeight - TuiLayoutMetrics.PanelChromeHeight - 1);
        var lines = new List<IRenderable>(summaryLines.Count + 1);

        for (var index = 0; index < summaryLines.Count; index++)
        {
            lines.Add(CreateSingleLineMarkup(summaryLines[index], $"fullscreen stats summary {index + 1}"));
        }

        lines.Add(CreateSingleLineMarkup("[bold yellow]Controls:[/] [gray]ESC/ENTER[/] exit fullscreen, [gray]TAB[/] switch panel, [gray]U/D[/] scroll, [gray]F[/]/[gray]SHIFT+F[/] filter, [gray]S[/] save, [gray]E[/] export, [gray]SHIFT+E[/] session, [gray]X[/] fixes", "fullscreen stats controls"));

        while (lines.Count < TuiLayoutMetrics.StatsPanelHeight - TuiLayoutMetrics.PanelChromeHeight)
        {
            lines.Add(new Markup(" "));
        }

        return new Rows(lines.ToArray())
        {
            Expand = true
        };
    }

    private static IReadOnlyList<string> BuildNormalStatsSummaryLines(TuiSnapshot snapshot)
    {
        var summaryLineCount = TuiLayoutMetrics.GetNormalStatsSummaryLineCount(snapshot.ConsoleWidth);
        var lines = BuildStatsSummaryLines(snapshot, summaryLineCount).ToArray();

        if (summaryLineCount >= 4)
        {
            lines[0] = BuildCompactNormalMetadataLine(snapshot);
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildStatsSummaryLines(TuiSnapshot snapshot, int summaryLineCount)
    {
        var statsLines = BuildStatsLines(snapshot);
        var contextLine = BuildStatsContextLine(snapshot, statsLines);
        var lines = new List<string>(summaryLineCount);

        if (!string.IsNullOrWhiteSpace(contextLine))
        {
            lines.Add(contextLine);
        }
        else if (summaryLineCount >= 4)
        {
            lines.Add(statsLines.MetadataLine);
        }

        lines.Add(statsLines.CurrentLine);
        lines.Add(AppendActiveFixesShorthand(statsLines.SessionLine, snapshot.ActiveFixesShorthand));

        if (lines.Count < summaryLineCount)
        {
            lines.Add(statsLines.LatencyLine);
        }

        return lines;
    }

    private static string? BuildStatsContextLine(TuiSnapshot snapshot, StatsTextLines statsLines)
    {
        if (snapshot.IsNamedSavePromptActive)
        {
            return BuildSaveLine(snapshot);
        }

        if (snapshot.IsInteractionFilterPromptActive || snapshot.HasActiveFilter)
        {
            return statsLines.FilterLine;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.StatusMessage))
        {
            return statsLines.StatusLine;
        }

        return null;
    }

    private static string AppendActiveFixesShorthand(string line, string activeFixesShorthand)
    {
        return string.IsNullOrWhiteSpace(activeFixesShorthand)
            ? line
            : line + activeFixesShorthand;
    }

    private static IReadOnlyList<string> BuildCompactNormalControlsLines(int consoleWidth)
    {
        return TuiLayoutMetrics.GetNormalStatsControlLineCount(consoleWidth) switch
        {
            1 => new[] { JoinCompactNormalControlLines(0, 3) },
            2 => new[] { JoinCompactNormalControlLines(0, 2), NormalStatsControlLineGroups[2].Markup },
            _ => NormalStatsControlLineGroups.Select(group => group.Markup).ToArray()
        };
    }

    private static string JoinCompactNormalControlLines(int startIndex, int count)
    {
        return string.Join(" | ", NormalStatsControlLineGroups.Skip(startIndex).Take(count).Select(group => group.Markup));
    }

    private static IRenderable BuildFixSelectionStatsContent(TuiSnapshot snapshot)
    {
        var lines = new List<IRenderable>
        {
            CreateSingleLineMarkup(BuildFixSelectionSummaryLine(snapshot), "fix selection summary")
        };

        foreach (var itemLine in BuildFixSelectionItemLines(snapshot, maxVisibleItems: 3))
        {
            lines.Add(CreateSingleLineMarkup(itemLine, "fix selection item"));
        }

        while (lines.Count < 4)
        {
            lines.Add(new Markup(" "));
        }

        lines.Add(CreateSingleLineMarkup("[bold yellow]Controls:[/] [gray]U/D[/] select | [gray]SPC[/] toggle | [gray]ENTER[/] apply | [gray]ESC[/] cancel", "fix selection controls"));

        return new Rows(lines.ToArray())
        {
            Expand = true
        };
    }

    private static string BuildFixSelectionSummaryLine(TuiSnapshot snapshot)
    {
        if (snapshot.FixSelectionItems.Count == 0)
        {
            return "[bold gray]Fixes:[/] [dim]No fixes available.[/]";
        }

        var enabledCount = snapshot.FixSelectionItems.Count(item => item.Enabled);
        var selectedIndex = snapshot.FixSelectionIndex < 0
            ? 0
            : Math.Min(snapshot.FixSelectionIndex, snapshot.FixSelectionItems.Count - 1);

        return $"[bold gray]Fixes:[/] {enabledCount}/{snapshot.FixSelectionItems.Count} enabled | [bold gray]Selected:[/] {selectedIndex + 1}/{snapshot.FixSelectionItems.Count}";
    }

    private static IEnumerable<string> BuildFixSelectionItemLines(TuiSnapshot snapshot, int maxVisibleItems)
    {
        if (snapshot.FixSelectionItems.Count == 0)
        {
            return new[] { "[dim]No fixes available.[/]" };
        }

        var visibleCount = Math.Min(maxVisibleItems, snapshot.FixSelectionItems.Count);
        var selectedIndex = snapshot.FixSelectionIndex < 0
            ? 0
            : Math.Min(snapshot.FixSelectionIndex, snapshot.FixSelectionItems.Count - 1);
        var startIndex = Math.Max(0, selectedIndex - visibleCount / 2);

        if (startIndex + visibleCount > snapshot.FixSelectionItems.Count)
        {
            startIndex = Math.Max(0, snapshot.FixSelectionItems.Count - visibleCount);
        }

        var lines = new List<string>(visibleCount);
        for (var index = startIndex; index < startIndex + visibleCount; index++)
        {
            var item = snapshot.FixSelectionItems[index];
            var selectionMarker = index == selectedIndex ? "[bold cyan]>[/]" : " ";
            var toggleMarker = item.Enabled ? "[green]ON[/]" : "[dim]OFF[/]";
            lines.Add($"{selectionMarker} {toggleMarker} {Markup.Escape(item.Name)} [dim]({Markup.Escape(item.Shorthand)})[/]");
        }

        return lines;
    }

    private static string BuildCompactNormalMetadataLine(TuiSnapshot snapshot)
    {
        var visible = snapshot.VisibleInteraction;
        var modelValue = visible?.Model ?? "unknown";
        var endpointValue = visible?.RequestTarget ?? "unknown";
        var status = visible?.ResponseStatusCode?.ToString() ?? "n/a";
        var finishValue = string.IsNullOrWhiteSpace(visible?.FinishReason) ? "n/a" : visible!.FinishReason;
        var forwardedValue = visible is null
            ? "n/a"
            : BuildForwardedRequestDisplayPlain(visible);
        var diagnosticsValue = visible is not null && visible.Diagnostics.Count > 0 && snapshot.ConsoleWidth >= 96
            ? BuildDiagnosticDisplayPlain(visible)
            : null;
        var fieldWidths = GetCompactMetadataFieldWidths(
            snapshot.ConsoleWidth,
            status.Length,
            modelValue.Length,
            endpointValue.Length,
            finishValue.Length,
            forwardedValue.Length,
            diagnosticsValue?.Length ?? 0,
            diagnosticsValue is not null);

        var model = TruncatePlainText(modelValue, fieldWidths.Model);
        var endpoint = TruncatePlainText(endpointValue, fieldWidths.Endpoint);
        var finish = TruncatePlainText(finishValue, fieldWidths.Finish);
        var forwarded = TruncatePlainText(forwardedValue, fieldWidths.Forwarded);

        var line = $"[bold gray]Model:[/] {Markup.Escape(model)} | [bold gray]Endpoint:[/] {Markup.Escape(endpoint)} | [bold gray]Status:[/] {status} | [bold gray]Fwd:[/] {Markup.Escape(forwarded)} | [bold gray]Finish:[/] {Markup.Escape(finish)}";
        if (diagnosticsValue is not null)
        {
            var diagnostics = TruncatePlainText(diagnosticsValue, fieldWidths.Diagnostics);
            line += $" | [bold gray]Diag:[/] {Markup.Escape(diagnostics)}";
        }

        return line;
    }

    private static string BuildCompactNormalSummaryLine(TuiSnapshot snapshot)
    {
        if (snapshot.IsNamedSavePromptActive)
        {
            var fileName = string.IsNullOrEmpty(snapshot.PendingSaveFileName)
                ? "Type a file name"
                : TruncatePlainText(snapshot.PendingSaveFileName, 42);

            return $"[bold gray]Save:[/] [yellow]{Markup.Escape(fileName)}[/][bold cyan]█[/]";
        }

        if (snapshot.IsInteractionFilterPromptActive)
        {
            var query = string.IsNullOrEmpty(snapshot.PendingInteractionFilterQuery)
                ? "Type a query"
                : TruncatePlainText(snapshot.PendingInteractionFilterQuery, 42);

            return $"[bold gray]Filter:[/] [yellow]{Markup.Escape(query)}[/][bold cyan]█[/]";
        }

        if (snapshot.HasActiveFilter)
        {
            var summary = TruncatePlainText(snapshot.ActiveFilterSummary, 36);
            return $"[bold gray]Filter:[/] [yellow]{Markup.Escape(summary)}[/] [dim]({snapshot.FilteredInteractionCount}/{snapshot.TotalInteractionCount}, Shift+F clears)[/]";
        }

        if (!string.IsNullOrWhiteSpace(snapshot.StatusMessage))
        {
            return $"[bold gray]Status:[/] {(snapshot.StatusIsError ? "[bold red]" : "[bold green]")}{Markup.Escape(TruncatePlainText(snapshot.StatusMessage, 52))}[/]";
        }

        return $"[bold gray]Current:[/] {BuildCompactCurrentSummary(snapshot.VisibleInteraction)} | [bold gray]Session:[/] {BuildCompactSessionSummary(snapshot.SessionSummary, snapshot.Locked)} | [bold gray]Lat:[/] {BuildCompactLatencySummary(snapshot.SessionSummary.Latency)}";
    }

    private static string BuildCompactCurrentSummary(TuiVisibleInteractionSnapshot? visible)
    {
        if (visible is null)
        {
            return "waiting";
        }

        if (visible.TotalTokens > 0 || visible.StreamedTokenCount > 0)
        {
            var completionTokens = visible.CompletionTokens > 0 ? visible.CompletionTokens : visible.StreamedTokenCount;
            var summary = $"{visible.PromptTokens}p/{completionTokens}d/{visible.TotalTokens}t";
            if (visible.IsStreaming)
            {
                summary += " live";
            }

            return summary;
        }

        if (visible.IsStreaming)
        {
            return $"{visible.StreamedTokenCount} tok live";
        }

        return "waiting";
    }

    private static string BuildCompactSessionSummary(SessionSummary summary, bool locked)
    {
        var sessionText = $"{summary.InteractionCount} req, {summary.Tokens.TotalTokens} tok";
        if (locked)
        {
            sessionText += ", lock";
        }

        return sessionText;
    }

    private static string BuildCompactLatencySummary(SessionLatencySummary latency)
    {
        if (latency.ActiveSpanSeconds.HasValue)
        {
            return $"{latency.ActiveSpanSeconds.Value.ToString("F1", CultureInfo.InvariantCulture)}s";
        }

        if (latency.AverageTimeToFirstTokenSeconds.HasValue)
        {
            return $"ttft {latency.AverageTimeToFirstTokenSeconds.Value.ToString("F2", CultureInfo.InvariantCulture)}s";
        }

        if (latency.AverageWallClockDurationSeconds.HasValue)
        {
            return $"dur {latency.AverageWallClockDurationSeconds.Value.ToString("F2", CultureInfo.InvariantCulture)}s";
        }

        return "n/a";
    }

    internal static string BuildForwardedRequestDisplayPlain(TuiVisibleInteractionSnapshot visible)
    {
        var mutationCount = visible.ForwardedRequestMutations.Count;
        if (mutationCount == 0 && !visible.ForceContinueApplied)
        {
            return "unchanged";
        }

        if (!visible.ForceContinueApplied && !ForwardedRequestMutation.HasAttentionWorthyChanges(visible.ForwardedRequestMutations))
        {
            var compactSummary = ForwardedRequestMutation.SummarizeCompact(visible.ForwardedRequestMutations);
            return string.IsNullOrWhiteSpace(compactSummary)
                ? "normalized"
                : compactSummary;
        }

        var effectiveCount = Math.Max(mutationCount, visible.ForceContinueApplied ? 1 : 0);
        return effectiveCount == 1
            ? "changed(1)"
            : $"changed({effectiveCount})";
    }

    private static string TruncatePlainText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return maxLength <= 3
            ? text[..maxLength]
            : text[..(maxLength - 3)] + "...";
    }

    private sealed record CompactMetadataFieldWidths(
        int Model,
        int Endpoint,
        int Finish,
        int Forwarded,
        int Diagnostics);

    private sealed record StatsControlLineGroup(string Markup, string PlainText);

    private static CompactMetadataFieldWidths GetCompactMetadataFieldWidths(
        int consoleWidth,
        int statusLength,
        int modelLength,
        int endpointLength,
        int finishLength,
        int forwardedLength,
        int diagnosticsLength,
        bool hasDiagnostics)
    {
        var preferredWidths = hasDiagnostics
            ? new[] { modelLength, endpointLength, finishLength, forwardedLength, diagnosticsLength }
            : new[] { modelLength, endpointLength, finishLength, forwardedLength };
        var widths = hasDiagnostics
            ? new[]
            {
                Math.Min(14, modelLength),
                Math.Min(18, endpointLength),
                Math.Min(10, finishLength),
                Math.Min(12, forwardedLength),
                Math.Min(16, diagnosticsLength)
            }
            : new[]
            {
                Math.Min(14, modelLength),
                Math.Min(18, endpointLength),
                Math.Min(10, finishLength),
                Math.Min(12, forwardedLength)
            };

        var availableTextWidth = GetCompactMetadataAvailableTextWidth(consoleWidth, statusLength, hasDiagnostics);
        DistributeResponsiveWidth(widths, preferredWidths, availableTextWidth - widths.Sum());

        return new CompactMetadataFieldWidths(
            Model: widths[0],
            Endpoint: widths[1],
            Finish: widths[2],
            Forwarded: widths[3],
            Diagnostics: hasDiagnostics ? widths[4] : 0);
    }

    private static int GetCompactMetadataAvailableTextWidth(int consoleWidth, int statusLength, bool hasDiagnostics)
    {
        var innerWidth = Math.Max(0, consoleWidth - 4);
        var fixedWidth = 50 + statusLength + (hasDiagnostics ? 9 : 0);
        return Math.Max(0, innerWidth - fixedWidth);
    }

    private static void DistributeResponsiveWidth(int[] widths, int[] preferredWidths, int extraWidth)
    {
        if (extraWidth <= 0)
        {
            return;
        }

        var allocationOrder = new[] { 0, 1, 0, 1, 2, 3, 4 };
        while (extraWidth > 0)
        {
            var grew = false;
            foreach (var index in allocationOrder)
            {
                if (index >= widths.Length || widths[index] >= preferredWidths[index])
                {
                    continue;
                }

                widths[index]++;
                extraWidth--;
                grew = true;
                if (extraWidth == 0)
                {
                    break;
                }
            }

            if (!grew)
            {
                break;
            }
        }
    }

    private sealed record StatsTextLines(
        string MetadataLine,
        string CurrentLine,
        string SessionLine,
        string LatencyLine,
        string FilterLine,
        string ControlsLine,
        string StatusLine);

    private static StatsTextLines BuildStatsLines(TuiSnapshot snapshot)
    {
        var visible = snapshot.VisibleInteraction;
        var sessionSummary = snapshot.SessionSummary;
        var currentTokensDisplay = "Waiting for completion...";

        if (visible != null && (visible.TotalTokens > 0 || visible.StreamedTokenCount > 0))
        {
            double prefillSpeed = 0;
            double decodeSpeed = 0;
            double liveDecodeSpeed = 0;
            var speedSource = string.Empty;

            if (visible.HasApiMetrics && visible.ApiPrefillSpeed.HasValue)
            {
                prefillSpeed = visible.ApiPrefillSpeed.Value;
                decodeSpeed = visible.ApiDecodeSpeed ?? 0;
                speedSource = " [dim](API)[/]";
            }
            else if (visible.FirstTokenTime.HasValue)
            {
                var endTime = visible.EndTime ?? DateTime.UtcNow;
                var prefillSpan = (visible.FirstTokenTime.Value - visible.StartTime).TotalSeconds;
                if (prefillSpan > 0)
                {
                    prefillSpeed = visible.PromptTokens / prefillSpan;
                }

                var decodeSpan = (endTime - visible.FirstTokenTime.Value).TotalSeconds;
                if (decodeSpan > 0)
                {
                    decodeSpeed = visible.CompletionTokens / decodeSpan;
                }

                speedSource = " [dim](local)[/]";
            }

            if (visible.IsStreaming && visible.FirstTokenTime.HasValue)
            {
                var elapsed = (DateTime.UtcNow - visible.FirstTokenTime.Value).TotalSeconds;
                if (elapsed > 0)
                {
                    liveDecodeSpeed = visible.StreamedTokenCount / elapsed;
                }
            }

            var displayedCompletionTokens = visible.CompletionTokens > 0 ? visible.CompletionTokens : visible.StreamedTokenCount;
            var liveBadge = visible.IsStreaming ? " [green blink]● LIVE[/]" : string.Empty;
            var cachedDisplay = visible.CachedPromptTokens > 0 ? $" [bold dim]({visible.CachedPromptTokens} cached)[/]" : string.Empty;
            var reasoningDisplay = visible.ReasoningTokens > 0 ? $" [magenta]{visible.ReasoningTokens} reasoning[/]" : string.Empty;
            var decodeSpeedDisplay = visible.IsStreaming && liveDecodeSpeed > 0
                ? $"({liveDecodeSpeed:F1} t/s live)"
                : $"({decodeSpeed:F1} t/s)";

            currentTokensDisplay = $"{visible.PromptTokens} prefill ({prefillSpeed:F1} t/s){cachedDisplay}, {displayedCompletionTokens} decode {decodeSpeedDisplay}{reasoningDisplay}, {visible.TotalTokens} total{speedSource}{liveBadge}";
        }
        else if (visible != null && visible.IsStreaming)
        {
            var liveSpeed = 0d;
            if (visible.FirstTokenTime.HasValue)
            {
                var elapsed = (DateTime.UtcNow - visible.FirstTokenTime.Value).TotalSeconds;
                if (elapsed > 0)
                {
                    liveSpeed = visible.StreamedTokenCount / elapsed;
                }
            }

            currentTokensDisplay = $"[green blink]● LIVE[/] {visible.StreamedTokenCount} tokens ({liveSpeed:F1} t/s)";
        }

        var apiMetricsSummary = string.Empty;
        if (visible != null && visible.HasApiMetrics)
        {
            var apiParts = new List<string>();
            if (visible.ApiLoadDuration is > 0)
            {
                apiParts.Add($"load: {visible.ApiLoadDuration.Value / 1_000_000_000.0:F3}s");
            }

            if (visible.ApiPromptEvalDuration is > 0)
            {
                apiParts.Add($"prefill: {visible.ApiPromptEvalDuration.Value / 1_000_000_000.0:F3}s");
            }

            if (visible.ApiEvalDuration is > 0)
            {
                apiParts.Add($"decode: {visible.ApiEvalDuration.Value / 1_000_000_000.0:F3}s");
            }

            if (visible.ApiTotalDuration is > 0)
            {
                apiParts.Add($"total: {visible.ApiTotalDuration.Value / 1_000_000_000.0:F3}s");
            }

            if (apiParts.Count > 0)
            {
                apiMetricsSummary = string.Join(", ", apiParts);
            }
        }

        var endpointDisplay = visible is null ? "unknown" : Markup.Escape(visible.RequestTarget);
        var statusDisplay = visible?.ResponseStatusCode?.ToString() ?? "n/a";
        var finishDisplay = visible is null || string.IsNullOrWhiteSpace(visible.FinishReason)
            ? "n/a"
            : Markup.Escape(visible.FinishReason);
        var forwardedDisplay = visible is null ? "n/a" : BuildForwardedRequestDisplay(visible);
        var filterLine = BuildFilterLine(snapshot);

        var statusLine = string.IsNullOrWhiteSpace(snapshot.StatusMessage)
            ? "[bold gray]Status:[/] [dim]Press S to save the current view, P to replay it, E to export it, or SHIFT+E for the full session.[/]"
            : $"[bold gray]Status:[/] {(snapshot.StatusIsError ? "[bold red]" : "[bold green]")}{Markup.Escape(snapshot.StatusMessage)}[/]";

        var sessionLine = $"[bold gray]Session:[/] {BuildSessionSummaryText(sessionSummary, snapshot.Locked)}";
        var latencyLine = $"[bold gray]Latency:[/] {BuildSessionLatencyText(sessionSummary.Latency)}";
        if (!string.IsNullOrWhiteSpace(apiMetricsSummary))
        {
            latencyLine += $" | [bold gray]API:[/] [cyan]{apiMetricsSummary}[/]";
        }

        var metadataLine = $"[bold gray]Model:[/] {Markup.Escape(visible?.Model ?? "unknown")} | [bold gray]Endpoint:[/] {endpointDisplay} | [bold gray]Status:[/] {statusDisplay} | [bold gray]Finish:[/] {finishDisplay} | [bold gray]Fwd:[/] {forwardedDisplay}";
        if (visible is not null && visible.Diagnostics.Count > 0)
        {
            metadataLine += $" | [bold gray]Diag:[/] {BuildDiagnosticDisplay(visible)}";
        }

        var currentLine = $"[bold gray]Current:[/]";
        if (visible is not null)
        {
            currentLine += $" [bold gray]Fwd:[/] {forwardedDisplay} |";
        }

        currentLine += $" Tokens: {currentTokensDisplay}";

        return new StatsTextLines(
            MetadataLine: metadataLine,
            CurrentLine: currentLine,
            SessionLine: sessionLine,
            LatencyLine: latencyLine,
            FilterLine: filterLine,
                ControlsLine: $"[bold yellow]Controls:[/] [gray]TAB[/] panes, [gray]LEFT/RIGHT[/] select, [gray]U/D[/] scroll, [gray]PGUP/DN[/] section, [gray]SPC[/] lock, [gray]C[/] current, [gray]F[/] filter, [gray]SHIFT+F[/] clear, [gray]P[/] replay, [gray]R[/] raw, [gray]S[/] save, [gray]E[/] export, [gray]SHIFT+E[/] session, [gray]L[/] log, [gray]X[/] fixes, [gray]ENTER[/] fullscreen, [gray]Q[/] quit{snapshot.ActiveFixesShorthand}",
            StatusLine: statusLine);
    }

    private static string BuildStatsText(TuiSnapshot snapshot)
    {
        var statsLines = BuildStatsLines(snapshot);
        return string.Join(
            "\n",
            statsLines.MetadataLine,
            statsLines.CurrentLine,
            statsLines.SessionLine,
            statsLines.LatencyLine,
            statsLines.FilterLine,
            statsLines.ControlsLine,
            statsLines.StatusLine);
    }

    private static string BuildFullscreenStatsText(TuiSnapshot snapshot)
    {
        var lines = new List<string>();

        if (snapshot.IsNamedSavePromptActive)
        {
            lines.Add(BuildSaveLine(snapshot));
        }
        else if (snapshot.IsInteractionFilterPromptActive || snapshot.HasActiveFilter)
        {
            lines.Add(BuildFilterLine(snapshot));
        }

        lines.Add("[bold yellow]Controls:[/] [gray]ESC/ENTER[/] exit fullscreen, [gray]TAB[/] switch panel, [gray]U/D[/] scroll, [gray]F[/]/[gray]SHIFT+F[/] filter, [gray]S[/] save, [gray]E[/] export, [gray]SHIFT+E[/] session, [gray]X[/] fixes");

        if (!string.IsNullOrWhiteSpace(snapshot.StatusMessage))
        {
            lines.Add($"[bold gray]Status:[/] {(snapshot.StatusIsError ? "[bold red]" : "[bold green]")}{Markup.Escape(snapshot.StatusMessage)}[/]");
        }

        return string.Join("\n", lines);
    }

    private static string BuildSaveLine(TuiSnapshot snapshot)
    {
        var fileNameDisplay = string.IsNullOrEmpty(snapshot.PendingSaveFileName)
            ? "[dim]Type a file name[/]"
            : $"[yellow]{Markup.Escape(snapshot.PendingSaveFileName)}[/]";

        return $"[bold gray]Save:[/] {fileNameDisplay}[bold cyan]█[/] [dim](Enter save, Esc cancel, Backspace edit)[/]";
    }

    private static string BuildFilterLine(TuiSnapshot snapshot)
    {
        if (snapshot.IsInteractionFilterPromptActive)
        {
            var queryDisplay = string.IsNullOrEmpty(snapshot.PendingInteractionFilterQuery)
                ? "[dim]Type a search or filter query[/]"
                : $"[yellow]{Markup.Escape(snapshot.PendingInteractionFilterQuery)}[/]";

            return $"[bold gray]Filter:[/] {queryDisplay}[bold cyan]█[/] [dim](Enter apply, Esc cancel, Backspace edit)[/]";
        }

        return snapshot.HasActiveFilter
            ? $"[bold gray]Filter:[/] [yellow]{Markup.Escape(snapshot.ActiveFilterSummary)}[/] [dim]({snapshot.FilteredInteractionCount}/{snapshot.TotalInteractionCount} matches, Shift+F clears)[/]"
            : "[bold gray]Filter:[/] [dim]None. Press F to search or filter interactions.[/]";
    }

    private static bool HasForwardedRequestChange(TuiInteractionSummary interaction)
    {
        return interaction.HasAttentionWorthyForwardedRequestMutations ||
               interaction.ForceContinueApplied ||
               interaction.HasAttentionWorthyDiagnostics;
    }

    private static string BuildForwardedRequestDisplay(TuiVisibleInteractionSnapshot visible)
    {
        var mutationCount = visible.ForwardedRequestMutations.Count;
        if (mutationCount == 0 && !visible.ForceContinueApplied)
        {
            return "[dim]unchanged[/]";
        }

        if (!visible.ForceContinueApplied && !ForwardedRequestMutation.HasAttentionWorthyChanges(visible.ForwardedRequestMutations))
        {
            var compactSummary = ForwardedRequestMutation.SummarizeCompact(visible.ForwardedRequestMutations);
            return string.IsNullOrWhiteSpace(compactSummary)
                ? "[dim]normalized[/]"
                : $"[dim]{Markup.Escape(compactSummary)}[/]";
        }

        var effectiveCount = Math.Max(mutationCount, visible.ForceContinueApplied ? 1 : 0);
        return effectiveCount == 1
            ? "[yellow]changed (1)[/]"
            : $"[yellow]changed ({effectiveCount})[/]";
    }

    private static string BuildDiagnosticDisplayPlain(TuiVisibleInteractionSnapshot visible)
    {
        if (visible.Diagnostics.Count == 0)
        {
            return "no";
        }

        var compactSummary = InteractionDiagnostic.SummarizeCompact(visible.Diagnostics);
        return string.IsNullOrWhiteSpace(compactSummary)
            ? visible.Diagnostics.Count == 1 ? "recorded" : $"{visible.Diagnostics.Count} diagnostics"
            : compactSummary;
    }

    private static string BuildDiagnosticDisplay(TuiVisibleInteractionSnapshot visible)
    {
        var plain = BuildDiagnosticDisplayPlain(visible);
        if (plain == "no")
        {
            return "[dim]no[/]";
        }

        return InteractionDiagnostic.HasAttentionWorthyEntries(visible.Diagnostics)
            ? $"[yellow]{Markup.Escape(plain)}[/]"
            : $"[dim]{Markup.Escape(plain)}[/]";
    }

    private static string BuildSessionSummaryText(SessionSummary summary, bool locked)
    {
        var tokens = summary.Tokens;
        var cachedDisplay = tokens.CachedPromptTokens > 0 ? $" [bold dim]({tokens.CachedPromptTokens} cached)[/]" : string.Empty;
        var averageTokensPerSecond = summary.Latency.ActiveSpanSeconds is > 0 && tokens.TotalTokens > 0
            ? tokens.TotalTokens / summary.Latency.ActiveSpanSeconds.Value
            : 0d;

        var summaryText = $"{summary.InteractionCount} req | {tokens.PromptTokens} prefill{cachedDisplay}, {tokens.CompletionTokens} decode, {tokens.TotalTokens} total, {tokens.ReasoningTokens} reasoning";
        if (averageTokensPerSecond > 0)
        {
            summaryText += $" [dim]({averageTokensPerSecond:F1} t/s avg)[/]";
        }

        summaryText += $" | {BuildSessionCostText(summary.Cost)}";

        if (locked)
        {
            summaryText += " [bold red]🔒 LOCKED[/]";
        }

        return summaryText;
    }

    private static string BuildSessionLatencyText(SessionLatencySummary latency)
    {
        var parts = new List<string>();

        if (latency.ActiveSpanSeconds.HasValue)
        {
            parts.Add($"active {latency.ActiveSpanSeconds.Value.ToString("F1", CultureInfo.InvariantCulture)}s");
        }

        if (latency.AverageTimeToFirstTokenSeconds.HasValue)
        {
            parts.Add($"avg TTFT {latency.AverageTimeToFirstTokenSeconds.Value.ToString("F3", CultureInfo.InvariantCulture)}s/{latency.TimeToFirstTokenSampleCount}");
        }

        if (latency.AverageWallClockDurationSeconds.HasValue)
        {
            parts.Add($"avg duration {latency.AverageWallClockDurationSeconds.Value.ToString("F3", CultureInfo.InvariantCulture)}s/{latency.WallClockDurationSampleCount}");
        }

        if (latency.AverageApiTotalDurationSeconds.HasValue)
        {
            parts.Add($"avg API total {latency.AverageApiTotalDurationSeconds.Value.ToString("F3", CultureInfo.InvariantCulture)}s/{latency.ApiTotalDurationSampleCount}");
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : "[dim]n/a[/]";
    }

    private static string BuildSessionCostText(SessionCostSummary cost)
    {
        if (!cost.HasPricingConfigured)
        {
            return "[dim]cost n/a[/]";
        }

        if (!cost.EstimatedUsd.HasValue)
        {
            return cost.UnpricedInteractionCount > 0
                ? $"[yellow]cost n/a[/] [dim]({cost.UnpricedInteractionCount} unpriced)[/]"
                : "[dim]cost n/a[/]";
        }

        var formatted = "$" + cost.EstimatedUsd.Value.ToString("F6", CultureInfo.InvariantCulture);
        return cost.IsPartial
            ? $"[yellow]partial {formatted}[/] [dim]({cost.PricedInteractionCount} priced)[/]"
            : $"[green]cost {formatted}[/]";
    }
}
