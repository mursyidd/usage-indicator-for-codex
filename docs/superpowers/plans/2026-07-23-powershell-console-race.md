# PowerShell Console Race Implementation Plan

> **For agentic workers:** Execute task-by-task with the Sol root as the sole
> writer and integrator. Every tracked-file implementation task requires a fresh
> narrow read-only exploration lane and a separate fresh post-implementation
> review lane.

**Goal:** Make direct Windows PowerShell commands wait for complete console
output while preserving console-free normal GUI launch and all lifecycle
behavior.

**Architecture:** Publish a native console-subsystem launcher as
`UsageIndicatorForCodex.exe` and rename the WPF `WinExe` to
`UsageIndicatorForCodex.Gui.exe`. The launcher transparently forwards the exact
argument vector and waits for all argument-bearing commands except the exact
sole `--background`; zero-argument launch is also asynchronous.

**Tech Stack:** .NET 8 WPF, native Win32 C, MSVC Build Tools, Windows PowerShell
5.1, GitHub Actions, self-contained win-x64 publish.

## Global Constraints

- The native launcher must not parse managed commands or duplicate help text.
- Every nonempty argument vector except exact sole `--background` is forwarded
  and waited synchronously.
- Standard output and error handles are inherited.
- The GUI process exit code is returned unchanged for synchronous launches.
- New startup registrations target `UsageIndicatorForCodex.Gui.exe --background`.
- Old `UsageIndicatorForCodex.exe --background` scheduled tasks remain usable.
- A noticeable Explorer-launch console flash is a release blocker.
- Existing staged changes are immutable and must remain staged.
- Generated diagnostics, publish output, extracted output, and ZIP files remain
  ignored and unstaged.
- No commit, push, tag, release, reset, revert, or broad staging command.

---

### Task 1: Publish-Level Failing Console Contract

**Files:**
- Create: `tests/console-contract.ps1`
- Create: `tests/UsageIndicatorForCodex.LauncherProbe/UsageIndicatorForCodex.LauncherProbe.csproj`
- Create: `tests/UsageIndicatorForCodex.LauncherProbe/Program.cs`
- Modify: `UsageIndicatorForCodex.sln`
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: a completed self-contained publish directory and optional ZIP path.
- Produces: a terminating PowerShell acceptance harness whose exit code is zero
  only when the launcher, GUI PE, console ordering, quoting, lifecycle, and
  archive contracts pass.

- [ ] **Step 1: Map safe lifecycle-test isolation**

Use a fresh read-only explorer to identify how single-instance names, settings,
scheduled-task abstractions, and process cleanup can be isolated without
touching the user's running application or real Task Scheduler state.

- [ ] **Step 2: Write the PE and process helpers**

Implement PowerShell helpers that read the PE optional-header subsystem, run
Windows PowerShell 5.1 with redirected stdout/stderr, enforce a timeout, capture
the exact exit code, and fail with actionable output. Implement the .NET 8 probe
with two modes: its default mode serializes its received argument array and
returns an environment-selected exit code; `--verify-launcher` uses
`ProcessStartInfo.ArgumentList` to launch a disposable copy of the native
launcher with hard-coded exact argument vectors.

- [ ] **Step 3: Add the failing public/GUI subsystem assertions**

Require `UsageIndicatorForCodex.exe` to have subsystem value `3` and
`UsageIndicatorForCodex.Gui.exe` to have subsystem value `2`.

- [ ] **Step 4: Add output, quoting, and prompt-order assertions**

Run the real public launcher from Windows PowerShell 5.1 with `--help` and
invalid input. Assert stdout/stderr placement, exit code `0` or `2`, and that
the final help line precedes the next prompt marker. Run the launcher probe with
hard-coded vectors for a path containing spaces, a literal quote, an empty
argument, multiple arguments, and malformed input; compare serialized JSON
arrays element-by-element.

- [ ] **Step 5: Add lifecycle and asynchronous-launch assertions**

Replace only `UsageIndicatorForCodex.Gui.exe` inside a disposable publish copy
with the launcher probe apphost. Use unique files and environment variables to
select exit code `37`, serialize arguments, signal the probe PID, and hold the
probe alive. Exercise every lifecycle argument through the launcher without
touching the real startup task, settings, or running instance. Assert zero
arguments and `--background` return promptly, the recorded child remains alive,
the specific launcher PID has exited, and cleanup terminates only the recorded
probe child.

- [ ] **Step 6: Add ZIP assertions**

When an archive path is supplied, assert both executables are present at the ZIP
root and reuse the prohibited-file rules expected by the packaging script.

- [ ] **Step 7: Run the old artifact to prove red**

Run:

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  .\tests\console-contract.ps1 `
  -PublishDirectory .\artifacts\publish\win-x64
```

Expected: failure because the public executable has GUI subsystem `2` and
`UsageIndicatorForCodex.Gui.exe` is absent.

- [ ] **Step 8: Wire the harness into CI and release publish jobs**

Publish to a temporary directory, build/copy the launcher, and invoke the same
harness after packaging. Keep build/test jobs deterministic and avoid
production state.

- [ ] **Step 9: Run a fresh read-only review**

Provide the task contract, changed files, completed diff, and red-run evidence.
Resolve only BLOCKER findings before beginning Task 2.

### Task 2: Native Launcher and Managed GUI Split

**Files:**
- Create: `src/UsageIndicatorForCodex.Launcher/launcher.c`
- Create: `src/UsageIndicatorForCodex.Launcher/kernel32.def`
- Create: `src/UsageIndicatorForCodex.Launcher/shell32.def`
- Create: `src/UsageIndicatorForCodex.Launcher/user32.def`
- Create: `scripts/build-launcher.ps1`
- Modify: `src/UsageIndicatorForCodex/UsageIndicatorForCodex.csproj`
- Modify: `src/UsageIndicatorForCodex/CommandLineOptions.cs`
- Modify: `UsageIndicatorForCodex.sln` only if required for discoverability

**Interfaces:**
- Consumes: the exact argument vector produced from `GetCommandLineW` by
  `CommandLineToArgvW` in a CRT-free native entry point.
- Produces: `UsageIndicatorForCodex.exe`, which starts sibling
  `UsageIndicatorForCodex.Gui.exe`, inherits standard handles, and either waits
  and returns the child exit code or returns after successful asynchronous
  launch.

- [ ] **Step 1: Map the smallest MSVC build invocation**

Use a fresh read-only explorer to locate installed Visual Studio compiler,
linker, and library-manager binaries and confirm the GitHub Windows runner
components needed for x64 compilation. The local machine has compiler tools but
no registered Windows SDK, so the launcher must remain CRT- and SDK-header-free.

- [ ] **Step 2: Rename the managed assembly**

Change only the WPF assembly name to `UsageIndicatorForCodex.Gui` while retaining
`OutputType=WinExe`, `UseWPF=true`, and the existing managed command processor.

- [ ] **Step 3: Implement Windows argument quoting**

Use a CRT-free native entry point and `CommandLineToArgvW` to recover the exact
Windows argument vector. Quote every forwarded argument independently using the
Windows inverse rules: wrap when needed, double backslashes before quotes,
escape quotes, double trailing backslashes before the closing quote, and
preserve empty arguments. Commit minimal import definitions for only the
Kernel32, Shell32, and User32 APIs used by the launcher.

- [ ] **Step 4: Implement transparent process creation**

Resolve the launcher's directory with a dynamically sized process-heap buffer,
append `UsageIndicatorForCodex.Gui.exe`, build the child command line, inherit
handles with `CreateProcessW`, and report launch failures with `WriteFile` to
the inherited stderr handle. Compile with `/GS-`, `/Zl`, `/NODEFAULTLIB`, an
explicit native entry point, and `/SUBSYSTEM:CONSOLE`; generate import libraries
from the committed `.def` files so the local build does not depend on Windows
SDK headers or libraries.

- [ ] **Step 5: Implement wait policy**

Return asynchronously only for `argc == 1` or
`argc == 2 && wcscmp(argv[1], L"--background") == 0`. For every other vector,
wait for the GUI process and return `GetExitCodeProcess` unchanged.

- [ ] **Step 6: Implement console-free desktop startup**

For zero-argument launch only, hide a console created solely for the launcher
before starting the GUI. Do not hide an inherited caller console and do not
treat hiding as acceptance without the Explorer smoke test.

- [ ] **Step 7: Build and run the contract green**

Compile the launcher into a clean publish directory, then run
`tests/console-contract.ps1`. Expected: PE, help, invalid input, quoting,
exit-code, and orphan-process assertions pass.

- [ ] **Step 8: Run managed build and full tests**

Run Release restore/build and the full executable test project. Expected:
zero build errors and all tests pass.

- [ ] **Step 9: Run a fresh read-only review**

Review only the accepted launcher/GUI split contract, authorized files, diff,
and verification evidence. Resolve BLOCKER findings and rerun the full Task 2
verification.

### Task 3: Startup Migration, Packaging, Documentation, and Release Acceptance

**Files:**
- Modify: `src/UsageIndicatorForCodex/Services/StartupTaskManager.cs`
- Modify: `src/UsageIndicatorForCodex/App.xaml.cs` only if migration invocation changes
- Modify: `tests/UsageIndicatorForCodex.Tests/Program.cs`
- Modify: `scripts/package-release.ps1`
- Modify: `README.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: canonical and legacy scheduled-task metadata, publish directory, and
  release archive path.
- Produces: direct GUI startup registration, safe compatibility migration,
  validated dual-executable ZIP, and accurate user/build documentation.

- [ ] **Step 1: Map canonical-task migration invariants**

Use a fresh read-only explorer to identify the minimum recognition predicate
that distinguishes this product's old launcher-backed canonical task from an
unrelated task.

- [ ] **Step 2: Add failing scheduler tests**

Cover new installs targeting `UsageIndicatorForCodex.Gui.exe`, recognized
canonical tasks targeting sibling `UsageIndicatorForCodex.exe --background`,
unrelated canonical task preservation, legacy-name behavior, registration
failure ordering, and fail-open normal launch.

- [ ] **Step 3: Implement minimal migration**

Register the managed GUI path directly. Recognize and replace only the
canonical launcher-backed task shape owned by this product. Preserve the
existing legacy task predicate and safe ordering.

- [ ] **Step 4: Update package validation**

Require both root executables and retain every existing runtime and prohibited
artifact rule.

- [ ] **Step 5: Update workflows**

Install/discover MSVC deterministically, build the launcher, publish the GUI,
package both, and run the publish contract in CI and release workflows.

- [ ] **Step 6: Update README**

Document the public launcher, internal GUI executable, direct PowerShell
behavior, normal launch behavior, startup target, update compatibility, and
build/package verification commands without recommending `Start-Process -Wait`
as the fix.

- [ ] **Step 7: Publish and package**

Create ignored output under `artifacts`, produce
`usage-indicator-for-codex-win-x64.zip`, and run package plus console-contract
validation.

- [ ] **Step 8: Extract and repeat smoke tests**

Extract the ZIP to a disposable path containing spaces. Repeat direct
PowerShell `--help`, invalid-argument, lifecycle-safe checks, no-argument GUI
launch, and `--background`; verify no launcher processes remain.

- [ ] **Step 9: Perform manual Explorer double-click acceptance**

Launch the extracted public executable by Explorer double-click and observe the
entire startup. Record whether any console window or flash is noticeable. If
one is noticeable, stop and report a release blocker rather than accepting the
build.

- [ ] **Step 10: Run final full verification**

Freshly run restore, Release build, full tests, self-contained publish, native
launcher build, package validation, console contract, ZIP inspection, extracted
smokes, process-orphan checks, and Git status.

- [ ] **Step 11: Run final fresh read-only review**

Give the reviewer the complete accepted contract, changed-file list, final diff,
and fresh verification evidence. Correct BLOCKER findings only, rerun affected
verification, and decide final acceptance as root.

- [ ] **Step 12: Stage only intended tracked changes**

Use explicit path-based `git add` commands. Preserve all pre-existing staged
documentation and image changes, keep generated artifacts unstaged, and report
the final staged/unstaged/ignored status without committing.
