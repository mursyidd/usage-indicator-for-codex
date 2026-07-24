# Security Policy

## Reporting a vulnerability

Use GitHub's private **Report a vulnerability** flow in the repository's
Security tab when available. Include the affected version or commit,
reproduction steps, impact, and the smallest safe diagnostic output needed to
confirm the issue.

Do not place credentials, tokens, cookies, account identifiers, private paths,
raw app-server responses, or other sensitive data in a public issue. If private
reporting is unavailable, open a public issue containing no sensitive details
and ask the maintainer to establish a private channel.

There is no guaranteed response or remediation timeline until the repository
owner publishes one.

## Credential and process boundary

Usage Indicator for Codex relies on authentication already managed by a
separately installed local Codex CLI. The companion starts only the selected
`.exe` or `.cmd` launcher with `app-server --stdio` and sends:

- `initialize`;
- `account/read` with `refreshToken: false`; and
- `account/rateLimits/read`.

It does not read credential files, browser profiles, tokens, or Codex Desktop
package files. It does not perform login, logout, token refresh, authentication
changes, model requests, thread or turn requests, CLI installation/update, or
Codex Desktop modification.

`CODEX_CLI_PATH` is a code-execution trust boundary. It must point to an
absolute launcher the user trusts. The companion does not download, repair, or
authenticate that launcher. An explicit override is authoritative and fails
closed if it cannot be used.

An account value returned by the CLI is transformed into a deterministic,
truncated SHA-256 fingerprint used only in memory to keep account and rate-limit
responses scoped together. Predictable identities may still be guessable, so
the fingerprint is account-derived data. It, the source identity, tokens, and
usage values are not written to settings or logs.

The canonical settings file contains only enabled state and layout offsets. A
retained legacy settings file has the same boundary. Neither is a credential
store.

Reports requiring credential collection, raw authenticated responses, or
weakened process boundaries must be rejected or redesigned around synthetic
data.

## Installer and update boundary

The installer is per-user, writes application files only below
`%LOCALAPPDATA%\Programs\UsageIndicatorForCodex`, and adds only its `bin`
directory to the current user's `PATH`. It records ownership only when it adds
that exact entry. Uninstall must not remove a matching PATH entry that existed
before installation.

Update checks use an explicitly configured GitHub repository URL compiled into
release builds. The owner is derived from build configuration or repository
metadata and is never guessed.

`check-update` retrieves stable release metadata only. `update` requires exact
versioned installer and checksum asset names, binds the checksum record to the
installer filename, verifies SHA-256, stops the running application, and starts
the installer interactively. The updater does not replace installed files
itself, invoke silent installation, or run automatically in the background. A
distinct per-user mutex is acquired before update network access and held
through installer launch. Concurrent update commands are rejected with
`An update is already in progress.`; `check-update` is not locked.

Task Scheduler ownership is positive, not name-only. The canonical
`UsageIndicatorForCodex` task is recognized only when it runs either the exact
expected `UsageIndicatorForCodex.Gui.exe --background` path or the exact
sibling `UsageIndicatorForCodex.exe --background` path. Recognized
launcher-backed tasks are migrated to the direct GUI form. Foreign canonical
and legacy tasks are preserved. Ownership collisions return exit code `2`;
operational inspection failures return `1`. Disable and uninstall remove only
positively recognized owned tasks. The sibling launcher form is an internal
upgrade compatibility rule, not a public command interface.

Repository URL handling, GitHub release parsing, asset selection, temporary
downloads, checksum verification, process stopping, installer launch, PATH
ownership, Task Scheduler, and uninstall behavior are security-sensitive
boundaries.

## Release trust

The release contract contains only:

```text
UsageIndicatorForCodex-Setup-v<version>.exe
UsageIndicatorForCodex-Setup-v<version>.exe.sha256
```

The exact version comes from canonical repository product metadata, must match
the release tag, and the release still contains exactly two public assets.

The installer displays the repository `LICENSE` and installs its byte-identical
copy as `app\LICENSE.txt`. CI rebuilds the self-contained application and
installed launcher, validates installer invariants, asset names, license
content, and the checksum, then uploads exactly those two assets.

Public builds are unsigned and do not provide an Authenticode publisher
identity. Windows may show SmartScreen or unknown-publisher warnings. Obtain
releases from the intended repository and exact tag, and do not disable
system-wide security protections merely to run the companion.

SHA-256 proves that downloaded bytes match a checksum record. A checksum from
the same release does not independently authenticate the publisher or protect
against compromise of that repository/release, because an attacker able to
replace the asset may also replace its checksum.

## Supported security scope

Security fixes are evaluated against current source and the self-contained
Windows x64 installer documented in the README. Platform and bundled runtime
support end when their upstream support ends. Windows 11 Arm64 is permitted by
the installer through x64 emulation but remains unverified; Windows 10 is
unsupported.
