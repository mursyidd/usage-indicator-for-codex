using System.Windows.Threading;
using UsageIndicatorForCodex.Core;
using UsageIndicatorForCodex.Interop;
using UsageIndicatorForCodex.Views;

namespace UsageIndicatorForCodex.Services;

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
    private bool _activeCodexWindowIsMinimized;
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
        _tracker.WindowChanged += TrackerOnWindowChanged;
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
            if (_activeCodexWindow != 0)
            {
                _overlay.SetOwner(_activeCodexWindow);
            }

            UpdatePlacement();
            _ = RefreshAsync(force: true);
        }
        else
        {
            InvalidateUsage();
            _refreshRunner.Cancel();
            _overlay.Hide();
        }
    }

    public bool IsEnabled => _settings.Enabled;

    public Task<bool> RevalidateAsync()
    {
        if (_disposed)
        {
            return Task.FromResult(false);
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = CompleteRevalidationWhenRefreshFinishesAsync(
            _refreshRunner.ReplaceAsync(cancellationToken => RevalidateOnceAsync(cancellationToken, completion)),
            completion);
        return completion.Task;
    }

    private static async Task CompleteRevalidationWhenRefreshFinishesAsync(Task refresh, TaskCompletionSource<bool> completion)
    {
        try
        {
            await refresh;
        }
        catch
        {
        }

        completion.TrySetResult(false);
    }

    private void TrackerOnWindowChanged(object? sender, CodexWindowChangedEventArgs eventArgs)
    {
        _ = _overlay.Dispatcher.InvokeAsync(async () =>
        {
            if (_disposed)
            {
                return;
            }

            if (eventArgs.Change == CodexWindowChange.Minimized)
            {
                _activeCodexWindowIsMinimized = true;
                _refreshGeneration++;
                _refreshRunner.Cancel();
                _overlay.Hide();
                return;
            }

            if (eventArgs.Change == CodexWindowChange.Detached || eventArgs.WindowHandle is null)
            {
                _activeCodexWindow = 0;
                _activeCodexWindowIsMinimized = false;
                InvalidateUsage();
                _refreshRunner.Cancel();
                _overlay.SetOwner(0);
                _overlay.Hide();
                return;
            }

            _activeCodexWindow = eventArgs.WindowHandle.Value;
            _activeCodexWindowIsMinimized = false;
            if (!_settings.Enabled)
            {
                _overlay.Hide();
                return;
            }

            _overlay.SetOwner(_activeCodexWindow);
            UpdatePlacement();
            if (eventArgs.Change == CodexWindowChange.BoundsChanged)
            {
                return;
            }

            if (eventArgs.Change == CodexWindowChange.Attached ||
                (eventArgs.Change is CodexWindowChange.Activated or CodexWindowChange.Restored
                    && (_snapshot is null || DateTimeOffset.UtcNow - _lastRefresh > TimeSpan.FromMinutes(1))))
            {
                await RefreshAsync(force: true);
            }
        });
    }

    private Task RefreshAsync(bool force) => _refreshRunner.RunAsync(cancellationToken => RefreshOnceAsync(force, cancellationToken));

    private async Task RefreshOnceAsync(bool force, CancellationToken cancellationToken)
    {
        if (_disposed
            || !_settings.Enabled
            || _activeCodexWindow == 0
            || _activeCodexWindowIsMinimized
            || (!force && DateTimeOffset.UtcNow - _lastRefresh < TimeSpan.FromMinutes(1)))
        {
            return;
        }

        var generation = ++_refreshGeneration;
        Render(IndicatorState.Loading, null);
        try
        {
            var snapshot = await _usageProvider.ReadAsync(cancellationToken);
            if (_disposed
                || cancellationToken.IsCancellationRequested
                || generation != _refreshGeneration
                || _activeCodexWindow == 0
                || _activeCodexWindowIsMinimized)
            {
                return;
            }

            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!_disposed
                && generation == _refreshGeneration
                && _activeCodexWindow != 0
                && !_activeCodexWindowIsMinimized)
            {
                _lastRefresh = DateTimeOffset.UtcNow;
                Render(IndicatorState.Unavailable, null);
            }
        }
    }

    private async Task RevalidateOnceAsync(CancellationToken cancellationToken, TaskCompletionSource<bool> completion)
    {
        try
        {
            var snapshot = await _usageProvider.ReadAsync(cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                completion.TrySetResult(false);
                return;
            }

            if (_settings.Enabled && _activeCodexWindow != 0 && !_activeCodexWindowIsMinimized)
            {
                ApplySnapshot(snapshot);
            }

            completion.TrySetResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetResult(false);
        }
        catch (Exception)
        {
            if (!_disposed && _settings.Enabled && _activeCodexWindow != 0 && !_activeCodexWindowIsMinimized)
            {
                Render(IndicatorState.Unavailable, null);
            }

            completion.TrySetResult(false);
        }
    }

    private void ApplySnapshot(UsageSnapshot snapshot)
    {
        if (_snapshot is not null && !string.Equals(_snapshot.AccountFingerprint, snapshot.AccountFingerprint, StringComparison.Ordinal))
        {
            InvalidateUsage();
            Render(IndicatorState.Loading, null);
        }

        _snapshot = snapshot;
        _lastRefresh = DateTimeOffset.UtcNow;
        Render(IndicatorState.Available, snapshot);
    }

    private void InvalidateUsage()
    {
        _refreshGeneration++;
        _snapshot = null;
        _lastRefresh = DateTimeOffset.MinValue;
    }

    private void Render(IndicatorState state, UsageSnapshot? snapshot)
    {
        if (_activeCodexWindow == 0
            || _activeCodexWindowIsMinimized
            || !_tracker.TryGetWindowRect(_activeCodexWindow, out var rect))
        {
            _overlay.Hide();
            return;
        }

        var layout = _overlay.Render(state, snapshot, IndicatorPresentation.GetAvailableOverlayWidth(rect.Width));
        if (layout == OverlayLayout.Hidden)
        {
            _overlay.Hide();
            return;
        }

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
        _tracker.WindowChanged -= TrackerOnWindowChanged;
        _refreshRunner.Cancel();
        _tracker.Dispose();
        _overlay.Close();
    }
}
