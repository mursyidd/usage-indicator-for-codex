# Usage Indicator for Codex Public Release Migration Implementation Plan

> **For agentic workers:** Execute this plan task-by-task. The root agent is the sole writer. Every task requires a fresh read-only explorer or reviewer as specified by `AGENTS.md`.

**Goal:** Complete the canonical rename, preserve installed-user compatibility, and produce a verified public self-contained Windows x64 release without committing or publishing.

**Architecture:** Rename the current WPF application and test harness in place, then isolate upgrade compatibility in explicit legacy constants and migration paths. Parse commands before application initialization, coordinate both old and new instance identities during the transition, migrate settings and Task Scheduler state create-first, and drive CI/release packaging from one canonical solution.

**Tech Stack:** .NET 8, WPF, C#, Windows named mutexes and pipes, Task Scheduler COM, PowerShell, GitHub Actions.

## Global Constraints

- Product: `Usage Indicator for Codex`.
- Repository and local folder: `usage-indicator-for-codex`.
- Solution, projects, namespaces, assembly, executable, settings folder, and scheduled task: `UsageIndicatorForCodex`.
- Test project and assembly: `UsageIndicatorForCodex.Tests`.
- Release archive: `usage-indicator-for-codex-win-x64.zip`.
- `CodexUsageIndicator` may remain only as an explicitly named legacy migration identity or in historical documentation.
- Preserve the existing overlay UI, display, provider, and minimize/restore behavior.
- Preserve all five pre-existing staged files and their content through path renames.
- Do not commit, push, tag, create a release, create a GitHub repository, or rewrite history.
- Generated build, publish, test, graph, and ZIP artifacts remain unstaged.
- Browser verification does not apply to this native WPF application.

---

### Task 1: Canonical Repository and Build Identity

**Files:**

- Create: `UsageIndicatorForCodex.sln`
- Rename: `src/CodexUsageIndicator/` to `src/UsageIndicatorForCodex/`
- Rename: `src/UsageIndicatorForCodex/CodexUsageIndicator.csproj` to `src/UsageIndicatorForCodex/UsageIndicatorForCodex.csproj`
- Rename: `tests/CodexUsageIndicator.Tests/` to `tests/UsageIndicatorForCodex.Tests/`
- Rename: `tests/UsageIndicatorForCodex.Tests/CodexUsageIndicator.Tests.csproj` to `tests/UsageIndicatorForCodex.Tests/UsageIndicatorForCodex.Tests.csproj`
- Modify: every tracked `.cs` and `.xaml` file below those directories
- Modify: `src/UsageIndicatorForCodex/Properties/AssemblyInfo.cs`
- Modify: `src/UsageIndicatorForCodex/UsageIndicatorForCodex.csproj`
- Modify: `tests/UsageIndicatorForCodex.Tests/UsageIndicatorForCodex.Tests.csproj`
- Rename: `2026-07-23-codex-usage-indicator-design.md` to `2026-07-23-usage-indicator-for-codex-design.md`
- Modify: the renamed historical design only for current-identity references
- Rename current task spec and plan filenames to use the repository slug if their final names are not already canonical
- Preserve/rename: the five pre-existing staged lifecycle files

**Interfaces:**

- Produces: assembly `UsageIndicatorForCodex`, executable `UsageIndicatorForCodex.exe`, namespace root `UsageIndicatorForCodex`, friend assembly `UsageIndicatorForCodex.Tests`.
- Produces: one solution containing both canonical projects.

- [ ] **Step 1: Record the pre-rename index and staged blobs**

Run:

```powershell
git status --short
git diff --cached --name-status
git diff --cached
git ls-files
```

Expected: five existing staged files are visible and no unrelated unstaged tracked changes exist beyond this plan/spec.

- [ ] **Step 2: Move tracked paths without losing the index**

Use explicit `Move-Item` operations for the two project directories, project filenames, and historical design filename. Do not use recursive delete or broad Git reset operations.

- [ ] **Step 3: Replace current namespace and project identity**

All current code must use:

```csharp
namespace UsageIndicatorForCodex;
namespace UsageIndicatorForCodex.Core;
namespace UsageIndicatorForCodex.Interop;
namespace UsageIndicatorForCodex.Services;
namespace UsageIndicatorForCodex.Views;
```

The WPF application declaration must be:

```xml
<Application x:Class="UsageIndicatorForCodex.App"
```

The friend assembly must be:

```csharp
[assembly: InternalsVisibleTo("UsageIndicatorForCodex.Tests")]
```

The test project reference must be:

```xml
<ProjectReference Include="..\..\src\UsageIndicatorForCodex\UsageIndicatorForCodex.csproj" />
```

- [ ] **Step 4: Pin assembly and product metadata**

Add to the application project:

```xml
<AssemblyName>UsageIndicatorForCodex</AssemblyName>
<RootNamespace>UsageIndicatorForCodex</RootNamespace>
<Product>Usage Indicator for Codex</Product>
<Description>Displays Codex CLI account usage over the Codex Desktop title bar.</Description>
```

Add canonical `AssemblyName` and `RootNamespace` values to the test project.

- [ ] **Step 5: Create the canonical solution**

Run:

```powershell
dotnet new sln --name UsageIndicatorForCodex
dotnet sln .\UsageIndicatorForCodex.sln add .\src\UsageIndicatorForCodex\UsageIndicatorForCodex.csproj
dotnet sln .\UsageIndicatorForCodex.sln add .\tests\UsageIndicatorForCodex.Tests\UsageIndicatorForCodex.Tests.csproj
```

Expected: the solution lists exactly the application and test projects.

- [ ] **Step 6: Update canonical identity assertions in the test harness**

Add tests that assert:

```csharp
AssertEqual("UsageIndicatorForCodex", typeof(App).Assembly.GetName().Name!);
AssertEqual("UsageIndicatorForCodex.Tests", typeof(Program).Assembly.GetName().Name!);
```

Use an assembly-local test type instead of `Program` if top-level statements do not expose it.

- [ ] **Step 7: Verify Task 1**

Run:

```powershell
dotnet restore .\UsageIndicatorForCodex.sln
dotnet build .\UsageIndicatorForCodex.sln --configuration Release --no-restore
dotnet run --project .\tests\UsageIndicatorForCodex.Tests\UsageIndicatorForCodex.Tests.csproj --configuration Release --no-build
git diff --cached --check
git diff --check
```

Expected: restore/build/test succeed, no whitespace errors, and the staged minimize/restore behavior remains present under canonical paths.

- [ ] **Step 8: Fresh read-only Task 1 review**

Provide the reviewer the accepted canonical-name contract, changed file list, completed diff, and verification output. Correct only demonstrated blockers, rerun Task 1 verification, and do not implement advisory scope.

---

### Task 2: Strict Commands and Cross-Version Instance Coordination

**Files:**

- Create: `src/UsageIndicatorForCodex/CommandLineOptions.cs`
- Modify: `src/UsageIndicatorForCodex/App.xaml.cs`
- Modify: `src/UsageIndicatorForCodex/Services/SingleInstanceService.cs`
- Modify: `tests/UsageIndicatorForCodex.Tests/Program.cs`

**Interfaces:**

- Produces: `CommandLineAction` enum containing `Run`, `Install`, `Uninstall`, `Toggle`, `RevalidateCli`, `Exit`, `Help`, and `Invalid`.
- Produces: `CommandLineOptions.Parse(IReadOnlyList<string>)` with `Action`, `ExitCode`, and `Message`.
- Extends: `InstanceCommand` with `Exit`.
- Produces: canonical and legacy identity factories and ordered pipe candidates.

- [ ] **Step 1: Add failing parser tests**

Cover these exact cases:

```csharp
AssertEqual(CommandLineAction.Run, CommandLineOptions.Parse([]).Action);
AssertEqual(CommandLineAction.Run, CommandLineOptions.Parse(["--background"]).Action);
AssertEqual(CommandLineAction.Help, CommandLineOptions.Parse(["--help"]).Action);
AssertEqual(CommandLineAction.Help, CommandLineOptions.Parse(["-h"]).Action);
AssertEqual(CommandLineAction.Exit, CommandLineOptions.Parse(["--exit"]).Action);
AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--unknown"]).Action);
AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--toggle", "--exit"]).Action);
AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--help", "--help"]).Action);
AssertEqual(CommandLineAction.Invalid, CommandLineOptions.Parse(["--HELP"]).Action);
```

Also assert that invalid results have a nonzero exit code and help has zero.

- [ ] **Step 2: Implement the pure command parser**

Use a single exact-token switch. Return `Invalid` for any argument count above one and for unrecognized tokens. Define one canonical usage string containing all commands and the statement that normal launch starts immediately while `--install` only registers startup.

- [ ] **Step 3: Add failing dual-identity instance tests**

Tests must prove:

- a canonical primary owns both canonical and legacy mutexes;
- a second canonical instance is not primary;
- an externally held legacy mutex prevents a canonical primary;
- a new primary accepts commands on canonical and legacy pipe names;
- ordered command sending reaches a canonical or legacy server;
- `Exit` is parsed and delivered.

Use unique GUID-suffixed injected identity names so tests do not contend with real application instances.

- [ ] **Step 4: Implement transition identity ownership**

Represent identities explicitly:

```csharp
internal sealed record InstanceIdentity(string MutexName, string PipeName);
```

For the real user, build canonical names with `UsageIndicatorForCodex` and legacy names with `CodexUsageIndicator`. Acquire both mutexes on one owner thread before setting `IsPrimary=true`. Release every acquired mutex deterministically during disposal.

Run one pipe server loop per identity for a new primary. Keep `PipeOptions.CurrentUserOnly`. Add an ordered send method that tries canonical, then legacy, and distinguishes “no server” from an explicit failed response.

- [ ] **Step 5: Dispatch before normal application initialization**

In `App.OnStartup`, parse first. `Help` and `Invalid` render command text and shut down before creating settings, instance services, coordinator, provider, overlay, or hotkeys.

Dispatch rules:

```csharp
CommandLineAction.Run => StartApplication();
CommandLineAction.Install => InstallStartupAndShutdown();
CommandLineAction.Uninstall => UninstallStartupAndShutdown();
CommandLineAction.Toggle => SendOrPersistToggleAndShutdown();
CommandLineAction.RevalidateCli => RevalidateAndShutdown();
CommandLineAction.Exit => SendExitAndShutdown();
```

`HandleInstanceCommandAsync(InstanceCommand.Exit, ...)` must invoke a dispatcher shutdown and report success.

- [ ] **Step 6: Implement help output without changing normal UI behavior**

Keep `OutputType=WinExe`. Add a small console writer that attaches to the parent console and writes help/error there. If no console can be attached, show the same text in a message box titled `Usage Indicator for Codex`. Only help/error paths use this facility.

- [ ] **Step 7: Verify Task 2**

Run the canonical test harness plus published executable probes:

```powershell
.\UsageIndicatorForCodex.exe --help
.\UsageIndicatorForCodex.exe -h
.\UsageIndicatorForCodex.exe --unknown
.\UsageIndicatorForCodex.exe --help --exit
.\UsageIndicatorForCodex.exe --exit
```

Expected: help exits 0; invalid forms exit nonzero; none starts the overlay; exit succeeds with and without a primary; test harness proves dual-name coordination.

- [ ] **Step 8: Fresh read-only Task 2 review**

Review only parser strictness, pre-initialization behavior, current-user pipe security, cross-version exclusion, exit semantics, and preservation of existing commands. Correct blockers and rerun Task 2 verification.

---

### Task 3: Settings and Scheduled-Task Migration

**Files:**

- Modify: `src/UsageIndicatorForCodex/Services/UserSettings.cs`
- Modify: `src/UsageIndicatorForCodex/Services/StartupTaskManager.cs`
- Modify: `src/UsageIndicatorForCodex/App.xaml.cs`
- Modify: `tests/UsageIndicatorForCodex.Tests/Program.cs`

**Interfaces:**

- Produces: canonical and legacy settings path helpers.
- Produces: atomic settings save and create-if-absent migration.
- Produces: canonical `TaskName`, legacy `LegacyTaskName`, create-before-delete install/migration, and dual-name uninstall.

- [ ] **Step 1: Add failing settings migration tests**

Use isolated temporary directories to prove:

1. canonical path ends in `UsageIndicatorForCodex\settings.json`;
2. canonical valid settings win over different legacy settings;
3. canonical malformed settings produce defaults and do not fall back;
4. valid legacy settings migrate only when canonical is absent;
5. malformed legacy settings do not create canonical settings;
6. an existing canonical file is never overwritten;
7. saves leave no migration temporary files.

- [ ] **Step 2: Implement canonical/legacy paths and migration**

Expose a testable constructor:

```csharp
internal UserSettingsStore(string canonicalPath, string? legacyPath)
```

`Load` checks canonical existence first. When only legacy exists, deserialize and validate it, serialize to a unique same-directory temporary file with `Flush(true)`, and move with `overwrite:false`. On an `IOException` caused by a migration race, delete the temporary file and load the winning canonical file.

Normal `Save` writes a unique same-directory temporary file, flushes, and atomically replaces or moves it. Always remove abandoned temporary files in `finally`.

- [ ] **Step 3: Add failing startup migration-plan tests**

Extract a scheduler adapter or pure orchestration seam and prove the call order:

```text
Register UsageIndicatorForCodex
Delete CodexUsageIndicator
```

When registration throws, assert that legacy delete was never attempted. Assert uninstall attempts both names and ignores only missing-task HRESULT `0x80070002`.

- [ ] **Step 4: Implement canonical startup and migration**

Use:

```csharp
internal const string TaskName = "UsageIndicatorForCodex";
internal const string LegacyTaskName = "CodexUsageIndicator";
```

`Install` registers the canonical task with `--background`, then deletes the legacy task. Add a normal-launch compatibility migration that first detects the legacy task, registers canonical with the current executable, and then deletes legacy. Failure to migrate must leave legacy intact and must not prevent the normal application from starting.

`Uninstall` attempts canonical and legacy deletion independently. Preserve the current interactive-user configuration, three retries, one-minute interval, and no execution limit.

- [ ] **Step 5: Verify isolated settings migration**

Run the test harness and an isolated executable/process test that redirects LocalAppData or injects test paths without touching real user settings.

Expected: every precedence/race case passes and no temp files remain.

- [ ] **Step 6: Verify disposable scheduled-task behavior**

Use a uniquely suffixed disposable task name through the scheduler test seam. Verify registration XML/action path/`--background`, restart settings, create-before-delete ordering, and removal. Restore any pre-test task state and report it.

- [ ] **Step 7: Fresh read-only Task 3 review**

Review only settings precedence/atomicity, task ordering/failure handling, actual task configuration, and normal-launch behavior. Correct blockers and rerun Task 3 verification.

---

### Task 4: CI and Release Packaging

**Files:**

- Create: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release.yml`
- Modify: `src/UsageIndicatorForCodex/Properties/PublishProfiles/win-x64-self-contained.pubxml`
- Create: `scripts/package-release.ps1`
- Modify: `.gitignore` only if required for deterministic artifact hygiene
- Modify: `tests/UsageIndicatorForCodex.Tests/Program.cs` for packaging helper tests only when useful

**Interfaces:**

- Produces: reusable packaging script accepting publish directory and archive path.
- Produces: archive `usage-indicator-for-codex-win-x64.zip`.

- [ ] **Step 1: Add a deterministic release packaging script**

The script must:

```powershell
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$ArchivePath
)
```

Resolve both paths, require `UsageIndicatorForCodex.exe`, `coreclr.dll`, `hostfxr.dll`, and `hostpolicy.dll`, reject any `*.pdb`, and reject relative entries containing `.git`, `src`, `tests`, `bin`, or `obj`. Remove an existing target archive only when it is exactly the requested archive path, then compress only `$PublishDirectory\*`.

- [ ] **Step 2: Create CI workflow**

On pushes and pull requests to `master`, run:

```powershell
dotnet restore .\UsageIndicatorForCodex.sln
dotnet build .\UsageIndicatorForCodex.sln --configuration Release --no-restore
dotnet run --project .\tests\UsageIndicatorForCodex.Tests\UsageIndicatorForCodex.Tests.csproj --configuration Release --no-build
```

Use `actions/checkout@v4`, `actions/setup-dotnet@v4`, and .NET `8.0.x`.

- [ ] **Step 3: Correct the release workflow**

The tag workflow must restore and build the solution, run the tests, publish the canonical project with `--runtime win-x64 --self-contained true`, package with `scripts/package-release.ps1`, and upload only `usage-indicator-for-codex-win-x64.zip` to the GitHub Release.

- [ ] **Step 4: Verify publish metadata**

The profile must retain:

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>false</PublishSingleFile>
<DebugType>none</DebugType>
<DebugSymbols>false</DebugSymbols>
```

- [ ] **Step 5: Publish and package locally**

Run:

```powershell
dotnet publish .\src\UsageIndicatorForCodex\UsageIndicatorForCodex.csproj --configuration Release --runtime win-x64 --self-contained true --no-build --output .\artifacts\publish\win-x64
.\scripts\package-release.ps1 -PublishDirectory .\artifacts\publish\win-x64 -ArchivePath .\artifacts\usage-indicator-for-codex-win-x64.zip
```

If `--no-build` cannot consume the prior RID-specific build, perform the required RID-specific build first rather than weakening publish verification.

- [ ] **Step 6: Verify archive inventory**

Open the ZIP with `System.IO.Compression.ZipFile`, assert canonical executable/runtime entries, no PDB, and no prohibited path component. Record entry count and byte size. Keep the ZIP unstaged.

- [ ] **Step 7: Fresh read-only Task 4 review**

Review workflow syntax, restore/build/test/publish ordering, self-contained proof, packaging boundary, archive naming, and prohibited-content checks. Correct blockers and repeat local publish/package verification.

---

### Task 5: Public Documentation, License, Legacy Audit, and Folder Rename

**Files:**

- Modify: `README.md`
- Modify: `SECURITY.md`
- Modify: `CONTRIBUTING.md`
- Modify: `2026-07-23-usage-indicator-for-codex-design.md`
- Modify: staged lifecycle plan/spec path references
- Modify: current migration plan/spec path references
- Create: `LICENSE`
- Rename outer folder: `C:\Projects\UsageIndicatorForCodex` to `C:\Projects\usage-indicator-for-codex`

**Interfaces:**

- Produces: public release and support documentation aligned with runtime behavior.
- Produces: MIT license copyright `Mursyid`.

- [ ] **Step 1: Update README completely**

Document:

- canonical product/repository/executable/settings/task/archive names;
- normal launch starts immediately;
- `--install` only registers startup;
- every supported command including help/exit;
- safe update order and automatic legacy migration;
- install, update, disable, exit, uninstall, and complete removal;
- `CODEX_CLI_PATH` as an absolute `.exe` or `.cmd`, persistent user environment examples, quoting, precedence, and fail-closed behavior;
- self-contained `win-x64` requirements;
- public binaries are unsigned and may trigger SmartScreen/publisher warnings;
- build/test/publish/package commands using the solution and canonical paths.

- [ ] **Step 2: Update SECURITY and CONTRIBUTING**

Use the canonical product identity, correct framework/self-contained release wording, security reporting boundary, test commands, generated-artifact policy, and MIT contribution terms.

- [ ] **Step 3: Add MIT license**

Create the standard MIT text with:

```text
Copyright (c) 2026 Mursyid
```

- [ ] **Step 4: Audit all legacy occurrences**

Search tracked and relevant untracked review files:

```powershell
Get-ChildItem -Recurse -File |
  Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj|artifacts|runtime|graphify-out)\\' } |
  Select-String -Pattern 'CodexUsageIndicator|Codex Usage Indicator|codex-usage-indicator'
```

Every result must be classified in the final report as:

- explicit runtime migration constant;
- migration test;
- explicit historical documentation.

Remove every accidental current-identity occurrence.

- [ ] **Step 5: Run the complete fresh verification matrix**

Run canonical restore, build, tests, publish, command probes, isolated settings migration, disposable scheduled-task migration, old/new instance exclusion, packaging, archive inventory, workflow/static searches, `git diff --check`, and final source-name audit.

- [ ] **Step 6: Remove temporary diagnostics**

Remove `graphify-out/` and disposable test state. Keep only the useful release ZIP unstaged. Do not remove ignored user-owned `runtime/`.

- [ ] **Step 7: Stage only intended tracked changes**

Use explicit `git add -- <path>` arguments for every changed/renamed source and documentation path. Do not use `git add .` or `git add -A`. Confirm the pre-existing staged lifecycle work remains staged.

- [ ] **Step 8: Rename the outer repository folder**

From `C:\Projects`, resolve and verify both exact paths, confirm the target does not exist, then rename:

```powershell
Move-Item -LiteralPath 'C:\Projects\UsageIndicatorForCodex' -Destination 'C:\Projects\usage-indicator-for-codex'
```

Do not move any broader directory.

- [ ] **Step 9: Verify final state from the new path**

Run:

```powershell
git -C C:\Projects\usage-indicator-for-codex status --short --branch
git -C C:\Projects\usage-indicator-for-codex diff --cached --check
git -C C:\Projects\usage-indicator-for-codex diff --cached --name-status
git -C C:\Projects\usage-indicator-for-codex diff --name-status
```

Expected: all intended tracked changes are staged, no generated output is staged, the ZIP is the only useful unstaged artifact, and no unrelated work was altered.

- [ ] **Step 10: Fresh read-only Task 5 review**

Review documentation completeness, license owner/history evidence, legacy-name classifications, exact staged set, artifact state, and canonical folder path. Correct blockers, rerun affected verification, and prepare the completion audit.

## Final Completion Audit

Before claiming completion, map every numbered user requirement to fresh command/file/runtime evidence. Confirm:

1. canonical names across files, namespaces, outputs, docs, and outer folder;
2. safe task/settings migration without overwrite;
3. old/new mutual exclusion;
4. strict help/exit/invalid argument behavior without app startup;
5. install-only startup registration semantics;
6. CI and release restore/build/test/publish correctness;
7. valid self-contained ZIP name, contents, count, and size;
8. documentation and unsigned-build coverage;
9. MIT owner from `git shortlog -sne --all`;
10. only intentional legacy occurrences remain;
11. intended source changes are staged, generated artifacts are not.

Do not mark the goal complete while any evidence is missing, indirect, stale, or contradictory.
