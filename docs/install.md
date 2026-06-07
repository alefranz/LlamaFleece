# Install LlamaFleece

LlamaFleece ships as an application, not as a NuGet package or `dotnet tool`.

Every tagged GitHub release publishes self-contained archives for the main desktop platforms. Those builds do not require the .NET SDK on the target machine.

LlamaFleece can be configured with `appsettings.json`, environment variables, or command-line arguments. See [configuration.md](configuration.md) for configuration samples and precedence rules.

## Release assets

Download the matching archive from the [GitHub Releases page](https://github.com/alefranz/LlamaFleece/releases).

Stable releases use tags such as `v0.2.0`. Pre-releases append a suffix such as `-preview.1` or `-rc.1`, and release asset names include that full tag.

| Platform | Release asset pattern | Notes |
| --- | --- | --- |
| Windows x64 | `LlamaFleece-vX.Y.Z[-suffix]-win-x64.zip` | Extract and run `LlamaFleece.exe`. |
| macOS Apple Silicon | `LlamaFleece-vX.Y.Z[-suffix]-macos-arm64.tar.gz` | Extract and run `./LlamaFleece`. |
| macOS Intel | `LlamaFleece-vX.Y.Z[-suffix]-macos-x64.tar.gz` | Extract and run `./LlamaFleece`. |
| Linux x64 | `LlamaFleece-vX.Y.Z[-suffix]-linux-x64.tar.gz` | Extract and run `./LlamaFleece`. |

Each release also includes `SHA256SUMS.txt` so you can verify the downloaded archive before running it.

## Runtime diagnostics log

The self-contained single-file build does not move the runtime diagnostics log into a hidden extraction cache. When the extracted release folder is writable, LlamaFleece writes the log to `logs/llamafleece-runtime.log` under the application base directory, alongside the executable you launched.

If that location is not writable, LlamaFleece falls back to a per-user local app-data location and then the system temp directory.

- Windows fallback: `%LOCALAPPDATA%\LlamaFleece\logs\llamafleece-runtime.log`
- Final fallback: the system temp directory under `LlamaFleece/logs/`

Check that file for startup failures, shutdown reasons, host lifecycle events, and unhandled exceptions that reached the process boundary.

## Platform and terminal support

Use the published archives above when you want the project's current first-class runtime targets.

| Area | Current expectation |
| --- | --- |
| Published release targets | Windows x64, Linux x64, macOS x64, and macOS arm64. |
| Other runtime identifiers | Source builds may work on other .NET 10-compatible RIDs, but those combinations are not published release assets or documented support targets today. |
| Terminal requirement | Run in an interactive ANSI-capable terminal attached to the process. Startup always enters the live Spectre.Console UI, and the keyboard path reads directly from the console. |
| Expected host type | Normal desktop or server shells such as Windows Terminal, PowerShell, Command Prompt, Terminal.app, iTerm2, or a TTY-backed Linux terminal emulator. |
| Unsupported host type | Redirected stdin or stdout, CI logs, detached services or daemons, and other non-interactive launches are outside the supported runtime contract. |
| Headless mode | Unsupported today. There is no startup switch that disables the TUI and keeps only the proxy host. |

### Windows

```powershell
Expand-Archive .\LlamaFleece-vX.Y.Z-win-x64.zip -DestinationPath .
Set-Location .\LlamaFleece-vX.Y.Z-win-x64
$env:Proxy__UpstreamUrl = "http://localhost:11434"
.\LlamaFleece.exe
```

Optional checksum verification:

```powershell
Get-FileHash .\LlamaFleece-vX.Y.Z-win-x64.zip -Algorithm SHA256
```

### macOS

Use the archive that matches your CPU: `macos-arm64` for Apple Silicon or `macos-x64` for Intel.

```bash
tar -xzf LlamaFleece-vX.Y.Z-macos-arm64.tar.gz
cd LlamaFleece-vX.Y.Z-macos-arm64
export Proxy__UpstreamUrl="http://localhost:11434"
./LlamaFleece
```

Optional checksum verification:

```bash
shasum -a 256 LlamaFleece-vX.Y.Z-macos-arm64.tar.gz
```

If macOS blocks the downloaded binary because it is quarantined, clear the quarantine flag once after extraction:

```bash
xattr -d com.apple.quarantine ./LlamaFleece
```

### Linux

```bash
tar -xzf LlamaFleece-vX.Y.Z-linux-x64.tar.gz
cd LlamaFleece-vX.Y.Z-linux-x64
export Proxy__UpstreamUrl="http://localhost:11434"
./LlamaFleece
```

Optional checksum verification:

```bash
sha256sum LlamaFleece-vX.Y.Z-linux-x64.tar.gz
```

## Build from source

Use the source-build path when you want to run from a checkout, target a runtime that is not published on GitHub Releases, or modify the code locally.

Prerequisites:

- .NET 10 SDK
- An interactive terminal session attached to a real console or TTY

Run directly from the repo:

```powershell
$env:Proxy__UpstreamUrl = "http://localhost:11434"
dotnet run --project .\LlamaFleece
```

If you prefer file-based configuration, create `appsettings.json` in the directory you launch from by starting with [../appsettings.example.json](../appsettings.example.json).

Create your own self-contained publish output:

```powershell
dotnet publish .\LlamaFleece\LlamaFleece.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

Change `--runtime` to the RID you need, for example `linux-x64`, `osx-x64`, or `osx-arm64`.