# Changelog

All notable changes to LlamaFleece are documented in this file.

The format is based on Keep a Changelog, and release versions follow Semantic Versioning.

## [Unreleased]

No unreleased changes.

## [1.0.0]

### Added

- Initial public release of LlamaFleece as a local reverse proxy with a live terminal UI for LLM traffic observability and debugging.
- Tracked interaction support for OpenAI-compatible chat and completions endpoints, the OpenAI Responses API, and Anthropic Messages.
- Live request previews plus streamed text, reasoning, tool-call, usage, and timing projection when the upstream exposes those signals.
- Interaction replay, request-log inspection, redacted exports, named saves, and optional local session persistence and restore.
- Configuration reference docs, example `appsettings` file, smoke-test scripts, and a performance harness for local comparison workflows.
- Self-contained release packaging for Windows x64, Linux x64, macOS x64, and macOS arm64.

### Supported Platforms

- Published release archives target Windows x64, Linux x64, macOS x64, and macOS arm64.
- Source builds remain available anywhere .NET 10 and an interactive ANSI-capable terminal are supported, but those targets are outside the checked release-artifact matrix.

### Known Limitations

- LlamaFleece requires a real interactive terminal and does not provide a headless mode.
- Rich structured projection is SSE-first; unsupported or malformed stream shapes fall back to raw forwarding with diagnostics where possible.
- Redaction is best effort, and prompts, tool schemas, model names, exports, or persisted session files may still contain sensitive data.
- The project is intended for local observability and debugging, not as a hardened security boundary or production reverse proxy.

### Upgrade and Versioning Notes

- This is the first public release, so there are no upgrade steps from earlier public versions.
- Release versions follow Semantic Versioning from `1.0.0` onward.
- Use prerelease tags such as `v1.0.0-rc.1` when publishing release candidates.