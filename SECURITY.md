# Security Policy

## Reporting a vulnerability

Use GitHub’s private **Report a vulnerability** flow in the repository’s Security tab when available. Include the affected version or commit, reproduction steps, impact, and the smallest safe diagnostic output needed to confirm the issue.

Do not place credentials, tokens, cookies, account identifiers, private paths, raw app-server responses, or other sensitive data in a public issue. If private vulnerability reporting is unavailable, open a public issue containing no sensitive details and ask the maintainer to establish a private channel.

There is no guaranteed response or remediation timeline until the repository owner publishes one.

## Credential and process boundary

Usage Indicator for Codex relies on authentication already managed by a separately installed local Codex CLI. The companion starts only the selected `.exe` or `.cmd` launcher with `app-server --stdio` and sends:

- `initialize`;
- `account/read` with `refreshToken: false`; and
- `account/rateLimits/read`.

It does not read credential files, browser profiles, tokens, or Codex Desktop package files. It does not perform login, logout, token refresh, authentication changes, model requests, thread or turn requests, CLI installation/update, or Codex Desktop modification.

`CODEX_CLI_PATH` is a code-execution trust boundary. It must point to an absolute launcher the user trusts. The companion does not download, repair, or authenticate that launcher. An explicit override is authoritative and fails closed if it cannot be used.

An account value returned by the CLI is transformed into a deterministic truncated SHA-256 fingerprint used only in memory to keep account and rate-limit responses scoped together. Predictable identities may still be guessable, so the fingerprint is account-derived data. It, the source identity, tokens, and usage values are not written to settings or logs.

The canonical settings file contains only enabled state and layout offsets. A retained legacy settings file has the same boundary. Neither is a credential store.

Reports requiring credential collection, raw authenticated responses, or weakened process boundaries must be rejected or redesigned around synthetic data.

## Release trust

GitHub release archives are self-contained `win-x64` builds. The packaging process rejects PDBs, source, tests, and repository/build-tree content.

Public builds are currently unsigned and do not provide an Authenticode publisher identity. Windows may show SmartScreen or unknown-publisher warnings. Obtain releases from the intended repository and tag, inspect local hashes when a trusted comparison value is available, and do not disable system-wide security protections merely to run the companion.

## Supported security scope

Security fixes are evaluated against current source and the self-contained Windows x64 release documented in the README. Platform and bundled runtime support end when their upstream support ends.
