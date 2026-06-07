using System.Globalization;

public sealed record class PerformanceHarnessOptions
{
    public const double DefaultMaxLatencyP95RegressionRatio = 0.20d;
    public const double DefaultMaxThroughputRegressionRatio = 0.15d;
    public const double DefaultMaxPeakWorkingSetRegressionRatio = 0.20d;
    public const double DefaultMaxPeakManagedHeapRegressionRatio = 0.20d;
    public const double DefaultMaxAllocatedBytesPerRequestRegressionRatio = 0.15d;

    public string OutputRootPath { get; init; } = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "perf"));

    public string? CompareBaselinePath { get; init; }

    public string? WriteBaselinePath { get; init; }

    public int WarmupRequests { get; init; } = 20;

    public int MeasuredRequests { get; init; } = 200;

    public int Concurrency { get; init; } = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));

    public int ResponseChunkCount { get; init; } = 64;

    public string ScenarioFilter { get; init; } = "all";

    public double MaxLatencyP95RegressionRatio { get; init; } = DefaultMaxLatencyP95RegressionRatio;

    public double MaxThroughputRegressionRatio { get; init; } = DefaultMaxThroughputRegressionRatio;

    public double MaxPeakWorkingSetRegressionRatio { get; init; } = DefaultMaxPeakWorkingSetRegressionRatio;

    public double MaxPeakManagedHeapRegressionRatio { get; init; } = DefaultMaxPeakManagedHeapRegressionRatio;

    public double MaxAllocatedBytesPerRequestRegressionRatio { get; init; } = DefaultMaxAllocatedBytesPerRequestRegressionRatio;

    public static bool IsHelpRequested(string[] args)
    {
        return args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase));
    }

    public static PerformanceHarnessOptions Parse(string[] args)
    {
        var options = new PerformanceHarnessOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];

            switch (arg)
            {
                case "--output-root":
                    options = options with { OutputRootPath = NormalizePath(ReadString(args, ref index, arg)) };
                    break;
                case "--compare":
                    options = options with { CompareBaselinePath = NormalizePath(ReadString(args, ref index, arg)) };
                    break;
                case "--write-baseline":
                    options = options with { WriteBaselinePath = NormalizePath(ReadString(args, ref index, arg)) };
                    break;
                case "--warmup":
                    options = options with { WarmupRequests = ReadInt(args, ref index, arg) };
                    break;
                case "--requests":
                    options = options with { MeasuredRequests = ReadInt(args, ref index, arg) };
                    break;
                case "--concurrency":
                    options = options with { Concurrency = ReadInt(args, ref index, arg) };
                    break;
                case "--response-chunks":
                    options = options with { ResponseChunkCount = ReadInt(args, ref index, arg) };
                    break;
                case "--scenario":
                    options = options with { ScenarioFilter = ReadString(args, ref index, arg).Trim().ToLowerInvariant() };
                    break;
                case "--max-latency-p95-regression":
                    options = options with { MaxLatencyP95RegressionRatio = ReadDouble(args, ref index, arg) };
                    break;
                case "--max-throughput-regression":
                    options = options with { MaxThroughputRegressionRatio = ReadDouble(args, ref index, arg) };
                    break;
                case "--max-working-set-regression":
                    options = options with { MaxPeakWorkingSetRegressionRatio = ReadDouble(args, ref index, arg) };
                    break;
                case "--max-managed-heap-regression":
                    options = options with { MaxPeakManagedHeapRegressionRatio = ReadDouble(args, ref index, arg) };
                    break;
                case "--max-allocated-per-request-regression":
                    options = options with { MaxAllocatedBytesPerRequestRegressionRatio = ReadDouble(args, ref index, arg) };
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        Validate(options);
        return options;
    }

    public static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: dotnet run --configuration Release --project .\\LlamaFleece.PerfHarness -- [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --output-root <path>                    Output directory for generated reports. Default: artifacts/perf");
        writer.WriteLine("  --compare <baseline.json>               Compare the current run against a saved baseline report.");
        writer.WriteLine("  --write-baseline <baseline.json>        Write a baseline JSON and Markdown pair to the specified path.");
        writer.WriteLine("  --warmup <count>                        Warmup request count per scenario. Default: 20");
        writer.WriteLine("  --requests <count>                      Measured request count per scenario. Default: 200");
        writer.WriteLine("  --concurrency <count>                   Parallel request count per scenario. Default: min(processors, 8)");
        writer.WriteLine("  --response-chunks <count>               SSE chunks emitted by the deterministic upstream. Default: 64");
        writer.WriteLine("  --scenario <all|chat-completions|responses-api>");
        writer.WriteLine("                                           Scenario selection. Default: all");
        writer.WriteLine("  --max-latency-p95-regression <ratio>    Maximum allowed p95 latency increase when comparing. Default: 0.20");
        writer.WriteLine("  --max-throughput-regression <ratio>     Maximum allowed request/sec decrease when comparing. Default: 0.15");
        writer.WriteLine("  --max-working-set-regression <ratio>    Maximum allowed peak working set increase when comparing. Default: 0.20");
        writer.WriteLine("  --max-managed-heap-regression <ratio>   Maximum allowed peak managed heap increase when comparing. Default: 0.20");
        writer.WriteLine("  --max-allocated-per-request-regression <ratio>");
        writer.WriteLine("                                           Maximum allowed allocated bytes/request increase when comparing. Default: 0.15");
    }

    private static string NormalizePath(string value)
    {
        return Path.GetFullPath(string.IsNullOrWhiteSpace(value) ? Environment.CurrentDirectory : value);
    }

    private static string ReadString(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {option}.");
        }

        index++;
        return args[index];
    }

    private static int ReadInt(string[] args, ref int index, string option)
    {
        var rawValue = ReadString(args, ref index, option);
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new ArgumentException($"Invalid integer value '{rawValue}' for {option}.");
        }

        return value;
    }

    private static double ReadDouble(string[] args, ref int index, string option)
    {
        var rawValue = ReadString(args, ref index, option);
        if (!double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
        {
            throw new ArgumentException($"Invalid numeric value '{rawValue}' for {option}.");
        }

        return value;
    }

    private static void Validate(PerformanceHarnessOptions options)
    {
        if (options.WarmupRequests < 0)
        {
            throw new ArgumentException("Warmup request count must be zero or greater.");
        }

        if (options.MeasuredRequests <= 0)
        {
            throw new ArgumentException("Measured request count must be greater than zero.");
        }

        if (options.Concurrency <= 0)
        {
            throw new ArgumentException("Concurrency must be greater than zero.");
        }

        if (options.ResponseChunkCount <= 0)
        {
            throw new ArgumentException("Response chunk count must be greater than zero.");
        }

        if (options.ScenarioFilter is not ("all" or "chat-completions" or "responses-api"))
        {
            throw new ArgumentException("Scenario must be one of: all, chat-completions, responses-api.");
        }

        ValidateRatio(options.MaxLatencyP95RegressionRatio, nameof(options.MaxLatencyP95RegressionRatio));
        ValidateRatio(options.MaxThroughputRegressionRatio, nameof(options.MaxThroughputRegressionRatio));
        ValidateRatio(options.MaxPeakWorkingSetRegressionRatio, nameof(options.MaxPeakWorkingSetRegressionRatio));
        ValidateRatio(options.MaxPeakManagedHeapRegressionRatio, nameof(options.MaxPeakManagedHeapRegressionRatio));
        ValidateRatio(options.MaxAllocatedBytesPerRequestRegressionRatio, nameof(options.MaxAllocatedBytesPerRequestRegressionRatio));

        if (!string.IsNullOrWhiteSpace(options.WriteBaselinePath) &&
            !options.WriteBaselinePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Baseline output path must end with .json.");
        }

        if (!string.IsNullOrWhiteSpace(options.CompareBaselinePath) &&
            !options.CompareBaselinePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Baseline compare path must point to a .json file.");
        }
    }

    private static void ValidateRatio(double value, string name)
    {
        if (value < 0d || value > 1d)
        {
            throw new ArgumentException($"{name} must be between 0 and 1.");
        }
    }
}