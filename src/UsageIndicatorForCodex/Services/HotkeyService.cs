using System.Windows.Interop;
using UsageIndicatorForCodex.Interop;

namespace UsageIndicatorForCodex.Services;

internal sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x4355;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VirtualKeyU = 0x55;
    private bool _registered;

    public event EventHandler? ToggleRequested;

    public void Start()
    {
        if (_registered)
        {
            return;
        }

        _registered = NativeMethods.RegisterHotKey(0, HotkeyId, ModAlt | ModControl, VirtualKeyU);
        if (_registered)
        {
            ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;
        }
    }

    private void OnThreadMessage(ref MSG message, ref bool handled)
    {
        if (message.message == WmHotkey && message.wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            ToggleRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        ComponentDispatcher.ThreadPreprocessMessage -= OnThreadMessage;
        NativeMethods.UnregisterHotKey(0, HotkeyId);
        _registered = false;
    }
}
