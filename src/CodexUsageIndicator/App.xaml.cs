using System.Diagnostics;
using System.Windows;
using CodexUsageIndicator.Services;
using CodexUsageIndicator.Views;

namespace CodexUsageIndicator;

public partial class App : System.Windows.Application
{
    private IndicatorCoordinator? _coordinator;
    private HotkeyService? _hotkey;
    private SingleInstanceService? _singleInstance;

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

        var settingsStore = new UserSettingsStore();
        var command = arguments.Contains("--toggle")
            ? InstanceCommand.Toggle
            : arguments.Contains("--revalidate-cli")
                ? InstanceCommand.RevalidateCli
                : (InstanceCommand?)null;
        var singleInstance = SingleInstanceService.CreateForCurrentUser();
        if (command is not null)
        {
            if (!singleInstance.IsPrimary)
            {
                _ = SendCommandAndShutdownAsync(singleInstance.PipeName, command.Value);
                singleInstance.Dispose();
                return;
            }

            singleInstance.Dispose();
            if (command == InstanceCommand.Toggle)
            {
                var settings = settingsStore.Load();
                settingsStore.Save(settings with { Enabled = !settings.Enabled });
                Shutdown();
            }
            else
            {
                _ = RevalidateCliAndShutdownAsync();
            }

            return;
        }

        _singleInstance = singleInstance;
        if (!_singleInstance.IsPrimary)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _coordinator = new IndicatorCoordinator(
            new CodexWindowTracker(),
            new CodexAppServerUsageProvider(),
            new UsageOverlayWindow(),
            settingsStore);
        _coordinator.Start();
        _singleInstance.Start(HandleInstanceCommandAsync);
        _hotkey = new HotkeyService();
        _hotkey.ToggleRequested += (_, _) => _coordinator?.ToggleEnabled();
        _hotkey.Start();
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _singleInstance?.Dispose();
        _hotkey?.Dispose();
        _coordinator?.Dispose();
        base.OnExit(eventArgs);
    }

    private async Task<bool> HandleInstanceCommandAsync(InstanceCommand command, CancellationToken cancellationToken)
    {
        var operation = Dispatcher.InvokeAsync(async () =>
        {
            if (_coordinator is null)
            {
                return false;
            }

            return command switch
            {
                InstanceCommand.Toggle => ToggleCoordinator(),
                InstanceCommand.RevalidateCli => await _coordinator.RevalidateAsync(),
                _ => false
            };
        });
        return await operation.Task.Unwrap();
    }

    private bool ToggleCoordinator()
    {
        _coordinator?.ToggleEnabled();
        return _coordinator is not null;
    }

    private async Task SendCommandAndShutdownAsync(string pipeName, InstanceCommand command)
    {
        var succeeded = await SingleInstanceService.TrySendAsync(pipeName, command);
        if (command == InstanceCommand.RevalidateCli)
        {
            MessageBox.Show(
                succeeded == true
                    ? "The configured Codex CLI returned a verified usage response. This does not compare it with Codex Desktop and does not enable live usage."
                    : "The configured Codex CLI could not be revalidated. Usage remains unavailable.",
                "Codex Usage Indicator",
                MessageBoxButton.OK,
                succeeded == true ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        Shutdown(succeeded == true ? 0 : 1);
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
