using System.Text;
using Xunit;

public class ProxySseTransportTests
{
    [Fact]
    public async Task CopySseAsync_ForwardsLinesAndTracksCompletionSentinel()
    {
        const string upstreamBody = "data: one\n\n" +
                                  "data: [DONE]\n\n" +
                                  "data: two\n\n";

        await using var upstream = new MemoryStream(Encoding.UTF8.GetBytes(upstreamBody));
        await using var downstream = new MemoryStream();

        var forwardedLineCount = 0;
        var transport = new ProxySseTransport(downstream);

        var sawDone = await transport.CopySseAsync(
            upstream,
            (_, _) =>
            {
                forwardedLineCount++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(sawDone);
        Assert.Equal(5, forwardedLineCount);

        downstream.Position = 0;
        using var downstreamReader = new StreamReader(downstream, Encoding.UTF8, leaveOpen: true);
        var forwardedText = await downstreamReader.ReadToEndAsync();

        Assert.Contains("data: one", forwardedText, StringComparison.Ordinal);
        Assert.Contains("data: two", forwardedText, StringComparison.Ordinal);
        Assert.DoesNotContain("data: [DONE]", forwardedText, StringComparison.Ordinal);
    }
}
