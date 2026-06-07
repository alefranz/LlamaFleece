internal static class TuiSectionNavigator
{
    private const int FallbackStep = 5;

    public static int MoveToPreviousSection(int totalLineCount, int currentScroll, IReadOnlyList<int> sectionStarts, int viewportLineCount)
    {
        if (sectionStarts.Count == 0)
        {
            return Math.Max(0, currentScroll + FallbackStep);
        }

        var currentTopLine = GetTopLine(totalLineCount, currentScroll, viewportLineCount);
        var targetTopLine = -1;

        for (var i = sectionStarts.Count - 1; i >= 0; i--)
        {
            if (sectionStarts[i] < currentTopLine)
            {
                targetTopLine = sectionStarts[i];
                break;
            }
        }

        if (targetTopLine < 0)
        {
            targetTopLine = 0;
        }

        return GetScrollForTopLine(totalLineCount, viewportLineCount, targetTopLine);
    }

    public static int MoveToNextSection(int totalLineCount, int currentScroll, IReadOnlyList<int> sectionStarts, int viewportLineCount)
    {
        if (sectionStarts.Count == 0)
        {
            return Math.Max(0, currentScroll - FallbackStep);
        }

        var currentTopLine = GetTopLine(totalLineCount, currentScroll, viewportLineCount);

        foreach (var sectionStart in sectionStarts)
        {
            if (sectionStart > currentTopLine)
            {
                return GetScrollForTopLine(totalLineCount, viewportLineCount, sectionStart);
            }
        }

        return currentScroll;
    }

    public static int GetScrollForTopLine(int totalLineCount, int viewportLineCount, int targetTopLine)
    {
        return Math.Max(0, totalLineCount - Math.Max(1, viewportLineCount) - Math.Max(0, targetTopLine));
    }

    private static int GetTopLine(int totalLineCount, int currentScroll, int viewportLineCount)
    {
        return Math.Max(0, totalLineCount - Math.Max(1, viewportLineCount) - Math.Max(0, currentScroll));
    }
}
