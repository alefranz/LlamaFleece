using System.Globalization;
using System.Text;
using System.Text.Json;

public static class PerformanceReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static PerformanceSuiteReport ReadReport(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Baseline report not found: {fullPath}", fullPath);
        }

        using var stream = File.OpenRead(fullPath);
        var report = JsonSerializer.Deserialize<PerformanceSuiteReport>(stream, JsonOptions);
        return report ?? throw new InvalidDataException($"Failed to deserialize baseline report: {fullPath}");
    }

    public static PerformanceWriteResult WriteReport(PerformanceSuiteReport report, PerformanceHarnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(options);

        var runDirectory = Path.Combine(
            options.OutputRootPath,
            report.GeneratedAtUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss'Z'", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(runDirectory);

        var reportJsonPath = Path.Combine(runDirectory, "performance-report.json");
        var reportMarkdownPath = Path.Combine(runDirectory, "performance-report.md");

        File.WriteAllText(reportJsonPath, JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(reportMarkdownPath, BuildMarkdown(report, reportJsonPath, title: "LlamaFleece Performance Report"));

        string? baselineJsonPath = null;
        string? baselineMarkdownPath = null;

        if (!string.IsNullOrWhiteSpace(options.WriteBaselinePath))
        {
            baselineJsonPath = options.WriteBaselinePath;
            baselineMarkdownPath = Path.ChangeExtension(baselineJsonPath, ".md");

            var directory = Path.GetDirectoryName(baselineJsonPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var baselineReport = report with { Comparison = null };
            File.WriteAllText(baselineJsonPath, JsonSerializer.Serialize(baselineReport, JsonOptions));
            File.WriteAllText(baselineMarkdownPath, BuildMarkdown(baselineReport, baselineJsonPath, title: "LlamaFleece Performance Baseline"));
        }

        return new PerformanceWriteResult(reportJsonPath, reportMarkdownPath, baselineJsonPath, baselineMarkdownPath);
    }

    private static string BuildMarkdown(PerformanceSuiteReport report, string sourcePath, string title)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {title}");
        builder.AppendLine();
        builder.AppendLine($"- Generated UTC: {report.GeneratedAtUtc:O}");
        builder.AppendLine($"- Report path: {sourcePath}");
        builder.AppendLine($"- Runtime: {report.Host.FrameworkDescription}");
        builder.AppendLine($"- OS: {report.Host.OsDescription}");
        builder.AppendLine($"- Architecture: {report.Host.ProcessArchitecture}");
        builder.AppendLine($"- Processor count: {report.Host.ProcessorCount}");
        builder.AppendLine($"- Build configuration: {report.Settings.BuildConfiguration}");
        builder.AppendLine($"- Warmup requests per scenario: {report.Settings.WarmupRequests}");
        builder.AppendLine($"- Measured requests per scenario: {report.Settings.MeasuredRequests}");
        builder.AppendLine($"- Concurrency: {report.Settings.Concurrency}");
        builder.AppendLine($"- Response chunks per request: {report.Settings.ResponseChunksPerRequest}");
        builder.AppendLine();

        foreach (var scenario in report.Scenarios)
        {
            builder.AppendLine($"## {scenario.Name}");
            builder.AppendLine();
            builder.AppendLine($"- Endpoint: {scenario.Endpoint}");
            builder.AppendLine($"- Requests: {scenario.RequestCount} measured, {scenario.WarmupCount} warmup");
            builder.AppendLine($"- Total time: {scenario.TotalElapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms");
            builder.AppendLine($"- Throughput: {scenario.RequestsPerSecond.ToString("F3", CultureInfo.InvariantCulture)} req/s, {FormatBytesPerSecond(scenario.ResponseBytesPerSecond)}");
            builder.AppendLine($"- Response volume: {FormatBytes(scenario.TotalResponseBytes)} total");
            builder.AppendLine(
                $"- Latency: avg {scenario.Latency.AverageMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms, " +
                $"p50 {scenario.Latency.P50Milliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms, " +
                $"p95 {scenario.Latency.P95Milliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms, " +
                $"p99 {scenario.Latency.P99Milliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms, " +
                $"max {scenario.Latency.MaxMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms");
            builder.AppendLine(
                $"- Memory: alloc/request {scenario.Memory.AllocatedBytesPerRequest.ToString("F3", CultureInfo.InvariantCulture)} bytes, " +
                $"peak managed heap {FormatBytes(scenario.Memory.PeakManagedHeapBytes)}, " +
                $"peak working set {FormatBytes(scenario.Memory.PeakWorkingSetBytes)}, " +
                $"peak private memory {FormatBytes(scenario.Memory.PeakPrivateMemoryBytes)}");
            builder.AppendLine();
        }

        if (report.Comparison is not null)
        {
            builder.AppendLine("## Baseline Comparison");
            builder.AppendLine();
            builder.AppendLine($"- Baseline path: {report.Comparison.BaselinePath}");
            builder.AppendLine($"- Result: {(report.Comparison.Passed ? "PASS" : "FAIL")}");
            builder.AppendLine($"- Max p95 latency regression: {report.Comparison.MaxLatencyP95RegressionRatio.ToString("P0", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"- Minimum absolute p95 latency allowance: {report.Comparison.MinimumLatencyP95IncreaseMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ms");
            builder.AppendLine($"- Max throughput regression: {report.Comparison.MaxThroughputRegressionRatio.ToString("P0", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"- Max peak working set regression: {report.Comparison.MaxPeakWorkingSetRegressionRatio.ToString("P0", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"- Max peak managed heap regression: {report.Comparison.MaxPeakManagedHeapRegressionRatio.ToString("P0", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"- Max allocated bytes/request regression: {report.Comparison.MaxAllocatedBytesPerRequestRegressionRatio.ToString("P0", CultureInfo.InvariantCulture)}");

            if (report.Comparison.Failures.Count == 0)
            {
                builder.AppendLine("- No regressions detected.");
            }
            else
            {
                foreach (var failure in report.Comparison.Failures)
                {
                    builder.AppendLine($"- {failure}");
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatBytes(long value)
    {
        return FormatBytes((double)value);
    }

    private static string FormatBytes(double value)
    {
        var suffixes = new[] { "B", "KiB", "MiB", "GiB", "TiB" };
        var index = 0;
        var size = value;

        while (size >= 1024d && index < suffixes.Length - 1)
        {
            size /= 1024d;
            index++;
        }

        return $"{size.ToString("F2", CultureInfo.InvariantCulture)} {suffixes[index]}";
    }

    private static string FormatBytesPerSecond(double value)
    {
        return $"{FormatBytes(value)}/s";
    }
}