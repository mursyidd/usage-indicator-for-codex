# Security Policy

## Reporting a vulnerability

Use GitHub's private **Report a vulnerability** flow in the repository's Security tab when it is available. Include the affected version or commit, reproduction steps, impact, and the smallest safe diagnostic output needed to confirm the issue.

Do not place credentials, tokens, cookies, account identifiers, private paths, raw app-server responses, or other sensitive data in a public issue. If private vulnerability reporting is not available, open a public issue containing no sensitive details and ask the maintainer to establish a private reporting channel.

There is no guaranteed response or remediation timeline until the repository owner publishes one.

## Credential-handling boundary

Codex Usage Indicator relies on the authentication already managed by a separately installed local Codex CLI. The companion starts only the configured `.cmd` launcher with `app-server --stdio` and sends:

- `initialize`;
- `account/read` with `refreshToken: false`; and
- `account/rateLimits/read`.

It does not read credential files, browser profiles, tokens, or Codex Desktop package files. It does not perform login, logout, token refresh, authentication changes, model requests, thread or turn requests, CLI installation or update, or Codex Desktop modification.

An account value returned by the CLI is transformed into a deterministic, truncated SHA-256 fingerprint used only in memory to keep the account and rate-limit response scoped together. Because predictable identities may be guessable, the fingerprint must still be treated as account-derived data. The fingerprint, account identity, tokens, and usage values are not written to settings or logs.

Reports that require weakening these boundaries, collecting raw credentials, or publishing authenticated responses must be rejected or redesigned around synthetic data.

## Supported security scope

Security fixes are evaluated against the current source and the framework-dependent Windows x64 release described in the README. Dependency and platform support also end when their upstream support ends.
