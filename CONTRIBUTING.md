# Contributing

## Development setup

Use Windows 11 x64 with a .NET SDK capable of targeting `net8.0-windows`. A Codex CLI account is not required for the default build and tests.

From the repository root:

```powershell
dotnet restore .\tests\CodexUsageIndicator.Tests\CodexUsageIndicator.Tests.csproj
dotnet build .\src\CodexUsageIndicator\CodexUsageIndicator.csproj --configuration Release --no-restore
dotnet run --project .\tests\CodexUsageIndicator.Tests\CodexUsageIndicator.Tests.csproj --configuration Release --no-restore
```

Keep changes focused and add a regression check to the existing test harness for behavior changes. Document any new setting, default, validation rule, command-line option, external dependency, or security boundary.

## Security-sensitive changes

Do not add credential-file access, browser-session access, authentication mutation, model requests, Codex Desktop modification, or automatic CLI installation/update. Do not place real app-server responses, account identifiers, tokens, private paths, or usage data in tests, fixtures, issues, or pull requests.

Use synthetic account and rate-limit data. The normal test suite must remain independent of a logged-in CLI; account-backed probes require deliberate manual invocation and must not expose returned values.

## Repository hygiene

Do not commit build or publish output, PDBs, test results, logs, caches, settings, environment files, archives, IDE state, or local runtime copies. Before submitting a change, inspect `git status --short --ignored`, verify only intended source files are tracked, and run the full default test suite.

No license has been selected, so contributions must not assume an open-source license until the repository owner adds one.
