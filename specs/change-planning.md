# Change Planning

This file is meant to answer: if you want to change behavior, where should you start and what is likely to break?

## Planning Heuristic

For almost every change, first classify it into one of these stages:

1. Startup and wiring.
2. Request intake and normalization.
3. Response stream parsing.
4. UI state and rendering.

Once the stage is clear, the entry files are usually obvious.

## Current Backlog-Relevant Areas

The backlog now spans startup configuration, export or replay ideas, additional provider support, and TUI cleanup. The `force_continue` path is still an important coordination boundary to understand before changing runtime flow.

### Changing startup or runtime ownership

Primary files today: `LlamaFleece/Program.cs`, `LlamaFleece/TuiManager.cs`, `LlamaFleece/ApplicationShutdownCoordinator.cs`

Current model:

- The product is intentionally TUI-first.
- `Program.cs` starts the proxy host and the live Spectre session together.
- `TuiManager` owns one live UI runtime, while tests only swap scoped `TuiState` instances for isolation.

Planning implication:

If future work changes startup or lifecycle ownership, default to simplifying around the existing TUI-first contract instead of introducing a headless/runtime-pluggability seam. Treat a real headless mode as a separate product decision, not a cleanup task.

### Adjusting the real `force_continue` flow

Primary files today: `LlamaFleece/LoggingMiddleware.cs`, `LlamaFleece/TrackedRequestCoordinator.cs`, `LlamaFleece/TrackedRequestContinuationPolicy.cs`, `LlamaFleece/TrackedRequestNormalizationPolicy.cs`, `LlamaFleece/TrackedRequestPayload.cs`, `LlamaFleece/ProxyLoggingStream.cs`

What the current code can do:

- Detect that a streamed response completed with no content.
- Reuse the normalized original request payload to issue one follow-up continue request.
- Keep request normalization and continuation payload shaping behind dedicated tracked-request policies instead of embedding them inside the payload container.
- Merge the follow-up stream back into the same client response and tracked interaction.

What still constrains the implementation:

- Continuation depends on the original request being expressible as `messages[]`, `prompt`, or Responses API `instructions` plus `input`.
- The coordinator intentionally stops after one follow-up attempt.

Planning implication:

If `force_continue` behavior changes again, keep the trigger decision in `LlamaFleece/TrackedRequestCoordinator.cs` or `LlamaFleece/TrackedRequestContinuationPolicy.cs`, and keep request-body rewriting in the tracked-request policy classes rather than pushing it back into TUI state or raw stream parsing.

### Fixing malformed or raw tool-call output in the output pane

Primary files: `LlamaFleece/ProxyLoggingStream.cs`, `LlamaFleece/Tui/TuiOutputFormatter.cs`, `LlamaFleece/Tui/TuiRenderer.cs`

Current model:

- `ProxyLoggingStream` assembles incremental tool-call chunks into typed `OutputSegment` lines before they reach the renderer.
- `LlamaFleece/Tui/TuiOutputFormatter.cs` styles those segment kinds.
- `LlamaFleece/Tui/TuiRenderer.cs` only windows and renders segment lists.

Planning implication:

If tool-call display changes again, prefer extending the segment model or `StreamedToolCallAssembler` instead of reintroducing renderer-side string reconstruction.

## TUI Change Entry Points

The oversized `LlamaFleece/TuiManager.cs` refactor is complete. Future TUI work should start in the owning component instead of growing the facade again.

- `LlamaFleece/Tui/TuiState.cs` for interaction mutation, scrolling state, and snapshot assembly.
- `LlamaFleece/Tui/TuiKeyboardController.cs` for key handling and pane navigation.
- `LlamaFleece/Tui/TuiRenderer.cs` for Spectre layout construction and stats presentation.
- `LlamaFleece/Tui/TuiOutputFormatter.cs` for reasoning markup and other output-line transformations.
- `LlamaFleece/TuiManager.cs` only when the public static write surface needs to change.

When a TUI change adds or reshapes state, keyboard handling, rendering, or formatting behavior, keep that logic in the owning `LlamaFleece/Tui/` component and add or extend focused tests for that component instead of growing `TuiManager`.

## Common Change Scenarios

### Add support for another provider response shape

Start in `LlamaFleece/ProviderCapabilityRegistry.cs`, then move to `LlamaFleece/ProxyLoggingStream.cs`.

Questions to answer first:

- Does the new provider need a newly tracked endpoint in `LlamaFleece/InteractionEndpointClassifier.cs`?
- Does request preview need request-schema support in `LlamaFleece/LoggingMiddleware.cs`?
- Is the provider still using SSE `data:` frames?
- Does it stream text in `choices[0].delta`, another field, or a different envelope entirely?
- Are tool calls incremental or complete objects?
- If tool calls are incremental, can they be normalized through `StreamedToolCallAssembler`, or does the segment model need a new kind?
- Can usage and timing arrive mid-stream or only at the end?

Planning implication:

Declare the provider family and capabilities in the registry first, then extend parser branches or request handling as needed instead of adding another anonymous shape check in the stream loop.

### Add richer request-side metadata to the UI

Start in `LlamaFleece/LoggingMiddleware.cs`, then update `LlamaFleece/Interaction.cs` and `LlamaFleece/Tui/TuiRenderer.cs` if the metadata needs persistent structured state instead of one-off lines.

### Add or adjust interaction search or filtering

Primary files: `LlamaFleece/Tui/InteractionFilterService.cs`, `LlamaFleece/Tui/InteractionFilterPrompt.cs`, `LlamaFleece/TuiManager.cs`, `LlamaFleece/Tui/TuiState.cs`, `LlamaFleece/Tui/TuiRenderer.cs`

Current model:

- `InteractionFilterService` parses the compact filter query and matches against first-class interaction fields.
- `TuiManager` and `TuiState` keep the authoritative interaction list plus one active filter and derive a filtered visible list for snapshots.
- `TuiRenderer` only shows the filtered count, active filter summary, and the already-filtered interactions.

Planning implication:

If you add another searchable field, promote it into `Interaction` first, then extend `InteractionFilterService` and snapshot or renderer plumbing instead of scraping formatted panel text.

### Change panes, scrolling, or fullscreen behavior

Start in `LlamaFleece/Tui/TuiKeyboardController.cs` and `LlamaFleece/Tui/TuiRenderer.cs`.

Questions to answer first:

- Is the behavior purely visual, or does it need new persisted interaction state?
- Does section navigation need `LlamaFleece/Tui/TuiSectionNavigator.cs` updates as well?
- Should the change remain inside the TUI subsystem, or does the public `TuiManager` facade need to grow?

### Add persistence or export

Do not start in the renderer.

Start in `LlamaFleece/InteractionExportService.cs` and `LlamaFleece/InteractionPersistenceService.cs`.

That service owns:

- Snapshot-friendly export records derived from `Interaction` state.
- Session export shaping, including request log entries and aggregate totals.
- JSON and Markdown artifact emission under the runtime `exports/` directory.

The persistence service owns:

- The single JSON state file used for restart recovery.
- Restoring persisted snapshots back into in-memory interaction state.
- Local write cadence and atomic file replacement for session saves.

Only after the export shape is correct should you wire shortcuts or status messages into `LlamaFleece/TuiManager.cs` or `LlamaFleece/Tui/TuiState.cs`. Keep `LlamaFleece/Tui/TuiRenderer.cs` limited to surfacing export status.

### Change session summary or estimated cost behavior

Primary files: `LlamaFleece/SessionSummaryService.cs`, `LlamaFleece/ProxyOptions.cs`, `LlamaFleece/Tui/TuiRenderer.cs`, `LlamaFleece/InteractionExportService.cs`

Current model:

- `SessionSummaryService` computes one session-wide summary from in-memory interactions plus session timing markers.
- `ProxyOptions` owns the explicit token-rate configuration surface used for cost estimation.
- `TuiRenderer` only formats the precomputed summary for the stats pane.
- `InteractionExportService` serializes the same summary into session JSON and Markdown exports.

Planning implication:

If you change summary formulas or cost assumptions, do it in `SessionSummaryService` first and keep the renderer and export output as formatting-only consumers of that model.

### Add more fixes or workarounds

The current toggle registry lives in `LlamaFleece/Tui/TuiState.cs`, but behavior may belong elsewhere.

Use this rule:

- UI-only toggles can stay near `LlamaFleece/Tui/TuiState.cs`.
- Request mutation fixes belong near `LoggingMiddleware`.
- Stream interpretation fixes belong near `ProxyLoggingStream`.
- Retry or replay fixes need a new orchestration boundary.

## Risk Areas

### Concurrency

State writes are lock-protected, but the system still depends on a single global mutable manager. Any change that increases write frequency or cross-interaction coordination should be treated carefully.

### Protocol assumptions

The parser is narrow by design. Small provider differences can still degrade structured projection, but parse fallbacks are now recorded as durable interaction diagnostics instead of failing silently.

### Rendering coupling

Output styling currently depends on parser-emitted markers and string transforms. Changes in one side can degrade the other without compile-time help.

## Practical Workflow For Future Changes

1. Read `architecture.md` to locate the change stage.
2. Read `code-map.md` to identify the owning files.
3. Read `design-decisions.md` to see whether the change crosses an intentional boundary.
4. Check open issues or your local planning notes to confirm whether the work is new, partial, or already tracked.
5. Validate behavior with `dotnet test` plus `eng/test.ps1` or `eng/test-responses.ps1` when the change crosses UI and streaming boundaries.