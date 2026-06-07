internal static class TuiLayoutMetrics
{
    public const int InteractionsPanelHeight = 3;
    public const int InputPanelHeight = 10;
    public const int StatsPanelHeight = 7;
    public const int PanelChromeHeight = 2;
    public const int FallbackConsoleWidth = 80;
    public const int FallbackConsoleHeight = 25;
    private const int CompactStatsOneLineControlsWidth = 187;
    private const int CompactStatsTwoLineControlsWidth = 119;

    public static int ReadConsoleWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch
        {
            return FallbackConsoleWidth;
        }
    }

    public static int ReadConsoleHeight()
    {
        try
        {
            return Console.WindowHeight;
        }
        catch
        {
            return FallbackConsoleHeight;
        }
    }

    public static int GetInputViewportLines(bool fullscreenMode, int consoleHeight)
    {
        return fullscreenMode
            ? GetFullscreenViewportLines(consoleHeight)
            : InputPanelHeight - PanelChromeHeight;
    }

    public static int GetOutputViewportLines(bool fullscreenMode, int consoleHeight)
    {
        return GetOutputViewportLines(fullscreenMode, consoleHeight, StatsPanelHeight);
    }

    public static int GetOutputViewportLines(bool fullscreenMode, int consoleHeight, int statsPanelHeight)
    {
        return fullscreenMode
            ? GetFullscreenViewportLines(consoleHeight)
            : Math.Max(1, consoleHeight - InteractionsPanelHeight - InputPanelHeight - statsPanelHeight - PanelChromeHeight);
    }

    public static int GetLogViewportLines(int consoleHeight)
    {
        return Math.Max(1, consoleHeight - InteractionsPanelHeight - StatsPanelHeight - PanelChromeHeight);
    }

    public static int GetFullscreenViewportLines(int consoleHeight)
    {
        return Math.Max(1, consoleHeight - StatsPanelHeight - PanelChromeHeight);
    }

    public static int GetStatsPanelHeight(bool logMode, bool fullscreenMode, bool fixSelectionPromptActive, int consoleWidth)
    {
        if (logMode || fullscreenMode || fixSelectionPromptActive)
        {
            return StatsPanelHeight;
        }

        return PanelChromeHeight + GetNormalStatsSummaryLineCount(consoleWidth) + GetNormalStatsControlLineCount(consoleWidth);
    }

    public static int GetNormalStatsSummaryLineCount(int consoleWidth)
    {
        return GetNormalStatsControlLineCount(consoleWidth) == 1 ? 4 : 3;
    }

    public static int GetNormalStatsControlLineCount(int consoleWidth)
    {
        var availableWidth = Math.Max(0, consoleWidth - 4);
        if (availableWidth >= CompactStatsOneLineControlsWidth)
        {
            return 1;
        }

        if (availableWidth >= CompactStatsTwoLineControlsWidth)
        {
            return 2;
        }

        return 3;
    }
}
