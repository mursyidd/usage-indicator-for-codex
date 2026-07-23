# PowerShell Console Race Design

## Problem

The published `UsageIndicatorForCodex.exe` is a WPF `WinExe`. Windows PowerShell
therefore classifies it as a GUI application and returns its next interactive
prompt before the process exits. The application later calls
`AttachConsole(ATTACH_PARENT_PROCESS)` during WPF startup and writes help or
invalid-argument output into a console that PowerShell has already made
interactive again.

The published executable has PE subsystem
`IMAGE_SUBSYSTEM_WINDOWS_GUI`. Windows PowerShell 5.1 reports it as a Windows
application rather than a console application. Direct invocation returned
control after about 67 ms in the diagnostic run, while explicit process lifetime
measurements ranged from about 132 ms to 198 ms.

## Selected Architecture

The release contains two executable entry points with distinct responsibilities:

- `UsageIndicatorForCodex.exe` is a small native
  `IMAGE_SUBSYSTEM_WINDOWS_CUI` launcher and remains the canonical public
  command.
- `UsageIndicatorForCodex.Gui.exe` is the managed WPF application and remains a
  `WinExe`/`IMAGE_SUBSYSTEM_WINDOWS_GUI` application.

The native launcher performs no command parsing and contains no help text. It
locates `UsageIndicatorForCodex.Gui.exe` in its own directory, reconstructs a
Windows command line from the exact argument vector using the documented
backslash-and-quote escaping rules, and starts the GUI executable with inherited
standard handles.

For one or more arguments, the launcher waits for the GUI process and returns
the GUI process exit code unchanged. This includes recognized commands,
arguments containing quotes, multiple arguments, and malformed input.

For zero arguments or an exact sole `--background` argument, the launcher starts
the GUI process and returns without waiting. The launcher does not reinterpret
any other argument.

## GUI Launch and Console Behavior

The managed application remains the only component that parses commands,
produces help text, selects stdout versus stderr, and assigns command exit codes.
Its existing parent-console attachment remains responsible for console output
when invoked through the launcher.

When the public launcher is started without arguments from a console-free
desktop context, it may hide only the newly allocated launcher console before
starting the GUI process. This is an implementation detail, not acceptance by
itself. A manual Explorer double-click test must show no noticeable console
window or flash. Any noticeable flash is a release blocker and requires a
different launch surface or bootstrap implementation.

`UsageIndicatorForCodex.Gui.exe` itself must never allocate a console.

## Startup Registration and Migration

`App.GetExecutablePath()` resolves to `UsageIndicatorForCodex.Gui.exe` after the
assembly rename. New `--install` registrations therefore point directly to:

```text
UsageIndicatorForCodex.Gui.exe --background
```

Existing scheduled tasks that point to:

```text
UsageIndicatorForCodex.exe --background
```

remain functional because the new launcher treats the exact sole
`--background` argument as asynchronous GUI startup. The managed startup
migration path may replace a recognized canonical launcher-backed task with the
direct GUI path, but it must not broaden legacy-task recognition or delete
unrelated tasks.

## Build and Packaging

The WPF project publishes as `UsageIndicatorForCodex.Gui.exe`. A deterministic
native build script compiles the launcher into the publish directory as
`UsageIndicatorForCodex.exe`. CI and release workflows build the native launcher
before publish-level contract verification.

The release packaging script requires both executables at the ZIP root, retains
the existing self-contained runtime requirements, and continues to reject PDBs,
source files, project metadata, test artifacts, and prohibited directory
components.

## Verification Contract

A publish-level PowerShell harness must fail against the old release and verify:

- `UsageIndicatorForCodex.exe` is `IMAGE_SUBSYSTEM_WINDOWS_CUI`;
- `UsageIndicatorForCodex.Gui.exe` is `IMAGE_SUBSYSTEM_WINDOWS_GUI`;
- a direct Windows PowerShell `--help` invocation completes output before the
  next prompt marker;
- invalid arguments write to stderr and return exit code `2`;
- arguments survive Windows quoting, including spaces, literal quotes, empty
  arguments, multiple arguments, and malformed input;
- lifecycle commands return the managed process exit code unchanged;
- no-argument and `--background` launches return promptly and leave no launcher
  process after GUI startup;
- new startup registration targets the GUI executable directly;
- the final ZIP contains both executables and no prohibited files.

Final acceptance also includes the full managed test suite, release build,
self-contained publish, package validation, disposable ZIP extraction,
post-extraction CLI and GUI smoke tests, and a manual Explorer double-click
flash check.

## Repository Constraints

- Preserve all staged work that existed before this task.
- Keep generated diagnostics, publish output, extracted ZIP contents, and the
  ZIP ignored and unstaged.
- Stage only this task's intended tracked changes after verification.
- Do not commit, amend, tag, push, or create a release.
