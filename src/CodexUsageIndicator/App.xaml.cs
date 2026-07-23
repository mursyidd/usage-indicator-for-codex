using System.Diagnostics;
using System.Windows;
using CodexUsageIndicator.Services;
using CodexUsageIndicator.Views;

namespace CodexUsageIndicator;

public partial class App : System.Windows.Application
{
    private IndicatorCoordinator? _coordinator;
    private HotkeyService? _hotkey;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var arguments = eventArgs.Args.Select(argument => argument.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        if (arguments.Contains("--install"))
        {
            try
            {
                StartupTaskManager.Install(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
                    ?? throw new InvalidOperationException("The companion executable path is unavailable."));
                Shutdown(0);
            }
            catch (Exception exception)
            {
                MessageBox.Show($"Automatic startup could not be installed. {exception.Message}", "Codex Usage Indicator", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }

        if (arguments.Contains("--uninstall"))
        {
            StartupTaskManager.Uninstall();
            Shutdown();
            return;
        }

        if (arguments.Contains("--revalidate-cli"))
        {
            _ = RevalidateCliAndShutdownAsync();
            return;
        }

        var settingsStore = new UserSettingsStore();
        if (arguments.Contains("--toggle"))
        {
            var settings = settingsStore.Load();
            settingsStore.Save(settings with { Enabled = !settings.Enabled });
            Shutdown();
            return;
        }

        _coordinator = new IndicatorCoordinator(
            new CodexWindowTracker(),
            new CodexAppServerUsageProvider(),
            new UsageOverlayWindow(),
            settingsStore);
        _coordinator.Start();
        _hotkey = new HotkeyService();
        _hotkey.ToggleRequested += (_, _) => _coordinator?.ToggleEnabled();
        _hotkey.Start();
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _hotkey?.Dispose();
        _coordinator?.Dispose();
        base.OnExit(eventArgs);
    }

    private async Task RevalidateCliAndShutdownAsync()
    {
        try
        {
            var snapshot = await new CodexCliAppServerReader().ReadAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(snapshot.AccountFingerprint) || snapshot.ResetsAt <= DateTimeOffset.UtcNow || snapshot.RemainingPercent is < 0 or > 100)
            {
                throw new InvalidOperationException("The configured Codex CLI did not return a verifiable usage response.");
            }

            MessageBox.Show("The configured Codex CLI returned a verified usage response. This does not compare it with Codex Desktop and does not enable live usage.", "Codex Usage Indicator", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
        }
        catch
        {
            MessageBox.Show("The configured Codex CLI could not be revalidated. Usage remains unavailable.", "Codex Usage Indicator", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }
}
