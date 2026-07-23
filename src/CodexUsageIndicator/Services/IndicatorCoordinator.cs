using System.Windows.Threading;
using CodexUsageIndicator.Core;
using CodexUsageIndicator.Interop;
using CodexUsageIndicator.Views;

namespace CodexUsageIndicator.Services;

internal sealed class IndicatorCoordinator : IDisposable
{
    private readonly CodexWindowTracker _tracker;
    private readonly IUsageProvider _usageProvider;
    private readonly UsageOverlayWindow _overlay;
    private readonly UserSettingsStore _settingsStore;
    private readonly DispatcherTimer _refreshTimer;
    private readonly CoalescingRefreshRunner _refreshRunner = new();
    private UserSettings _settings;
    private nint _activeCodexWindow;
    private UsageSnapshot? _snapshot;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private int _refreshGeneration;
    private bool _disposed;

    public IndicatorCoordinator(CodexWindowTracker tracker, IUsageProvider usageProvider, UsageOverlayWindow overlay, UserSettingsStore settingsStore)
    {
        _tracker = tracker;
        _usageProvider = usageProvider;
        _overlay = overlay;
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(force: true);
        _tracker.ActiveWindowChanged += TrackerOnActiveWindowChanged;
        _overlay.RetryRequested += async (_, _) => await RefreshAsync(force: true);
    }

    public void Start()
    {
        _tracker.Start();
        _refreshTimer.Start();
    }

    public void ToggleEnabled()
    {
        _settings = _settings with { Enabled = !_settings.Enabled };
        _settingsStore.Save(_settings);
        if (_settings.Enabled)
        {
            UpdatePlacement();
            _ = RefreshAsync(force: true);
        }
        else
        {
            _overlay.Hide();
        }
    }

    public bool IsEnabled => _settings.Enabled;

    private void TrackerOnActiveWindowChanged(object? sender, nint? windowHandle)
    {
        _ = _overlay.Dispatcher.InvokeAsync(async () =>
        {
            if (_disposed)
            {
                return;
            }

            if (windowHandle is null)
            {
                _activeCodexWindow = 0;
                InvalidateUsage();
                _overlay.Hide();
                return;
            }

            var changedWindow = _activeCodexWindow != windowHandle.Value;
            _activeCodexWindow = windowHandle.Value;
            if (!_settings.Enabled)
            {
                _overlay.Hide();
                return;
            }

            UpdatePlacement();
            if (changedWindow || DateTimeOffset.UtcNow - _lastRefresh > TimeSpan.FromMinutes(1))
            {
                await RefreshAsync(force: true);
            }
        });
    }

    private Task RefreshAsync(bool force) => _refreshRunner.RunAsync(() => RefreshOnceAsync(force));

    private async Task RefreshOnceAsync(bool force)
    {
        if (_disposed || !_settings.Enabled || _activeCodexWindow == 0 || (!force && DateTimeOffset.UtcNow - _lastRefresh < TimeSpan.FromMinutes(1)))
        {
            return;
        }

        var generation = ++_refreshGeneration;
        Render(IndicatorState.Loading, null);
        try
        {
            var snapshot = await _usageProvider.ReadAsync(CancellationToken.None);
            if (_disposed || generation != _refreshGeneration || _activeCodexWindow == 0)
            {
                return;
            }

            if (_snapshot is not null && !string.Equals(_snapshot.AccountFingerprint, snapshot.AccountFingerprint, StringComparison.Ordinal))
            {
                InvalidateUsage();
                Render(IndicatorState.Loading, null);
            }

            _snapshot = snapshot;
            _lastRefresh = DateTimeOffset.UtcNow;
            Render(IndicatorState.Available, snapshot);
        }
        catch (Exception)
        {
            if (!_disposed && generation == _refreshGeneration && _activeCodexWindow != 0)
            {
                _lastRefresh = DateTimeOffset.UtcNow;
                Render(IndicatorState.Unavailable, null);
            }
        }
    }

    private void InvalidateUsage()
    {
        _refreshGeneration++;
        _snapshot = null;
        _lastRefresh = DateTimeOffset.MinValue;
    }

    private void Render(IndicatorState state, UsageSnapshot? snapshot)
    {
        if (_activeCodexWindow == 0 || !_tracker.TryGetWindowRect(_activeCodexWindow, out var rect))
        {
            _overlay.Hide();
            return;
        }

        var layout = IndicatorPresentation.SelectLayout(rect.Width);
        if (layout == OverlayLayout.Hidden)
        {
            _overlay.Hide();
            return;
        }

        _overlay.Render(state, snapshot, layout);
        _overlay.Show();
        _overlay.Position(_activeCodexWindow, rect, _settings);
    }

    private void UpdatePlacement()
    {
        Render(_snapshot is null ? IndicatorState.Loading : IndicatorState.Available, _snapshot);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshTimer.Stop();
        _tracker.ActiveWindowChanged -= TrackerOnActiveWindowChanged;
        _tracker.Dispose();
        _overlay.Close();
    }
}
