public enum OutputSegmentKind
{
    Text,
    Reasoning,
    Markup,
    ToolCallName,
    ToolCallArguments
}

public readonly record struct OutputSegment(OutputSegmentKind Kind, string Text, string? Key = null);