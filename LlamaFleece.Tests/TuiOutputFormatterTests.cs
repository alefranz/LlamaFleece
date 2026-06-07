using Xunit;
using Spectre.Console;

public class TuiOutputFormatterTests
{
    [Fact]
    public void FormatLines_PreservesExplicitMarkupLines()
    {
        var formatted = TuiOutputFormatter.FormatLines(new[]
        {
            new OutputSegment(OutputSegmentKind.Markup, "[bold magenta]tool[/]")
        });

        Assert.Equal("[bold magenta]tool[/]", formatted);
    }

    [Fact]
    public void FormatLines_RendersReasoningSegmentsInGrey()
    {
        var formatted = TuiOutputFormatter.FormatLines(new[]
        {
            new OutputSegment(OutputSegmentKind.Reasoning, "draft"),
            new OutputSegment(OutputSegmentKind.Reasoning, "still drafting"),
            new OutputSegment(OutputSegmentKind.Text, "final")
        });

        Assert.Equal("[grey]draft[/]\n[grey]still drafting[/]\nfinal", formatted.Replace("\r\n", "\n"));
    }

    [Fact]
    public void FormatLines_HighlightsUnknownClosingTags()
    {
        var formatted = TuiOutputFormatter.FormatLines(new[] { new OutputSegment(OutputSegmentKind.Text, "</tool_call>") });

        Assert.Equal("[red]</tool_call>[/]", formatted);
    }

    [Fact]
    public void FormatLines_FormatsToolCallArguments()
    {
        var formatted = TuiOutputFormatter.FormatLines(new[]
        {
            new OutputSegment(OutputSegmentKind.ToolCallName, "search_docs"),
            new OutputSegment(OutputSegmentKind.ToolCallArguments, "{\"query\":\"llama\"}")
        });

        Assert.Equal("[bold magenta]🔧 search_docs[/]\n[dim]parameters: {\"query\":\"llama\"}[/]", formatted.Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData("[Done.]")]
    [InlineData("[Output Generation] -> *Proceeds*")]
    [InlineData("*(Self-Correction/Note during drafting)*:")]
    [InlineData("Output matches the final refined version.")]
    [InlineData("[")]
    [InlineData("[Output Generation")]
    public void FormatLines_ProducesValidMarkupForBracketedReasoningText(string text)
    {
        var formatted = TuiOutputFormatter.FormatLines(new[]
        {
            new OutputSegment(OutputSegmentKind.Reasoning, text)
        });

        var exception = Record.Exception(() => _ = new Markup(formatted));

        Assert.Null(exception);
    }

    [Fact]
    public void FormatLines_DoesNotExtraDimDoneReasoningMarker()
    {
        var formatted = TuiOutputFormatter.FormatLines(new[]
        {
            new OutputSegment(OutputSegmentKind.Reasoning, "[Done.]")
        });

         Assert.Equal("[grey][[Done.]][/]", formatted);
    }
}
