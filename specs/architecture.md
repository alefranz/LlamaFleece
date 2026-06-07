# Architecture

## Purpose

LlamaFleece is a local HTTP reverse proxy for LLM traffic that adds a live terminal UI for observing prompts, streamed responses, token usage, and request metadata while forwarding requests upstream.

The project is optimized for local observability, not for running as a general-purpose production proxy.
Headless proxy mode is not a first-class runtime in the current architecture.

## Runtime Topology

The process hosts two concerns in one executable:

1. An ASP.NET Core web app listening on a configurable port.
2. A background Spectre.Console TUI that renders interaction state in real time.

That TUI is an intentional part of the runtime, not an optional shell around a separately owned headless proxy core.

At startup:

1. `LlamaFleece/Program.cs` creates the web host.
2. Startup binds and validates one `ProxyOptions` object before the app is built.
3. Kestrel listens on `Proxy:ListenPort`, default `5000`, with legacy root key `Port` still accepted as a fallback.
4. YARP is configured with a single catch-all route and one destination.
5. `LoggingMiddleware` is inserted before the reverse proxy.
6. The web host and the TUI are started concurrently, with the TUI remaining on the main thread and the server running asynchronously alongside it.
7. When session persistence is enabled, startup attempts to restore the last persisted interaction-history snapshot before new requests arrive.
8. Requests are proxied until shutdown requested from `Ctrl+C` or the TUI `Q` key.

## Runtime Environment Expectations

The current runtime assumes one process with a real console attached.

- `LlamaFleece/Program.cs` always starts `TuiManager.RunAsync(...)` alongside the web host; there is no alternate startup branch that omits the TUI.
- `LlamaFleece/TuiManager.cs` always enters `AnsiConsole.Live(...)` for rendering.
- `LlamaFleece/Tui/TuiKeyboardController.cs` polls `Console.KeyAvailable`, reads keys via `Console.ReadKey(...)`, and uses `AnsiConsole.Prompt(...)` for interactive commands.
- Because there is no redirected-I/O or service-host fallback, an interactive ANSI-capable terminal is part of the runtime contract.

Headless or detached execution is therefore not a supported architecture path today, even if the HTTP proxy happens to keep forwarding traffic in some non-interactive environments.

## External Dependencies

- `Yarp.ReverseProxy`: forwards the HTTP traffic upstream.
- `Spectre.Console`: renders the interactive terminal UI.

## Configuration Surface

Runtime configuration now binds through a strongly typed `ProxyOptions` object.

Primary keys:

- `Proxy:UpstreamUrl`: absolute upstream base URL. Default `http://localhost:8123`.
- `Proxy:ListenPort`: local listening port. Default `5000`.
- `Proxy:Timeouts:TrackedRequestSeconds`: optional full lifetime timeout for tracked upstream requests handled by `TrackedRequestCoordinator`.
- `Proxy:Timeouts:ShutdownSeconds`: optional ASP.NET Core host shutdown timeout.
- `Proxy:UpstreamAuth:Scheme` and `Proxy:UpstreamAuth:Parameter`: optional injected `Authorization` header for upstream requests.
- `Proxy:UpstreamHeaders:*`: optional static upstream header overrides applied to tracked and untracked proxied requests.
- `Proxy:Pricing:Default:PromptUsdPer1MTokens` and `Proxy:Pricing:Default:CompletionUsdPer1MTokens`: optional default token-pricing pair used for session cost estimates.
- `Proxy:Pricing:Models:<model>:PromptUsdPer1MTokens` and `Proxy:Pricing:Models:<model>:CompletionUsdPer1MTokens`: optional per-model token-pricing overrides used when interaction `model` names match exactly.
- `Proxy:Persistence:Enabled`: optional opt-in toggle for local session-history persistence.
- `Proxy:Persistence:SessionFilePath`: optional JSON file path for persisted session state. Relative paths resolve from the application base directory and default to `state/session-history.json` when omitted.

Compatibility behavior:

- Legacy root keys `TargetUrl` and `Port` still work when the `Proxy` section omits those values.
- Startup validation rejects invalid ports, invalid or non-HTTP upstream URLs, embedded URL credentials, reserved injected headers, and conflicting authorization settings.
- Pricing validation requires explicit prompt and completion rates together for each configured pricing entry. The app ships no built-in provider pricing.
- Startup status output never prints configured auth or header values; it only prints the upstream URL and the count of injected headers when any are configured.

## Request Lifecycle

### 1. Incoming request reaches Kestrel

All paths match the single YARP route. The application does not define dedicated controller endpoints.

### 2. `LoggingMiddleware` classifies the request

The middleware treats a request as an interaction worth parsing when both are true:

- The method is `POST`.
- The path matches one of the supported interaction endpoints exactly: `/v1/chat/completions`, `/v1/completions`, `/v1/responses`, or `/v1/messages`.

All proxied requests can still appear in the log view, but only those exact tracked interaction endpoints become interaction sessions in the main TUI.

### 3. Request body is buffered and parsed

For tracked interactions, the middleware:

- Creates a new session in `TuiManager`.
- Captures the original request envelope on the interaction, including method, path, query string, and content type.
- Reads the request JSON, stores a redacted raw copy for the TUI, export, and persistence path, and keeps the original request body only in volatile memory for live-session replay.
- Extracts `model` when present.
- Extracts `messages[]`, `prompt`, Anthropic `system`, or Responses API `instructions` plus `input` content and formats them for the input pane.
- Preserves a redacted raw request body even when structured preview extraction fails, and appends a visible warning to the interaction input plus request log so the missing structured preview is explained.
- Builds a normalized request payload that adds `stream_options.include_usage = true` on `/v1/chat/completions` and `/v1/completions` requests when missing.
- Records explicit forwarded-request mutation metadata whenever body normalization or later forwarding policy changes what leaves the process.

That normalized payload is preserved for the rest of the tracked request lifecycle so the app can issue one follow-up `force_continue` request without rebuilding prompt state from the UI.

### 4. Tracked requests are coordinated before YARP

For tracked interactions, the middleware delegates to `TrackedRequestCoordinator` instead of letting YARP handle the request directly.

That coordinator owns:

- Forwarding the normalized original request upstream.
- Reusing the normalized payload to issue a single follow-up `force_continue` request when the first streamed attempt returns no content.
- Merging the continued stream back into the same downstream response and tracked interaction.
- Applying configured upstream auth or header injection before the tracked request leaves the process.
- Appending secret-safe forwarded-request mutation summaries to the tracked interaction when normalization, header injection, or follow-up continuation changes the forwarded request.
- Recording durable interaction diagnostics for continuation attempts or outcomes and tracked upstream transport failures.

The same transport boundary is also reused for manual replay from the TUI. `InteractionReplayService` creates a fresh interaction session from the captured request envelope plus raw request body, then asks `TrackedRequestCoordinator` to resend that request against the app's current upstream target.

YARP still handles untracked requests through the catch-all route, but it now applies the same configured upstream auth or header injection through request transforms so tracked and untracked traffic share one upstream credential/header policy.

### 5. Upstream SSE lines are decoded and interpreted

`ProxyLoggingStream`:

- Streams SSE lines to the real response body.
- Stores a redacted raw output copy for raw-mode viewing, export, and persistence.
- Interprets only SSE-style `data: ...` messages for TUI state.
- Defers forwarding `[DONE]` until the coordinator decides whether the interaction is finished or needs a follow-up request.

Provider support is now declared explicitly in `LlamaFleece/ProviderCapabilityRegistry.cs` instead of being inferred only from parser branches.

| Provider family | Endpoints | Request preview | Text | Reasoning | Tool calls | Usage | Timing metrics | Force continue |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| OpenAI-compatible chat/completions | `/v1/chat/completions`, `/v1/completions` | yes | yes | yes | yes | yes | no | yes |
| OpenAI Responses API | `/v1/responses` | yes | yes | yes | yes | yes | no | yes |
| Ollama OpenAI-compatible metadata | `/v1/chat/completions`, `/v1/completions` | yes | yes | no | yes | yes | yes | yes |
| Anthropic Messages | `/v1/messages` | yes | yes | yes | yes | yes | no | yes |

Within parsed JSON chunks it currently understands:

- `usage` blocks for token counts.
- OpenAI-style `choices[].delta.content` across all streamed choices.
- OpenAI-style `choices[].delta.reasoning_content` across all streamed choices.
- OpenAI-style `choices[].delta.tool_calls[*].function` across all streamed choices.
- Responses API `response.output_text.delta` and final assistant `response.output_item.done` message items.
- Responses API reasoning deltas such as `response.reasoning_text.delta` and `response.reasoning_summary_text.delta`.
- Responses API tool-call deltas such as `response.function_call_arguments.delta`, `response.custom_tool_call_input.delta`, and final tool items from `response.output_item.done` or `response.completed.response.output`.
- Responses API `response.completed` usage blocks.
- Ollama timing fields such as `prompt_eval_duration`, `eval_duration`, and `total_duration`.
- Anthropic `message_start`, `message_delta`, and `message_stop` events for usage and finish reasons.
- Anthropic `content_block_delta` text and thinking deltas.
- Anthropic `content_block_start` and `content_block_delta` tool-use payloads.

When streamed JSON parsing falls back because an SSE `data:` payload is malformed, the parser now records a structured interaction diagnostic and keeps forwarding the raw stream instead of failing silently.

When `[DONE]` arrives on an empty streamed response and `force_continue` is enabled, the coordinator can issue one follow-up continue request and emit a single merged completion event to the caller.

## UI State Model

`TuiManager` is the central in-memory state store and renderer coordinator.

It now owns one live TUI runtime directly. Test helpers can still swap scoped `TuiState` instances for isolation, but production code no longer routes through an internal pluggable runtime abstraction.

Each tracked request becomes one `Interaction` object containing:

- Rendered input lines.
- Rendered output lines.
- Redacted raw request and raw response text used by the TUI, exports, and persistence, plus a live-session-only original request body retained in volatile memory for manual replay.
- Structured, secret-safe forwarded-request mutation summaries for request normalization, configured header injection, and any follow-up continuation request that was actually sent.
- Structured diagnostics for parse fallbacks, continuation attempts or outcomes, and upstream failures, carried through live state, export, and persistence.
- Token counts, timing metadata, upstream response status code, and finish reason.
- Section boundaries used for page navigation.
- Pane scroll offsets.
- Streaming state and model name.

The TUI also keeps one active interaction filter in memory. That filter is parsed into structured predicates instead of matching against rendered panel text, so model, endpoint, status code, finish reason, token counts, and start-time filters all run against first-class interaction fields.

Session-level summaries now sit beside the per-interaction view:

- `LlamaFleece/SessionSummaryService.cs` aggregates prompt, completion, cached, reasoning, and total tokens across the in-memory session.
- The same summary derives session latency rollups from per-interaction timestamps, including overall active span, average time-to-first-token, average wall-clock duration, and average API-reported total duration when available.
- Estimated cost is only shown when pricing is configured explicitly. If some billable interactions do not have matching rates, the app shows a partial estimate instead of pretending it knows the missing cost.

Manual export now sits beside that live state model:

- `P` replays the currently visible interaction against the current configured upstream target.
- `E` exports the currently visible interaction.
- `Shift+E` exports the full in-memory session.
- `S` opens a compact search or filter prompt for interactions.
- `Shift+S` clears the active interaction filter.
- `LlamaFleece/InteractionExportService.cs` owns file-format shaping and filesystem writes.
- `LlamaFleece/InteractionPersistenceService.cs` owns JSON session-state reads and writes for restart recovery.
- `LlamaFleece/InteractionReplayService.cs` owns replay orchestration and status reporting.
- Session exports now include the same structured session summary shown in the stats pane: tokens, latency rollups, reasoning tokens, and cost estimate availability.
- `LlamaFleece/Tui/InteractionFilterService.cs` owns filter-query parsing and match evaluation.
- Exports are written under `exports/` beneath the application base directory, with separate `interactions/` and `sessions/` subfolders.
- Session exports still write one machine-readable JSON artifact and one human-readable Markdown artifact. Interaction exports now write a metadata-only JSON artifact, a readable Markdown artifact, and separate raw request and raw response text artifacts.
- Interaction exports, session exports, and optional persisted session snapshots all use the same redacted interaction snapshot shape for request targets, logs, diagnostics, and raw request or response text, but only the interaction export bundle splits the raw request and response into separate sibling files.
- Interaction exports and persisted session snapshots include the same structured forwarded-request mutation list shown in the live UI.
- When persistence is enabled, the in-memory session snapshot is also serialized to one local JSON file often enough for restart recovery, using the same interaction and session snapshot records as manual export.

The UI has four main views:

1. Interactions strip.
2. Input pane.
3. Output pane.
4. Stats pane.

There are also two modal behaviors:

- Fullscreen mode for the active pane.
- Log mode for all proxied requests, not just tracked LLM interactions.

## Rendering Model

The TUI uses a snapshot pattern:

1. Writers mutate shared state under a lock.
2. The live render loop takes a snapshot under that lock.
3. Layout building happens outside the lock.

This reduces time spent blocking request/stream updates during rendering.

The renderer does not own export formatting or persistence. It only shows the latest export or persistence success or failure message in the stats pane.

The renderer also no longer calculates session summary metrics ad hoc. It renders a precomputed session-summary snapshot so runtime stats and exports share one calculation path.

The renderer also does not parse filter expressions or decide which interactions match. It only renders the already-filtered interaction list plus the active filter summary and match counts in the stats or interactions headers.

Keyboard polling now also happens inside that same live render loop instead of on a separate console-reading thread. Keeping console reads and Spectre live writes on one thread avoids stale repaints and missed mode transitions that can happen when terminal input and output race each other on Windows.

## Shutdown Model

`LlamaFleece/Program.cs` configures a shared shutdown callback through `LlamaFleece/ApplicationShutdownCoordinator.cs`.

`Ctrl+C` and the TUI `Q` key both flow through that same callback. The callback:

- Requests ASP.NET Core host shutdown so Kestrel stops accepting new requests.
- Leaves in-flight proxy work to complete under normal host shutdown semantics.
- Cancels the TUI loop so the Spectre live session exits normally and restores the terminal.

`Program.cs` also writes a small runtime diagnostics file so unexpected host stops, explicit shutdown reasons, and process-boundary exceptions survive after the terminal session disappears. The default target is `logs/llamafleece-runtime.log` under the app base directory, with local-app-data and temp-directory fallbacks when that path is not writable.

There is no dedicated hosted service for the TUI, but `LlamaFleece/Program.cs` now coordinates the web-host task and the TUI task together so the terminal UI owns the main thread while the server runs concurrently.

## Architectural Constraints

- The app is single-process and heavily in-memory.
- The app is intentionally TUI-first; there is no first-class headless runtime path to preserve in normal feature work.
- Persisted history is local-file based and opt-in. Restore and save decisions stay in the export or persistence layer instead of in the renderer.
- Cross-cutting state is centralized in static members on `TuiManager`.
- Response parsing assumes SSE framing and specific OpenAI/Ollama payload shapes.
- The current design is strong for local debugging but not yet structured for automated testing or provider abstraction.