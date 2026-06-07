# Design Decisions

This document captures the current architectural decisions that matter when planning future work. Some were explicit choices, others are de facto decisions made by the current implementation.

## 1. Single executable is intentionally TUI-first

The proxy server and terminal UI run in the same process.

Headless proxy mode is not a first-class runtime for the current product direction.

Why it helps:

- Easy local setup.
- No IPC layer.
- Low friction for inspecting live traffic.
- Keeps startup, shutdown, and observability responsibilities centered on one local terminal workflow.

Tradeoff:

- UI lifecycle and HTTP lifecycle are coupled.
- Running the proxy without the interactive terminal is not a supported long-term ownership path.

## 2. Reverse proxying is intentionally broad

YARP is configured with one catch-all route and one destination.

Why it helps:

- Keeps the proxy transparent.
- Avoids per-endpoint route maintenance.

Tradeoff:

- Any endpoint-specific behavior must be inferred in middleware.
- Request handling logic depends on string matching against the path.

## 3. Interaction tracking is intentionally selective

Only `POST` requests whose path matches `/v1/chat/completions`, `/v1/completions`, `/v1/responses`, or `/v1/messages` are treated as tracked interactions in the main UI.

Why it helps:

- Focuses the main interface on LLM requests instead of all traffic.
- Keeps the interaction model simple.

Tradeoff:

- Alternate API shapes will be invisible until the explicit classifier is extended.

## 4. A global static TUI facade is intentionally the only live UI runtime

`TuiManager` is still the static singleton-like runtime entry point used by the middleware and response stream. Smaller classes under `LlamaFleece/Tui/` now cover extracted state, keyboard handling, rendering, and formatting logic, and the live app now delegates its frame construction to the extracted renderer while keeping `TuiManager` as the external write surface.

The manager no longer keeps an internal pluggable runtime abstraction for production code. There is one live TUI runtime, and test helpers only swap scoped `TuiState` instances when isolation is needed.

Why it helps:

- Very low ceremony for emitting UI updates from anywhere.
- Easier to refactor and test isolated UI logic without breaking the external write surface.
- Keeps the code aligned with the TUI-first product decision instead of preserving a mostly hypothetical headless/runtime-pluggability seam.

Tradeoff:

- High coupling still exists at the subsystem boundary.
- Concurrency and end-to-end UI behavior still require extra care.
- Moving fully away from global static coordination would still be a larger architectural change.

This is the most important current structural constraint.

## 5. Interaction data is in-memory by default, with explicit export and opt-in persistence

Request and response data still live only in the current process by default, but the TUI can now write explicit export artifacts on demand and can optionally persist one local JSON session snapshot for restart recovery.
User-visible and persisted artifacts now store redacted request targets plus redacted raw request and response text. The original request body is kept only in volatile memory for live-session replay and is not written to export or persistence artifacts.

Why it helps:

- Simple data model.
- No always-on persistence burden during normal runtime.
- Users can capture a single interaction as metadata JSON plus readable and raw text artifacts, or capture the full session as JSON plus Markdown when they need to keep or share state.
- Users who opt in can recover recent interaction history after restarting without introducing a larger storage system.
- Common credentials and API tokens are less likely to leak into the terminal UI, exported artifacts, or persisted session files.

Tradeoff:

- Persistence cadence, restore failure handling, and the local state-file format are now part of the runtime surface and need tests when changed.
- Startup now has to reconcile restore status with the existing in-memory-first TUI flow.
- Replay fidelity for restored history depends on the redacted persisted request body rather than on a separately persisted original secret-bearing payload.

## 6. Request mutation is allowed when it improves observability

The middleware injects `stream_options.include_usage = true` on `/v1/chat/completions` and `/v1/completions` requests when that field is absent.
Configured upstream auth or header overrides can also change the forwarded request before it leaves the process.

Those changes are now treated as explicit, structured interaction metadata instead of as incidental console strings. The live UI, exports, and persisted snapshots only surface secret-safe summaries, never configured auth parameters or header values.
Low-signal normalization such as auto-enabling usage reporting stays visible in forwarded-request metadata and exports, but it is no longer appended to the request input pane or highlighted with the interaction-strip warning badge.

Why it helps:

- Makes token usage visible without requiring every caller to opt in.
- Makes request rewriting auditable in the UI and exports without dumping secret-bearing values.

Tradeoff:

- The proxy is not purely passive.
- Any future request rewriting should be treated as an explicit product choice, not an incidental side effect.
- Each new forwarding change now needs an explicit sanitized summary so the app does not regress into leaking secret values while explaining what changed.

## 7. Stream parsing is still SSE-first, but provider support is now declared explicitly

The response stream logic still expects SSE `data:` frames, but the supported provider families now live in an explicit `ProviderCapabilityRegistry` instead of being discoverable only by reading parser branches.

`ProxyProviderEventParser` is still intentionally one dispatcher over the current OpenAI-compatible, Responses API, and Anthropic shapes. If another distinct streamed response shape is added, split the provider-specific parsing into family handlers behind the existing `ProviderCapabilityRegistry` classification contract instead of extending the shared parser with another large branch.

Why it helps:

- Makes the supported provider families and capabilities auditable in one place.
- Covers OpenAI-compatible chat/completions, Responses API traffic, Ollama timing metadata, and Anthropic Messages SSE.
- Keeps richer metrics and streamed rendering tied to declared protocol families instead of scattered conditionals.

Tradeoff:

- Non-SSE providers, alternative chunk shapes, or incompatible tool-call formats still require parser changes.
- Some provider products still share one event family, so the capability matrix describes what the parser can consume rather than promising every upstream exposes every field.

## 8. Output rendering now uses typed segment lines instead of in-band sentinels

The streamed output pipeline stores typed `OutputSegment` lines for plain text, reasoning, markup, and tool-call fields.

Why it helps:

- Keeps provider protocol assembly in `ProxyLoggingStream` instead of in the renderer.
- Lets `TuiOutputFormatter` focus on styling rather than reconstructing semantics from raw text.
- Makes streamed tool-call updates safe to upsert as arguments arrive incrementally.

Tradeoff:

- The interaction model is a little richer than a plain list of strings.
- Adding new output behaviors now usually means extending the segment model deliberately instead of slipping in another string transform.

The live Spectre renderer also reuses one fixed four-panel layout across normal, log, and fullscreen modes by swapping panel contents instead of re-splitting layouts at runtime.

Why it helps:

- Avoids `Spectre.Console` runtime failures like `Cannot split the same layout twice` during live refresh.
- Keeps mode switches safe under the continuous render loop.
- Keeps console input polling on the same thread as live repainting, which is more reliable for the Windows terminal path used by this project.

Tradeoff:

- Log and fullscreen modes are simulated within the fixed root layout rather than rebuilding a truly different layout tree.
- Prompt-like editing flows should stay in-frame inside the fixed layout, not open a second Spectre prompt from the live loop. The filter and fixes editors now both follow that rule.

## 9. Transport-level recovery and replay live beside tracked request orchestration

The `force_continue` fix still lives behind the same UI toggle, and manual replay now uses the same tracked request transport path. Both behaviors are implemented in request orchestration classes instead of in the renderer or stream parser alone.

Why it helps:

- Turns an empty-response workaround into a real reliability feature.
- Keeps the retry decision at the request/response boundary where the original normalized payload still exists.
- Lets replay use the current upstream configuration without rebuilding requests from formatted UI text.

Tradeoff:

- The behavior currently issues only one follow-up request.
- The follow-up request still depends on the original payload being normalizable into `messages[]`, `prompt`, or Responses API `instructions` and `input` form.
- Manual replay still depends on the interaction having a captured request envelope plus raw request body in memory.

## 10. Validation is now mixed, not fully manual

The repository includes PowerShell smoke tests for chat completions and Responses API flows, a focused xUnit suite, and a release-only performance harness that drives deterministic tracked request load through the proxy pipeline and writes JSON or Markdown baseline reports.

Why it helps:

- Gives the refactored TUI some regression coverage.
- Makes formatter and scrolling changes safer to iterate on.
- Gives release builds a repeatable baseline for latency, throughput, and memory.

Tradeoff:

- Coverage is still narrow.
- Streaming, middleware, and concurrency behavior still rely on targeted harnesses instead of broad always-on test coverage.
- Performance comparisons are intentionally opt-in because they are slower and environment-sensitive.

## 11. Interaction filtering runs on structured state, not rendered text

The interaction list can now be filtered through one compact query prompt, but the matching runs on first-class interaction fields such as request envelope, status code, finish reason, token totals, and start time.

Why it helps:

- Keeps the renderer focused on presentation.
- Avoids brittle matching against Spectre markup or formatted stats strings.
- Lets status code and finish reason become reusable interaction metadata instead of renderer-only details.

Tradeoff:

- The interaction model and snapshots are slightly richer.
- Any new searchable field now needs explicit promotion into interaction state plus filter-service coverage.

## 12. Startup configuration is now centralized and validated once

Proxy startup configuration now binds through one `ProxyOptions` object instead of reading raw root keys in `LlamaFleece/Program.cs`.

Why it helps:

- Keeps the runtime knobs for upstream URL, listen port, shutdown or tracked-request timeouts, and upstream auth or header injection in one place.
- Fails fast on invalid configuration before the web host starts.
- Preserves a small compatibility bridge for existing `TargetUrl` and `Port` root keys.

Tradeoff:

- The runtime now has an explicit configuration shape to maintain instead of a few ad hoc keys.
- URL validation intentionally rejects embedded URL credentials so secrets are not accidentally echoed in startup output.

## 13. Cost estimation is opt-in and configuration-driven

Session exports and the live stats pane can now show estimated session cost, but only from explicit token-rate configuration under `Proxy:Pricing`.

Why it helps:

- Avoids shipping stale or hidden provider pricing assumptions.
- Keeps the estimate auditable because the configured prompt and completion rates are the only inputs.
- Lets mixed-model sessions use model-specific overrides when needed.

Tradeoff:

- Cost remains `n/a` until the user supplies rates.
- Sessions that include billable interactions without matching rates only get a partial estimate.
- Reasoning tokens are summarized separately, but cost estimation still assumes provider-reported completion or output token totals already include any billed reasoning tokens.

## 14. Distribution is application-first, not package-first

The project metadata now treats LlamaFleece as a shipped application with GitHub release artifacts and source builds, not as a NuGet library or `dotnet tool`.

Why it helps:

- Fits the current TUI-first runtime and interactive-terminal requirement.
- Keeps release expectations aligned with OS-specific publish outputs instead of package-manager semantics.
- Makes it explicit that package metadata exists to describe release artifacts and assembly identity, not to encourage `dotnet pack` as a supported install path.

Tradeoff:

- Users still need the .NET 10 SDK until automated release artifacts and install docs are added.
- Cross-platform install UX remains a separate release-engineering task.

Any larger refactor should either add tests or preserve a very deliberate manual verification checklist.