using System.Globalization;

public static class PerformanceBaselineComparer
{
    public const double MinimumLatencyP95IncreaseMilliseconds = 1.0d;

    public static BaselineComparisonReport Compare(
        PerformanceSuiteReport current,
        PerformanceSuiteReport baseline,
        PerformanceHarnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        ValidateComparableSettings(current.Settings, baseline.Settings, failures);

        var baselineScenarios = baseline.Scenarios.ToDictionary(
            scenario => scenario.Name,
            scenario => scenario,
            StringComparer.OrdinalIgnoreCase);

        foreach (var scenario in current.Scenarios)
        {
            if (!baselineScenarios.TryGetValue(scenario.Name, out var baselineScenario))
            {
                failures.Add($"Missing baseline scenario '{scenario.Name}'. Capture a fresh baseline with the same scenario set before comparing.");
                continue;
            }

            CompareLowerBound(
                failures,
                scenario.Name,
                metricName: "throughput",
                currentValue: scenario.RequestsPerSecond,
                baselineValue: baselineScenario.RequestsPerSecond,
                tolerance: options.MaxThroughputRegressionRatio,
                units: "req/s");

            CompareUpperBound(
                failures,
                scenario.Name,
                metricName: "p95 latency",
                currentValue: scenario.Latency.P95Milliseconds,
                baselineValue: baselineScenario.Latency.P95Milliseconds,
                tolerance: options.MaxLatencyP95RegressionRatio,
                minimumAbsoluteIncrease: MinimumLatencyP95IncreaseMilliseconds,
                units: "ms");

            CompareUpperBound(
                failures,
                scenario.Name,
                metricName: "peak working set",
                currentValue: scenario.Memory.PeakWorkingSetBytes,
                baselineValue: baselineScenario.Memory.PeakWorkingSetBytes,
                tolerance: options.MaxPeakWorkingSetRegressionRatio,
                minimumAbsoluteIncrease: 0d,
                units: "bytes");

            CompareUpperBound(
                failures,
                scenario.Name,
                metricName: "peak managed heap",
                currentValue: scenario.Memory.PeakManagedHeapBytes,
                baselineValue: baselineScenario.Memory.PeakManagedHeapBytes,
                tolerance: options.MaxPeakManagedHeapRegressionRatio,
                minimumAbsoluteIncrease: 0d,
                units: "bytes");

            CompareUpperBound(
                failures,
                scenario.Name,
                metricName: "allocated bytes/request",
                currentValue: scenario.Memory.AllocatedBytesPerRequest,
                baselineValue: baselineScenario.Memory.AllocatedBytesPerRequest,
                tolerance: options.MaxAllocatedBytesPerRequestRegressionRatio,
                minimumAbsoluteIncrease: 0d,
                units: "bytes");
        }

        return new BaselineComparisonReport
        {
            BaselinePath = options.CompareBaselinePath ?? string.Empty,
            Passed = failures.Count == 0,
            MaxLatencyP95RegressionRatio = options.MaxLatencyP95RegressionRatio,
            MinimumLatencyP95IncreaseMilliseconds = MinimumLatencyP95IncreaseMilliseconds,
            MaxThroughputRegressionRatio = options.MaxThroughputRegressionRatio,
            MaxPeakWorkingSetRegressionRatio = options.MaxPeakWorkingSetRegressionRatio,
            MaxPeakManagedHeapRegressionRatio = options.MaxPeakManagedHeapRegressionRatio,
            MaxAllocatedBytesPerRequestRegressionRatio = options.MaxAllocatedBytesPerRequestRegressionRatio,
            Failures = failures
        };
    }

    private static void ValidateComparableSettings(
        PerformanceRunSettings current,
        PerformanceRunSettings baseline,
        ICollection<string> failures)
    {
        if (!string.Equals(current.BuildConfiguration, baseline.BuildConfiguration, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"Build configuration mismatch: current {current.BuildConfiguration}, baseline {baseline.BuildConfiguration}.");
        }

        if (current.MeasuredRequests != baseline.MeasuredRequests ||
            current.Concurrency != baseline.Concurrency ||
            current.ResponseChunksPerRequest != baseline.ResponseChunksPerRequest ||
            !string.Equals(current.ScenarioFilter, baseline.ScenarioFilter, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"Run settings mismatch: current requests={current.MeasuredRequests}, concurrency={current.Concurrency}, chunks={current.ResponseChunksPerRequest}, scenario={current.ScenarioFilter}; " +
                $"baseline requests={baseline.MeasuredRequests}, concurrency={baseline.Concurrency}, chunks={baseline.ResponseChunksPerRequest}, scenario={baseline.ScenarioFilter}.");
        }
    }

    private static void CompareLowerBound(
        ICollection<string> failures,
        string scenarioName,
        string metricName,
        double currentValue,
        double baselineValue,
        double tolerance,
        string units)
    {
        if (baselineValue <= 0d)
        {
            return;
        }

        var minimumAllowed = baselineValue * (1d - tolerance);
        if (currentValue >= minimumAllowed)
        {
            return;
        }

        failures.Add(
            $"{scenarioName} {metricName} regression: current {FormatValue(currentValue, units)} is below allowed {FormatValue(minimumAllowed, units)} " +
            $"(baseline {FormatValue(baselineValue, units)}, tolerance {FormatTolerance(tolerance)})." );
    }

    private static void CompareUpperBound(
        ICollection<string> failures,
        string scenarioName,
        string metricName,
        double currentValue,
        double baselineValue,
        double tolerance,
        double minimumAbsoluteIncrease,
        string units)
    {
        if (baselineValue <= 0d)
        {
            return;
        }

        var maximumAllowed = baselineValue * (1d + tolerance);
        if (minimumAbsoluteIncrease > 0d)
        {
            maximumAllowed = Math.Max(maximumAllowed, baselineValue + minimumAbsoluteIncrease);
        }

        if (currentValue <= maximumAllowed)
        {
            return;
        }

        failures.Add(
            $"{scenarioName} {metricName} regression: current {FormatValue(currentValue, units)} is above allowed {FormatValue(maximumAllowed, units)} " +
            $"(baseline {FormatValue(baselineValue, units)}, tolerance {FormatTolerance(tolerance)}{FormatAbsoluteIncrease(minimumAbsoluteIncrease, units)})." );
    }

    private static string FormatTolerance(double tolerance)
    {
        return tolerance.ToString("P0", CultureInfo.InvariantCulture);
    }

    private static string FormatAbsoluteIncrease(double minimumAbsoluteIncrease, string units)
    {
        if (minimumAbsoluteIncrease <= 0d)
        {
            return string.Empty;
        }

        return $" or +{FormatValue(minimumAbsoluteIncrease, units)}";
    }

    private static string FormatValue(double value, string units)
    {
        return $"{value.ToString("F3", CultureInfo.InvariantCulture)} {units}";
    }
}