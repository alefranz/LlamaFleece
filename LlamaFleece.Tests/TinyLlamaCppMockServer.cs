using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal sealed class TinyLlamaCppMockServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly object _gate = new();
    private readonly Queue<TinyLlamaCppMockResponse> _responses = new();
    private readonly List<TinyLlamaCppMockRequest> _requests = new();

    private TinyLlamaCppMockServer(WebApplication app)
    {
        _app = app;
    }

    public Uri BaseAddress { get; private set; } = null!;

    public IReadOnlyList<TinyLlamaCppMockRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    public static async Task<TinyLlamaCppMockServer> StartAsync(params TinyLlamaCppMockResponse[] responses)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, 0);
        });

        var app = builder.Build();
        var server = new TinyLlamaCppMockServer(app);
        server.Enqueue(responses);
        app.Run(server.HandleAsync);

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var address = addresses?.Addresses.SingleOrDefault()
            ?? throw new InvalidOperationException("Tiny llama.cpp mock server did not expose a listening address.");

        server.BaseAddress = new Uri(address.EndsWith("/", StringComparison.Ordinal) ? address : address + "/", UriKind.Absolute);
        return server;
    }

    public HttpClient CreateClient()
    {
        return new HttpClient
        {
            BaseAddress = BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public void Enqueue(params TinyLlamaCppMockResponse[] responses)
    {
        lock (_gate)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(response);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task HandleAsync(HttpContext context)
    {
        var request = await CaptureRequestAsync(context.Request);
        var response = DequeueResponse(request);

        context.Response.StatusCode = (int)response.StatusCode;
        context.Response.ContentType = response.ContentType;
        foreach (var header in response.Headers)
        {
            context.Response.Headers[header.Key] = header.Value;
        }

        foreach (var chunk in response.Chunks)
        {
            var bytes = Encoding.UTF8.GetBytes(chunk);
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }

    private TinyLlamaCppMockResponse DequeueResponse(TinyLlamaCppMockRequest request)
    {
        lock (_gate)
        {
            _requests.Add(request);

            if (_responses.Count == 0)
            {
                return TinyLlamaCppMockResponse.Json(
                    "{\"error\":\"No scripted response configured for the tiny llama.cpp mock server.\"}",
                    HttpStatusCode.InternalServerError);
            }

            return _responses.Dequeue();
        }
    }

    private static async Task<TinyLlamaCppMockRequest> CaptureRequestAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        var headers = request.Headers.ToDictionary(
            pair => pair.Key,
            pair => Array.ConvertAll(pair.Value.ToArray(), value => value ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        return new TinyLlamaCppMockRequest(
            request.Method,
            request.Path.Value ?? "/",
            request.QueryString.Value ?? string.Empty,
            body,
            headers);
    }
}

internal sealed record TinyLlamaCppMockRequest(
    string Method,
    string Path,
    string QueryString,
    string Body,
    IReadOnlyDictionary<string, string[]> Headers);

internal sealed record TinyLlamaCppMockResponse(
    HttpStatusCode StatusCode,
    string ContentType,
    IReadOnlyList<string> Chunks,
    IReadOnlyDictionary<string, string[]> Headers)
{
    public static TinyLlamaCppMockResponse Json(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new TinyLlamaCppMockResponse(
            statusCode,
            "application/json",
            new[] { body },
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
    }

    public static TinyLlamaCppMockResponse Sse(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new TinyLlamaCppMockResponse(
            statusCode,
            "text/event-stream",
            new[] { body },
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
    }

    public static TinyLlamaCppMockResponse SseChunks(IEnumerable<string> chunks, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new TinyLlamaCppMockResponse(
            statusCode,
            "text/event-stream",
            chunks.ToArray(),
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
    }
}