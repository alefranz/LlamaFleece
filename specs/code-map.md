# Code Map

## Top-Level Ownership

| File | Owns | Edit Here When |
| --- | --- | --- |
| `LlamaFleece/Program.cs` | Host startup, Kestrel config, YARP route wiring, friendly startup failure reporting, and the TUI-first runtime lifecycle | changing ports, destinations, startup flow, middleware order, shutdown handling, or the app's TUI-first startup contract |
| `LlamaFleece/ProxyOptions.cs` | Strongly typed proxy configuration binding, validation, and legacy key fallback | changing the runtime configuration surface, defaults, or validation rules |
| `LlamaFleece/UpstreamRequestHeaderInjection.cs` | Shared upstream auth or custom-header injection policy for tracked and YARP-routed traffic, plus secret-safe mutation summaries for those forwarding changes | changing how configured auth or header overrides are materialized onto proxied requests or surfaced safely in UI or export metadata |
| `LlamaFleece/ApplicationShutdownCoordinator.cs` | Shared shutdown signal between keyboard paths and host lifecycle | changing how TUI quit requests or `Ctrl+C` map onto graceful application shutdown |
| `LlamaFleece/InteractionEndpointClassifier.cs` | Exact tracked-endpoint matching and endpoint-kind classification shared by middleware and request payload normalization | changing which routes count as tracked interactions or which endpoint-specific payload policies apply |
| `LlamaFleece/LoggingMiddleware.cs` | Request-body parsing, request logging, and delegation into tracked-request orchestration | changing tracked request capture, parsing request payloads, or shaping the tracked request handoff |
| `LlamaFleece/TrackedRequestCoordinator.cs` | Tracked upstream proxying, one follow-up `force_continue` orchestration pass, response/header merging, and projection of forwarded-request mutation or interaction-diagnostic state into interactions | changing retry or continuation behavior, upstream request forwarding, tracked-response merge semantics, or how forwarded-request mutations or continuation/upstream diagnostics are surfaced |
| `LlamaFleece/TrackedRequestNormalizationPolicy.cs` | Endpoint-aware tracked request normalization rules and normalization-side mutation metadata | changing safe request rewriting before the tracked request is forwarded |
| `LlamaFleece/TrackedRequestContinuationPolicy.cs` | `force_continue` capability detection, continuation payload shaping, and follow-up trigger criteria for empty streamed responses | changing when an empty streamed response should issue a follow-up or how that follow-up request body is built |
| `LlamaFleece/TrackedRequestPayload.cs` | Captured tracked request envelope plus normalized JSON or bytes and forwarded-request mutation state for replay or retry | changing what request state must survive across tracked forwarding, follow-up, or replay |
| `LlamaFleece/InteractionReplayService.cs` | Manual replay of captured interactions against the current upstream target | changing replay trigger behavior, replay status reporting, or how captured requests are resent |
| `LlamaFleece/ProviderCapabilityRegistry.cs` | Declared provider support matrix plus streamed-event family classification | adding or documenting provider families, tracked endpoints, or parser capability declarations |
| `LlamaFleece/ProxyLoggingStream.cs` | SSE line forwarding, usage extraction, raw output capture, typed streamed output assembly, and durable stream-side diagnostics for parse fallbacks or provider-reported failures | changing response parsing, tool-call segment assembly, finish handling, provider-specific stream support, or parser-side diagnostics |
| `LlamaFleece/SessionSummaryService.cs` | Session-wide token, latency, reasoning, and cost-summary calculations | changing summary formulas, latency rollups, or config-driven cost estimation |
| `LlamaFleece/InteractionPersistenceService.cs` | Optional local JSON persistence for session snapshots and restart restore | changing how often session state is saved, the persisted file format, or restore error handling |
| `LlamaFleece/TuiManager.cs` | Thin static facade for the rest of the app to write into the single live TUI subsystem | changing the external TUI API surface, test-state isolation helpers, or lifecycle entry point |
| `LlamaFleece.PerfHarness/` | Release-build performance and load harness plus baseline comparison logic | changing deterministic perf scenarios, baseline metrics, thresholds, or report output |
| `LlamaFleece/Tui/InteractionFilterService.cs` | Structured interaction filter parsing and match evaluation for model, endpoint, status, finish, token, and start-time filters | changing filter syntax, searchable fields, or how filtered interaction lists are derived |
| `LlamaFleece/Tui/InteractionFilterPrompt.cs` | Compact Spectre prompt for entering or editing the active interaction filter | changing the filter prompt UX or the help text shown to users |
| `LlamaFleece/InteractionExportService.cs` | Manual export snapshots and interaction/session artifact emission | changing export formats, export directory layout, or what session data, forwarded-request mutations, diagnostics, or raw request/response files get serialized |
| `LlamaFleece/Tui/` | TUI state, keyboard handling, snapshots, layout rendering, output formatting, fix toggles | changing UI layout, panes, scrolling, stats, fixes, interaction selection, or render behavior |
| `LlamaFleece/Interaction.cs` | Per-interaction state model, including forwarded-request mutation metadata and structured diagnostics carried into UI, export, and persistence | adding fields that need to survive through one tracked request/response lifecycle |
| `LlamaFleece/LlamaFleece.csproj` | Runtime and package dependencies | changing framework version or adding packages |
| `LlamaFleece.Tests/` | Focused regression tests for extracted TUI helpers | adding coverage for formatting, scrolling, and other pure logic |
| `eng/perf.ps1` | Release-build performance harness wrapper | adjusting how reproducible perf baselines are invoked, recorded, or compared |
| `eng/test.ps1` | Chat completions smoke test runner | adjusting the quick local test path or sample request |
| `eng/test-responses.ps1` | Responses API smoke test runner | adjusting the quick local test path or sample request |
| `eng/payloads/test_payload.json` | Chat completions sample streaming payload | adjusting the canned manual request body |
| `eng/payloads/test_responses_payload.json` | Responses API sample streaming payload | adjusting the canned manual request body |

## Runtime Boundaries

### Bootstrap boundary

Owned by `LlamaFleece/Program.cs`, `LlamaFleece/ProxyOptions.cs`, and `LlamaFleece/UpstreamRequestHeaderInjection.cs`.

`LlamaFleece/Program.cs` should stay small. Configuration defaults, validation, and legacy fallback now live in `LlamaFleece/ProxyOptions.cs`, while shared upstream header injection materialization lives in `LlamaFleece/UpstreamRequestHeaderInjection.cs` so both the tracked coordinator and YARP transforms can reuse one policy.

Startup also owns the plain stderr failure path for invalid configuration, listen-bind failures, persisted-session restore failures, and terminal initialization failures so end users get recovery steps without a raw stack trace.

This bootstrap path is intentionally TUI-first: startup owns both the proxy host and the live Spectre session together instead of preserving a parallel headless runtime seam.

### Request intake boundary

Owned by `LlamaFleece/LoggingMiddleware.cs`.

This is the right place for:

- Request filtering.
- Reading JSON payloads.
- Mapping request JSON into interaction input lines.
- Any safe request mutation before tracked-request orchestration begins.

It should stay focused on request capture rather than retry orchestration.

Exact request-path classification is shared with `LlamaFleece/InteractionEndpointClassifier.cs` so payload normalization and middleware tracking follow the same endpoint rules.

### Request/response coordination boundary

Owned by `LlamaFleece/TrackedRequestCoordinator.cs`, `LlamaFleece/TrackedRequestNormalizationPolicy.cs`, `LlamaFleece/TrackedRequestContinuationPolicy.cs`, `LlamaFleece/TrackedRequestPayload.cs`, and `LlamaFleece/InteractionReplayService.cs`.

This is the right place for:

- Preserving normalized request payloads.
- Recording structured forwarded-request mutations for normalization or continuation payload changes.
- Deciding whether `force_continue` should issue a follow-up request and how that follow-up payload is derived.
- Merging multiple upstream attempts into one downstream streamed response.
- Replaying a captured request envelope plus raw body against the current configured upstream target.

If retry, replay, or safe request continuation behavior changes, start here instead of in `LlamaFleece/TuiManager.cs`.

### Response stream boundary

Owned by `LlamaFleece/ProviderCapabilityRegistry.cs` and `LlamaFleece/ProxyLoggingStream.cs`.

This is the only place that currently sees streamed response bytes before they leave the proxy. Any feature that depends on output content, finish reasons, tool calls, or usage data will cross this boundary.

### UI and state boundary

Owned by `LlamaFleece/TuiManager.cs`, `LlamaFleece/Tui/`, `LlamaFleece/Interaction.cs`, and `LlamaFleece/InteractionExportService.cs`.

If a feature changes what the user sees, how interactions are stored, or how the terminal behaves, it will almost certainly touch this boundary.

`LlamaFleece/TuiManager.cs` should be treated as the single live TUI runtime facade. Test helpers may isolate `TuiState`, but production features should not add new runtime-pluggability seams unless the product direction changes.

Secret-safe forwarded-request mutation summaries now cross this boundary too: request-side coordination classes decide what changed, and the UI or export layer decides how to present that already-sanitized metadata.

Manual export format and path decisions should start in `LlamaFleece/InteractionExportService.cs`, with `TuiManager` or `TuiState` only gathering snapshots and surfacing status.

Interaction filtering belongs here too, but the matching logic now lives in `LlamaFleece/Tui/InteractionFilterService.cs` so renderers and keyboard handlers stay thin.

## Where To Start For Common Changes

| Desired change | Primary files |
| --- | --- |
| Change the upstream target, port, timeout, or injected upstream headers | `LlamaFleece/ProxyOptions.cs`, `LlamaFleece/Program.cs`, sometimes `LlamaFleece/UpstreamRequestHeaderInjection.cs` |
| Change session summary formulas or cost estimation | `LlamaFleece/SessionSummaryService.cs`, `LlamaFleece/ProxyOptions.cs`, `LlamaFleece/Tui/TuiRenderer.cs`, `LlamaFleece/InteractionExportService.cs` |
| Change how `Q` or `Ctrl+C` performs shutdown | `LlamaFleece/Program.cs`, `LlamaFleece/ApplicationShutdownCoordinator.cs` |
| Support a new request schema | `LlamaFleece/LoggingMiddleware.cs`, possibly `LlamaFleece/Interaction.cs` |
| Show additional request metadata in the input pane | `LlamaFleece/LoggingMiddleware.cs`, `LlamaFleece/Interaction.cs`, `LlamaFleece/Tui/TuiRenderer.cs` |
| Parse a new streamed response field | `LlamaFleece/ProviderCapabilityRegistry.cs`, `LlamaFleece/ProxyLoggingStream.cs`, sometimes `LlamaFleece/Tui/TuiRenderer.cs` or `LlamaFleece/Tui/TuiOutputFormatter.cs` |
| Change reasoning vs output styling | `LlamaFleece/ProxyLoggingStream.cs`, `LlamaFleece/Tui/TuiOutputFormatter.cs` |
| Change interaction search or filtering behavior | `LlamaFleece/Tui/InteractionFilterService.cs`, `LlamaFleece/TuiManager.cs`, `LlamaFleece/Tui/TuiState.cs`, `LlamaFleece/Tui/TuiRenderer.cs` |
| Add a new pane, shortcut, or display mode | `LlamaFleece/Tui/TuiKeyboardController.cs`, `LlamaFleece/Tui/TuiRenderer.cs`, sometimes `LlamaFleece/Tui/TuiState.cs` |
| Track new token or latency metrics | `LlamaFleece/Interaction.cs`, `LlamaFleece/ProxyLoggingStream.cs`, `LlamaFleece/Tui/TuiState.cs`, `LlamaFleece/Tui/TuiRenderer.cs` |
| Add request/response persistence | `LlamaFleece/InteractionExportService.cs`, `LlamaFleece/InteractionPersistenceService.cs`, `LlamaFleece/TuiManager.cs`, `LlamaFleece/Tui/TuiState.cs` |
| Change the TUI facade surface | `LlamaFleece/TuiManager.cs`, plus whichever `LlamaFleece/Tui/` class owns the behavior |

## Current Hotspots

### `LlamaFleece/Tui/TuiState.cs` and `LlamaFleece/Tui/TuiRenderer.cs`

The TUI hotspot is now split instead of concentrated in one file.

`LlamaFleece/Tui/TuiState.cs` owns global mutable state and snapshot assembly.

`LlamaFleece/Tui/TuiRenderer.cs` owns layout construction, panel selection, and stats presentation.

That is much safer than the old monolith, but most non-trivial UI changes will still cross both files.

### `LlamaFleece/ProxyLoggingStream.cs`

This file still mixes transport concerns with provider-specific protocol parsing, but it now stops at typed output-segment assembly instead of emitting renderer-specific markup.

That is a safer boundary than before, but provider support changes are still likely to land here first because this is where incremental content, reasoning, tool calls, and usage data are normalized.

### `LlamaFleece/LoggingMiddleware.cs`

This file already mixes request logging, request parsing, and request mutation. It is still manageable, but features like replay, retries, or richer request normalization will push it past a reasonable boundary quickly.

## Minimal Mental Model

If you only remember one flow, remember this:

`LlamaFleece/Program.cs` loads and validates `LlamaFleece/ProxyOptions.cs` -> `LlamaFleece/LoggingMiddleware.cs` decides whether to track requests and captures input -> `LlamaFleece/TrackedRequestCoordinator.cs` forwards tracked requests and can issue a follow-up continue attempt -> `LlamaFleece/ProxyLoggingStream.cs` watches streamed output -> `LlamaFleece/TuiManager.cs` forwards UI writes into `LlamaFleece/Tui/` state and rendering components.