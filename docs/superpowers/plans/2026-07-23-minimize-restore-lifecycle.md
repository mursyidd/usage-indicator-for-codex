# Minimize and Restore Lifecycle Implementation Plan

> **For agentic workers:** Execute this single task test-first and obtain a fresh read-only review after implementation.

**Goal:** Restore the title-bar usage indicator immediately after Codex is restored from minimization.

**Architecture:** Keep the attached Codex handle while it is iconic and represent minimization as a lifecycle state rather than detachment. The coordinator hides without discarding ownership or cached usage, then renders immediately when the tracker reports restoration.

**Tech Stack:** C# 13, .NET 10, WPF, Win32 WinEvents, existing console test harness

## Global Constraints

- Do not add arbitrary sleeps or a polling loop.
- Destroyed and hidden non-minimized windows must still detach.
- Preserve foreground switching, foreign-focus retention, and refresh throttling.
- Do not commit; stage task files only after verification and review.

---

### Task 1: Preserve attachment across minimize and restore

**Files:**
- Modify: `src/UsageIndicatorForCodex/Services/CodexWindowTracker.cs`
- Modify: `src/UsageIndicatorForCodex/Services/IndicatorCoordinator.cs`
- Test: `tests/UsageIndicatorForCodex.Tests/Program.cs`

**Interfaces:**
- `CodexWindowTracker` produces `CodexWindowChange.Minimized` and `CodexWindowChange.Restored`.
- `SelectAttachedWindow` consumes separate eligibility and minimized-state predicates.
- `IndicatorCoordinator` retains `_activeCodexWindow` and `_snapshot` for `Minimized`.

- [ ] Add regression assertions showing that an attached iconic window remains selected, an eligible foreground Codex window can replace it, and a non-iconic ineligible window detaches.
- [ ] Run the test project and confirm the new call contract fails before implementation.
- [ ] Add minimized-state tracking to `CodexWindowTracker`, special-case destruction, and publish one-shot minimized/restored changes.
- [ ] Update `IndicatorCoordinator` to hide without detaching on minimize and render immediately on restore.
- [ ] Run the complete test project and confirm all checks pass.
- [ ] Review the completed diff against the acceptance criteria, rerun verification after any blocker correction, and stage only the task files.
