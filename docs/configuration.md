# Configure LlamaFleece

LlamaFleece binds runtime settings through the `Proxy` section in `LlamaFleece/ProxyOptions.cs`.

- Use `Proxy:...` keys in JSON files and command-line arguments.
- Use `Proxy__...` environment variables, where double underscores map to `:`.
- Use [../appsettings.example.json](../appsettings.example.json) as the starting point for a local `appsettings.json`.

## Configuration Sources And Precedence

LlamaFleece relies on the default `WebApplication.CreateBuilder(args)` configuration pipeline and then applies its own legacy-key fallback and code defaults.

For each individual key, the effective value comes from the first source below that supplies it:

1. Command-line arguments such as `--Proxy:ListenPort=5101`
2. Environment variables such as `Proxy__ListenPort=5101`
3. `appsettings.{Environment}.json` when you set `DOTNET_ENVIRONMENT` or `ASPNETCORE_ENVIRONMENT`
4. `appsettings.json`
5. Legacy root keys `TargetUrl` and `Port`, but only when `Proxy:UpstreamUrl` and `Proxy:ListenPort` are still unset
6. Built-in defaults from `ProxyOptions`

Two details matter:

- Precedence is per key, not all-or-nothing. For example, `Proxy:UpstreamUrl` can come from an environment variable while `Proxy:Persistence:Enabled` still comes from `appsettings.json`.
- The legacy root keys are fallback-only. If any configuration provider sets `Proxy:UpstreamUrl` or `Proxy:ListenPort`, the matching root key is ignored.

## Where To Put appsettings.json

- When running from the repo with `dotnet run`, put `appsettings.json` in the repo root.
- When running a published binary, the simplest approach is to place `appsettings.json` next to the executable and launch the app from that directory.
- `Proxy:Persistence:SessionFilePath` is separate from the config-file location. Relative session file paths resolve from the application base directory, not from the working directory.

## Sample appsettings.json

You can copy [../appsettings.example.json](../appsettings.example.json) to `appsettings.json` and change only the values you need.

The sample file is intentionally safe as a starting point: it does not inject auth headers, custom upstream headers, or pricing overrides by default.

Minimal example:

```json
{
  "Proxy": {
    "UpstreamUrl": "http://localhost:11434"
  }
}
```

LlamaFleece validates `Proxy:UpstreamUrl` as an absolute `http` or `https` URL during startup, but it does not make a startup probe to confirm the upstream is reachable. If the URL is valid and the upstream is offline, the app can still start successfully and the first proxied or replayed request reports the connection failure.

## ListenHost And Network Exposure

`Proxy:ListenHost` defaults to `localhost`, so LlamaFleece only accepts connections from the same machine unless you explicitly opt into another address.

Supported values are:

- `localhost` for dual-stack loopback binding where available.
- A literal IPv4 or IPv6 address such as `127.0.0.1`, `::1`, `192.168.1.20`, `0.0.0.0`, or `::`.

Two special opt-in cases matter:

- `0.0.0.0` listens on all IPv4 interfaces.
- `::` listens on all IPv6 interfaces and may also expose the proxy broadly depending on the host networking stack.

If you bind to a non-loopback address, any client that can reach that port can send prompts through this proxy. Those requests can then appear in the live TUI, be included in exports or persisted session history, and be replayed from the local console. Any configured `Proxy:UpstreamAuth` or `Proxy:UpstreamHeaders` are applied to forwarded requests, so non-loopback binding effectively shares that upstream access with reachable clients. Use non-loopback bindings only on trusted networks and keep OS or firewall rules tight.

Full example:

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
      "Parameter": "replace-with-your-token"
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
      },
      "Models": {
        "gpt-4.1": {
          "PromptUsdPer1MTokens": 2.0,
          "CompletionUsdPer1MTokens": 8.0
        }
      }
    }
  }
}
```

## Environment-Variable Examples

PowerShell:

```powershell
$env:Proxy__UpstreamUrl = "http://localhost:11434"
$env:Proxy__ListenHost = "localhost"
$env:Proxy__ListenPort = "5100"
$env:Proxy__Timeouts__TrackedRequestSeconds = "180"
$env:Proxy__UpstreamAuth__Scheme = "Bearer"
$env:Proxy__UpstreamAuth__Parameter = "replace-with-your-token"
$env:Proxy__UpstreamHeaders__X-Trace-Source = "LlamaFleece"
$env:Proxy__Persistence__Enabled = "true"
$env:Proxy__Persistence__SessionFilePath = "state/session-history.json"
$env:Proxy__Pricing__Default__PromptUsdPer1MTokens = "5.0"
$env:Proxy__Pricing__Default__CompletionUsdPer1MTokens = "15.0"

dotnet run --project .\LlamaFleece
```

Bash:

```bash
export Proxy__UpstreamUrl="http://localhost:11434"
export Proxy__ListenHost="localhost"
export Proxy__ListenPort="5100"
export Proxy__Timeouts__TrackedRequestSeconds="180"
export Proxy__UpstreamAuth__Scheme="Bearer"
export Proxy__UpstreamAuth__Parameter="replace-with-your-token"
export Proxy__UpstreamHeaders__X-Trace-Source="LlamaFleece"
export Proxy__Persistence__Enabled="true"
export Proxy__Persistence__SessionFilePath="state/session-history.json"
export Proxy__Pricing__Default__PromptUsdPer1MTokens="5.0"
export Proxy__Pricing__Default__CompletionUsdPer1MTokens="15.0"

./LlamaFleece
```

Examples for dynamic keys:

- `Proxy__UpstreamHeaders__X-Request-Source=LlamaFleece`
- `Proxy__Pricing__Models__gpt-4.1__PromptUsdPer1MTokens=2.0`
- `Proxy__Pricing__Models__gpt-4.1__CompletionUsdPer1MTokens=8.0`

## Command-Line Examples

Source build:

```powershell
dotnet run --project .\LlamaFleece -- `
  --Proxy:UpstreamUrl=http://localhost:11434 `
  --Proxy:ListenHost=localhost `
  --Proxy:ListenPort=5101 `
  --Proxy:Persistence:Enabled=true
```

Published executable:

```powershell
.\LlamaFleece.exe --Proxy:UpstreamUrl=http://localhost:11434 --Proxy:ListenHost=localhost --Proxy:ListenPort=5101
```

Command-line arguments override matching values from environment variables and `appsettings.json`.

## Configuration Reference

| Key | Environment variable | Default | Notes |
| --- | --- | --- | --- |
| `Proxy:UpstreamUrl` | `Proxy__UpstreamUrl` | `http://localhost:8123` | Absolute upstream base URL. Must use `http` or `https` and cannot include embedded credentials, a query string, or a fragment. |
| `Proxy:ListenHost` | `Proxy__ListenHost` | `localhost` | Local listening host. Must be `localhost` or a literal IPv4 or IPv6 address. Use `0.0.0.0` or `::` only when you intentionally want non-loopback exposure. |
| `Proxy:ListenPort` | `Proxy__ListenPort` | `5000` | Local listening port. Must be between `1` and `65535`. |
| `Proxy:Timeouts:TrackedRequestSeconds` | `Proxy__Timeouts__TrackedRequestSeconds` | unset | Optional full-lifetime timeout for tracked upstream requests. Must be greater than zero when set. |
| `Proxy:Timeouts:ShutdownSeconds` | `Proxy__Timeouts__ShutdownSeconds` | unset | Optional graceful shutdown timeout for the host. Must be greater than zero when set. |
| `Proxy:UpstreamAuth:Scheme` and `Proxy:UpstreamAuth:Parameter` | `Proxy__UpstreamAuth__Scheme` and `Proxy__UpstreamAuth__Parameter` | unset | Optional injected `Authorization` header. Both values are required together. `Scheme` cannot contain whitespace. |
| `Proxy:UpstreamHeaders:<header-name>` | `Proxy__UpstreamHeaders__<header-name>` | unset | Optional static upstream headers applied to tracked and untracked proxied requests. Reserved transport headers are rejected. |
| `Proxy:Persistence:Enabled` | `Proxy__Persistence__Enabled` | `false` | Enables local session persistence and restore on restart. |
| `Proxy:Persistence:SessionFilePath` | `Proxy__Persistence__SessionFilePath` | `state/session-history.json` under the app base directory | Optional override for the persisted session file path. Relative paths resolve from the app base directory. |
| `Proxy:Pricing:Default:PromptUsdPer1MTokens` and `Proxy:Pricing:Default:CompletionUsdPer1MTokens` | `Proxy__Pricing__Default__PromptUsdPer1MTokens` and `Proxy__Pricing__Default__CompletionUsdPer1MTokens` | unset | Optional default token pricing used for session cost estimates. Both values must be set together and must be zero or greater. |
| `Proxy:Pricing:Models:<model>:PromptUsdPer1MTokens` and `Proxy:Pricing:Models:<model>:CompletionUsdPer1MTokens` | `Proxy__Pricing__Models__<model>__PromptUsdPer1MTokens` and `Proxy__Pricing__Models__<model>__CompletionUsdPer1MTokens` | unset | Optional exact-model pricing overrides for session cost estimates. Model keys are matched exactly. Both values must be set together. |

## Current Session Retention Limitation

LlamaFleece does not currently expose a `Proxy` setting that caps retained session history. The live session keeps all in-memory interactions, request-log entries, and each interaction's redacted raw request and response buffers until the process exits.

If `Proxy:Persistence:Enabled=true`, that same full retained session snapshot is written to `Proxy:Persistence:SessionFilePath` and restored on the next launch. Treat the current session model as a bounded local debugging run, especially when proxying traffic for long periods.

Legacy compatibility keys:

| Root key | Purpose | Behavior |
| --- | --- | --- |
| `TargetUrl` | Legacy upstream URL key | Used only when `Proxy:UpstreamUrl` is unset across the higher-precedence sources above. |
| `Port` | Legacy listen-port key | Used only when `Proxy:ListenPort` is unset across the higher-precedence sources above. |

## Validation Rules That Commonly Fail Startup

- Do not embed credentials in `Proxy:UpstreamUrl`. Use `Proxy:UpstreamAuth` or `Proxy:UpstreamHeaders` instead.
- Do not include a query string or fragment in `Proxy:UpstreamUrl`.
- `Proxy:ListenHost` must be `localhost` or a literal IPv4 or IPv6 address.
- Do not configure both `Proxy:UpstreamAuth` and `Proxy:UpstreamHeaders:Authorization`.
- Do not override transport headers such as `Host` or `Content-Length` in `Proxy:UpstreamHeaders`.
- Configure pricing in prompt/completion pairs. A single rate on its own is invalid.

Startup status output is intentionally secret-safe: it shows the upstream URL and the count of configured injected headers, but not auth parameters or header values.