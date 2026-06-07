using Xunit;

public class TuiSectionNavigatorTests
{
    [Fact]
    public void MoveToPreviousSection_ConvertsSectionOffsetsIntoBottomBasedScroll()
    {
        var scroll = TuiSectionNavigator.MoveToPreviousSection(
            totalLineCount: 40,
            currentScroll: 12,
            sectionStarts: new[] { 0, 10, 20, 30 },
            viewportLineCount: 8);

        Assert.Equal(22, scroll);
    }

    [Fact]
    public void MoveToNextSection_ConvertsSectionOffsetsIntoBottomBasedScroll()
    {
        var scroll = TuiSectionNavigator.MoveToNextSection(
            totalLineCount: 40,
            currentScroll: 12,
            sectionStarts: new[] { 0, 10, 20, 30 },
            viewportLineCount: 8);

        Assert.Equal(2, scroll);
    }

    [Fact]
    public void MoveToPreviousSection_UsesFallbackWhenNoSectionsExist()
    {
        var scroll = TuiSectionNavigator.MoveToPreviousSection(
            totalLineCount: 20,
            currentScroll: 4,
            sectionStarts: System.Array.Empty<int>(),
            viewportLineCount: 8);

        Assert.Equal(9, scroll);
    }
}
