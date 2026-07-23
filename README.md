# Usage Indicator for Codex

Usage Indicator for Codex is an independent Windows companion that displays the most restrictive active Codex usage limit over a Codex Desktop title bar. It follows the Desktop window for placement only and does not modify the signed Codex Desktop installation.

Usage comes from the ChatGPT-authenticated account in a separately installed local Codex CLI. Codex Desktop is not the usage or identity source, and the companion does not correlate the Desktop account with the CLI account. If those accounts differ, the displayed usage can differ from the account visible in Desktop.

## Requirements and support

- Windows 11 x64. Windows 10 and Arm64 are not currently verified.
- Codex Desktop for the followed window.
- A separately installed compatible Codex CLI authenticated with ChatGPT.
- For source development, a .NET SDK capable of targeting `net8.0-windows`.
- For framework-dependent source runs, the x64 .NET 8 Desktop Runtime.

The release archive is a self-contained `win-x64` build and does not require a separately installed .NET runtime. The repository does not install or update .NET, Codex CLI, or Codex Desktop.

## Install and launch

1. Download `usage-indicator-for-codex-win-x64.zip` from the project’s GitHub Releases page.
2. Verify that the download came from the intended repository and tag.
3. Extract the entire ZIP to a permanent directory. Do not run it from the ZIP or a temporary download directory.
4. Launch `UsageIndicatorForCodex.exe`.

A normal launch starts the companion immediately. It does not require installation and does not register automatic startup.

Public release builds are currently unsigned. Windows may show Microsoft Defender SmartScreen or an unknown-publisher warning. Check the repository/tag and inspect the downloaded file before choosing whether to run it. You can record its local SHA-256 hash with:

```powershell
Get-FileHash .\usage-indicator-for-codex-win-x64.zip -Algorithm SHA256
```

The project does not publish a code-signing identity, so the hash only identifies the downloaded bytes; compare it with a trusted value when one is provided through a separate trusted channel.

## Commands

Arguments are case-sensitive. Only one command may be supplied. Invalid, duplicate, or combined arguments return a nonzero exit code and do not start the companion.

```text
UsageIndicatorForCodex.exe                 Start immediately
UsageIndicatorForCodex.exe --background    Start immediately; used by Task Scheduler
UsageIndicatorForCodex.exe --install       Register/update automatic startup, then exit
UsageIndicatorForCodex.exe --uninstall     Remove owned startup tasks, then exit
UsageIndicatorForCodex.exe --toggle        Toggle the running or persisted enabled state
UsageIndicatorForCodex.exe --revalidate-cli
                                           Validate the configured CLI account safely
UsageIndicatorForCodex.exe --exit          Exit a running canonical instance
UsageIndicatorForCodex.exe --help
UsageIndicatorForCodex.exe -h              Show command help
```

`--install` is responsible only for startup registration. It does not continue into a normal application launch.

## Automatic startup

From the permanent extracted directory:

```powershell
.\UsageIndicatorForCodex.exe --install
```

This creates the current user’s `UsageIndicatorForCodex` Task Scheduler logon task. The task launches that exact executable path with `--background`, has no execution-time limit, and retries a crash up to three times at one-minute intervals.

If you move the extracted directory, run `--install` again from the new location. Installing from a temporary directory is unsafe because the task retains the exact executable path.

To remove automatic startup without deleting settings or application files:

```powershell
.\UsageIndicatorForCodex.exe --uninstall
```

Uninstall removes the canonical `UsageIndicatorForCodex` task. It removes a legacy task named `CodexUsageIndicator` only when the task has the old application's executable action (`CodexUsageIndicator.exe` with `--background`). An unrecognized same-name task is preserved for manual inspection.

## Update and legacy migration

For an update from a canonical release:

1. Run `.\UsageIndicatorForCodex.exe --exit`.
2. Replace the extracted files with the complete contents of the new ZIP.
3. Launch `UsageIndicatorForCodex.exe` normally.
4. If the installation directory changed, run `.\UsageIndicatorForCodex.exe --install` again.

For a one-time update from the legacy `CodexUsageIndicator` build:

1. Stop the running `CodexUsageIndicator.exe` from Task Manager or sign out of Windows. The old process does not understand the new `--exit` command.
2. Extract the new release to a permanent directory.
3. Launch `UsageIndicatorForCodex.exe` normally.

The first renamed launch prevents the old and new executables from running simultaneously. If a recognized legacy startup task exists, it registers `UsageIndicatorForCodex` first, then deletes `CodexUsageIndicator`. A registration failure leaves the legacy task intact for a later retry. A same-name task whose executable action cannot be confirmed as the old application is not deleted automatically and may require manual inspection in Task Scheduler.

Settings move from:

```text
%LOCALAPPDATA%\CodexUsageIndicator\settings.json
```

to:

```text
%LOCALAPPDATA%\UsageIndicatorForCodex\settings.json
```

If canonical settings already exist, they always win and are never overwritten by legacy settings. If canonical settings are absent and the legacy file is valid, the companion creates the canonical file atomically and retains the legacy file for rollback. Malformed or invalid legacy settings are not migrated.

## Codex CLI configuration

Without an override, the companion tries local launchers in this order:

1. a native `codex.exe` found on `PATH`;
2. `%APPDATA%\npm\codex.cmd`;
3. another `codex.cmd` found on `PATH`.

To select a specific installation, set `CODEX_CLI_PATH` to the absolute path of a `.exe` or `.cmd` launcher:

```powershell
$env:CODEX_CLI_PATH = 'C:\Program Files\Codex CLI\codex.cmd'
.\UsageIndicatorForCodex.exe
```

That form affects only the current shell and processes launched from it. To configure future interactive and Task Scheduler launches, set the user environment variable:

```powershell
[Environment]::SetEnvironmentVariable(
    'CODEX_CLI_PATH',
    'C:\Program Files\Codex CLI\codex.cmd',
    'User')
```

Sign out and back in, or otherwise ensure the new user environment is visible to the process that launches the companion. Then restart the companion:

```powershell
.\UsageIndicatorForCodex.exe --exit
.\UsageIndicatorForCodex.exe
```

Paths containing spaces are supported. The explicit override is authoritative: relative paths, unsupported extensions, missing files, launch failures, logged-out CLIs, API-key authentication, incompatible responses, and malformed responses fail closed to `Usage unavailable` rather than silently selecting another installation.

After changing the CLI launcher or account:

```powershell
.\UsageIndicatorForCodex.exe --revalidate-cli
```

Revalidation reports success or failure only. It does not print identity, tokens, or usage values and does not compare the CLI account with Codex Desktop.

## Display and controls

- `Usage —` means the companion is loading or refreshing.
- `Usage unavailable` means no verified current CLI-account rate-limit response was available. It never means 0% remaining. Clicking it retries.
- A percentage appears only after a verified ChatGPT-account response with an active reset window.
- The overlay hides when no eligible Codex Desktop window is visible, while its window is minimized, or when the title bar is too narrow.
- `Ctrl+Alt+U` and `--toggle` enable or disable the overlay.

The companion refreshes on attachment, periodically while Codex is active, and after a stale minimized or backgrounded window is restored. Movement and resizing reposition the indicator without launching a new CLI probe.

Per-user settings are:

```json
{
  "Enabled": true,
  "HorizontalOffset": 0,
  "VerticalOffset": 6
}
```

Offsets are logical pixels and must be finite values from `-500` through `500`. A missing, malformed, or invalid canonical file falls back to all defaults and does not fall back to legacy data.

## Build and test

From the repository root:

```powershell
dotnet restore .\UsageIndicatorForCodex.sln
dotnet build .\UsageIndicatorForCodex.sln --configuration Release --no-restore
dotnet run --project .\tests\UsageIndicatorForCodex.Tests\UsageIndicatorForCodex.Tests.csproj --configuration Release --no-build
dotnet run --project .\src\UsageIndicatorForCodex\UsageIndicatorForCodex.csproj --configuration Release --no-build
```

The normal test suite uses synthetic data and does not require an authenticated account. Optional account-backed probes require deliberate invocation and must not expose returned identity or usage values.

## Publish and package

```powershell
dotnet restore .\src\UsageIndicatorForCodex\UsageIndicatorForCodex.csproj --runtime win-x64
dotnet publish .\src\UsageIndicatorForCodex\UsageIndicatorForCodex.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --no-restore `
  -p:PublishProfile=win-x64-self-contained `
  --output .\artifacts\publish\win-x64

.\scripts\package-release.ps1 `
  -PublishDirectory .\artifacts\publish\win-x64 `
  -ArchivePath .\artifacts\usage-indicator-for-codex-win-x64.zip
```

The packaging script requires the canonical executable and self-contained runtime files. It rejects PDBs, source/build metadata, tests, `.git`, `bin`, and `obj` content before and after compression.

The CI workflow restores, builds, and tests pushes and pull requests to `master`. Pushing a `v*` tag runs the release workflow, repeats restore/build/test, publishes self-contained `win-x64`, verifies the archive, and creates a GitHub Release containing `usage-indicator-for-codex-win-x64.zip`. Creating or pushing tags remains a maintainer action.

## Complete removal

1. Run `.\UsageIndicatorForCodex.exe --exit`.
2. Run `.\UsageIndicatorForCodex.exe --uninstall`.
3. Delete the extracted application directory.
4. Optionally delete `%LOCALAPPDATA%\UsageIndicatorForCodex`.
5. If migrated from the old build, optionally delete `%LOCALAPPDATA%\CodexUsageIndicator`.

These actions do not uninstall or modify Codex CLI or Codex Desktop.

## Security and privacy boundaries

The companion launches only the configured CLI as `app-server --stdio`, then sends `initialize`, `account/read` with `refreshToken: false`, and `account/rateLimits/read`. It does not read credential files, browser profiles, tokens, or Codex Desktop package files; alter authentication; send model/thread/turn requests; or install/update the CLI.

See [SECURITY.md](SECURITY.md) for vulnerability reporting and the full credential-handling boundary.

## Limitations

- Desktop and CLI account identities are not automatically correlated.
- Only the configured CLI account’s ChatGPT-plan rate limits are shown; OpenAI Platform API usage is not substituted.
- Window detection depends on the current Codex Desktop process, package family, and title conventions.
- Live Desktop attachment, SmartScreen behavior, and clean-machine installation still require manual Windows verification.
- Windows 10 and Arm64 are not currently claimed.

## Contributing and license

See [CONTRIBUTING.md](CONTRIBUTING.md). This project is licensed under the [MIT License](LICENSE).
