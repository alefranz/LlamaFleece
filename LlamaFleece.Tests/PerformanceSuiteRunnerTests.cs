using Xunit;

[Collection("TuiManager serial")]
public class PerformanceSuiteRunnerTests
{
    [Fact]
    public async Task RunAsync_ProducesScenarioMetrics()
    {
        var runner = new PerformanceSuiteRunner();
        var options = new PerformanceHarnessOptions
        {
            WarmupRequests = 1,
            MeasuredRequests = 4,
            Concurrency = 2,
            ResponseChunkCount = 6,
            ScenarioFilter = "chat-completions"
        };

        var report = await runner.RunAsync(options);

        var scenario = Assert.Single(report.Scenarios);
        Assert.Equal("chat-completions", scenario.Name);
        Assert.Equal("/v1/chat/completions", scenario.Endpoint);
        Assert.Equal(1, scenario.WarmupCount);
        Assert.Equal(4, scenario.RequestCount);
        Assert.True(scenario.TotalResponseBytes > 0);
        Assert.True(scenario.TotalElapsedMilliseconds > 0);
        Assert.True(scenario.RequestsPerSecond > 0);
        Assert.True(scenario.ResponseBytesPerSecond > 0);
        Assert.Equal(4, scenario.Latency.SampleCount);
        Assert.True(scenario.Latency.P95Milliseconds >= scenario.Latency.P50Milliseconds);
        Assert.True(scenario.Memory.PeakWorkingSetBytes > 0);
        Assert.True(scenario.Memory.PeakManagedHeapBytes > 0);
        Assert.True(scenario.Memory.AllocatedBytesPerRequest > 0);
    }

    [Fact]
    public void Compare_FailsWhenMetricsExceedRegressionThresholds()
    {
        var baselineScenario = new PerformanceScenarioReport
        {
            Name = "chat-completions",
            Endpoint = "/v1/chat/completions",
            RequestCount = 10,
            WarmupCount = 1,
            Concurrency = 2,
            ResponseChunkCount = 8,
            RequestsPerSecond = 100d,
            Latency = new PerformanceLatencyMetrics
            {
                SampleCount = 10,
                P50Milliseconds = 8d,
                P95Milliseconds = 10d,
                P99Milliseconds = 11d,
                MaxMilliseconds = 12d
            },
            Memory = new PerformanceMemoryMetrics
            {
                PeakWorkingSetBytes = 10_000_000,
                PeakManagedHeapBytes = 4_000_000,
                AllocatedBytesPerRequest = 1_000d
            }
        };

        var baseline = new PerformanceSuiteReport
        {
            Settings = new PerformanceRunSettings
            {
                BuildConfiguration = "Release",
                WarmupRequests = 1,
                MeasuredRequests = 10,
                Concurrency = 2,
                ResponseChunksPerRequest = 8,
                ScenarioFilter = "chat-completions"
            },
            Scenarios = new[] { baselineScenario }
        };

        var current = baseline with
        {
            Scenarios = new[]
            {
                baselineScenario with
                {
                    RequestsPerSecond = 80d,
                    Latency = baselineScenario.Latency with
                    {
                        P95Milliseconds = 13d,
                        P99Milliseconds = 14d,
                        MaxMilliseconds = 15d
                    },
                    Memory = baselineScenario.Memory with
                    {
                        PeakWorkingSetBytes = 13_000_000,
                        PeakManagedHeapBytes = 5_000_000,
                        AllocatedBytesPerRequest = 1_250d
                    }
                }
            }
        };

        var comparison = PerformanceBaselineComparer.Compare(
            current,
            baseline,
            new PerformanceHarnessOptions
            {
                CompareBaselinePath = Path.GetFullPath(Path.Combine("docs", "performance-baselines", "sample.json")),
                WarmupRequests = 1,
                MeasuredRequests = 10,
                Concurrency = 2,
                ResponseChunkCount = 8,
                ScenarioFilter = "chat-completions",
                MaxThroughputRegressionRatio = 0.15d,
                MaxLatencyP95RegressionRatio = 0.20d,
                MaxPeakWorkingSetRegressionRatio = 0.20d,
                MaxPeakManagedHeapRegressionRatio = 0.20d,
                MaxAllocatedBytesPerRequestRegressionRatio = 0.15d
            });

        Assert.False(comparison.Passed);
        Assert.Contains(comparison.Failures, failure => failure.Contains("throughput", StringComparison.Ordinal));
        Assert.Contains(comparison.Failures, failure => failure.Contains("p95 latency", StringComparison.Ordinal));
        Assert.Contains(comparison.Failures, failure => failure.Contains("peak working set", StringComparison.Ordinal));
        Assert.Contains(comparison.Failures, failure => failure.Contains("peak managed heap", StringComparison.Ordinal));
        Assert.Contains(comparison.Failures, failure => failure.Contains("allocated bytes/request", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_AllowsSmallAbsoluteLatencyVarianceOnFastRuns()
    {
        var baseline = new PerformanceSuiteReport
        {
            Settings = new PerformanceRunSettings
            {
                BuildConfiguration = "Release",
                WarmupRequests = 1,
                MeasuredRequests = 10,
                Concurrency = 2,
                ResponseChunksPerRequest = 8,
                ScenarioFilter = "chat-completions"
            },
            Scenarios = new[]
            {
                new PerformanceScenarioReport
                {
                    Name = "chat-completions",
                    Endpoint = "/v1/chat/completions",
                    RequestCount = 10,
                    WarmupCount = 1,
                    Concurrency = 2,
                    ResponseChunkCount = 8,
                    RequestsPerSecond = 100d,
                    Latency = new PerformanceLatencyMetrics
                    {
                        SampleCount = 10,
                        P95Milliseconds = 1.2d
                    },
                    Memory = new PerformanceMemoryMetrics
                    {
                        PeakWorkingSetBytes = 10_000_000,
                        PeakManagedHeapBytes = 4_000_000,
                        AllocatedBytesPerRequest = 1_000d
                    }
                }
            }
        };

        var current = baseline with
        {
            Scenarios = new[]
            {
                baseline.Scenarios.Single() with
                {
                    Latency = baseline.Scenarios.Single().Latency with
                    {
                        P95Milliseconds = 1.9d
                    }
                }
            }
        };

        var comparison = PerformanceBaselineComparer.Compare(
            current,
            baseline,
            new PerformanceHarnessOptions
            {
                CompareBaselinePath = Path.GetFullPath(Path.Combine("docs", "performance-baselines", "sample.json")),
                WarmupRequests = 1,
                MeasuredRequests = 10,
                Concurrency = 2,
                ResponseChunkCount = 8,
                ScenarioFilter = "chat-completions"
            });

        Assert.True(comparison.Passed);
    }
}