# LlamaFleece Specs

This folder is the planning entry point for the project.

Use these docs before making non-trivial changes so you can answer three questions quickly:

- What the application does at runtime.
- Which file or subsystem owns the behavior you want to change.
- Which existing design constraints will shape the implementation.

## Reading Order

1. [architecture.md](architecture.md) for the runtime model and request flow.
2. [code-map.md](code-map.md) for where code sits and which files to edit.
3. [design-decisions.md](design-decisions.md) for the current architectural tradeoffs.
4. [change-planning.md](change-planning.md) for common change paths and refactor guidance.

## Project Snapshot

- Type: .NET 10 console-hosted web proxy with an in-process terminal UI.
- App project: `LlamaFleece/LlamaFleece.csproj`.
- Entry point: `LlamaFleece/Program.cs`.
- Proxy stack: YARP reverse proxy with a single catch-all route.
- UI stack: Spectre.Console live-rendered TUI.
- Current storage model: in-memory by default, with opt-in local JSON session persistence.
- Current validation model: focused xUnit coverage, manual smoke testing via `eng/test.ps1` and `eng/test-responses.ps1`, plus release-only baseline capture through `eng/perf.ps1` and `LlamaFleece.PerfHarness`.

## Scope Of These Docs

These files describe the code as it exists today, including current constraints and de facto design choices. They are not aspirational architecture documents.

When the runtime flow, ownership boundaries, or major implementation tradeoffs change, update the relevant spec file in the same change.