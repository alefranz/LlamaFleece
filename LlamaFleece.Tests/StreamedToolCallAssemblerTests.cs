using System.Text.Json;
using Xunit;

public class StreamedToolCallAssemblerTests
{
    [Fact]
    public void Apply_AccumulatesIncrementalFunctionArgumentsAcrossChunks()
    {
        var assembler = new StreamedToolCallAssembler();

        using var firstChunk = JsonDocument.Parse("""
        [
          {
            "index": 0,
            "id": "call_1",
            "function": {
              "name": "search_docs",
              "arguments": "{\"query\":\"ll"
            }
          }
        ]
        """);

        using var secondChunk = JsonDocument.Parse("""
        [
          {
            "index": 0,
            "function": {
              "arguments": "ama\"}"
            }
          }
        ]
        """);

        var firstUpdates = assembler.Apply(firstChunk.RootElement);
        var secondUpdates = assembler.Apply(secondChunk.RootElement);

        Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallName, "search_docs", "tool-call:call_1:name"), firstUpdates[0]);
        Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallArguments, "{\"query\":\"ll", "tool-call:call_1:arguments"), firstUpdates[1]);
        Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallName, "search_docs", "tool-call:call_1:name"), secondUpdates[0]);
        Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallArguments, "{\"query\":\"llama\"}", "tool-call:call_1:arguments"), secondUpdates[1]);
    }

    [Fact]
    public void Apply_UsesIndexFallbackWhenToolCallIdIsMissing()
    {
        var assembler = new StreamedToolCallAssembler();

        using var chunk = JsonDocument.Parse("""
        [
          {
            "index": 1,
            "function": {
              "name": "weather",
              "arguments": "{}"
            }
          }
        ]
        """);

        var updates = assembler.Apply(chunk.RootElement);

        Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallName, "weather", "tool-call:1:name"), updates[0]);
        Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallArguments, "{}", "tool-call:1:arguments"), updates[1]);
    }

  [Fact]
  public void ApplyResponseToolSnapshot_ReusesPlaceholderSegmentOrderWhenNameArrivesLater()
  {
    var assembler = new StreamedToolCallAssembler();

    var deltaUpdates = assembler.ApplyResponseToolDelta("item_1", "call_1", "{\"path\":\"", placeholderName: "tool call");
    var snapshotUpdates = assembler.ApplyResponseToolSnapshot(null, "call_1", "apply_patch", "{\"path\":\"PLAN.md\"}");

    Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallName, "tool call", "tool-call:call_1:name"), deltaUpdates[0]);
    Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallArguments, "{\"path\":\"", "tool-call:call_1:arguments"), deltaUpdates[1]);
    Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallName, "apply_patch", "tool-call:call_1:name"), snapshotUpdates[0]);
    Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallArguments, "{\"path\":\"PLAN.md\"}", "tool-call:call_1:arguments"), snapshotUpdates[1]);
  }

  [Fact]
  public void Apply_UsesChoiceScopeToAvoidCrossChoiceToolCallCollisions()
  {
    var assembler = new StreamedToolCallAssembler();

    using var firstChoiceChunk = JsonDocument.Parse("""
    [
      {
        "index": 0,
        "function": {
          "name": "apply_patch",
          "arguments": "{}"
        }
      }
    ]
    """);

    using var secondChoiceChunk = JsonDocument.Parse("""
    [
      {
        "index": 0,
        "function": {
          "name": "runTests",
          "arguments": "{}"
        }
      }
    ]
    """);

    var firstUpdates = assembler.Apply(firstChoiceChunk.RootElement, "choice-0");
    var secondUpdates = assembler.Apply(secondChoiceChunk.RootElement, "choice-1");

    Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallName, "apply_patch", "tool-call:choice-0:0:name"), firstUpdates[0]);
    Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallArguments, "{}", "tool-call:choice-0:0:arguments"), firstUpdates[1]);
    Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallName, "runTests", "tool-call:choice-1:0:name"), secondUpdates[0]);
    Assert.Equal(new OutputSegment(OutputSegmentKind.ToolCallArguments, "{}", "tool-call:choice-1:0:arguments"), secondUpdates[1]);
  }
}