using Xunit;

[Collection("TuiManager serial")]
public class TuiKeyboardControllerTests
{
    [Fact]
    public void HandleKeyForTests_QRequestsCoordinatedShutdown()
    {
        ApplicationShutdownCoordinator.ResetForTests();

        try
        {
            var shutdownCalls = 0;
            string? shutdownReason = null;
            var controller = new TuiKeyboardController();
            var state = new TuiState();

            ApplicationShutdownCoordinator.Configure(reason =>
            {
                shutdownCalls++;
                shutdownReason = reason;
            });

            controller.HandleKeyForTests(new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false), state);

            Assert.Equal(1, shutdownCalls);
            Assert.True(ApplicationShutdownCoordinator.IsShutdownRequested);
            Assert.Equal("TUI quit key pressed.", shutdownReason);
            Assert.Equal("TUI quit key pressed.", ApplicationShutdownCoordinator.ShutdownReason);
            Assert.False(state.LogMode);
        }
        finally
        {
            ApplicationShutdownCoordinator.ResetForTests();
        }
    }

    [Fact]
    public void HandleKeyForTests_EExportsVisibleInteractionAndUpdatesStatus()
    {
        using var exportDirectory = new TestExportDirectory();

        var controller = new TuiKeyboardController();
        var state = new TuiState(new InteractionExportService(exportDirectory.Path));

        state.AppendInputMessage("green", "user", "Hello export");
        state.AppendOutputRaw("Hi there.");

        controller.HandleKeyForTests(new ConsoleKeyInfo('e', ConsoleKey.E, false, false, false), state);

        var snapshot = state.TakeSnapshot();
        Assert.False(snapshot.StatusIsError);
        Assert.Contains("Exported interaction 0", snapshot.StatusMessage, StringComparison.Ordinal);

        state.ToggleLogMode();
        var logSnapshot = state.TakeSnapshot();
        Assert.Contains(logSnapshot.LogEntries, entry => entry.Contains("Exported interaction 0", StringComparison.Ordinal));

        var interactionDirectory = Path.Combine(exportDirectory.Path, "interactions");
        Assert.Contains(Directory.GetFiles(interactionDirectory), file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Directory.GetFiles(interactionDirectory), file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Directory.GetFiles(interactionDirectory), file => file.EndsWith(".request.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Directory.GetFiles(interactionDirectory), file => file.EndsWith(".response.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HandleKeyForTests_ShiftEExportsSessionAndWritesArtifacts()
    {
        using var exportDirectory = new TestExportDirectory();

        var controller = new TuiKeyboardController();
        var state = new TuiState(new InteractionExportService(exportDirectory.Path));

        state.AppendInputMessage("green", "user", "First exportable interaction");
        state.NewSession();
        state.AppendInputMessage("green", "user", "Second exportable interaction");

        controller.HandleKeyForTests(new ConsoleKeyInfo('E', ConsoleKey.E, true, false, false), state);

        var snapshot = state.TakeSnapshot();
        Assert.False(snapshot.StatusIsError);
        Assert.Contains("Exported session", snapshot.StatusMessage, StringComparison.Ordinal);

        var logSnapshot = state.GetLogSnapshot();
        Assert.Contains(logSnapshot.Entries, entry => entry.Contains("Exported session", StringComparison.Ordinal));

        var sessionDirectory = Path.Combine(exportDirectory.Path, "sessions");
        Assert.Contains(Directory.GetFiles(sessionDirectory), file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Directory.GetFiles(sessionDirectory), file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HandleKeyForTests_PStartsReplayService()
    {
        var replayCalls = 0;
        var controller = new TuiKeyboardController();
        var state = new TuiState(interactionReplayService: new StubReplayService(() => replayCalls++));

        controller.HandleKeyForTests(new ConsoleKeyInfo('p', ConsoleKey.P, false, false, false), state);

        Assert.Equal(1, replayCalls);
    }

    [Fact]
    public void HandleKeyForTests_FStartsInlineFilterPromptAndAppliesFilter()
    {
        var controller = new TuiKeyboardController();
        var state = new TuiState();

        SeedFilterableInteractions(state);

        controller.HandleKeyForTests(new ConsoleKeyInfo('f', ConsoleKey.F, false, false, false), state);

        var promptStartedSnapshot = state.TakeSnapshot();
        Assert.Contains("Editing interaction filter", promptStartedSnapshot.StatusMessage, StringComparison.Ordinal);

        SendFilterPromptText(controller, state, "status=200");
        controller.HandleKeyForTests(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), state);

        var filteredSnapshot = state.TakeSnapshot();
        Assert.True(filteredSnapshot.HasActiveFilter);
        Assert.Equal(1, filteredSnapshot.FilteredInteractionCount);
        Assert.NotNull(filteredSnapshot.VisibleInteraction);
        Assert.Equal(200, filteredSnapshot.VisibleInteraction!.ResponseStatusCode);
        Assert.Contains("Applied interaction filter", filteredSnapshot.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleKeyForTests_ShiftFClearsActiveFilterWithoutOpeningPrompt()
    {
        var controller = new TuiKeyboardController();
        var state = new TuiState();

        SeedFilterableInteractions(state);
        Assert.True(state.ApplyInteractionFilterQuery("status=200"));

        controller.HandleKeyForTests(new ConsoleKeyInfo('F', ConsoleKey.F, true, false, false), state);

        var snapshot = state.TakeSnapshot();
        Assert.False(snapshot.HasActiveFilter);
        Assert.False(snapshot.IsInteractionFilterPromptActive);
        Assert.Equal(2, snapshot.FilteredInteractionCount);
        Assert.Contains("Cleared interaction filter", snapshot.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleKeyForTests_XStartsInlineFixEditorAndAppliesChangesOnEnter()
    {
        var controller = new TuiKeyboardController();
        var state = new TuiState();

        controller.HandleKeyForTests(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false), state);

        var editingSnapshot = state.TakeSnapshot();
        Assert.True(editingSnapshot.IsFixSelectionPromptActive);
        Assert.Equal(0, editingSnapshot.FixSelectionIndex);
        Assert.Single(editingSnapshot.FixSelectionItems);
        Assert.True(editingSnapshot.FixSelectionItems[0].Enabled);
        Assert.True(state.ActiveFixes["force_continue"].Enabled);
        Assert.Contains("Editing fixes", editingSnapshot.StatusMessage, StringComparison.Ordinal);

        controller.HandleKeyForTests(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false), state);

        var pendingSnapshot = state.TakeSnapshot();
        Assert.True(pendingSnapshot.IsFixSelectionPromptActive);
        Assert.False(pendingSnapshot.FixSelectionItems[0].Enabled);
        Assert.Contains("FC", pendingSnapshot.ActiveFixesShorthand, StringComparison.Ordinal);
        Assert.True(state.ActiveFixes["force_continue"].Enabled);

        controller.HandleKeyForTests(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), state);

        var appliedSnapshot = state.TakeSnapshot();
        Assert.False(appliedSnapshot.IsFixSelectionPromptActive);
        Assert.Empty(appliedSnapshot.ActiveFixesShorthand);
        Assert.False(state.ActiveFixes["force_continue"].Enabled);
        Assert.Contains("Applied fixes", appliedSnapshot.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleKeyForTests_EscapeCancelsInlineFixEditorWithoutChangingActiveFixes()
    {
        var controller = new TuiKeyboardController();
        var state = new TuiState();

        controller.HandleKeyForTests(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false), state);
        controller.HandleKeyForTests(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false), state);
        controller.HandleKeyForTests(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false), state);

        var snapshot = state.TakeSnapshot();
        Assert.False(snapshot.IsFixSelectionPromptActive);
        Assert.Contains("FC", snapshot.ActiveFixesShorthand, StringComparison.Ordinal);
        Assert.True(state.ActiveFixes["force_continue"].Enabled);
        Assert.Contains("Canceled fixes edit", snapshot.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleKeyForTests_LTogglesLogModeAndConsumesLogNavigationUntilEscape()
    {
        var controller = new TuiKeyboardController();
        var state = new TuiState();

        state.AppendLog("first request");

        controller.HandleKeyForTests(new ConsoleKeyInfo('l', ConsoleKey.L, false, false, false), state);

        var opened = state.GetLogSnapshot();
        Assert.True(opened.LogMode);
        Assert.Single(opened.Entries);
        Assert.Contains("first request", opened.Entries[0], StringComparison.Ordinal);

        controller.HandleKeyForTests(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false), state);

        var scrolled = state.GetLogSnapshot();
        Assert.True(scrolled.LogMode);
        Assert.Equal(1, scrolled.LogScroll);

        controller.HandleKeyForTests(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false), state);

        var closed = state.GetLogSnapshot();
        Assert.False(closed.LogMode);
        Assert.Single(closed.Entries);
        Assert.Contains("first request", closed.Entries[0], StringComparison.Ordinal);
    }

    [Fact]
    public void HandleKeyForTests_SStartsNamedSavePromptAndWritesInteractionSlotArtifacts()
    {
        using var exportDirectory = new TestExportDirectory();

        var controller = new TuiKeyboardController();
        var state = new TuiState(new InteractionExportService(exportDirectory.Path));

        state.AppendInputMessage("green", "user", "Hello save");
        state.AppendOutputRaw("Hi there.");
        state.AppendRawOutput("data: Hi there.");
        state.RawMode = true;

        controller.HandleKeyForTests(new ConsoleKeyInfo('s', ConsoleKey.S, false, false, false), state);

        var promptSnapshot = state.TakeSnapshot();
        Assert.True(promptSnapshot.IsNamedSavePromptActive);
        Assert.False(promptSnapshot.IsInteractionFilterPromptActive);
        Assert.False(promptSnapshot.IsFixSelectionPromptActive);
        Assert.Contains("Editing save file name", promptSnapshot.StatusMessage, StringComparison.Ordinal);

        SendSavePromptText(controller, state, "visible:save/name");
        controller.HandleKeyForTests(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), state);

        var snapshot = state.TakeSnapshot();
        Assert.False(snapshot.IsNamedSavePromptActive);
        Assert.Contains("Saved interaction slot", snapshot.StatusMessage, StringComparison.Ordinal);

        var metadataPath = Path.Combine(exportDirectory.Path, "saved", "interactions", "visible-save-name.json");
        var markdownPath = Path.Combine(exportDirectory.Path, "saved", "interactions", "visible-save-name.md");
        var rawRequestPath = Path.Combine(exportDirectory.Path, "saved", "interactions", "visible-save-name.request.txt");
        var rawResponsePath = Path.Combine(exportDirectory.Path, "saved", "interactions", "visible-save-name.response.txt");
        Assert.True(File.Exists(metadataPath));
        Assert.True(File.Exists(markdownPath));
        Assert.True(File.Exists(rawRequestPath));
        Assert.True(File.Exists(rawResponsePath));

        var markdown = File.ReadAllText(markdownPath);
        Assert.Contains("# LlamaFleece Interaction View", markdown, StringComparison.Ordinal);
        Assert.Contains("Hello save", markdown, StringComparison.Ordinal);
        Assert.Contains("Hi there.", markdown, StringComparison.Ordinal);

        Assert.Equal(string.Empty, File.ReadAllText(rawRequestPath));
        Assert.Equal("data: Hi there.", File.ReadAllText(rawResponsePath));
    }

    [Fact]
    public void HandleKeyForTests_EnterAndEscapeToggleFullscreenWhenNoPromptIsActive()
    {
        var controller = new TuiKeyboardController();
        var state = new TuiState();

        controller.HandleKeyForTests(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), state);
        Assert.True(state.FullscreenMode);

        controller.HandleKeyForTests(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false), state);
        Assert.False(state.FullscreenMode);
    }

    [Fact]
    public void HandleKeyForTests_ModalPromptsConsumeKeysBeforeLogAndFullscreenShortcuts()
    {
        using var exportDirectory = new TestExportDirectory();

        var controller = new TuiKeyboardController();
        var state = new TuiState(new InteractionExportService(exportDirectory.Path));

        SeedFilterableInteractions(state);

        controller.HandleKeyForTests(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), state);
        Assert.True(state.FullscreenMode);

        controller.HandleKeyForTests(new ConsoleKeyInfo('s', ConsoleKey.S, false, false, false), state);
        controller.HandleKeyForTests(new ConsoleKeyInfo('l', ConsoleKey.L, false, false, false), state);

        var savePromptSnapshot = state.TakeSnapshot();
        Assert.True(savePromptSnapshot.IsNamedSavePromptActive);
        Assert.False(savePromptSnapshot.LogMode);
        Assert.Equal("l", savePromptSnapshot.PendingSaveFileName);

        controller.HandleKeyForTests(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false), state);

        var canceledSaveSnapshot = state.TakeSnapshot();
        Assert.False(canceledSaveSnapshot.IsNamedSavePromptActive);
        Assert.True(canceledSaveSnapshot.FullscreenMode);
        Assert.False(canceledSaveSnapshot.LogMode);
        Assert.Contains("Canceled named save", canceledSaveSnapshot.StatusMessage, StringComparison.Ordinal);

        controller.HandleKeyForTests(new ConsoleKeyInfo('f', ConsoleKey.F, false, false, false), state);
        SendFilterPromptText(controller, state, "status=200");
        controller.HandleKeyForTests(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), state);

        var filteredSnapshot = state.TakeSnapshot();
        Assert.True(filteredSnapshot.FullscreenMode);
        Assert.True(filteredSnapshot.HasActiveFilter);
        Assert.False(filteredSnapshot.IsInteractionFilterPromptActive);
        Assert.Contains("Applied interaction filter", filteredSnapshot.StatusMessage, StringComparison.Ordinal);
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

    private static void SendFilterPromptText(TuiKeyboardController controller, TuiState state, string text)
    {
        foreach (var character in text)
        {
            controller.HandleKeyForTests(new ConsoleKeyInfo(character, ConsoleKey.Spacebar, false, false, false), state);
        }
    }

    private static void SendSavePromptText(TuiKeyboardController controller, TuiState state, string text)
    {
        foreach (var character in text)
        {
            controller.HandleKeyForTests(new ConsoleKeyInfo(character, ConsoleKey.Spacebar, false, false, false), state);
        }
    }
}