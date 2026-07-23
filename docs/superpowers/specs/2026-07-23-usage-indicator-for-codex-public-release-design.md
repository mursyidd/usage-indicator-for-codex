# Usage Indicator for Codex Public Release Migration Design

Date: 23 July 2026
Status: Approved for implementation planning

## Objective

Rename the product and repository completely while preserving the existing WPF overlay and usage behavior, migrating installed users safely, and producing a verifiable public Windows x64 release.

## Canonical Identity

- Product: `Usage Indicator for Codex`
- Repository and local folder: `usage-indicator-for-codex`
- Solution, projects, namespaces, assembly, executable, settings folder, and scheduled task: `UsageIndicatorForCodex`
- Test project and assembly: `UsageIndicatorForCodex.Tests`
- Release archive: `usage-indicator-for-codex-win-x64.zip`

The legacy identifier `CodexUsageIndicator` is forbidden as a current identity. It may remain only in named compatibility constants, migration tests, or historical documentation that explicitly identifies it as legacy.

## Repository and Build Structure

Add `UsageIndicatorForCodex.sln` because the repository currently has no solution file. Rename the source and test directories, project files, project references, namespaces, XAML class name, friend assembly, assembly output, executable, workflow paths, documentation paths, and command examples.

Add a pull-request and branch CI workflow that restores, builds, and runs the canonical solution. Update the tag release workflow to restore, build, test, and publish the canonical application as a self-contained `win-x64` package. Both workflows must fail on warnings or errors that prevent those operations.

## Command Dispatch

Parse the full command line before creating settings, instance coordination, the overlay coordinator, the CLI provider, hotkeys, or scheduled-task services.

Accepted invocations are:

- no arguments: start the application immediately;
- `--background`: start the application immediately without changing startup registration;
- `--install`: register or update startup and exit;
- `--uninstall`: always attempt to remove the canonical startup registration, remove the legacy registration only when it is recognized as owned, and exit;
- `--toggle`: toggle the running or persisted enabled state and exit;
- `--revalidate-cli`: perform the existing safe CLI validation and exit;
- `--exit`: request that a running instance exit and then exit successfully when no instance is running;
- `--help` or `-h`: write usage to the parent console and exit successfully; if no parent console is available, display the same text in a message box.

Arguments are case-sensitive and mutually exclusive. Unknown arguments, duplicate arguments, and invalid combinations write an error and usage to the parent console (or display the same text in a message box when no console is available), return a nonzero exit code, and never initialize the application.

`--install` is responsible only for startup registration. It does not continue into a normal application launch. A no-argument or `--background` launch starts the application immediately.

## Settings Migration

The canonical settings path is `%LOCALAPPDATA%\UsageIndicatorForCodex\settings.json`. The legacy path is `%LOCALAPPDATA%\CodexUsageIndicator\settings.json`.

Migration follows these rules:

1. If the canonical settings file exists, load only it. Never overwrite it or fall back to the legacy file, even if the canonical file is invalid.
2. If the canonical file is absent and the legacy file contains valid settings, atomically create the canonical file without overwrite and return the migrated settings.
3. If another process creates canonical settings during migration, discard the temporary migration file and load the canonical file.
4. If the legacy file is missing, malformed, invalid, or unreadable, use defaults.
5. Retain the legacy file for rollback and do not mutate it.

Normal saves use an atomic temporary-file replacement so a crash cannot leave a partially written settings file.

## Startup Task Migration

The canonical Task Scheduler task is `UsageIndicatorForCodex`; the legacy task is `CodexUsageIndicator`.

Installation and automatic upgrade migration register or update the canonical task first. Only after successful registration may a same-name legacy task be deleted, and only when ownership is positively confirmed. Recognition requires exactly one executable action whose normalized, fully qualified path ends with `CodexUsageIndicator.exe`, with the exact argument `--background`. Unrecognized, ambiguous, multi-action, non-executable, malformed, or unreadable same-name tasks must be preserved. Registration failure leaves the legacy task untouched. The canonical task launches the current executable with `--background`, uses the current interactive user, has no execution-time limit, and retains the existing three retries at one-minute intervals.

A normal launch may perform this narrowly scoped compatibility migration when a recognized owned legacy task exists, but it must still start the application immediately. `--uninstall` always attempts to remove the canonical task, but removes the legacy task only when it is recognized as owned. Task-not-found results are ignored and other removal failures are surfaced.

## Cross-Version Instance Coordination

The canonical mutex and pipe identities use `UsageIndicatorForCodex`. Compatibility code also recognizes the legacy `CodexUsageIndicator` mutex and pipe.

A new instance must hold both canonical and legacy mutexes before becoming primary. If either identity is already owned, it must not start the overlay. Commands understood by both versions try the canonical pipe and then the legacy pipe so new clients can reach either version. `--exit` targets only the canonical pipe because the legacy application does not implement that command. A new primary listens on both pipe names so a legacy command client can reach it. This prevents an old and new executable from running simultaneously during an upgrade without leaving the legacy identity as the current product identity.

The new `Exit` instance command shuts down the primary cleanly through the existing current-user-only named-pipe boundary.

## Release Packaging

Publish with:

- configuration `Release`;
- runtime identifier `win-x64`;
- `SelfContained=true`;
- debug symbols and PDB generation disabled.

Create `usage-indicator-for-codex-win-x64.zip` from only the publish directory contents. Validate the canonical executable and required self-contained runtime files before archiving. Reject PDBs and any archive entry containing source, tests, `.git`, `bin`, or `obj`. Generated publish output and the ZIP remain unstaged.

## Documentation and Licensing

Update README, SECURITY, CONTRIBUTING, the existing product design history, workflow descriptions, command examples, build/publish paths, installation, update, uninstall, settings migration, `CODEX_CLI_PATH`, and release archive instructions.

Document that public binaries are unsigned. Users must verify the GitHub release source and may see Windows SmartScreen or publisher warnings. Do not suggest bypassing security controls indiscriminately.

Add the MIT license with copyright owner `Mursyid`, as established by repository history.

## Verification

Automated checks must cover:

- strict command parsing, help, invalid arguments, and `--exit`;
- canonical and legacy instance exclusion and pipe interoperability;
- settings precedence, valid migration, invalid legacy fallback, migration races, and atomic saves;
- canonical startup configuration, create-before-recognized-delete migration ordering, the full preserve-by-default ownership-recognition matrix, and canonical-always/recognized-legacy-only uninstall;
- canonical solution/project/assembly/output identities;
- archive name and prohibited-content checks.

Run fresh restore, build, test, publish, executable command, Task Scheduler, settings migration, old/new instance, and ZIP-content verification. Runtime tests must use isolated temporary settings paths and disposable test task names where possible. Any real task or settings state changed for verification must be restored afterward.

The three-browser matrix does not apply because this is a native WPF application with no browser route or web frontend. Manual verification remains necessary for the overlay’s visual attachment to a real Codex Desktop window, Windows SmartScreen behavior, and a clean-machine installation experience.

## Repository Hygiene

Preserve all pre-existing staged lifecycle changes through path renames. Do not commit, push, tag, create a release, create a GitHub repository, or rewrite history. Remove temporary diagnostics that are not useful for review. Keep the release ZIP unstaged for inspection. Rename the outer repository folder to `usage-indicator-for-codex` only after verification that does not depend on the old working directory.

At completion, stage only intended source and documentation changes in addition to the already staged work, and report the exact staged files, retained unstaged artifacts, verification evidence, remaining intentional legacy-name occurrences, final Git status, manual checks, and release blockers.
