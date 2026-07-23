# Codex Usage Indicator

Codex Usage Indicator is an independent Windows companion that displays the most restrictive active Codex usage limit over a Codex Desktop title bar. It follows the Desktop window for placement only and does not modify the signed Codex Desktop installation.

The usage data comes from the ChatGPT-authenticated account in a separately installed local Codex CLI. Codex Desktop is **not** the usage or identity source, and the companion does not automatically correlate the Desktop account with the CLI account. If those accounts differ, the displayed usage can differ from the account visible in Desktop.

## Requirements and support

- Windows 11 x64. Windows 10 and Arm64 have not been verified by this project.
- To build: a .NET SDK capable of targeting `net8.0-windows` (the .NET 8 SDK or a compatible later SDK).
- To run the framework-dependent release: the x64 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
- A separately installed, compatible Codex CLI authenticated with ChatGPT.
- Codex Desktop for the followed window.

The repository does not install or update .NET, Codex CLI, or Codex Desktop. Upstream [.NET Windows support](https://learn.microsoft.com/dotnet/core/install/windows) and [.NET lifecycle](https://learn.microsoft.com/lifecycle/products/microsoft-net-and-net-core) still apply.

## Codex CLI configuration

By default, the companion uses the ordinary per-user npm launcher:

```text
%APPDATA%\npm\codex.cmd
```

This is an environment-based path, not a developer-specific installation path. To use another installation, set `CODEX_CLI_PATH` to the fully qualified path of its `.cmd` launcher:

```powershell
$env:CODEX_CLI_PATH = 'C:\Program Files\Codex CLI\codex.cmd'
```

Paths containing spaces are supported. Relative paths, non-`.cmd` files, missing launchers, logged-out CLIs, API-key authentication, incompatible responses, and malformed responses fail closed to `Usage unavailable`. The application intentionally does not search `PATH`, so it cannot silently select a different CLI installation.

After changing the CLI account or launcher, revalidate it:

```powershell
.\CodexUsageIndicator.exe --revalidate-cli
```

Revalidation reports only success or failure. It does not print identity, tokens, or usage values and does not compare the CLI account with Codex Desktop.

## Build, test, and run

From the repository root:

```powershell
dotnet restore .\tests\CodexUsageIndicator.Tests\CodexUsageIndicator.Tests.csproj
dotnet build .\src\CodexUsageIndicator\CodexUsageIndicator.csproj --configuration Release --no-restore
dotnet run --project .\tests\CodexUsageIndicator.Tests\CodexUsageIndicator.Tests.csproj --configuration Release --no-restore
dotnet run --project .\src\CodexUsageIndicator\CodexUsageIndicator.csproj --configuration Release --no-restore
```

The default test run is local and does not use an authenticated account. Optional account-backed probes exist in the test executable, but they are intentionally excluded from the normal suite and should not be run casually.

## Display states

- `Usage —` means the companion is loading or refreshing.
- `Usage unavailable` means no verified, current CLI-account rate-limit response was available. It never means 0% remaining. Clicking the unavailable indicator retries.
- A percentage is shown only after a verified ChatGPT-account response with an active reset window.
- The overlay hides when no eligible Codex Desktop window is visible or the title bar is too narrow.

The companion refreshes on attachment, periodically while Codex is active, and after eligible focus changes. It keeps the last attached visible Codex window when another application receives focus.

## Settings and controls

`Ctrl+Alt+U` toggles the overlay. The same setting can be toggled without opening the UI:

```powershell
.\CodexUsageIndicator.exe --toggle
```

Per-user settings are stored at:

```text
%LOCALAPPDATA%\CodexUsageIndicator\settings.json
```

Defaults are:

```json
{
  "Enabled": true,
  "HorizontalOffset": 0,
  "VerticalOffset": 6
}
```

Offsets are logical pixels and must be finite numbers from `-500` through `500`. The application accepts manual edits while it is stopped. A missing, malformed, or invalid settings file falls back atomically to all defaults.

## Publish and release artifact

The intended downloadable artifact is a framework-dependent Windows x64 ZIP:

```powershell
dotnet publish .\src\CodexUsageIndicator\CodexUsageIndicator.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained false `
  --output .\artifacts\publish\win-x64
```

Create the ZIP from these four files only:

```text
CodexUsageIndicator.exe
CodexUsageIndicator.dll
CodexUsageIndicator.deps.json
CodexUsageIndicator.runtimeconfig.json
```

Do not distribute PDBs, settings, logs, caches, test output, source archives, or local runtime copies. `artifacts/`, `publish/`, binaries, symbols, archives, and local settings are ignored and must not be committed.

## Startup, disable, and uninstall

Run the published executable once to create a per-user Task Scheduler logon task:

```powershell
.\CodexUsageIndicator.exe --install
```

The task launches the same executable with `--background` and retries a crash up to three times at one-minute intervals. Installing from a temporary location is unsafe because the task retains that exact executable path. Installation does not require writing to Codex Desktop.

To disable the overlay while keeping startup installed, press `Ctrl+Alt+U` or run `--toggle`. Run it again to re-enable the overlay.

To remove automatic startup:

```powershell
.\CodexUsageIndicator.exe --uninstall
```

`--uninstall` removes only the companion-owned scheduled task. To remove the companion completely:

1. Run `--uninstall`.
2. Exit the running companion.
3. Delete the published companion folder.
4. Optionally delete `%LOCALAPPDATA%\CodexUsageIndicator` to remove per-user settings.

These actions do not uninstall or modify Codex CLI or Codex Desktop.

## Security and privacy boundaries

The companion launches only the configured CLI as `app-server --stdio`, then sends `initialize`, `account/read` with `refreshToken: false`, and `account/rateLimits/read`. It derives a deterministic, truncated SHA-256 account fingerprint for in-memory response consistency and does not persist it. The fingerprint is not a substitute for protecting the underlying account identity.

The application does not:

- read Codex credential or browser files;
- print or store tokens or account identity;
- log usage values;
- sign in, sign out, refresh authentication, or alter authentication;
- send model, thread, turn, or other usage-consuming requests;
- modify Codex Desktop or read its package files; or
- install, replace, or silently update the CLI.

See [SECURITY.md](SECURITY.md) for vulnerability reporting and the complete credential-handling boundary.

## Current limitations

- Desktop and CLI account identities are not automatically correlated.
- Only the configured CLI account's ChatGPT-plan rate limits are shown; OpenAI Platform API usage is not substituted.
- Window detection depends on the current Codex Desktop process, package family, and title conventions and may need updates after a Desktop redesign.
- The project has automated unit/integration checks, but live CLI-account access, real Desktop attachment, Task Scheduler installation, startup recovery, and uninstall behavior require deliberate manual verification.
- Windows 10 and Arm64 behavior are not currently claimed.

## Contributing and license

See [CONTRIBUTING.md](CONTRIBUTING.md) before submitting changes.

No project license has been selected. Until the repository owner selects and adds one, the source is not offered under an open-source license and publication remains blocked.
