# Usage Indicator for Codex

[![CI](https://github.com/mursyidd/usage-indicator-for-codex/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/mursyidd/usage-indicator-for-codex/actions/workflows/ci.yml) [![Latest Release](https://img.shields.io/github/v/release/mursyidd/usage-indicator-for-codex?display_name=tag&sort=semver)](https://github.com/mursyidd/usage-indicator-for-codex/releases/latest) [![License: MIT](https://img.shields.io/github/license/mursyidd/usage-indicator-for-codex)](LICENSE)

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

The per-user installer is the only supported distribution. Download it from the
repository's [Latest Release](../../releases/latest), after verifying the
intended repository and release tag:

1. Download the installer named for the version shown by that GitHub Release:

   ```text
   UsageIndicatorForCodex-Setup-v<version>.exe
   ```

   You may also download the optional checksum file to verify the installer:

   ```text
   UsageIndicatorForCodex-Setup-v<version>.exe.sha256
   ```

   GitHub's automatically generated source-code archives are not installers.

2. If you downloaded the checksum file, place only the installer and its
   matching checksum file in one folder. Open a terminal in that folder and
   verify the installer:

   ```powershell
   $installers = @(
       Get-ChildItem -LiteralPath . `
           -Filter 'UsageIndicatorForCodex-Setup-v*.exe' `
           -File
   )

   if ($installers.Count -ne 1) {
       throw 'Expected exactly one Usage Indicator installer in this folder.'
   }

   $installer = $installers[0]

   Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256
   Get-Content -LiteralPath "$($installer.FullName).sha256"
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

## Quick start

First run: download &rarr; install &rarr; open a new terminal &rarr; start the
companion &rarr; inspect status.

After installation, open a new terminal and run:

```powershell
usage-indicator start
usage-indicator status
```

The new terminal is required because terminals that were already open do not
receive the installer's updated user `PATH`.

A successful fresh installation can legitimately report:

```text
running: true
indicator-enabled: true
credit-expiry: disabled
startup: disabled
```

Startup is optional; it does not need to be enabled for the companion to run.
For [`usage-indicator` is not recognized](#usage-indicator-is-not-recognized),
[an indicator that is not visible](#the-indicator-is-not-visible), or
[`Usage unavailable`](#the-indicator-shows-usage-unavailable), see [Troubleshooting](#troubleshooting).

## Installed layout

```text
%LOCALAPPDATA%\Programs\UsageIndicatorForCodex\
├── app\
│   ├── UsageIndicatorForCodex.Gui.exe
│   ├── LICENSE.txt
│   └── self-contained runtime files
├── bin\
│   └── usage-indicator.exe
└── updater\
    └── UsageIndicatorForCodex.UpdateHost.exe
```

Only `bin` is added to the current user's `PATH`. The installer records
ownership only when it adds that exact entry, so uninstall preserves a matching
entry that existed before installation.

`UsageIndicatorForCodex.Gui.exe` is the internal WPF implementation and command
host, not a public command. Use `usage-indicator` instead of invoking it
directly. The native launcher is the stable bootstrap. The standalone update
host is copied to a versioned LocalAppData cache before update work, so the
installer can replace the installed host and GUI payload safely.

## Commands

Commands are case-sensitive and accept exactly one verb. Running
`usage-indicator` without arguments shows help.

```text
usage-indicator start             Start the GUI and return immediately
usage-indicator stop              Stop the canonical running instance
usage-indicator status            Inspect running, indicator, credit, and startup state
usage-indicator version           Print the product version
usage-indicator check-update      Report whether a stable update is available
usage-indicator update            Verify, install, and validate a stable update
usage-indicator enable-startup    Register or update current-user startup
usage-indicator disable-startup   Remove positively recognized owned tasks
usage-indicator enable-credit-expiry   Show returned reset-credit expiry details
usage-indicator disable-credit-expiry  Hide reset-credit expiry details
usage-indicator help              Show command help
```

This installed `usage-indicator` command is the only public command interface.
`start`, `stop`, `enable-startup`, `disable-startup`,
`enable-credit-expiry`, and `disable-credit-expiry` are idempotent.

## Status and exit codes

`usage-indicator status` prints an exact, non-localized four-line record:

```text
running: true|false
indicator-enabled: true|false
credit-expiry: enabled|disabled
startup: enabled|disabled|unrecognized
```

- `status` exits `0` after a successful inspection, including when the
  application is stopped or startup is `unrecognized`.
- `enable-startup` and `disable-startup` exit `2` when a foreign same-name
  scheduled task is preserved.
- `enable-credit-expiry` and `disable-credit-expiry` exit `0` only after the
  setting is persisted and any running indicator acknowledges the live change.
  Persistence or live-application failures exit `1`.
- Operational inspection, scheduler, settings, network, download, or update
  failures exit `1`.
- Invalid, duplicate, combined, or incorrectly cased command syntax exits `2`
  and does not start the companion.
- A concurrent `usage-indicator update` exits `1`.
- A validated installer restart requirement exits `3010`; the companion is not
  restarted until Windows has restarted.

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

1. The stable native launcher copies the installed standalone UpdateHost to
   `%LOCALAPPDATA%\UsageIndicatorForCodex\update-host\v<version>` and waits for
   that cached process in the original shell.
2. The host acquires the per-user update mutex, queries the latest stable
   release, and selects the exact versioned installer and checksum assets.
3. It downloads both, requires the checksum record to name that installer, and
   verifies SHA-256 before inspecting or stopping the companion.
4. It records whether the companion is running and stops it gracefully through
   the existing single-instance command channel.
5. It runs the verified installer silently in private `/CLIUPDATE` mode, waits
   for completion, then independently requires the target version from
   installer-owned registry state, the installed UpdateHost, and the GUI.
6. On installer exit `0`, it restarts only a companion that was previously
   running. On validated exit `3010`, it returns `3010`, reports that Windows
   must restart, and does not restart the companion or print ordinary success.

Each phase is printed in the invoking PowerShell or `cmd.exe` session. A
post-installer failure reports the installer log under
`%LOCALAPPDATA%\UsageIndicatorForCodex\update-logs`. UpdateHost cache copies are
process-specific, so concurrent launcher invocations cannot overwrite a running
host.

Private `/CLIUPDATE` is not a silent first-install mechanism. It requires an
existing complete bootstrap-v1 installation, matching installer-owned install
path and bootstrap state, and it never replaces the running stable launcher or
changes startup/PATH ownership. A fresh installation remains interactive. A
legacy installation receives one interactive transitional upgrade that installs
bootstrap v1; later compatible updates can be silent.

Updates never run from a timer, service, scheduled task, or automatic background
path, and production code never force-terminates the companion. Development
builds without an explicitly configured GitHub repository URL fail closed.

A distinct per-user update mutex is acquired before release metadata is
requested and held through installer launch. A concurrent update exits `1` and
prints:

```text
An update is already in progress.
```

The mutex is held through installation, validation, and any conditional restart,
then released after success, no update, failure, cancellation, or a validated
restart requirement. Abandoned mutexes are recovered safely.
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
  available. Usage unavailable does not mean 0% remaining. Clicking it retries.
- A percentage appears only after a verified ChatGPT-account response with an
  active reset window.
- Reset-credit expiry is opt-in and disabled by default. Enable or disable it
  immediately, without restarting a running indicator:

  ```powershell
  usage-indicator enable-credit-expiry
  usage-indicator disable-credit-expiry
  ```

  When enabled, the Full layout can show the earliest valid future expiry among
  available reset-credit detail rows returned by the same Codex app-server
  usage response. It never displays the credit count. The optional segment is
  absent when the returned detail has no usable future row, the account has no
  available returned detail, or the installed Codex CLI/backend does not
  provide the optional detail. Width pressure removes this segment before the
  ordinary Full layout falls back to Narrow.
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
  "VerticalOffset": 6,
  "CreditExpiryEnabled": false
}
```

Offsets are logical pixels from `-500` through `500`. Missing, malformed, or
invalid canonical settings fall back to defaults. Valid settings from the
historical `%LOCALAPPDATA%\CodexUsageIndicator\settings.json` location migrate
atomically only when canonical settings do not already exist. Existing settings
files without `CreditExpiryEnabled` remain valid and default it to `false`.

## Troubleshooting

### `usage-indicator` is not recognized

Terminals already open during installation do not receive the updated user
`PATH`. Close the old terminal, open a new PowerShell window, then run:

```powershell
usage-indicator version
usage-indicator status
```

If it still fails, check whether the installed launcher exists:

```powershell
Test-Path "$env:LOCALAPPDATA\Programs\UsageIndicatorForCodex\bin\usage-indicator.exe"
```

`True` means the launcher is installed, but the terminal `PATH` may be stale or
changed. `False` means installation is incomplete or the launcher was removed;
rerun the official installer.

### The indicator shows `Usage unavailable`

Usage unavailable does not mean 0% remaining. It means no verified compatible
usage response was obtained from the selected Codex CLI account. Possible
causes include an unavailable or logged-out Codex CLI, API-key authentication
instead of ChatGPT authentication, a `CODEX_CLI_PATH` that points to a missing,
obsolete, unsupported, or incompatible file, a selected CLI that cannot launch,
an incompatible or malformed CLI response, or failed network or account access.

Inspect the local CLI and companion status:

```powershell
Get-Command codex -All
codex --version
usage-indicator status
```

Then restart the companion:

```powershell
usage-indicator stop
usage-indicator start
```

Inspect overrides with:

```powershell
$env:CODEX_CLI_PATH
[Environment]::GetEnvironmentVariable('CODEX_CLI_PATH', 'User')
```

Clicking `Usage unavailable` retries. Do not provide tokens, API keys,
credential files, browser profiles, or authentication data when reporting this
problem.

### Displayed usage belongs to another account

Codex Desktop is used only for overlay placement. Usage and account identity
come from the separately installed Codex CLI. Codex Desktop and Codex CLI can
use different ChatGPT accounts, and the companion does not automatically
correlate those identities. Verify the account authenticated in the local Codex
CLI; the application cannot read Codex Desktop identity.

### The indicator is not visible

Start with:

```powershell
usage-indicator status
```

- `running: false` &rarr; run `usage-indicator start`.
- `indicator-enabled: false` &rarr; press `Ctrl+Alt+U`, then inspect status again.
- If both are true, check that Codex Desktop is running, an eligible Codex
  Desktop window is visible, it is not minimized, the title bar is wide enough,
  and the overlay is not temporarily hidden during a window transition.

Restart the companion if needed:

```powershell
usage-indicator stop
usage-indicator start
```

### The reset time appears wrong

Reset time uses the current Windows local timezone. Check:

```powershell
Get-TimeZone
```

Verify the Windows timezone, system date and time, and automatic time
synchronization.

### Status reports `startup: unrecognized`

This is an ownership warning, not a failed status inspection. A same-name Task
Scheduler entry exists, but its action is not positively recognized as belonging
to this installation. The application refuses to modify or delete it, the
installer disables startup mutation in this state, and the uninstaller preserves
the foreign task. You may inspect Task Scheduler manually; do not delete the
task blindly.

### SmartScreen or unknown-publisher warning

Public builds are currently unsigned. Download only from the official repository
release, verify the repository and release tag, and optionally verify SHA-256.
Do not disable SmartScreen or system-wide security protections. A checksum
downloaded from the same release detects corruption relative to that checksum,
but does not independently authenticate the publisher.

### Update checking or installation fails

Start with:

```powershell
usage-indicator check-update
```

Failures can mean GitHub is unavailable, the embedded repository URL is missing
or invalid, release metadata is malformed, exact installer or checksum assets
are missing, the checksum record names a different installer, SHA-256
verification fails, another update is already running, or the installer cannot
be launched, the private installer guard rejects the existing layout, installed
version validation fails, or a previously running companion cannot restart.

When another update holds the lock, the exact message is:

```text
An update is already in progress.
```

Do not replace installed files manually.

Post-installer failures print the exact installer log path. Exit `3010` means
the target version was installed and validated, but Windows must restart before
the companion can be started again.

### Bug-report diagnostic information

Include this compact diagnostic bundle:

```powershell
usage-indicator version
usage-indicator status
codex --version
Get-Command codex -All
Get-TimeZone
```

Also include Windows version and architecture; whether Codex Desktop was visible
or minimized; whether the issue followed installation, startup, update, or
account switching; a relevant screenshot; and whether `CODEX_CLI_PATH` is
configured. Do not include ChatGPT tokens, API keys, Codex credential files,
browser data, or authentication secrets.

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
UsageIndicatorForCodex-Setup-v<version>.exe
UsageIndicatorForCodex-Setup-v<version>.exe.sha256
```

The exact version comes from `Directory.Build.props` through
`UsageIndicatorProductVersion`.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the installer build, contract checks,
and contribution requirements.

## Limitations and license

- Desktop and CLI account identities are not automatically correlated.
- Only the configured CLI account's ChatGPT-plan limits are shown; OpenAI
  Platform API usage is not substituted.
- Window detection depends on current Codex Desktop process, package, title, and
  native window-role conventions.
- Builds are unsigned; SmartScreen and clean-machine installer behavior require
  manual Windows verification.
- A same-release checksum is integrity evidence, not independent publisher
  authentication.
- Windows 10 is unsupported. Windows 11 Arm64 installation is permitted only
  through x64 emulation and remains unverified.

This project is licensed under the [MIT License](LICENSE). The installer
displays that license and installs the repository-sourced copy as
`app\LICENSE.txt`.
