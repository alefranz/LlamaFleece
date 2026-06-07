# LlamaFleece

LlamaFleece is a local reverse proxy for LLM traffic with a live terminal UI. Point an existing client at a local port, forward requests upstream, and inspect structured prompts, streamed output, reasoning, tool calls, token usage, timing metadata, and forwarded-request mutations while a session is live.

It is built for local observability and debugging. Run it beside your model client, watch live interactions as they stream, lock onto one request while new traffic keeps flowing, replay the visible interaction against the configured upstream, and export redacted artifacts for later analysis.

It started as a way to debug coding harnesses against models such as Qwen 3.5 and Qwen 3.6, especially when responses stopped abruptly or the observed behavior did not match what the client UI made visible. The same visibility also makes it a practical learning tool for understanding how harnesses use system prompts, tool calling, reasoning, and streamed responses.

## Screenshot

![LlamaFleece showing a captured interaction with structured output and live session stats](docs/screenshots/9.png)

See [docs/screenshots.md](docs/screenshots.md) for additional real terminal captures, including raw mode.

## What It Does

- Track LLM requests on `/v1/chat/completions`, `/v1/completions`, `/v1/responses`, and `/v1/messages`.
- Show structured request previews plus streamed text, reasoning, tool-call, usage, and timing data when the upstream exposes them.
- Keep a request log for all proxied traffic, not only tracked interactions.
- Replay the visible interaction against the configured upstream target.
- Save the visible interaction or the active input or output pane with a chosen file name.
- Export interactions as metadata JSON, readable Markdown, and separate raw request or response text files, or export the full in-memory session as JSON plus Markdown.
- Persist session history locally and restore it on restart.
- Apply the built-in `force_continue` workaround when a supported SSE stream ends without visible content, sending one follow-up continue request so an empty model turn does not stall the harness.

## Supported Traffic

Tracked interactions are created for exact `POST` requests to these endpoints:

- `/v1/chat/completions`
- `/v1/completions`
- `/v1/responses`
- `/v1/messages`

Other proxied traffic remains available in the request log view.

Provider support is capability-based. The matrix below describes the request-preview and streaming shapes that LlamaFleece understands.

| Provider family | Endpoints | Request preview | Text | Reasoning | Tool calls | Usage | Timing metrics | `force_continue` |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| OpenAI-compatible chat/completions SSE | `/v1/chat/completions`, `/v1/completions` | yes | yes | yes | yes | yes | no | yes |
| OpenAI Responses API SSE | `/v1/responses` | yes | yes | yes | yes | yes | no | yes |
| Ollama OpenAI-compatible metadata | `/v1/chat/completions`, `/v1/completions` | yes | yes | no | yes | yes | yes | yes |
| Anthropic Messages SSE | `/v1/messages` | yes | yes | yes | yes | yes | no | yes |

Rich parsing is SSE-first. Unsupported or malformed chunks are forwarded downstream, and LlamaFleece records diagnostics when structured projection falls back.

## Install

Published GitHub Releases include self-contained archives for:

- Windows x64
- Linux x64
- macOS x64
- macOS arm64

Those builds do not require the .NET SDK on the target machine.

For platform-specific install steps, checksum verification, and source-build instructions, see [docs/install.md](docs/install.md).

## Platform and Terminal Requirements

- Run LlamaFleece in an interactive ANSI-capable terminal attached to the process.
- The runtime starts the Spectre.Console live UI immediately and reads keyboard input directly from the console.
- Windows Terminal, PowerShell, Command Prompt, Terminal.app, iTerm2, and mainstream Linux terminal emulators are suitable environments.
- Redirected stdin or stdout, CI logs, detached service managers, and other non-interactive hosts are outside the supported runtime model.
- LlamaFleece does not provide a headless mode.

## Quick Start

If you want a prebuilt binary, start with the install guide above. The commands below cover the source-build workflow.

### Prerequisites

- .NET 10 SDK
- An interactive terminal session attached to a real console or TTY
- An upstream model endpoint that speaks one of the supported API shapes

### Run the proxy

PowerShell example:

```powershell
$env:Proxy__UpstreamUrl = "http://localhost:11434"
# Optional when the upstream expects auth:
# $env:Proxy__UpstreamAuth__Scheme = "Bearer"
# $env:Proxy__UpstreamAuth__Parameter = "your-token"

dotnet run --project .\LlamaFleece
```

LlamaFleece listens on `http://localhost:5000` by default and binds loopback only unless you set `Proxy:ListenHost` to another address. If `Proxy:UpstreamUrl` is not set, it falls back to `http://localhost:8123`.

Startup validates configuration values, but upstream reachability is checked when the first proxied or replayed request is sent.

### Send traffic through it

Point your existing client at `http://localhost:5000` instead of your upstream directly.

The repository also includes two smoke-test scripts under `eng/`:

```powershell
.\eng\test.ps1 -Model your-model-name
.\eng\test-responses.ps1 -BaseUrl http://localhost:5000 -Model your-model-name
```

- `eng/test.ps1` targets `/v1/chat/completions`.
- `eng/test-responses.ps1` targets `/v1/responses`.
- Both scripts accept `-BaseUrl` and `-Model` overrides and write their generated request bodies under `artifacts/smoke-tests/` by default.

## Configuration

LlamaFleece binds settings through the `Proxy` section. Standard ASP.NET Core configuration binding applies, so environment variables such as `Proxy__UpstreamUrl` map to `Proxy:UpstreamUrl`.

For the full reference, precedence rules, environment-variable examples, and file-based samples, see [docs/configuration.md](docs/configuration.md) and [appsettings.example.json](appsettings.example.json).

Configuration precedence is, per key:

1. Command-line arguments
2. Environment variables
3. `appsettings.{Environment}.json`
4. `appsettings.json`
5. Built-in defaults in `ProxyOptions`

### Common settings

| Key | Default | Notes |
| --- | --- | --- |
| `Proxy:UpstreamUrl` | `http://localhost:8123` | Absolute upstream base URL. Must use `http` or `https` and cannot include embedded credentials, a query string, or a fragment. |
| `Proxy:ListenHost` | `localhost` | Local listening host. Must be `localhost` or a literal IPv4 or IPv6 address. |
| `Proxy:ListenPort` | `5000` | Local listening port. |
| `Proxy:Timeouts:TrackedRequestSeconds` | unset | Optional full-lifetime timeout for tracked upstream requests. |
| `Proxy:Timeouts:ShutdownSeconds` | unset | Optional graceful shutdown timeout for the host. |
| `Proxy:UpstreamAuth:Scheme` and `Proxy:UpstreamAuth:Parameter` | unset | Optional injected `Authorization` header. Both values are required together. |
| `Proxy:UpstreamHeaders:*` | unset | Optional static upstream headers applied to tracked and untracked proxied requests. Reserved transport headers are rejected. |
| `Proxy:Persistence:Enabled` | `false` | Enables local session persistence and restore on restart. |
| `Proxy:Persistence:SessionFilePath` | `state/session-history.json` under the app base directory | Optional override for the persisted session file path. Relative paths resolve from the app base directory. |
| `Proxy:Pricing:Default:*` | unset | Optional default token pricing used for session cost estimates. |
| `Proxy:Pricing:Models:<model>:*` | unset | Optional exact-model pricing overrides for session cost estimates. |

Example configuration:

```json
{
  "Proxy": {
    "UpstreamUrl": "http://localhost:11434",
    "ListenHost": "localhost",
    "ListenPort": 5000,
    "Timeouts": {
      "TrackedRequestSeconds": 180,
      "ShutdownSeconds": 10
    },
    "UpstreamAuth": {
      "Scheme": "Bearer",
      "Parameter": "your-token"
    },
    "UpstreamHeaders": {
      "X-Trace-Source": "LlamaFleece"
    },
    "Persistence": {
      "Enabled": true,
      "SessionFilePath": "state/session-history.json"
    },
    "Pricing": {
      "Default": {
        "PromptUsdPer1MTokens": 5.0,
        "CompletionUsdPer1MTokens": 15.0
      }
    }
  }
}
```

You can copy [appsettings.example.json](appsettings.example.json) to `appsettings.json` and adjust only the values you need.

### Network Exposure

`Proxy:ListenHost` defaults to `localhost`, so the proxy only accepts connections from the same machine unless you opt into a non-loopback IP.

If you set `Proxy:ListenHost` to a non-loopback address such as `0.0.0.0`, `::`, or a LAN IP, any client that can reach that port can submit prompts through this proxy. Those interactions can appear in the live TUI, be included in exports or persisted session history, and be replayed from the local console. Any configured `Proxy:UpstreamAuth` or `Proxy:UpstreamHeaders` are applied to forwarded requests, so non-loopback binding effectively shares that upstream access with reachable clients. Use that mode only on trusted networks and with firewall controls in place.

Notes:

- Cost estimates appear only when pricing is configured explicitly.
- Each pricing entry must set prompt and completion rates together.
- Startup status output does not print configured auth or injected header values.

## TUI Controls

| Key | Action |
| --- | --- |
| `Tab` | Cycle the active pane. |
| `Left` / `Right` | Change the visible interaction while the interactions strip is active. |
| `Up` / `Down` | Scroll the active pane. |
| `PageUp` / `PageDown` | Jump between sections in the input or output pane. |
| `Space` | Lock or unlock the visible interaction while new requests continue to arrive. |
| `C` | Jump to the newest interaction. |
| `R` | Toggle raw request and response mode. |
| `F` | Open the interaction filter prompt. |
| `Shift+F` | Clear the active interaction filter. |
| `S` | Open the named save prompt for the visible interaction or the active input or output pane. |
| `P` | Replay the visible interaction against the configured upstream target. |
| `E` | Export the visible interaction. |
| `Shift+E` | Export the full session. |
| `L` | Toggle the request log view for all proxied traffic. |
| `X` | Open the fixes menu. |
| `Enter` | Toggle fullscreen for the active pane. |
| `Esc` | Leave fullscreen, close log mode, or cancel the filter prompt. |
| `Q` | Quit the application. |

## Exports, Persistence, and Redaction

Named saves are written under `exports/saved/interactions`, `exports/saved/input`, or `exports/saved/output` beneath the app base directory.

Saving a whole interaction writes four sibling files under `exports/saved/interactions`:

- A metadata-only `.json`
- A readable `.md`
- A `.request.txt` raw request capture
- A `.response.txt` raw response capture

Timestamped exports are written under `exports/interactions` and `exports/sessions`.

When persistence is enabled, session history is stored in `state/session-history.json` by default and restored on restart.

LlamaFleece applies the same redaction pass to the TUI, raw mode, status messages, request log, exports, and persisted session snapshots. Sensitive query-string values and common secret-bearing fields such as `authorization`, `api_key`, `access_token`, `client_secret`, `password`, `sig`, Bearer or Basic credentials, JWTs, and `sk-*`-style API keys are masked before display or export.

The original request body is kept in volatile memory so replay can resend the captured interaction. It is not written to JSON or Markdown exports and is not written to the optional persisted session-history file.

LlamaFleece retains the live session in memory for the life of the process. If persistence is enabled, that retained snapshot is also written to disk and restored on the next launch. Use bounded local debugging runs for long-lived traffic.

## Troubleshooting

### Requests proxy, but nothing appears in the main interaction strip

Only exact `POST` requests to `/v1/chat/completions`, `/v1/completions`, `/v1/responses`, and `/v1/messages` become tracked interactions in the main UI. Use `L` to inspect all proxied traffic in log mode.

### Startup fails with configuration validation

- `Proxy:UpstreamUrl` must be an absolute `http` or `https` URL.
- Do not embed credentials in `Proxy:UpstreamUrl`. Use `Proxy:UpstreamAuth` or `Proxy:UpstreamHeaders` instead.
- Do not configure both `Proxy:UpstreamAuth` and `Proxy:UpstreamHeaders:Authorization`.
- Reserved transport headers such as `Host` and `Content-Length` cannot be overridden.

### Cost stays `n/a`

Cost estimates are opt-in. Configure pricing under `Proxy:Pricing`, and make sure each entry provides both prompt and completion rates.

### Session history did not restore after restart

Persistence is disabled by default. Set `Proxy:Persistence:Enabled=true` to enable it. Relative session file paths resolve from the application base directory.

### The app exited and the terminal did not explain why

LlamaFleece writes a runtime diagnostics log to `logs/llamafleece-runtime.log` under the application base directory when possible. If that location is not writable, it falls back to the local app-data directory and then the system temp directory. Check that file for startup failures, host lifecycle events, shutdown reasons such as `Ctrl+C` or the TUI `Q` key, and unhandled exceptions that reached the process boundary.

### Structured parsing looks incomplete

LlamaFleece expects SSE `data:` frames for rich projection. If an upstream uses a different framing model or sends malformed chunks, the proxy still forwards the raw output, but the UI may only be able to show partial structure and a diagnostic.

## Development and Testing

Run the `dotnet` commands from the repository root.

```powershell
dotnet restore
dotnet build
dotnet test
.\eng\test.ps1 -Model your-model-name
.\eng\test-responses.ps1 -BaseUrl http://localhost:5000 -Model your-model-name
.\eng\perf.ps1
```

`dotnet test` runs the automated test suite, the smoke-test scripts exercise live chat completions and Responses API flows, and `.\eng\perf.ps1` captures latency, throughput, and memory baselines. See [docs/performance.md](docs/performance.md) for the baseline capture and comparison workflow.