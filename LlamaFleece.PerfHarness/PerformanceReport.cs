public sealed record class PerformanceSuiteReport
{
    public int SchemaVersion { get; init; } = 1;

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public PerformanceHostInfo Host { get; init; } = new();

    public PerformanceRunSettings Settings { get; init; } = new();

    public IReadOnlyList<PerformanceScenarioReport> Scenarios { get; init; } = Array.Empty<PerformanceScenarioReport>();

    public BaselineComparisonReport? Comparison { get; init; }
}

public sealed record class PerformanceHostInfo
{
    public string FrameworkDescription { get; init; } = string.Empty;

    public string OsDescription { get; init; } = string.Empty;

    public string ProcessArchitecture { get; init; } = string.Empty;

    public int ProcessorCount { get; init; }
}

public sealed record class PerformanceRunSettings
{
    public string BuildConfiguration { get; init; } = "Release";

    public int WarmupRequests { get; init; }

    public int MeasuredRequests { get; init; }

    public int Concurrency { get; init; }

    public int ResponseChunksPerRequest { get; init; }

    public string ScenarioFilter { get; init; } = "all";
}

public sealed record class PerformanceScenarioReport
{
    public string Name { get; init; } = string.Empty;

    public string Endpoint { get; init; } = string.Empty;

    public int WarmupCount { get; init; }

    public int RequestCount { get; init; }

    public int Concurrency { get; init; }

    public int ResponseChunkCount { get; init; }

    public long TotalResponseBytes { get; init; }

    public double TotalElapsedMilliseconds { get; init; }

    public double RequestsPerSecond { get; init; }

    public double ResponseBytesPerSecond { get; init; }

    public PerformanceLatencyMetrics Latency { get; init; } = new();

    public PerformanceMemoryMetrics Memory { get; init; } = new();
}

public sealed record class PerformanceLatencyMetrics
{
    public int SampleCount { get; init; }

    public double MinMilliseconds { get; init; }

    public double AverageMilliseconds { get; init; }

    public double P50Milliseconds { get; init; }

    public double P95Milliseconds { get; init; }

    public double P99Milliseconds { get; init; }

    public double MaxMilliseconds { get; init; }
}

public sealed record class PerformanceMemoryMetrics
{
    public long WorkingSetBeforeBytes { get; init; }

    public long WorkingSetAfterBytes { get; init; }

    public long PeakWorkingSetBytes { get; init; }

    public long PrivateMemoryBeforeBytes { get; init; }

    public long PrivateMemoryAfterBytes { get; init; }

    public long PeakPrivateMemoryBytes { get; init; }

    public long ManagedHeapBeforeBytes { get; init; }

    public long ManagedHeapAfterBytes { get; init; }

    public long PeakManagedHeapBytes { get; init; }

    public long TotalAllocatedBytes { get; init; }

    public double AllocatedBytesPerRequest { get; init; }
}

public sealed record class BaselineComparisonReport
{
    public string BaselinePath { get; init; } = string.Empty;

    public bool Passed { get; init; }

    public double MaxLatencyP95RegressionRatio { get; init; }

    public double MinimumLatencyP95IncreaseMilliseconds { get; init; }

    public double MaxThroughputRegressionRatio { get; init; }

    public double MaxPeakWorkingSetRegressionRatio { get; init; }

    public double MaxPeakManagedHeapRegressionRatio { get; init; }

    public double MaxAllocatedBytesPerRequestRegressionRatio { get; init; }

    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();
}

public sealed record class PerformanceWriteResult(
    string ReportJsonPath,
    string ReportMarkdownPath,
    string? BaselineJsonPath,
    string? BaselineMarkdownPath);