using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.AspNetCore.Http;

public sealed class PerformanceSuiteRunner
{
    public async Task<PerformanceSuiteReport> RunAsync(PerformanceHarnessOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var scenarios = BuildScenarioDefinitions(options);
        var reports = new List<PerformanceScenarioReport>(scenarios.Count);

        foreach (var scenario in scenarios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scenarioReport = await RunScenarioAsync(scenario, options, cancellationToken);
            reports.Add(scenarioReport);
        }

        return new PerformanceSuiteReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Host = new PerformanceHostInfo
            {
                FrameworkDescription = RuntimeInformation.FrameworkDescription,
                OsDescription = RuntimeInformation.OSDescription,
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount = Environment.ProcessorCount
            },
            Settings = new PerformanceRunSettings
            {
                BuildConfiguration = "Release",
                WarmupRequests = options.WarmupRequests,
                MeasuredRequests = options.MeasuredRequests,
                Concurrency = options.Concurrency,
                ResponseChunksPerRequest = options.ResponseChunkCount,
                ScenarioFilter = options.ScenarioFilter
            },
            Scenarios = reports
        };
    }

    private static IReadOnlyList<PerformanceScenarioDefinition> BuildScenarioDefinitions(PerformanceHarnessOptions options)
    {
        var allScenarios = new[]
        {
            CreateChatCompletionsScenario(options.ResponseChunkCount),
            CreateResponsesScenario(options.ResponseChunkCount)
        };

        return options.ScenarioFilter == "all"
            ? allScenarios
            : allScenarios.Where(scenario => string.Equals(scenario.Name, options.ScenarioFilter, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static async Task<PerformanceScenarioReport> RunScenarioAsync(
        PerformanceScenarioDefinition scenario,
        PerformanceHarnessOptions options,
        CancellationToken cancellationToken)
    {
        using var isolatedState = TuiManager.BeginIsolatedScopeForTests();
        using var client = new HttpClient(new DeterministicUpstreamHandler(scenario.ResponseBodyBytes))
        {
            BaseAddress = new Uri("http://perf-upstream.local")
        };

        var middleware = new LoggingMiddleware(_ => Task.CompletedTask, new TrackedRequestCoordinator(client));

        TuiManager.ResetForTests();
        await ExecuteLoadAsync(
            middleware,
            scenario,
            options.WarmupRequests,
            options.Concurrency,
            latencies: null,
            onResponseBytes: null,
            cancellationToken);

        TuiManager.ResetForTests();
        ForceGarbageCollection();

        using var process = Process.GetCurrentProcess();
        var beforeSnapshot = CaptureMemorySnapshot(process);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

        using var peakMemoryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var peakMemoryTask = CapturePeakMemoryAsync(process, peakMemoryCts.Token);

        var latencies = new double[options.MeasuredRequests];
        long totalResponseBytes = 0;
        var totalStopwatch = Stopwatch.StartNew();

        try
        {
            await ExecuteLoadAsync(
                middleware,
                scenario,
                options.MeasuredRequests,
                options.Concurrency,
                latencies,
                bytes => Interlocked.Add(ref totalResponseBytes, bytes),
                cancellationToken);
        }
        finally
        {
            totalStopwatch.Stop();
            peakMemoryCts.Cancel();
        }

        var peakMemory = await peakMemoryTask;
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        var afterSnapshot = CaptureMemorySnapshot(process);

        var interactionCount = TuiManager.InteractionCountForTests();
        if (interactionCount != options.MeasuredRequests)
        {
            throw new InvalidOperationException(
                $"Scenario '{scenario.Name}' captured {interactionCount} interactions, expected {options.MeasuredRequests}. The harness requires deterministic tracked-request capture.");
        }

        var latency = BuildLatencyMetrics(latencies);
        var elapsedSeconds = totalStopwatch.Elapsed.TotalSeconds;

        return new PerformanceScenarioReport
        {
            Name = scenario.Name,
            Endpoint = scenario.RequestPath,
            WarmupCount = options.WarmupRequests,
            RequestCount = options.MeasuredRequests,
            Concurrency = options.Concurrency,
            ResponseChunkCount = options.ResponseChunkCount,
            TotalResponseBytes = totalResponseBytes,
            TotalElapsedMilliseconds = totalStopwatch.Elapsed.TotalMilliseconds,
            RequestsPerSecond = elapsedSeconds > 0d ? options.MeasuredRequests / elapsedSeconds : 0d,
            ResponseBytesPerSecond = elapsedSeconds > 0d ? totalResponseBytes / elapsedSeconds : 0d,
            Latency = latency,
            Memory = new PerformanceMemoryMetrics
            {
                WorkingSetBeforeBytes = beforeSnapshot.WorkingSetBytes,
                WorkingSetAfterBytes = afterSnapshot.WorkingSetBytes,
                PeakWorkingSetBytes = peakMemory.PeakWorkingSetBytes,
                PrivateMemoryBeforeBytes = beforeSnapshot.PrivateMemoryBytes,
                PrivateMemoryAfterBytes = afterSnapshot.PrivateMemoryBytes,
                PeakPrivateMemoryBytes = peakMemory.PeakPrivateMemoryBytes,
                ManagedHeapBeforeBytes = beforeSnapshot.ManagedHeapBytes,
                ManagedHeapAfterBytes = afterSnapshot.ManagedHeapBytes,
                PeakManagedHeapBytes = peakMemory.PeakManagedHeapBytes,
                TotalAllocatedBytes = Math.Max(0L, allocatedAfter - allocatedBefore),
                AllocatedBytesPerRequest = options.MeasuredRequests > 0
                    ? Math.Max(0d, allocatedAfter - allocatedBefore) / options.MeasuredRequests
                    : 0d
            }
        };
    }

    private static async Task ExecuteLoadAsync(
        LoggingMiddleware middleware,
        PerformanceScenarioDefinition scenario,
        int requestCount,
        int concurrency,
        double[]? latencies,
        Action<long>? onResponseBytes,
        CancellationToken cancellationToken)
    {
        if (requestCount == 0)
        {
            return;
        }

        await Parallel.ForEachAsync(
            Enumerable.Range(0, requestCount),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(concurrency, requestCount),
                CancellationToken = cancellationToken
            },
            async (requestIndex, token) =>
            {
                var stopwatch = Stopwatch.StartNew();
                var responseBytes = await InvokeTrackedRequestAsync(middleware, scenario, token);
                stopwatch.Stop();

                if (latencies is not null)
                {
                    latencies[requestIndex] = stopwatch.Elapsed.TotalMilliseconds;
                }

                onResponseBytes?.Invoke(responseBytes);
            });
    }

    private static async Task<long> InvokeTrackedRequestAsync(
        LoggingMiddleware middleware,
        PerformanceScenarioDefinition scenario,
        CancellationToken cancellationToken)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = scenario.RequestPath;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(scenario.RequestBodyBytes, writable: false);
        context.Response.Body = new MemoryStream();

        using var abortRegistration = cancellationToken.Register(context.Abort);
        await middleware.InvokeAsync(context);

        if (context.Response.StatusCode != StatusCodes.Status200OK)
        {
            throw new InvalidOperationException(
                $"Scenario '{scenario.Name}' returned unexpected status {context.Response.StatusCode}.");
        }

        return context.Response.Body.Length;
    }

    private static PerformanceScenarioDefinition CreateChatCompletionsScenario(int responseChunkCount)
    {
        const string requestBody = """
        {
          "model": "perf-chat-model",
          "messages": [
            { "role": "system", "content": "Respond concisely." },
            { "role": "user", "content": "Summarize the observed latency budget." }
          ],
          "stream": true
        }
        """;

        var responseBuilder = new StringBuilder();
        responseBuilder.AppendLine("data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}");
        responseBuilder.AppendLine();

        for (var index = 0; index < responseChunkCount; index++)
        {
            responseBuilder.Append("data: {\"choices\":[{\"delta\":{\"content\":\"");
            responseBuilder.Append($"token-{index + 1:0000} ");
            responseBuilder.Append("\"}}]}\n\n");
        }

        responseBuilder.Append($"data: {{\"choices\":[{{\"finish_reason\":\"stop\"}}],\"usage\":{{\"prompt_tokens\":18,\"completion_tokens\":{responseChunkCount},\"total_tokens\":{18 + responseChunkCount}}}}}\n\n");
        responseBuilder.Append("data: [DONE]\n\n");

        return new PerformanceScenarioDefinition(
            Name: "chat-completions",
            RequestPath: "/v1/chat/completions",
            RequestBodyBytes: Encoding.UTF8.GetBytes(requestBody),
            ResponseBodyBytes: Encoding.UTF8.GetBytes(responseBuilder.ToString()));
    }

    private static PerformanceScenarioDefinition CreateResponsesScenario(int responseChunkCount)
    {
        const string requestBody = """
        {
          "model": "perf-responses-model",
          "instructions": "Respond concisely.",
          "input": "Summarize the observed latency budget.",
          "stream": true
        }
        """;

        var responseBuilder = new StringBuilder();
        responseBuilder.AppendLine("event: response.created");
        responseBuilder.AppendLine("data: {\"type\":\"response.created\",\"response\":{\"id\":\"resp_perf\"}}");
        responseBuilder.AppendLine();

        for (var index = 0; index < responseChunkCount; index++)
        {
            responseBuilder.AppendLine("event: response.output_text.delta");
            responseBuilder.Append("data: {\"type\":\"response.output_text.delta\",\"item_id\":\"msg_perf\",\"output_index\":0,\"content_index\":0,\"delta\":\"");
            responseBuilder.Append($"token-{index + 1:0000} ");
            responseBuilder.Append("\"}\n\n");
        }

        responseBuilder.AppendLine("event: response.completed");
        responseBuilder.Append($"data: {{\"type\":\"response.completed\",\"response\":{{\"id\":\"resp_perf\",\"status\":\"completed\",\"usage\":{{\"input_tokens\":16,\"output_tokens\":{responseChunkCount},\"total_tokens\":{16 + responseChunkCount}}}}}}}\n\n");
        responseBuilder.Append("data: [DONE]\n\n");

        return new PerformanceScenarioDefinition(
            Name: "responses-api",
            RequestPath: "/v1/responses",
            RequestBodyBytes: Encoding.UTF8.GetBytes(requestBody),
            ResponseBodyBytes: Encoding.UTF8.GetBytes(responseBuilder.ToString()));
    }

    private static PerformanceLatencyMetrics BuildLatencyMetrics(double[] latencies)
    {
        if (latencies.Length == 0)
        {
            return new PerformanceLatencyMetrics();
        }

        var sortedLatencies = latencies.OrderBy(value => value).ToArray();

        return new PerformanceLatencyMetrics
        {
            SampleCount = latencies.Length,
            MinMilliseconds = sortedLatencies[0],
            AverageMilliseconds = latencies.Average(),
            P50Milliseconds = Percentile(sortedLatencies, 0.50d),
            P95Milliseconds = Percentile(sortedLatencies, 0.95d),
            P99Milliseconds = Percentile(sortedLatencies, 0.99d),
            MaxMilliseconds = sortedLatencies[^1]
        };
    }

    private static double Percentile(double[] sortedLatencies, double percentile)
    {
        if (sortedLatencies.Length == 0)
        {
            return 0d;
        }

        var position = (sortedLatencies.Length - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedLatencies[lower];
        }

        var weight = position - lower;
        return sortedLatencies[lower] + ((sortedLatencies[upper] - sortedLatencies[lower]) * weight);
    }

    private static void ForceGarbageCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static async Task<PeakMemorySample> CapturePeakMemoryAsync(Process process, CancellationToken cancellationToken)
    {
        var peak = PeakMemorySample.From(CaptureMemorySnapshot(process));

        while (!cancellationToken.IsCancellationRequested)
        {
            peak = peak.Max(CaptureMemorySnapshot(process));

            try
            {
                await Task.Delay(15, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return peak.Max(CaptureMemorySnapshot(process));
    }

    private static MemorySnapshot CaptureMemorySnapshot(Process process)
    {
        process.Refresh();
        return new MemorySnapshot(
            process.WorkingSet64,
            process.PrivateMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false));
    }

    private sealed class DeterministicUpstreamHandler : HttpMessageHandler
    {
        private readonly byte[] _responseBodyBytes;

        public DeterministicUpstreamHandler(byte[] responseBodyBytes)
        {
            _responseBodyBytes = responseBodyBytes;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_responseBodyBytes)
            };

            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        }
    }

    private sealed record PerformanceScenarioDefinition(
        string Name,
        string RequestPath,
        byte[] RequestBodyBytes,
        byte[] ResponseBodyBytes);

    private readonly record struct MemorySnapshot(
        long WorkingSetBytes,
        long PrivateMemoryBytes,
        long ManagedHeapBytes);

    private readonly record struct PeakMemorySample(
        long PeakWorkingSetBytes,
        long PeakPrivateMemoryBytes,
        long PeakManagedHeapBytes)
    {
        public static PeakMemorySample From(MemorySnapshot snapshot)
        {
            return new PeakMemorySample(snapshot.WorkingSetBytes, snapshot.PrivateMemoryBytes, snapshot.ManagedHeapBytes);
        }

        public PeakMemorySample Max(MemorySnapshot snapshot)
        {
            return new PeakMemorySample(
                Math.Max(PeakWorkingSetBytes, snapshot.WorkingSetBytes),
                Math.Max(PeakPrivateMemoryBytes, snapshot.PrivateMemoryBytes),
                Math.Max(PeakManagedHeapBytes, snapshot.ManagedHeapBytes));
        }
    }
}