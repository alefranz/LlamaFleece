if (PerformanceHarnessOptions.IsHelpRequested(args))
{
    PerformanceHarnessOptions.WriteUsage(Console.Out);
    return 0;
}

PerformanceHarnessOptions options;
try
{
    options = PerformanceHarnessOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    PerformanceHarnessOptions.WriteUsage(Console.Error);
    return 2;
}

try
{
#if DEBUG
    Console.Error.WriteLine("Run the performance harness with Release configuration so the collected baseline matches a release build.");
    return 2;
#else
    var runner = new PerformanceSuiteRunner();
    var report = await runner.RunAsync(options);

    if (!string.IsNullOrWhiteSpace(options.CompareBaselinePath))
    {
        var baseline = PerformanceReportWriter.ReadReport(options.CompareBaselinePath!);
        report = report with
        {
            Comparison = PerformanceBaselineComparer.Compare(report, baseline, options)
        };
    }

    var writeResult = PerformanceReportWriter.WriteReport(report, options);

    Console.WriteLine($"Performance report JSON: {writeResult.ReportJsonPath}");
    Console.WriteLine($"Performance report Markdown: {writeResult.ReportMarkdownPath}");

    if (!string.IsNullOrWhiteSpace(writeResult.BaselineJsonPath) &&
        !string.IsNullOrWhiteSpace(writeResult.BaselineMarkdownPath))
    {
        Console.WriteLine($"Baseline JSON: {writeResult.BaselineJsonPath}");
        Console.WriteLine($"Baseline Markdown: {writeResult.BaselineMarkdownPath}");
    }

    if (report.Comparison is null)
    {
        return 0;
    }

    Console.WriteLine($"Baseline comparison: {(report.Comparison.Passed ? "PASS" : "FAIL")}");
    foreach (var failure in report.Comparison.Failures)
    {
        Console.WriteLine($"- {failure}");
    }

    return report.Comparison.Passed ? 0 : 1;
#endif
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Performance harness failed: {ex.Message}");
    return 1;
}