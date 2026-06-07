internal readonly record struct ProxyLoggingResult(bool SawDone, bool GotContent, string? FinishReason);

internal sealed class ProxyLoggingStream
{
    private readonly ProxySseTransport _transport;
    private readonly ProxyProviderEventParser _providerEventParser;

    public ProxyLoggingStream(Stream downstream)
    {
        var uiProjector = new ProxyStreamUiProjector();
        _transport = new ProxySseTransport(downstream);
        _providerEventParser = new ProxyProviderEventParser(uiProjector);
    }

    public static bool CanProcessAsSse(HttpResponseMessage response)
    {
        return response.Content.Headers.ContentType?.MediaType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true;
    }

    public async Task<ProxyLoggingResult> CopySseAsync(Stream upstream, CancellationToken cancellationToken)
    {
        var sawDone = await _transport.CopySseAsync(upstream, HandleForwardedLineAsync, cancellationToken);
        return new ProxyLoggingResult(
            sawDone || _providerEventParser.SawDone,
            _providerEventParser.GotContent,
            _providerEventParser.FinishReason);
    }

    public static async Task WriteCompletionAsync(Stream downstream, CancellationToken cancellationToken)
    {
        ProxyStreamUiProjector.AppendRawOutput("data: [DONE]\n\n");
        await ProxySseTransport.WriteCompletionAsync(downstream, cancellationToken);
    }

    private Task HandleForwardedLineAsync(string line, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProxyStreamUiProjector.AppendRawOutput(line + "\n");
        _providerEventParser.ProcessLine(line);
        return Task.CompletedTask;
    }
}
