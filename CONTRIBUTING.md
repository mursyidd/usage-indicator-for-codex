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
  -Layout Portable `
  -OutputPath (Join-Path $publish 'UsageIndicatorForCodex.exe')
.\scripts\build-launcher.ps1 `
  -Layout Installed `
  -OutputPath .\artifacts\usage-indicator.exe

.\scripts\package-release.ps1 `
  -PublishDirectory $publish `
  -ArchivePath (Join-Path $release 'usage-indicator-for-codex-v0.1.0-win-x64.zip')

.\tests\console-contract.ps1 `
  -PublishDirectory $publish `
  -LauncherProbe .\tests\UsageIndicatorForCodex.LauncherProbe\bin\Release\net8.0\UsageIndicatorForCodex.LauncherProbe.exe `
  -InstalledLauncher .\artifacts\usage-indicator.exe `
  -ArchivePath (Join-Path $release 'usage-indicator-for-codex-v0.1.0-win-x64.zip')

.\scripts\build-installer.ps1 `
  -PublishDirectory $publish `
  -InstalledLauncher .\artifacts\usage-indicator.exe `
  -OutputDirectory $release `
  -RepositoryUrl $repositoryUrl

.\scripts\release-assets.ps1 `
  -InstallerPath (Join-Path $release 'UsageIndicatorForCodex-Setup-v0.1.0.exe') `
  -PortableArchivePath (Join-Path $release 'usage-indicator-for-codex-v0.1.0-win-x64.zip') `
  -OutputDirectory $release

.\tests\installer-contract.ps1 `
  -InstallerPath (Join-Path $release 'UsageIndicatorForCodex-Setup-v0.1.0.exe')
.\tests\release-contract.ps1 -AssetDirectory $release
```

Do not weaken package-content, launcher-layout, installer-ownership, version,
tag, or four-asset checks.

## Product version and commands

`Directory.Build.props` is the only product-version source. Do not copy the
version into application, launcher, installer, workflow, or packaging logic.
Tests and documentation may name the current expected value and asset contract.

When changing installed commands, update the managed parser, launcher contract,
README command list, startup/update behavior, and tests together. Preserve the
portable `UsageIndicatorForCodex.exe` switches unless a compatibility break is
explicitly approved.

## Security-sensitive changes

Do not add credential-file access, browser-session access, authentication
mutation, model requests, Codex Desktop modification, or automatic Codex CLI
installation/update.

Treat `CODEX_CLI_PATH`, executable launching, repository URL handling, GitHub
release parsing, downloads, checksums, installer execution, named pipes, Task
Scheduler, PATH ownership, settings migration, and release packaging as
security-sensitive boundaries.

Updates must remain explicit and interactive. They must verify the exact
versioned installer against its exact checksum and must not directly replace
running application files.

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
