# Usage Indicator for Codex

Usage Indicator for Codex is an independent Windows companion that displays the
most restrictive active Codex usage limit over a Codex Desktop title bar. It
uses the Desktop window only for placement and does not modify the signed Codex
Desktop installation.

Usage comes from the ChatGPT-authenticated account in a separately installed
local Codex CLI. Codex Desktop is not the usage or identity source. If Desktop
and CLI use different accounts, the displayed usage can differ from the account
visible in Desktop.

![Usage Indicator for Codex attached to the Codex Desktop title bar](docs/images/usage-indicator-context.png)

> The indicator follows Codex Desktop while displaying usage from the separately authenticated local Codex CLI account.

## Requirements

- Windows 11 x64.
  - Windows 11 Arm64 may work through Windows x64 emulation, but it has not
    been verified.
  - Windows 10, macOS, and Linux are not supported.
- Codex Desktop for the followed window.
- A compatible Codex CLI authenticated with ChatGPT.

The installer contains a self-contained `win-x64` build. It does not require a
separate .NET runtime and does not install or update .NET, Codex CLI, or Codex
Desktop.

### Development requirements

- A .NET SDK capable of targeting `net8.0-windows`.
- The x64 MSVC compiler, linker, and library manager for native launcher builds.
- Inno Setup 6 for installer builds.

## Installation

The per-user installer is the only supported distribution:

1. Download these two files from the intended GitHub Release:

   ```text
   UsageIndicatorForCodex-Setup-v0.1.0.exe
   UsageIndicatorForCodex-Setup-v0.1.0.exe.sha256
   ```

2. Verify the checksum:

   ```powershell
   Get-FileHash .\UsageIndicatorForCodex-Setup-v0.1.0.exe -Algorithm SHA256
   Get-Content .\UsageIndicatorForCodex-Setup-v0.1.0.exe.sha256
   ```

   The hexadecimal values must match, and the checksum record must name the
   exact installer.

3. Run the installer interactively. It installs only for the current user and
   does not request administrator privileges.
4. Open a new terminal so it receives the updated user `PATH`.

Public builds are currently unsigned. Windows may show Microsoft Defender
SmartScreen or an unknown-publisher warning. Verify the intended repository and
tag before deciding whether to run a build; do not disable system-wide security
protections. A checksum downloaded from the same release detects corruption
relative to that record but is not independent publisher authentication.

## Installed layout

```text
%LOCALAPPDATA%\Programs\UsageIndicatorForCodex\
├── app\
│   ├── UsageIndicatorForCodex.Gui.exe
│   ├── LICENSE.txt
│   └── self-contained runtime files
└── bin\
    └── usage-indicator.exe
```

Only `bin` is added to the current user's `PATH`. The installer records
ownership only when it adds that exact entry, so uninstall preserves a matching
entry that existed before installation.

`UsageIndicatorForCodex.Gui.exe` is the internal WPF implementation and command
host. Users should normally use `usage-indicator` instead of invoking it
directly.

## Quick start

Open a new terminal after installation, then run:

```powershell
usage-indicator start
usage-indicator status
```

The new terminal is required because terminals that were already open do not
receive the installer's updated user `PATH`.

## Commands

Commands are case-sensitive and accept exactly one verb. Running
`usage-indicator` without arguments shows help.

```text
usage-indicator start             Start the GUI and return immediately
usage-indicator stop              Stop the canonical running instance
usage-indicator status            Inspect running, indicator, and startup state
usage-indicator version           Print the product version
usage-indicator check-update      Report whether a stable update is available
usage-indicator update            Verify and launch a newer installer
usage-indicator enable-startup    Register or update current-user startup
usage-indicator disable-startup   Remove positively recognized owned tasks
usage-indicator help              Show command help
```

This installed `usage-indicator` command is the only public command interface.
`start`, `stop`, `enable-startup`, and `disable-startup` are idempotent.

## Status and exit codes

`usage-indicator status` prints an exact, non-localized three-line record:

```text
running: true|false
indicator-enabled: true|false
startup: enabled|disabled|unrecognized
```

- `status` exits `0` after a successful inspection, including when the
  application is stopped or startup is `unrecognized`.
- `enable-startup` and `disable-startup` exit `2` when a foreign same-name
  scheduled task is preserved.
- Operational inspection, scheduler, settings, network, download, or update
  failures exit `1`.
- Invalid, duplicate, combined, or incorrectly cased command syntax exits `2`
  and does not start the companion.
- A concurrent `usage-indicator update` exits `1`.

An unrecognized startup state is an ownership warning, not an inspection
failure. The application refuses to mutate the foreign task.

## Start with Windows

Enable current-user logon startup explicitly:

```powershell
usage-indicator enable-startup
```

This creates or updates the current user's `UsageIndicatorForCodex` Task
Scheduler logon task. The task launches the internal GUI in background mode,
using `UsageIndicatorForCodex.Gui.exe --background`, has no execution-time
limit, and retries a crash up to three times at one-minute intervals. This is
an internal scheduled action, not a public command.

For internal upgrade compatibility, ownership inspection also recognizes a
previous canonical task whose action is the exact sibling
`UsageIndicatorForCodex.exe --background` in the same application directory.
This is an internal upgrade compatibility rule, not a supported user command.
Enabling startup migrates that recognized task to the direct internal GUI form.
A same-name executable anywhere else is foreign.

Disable startup without deleting settings or application files:

```powershell
usage-indicator disable-startup
```

The command and uninstaller remove only positively recognized owned tasks.
They preserve foreign canonical tasks and preserve a legacy task named
`CodexUsageIndicator` unless its action is the recognized historical form.
Mixed owned and foreign inventories remove owned entries, preserve foreign
entries, and report an ownership collision.

The installer includes an optional **Start with Windows** checkbox. It is
unchecked on a fresh installation. On upgrade, the installer initializes it
from a successful installed-CLI status inspection. If status is
`startup: unrecognized`, the checkbox is unchecked and disabled, a collision
warning is shown, and the installer performs zero startup mutation.

If the old installed CLI is unavailable, malformed, or cannot complete
inspection, the installer preserves the existing startup state unless the user
explicitly changes the checkbox.

## Updates

`usage-indicator check-update` queries the latest stable GitHub Release and
reports availability without downloading or installing anything.

`usage-indicator update` is explicit:

1. Query the latest stable release.
2. Select the exact versioned installer and checksum assets.
3. Download both to a version-specific temporary directory.
4. Require the checksum record to name that installer and verify SHA-256.
5. Stop the running companion.
6. Launch the installer visibly and interactively.

The updater never copies over installed application files, supplies silent
installer flags, or runs from a timer, service, or automatic background path.
Development builds without an explicitly configured GitHub repository URL fail
closed.

A distinct per-user update mutex is acquired before release metadata is
requested and held through installer launch. A concurrent update exits `1` and
prints:

```text
An update is already in progress.
```

The mutex is released after success, no update, failure, cancellation, or
installer handoff. Abandoned mutexes are recovered safely.
`usage-indicator check-update` does not acquire this mutex.

## Codex CLI configuration

Without an override, the companion tries:

1. a native `codex.exe` on `PATH`;
2. `%APPDATA%\npm\codex.cmd`;
3. another `codex.cmd` on `PATH`.

To select a trusted installation for the current shell:

```powershell
$env:CODEX_CLI_PATH = 'C:\Program Files\Codex CLI\codex.cmd'
usage-indicator start
```

To configure future interactive and scheduled launches, set the user
environment variable:

```powershell
[Environment]::SetEnvironmentVariable(
    'CODEX_CLI_PATH',
    'C:\Program Files\Codex CLI\codex.cmd',
    'User')
```

Open a new terminal, then restart the companion:

```powershell
usage-indicator stop
usage-indicator start
```

Paths containing spaces are supported. An explicit override is authoritative:
relative paths, unsupported extensions, missing files, launch failures,
logged-out CLIs, API-key authentication, incompatible responses, and malformed
responses fail closed to `Usage unavailable`.

## Display and controls

![Close-up of the usage percentage, progress bar, and reset time](docs/images/usage-indicator-closeup.png)

- `Usage —` means the companion is loading or refreshing.
- `Usage unavailable` means no verified current CLI-account response was
  available. It never means 0% remaining. Clicking it retries.
- A percentage appears only after a verified ChatGPT-account response with an
  active reset window.
- The overlay hides when no eligible Codex Desktop window is visible, while its
  window is minimized, or when the title bar is too narrow.
- `Ctrl+Alt+U` enables or disables the overlay without changing Codex.

Per-user settings are stored at:

```text
%LOCALAPPDATA%\UsageIndicatorForCodex\settings.json
```

The settings schema is:

```json
{
  "Enabled": true,
  "HorizontalOffset": 0,
  "VerticalOffset": 6
}
```

Offsets are logical pixels from `-500` through `500`. Missing, malformed, or
invalid canonical settings fall back to defaults. Valid settings from the
historical `%LOCALAPPDATA%\CodexUsageIndicator\settings.json` location migrate
atomically only when canonical settings do not already exist.

## Uninstall

1. Optionally run `usage-indicator disable-startup`. The uninstaller also asks
   the installed CLI to remove only positively recognized owned startup tasks.
2. Open **Settings > Apps > Installed apps**, choose **Usage Indicator for
   Codex**, and select **Uninstall**.
3. The uninstaller removes application files and only the `PATH` entry recorded
   as installer-owned.
4. Optionally delete `%LOCALAPPDATA%\UsageIndicatorForCodex` to remove settings.

If migrated from an older build, optionally remove
`%LOCALAPPDATA%\CodexUsageIndicator`. Uninstall does not modify Codex CLI or
Codex Desktop.

## Security and privacy

The companion launches only the configured CLI as `app-server --stdio`, then
sends `initialize`, `account/read` with `refreshToken: false`, and
`account/rateLimits/read`. It does not read credential files, browser profiles,
tokens, or Codex Desktop package files; alter authentication; send model,
thread, or turn requests; or install or update Codex CLI.

See [SECURITY.md](SECURITY.md) for vulnerability reporting and the complete
trust boundary.

## Development and contributing

```powershell
dotnet restore .\UsageIndicatorForCodex.sln
dotnet build .\UsageIndicatorForCodex.sln --configuration Release --no-restore
dotnet run --project .\tests\UsageIndicatorForCodex.Tests\UsageIndicatorForCodex.Tests.csproj --configuration Release --no-build
.\tests\repository-contract.ps1
```

Release automation produces exactly:

```text
UsageIndicatorForCodex-Setup-v0.1.0.exe
UsageIndicatorForCodex-Setup-v0.1.0.exe.sha256
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the installer build, contract checks,
and contribution requirements.

## Limitations and license

- Desktop and CLI account identities are not automatically correlated.
- Only the configured CLI account's ChatGPT-plan limits are shown; OpenAI
  Platform API usage is not substituted.
- Window detection depends on current Codex Desktop process, package, and title
  conventions.
- Builds are unsigned; SmartScreen and clean-machine installer behavior require
  manual Windows verification.
- A same-release checksum is integrity evidence, not independent publisher
  authentication.
- Windows 10 is unsupported. Windows 11 Arm64 installation is permitted only
  through x64 emulation and remains unverified.

This project is licensed under the [MIT License](LICENSE). The installer
displays that license and installs the repository-sourced copy as
`app\LICENSE.txt`.
