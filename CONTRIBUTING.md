# Contributing to LlamaFleece

LlamaFleece is a local LLM reverse proxy with a live terminal UI. Contributions should stay aligned with that TUI-first, developer-tooling scope.

## Before You Start

- Check existing issues before starting larger work.
- Read [specs/README.md](specs/README.md) before making non-trivial changes so you understand the current runtime model, ownership boundaries, and design constraints.
- Keep each pull request focused. Avoid bundling unrelated refactors into feature or bug-fix work.

## Local Setup

### Prerequisites

- .NET 10 SDK
- PowerShell
- An interactive terminal session
- An upstream model endpoint for manual proxy testing

### Restore, build, and test

Run these commands from the repository root. They resolve through `LlamaFleece.slnx`.

```powershell
dotnet restore
dotnet build
dotnet test
```

### Run the app locally

```powershell
$env:Proxy__UpstreamUrl = "http://localhost:11434"
dotnet run --project .\LlamaFleece
```

LlamaFleece listens on `http://localhost:5000` by default. If your upstream requires authentication, set the corresponding `Proxy__UpstreamAuth__*` environment variables described in [README.md](README.md).

### Run the smoke scripts

With the app running locally, you can exercise the main streaming paths with:

```powershell
.\eng\test.ps1 -Model your-model-name
.\eng\test-responses.ps1 -BaseUrl http://localhost:5000 -Model your-model-name
```

Both scripts accept `-BaseUrl` and `-Model` overrides and write their generated request bodies under `artifacts/smoke-tests/` by default.

### Dry-run release packaging

Before pushing a release tag, run the dry-run packaging script so the staged payload includes the expected docs, sample config, README-linked assets, and per-platform binary names:

```powershell
.\eng\release-dry-run.ps1
```

During local iteration you can narrow the run to one RID, for example:

```powershell
.\eng\release-dry-run.ps1 -RuntimeId win-x64
```

## Test Expectations

- Run `dotnet test` for every code change.
- Add or update automated tests when behavior changes or when fixing a regression.
- If your change touches proxying, streaming projection, request parsing, replay, persistence, export, or TUI behavior, also run the relevant smoke script when practical.
- If you skip a relevant manual or smoke-test step, call that out in the pull request.

## Planning And Docs Expectations

- If your work aligns with an existing issue, link it in the pull request and keep any follow-up scope explicit.
- If your work changes runtime flow, ownership boundaries, or major design tradeoffs, update the relevant file under [specs/](specs/README.md) in the same change.
- If your work changes user-facing behavior, configuration, or developer workflow, update [README.md](README.md) or the relevant docs alongside the code.

## Pull Request Expectations

- Describe what changed and why.
- List the validation you ran.
- Link the related issue or discussion when applicable.
- Include screenshots or terminal captures for TUI changes when they help reviewers understand the result.

By contributing to this project, you agree to follow the expectations in [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).