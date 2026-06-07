# Security Policy

## Supported Versions

| Version | Supported |
| --- | --- |
| 1.x | yes |
| < 1.0.0 | no |

## Reporting a Vulnerability

Please do not report security issues in public GitHub issues, discussions, or pull requests.

When private vulnerability reporting is enabled on the public repository, use GitHub's `Report a vulnerability` flow. If that option is not available, contact the maintainers through a private channel before sharing details publicly.

Include:

- The affected version or commit.
- The platform and terminal environment.
- Clear reproduction steps.
- Sanitized request or response samples only when they are required to explain the issue.

Do not paste raw prompts, exports, persisted session files, credentials, tokens, or other sensitive captures into public reports.

## Security Expectations

LlamaFleece is a local observability and debugging tool. It is not a security boundary, a hardened proxy, or a production reverse proxy.

Redaction is best effort. Prompt content, tool schemas, model names, replayable request bodies held in memory, exported files, and optional persisted session history can still contain sensitive material. Run it only on trusted machines and networks, and review exported or persisted data before sharing it.