using System.Diagnostics;
using UsageIndicatorForCodex.Interop;

namespace UsageIndicatorForCodex.Services;

internal enum CodexWindowChange
{
    Attached,
    Activated,
    BoundsChanged,
    Minimized,
    Restored,
    Detached
}

internal sealed class CodexWindowChangedEventArgs(nint? windowHandle, CodexWindowChange change) : EventArgs
{
    public nint? WindowHandle { get; } = windowHandle;
    public CodexWindowChange Change { get; } = change;
}

internal sealed class CodexWindowTracker : IDisposable
{
    internal const string CodexPackageFamilyName = "OpenAI.Codex_2p2nqsd0c76g0";
    private static readonly IReadOnlyList<uint> EventTypes = Array.AsReadOnly(new[]
    {
        NativeMethods.EventSystemForeground,
        NativeMethods.EventSystemMinimizeStart,
        NativeMethods.EventSystemMinimizeEnd,
        NativeMethods.EventObjectDestroy,
        NativeMethods.EventObjectShow,
        NativeMethods.EventObjectHide,
        NativeMethods.EventObjectLocationChange
    });
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly List<nint> _hooks = [];
    private nint _attachedWindow;
    private bool _attachedWindowIsMinimized;
    private bool _disposed;

    public CodexWindowTracker()
    {
        _callback = OnWindowEvent;
    }

    public event EventHandler<CodexWindowChangedEventArgs>? WindowChanged;

    internal static bool ObservesEvent(uint eventType) => EventTypes.Contains(eventType);

    public void Start()
    {
        ThrowIfDisposed();
        foreach (var eventType in EventTypes)
        {
            var hook = NativeMethods.SetWinEventHook(eventType, eventType, 0, _callback, 0, 0, NativeMethods.WineventOutOfContext);
            if (hook != 0)
            {
                _hooks.Add(hook);
            }
        }

        _attachedWindow = FindMostRecentlyActiveEligibleWindow();
        if (_attachedWindow != 0)
        {
            Publish(CodexWindowChange.Attached, _attachedWindow);
        }
    }

    public bool TryGetWindowRect(nint windowHandle, out NativeMethods.Rect rect) => NativeMethods.GetWindowRect(windowHandle, out rect);

    private void OnWindowEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (eventType == NativeMethods.EventObjectLocationChange)
        {
            if (ShouldPublishLocationChange(_attachedWindow, hwnd, idObject))
            {
                if (_attachedWindowIsMinimized || NativeMethods.IsIconic(_attachedWindow))
                {
                    ReevaluateAttachment();
                }
                else
                {
                    Publish(CodexWindowChange.BoundsChanged, _attachedWindow);
                }
            }

            return;
        }

        if (eventType == NativeMethods.EventSystemForeground)
        {
            PublishForegroundWindow();
            return;
        }

        if (eventType == NativeMethods.EventObjectDestroy
            && ShouldDetachDestroyedWindow(_attachedWindow, hwnd, idObject, idChild))
        {
            _attachedWindow = 0;
            _attachedWindowIsMinimized = false;
            Publish(CodexWindowChange.Detached, null);
            return;
        }

        if (hwnd == _attachedWindow)
        {
            ReevaluateAttachment();
        }
    }

    private void PublishForegroundWindow()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (!IsEligibleCodexWindow(foreground))
        {
            return;
        }

        var previous = _attachedWindow;
        var wasMinimized = previous == foreground && _attachedWindowIsMinimized;
        _attachedWindow = foreground;
        _attachedWindowIsMinimized = false;
        Publish(
            previous != _attachedWindow
                ? CodexWindowChange.Attached
                : wasMinimized
                    ? CodexWindowChange.Restored
                    : CodexWindowChange.Activated,
            _attachedWindow);
    }

    private void ReevaluateAttachment()
    {
        var previous = _attachedWindow;
        var isMinimized = previous != 0 && NativeMethods.IsIconic(previous);
        _attachedWindow = SelectAttachedWindow(
            previous,
            NativeMethods.GetForegroundWindow(),
            IsEligibleCodexWindow,
            window => window == previous && isMinimized);
        if (_attachedWindow != previous)
        {
            _attachedWindowIsMinimized = false;
            Publish(_attachedWindow == 0 ? CodexWindowChange.Detached : CodexWindowChange.Attached, _attachedWindow);
            return;
        }

        if (_attachedWindow == 0)
        {
            return;
        }

        if (isMinimized)
        {
            if (!_attachedWindowIsMinimized)
            {
                _attachedWindowIsMinimized = true;
                Publish(CodexWindowChange.Minimized, _attachedWindow);
            }

            return;
        }

        if (_attachedWindowIsMinimized)
        {
            _attachedWindowIsMinimized = false;
            Publish(CodexWindowChange.Restored, _attachedWindow);
        }
    }

    private void Publish(CodexWindowChange change, nint? windowHandle) =>
        WindowChanged?.Invoke(this, new CodexWindowChangedEventArgs(windowHandle, change));

    internal static nint SelectAttachedWindow(
        nint attachedWindow,
        nint foregroundWindow,
        Func<nint, bool> isEligible,
        Func<nint, bool> isMinimized)
    {
        if (isEligible(foregroundWindow))
        {
            return foregroundWindow;
        }

        return attachedWindow != 0 && (isEligible(attachedWindow) || isMinimized(attachedWindow)) ? attachedWindow : 0;
    }

    internal static nint SelectInitialAttachedWindow(IEnumerable<nint> windowsInZOrder, Func<nint, bool> isEligible)
    {
        foreach (var window in windowsInZOrder)
        {
            if (isEligible(window))
            {
                return window;
            }
        }

        return 0;
    }

    internal static bool ShouldPublishLocationChange(nint attachedWindow, nint hwnd, int idObject) =>
        attachedWindow != 0 && hwnd == attachedWindow && idObject == NativeMethods.ObjIdWindow;

    internal static bool ShouldDetachDestroyedWindow(nint attachedWindow, nint hwnd, int idObject, int idChild) =>
        attachedWindow != 0
        && hwnd == attachedWindow
        && idObject == NativeMethods.ObjIdWindow
        && idChild == 0;

    private static nint FindMostRecentlyActiveEligibleWindow()
    {
        var windows = new List<nint>();
        var callback = new NativeMethods.EnumWindowsProc((window, _) =>
        {
            windows.Add(window);
            return true;
        });
        NativeMethods.EnumWindows(callback, 0);
        return SelectInitialAttachedWindow(windows, IsEligibleCodexWindow);
    }

    internal static bool IsCodexDesktopMainWindow(string processName, string packageFamilyName, string windowTitle, nint extendedWindowStyle) =>
        (extendedWindowStyle & NativeMethods.WsExToolWindow) == 0
        && string.Equals(processName, "ChatGPT", StringComparison.OrdinalIgnoreCase)
        && string.Equals(packageFamilyName, CodexPackageFamilyName, StringComparison.OrdinalIgnoreCase)
        && (string.Equals(windowTitle, "Codex", StringComparison.Ordinal)
            || string.Equals(windowTitle, "ChatGPT", StringComparison.Ordinal));

    private static bool IsEligibleCodexWindow(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return NativeMethods.TryGetPackageFamilyName(process.Handle, out var packageFamilyName)
                && IsCodexDesktopMainWindow(
                    process.ProcessName,
                    packageFamilyName,
                    NativeMethods.ReadWindowText(hwnd),
                    NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
