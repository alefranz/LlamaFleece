internal sealed class TuiKeyboardController
{
    public void ProcessPendingKeys(TuiState state, int maxKeysPerFrame = 8)
    {
        ProcessPendingKeys(state, TryReadKey, maxKeysPerFrame);
    }

    internal void ProcessPendingKeys(TuiState state, Func<(bool HasKey, ConsoleKeyInfo Key)> tryReadKey, int maxKeysPerFrame = 8)
    {
        ArgumentNullException.ThrowIfNull(tryReadKey);

        for (var i = 0; i < maxKeysPerFrame; i++)
        {
            var (hasKey, key) = tryReadKey();
            if (!hasKey)
            {
                return;
            }

            HandleKey(key, state);

            if (ApplicationShutdownCoordinator.IsShutdownRequested)
            {
                return;
            }
        }
    }

    internal void HandleKeyForTests(ConsoleKeyInfo key, TuiState state)
    {
        HandleKey(key, state);
    }

    private static (bool HasKey, ConsoleKeyInfo Key) TryReadKey()
    {
        var key = default(ConsoleKeyInfo);

        try
        {
            if (!Console.KeyAvailable)
            {
                return (false, default);
            }

            key = Console.ReadKey(intercept: true);
            return (true, key);
        }
        catch
        {
            return (false, default);
        }
    }

    private void HandleKey(ConsoleKeyInfo key, TuiState state)
    {
        if (state.TryHandleNamedSavePromptKey(key))
        {
            return;
        }

        if (state.TryHandleInteractionFilterPromptKey(key))
        {
            return;
        }

        if (state.TryHandleFixSelectionPromptKey(key))
        {
            return;
        }

        if (key.Key == ConsoleKey.Q)
        {
            ApplicationShutdownCoordinator.RequestShutdown("TUI quit key pressed.");
            return;
        }

        if (key.Key == ConsoleKey.L)
        {
            state.ToggleLogMode();
            return;
        }

        if (state.LogMode)
        {
            HandleLogModeKey(key, state);
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.E:
                if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                {
                    state.ExportSession();
                }
                else
                {
                    state.ExportVisibleInteraction();
                }

                return;
            case ConsoleKey.P:
                state.StartReplayVisibleInteraction();
                return;
            case ConsoleKey.S:
                state.PromptForNamedSave();
                return;
            case ConsoleKey.F:
                if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                {
                    state.ClearInteractionFilter();
                }
                else
                {
                    state.PromptForInteractionFilter();
                }

                return;
            case ConsoleKey.Tab:
                state.CycleActivePane();
                return;
            case ConsoleKey.R:
                state.ToggleRawMode();
                return;
            case ConsoleKey.Spacebar:
                state.ToggleLocked();
                return;
            case ConsoleKey.C:
                state.SelectCurrentInteraction();
                return;
            case ConsoleKey.X:
                state.PromptForFixSelection();
                return;
            case ConsoleKey.Enter:
                state.ToggleFullscreenMode();
                return;
            case ConsoleKey.Escape:
                state.DisableFullscreenMode();
                return;
            case ConsoleKey.LeftArrow when state.ActivePane == 0:
                state.SelectPreviousInteraction();
                return;
            case ConsoleKey.RightArrow when state.ActivePane == 0:
                state.SelectNextInteraction();
                return;
            case ConsoleKey.UpArrow:
                state.ScrollActivePaneUp();
                return;
            case ConsoleKey.DownArrow:
                state.ScrollActivePaneDown();
                return;
            case ConsoleKey.PageUp:
                state.MoveActivePaneToPreviousSection(GetViewportLineCount(state));
                return;
            case ConsoleKey.PageDown:
                state.MoveActivePaneToNextSection(GetViewportLineCount(state));
                return;
        }
    }

    private static void HandleLogModeKey(ConsoleKeyInfo key, TuiState state)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                state.CloseLogMode();
                break;
            case ConsoleKey.UpArrow:
                state.ScrollLogUp();
                break;
            case ConsoleKey.DownArrow:
                state.ScrollLogDown();
                break;
        }
    }

    private static int GetViewportLineCount(TuiState state)
    {
        var consoleHeight = TuiLayoutMetrics.ReadConsoleHeight();
        var consoleWidth = TuiLayoutMetrics.ReadConsoleWidth();
        var statsPanelHeight = TuiLayoutMetrics.GetStatsPanelHeight(
            logMode: false,
            fullscreenMode: state.FullscreenMode,
            fixSelectionPromptActive: false,
            consoleWidth);
        return state.ActivePane switch
        {
            1 => TuiLayoutMetrics.GetInputViewportLines(state.FullscreenMode, consoleHeight),
            2 => TuiLayoutMetrics.GetOutputViewportLines(state.FullscreenMode, consoleHeight, statsPanelHeight),
            _ => 1
        };
    }
}
