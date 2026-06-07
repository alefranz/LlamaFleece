using System.Text;
using Xunit;

[Collection("TuiManager serial")]
public class ProxyLoggingStreamTests
{
    [Fact]
    public async Task CopySseAsync_TracksDoneAndStreamsParsedContent()
    {
        const string upstreamBody = "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\n" +
                                  "data: [DONE]\n\n";

        TuiManager.ResetForTests();
        TuiManager.NewSession();

        await using var upstream = new MemoryStream(Encoding.UTF8.GetBytes(upstreamBody));
        await using var downstream = new MemoryStream();

        var stream = new ProxyLoggingStream(downstream);
        var result = await stream.CopySseAsync(upstream, CancellationToken.None);

        Assert.True(result.SawDone);
        Assert.True(result.GotContent);
        Assert.Null(result.FinishReason);

        downstream.Position = 0;
        using var downstreamReader = new StreamReader(downstream, Encoding.UTF8, leaveOpen: true);
        var forwardedText = await downstreamReader.ReadToEndAsync();

        Assert.Contains("data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}", forwardedText, StringComparison.Ordinal);
        Assert.DoesNotContain("data: [DONE]", forwardedText, StringComparison.Ordinal);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Contains("hello", interaction!.RawOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopySseAsync_ForwardsMalformedSsePayloadWithoutCrashing()
    {
        const string upstreamBody = "data: {not-json}\n\n";

        TuiManager.ResetForTests();
        TuiManager.NewSession();

        await using var upstream = new MemoryStream(Encoding.UTF8.GetBytes(upstreamBody));
        await using var downstream = new MemoryStream();

        var stream = new ProxyLoggingStream(downstream);
        var result = await stream.CopySseAsync(upstream, CancellationToken.None);

        Assert.False(result.SawDone);
        Assert.False(result.GotContent);
        Assert.Null(result.FinishReason);

        downstream.Position = 0;
        using var downstreamReader = new StreamReader(downstream, Encoding.UTF8, leaveOpen: true);
        var forwardedText = await downstreamReader.ReadToEndAsync();

        Assert.Contains("data: {not-json}", forwardedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteCompletionAsync_WritesDoneSentinel()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();

        await using var downstream = new MemoryStream();

        await ProxyLoggingStream.WriteCompletionAsync(downstream, CancellationToken.None);

        downstream.Position = 0;
        using var downstreamReader = new StreamReader(downstream, Encoding.UTF8, leaveOpen: true);
        var completionText = await downstreamReader.ReadToEndAsync();

        Assert.Equal("data: [DONE]\n\n", completionText);

        var interaction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(interaction);
        Assert.Contains("data: [DONE]", interaction!.RawOutput.ToString(), StringComparison.Ordinal);
    }
}
