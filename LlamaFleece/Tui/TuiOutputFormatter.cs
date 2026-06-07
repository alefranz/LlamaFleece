using System.Text.RegularExpressions;
using Spectre.Console;

internal static class TuiOutputFormatter
{
    private static readonly Regex ClosingTagRegex = new(@"</\s*[a-zA-Z0-9_]+\s*>", RegexOptions.Compiled);

    public static string FormatLines(IEnumerable<OutputSegment> lines)
    {
        return string.Join(Environment.NewLine, lines.Select(FormatLine));
    }

    private static string FormatLine(OutputSegment line)
    {
        return line.Kind switch
        {
            OutputSegmentKind.Markup => line.Text,
            OutputSegmentKind.Reasoning => WrapGrey(FormatText(line.Text)),
            OutputSegmentKind.ToolCallName => $"[bold magenta]🔧 {Markup.Escape(line.Text)}[/]",
            OutputSegmentKind.ToolCallArguments => $"[dim]parameters: {Markup.Escape(line.Text)}[/]",
            _ => FormatText(line.Text)
        };
    }

    private static string ApplyKnownReplacements(string text)
    {
        return text.Replace("[[Output Generation]] -&gt; *Proceeds*", "[dim][[Output Generation]] -> *Proceeds*[/]")
                   .Replace("*(Self-Correction/Note during drafting)*:", "[dim]*(Self-Correction/Note during drafting)*:[/]")
                   .Replace("Output matches the final refined version.", "[dim]Output matches the final refined version.[/]")
                   .Replace("[[Done.]]", "[[Done.]]");
    }

    private static string FormatText(string text)
    {
        return HighlightUnknownClosingTags(ApplyKnownReplacements(Markup.Escape(text)));
    }

    private static string HighlightUnknownClosingTags(string text)
    {
        return ClosingTagRegex.Replace(text, match =>
        {
            var compactValue = match.Value.Replace(" ", string.Empty);
            return compactValue is "</thinking>" or "</think>" or "</step>"
                ? match.Value
                : $"[red]{match.Value}[/]";
        });
    }

    private static string WrapGrey(string text)
    {
        return string.IsNullOrEmpty(text) ? text : $"[grey]{text}[/]";
    }
}
