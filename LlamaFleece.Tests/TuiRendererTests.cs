using Spectre.Console;
using Xunit;

public class TuiRendererTests
{
    [Fact]
    public void BuildFrame_CanRenderBlankSnapshotMoreThanOnce()
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();

        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 120,
            ConsoleHeight = 40,
            ActivePane = 0
        };

        renderer.BuildFrame(layout, snapshot);
        renderer.BuildFrame(layout, snapshot);
    }

    [Fact]
    public void BuildFrame_CanSwitchBetweenLayoutModes()
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();
        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 120,
            ConsoleHeight = 40,
            ActivePane = 0
        };

        renderer.BuildFrame(layout, snapshot);
        renderer.BuildFrame(layout, snapshot with { LogMode = true });
        renderer.BuildFrame(layout, snapshot with { FullscreenMode = true });
        renderer.BuildFrame(layout, snapshot);
    }

    [Theory]
    [InlineData(0, "Interactions", "Input", "Output")]
    [InlineData(1, "Input", "Interactions", "Output")]
    [InlineData(2, "Output", "Interactions", "Input")]
    public void BuildFrame_FullscreenModeExpandsTheSelectedPane(int activePane, string activePaneName, string hiddenPaneName1, string hiddenPaneName2)
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();
        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 120,
            ConsoleHeight = 40,
            ActivePane = activePane,
            FullscreenMode = true
        };

        renderer.BuildFrame(layout, snapshot);

        Assert.True(layout[activePaneName].IsVisible);
        Assert.Null(layout[activePaneName].Size);
        Assert.False(layout[hiddenPaneName1].IsVisible);
        Assert.False(layout[hiddenPaneName2].IsVisible);
        Assert.True(layout["Stats"].IsVisible);
        Assert.Equal(TuiLayoutMetrics.StatsPanelHeight, layout["Stats"].Size);
    }

    [Fact]
    public void BuildFrame_NormalModeRestoresPaneVisibilityAndSizingAfterFullscreen()
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();
        var fullscreenSnapshot = new TuiSnapshot
        {
            ConsoleWidth = 120,
            ConsoleHeight = 40,
            ActivePane = 1,
            FullscreenMode = true
        };
        var normalSnapshot = fullscreenSnapshot with { FullscreenMode = false };

        renderer.BuildFrame(layout, fullscreenSnapshot);
        renderer.BuildFrame(layout, normalSnapshot);

        Assert.True(layout["Interactions"].IsVisible);
        Assert.Equal(TuiLayoutMetrics.InteractionsPanelHeight, layout["Interactions"].Size);
        Assert.True(layout["Input"].IsVisible);
        Assert.Equal(TuiLayoutMetrics.InputPanelHeight, layout["Input"].Size);
        Assert.True(layout["Output"].IsVisible);
        Assert.Null(layout["Output"].Size);
        Assert.True(layout["Stats"].IsVisible);
        Assert.Equal(TuiLayoutMetrics.GetStatsPanelHeight(logMode: false, fullscreenMode: false, fixSelectionPromptActive: false, normalSnapshot.ConsoleWidth), layout["Stats"].Size);
    }

    [Theory]
    [InlineData(false, false, 0, "Controls:", "TAB pane", "Q quit")]
    [InlineData(false, true, 0, "Controls:", "ESC/ENTER", "switch panel")]
    [InlineData(false, true, 1, "Controls:", "ESC/ENTER", "U/D scroll")]
    [InlineData(false, true, 2, "Controls:", "ESC/ENTER", "U/D scroll")]
    [InlineData(true, false, 0, "Controls:", "L/ESC", "U/D scroll")]
    public void BuildFrame_RenderedOutputKeepsControlsVisibleInEveryMode(bool logMode, bool fullscreenMode, int activePane, string expected1, string expected2, string expected3)
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();
        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 80,
            ConsoleHeight = 25,
            ActivePane = activePane,
            LogMode = logMode,
            FullscreenMode = fullscreenMode,
            LogEntries = logMode ? new List<string> { "entry 1" } : new List<string>()
        };

        renderer.BuildFrame(layout, snapshot);

        var rendered = RenderToText(layout, snapshot.ConsoleWidth, snapshot.ConsoleHeight);

        Assert.Contains(expected1, rendered, StringComparison.Ordinal);
        Assert.Contains(expected2, rendered, StringComparison.Ordinal);
        Assert.Contains(expected3, rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFrame_CanRenderInlineFilterPromptInNormalAndFullscreenModes()
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();
        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 120,
            ConsoleHeight = 40,
            ActivePane = 0,
            IsInteractionFilterPromptActive = true,
            PendingInteractionFilterQuery = "status=200",
            StatusMessage = "Editing interaction filter. Type a query and press Enter to apply, or Esc to cancel."
        };

        renderer.BuildFrame(layout, snapshot);
        renderer.BuildFrame(layout, snapshot with { FullscreenMode = true });
    }

    [Fact]
    public void BuildFrame_CanRenderInlineNamedSavePromptInNormalAndFullscreenModes()
    {
        var renderer = new TuiRenderer();
        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 120,
            ConsoleHeight = 40,
            ActivePane = 0,
            IsNamedSavePromptActive = true,
            PendingSaveFileName = "visible-save",
            StatusMessage = "Editing save file name. Invalid filename characters will be replaced with '-'. Press Enter to save, or Esc to cancel."
        };

        var normalLayout = CreateLayout();
        renderer.BuildFrame(normalLayout, snapshot);
        var normalRendered = RenderToText(normalLayout, snapshot.ConsoleWidth, snapshot.ConsoleHeight);
        Assert.Contains("Save:", normalRendered, StringComparison.Ordinal);
        Assert.Contains("visible-save", normalRendered, StringComparison.Ordinal);

        var fullscreenLayout = CreateLayout();
        renderer.BuildFrame(fullscreenLayout, snapshot with { FullscreenMode = true });
        var fullscreenRendered = RenderToText(fullscreenLayout, snapshot.ConsoleWidth, snapshot.ConsoleHeight);
        Assert.Contains("Save:", fullscreenRendered, StringComparison.Ordinal);
        Assert.Contains("Enter save", fullscreenRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFrame_RendersUpdatedShortcutMapForNormalMode()
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();
        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 120,
            ConsoleHeight = 40,
            ActivePane = 0
        };

        renderer.BuildFrame(layout, snapshot);

        var rendered = RenderToText(layout, snapshot.ConsoleWidth, snapshot.ConsoleHeight);

        Assert.Contains("F/SHIFT+F filter", rendered, StringComparison.Ordinal);
        Assert.Contains("S save", rendered, StringComparison.Ordinal);
        Assert.Contains("X fixes", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFrame_RendersUpdatedFilterClearCopy()
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();
        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 120,
            ConsoleHeight = 40,
            ActivePane = 0,
            HasActiveFilter = true,
            ActiveFilterSummary = "status=200",
            FilteredInteractionCount = 1,
            TotalInteractionCount = 2
        };

        renderer.BuildFrame(layout, snapshot);

        var rendered = RenderToText(layout, snapshot.ConsoleWidth, snapshot.ConsoleHeight);

        Assert.Contains("Shift+F clears", rendered, StringComparison.Ordinal);
        Assert.Contains("Press Shift+F to clear", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFrame_WideStatsPanelKeepsFullMetadataVisible()
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();
        const string model = "gpt-4.1-mini-long-visible-name";
        const string endpoint = "/v1/chat/completions/stream";
        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 200,
            ConsoleHeight = 40,
            ActivePane = 0,
            VisibleInteraction = new TuiVisibleInteractionSnapshot
            {
                Model = model,
                RequestTarget = endpoint,
                ResponseStatusCode = 200,
                FinishReason = "stop"
            }
        };

        renderer.BuildFrame(layout, snapshot);

        var rendered = RenderToText(layout, snapshot.ConsoleWidth, snapshot.ConsoleHeight);

        Assert.Equal(7, layout["Stats"].Size);
        Assert.Contains(model, rendered, StringComparison.Ordinal);
        Assert.Contains(endpoint, rendered, StringComparison.Ordinal);
        Assert.Contains("Fwd: unchanged", rendered, StringComparison.Ordinal);
        Assert.Contains("TAB pane | L/R select | U/D scroll | PGUP/DN sec | SPC lock", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFrame_NormalModeRestoresDetailedStatsWithLocalFallback()
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();
        var startTime = new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc);
        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 200,
            ConsoleHeight = 40,
            ActivePane = 0,
            SessionSummary = new SessionSummary
            {
                InteractionCount = 4,
                Tokens = new SessionTokenSummary
                {
                    PromptTokens = 1200,
                    CompletionTokens = 600,
                    TotalTokens = 1800,
                    CachedPromptTokens = 100,
                    ReasoningTokens = 40
                },
                Latency = new SessionLatencySummary
                {
                    ActiveSpanSeconds = 12,
                    AverageTimeToFirstTokenSeconds = 0.25,
                    TimeToFirstTokenSampleCount = 4
                }
            },
            VisibleInteraction = new TuiVisibleInteractionSnapshot
            {
                Model = "gpt-4.1-mini",
                RequestTarget = "/v1/chat/completions",
                ResponseStatusCode = 200,
                FinishReason = "stop",
                PromptTokens = 400,
                CompletionTokens = 200,
                TotalTokens = 600,
                StartTime = startTime,
                FirstTokenTime = startTime.AddSeconds(2),
                EndTime = startTime.AddSeconds(6)
            }
        };

        renderer.BuildFrame(layout, snapshot);

        var rendered = RenderToText(layout, snapshot.ConsoleWidth, snapshot.ConsoleHeight);

        Assert.Contains("400 prefill (200.0 t/s)", rendered, StringComparison.Ordinal);
        Assert.Contains("200 decode (50.0 t/s)", rendered, StringComparison.Ordinal);
        Assert.Contains("(local)", rendered, StringComparison.Ordinal);
        Assert.Contains("4 req | 1200 prefill", rendered, StringComparison.Ordinal);
        Assert.Contains("1800 total, 40 reasoning", rendered, StringComparison.Ordinal);
        Assert.Contains("active 12.0s", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFrame_FullscreenModeShowsApiBackedDetailedStats()
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();
        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 200,
            ConsoleHeight = 40,
            ActivePane = 1,
            FullscreenMode = true,
            SessionSummary = new SessionSummary
            {
                InteractionCount = 8,
                Tokens = new SessionTokenSummary
                {
                    PromptTokens = 2400,
                    CompletionTokens = 1600,
                    TotalTokens = 4000,
                    ReasoningTokens = 50
                },
                Latency = new SessionLatencySummary
                {
                    ActiveSpanSeconds = 20,
                    AverageApiTotalDurationSeconds = 1.5,
                    ApiTotalDurationSampleCount = 8
                }
            },
            VisibleInteraction = new TuiVisibleInteractionSnapshot
            {
                Model = "gpt-4.1-mini",
                RequestTarget = "/v1/responses",
                ResponseStatusCode = 200,
                FinishReason = "stop",
                PromptTokens = 512,
                CompletionTokens = 256,
                TotalTokens = 768,
                HasApiMetrics = true,
                ApiPrefillSpeed = 256.4,
                ApiDecodeSpeed = 128.2,
                ApiLoadDuration = 100_000_000,
                ApiPromptEvalDuration = 2_000_000_000,
                ApiEvalDuration = 1_000_000_000,
                ApiTotalDuration = 3_100_000_000
            }
        };

        renderer.BuildFrame(layout, snapshot);

        var rendered = RenderToText(layout, snapshot.ConsoleWidth, snapshot.ConsoleHeight);

        Assert.Contains("512 prefill (256.4 t/s)", rendered, StringComparison.Ordinal);
        Assert.Contains("256 decode (128.2 t/s)", rendered, StringComparison.Ordinal);
        Assert.Contains("(API)", rendered, StringComparison.Ordinal);
        Assert.Contains("8 req | 2400 prefill", rendered, StringComparison.Ordinal);
        Assert.Contains("avg API total 1.500s/8", rendered, StringComparison.Ordinal);
        Assert.Contains("prefill: 2.000s", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFrame_RendersInlineFixEditorInNormalAndFullscreenModes()
    {
        var renderer = new TuiRenderer();
        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 120,
            ConsoleHeight = 40,
            ActivePane = 0,
            IsFixSelectionPromptActive = true,
            FixSelectionIndex = 0,
            FixSelectionItems = new List<TuiFixSelectionItem>
            {
                new()
                {
                    Key = "force_continue",
                    Name = "Force Continue on Empty Response",
                    Shorthand = "FC",
                    Enabled = true
                }
            }
        };

        var normalLayout = CreateLayout();
        renderer.BuildFrame(normalLayout, snapshot);
        var normalRendered = RenderToText(normalLayout, snapshot.ConsoleWidth, snapshot.ConsoleHeight);
        Assert.Contains("Force Continue on Empty Response", normalRendered, StringComparison.Ordinal);
        Assert.Contains("SPC toggle", normalRendered, StringComparison.Ordinal);
        Assert.Contains("ENTER apply", normalRendered, StringComparison.Ordinal);

        var fullscreenLayout = CreateLayout();
        renderer.BuildFrame(fullscreenLayout, snapshot with { FullscreenMode = true });
        var fullscreenRendered = RenderToText(fullscreenLayout, snapshot.ConsoleWidth, snapshot.ConsoleHeight);
        Assert.Contains("Force Continue on Empty Response", fullscreenRendered, StringComparison.Ordinal);
        Assert.Contains("ESC cancel", fullscreenRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFrame_LiveLayoutPromptTransitionsDoNotLeaveStaleModalContent()
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();
        var baseSnapshot = new TuiSnapshot
        {
            ConsoleWidth = 120,
            ConsoleHeight = 40,
            ActivePane = 0
        };

        var filterSnapshot = baseSnapshot with
        {
            IsInteractionFilterPromptActive = true,
            PendingInteractionFilterQuery = "status=200",
            StatusMessage = "Editing interaction filter. Type a query and press Enter to apply, or Esc to cancel."
        };
        var saveFullscreenSnapshot = baseSnapshot with
        {
            ActivePane = 1,
            FullscreenMode = true,
            IsNamedSavePromptActive = true,
            PendingSaveFileName = "capture",
            StatusMessage = "Editing save file name. Invalid filename characters will be replaced with '-'. Press Enter to save, or Esc to cancel."
        };
        var fixesSnapshot = baseSnapshot with
        {
            IsFixSelectionPromptActive = true,
            FixSelectionIndex = 0,
            FixSelectionItems = new List<TuiFixSelectionItem>
            {
                new()
                {
                    Key = "force_continue",
                    Name = "Force Continue on Empty Response",
                    Shorthand = "FC",
                    Enabled = true
                }
            },
            StatusMessage = "Editing fixes. Up/Down select, Space toggle, Enter apply, or Esc cancel."
        };
        var logSnapshot = baseSnapshot with
        {
            LogMode = true,
            LogEntries = new List<string>
            {
                "[10:31:24.534] live update"
            }
        };

        renderer.BuildFrame(layout, filterSnapshot);
        var filterRendered = RenderToText(layout, filterSnapshot.ConsoleWidth, filterSnapshot.ConsoleHeight);
        Assert.Contains("Filter:", filterRendered, StringComparison.Ordinal);
        Assert.Contains("status=200", filterRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("capture", filterRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Force Continue on Empty Response", filterRendered, StringComparison.Ordinal);

        renderer.BuildFrame(layout, saveFullscreenSnapshot);
        var saveRendered = RenderToText(layout, saveFullscreenSnapshot.ConsoleWidth, saveFullscreenSnapshot.ConsoleHeight);
        Assert.Contains("Save:", saveRendered, StringComparison.Ordinal);
        Assert.Contains("capture", saveRendered, StringComparison.Ordinal);
        Assert.Contains("ESC/ENTER", saveRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("status=200", saveRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Force Continue on Empty Response", saveRendered, StringComparison.Ordinal);

        renderer.BuildFrame(layout, fixesSnapshot);
        var fixesRendered = RenderToText(layout, fixesSnapshot.ConsoleWidth, fixesSnapshot.ConsoleHeight);
        Assert.Contains("Force Continue on Empty Response", fixesRendered, StringComparison.Ordinal);
        Assert.Contains("SPC toggle", fixesRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("capture", fixesRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("status=200", fixesRendered, StringComparison.Ordinal);

        renderer.BuildFrame(layout, logSnapshot);
        var logRendered = RenderToText(layout, logSnapshot.ConsoleWidth, logSnapshot.ConsoleHeight);
        Assert.Contains("live update", logRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Force Continue on Empty Response", logRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("capture", logRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("status=200", logRendered, StringComparison.Ordinal);

        renderer.BuildFrame(layout, baseSnapshot);
        var restoredRendered = RenderToText(layout, baseSnapshot.ConsoleWidth, baseSnapshot.ConsoleHeight);
        Assert.True(layout["Interactions"].IsVisible);
        Assert.True(layout["Input"].IsVisible);
        Assert.True(layout["Output"].IsVisible);
        Assert.True(layout["Stats"].IsVisible);
        Assert.Contains("TAB pane", restoredRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Force Continue on Empty Response", restoredRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("capture", restoredRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("status=200", restoredRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFrame_LogModeRendersPlainTextTimestampedEntries()
    {
        var renderer = new TuiRenderer();
        var layout = CreateLayout();
        var snapshot = new TuiSnapshot
        {
            ConsoleWidth = 120,
            ConsoleHeight = 40,
            ActivePane = 0,
            LogMode = true,
            LogEntries = new List<string>
            {
                "[10:31:24.534] LlamaFleece Proxy started on port 5000.",
                "[10:31:24.552] Proxying to http://localhost:8123 [source=abc]."
            }
        };

        renderer.BuildFrame(layout, snapshot);

        var rendered = RenderToText(layout, snapshot.ConsoleWidth, snapshot.ConsoleHeight);

        Assert.Contains("[10:31:24.534] LlamaFleece Proxy started on port 5000.", rendered, StringComparison.Ordinal);
        Assert.Contains("[10:31:24.552] Proxying to http://localhost:8123 [source=abc].", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, "[gray]L/ESC[/]", "[gray]Q[/] quit")]
    [InlineData(false, true, "[gray]ESC/ENTER[/]", "[gray]L[/] log")]
    [InlineData(false, false, "[gray]TAB[/]", "[gray]P[/] replay")]
    public void BuildErrorControlsLine_MatchesActiveMode(bool logMode, bool fullscreenMode, string expected1, string expected2)
    {
        var snapshot = new TuiSnapshot
        {
            LogMode = logMode,
            FullscreenMode = fullscreenMode
        };

        var controlsLine = TuiRenderer.BuildErrorControlsLine(snapshot);

        Assert.Contains(expected1, controlsLine, StringComparison.Ordinal);
        Assert.Contains(expected2, controlsLine, StringComparison.Ordinal);
    }

    private static Layout CreateLayout()
    {
        return new Layout("Root")
            .SplitRows(
                new Layout("Interactions").Size(TuiLayoutMetrics.InteractionsPanelHeight),
                new Layout("Input").Size(TuiLayoutMetrics.InputPanelHeight),
                new Layout("Output"),
                new Layout("Stats").Size(TuiLayoutMetrics.StatsPanelHeight));
    }

    private static string RenderToText(Layout layout, int width, int height)
    {
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No
        });

        console.Profile.Width = width;
        console.Profile.Height = height;
        console.Write(layout);

        return writer.ToString();
    }
}