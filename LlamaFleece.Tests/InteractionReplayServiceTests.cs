using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

[Collection("TuiManager serial")]
public class InteractionReplayServiceTests
{
    [Fact]
    public async Task ReplayInteractionAsync_ReplaysVisibleInteractionAgainstCurrentUpstream()
    {
        const string requestJson = "{\"model\":\"gpt-test\",\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}],\"stream\":true}";
        const string upstreamBody = "data: {\"choices\":[{\"delta\":{\"content\":\"Hi again\"}}]}\n\n" +
                                   "data: [DONE]\n\n";

        TuiManager.ResetForTests();
        TuiManager.NewSession();
        TuiManager.SetLatestRequestEnvelope(new InteractionRequestEnvelope
        {
            Method = HttpMethods.Post,
            Path = "/v1/chat/completions",
            QueryString = "?api-version=2026-05-01",
            ContentType = "application/json"
        });
        TuiManager.AppendRawInput(requestJson);

        await using var upstream = await TinyLlamaCppMockServer.StartAsync(TinyLlamaCppMockResponse.Sse(upstreamBody));
        var upstreamBaseAddress = new Uri(upstream.BaseAddress, "current-base/");

        using var client = upstream.CreateClient();
        client.BaseAddress = upstreamBaseAddress;

        var options = ProxyOptions.LoadAndValidate(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Proxy:UpstreamUrl"] = upstreamBaseAddress.ToString()
            })
            .Build());

        var service = new InteractionReplayService(
            new TrackedRequestCoordinator(client, options, UpstreamRequestHeaderInjection.Create(options)),
            options);

        await service.ReplayInteractionAsync(TuiManager.GetVisibleInteractionSnapshotForTests());

        var capturedRequest = Assert.Single(upstream.Requests);
        Assert.Equal("POST", capturedRequest.Method);
        Assert.Equal("/current-base/v1/chat/completions", capturedRequest.Path);
        Assert.Equal("?api-version=2026-05-01", capturedRequest.QueryString);
        Assert.Contains("\"include_usage\":true", capturedRequest.Body, StringComparison.Ordinal);

        var replayInteraction = TuiManager.GetVisibleInteractionSnapshotForTests();
        Assert.NotNull(replayInteraction);
        Assert.Equal(1, replayInteraction!.Id);
        Assert.NotNull(replayInteraction.RequestEnvelope);
        Assert.Equal("/v1/chat/completions", replayInteraction.RequestEnvelope!.Path);
        Assert.Equal("?api-version=2026-05-01", replayInteraction.RequestEnvelope.QueryString);
        Assert.Equal(requestJson, replayInteraction.RawInput.ToString());
        Assert.Equal("Hi again", replayInteraction.CurrentOutputLine);

        var status = TuiManager.GetStatusSnapshotForTests();
        Assert.False(status.IsError);
        Assert.Contains("Replayed interaction 0 as interaction 1 with 200.", status.Message, StringComparison.Ordinal);

        var logSnapshot = TuiManager.GetLogSnapshotForTests();
        Assert.Contains(logSnapshot.Entries, entry =>
            entry.Contains(">>> REPLAY POST", StringComparison.Ordinal) &&
            entry.Contains("/v1/chat/completions?api-version=2026-05-01", StringComparison.Ordinal) &&
            entry.Contains("[source=0]", StringComparison.Ordinal));
        Assert.Contains(logSnapshot.Entries, entry => entry.Contains("<<< REPLAY 200", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReplayInteractionAsync_FailsWhenCapturedEnvelopeIsMissing()
    {
        TuiManager.ResetForTests();
        TuiManager.NewSession();
        TuiManager.AppendRawInput("{\"prompt\":\"hello\"}");

        var options = ProxyOptions.LoadAndValidate(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Proxy:UpstreamUrl"] = "http://upstream.test/current-base"
            })
            .Build());

        using var client = new HttpClient
        {
            BaseAddress = new Uri("http://upstream.test/current-base")
        };

        var service = new InteractionReplayService(
            new TrackedRequestCoordinator(client, options, UpstreamRequestHeaderInjection.Create(options)),
            options);

        await service.ReplayInteractionAsync(TuiManager.GetVisibleInteractionSnapshotForTests());

        var status = TuiManager.GetStatusSnapshotForTests();
        Assert.True(status.IsError);
        Assert.Contains("has no captured request envelope", status.Message, StringComparison.Ordinal);
        Assert.Equal(1, TuiManager.InteractionCountForTests());
    }
}