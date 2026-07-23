# Contributing

## Development setup

Use Windows 11 x64 with a .NET SDK capable of targeting `net8.0-windows`. A Codex CLI account is not required for the normal build and tests.

From the repository root:

```powershell
dotnet restore .\UsageIndicatorForCodex.sln
dotnet build .\UsageIndicatorForCodex.sln --configuration Release --no-restore
dotnet run --project .\tests\UsageIndicatorForCodex.Tests\UsageIndicatorForCodex.Tests.csproj --configuration Release --no-build
```

Keep changes focused and add regression coverage to the existing test harness for behavior changes. Document every new setting, default, validation rule, command, external dependency, migration rule, or security boundary.

For release-path changes, also run:

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

Do not weaken the package-content checks.

## Security-sensitive changes

Do not add credential-file access, browser-session access, authentication mutation, model requests, Codex Desktop modification, or automatic CLI installation/update. Treat `CODEX_CLI_PATH`, executable launching, named pipes, Task Scheduler, settings migration, and release packaging as security-sensitive boundaries.

Use synthetic account and rate-limit data. The normal suite must remain independent of a logged-in CLI. Account-backed probes require deliberate manual invocation and must not expose returned values.

## Repository hygiene

Do not commit build/publish output, PDBs, test results, logs, caches, settings, environment files, archives, IDE state, or local runtime copies. Before submitting:

```powershell
git status --short --ignored
git diff --check
```

Verify that only intended source and documentation files are tracked. Do not use broad staging commands when unrelated work is present.

## License

Contributions are accepted under the repository’s [MIT License](LICENSE).
