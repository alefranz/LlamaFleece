# Performance Baselines

LlamaFleece keeps performance and load validation outside the default `dotnet test` loop.

`eng/perf.ps1` runs the release-only `LlamaFleece.PerfHarness` project, sends deterministic tracked traffic through the real `LoggingMiddleware` and `TrackedRequestCoordinator` path, and writes JSON plus Markdown reports for latency, throughput, and memory.

## Run A Report

```powershell
.\eng\perf.ps1
```

Default runs write to `artifacts/perf/<timestamp>/performance-report.json` and `artifacts/perf/<timestamp>/performance-report.md`.

The default report shape is:

- 20 warmup requests per scenario.
- 200 measured requests per scenario.
- Concurrency `min(processor count, 8)`.
- Deterministic `/v1/chat/completions` and `/v1/responses` SSE traffic with 64 output chunks per request.

## Record A Baseline

```powershell
.\eng\perf.ps1 -RecordBaseline docs/performance-baselines/windows-x64.json
```

Recording a baseline writes a checked-in JSON file plus a sibling Markdown summary. Use one baseline file per machine class, OS, or CI runner that you want to compare against.

## Compare Against A Baseline

```powershell
.\eng\perf.ps1 -Compare docs/performance-baselines/windows-x64.json
```

Comparison fails the harness when the current release run exceeds these default tolerances for the same scenario set and load shape:

- Request throughput drops by more than 15%.
- P95 latency rises by more than 20% or by more than 1 ms, whichever allows more slack.
- Peak working set rises by more than 20%.
- Peak managed heap rises by more than 20%.
- Allocated bytes per request rises by more than 15%.

You can override those thresholds, plus the request count, concurrency, scenario selection, and chunk count, through `eng/perf.ps1` parameters.

## Reproducibility Notes

- Always use the release harness path. Debug runs are rejected intentionally.
- Compare runs only when request count, concurrency, chunk count, and scenario selection match the baseline.
- Run on the same machine class or CI runner when you want strict comparisons.
- Refresh the baseline when the runtime, SDK, harness scenario shape, or intended performance envelope changes intentionally.

Checked-in baseline guidance lives in `docs/performance-baselines/README.md`.