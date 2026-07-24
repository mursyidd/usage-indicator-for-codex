# Usage Indicator for Codex Design

Date: 23 July 2026
Status: Implemented installer, installed CLI, safe startup ownership, and explicit updates

## Purpose

Provide a usage indicator for the account authenticated in the configured local Codex CLI. It appears visually centered over a Codex Desktop title bar without modifying the signed Codex installation.

The indicator runs as an independent Windows companion. Codex Desktop is only the window the companion detects and follows for placement; it is not the identity source for the displayed usage.

## Distribution and Command Architecture

The only supported distribution is the per-user Inno Setup installer. It installs
the self-contained GUI under
`%LOCALAPPDATA%\Programs\UsageIndicatorForCodex\app`, installs
`usage-indicator.exe` under the sibling `bin` directory, and adds only that
owned `bin` entry to the current user's `PATH`. The installed launcher provides
the public `start`, `stop`, `status`, `version`, `check-update`, `update`,
`enable-startup`, `disable-startup`, and `help` verbs. The GUI remains the
managed command host and is not a public executable interface. The release
contains exactly the installer and its checksum. The installer displays
repository `LICENSE` and installs its byte-identical copy as `LICENSE.txt`.
Release builds are self-contained `win-x64`. Windows 11 Arm64 is permitted
through x64 emulation but remains unverified; Windows 10 is unsupported.

## Display

The full indicator is:

```text
Usage [████████████████████] 100% left | [local reset timestamp]
```

Display rules:

- Show only the active usage limit with the lowest remaining percentage.
- Show the reset timestamp belonging to that selected limit.
- Convert the timestamp to the user's Windows local time internally.
- Do not display `Resets`, `expires`, a timezone abbreviation, or a UTC offset.
- Format the timestamp as `D Month h:mm am/pm`.
- Fill the bar in proportion to the remaining percentage.
- Do not animate or flash the indicator.

## Colors

- 50–100% remaining: green
- 20–49% remaining: amber
- 0–19% remaining: red
- Loading or unavailable: neutral gray

Color is supplemental. The numeric percentage remains visible so that the indicator does not depend on color alone.

## Responsive Layout

The companion selects the largest layout that fits without covering Codex controls:

```text
Normal:      Usage [████████████████████] 100% left | [local reset timestamp]
Narrow:      Usage [████████████] 100% left
Very narrow: Usage 100%
Too narrow:  hidden
```

The indicator returns automatically when sufficient space becomes available.

## Loading and Failure States

While retrieving usage:

```text
Usage [····················] — | —
```

When usage is temporarily unavailable:

```text
Usage [────────────────────] unavailable
```

Normal and loading states are non-clickable. The unavailable state accepts a click to retry immediately.

The companion retries when:

- Codex restarts;
- Codex regains focus;
- the periodic refresh interval elapses.

An unavailable response must never be represented as 0% remaining.

## CLI Account Scope

The usage value belongs only to the ChatGPT-authenticated account available through a separately installed compatible Codex CLI. The default per-user path is:

```text
%APPDATA%\npm\codex.cmd
```

`CODEX_CLI_PATH` may specify a different absolute `.exe` or `.cmd` launcher and is authoritative when set. Without an override, the provider resolves a native `codex.exe` on `PATH`, then `%APPDATA%\npm\codex.cmd`, then another `codex.cmd` on `PATH`. Invalid explicit overrides fail closed rather than silently selecting another installation.

The display intentionally remains the approved neutral wording (`Usage`); it does not claim that it follows the active Codex Desktop account. Codex Desktop determines overlay placement only.

The CLI provider launches `app-server --stdio` only through the resolved path and uses only the stable `initialize`, `account/read` with `refreshToken: false`, and `account/rateLimits/read` flow. It does not send model, thread, turn, login, or logout requests; read credentials, browser data, tokens, or Desktop package files; or install, update, or replace the CLI.

After changing the local CLI account, the user restarts the companion through
the installed `usage-indicator` command. Startup performs the same safe,
read-only CLI validation without printing account, token, or usage values. It
does not compare the CLI identity with Codex Desktop.

On Codex exit or restart, the overlay hides until an eligible Codex window returns. A Desktop sign-out or account change does not establish, invalidate, or synchronize the configured CLI account.

The companion fails closed to the neutral unavailable state when the CLI is absent, logged out, not ChatGPT-authenticated, incompatible, malformed, timed out, or otherwise unable to return a verified account-scoped rate-limit response. It never renders these failures as 0% remaining.

The companion also:

- does not store account credentials;
- does not copy browser session credentials;
- does not use OpenAI Platform API usage as a substitute for ChatGPT-plan Codex limits; and
- does not display an unverified CLI usage result.

The provider remains behind the usage-provider abstraction. A future documented, machine-readable Codex Desktop identity source may be added as a separate provider without redesigning the overlay.

## Window Behavior

The indicator is technically a separate Windows tool window but should appear to belong to Codex.

It must:

- have no taskbar icon;
- stay out of Alt+Tab;
- avoid taking keyboard focus;
- identify an eligible Codex Desktop main window through its `ChatGPT.exe` process name, `OpenAI.Codex_2p2nqsd0c76g0` package family, and `Codex` or `ChatGPT` window title, without reading package files;
- track Codex movement, resizing, minimization, restoration, and DPI changes;
- attach to the most recently foregrounded eligible Codex main window and remain attached while that window is visible, even when another application has focus;
- on companion startup, discover the first eligible Codex main window in top-level Windows z-order so it can attach even when another application is already foreground;
- switch only when a different eligible Codex main window becomes foreground;
- hide when its attached Codex window is minimized, closed, hidden, or otherwise no longer eligible for display;
- avoid covering native window controls; and
- support multiple Codex windows through that foreground-triggered switch rule.

The companion uses Windows window events rather than frequent position polling.

## Startup and Update Resilience

Automatic startup is enabled through `usage-indicator enable-startup`. A normal
launch does not register startup.

The canonical `UsageIndicatorForCodex` task has two positively recognized
forms: the exact expected `UsageIndicatorForCodex.Gui.exe --background` path,
or the exact sibling `UsageIndicatorForCodex.exe --background` path in the same
application directory. Enabling startup migrates the recognized launcher form
to the direct GUI form. The sibling launcher action exists only as an internal upgrade compatibility
rule; it is not a public command. Same-name executables
elsewhere, malformed or ambiguous actions, and foreign canonical or legacy
tasks are preserved. Ownership collisions exit `2`; operational scheduler
inspection failures exit `1`. Disable and uninstall remove only positively
recognized owned tasks.

The startup task must:

- install a companion-owned per-user Windows Task Scheduler logon task as the external crash-recovery owner;
- launch the companion with `--background` under the interactive user token;
- configure that task to restart a failed companion up to three times at one-minute intervals, with no execution-time limit;
- detect an already-running Codex window when it starts;
- reattach when Codex restarts;
- recover when Codex restarts during an update;
- avoid hardcoding Codex's versioned `WindowsApps` path;
- leave the signed Codex package unchanged.

A crashed companion cannot restart itself. Task Scheduler owns the bounded restart policy; no separate watchdog is required.

Installed updates are explicit. `check-update` reads stable release metadata
without locking or downloading. `update` acquires a distinct per-user mutex
before network access, downloads the exact installer and checksum assets,
verifies the filename-bound SHA-256 record, stops the GUI, and launches the
installer interactively. The mutex is released after success, no update,
failure, cancellation, or handoff; abandoned mutexes are recoverable. A
concurrent update exits `1` with `An update is already in progress.` The updater
never directly overwrites installed files.

A major Codex title-bar redesign may require a companion update. Configurable alignment offsets provide a recovery path for minor layout changes.

## Refresh Behavior

Usage refreshes:

- when the companion first attaches to Codex;
- when Codex regains focus and the cached result is more than one minute old;
- every five minutes while Codex is active.

Refreshing must not cause model requests or consume usage merely to update the indicator.

## Controls and Removal

No Codex skill is required for normal operation.

The companion provides `Ctrl+Alt+U` to enable or disable the overlay without
changing Codex. `usage-indicator stop` stops a running canonical instance, and
`usage-indicator help` describes the complete public command interface.
Invalid, duplicate, or combined arguments fail without starting the
application. Live CLI-account usage is enabled during normal operation, while
automatic startup remains explicit.

The installed uninstaller removes application files and only the PATH entry it
recorded as owned. Before file removal it asks the installed CLI to remove
positively recognized owned startup tasks. Foreign canonical and legacy tasks
remain untouched. When status reports `startup: unrecognized`, the installer
disables its startup checkbox and performs zero startup mutation.

The user may optionally delete local settings at
`%LOCALAPPDATA%\UsageIndicatorForCodex` after installed uninstallation. During
legacy migration, valid settings are copied atomically only when canonical
settings do not already exist, and legacy settings remain for rollback. A
`CodexUsageIndicator` task is deleted only when its single executable action is
a normalized fully qualified `CodexUsageIndicator.exe --background`; all
unrecognized legacy forms are preserved.

Uninstallation must not modify or repair Codex.

## Acceptance Criteria

The design is satisfied when:

1. The visible display remains `Usage [████████████████████] 100% left | [local reset timestamp]`, with the approved narrow, loading, and unavailable variants.
2. The indicator uses the approved text, date, percentage, color, and responsive formats.
3. Only the most restrictive active limit is displayed.
4. Timestamps are formatted in the user's Windows local time without a timezone label or UTC offset.
5. Documentation and runtime behavior identify the configured local Codex CLI account as the sole usage scope; they make no claim of automatic Desktop-account following.
6. Missing CLI authentication, incompatible or malformed responses, timeouts, and provider failures produce the neutral unavailable state.
7. Starting through `usage-indicator` validates the configured CLI account without reading credentials or emitting account, token, or usage data.
8. `usage-indicator enable-startup` creates the companion-owned per-user Task Scheduler task, and live values are shown only for the configured local Codex CLI account after a verified provider response.
9. The companion never modifies the signed Codex installation.
10. The overlay does not cover Codex controls or create taskbar and Alt+Tab clutter.
11. Foregrounding a non-Codex application does not detach the overlay from its visible attached Codex window; foregrounding another eligible Codex main window switches the attachment.
12. The companion-owned Task Scheduler task is the external crash-recovery owner and restarts failed runs no more than three times at one-minute intervals after the user installs it.
13. Canonical and explicitly legacy instance identities prevent the old and renamed applications from running simultaneously during migration.
14. Canonical settings and startup registration win without being overwritten by legacy state.
15. `usage-indicator` is the only public command interface, and the installer is the only supported distribution.
16. The installer displays repository `LICENSE`, installs `LICENSE.txt`, and the release contains exactly the installer and its checksum.
17. Concurrent installed updates are rejected before network or process mutation.
18. Startup ownership recognizes only the two exact canonical forms and the explicitly recognized legacy form; foreign tasks receive zero mutation.

## Implementation Gate

The verified CLI-account-scoped source is the production live-usage provider. It must not extract browser credentials, expose a remote-debugging port, or modify the Codex package.

If no reliable source can provide both the remaining percentage and its reset timestamp, the companion must remain unavailable rather than estimate usage. Passing this provider gate does not assert any relationship between the CLI account and Codex Desktop account.
