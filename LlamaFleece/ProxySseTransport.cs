using System.Text;

internal sealed class ProxySseTransport
{
    private static readonly byte[] CompletionBytes = Encoding.UTF8.GetBytes("data: [DONE]\n\n");

    private readonly Stream _downstream;

    public ProxySseTransport(Stream downstream)
    {
        _downstream = downstream;
    }

    public async Task<bool> CopySseAsync(
        Stream upstream,
        Func<string, CancellationToken, Task> onForwardedLineAsync,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(upstream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        var sawDone = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (IsCompletionLine(line))
            {
                sawDone = true;
                continue;
            }

            await WriteLineAsync(line, cancellationToken);
            await onForwardedLineAsync(line, cancellationToken);
        }

        return sawDone;
    }

    public static Task WriteCompletionAsync(Stream downstream, CancellationToken cancellationToken)
    {
        return WriteBytesAsync(downstream, CompletionBytes, cancellationToken);
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        var text = line + "\n";
        var bytes = Encoding.UTF8.GetBytes(text);
        await WriteBytesAsync(_downstream, bytes, cancellationToken);
    }

    private static async Task WriteBytesAsync(Stream downstream, byte[] bytes, CancellationToken cancellationToken)
    {
        await downstream.WriteAsync(bytes, cancellationToken);
        await downstream.FlushAsync(cancellationToken);
    }

    private static bool IsCompletionLine(string line)
    {
        return line.TrimEnd().Equals("data: [DONE]", StringComparison.Ordinal);
    }
}
