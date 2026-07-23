# Usage Indicator for Codex Design

Date: 23 July 2026
Status: CLI-account contract; production startup and CLI-account live display enabled

## Purpose

Provide a usage indicator for the account authenticated in the configured local Codex CLI. It appears visually centered over a Codex Desktop title bar without modifying the signed Codex installation.

The indicator runs as an independent Windows companion. Codex Desktop is only the window the companion detects and follows for placement; it is not the identity source for the displayed usage.

## Display

The full indicator is:

```text
Usage [████████████████████] 100% left | 24 July 10:30 am
```

Display rules:

- Show only the active usage limit with the lowest remaining percentage.
- Show the reset timestamp belonging to that selected limit.
- Convert the timestamp to Malaysia time internally.
- Do not display `Resets`, `expires`, or `MYT`.
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
Normal:      Usage [████████████████████] 100% left | 24 July 10:30 am
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

After changing the local CLI account, the user can run the companion with `--revalidate-cli`. The command performs the same safe read-only CLI validation and returns success or failure without printing account, token, or usage values. It does not compare the CLI identity with Codex Desktop.

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

Automatic startup is enabled through `--install` from the published companion executable. The command creates only the companion-owned per-user Task Scheduler task.

A normal launch starts the companion immediately and does not register startup. `--install` performs startup registration only and then exits.

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

A major Codex title-bar redesign may require a companion update. Configurable alignment offsets provide a recovery path for minor layout changes.

## Refresh Behavior

Usage refreshes:

- when the companion first attaches to Codex;
- when Codex regains focus and the cached result is more than one minute old;
- every five minutes while Codex is active.

Refreshing must not cause model requests or consume usage merely to update the indicator.

## Controls and Removal

No Codex skill is required for normal operation.

The companion provides `Ctrl+Alt+U` or `--toggle` to enable or disable the overlay without changing Codex. `--revalidate-cli` is the user-invoked CLI-account validation command. `--exit` stops a running canonical instance. `--help` and `-h` show command help. Invalid, duplicate, or combined arguments fail without starting the application. Live CLI-account usage is enabled during normal operation, while automatic startup is installed only by the explicit `--install` command.

Uninstallation removes only:

- its per-user startup registration when `--uninstall` is run.

Complete removal additionally requires exiting the companion, deleting its published files, and optionally deleting its local settings at `%LOCALAPPDATA%\UsageIndicatorForCodex`. During migration, valid legacy settings are copied atomically only when canonical settings do not already exist, and the legacy file is retained for rollback. Startup migration registers the canonical `UsageIndicatorForCodex` task before deleting the explicitly legacy `CodexUsageIndicator` task. These manual file removals remain owned by the user; `--uninstall` does not delete them.

Uninstallation must not modify or repair Codex.

## Acceptance Criteria

The design is satisfied when:

1. The visible display remains exactly `Usage [████████████████████] 100% left | 24 July 10:30 am`, with the approved narrow, loading, and unavailable variants.
2. The indicator uses the approved text, date, percentage, color, and responsive formats.
3. Only the most restrictive active limit is displayed.
4. Timestamps are formatted in Malaysia time without a timezone label.
5. Documentation and runtime behavior identify the configured local Codex CLI account as the sole usage scope; they make no claim of automatic Desktop-account following.
6. Missing CLI authentication, incompatible or malformed responses, timeouts, and provider failures produce the neutral unavailable state.
7. `--revalidate-cli` validates the configured CLI account without reading credentials or emitting account, token, or usage data.
8. `--install` creates the companion-owned per-user Task Scheduler task, and live values are shown only for the configured local Codex CLI account after a verified provider response.
9. The companion never modifies the signed Codex installation.
10. The overlay does not cover Codex controls or create taskbar and Alt+Tab clutter.
11. Foregrounding a non-Codex application does not detach the overlay from its visible attached Codex window; foregrounding another eligible Codex main window switches the attachment.
12. The companion-owned Task Scheduler task is the external crash-recovery owner and restarts failed runs no more than three times at one-minute intervals after the user installs it.
13. Canonical and explicitly legacy instance identities prevent the old and renamed applications from running simultaneously during migration.
14. Canonical settings and startup registration win without being overwritten by legacy state.

## Implementation Gate

The verified CLI-account-scoped source is the production live-usage provider. It must not extract browser credentials, expose a remote-debugging port, or modify the Codex package.

If no reliable source can provide both the remaining percentage and its reset timestamp, the companion must remain unavailable rather than estimate usage. Passing this provider gate does not assert any relationship between the CLI account and Codex Desktop account.
