using System.Diagnostics;
using CodexUsageIndicator.Interop;

namespace CodexUsageIndicator.Services;

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
    private bool _disposed;

    public CodexWindowTracker()
    {
        _callback = OnWindowEvent;
    }

    public event EventHandler<nint?>? ActiveWindowChanged;

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
        PublishAttachedWindow();
    }

    public bool TryGetWindowRect(nint windowHandle, out NativeMethods.Rect rect) => NativeMethods.GetWindowRect(windowHandle, out rect);

    private void OnWindowEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (eventType == NativeMethods.EventObjectLocationChange && !ShouldPublishLocationChange(_attachedWindow, hwnd, idObject))
        {
            return;
        }

        PublishAttachedWindow();
    }

    private void PublishAttachedWindow()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        _attachedWindow = SelectAttachedWindow(_attachedWindow, foreground, IsEligibleCodexWindow);
        ActiveWindowChanged?.Invoke(this, _attachedWindow == 0 ? null : _attachedWindow);
    }

    internal static nint SelectAttachedWindow(nint attachedWindow, nint foregroundWindow, Func<nint, bool> isEligible)
    {
        if (isEligible(foregroundWindow))
        {
            return foregroundWindow;
        }

        return attachedWindow != 0 && isEligible(attachedWindow) ? attachedWindow : 0;
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

    internal static bool IsCodexDesktopMainWindow(string processName, string packageFamilyName, string windowTitle) =>
        string.Equals(processName, "ChatGPT", StringComparison.OrdinalIgnoreCase)
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
                && IsCodexDesktopMainWindow(process.ProcessName, packageFamilyName, NativeMethods.ReadWindowText(hwnd));
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
