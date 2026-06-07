using namespace System.Globalization

param(
    [string]$OutputRoot = "artifacts/perf",
    [string]$Compare,
    [string]$RecordBaseline,
    [ValidateSet("all", "chat-completions", "responses-api")]
    [string]$Scenario = "all",
    [int]$WarmupRequests = 20,
    [int]$MeasuredRequests = 200,
    [int]$Concurrency = [Math]::Max(1, [Math]::Min([Environment]::ProcessorCount, 8)),
    [int]$ResponseChunks = 64,
    [double]$MaxLatencyP95Regression = 0.20,
    [double]$MaxThroughputRegression = 0.15,
    [double]$MaxWorkingSetRegression = 0.20,
    [double]$MaxManagedHeapRegression = 0.20,
    [double]$MaxAllocatedPerRequestRegression = 0.15
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PathValue)
}

$repoRoot = Resolve-FullPath -PathValue (Join-Path $PSScriptRoot '..')
$projectPath = Join-Path $repoRoot "LlamaFleece.PerfHarness\LlamaFleece.PerfHarness.csproj"

$arguments = @(
    "run",
    "--configuration", "Release",
    "--project", $projectPath,
    "--",
    "--output-root", (Resolve-FullPath -PathValue $OutputRoot),
    "--warmup", $WarmupRequests.ToString([CultureInfo]::InvariantCulture),
    "--requests", $MeasuredRequests.ToString([CultureInfo]::InvariantCulture),
    "--concurrency", $Concurrency.ToString([CultureInfo]::InvariantCulture),
    "--response-chunks", $ResponseChunks.ToString([CultureInfo]::InvariantCulture),
    "--scenario", $Scenario,
    "--max-latency-p95-regression", $MaxLatencyP95Regression.ToString([CultureInfo]::InvariantCulture),
    "--max-throughput-regression", $MaxThroughputRegression.ToString([CultureInfo]::InvariantCulture),
    "--max-working-set-regression", $MaxWorkingSetRegression.ToString([CultureInfo]::InvariantCulture),
    "--max-managed-heap-regression", $MaxManagedHeapRegression.ToString([CultureInfo]::InvariantCulture),
    "--max-allocated-per-request-regression", $MaxAllocatedPerRequestRegression.ToString([CultureInfo]::InvariantCulture)
)

if ($Compare)
{
    $arguments += @("--compare", (Resolve-FullPath -PathValue $Compare))
}

if ($RecordBaseline)
{
    $arguments += @("--write-baseline", (Resolve-FullPath -PathValue $RecordBaseline))
}

& dotnet @arguments
exit $LASTEXITCODE