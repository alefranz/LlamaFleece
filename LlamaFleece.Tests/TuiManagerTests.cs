using Xunit;
using Spectre.Console;
using System.Text.Json;
using System.Text.Json.Nodes;

[Collection("TuiManager serial")]
public class TuiManagerTests
{
    [Fact]
    public void HandleKeyForTests_LogModePreservesEntriesAcrossOpenAndClose()
    {
        TuiManager.ResetForTests();
        TuiManager.AppendLog("first request");

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('l', ConsoleKey.L, false, false, false));
        var opened = TuiManager.GetLogSnapshotForTests();

        Assert.True(opened.LogMode);
        Assert.Single(opened.Entries);
        Assert.Contains("first request", opened.Entries[0]);

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));
        var closed = TuiManager.GetLogSnapshotForTests();

        Assert.False(closed.LogMode);
        Assert.Single(closed.Entries);
        Assert.Contains("first request", closed.Entries[0]);
    }

    [Fact]
    public void HandleKeyForTests_LogModeConsumesScrollKeysWithoutCreatingInteractions()
    {
        TuiManager.ResetForTests();
        TuiManager.AppendLog("first request");
        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('l', ConsoleKey.L, false, false, false));

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        var afterUp = TuiManager.GetLogSnapshotForTests();

        Assert.True(afterUp.LogMode);
        Assert.Equal(1, afterUp.LogScroll);
        Assert.Equal(0, TuiManager.InteractionCountForTests());

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        var afterDown = TuiManager.GetLogSnapshotForTests();

        Assert.Equal(0, afterDown.LogScroll);
        Assert.Equal(0, TuiManager.InteractionCountForTests());
    }

    [Fact]
    public void HandleKeyForTests_RawModeCanToggleOffAfterPaneSwitch()
    {
        TuiManager.ResetForTests();

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));
        Assert.True(TuiManager.RawMode);

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));
        Assert.Equal(1, TuiManager.ActivePane);

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));
        Assert.False(TuiManager.RawMode);
        Assert.Equal(1, TuiManager.ActivePane);
    }

    [Fact]
    public void HandleKeyForTests_QRequestsCoordinatedShutdownWithoutCreatingInteraction()
    {
        TuiManager.ResetForTests();
        var shutdownCalls = 0;
        ApplicationShutdownCoordinator.Configure(_ => shutdownCalls++);

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false));

        Assert.Equal(1, shutdownCalls);
        Assert.True(ApplicationShutdownCoordinator.IsShutdownRequested);
        Assert.Equal(0, TuiManager.InteractionCountForTests());
    }

    [Fact]
    public void AppendInputMessage_ProducesValidSpectreMarkup()
    {
        TuiManager.ResetForTests();
        TuiManager.AppendInput("[bold magenta]>[/] [bold white]New POST Request[/] to [cyan]/v1/chat/completions[/]");
        TuiManager.AppendInputMessage("yellow", "system", "You are a helpful assistant. Please think step by step.");
        TuiManager.AppendInputMessage("green", "user", "Write a short 3-sentence story about a brave compiler.");

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();

        Assert.NotNull(interaction);
        var inputMarkup = string.Join(Environment.NewLine, interaction!.InputLines);
        var exception = Record.Exception(() => _ = new Markup(inputMarkup));
        Assert.Null(exception);
    }

    [Fact]
    public void HandleKeyForTests_ShiftEExportsSessionAndUpdatesStatus()
    {
        using var exportDirectory = new TestExportDirectory();

        TuiManager.ResetForTests();
        TuiManager.SetExportServiceForTests(new InteractionExportService(exportDirectory.Path));

        TuiManager.AppendInputMessage("green", "user", "First exportable interaction");
        TuiManager.NewSession();
        TuiManager.AppendInputMessage("green", "user", "Second exportable interaction");

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('E', ConsoleKey.E, true, false, false));

        var status = TuiManager.GetStatusSnapshotForTests();
        Assert.False(status.IsError);
        Assert.Contains("Exported session", status.Message, StringComparison.Ordinal);

        var logSnapshot = TuiManager.GetLogSnapshotForTests();
        Assert.Contains(logSnapshot.Entries, entry => entry.Contains("Exported session", StringComparison.Ordinal));

        var sessionDirectory = Path.Combine(exportDirectory.Path, "sessions");
        var jsonFiles = Directory.GetFiles(sessionDirectory, "*.json");
        var markdownFiles = Directory.GetFiles(sessionDirectory, "*.md");

        Assert.Single(jsonFiles);
        Assert.Single(markdownFiles);

        var json = JsonNode.Parse(File.ReadAllText(jsonFiles[0]))!.AsObject();
        Assert.Equal(2, json["interactionCount"]!.GetValue<int>());
        Assert.Equal(1, json["visibleInteractionId"]!.GetValue<int>());
    }

    [Fact]
    public void HandleKeyForTests_PStartsReplayService()
    {
        TuiManager.ResetForTests();
        var replayCalls = 0;
        TuiManager.SetReplayServiceForTests(new StubReplayService(() => replayCalls++));

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('p', ConsoleKey.P, false, false, false));

        Assert.Equal(1, replayCalls);
    }

    [Fact]
    public void HandleKeyForTests_FAppliesFilterAndShiftFClearsIt()
    {
        TuiManager.ResetForTests();
        TuiManager.CurrentModel = "gpt-a";
        TuiManager.SetLatestRequestEnvelope(new InteractionRequestEnvelope
        {
            Method = "POST",
            Path = "/v1/chat/completions",
            ContentType = "application/json"
        });
        TuiManager.SetLatestResponseStatusCode(200);
        TuiManager.SetLatestFinishReason("stop");

        TuiManager.NewSession();
        TuiManager.CurrentModel = "gpt-b";
        TuiManager.SetLatestRequestEnvelope(new InteractionRequestEnvelope
        {
            Method = "POST",
            Path = "/v1/responses",
            ContentType = "application/json"
        });
        TuiManager.SetLatestResponseStatusCode(500);
        TuiManager.SetLatestFinishReason("failed");

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('f', ConsoleKey.F, false, false, false));
        var promptStartedStatus = TuiManager.GetStatusSnapshotForTests();
        Assert.Contains("Editing interaction filter", promptStartedStatus.Message, StringComparison.Ordinal);

        SendFilterPromptText("status=200");
        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        var filteredVisible = TuiManager.GetVisibleInteractionSnapshotForTests();
        var filteredStatus = TuiManager.GetStatusSnapshotForTests();

        Assert.NotNull(filteredVisible);
        Assert.Equal(200, filteredVisible!.ResponseStatusCode);
        Assert.Contains("Applied interaction filter", filteredStatus.Message, StringComparison.Ordinal);

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('F', ConsoleKey.F, true, false, false));

        var clearedVisible = TuiManager.GetVisibleInteractionSnapshotForTests();
        var clearedStatus = TuiManager.GetStatusSnapshotForTests();

        Assert.NotNull(clearedVisible);
        Assert.Equal(500, clearedVisible!.ResponseStatusCode);
        Assert.Contains("Cleared interaction filter", clearedStatus.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleKeyForTests_SStartsNamedSavePromptAndWritesRawOutputArtifact()
    {
        using var exportDirectory = new TestExportDirectory();

        TuiManager.ResetForTests();
        TuiManager.SetExportServiceForTests(new InteractionExportService(exportDirectory.Path));
        TuiManager.AppendInputMessage("green", "user", "Inspect raw output");
        TuiManager.AppendRawOutput("data: {\"delta\":\"chunk\"}\n");
        TuiManager.ActivePane = 2;
        TuiManager.RawMode = true;

        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('s', ConsoleKey.S, false, false, false));

        var promptSnapshot = TuiManager.TakeSnapshotForTests();
        Assert.True(promptSnapshot.IsNamedSavePromptActive);
        Assert.Contains("Editing save file name", promptSnapshot.StatusMessage, StringComparison.Ordinal);

        SendSavePromptText("raw output capture");
        TuiManager.HandleKeyForTests(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        var snapshot = TuiManager.TakeSnapshotForTests();
        var status = TuiManager.GetStatusSnapshotForTests();

        Assert.False(snapshot.IsNamedSavePromptActive);
        Assert.Contains("Saved raw response", status.Message, StringComparison.Ordinal);

        var savedFile = Path.Combine(exportDirectory.Path, "saved", "output", "raw-output-capture.sse");
        Assert.True(File.Exists(savedFile));
        Assert.Equal("data: {\"delta\":\"chunk\"}\n", File.ReadAllText(savedFile));
    }

    [Fact]
    public void ProcessPendingKeysForTests_MatchesSharedKeyboardControllerStatePath()
    {
        TuiManager.ResetForTests();
        SeedFilterableInteractionsInManager();
        TuiManager.AppendLog("first request");

        var controller = new TuiKeyboardController();
        var state = new TuiState();
        SeedFilterableInteractions(state);
        state.AppendLog("first request");

        var keys = BuildSharedKeySequence();

        TuiManager.ProcessPendingKeysForTests(keys, keys.Length);
        foreach (var key in keys)
        {
            controller.HandleKeyForTests(key, state);
        }

        AssertEquivalentSnapshots(state.TakeSnapshot(), TuiManager.TakeSnapshotForTests());

        var expectedLog = state.GetLogSnapshot();
        var actualLog = TuiManager.GetLogSnapshotForTests();
        Assert.Equal(expectedLog.LogMode, actualLog.LogMode);
        Assert.Equal(expectedLog.LogScroll, actualLog.LogScroll);
        AssertEquivalentLogEntries(expectedLog.Entries, actualLog.Entries);

        var expectedStatus = state.GetStatusSnapshot();
        var actualStatus = TuiManager.GetStatusSnapshotForTests();
        Assert.Equal(expectedStatus.Message, actualStatus.Message);
        Assert.Equal(expectedStatus.IsError, actualStatus.IsError);
    }

    [Fact]
    public void RestorePersistedSession_RestoresHistoryAndFlushesNewChanges()
    {
        using var exportDirectory = new TestExportDirectory();
        var persistenceService = new InteractionPersistenceService(Path.Combine(exportDirectory.Path, "state", "session-history.json"), TimeSpan.Zero);

        var first = new Interaction { Id = 4, Model = "gpt-a", PromptTokens = 3, CompletionTokens = 2, TotalTokens = 5 };
        var second = new Interaction { Id = 9, Model = "gpt-b", PromptTokens = 5, CompletionTokens = 7, TotalTokens = 12 };
        var summary = new SessionSummaryService().BuildSummary(new[] { first, second }, DateTime.MinValue, DateTime.MinValue);
        var snapshot = InteractionExportService.SnapshotSession(
            new[] { first, second },
            visibleInteractionIndex: 1,
            logEntries: new[] { "[12:00:00.000] restored log" },
            activeFixes: new[] { "force_continue" },
            summary: summary);

        persistenceService.SaveSession(snapshot, force: true);

        TuiManager.ResetForTests();
        TuiManager.SetPersistenceServiceForTests(persistenceService);

        TuiManager.RestorePersistedSession();

        Assert.Equal(2, TuiManager.InteractionCountForTests());

        var visible = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(visible);
        Assert.Equal(9, visible!.Id);
        Assert.Equal("gpt-b", visible.Model);

        var status = TuiManager.GetStatusSnapshotForTests();
        Assert.False(status.IsError);
        Assert.Contains("Restored 2 persisted interactions", status.Message, StringComparison.Ordinal);

        var logSnapshot = TuiManager.GetLogSnapshotForTests();
        Assert.Contains(logSnapshot.Entries, entry => entry.Contains("Restored 2 persisted interactions", StringComparison.Ordinal));

        TuiManager.NewSession();
        var afterNewSession = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(afterNewSession);
        Assert.Equal(10, afterNewSession!.Id);

        TuiManager.FlushPersistedSession();
        var reloaded = persistenceService.LoadSession();
        Assert.True(reloaded.Found);
        Assert.Equal(3, reloaded.Session!.Interactions.Count);
    }

    [Fact]
    public void RestorePersistedSession_InvalidPersistedJsonThrows()
    {
        using var exportDirectory = new TestExportDirectory();
        var persistencePath = Path.Combine(exportDirectory.Path, "state", "session-history.json");
        Directory.CreateDirectory(Path.GetDirectoryName(persistencePath)!);
        File.WriteAllText(persistencePath, "{ invalid json");

        TuiManager.ResetForTests();
        TuiManager.SetPersistenceServiceForTests(new InteractionPersistenceService(persistencePath, TimeSpan.Zero));

        Assert.Throws<JsonException>(() => TuiManager.RestorePersistedSession());

        var status = TuiManager.GetStatusSnapshotForTests();
        Assert.Equal(string.Empty, status.Message);
        Assert.False(status.IsError);
    }

    [Fact]
    public void RestorePersistedSession_EmptyPersistedSessionAllowsContinuingWithNewSession()
    {
        using var exportDirectory = new TestExportDirectory();
        var persistenceService = new InteractionPersistenceService(Path.Combine(exportDirectory.Path, "state", "session-history.json"), TimeSpan.Zero);
        var summary = new SessionSummaryService().BuildSummary(Array.Empty<Interaction>(), DateTime.MinValue, DateTime.MinValue);
        var snapshot = InteractionExportService.SnapshotSession(
            Array.Empty<Interaction>(),
            visibleInteractionIndex: -1,
            logEntries: new[] { "[12:00:00.000] restored empty session" },
            activeFixes: Array.Empty<string>(),
            summary: summary);

        persistenceService.SaveSession(snapshot, force: true);

        TuiManager.ResetForTests();
        TuiManager.SetPersistenceServiceForTests(persistenceService);

        TuiManager.RestorePersistedSession();

        Assert.Equal(0, TuiManager.InteractionCountForTests());

        var status = TuiManager.GetStatusSnapshotForTests();
        Assert.False(status.IsError);
        Assert.Contains("Restored 0 persisted interactions", status.Message, StringComparison.Ordinal);

        TuiManager.AppendInputMessage("green", "user", "continued after empty restore");

        var visible = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(visible);
        Assert.Equal(0, visible!.Id);
        Assert.Contains("continued after empty restore", string.Join(Environment.NewLine, visible.InputLines), StringComparison.Ordinal);

        TuiManager.FlushPersistedSession();

        var reloaded = persistenceService.LoadSession();
        Assert.True(reloaded.Found);
        Assert.Single(reloaded.Session!.Interactions);
        Assert.Equal(1, reloaded.Session.NextInteractionId);
    }

    [Fact]
    public void BeginIsolatedScopeForTests_DoesNotLeakStateToGlobalRuntime()
    {
        TuiManager.ResetForTests();
        TuiManager.AppendLog("global");

        using (TuiManager.BeginIsolatedScopeForTests())
        {
            Assert.Equal(0, TuiManager.InteractionCountForTests());
            var scopedLogBefore = TuiManager.GetLogSnapshotForTests();
            Assert.DoesNotContain(scopedLogBefore.Entries, entry => entry.Contains("global", StringComparison.Ordinal));

            TuiManager.NewSession();
            TuiManager.AppendLog("scoped");

            Assert.Equal(1, TuiManager.InteractionCountForTests());
            var scopedLogAfter = TuiManager.GetLogSnapshotForTests();
            Assert.Contains(scopedLogAfter.Entries, entry => entry.Contains("scoped", StringComparison.Ordinal));
        }

        Assert.Equal(0, TuiManager.InteractionCountForTests());
        var globalLog = TuiManager.GetLogSnapshotForTests();
        Assert.Contains(globalLog.Entries, entry => entry.Contains("global", StringComparison.Ordinal));
        Assert.DoesNotContain(globalLog.Entries, entry => entry.Contains("scoped", StringComparison.Ordinal));
    }

    [Fact]
    public void BeginIsolatedScopeForTests_RestoresOuterScopedStateOnDispose()
    {
        TuiManager.ResetForTests();

        using (TuiManager.BeginIsolatedScopeForTests())
        {
            TuiManager.AppendLog("outer");

            using (TuiManager.BeginIsolatedScopeForTests())
            {
                var innerBefore = TuiManager.GetLogSnapshotForTests();
                Assert.DoesNotContain(innerBefore.Entries, entry => entry.Contains("outer", StringComparison.Ordinal));

                TuiManager.AppendLog("inner");
                var innerAfter = TuiManager.GetLogSnapshotForTests();
                Assert.Contains(innerAfter.Entries, entry => entry.Contains("inner", StringComparison.Ordinal));
            }

            var outerAfter = TuiManager.GetLogSnapshotForTests();
            Assert.Contains(outerAfter.Entries, entry => entry.Contains("outer", StringComparison.Ordinal));
            Assert.DoesNotContain(outerAfter.Entries, entry => entry.Contains("inner", StringComparison.Ordinal));
        }
    }

    private sealed class StubReplayService : IInteractionReplayService
    {
        private readonly Action _onReplay;

        public StubReplayService(Action onReplay)
        {
            _onReplay = onReplay;
        }

        public void StartReplayVisibleInteraction()
        {
            _onReplay();
        }
    }

    private static void SendFilterPromptText(string text)
    {
        foreach (var character in text)
        {
            TuiManager.HandleKeyForTests(new ConsoleKeyInfo(character, ConsoleKey.Spacebar, false, false, false));
        }
    }

    private static void SendSavePromptText(string text)
    {
        foreach (var character in text)
        {
            TuiManager.HandleKeyForTests(new ConsoleKeyInfo(character, ConsoleKey.Spacebar, false, false, false));
        }
    }

    private static ConsoleKeyInfo[] BuildSharedKeySequence()
    {
        var keys = new List<ConsoleKeyInfo>
        {
            new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false),
            new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false),
            new ConsoleKeyInfo('f', ConsoleKey.F, false, false, false)
        };

        foreach (var character in "status=200")
        {
            keys.Add(new ConsoleKeyInfo(character, ConsoleKey.Spacebar, false, false, false));
        }

        keys.Add(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        keys.Add(new ConsoleKeyInfo('l', ConsoleKey.L, false, false, false));
        keys.Add(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        return keys.ToArray();
    }

    private static void SeedFilterableInteractionsInManager()
    {
        TuiManager.CurrentModel = "gpt-a";
        TuiManager.SetLatestRequestEnvelope(new InteractionRequestEnvelope
        {
            Method = "POST",
            Path = "/v1/chat/completions",
            ContentType = "application/json"
        });
        TuiManager.SetLatestResponseStatusCode(200);
        TuiManager.SetLatestFinishReason("stop");

        TuiManager.NewSession();
        TuiManager.CurrentModel = "gpt-b";
        TuiManager.SetLatestRequestEnvelope(new InteractionRequestEnvelope
        {
            Method = "POST",
            Path = "/v1/responses",
            ContentType = "application/json"
        });
        TuiManager.SetLatestResponseStatusCode(500);
        TuiManager.SetLatestFinishReason("failed");
    }

    private static void SeedFilterableInteractions(TuiState state)
    {
        state.SetLatestModel("gpt-a");
        state.SetLatestRequestEnvelope(new InteractionRequestEnvelope
        {
            Method = "POST",
            Path = "/v1/chat/completions",
            ContentType = "application/json"
        });
        state.SetLatestResponseStatusCode(200);
        state.SetLatestFinishReason("stop");

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
    }

    private static void AssertEquivalentSnapshots(TuiSnapshot expected, TuiSnapshot actual)
    {
        Assert.Equal(expected.LogMode, actual.LogMode);
        Assert.Equal(expected.FullscreenMode, actual.FullscreenMode);
        Assert.Equal(expected.RawMode, actual.RawMode);
        Assert.Equal(expected.ActivePane, actual.ActivePane);
        Assert.Equal(expected.Locked, actual.Locked);
        Assert.Equal(expected.VisibleIndex, actual.VisibleIndex);
        Assert.Equal(expected.LogScroll, actual.LogScroll);
        AssertEquivalentLogEntries(expected.LogEntries, actual.LogEntries);
        Assert.Equal(expected.TotalInteractionCount, actual.TotalInteractionCount);
        Assert.Equal(expected.FilteredInteractionCount, actual.FilteredInteractionCount);
        Assert.Equal(expected.HasActiveFilter, actual.HasActiveFilter);
        Assert.Equal(expected.ActiveFilterSummary, actual.ActiveFilterSummary);
        Assert.Equal(expected.IsFixSelectionPromptActive, actual.IsFixSelectionPromptActive);
        Assert.Equal(expected.FixSelectionIndex, actual.FixSelectionIndex);
        Assert.Equal(expected.FixSelectionItems.Count, actual.FixSelectionItems.Count);
        Assert.Equal(expected.StatusMessage, actual.StatusMessage);
        Assert.Equal(expected.StatusIsError, actual.StatusIsError);

        for (var i = 0; i < expected.FixSelectionItems.Count; i++)
        {
            Assert.Equal(expected.FixSelectionItems[i].Key, actual.FixSelectionItems[i].Key);
            Assert.Equal(expected.FixSelectionItems[i].Name, actual.FixSelectionItems[i].Name);
            Assert.Equal(expected.FixSelectionItems[i].Shorthand, actual.FixSelectionItems[i].Shorthand);
            Assert.Equal(expected.FixSelectionItems[i].Enabled, actual.FixSelectionItems[i].Enabled);
        }

        Assert.Equal(expected.Interactions.Count, actual.Interactions.Count);
        for (var i = 0; i < expected.Interactions.Count; i++)
        {
            Assert.Equal(expected.Interactions[i].Id, actual.Interactions[i].Id);
            Assert.Equal(expected.Interactions[i].ForceContinueApplied, actual.Interactions[i].ForceContinueApplied);
            Assert.Equal(expected.Interactions[i].HasForwardedRequestMutations, actual.Interactions[i].HasForwardedRequestMutations);
            Assert.Equal(expected.Interactions[i].HasAttentionWorthyForwardedRequestMutations, actual.Interactions[i].HasAttentionWorthyForwardedRequestMutations);
        }

        Assert.NotNull(expected.VisibleInteraction);
        Assert.NotNull(actual.VisibleInteraction);
        Assert.Equal(expected.VisibleInteraction!.Model, actual.VisibleInteraction!.Model);
        Assert.Equal(expected.VisibleInteraction.RequestTarget, actual.VisibleInteraction.RequestTarget);
        Assert.Equal(expected.VisibleInteraction.ResponseStatusCode, actual.VisibleInteraction.ResponseStatusCode);
        Assert.Equal(expected.VisibleInteraction.FinishReason, actual.VisibleInteraction.FinishReason);
        Assert.Equal(expected.VisibleInteraction.InputScroll, actual.VisibleInteraction.InputScroll);
        Assert.Equal(expected.VisibleInteraction.OutputScroll, actual.VisibleInteraction.OutputScroll);
        Assert.Equal(expected.VisibleInteraction.RawInputText, actual.VisibleInteraction.RawInputText);
        Assert.Equal(expected.VisibleInteraction.RawOutputText, actual.VisibleInteraction.RawOutputText);
    }

    private static void AssertEquivalentLogEntries(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(NormalizeLogEntry(expected[i]), NormalizeLogEntry(actual[i]));
        }
    }

    private static string NormalizeLogEntry(string entry)
    {
        var separatorIndex = entry.IndexOf("] ", StringComparison.Ordinal);
        return separatorIndex >= 0
            ? entry[(separatorIndex + 2)..]
            : entry;
    }
}