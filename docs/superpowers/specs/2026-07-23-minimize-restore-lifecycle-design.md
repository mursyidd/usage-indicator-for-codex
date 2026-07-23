# Minimize and Restore Lifecycle Design

## Problem

`EVENT_SYSTEM_MINIMIZEEND` is emitted while a window is about to be restored. At that point `IsIconic` can still report that the Codex window is minimized. The tracker currently treats that temporary state as a permanent detachment, clears the remembered handle, and leaves later restore-related events unable to reposition the overlay immediately.

## Design

The tracker will distinguish a minimized attached window from a detached window.

- A minimized Codex window remains the attached window even though it is temporarily ineligible for display.
- The tracker publishes `Minimized` once when the attached handle enters the iconic state.
- The tracker publishes `Restored` once when the same handle becomes eligible again.
- A destroy event always detaches immediately, including when the window was minimized.
- A hidden, non-minimized window remains a genuine detachment.
- An eligible foreground Codex window can still replace the currently attached window.

The coordinator will enter a render-suppressed state, hide the overlay, invalidate any in-flight refresh generation, and cancel the active refresh on `Minimized`, but it will retain the active handle, owner relationship, and cached usage snapshot. Timer, refresh, and revalidation completions cannot render while this state is active. On `Restored`, the coordinator clears the suppressed state, shows and positions the cached indicator immediately, and refreshes usage only when no snapshot exists or the cached snapshot is older than one minute.

## Acceptance Criteria

1. Minimizing Codex hides the indicator without forgetting the Codex window handle.
2. Restoring Codex shows and positions the indicator on the first eligible restore-related event.
3. No fixed delay, sleep, or polling loop is introduced.
4. Closing Codex while minimized detaches the overlay and clears its owner.
5. Foreign focus and multiple eligible Codex-window selection continue to behave as before.
6. The existing test suite and new minimized-window regression tests pass.
