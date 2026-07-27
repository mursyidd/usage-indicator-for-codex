# Contributing

## Development setup

Use Windows 11 x64 with a .NET SDK capable of targeting `net8.0-windows`. A
Codex CLI account is not required for normal builds and tests.

```powershell
dotnet restore .\UsageIndicatorForCodex.sln
dotnet build .\UsageIndicatorForCodex.sln --configuration Release --no-restore
dotnet run --project .\tests\UsageIndicatorForCodex.Tests\UsageIndicatorForCodex.Tests.csproj --configuration Release --no-build
.\tests\repository-contract.ps1
```

Keep changes focused and add regression coverage for behavior changes. Document
every new setting, default, validation rule, command, external dependency,
migration rule, or security boundary.

## Release-path verification

Release builds also require:

- an x64 MSVC compiler, linker, and library manager;
- Inno Setup 6;
- an explicit GitHub `RepositoryUrl`, or an existing usable `origin` remote.

The repository owner must never be guessed. GitHub Actions derives the URL from
`${{ github.server_url }}/${{ github.repository }}`.

```powershell
$repositoryUrl = 'https://github.com/OWNER/REPOSITORY'
$publish = '.\artifacts\publish\win-x64'
$updateHost = '.\artifacts\update-host'
$launcherProbe = '.\artifacts\launcher-probe'
$release = '.\artifacts\release'

dotnet restore .\src\UsageIndicatorForCodex\UsageIndicatorForCodex.csproj --runtime win-x64
dotnet publish .\src\UsageIndicatorForCodex\UsageIndicatorForCodex.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishProfile=win-x64-self-contained `
  -p:RepositoryUrl=$repositoryUrl `
  --output $publish

.\scripts\build-launcher.ps1 `
  -OutputPath .\artifacts\usage-indicator.exe

dotnet publish .\tests\UsageIndicatorForCodex.LauncherProbe\UsageIndicatorForCodex.LauncherProbe.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output $launcherProbe

.\tests\installed-launcher-contract.ps1 `
  -InstalledLauncher .\artifacts\usage-indicator.exe `
  -LauncherProbe (Join-Path $launcherProbe 'UsageIndicatorForCodex.LauncherProbe.exe')

.\scripts\publish-update-host.ps1 `
  -OutputDirectory $updateHost `
  -RepositoryUrl $repositoryUrl

.\scripts\build-installer.ps1 `
  -PublishDirectory $publish `
  -InstalledLauncher .\artifacts\usage-indicator.exe `
  -UpdateHostPath (Join-Path $updateHost 'UsageIndicatorForCodex.UpdateHost.exe') `
  -OutputDirectory $release `
  -RepositoryUrl $repositoryUrl

. .\scripts\product-metadata.ps1

$metadata = Get-UsageIndicatorProductMetadata `
  -RepositoryUrl $repositoryUrl

$installerPath = Join-Path $release $metadata.InstallerAssetName

.\scripts\release-assets.ps1 `
  -InstallerPath $installerPath `
  -OutputDirectory $release

.\tests\installer-contract.ps1 `
  -InstallerPath $installerPath
.\tests\update-host-integration.ps1 `
  -UpdateHostPath (Join-Path $updateHost 'UsageIndicatorForCodex.UpdateHost.exe') `
  -InstallerPath $installerPath
.\tests\release-contract.ps1 -AssetDirectory $release
```

Do not weaken installed-launcher, installer-ownership, version, tag, or exact
two-asset checks. The installer must display repository `LICENSE` and install a
byte-identical copy as `app\LICENSE.txt`.

## Maintaining the Inno Setup pin

CI and release builds install Chocolatey package `innosetup` at the exact
version pinned in both workflow files. They run that exact `ISCC.exe` against
a no-output preprocessor probe that requires Inno's authoritative `Ver` value
to match the pin, then pass the successfully probed compiler path to the
installer build. File/product version metadata is not used because official
compiler binaries do not expose a reliable release version there.

To update the pin:

1. Confirm the intended version is published and verified in the Chocolatey
   community package repository.
2. Update the `choco install innosetup --version=...` command and
   `$expectedInnoSetupVersion` in both `.github/workflows/ci.yml` and
   `.github/workflows/release.yml`.
3. Update `$expectedInnoSetupVersion` in `tests/repository-contract.ps1`.
4. Run `.\tests\repository-contract.ps1`, compile the installer, and run
   `.\tests\installer-contract.ps1` before relying on CI or creating a release.

Never update only one workflow or remove the post-install compiler-version
check.

## Product version and commands

`Directory.Build.props` is the only product-version source. Do not copy the
version into application, launcher, installer, workflow, or packaging logic.
Tests and documentation may name the current expected value and asset contract.

When changing installed commands, update the managed parser, launcher contract,
README command list, startup/update behavior, and tests together.
`usage-indicator` is the only public command interface. Internal implementation
arguments must not be documented or accepted as public aliases.

## Security-sensitive changes

Do not add credential-file access, browser-session access, authentication
mutation, model requests, Codex Desktop modification, or automatic Codex CLI
installation/update.

Treat `CODEX_CLI_PATH`, executable launching, repository URL handling, GitHub
release parsing, downloads, checksums, installer execution, named pipes, Task
Scheduler, PATH ownership, settings migration, and release packaging as
security-sensitive boundaries.

Updates must remain explicit. The cached standalone host must verify the exact
versioned installer against its exact checksum before graceful process stopping,
use only the guarded bootstrap-v1 `/CLIUPDATE` contract, validate all three
installed version sources, and restart only when the companion was previously
running and the installer returned `0`. Validated `3010` must remain restart
required without ordinary success or application restart.

Never turn `/CLIUPDATE` into silent first installation, pass `/DIR`, overwrite
the stable launcher during a CLI update, mutate startup/PATH ownership in that
mode, force-terminate the application, add scheduled updates, or publish
UpdateHost as a third release asset. Legacy installations require one
interactive transition. `update` acquires the distinct per-user update mutex
before any network access and holds it through validation/restart; all exit paths
release it. `check-update` remains lock-free.

Startup ownership must remain path-specific. Both exact canonical forms are
owned: `UsageIndicatorForCodex.Gui.exe --background` and its exact sibling
`UsageIndicatorForCodex.exe --background`. Foreign canonical and legacy tasks
are preserved. Ownership collisions exit `2`, operational inspection failures
exit `1`, and uninstall removes only positively recognized owned tasks. The
sibling launcher action exists only for internal upgrade compatibility and must
not be promoted as a user command.

The installer targets Windows 11 with `ArchitecturesAllowed=x64compatible`.
That permits Arm64 only through x64 emulation; Arm64 remains unverified and
Windows 10 remains unsupported.

Use synthetic account, release, asset, and rate-limit data in normal tests.
Account-backed or network-backed probes require deliberate manual invocation
and must not expose returned values.

## Repository hygiene

Do not commit build/publish output, PDBs, test results, logs, caches, settings,
environment files, archives, IDE state, or local runtime copies.

```powershell
git status --short --ignored
git diff --check
```

Verify that `.env.example` remains trackable, ignored local state still exists
when removed from the index, and only intended source/documentation files are
staged. Do not use broad staging commands when unrelated work is present.

## License

Contributions are accepted under the repository's [MIT License](LICENSE).
