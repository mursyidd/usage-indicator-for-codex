using System.Diagnostics;
using System.Windows;
using UsageIndicatorForCodex.Services;
using UsageIndicatorForCodex.Views;

namespace UsageIndicatorForCodex;

public partial class App : System.Windows.Application
{
    private IndicatorCoordinator? _coordinator;
    private HotkeyService? _hotkey;
    private SingleInstanceService? _singleInstance;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var options = CommandLineOptions.Parse(eventArgs.Args);
        if (options.Action is CommandLineAction.Help or CommandLineAction.Invalid)
        {
            CommandLineOutput.Show(options.Message, options.Action == CommandLineAction.Invalid);
            Shutdown(options.ExitCode);
            return;
        }

        base.OnStartup(eventArgs);
        if (options.Action == CommandLineAction.Install)
        {
            try
            {
                StartupTaskManager.Install(GetExecutablePath());
                Shutdown(0);
            }
            catch (Exception exception)
            {
                MessageBox.Show($"Automatic startup could not be installed. {exception.Message}", "Usage Indicator for Codex", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }

        if (options.Action == CommandLineAction.Uninstall)
        {
            try
            {
                StartupTaskManager.Uninstall();
                Shutdown(0);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"Automatic startup could not be removed. {exception.Message}",
                    "Usage Indicator for Codex",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }

        if (options.Action == CommandLineAction.Run)
        {
            _ = TryMigrateLegacyStartup(GetExecutablePath, StartupTaskManager.TryMigrateLegacyTask);
        }

        var settingsStore = new UserSettingsStore();
        var command = options.Action switch
        {
            CommandLineAction.Toggle => InstanceCommand.Toggle,
            CommandLineAction.RevalidateCli => InstanceCommand.RevalidateCli,
            CommandLineAction.Exit => InstanceCommand.Exit,
            _ => (InstanceCommand?)null
        };
        var singleInstance = SingleInstanceService.CreateForCurrentUser();
        if (command is not null)
        {
            if (!singleInstance.IsPrimary)
            {
                var pipeNames = singleInstance.GetPipeNamesForCommand(command.Value);
                singleInstance.Dispose();
                _ = SendCommandAndShutdownAsync(pipeNames, command.Value);
                return;
            }

            singleInstance.Dispose();
            if (command == InstanceCommand.Toggle)
            {
                var settings = settingsStore.Load();
                settingsStore.Save(settings with { Enabled = !settings.Enabled });
                Shutdown(0);
            }
            else if (command == InstanceCommand.RevalidateCli)
            {
                _ = RevalidateCliAndShutdownAsync();
            }
            else
            {
                Shutdown(0);
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
        _singleInstance.Start(HandleInstanceCommandAsync, HandleInstanceResponseSent);
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
            if (command == InstanceCommand.Exit)
            {
                return true;
            }

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

    private void HandleInstanceResponseSent(InstanceCommand command)
    {
        if (command == InstanceCommand.Exit)
        {
            _ = Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(() => Shutdown(0)));
        }
    }

    private bool ToggleCoordinator()
    {
        _coordinator?.ToggleEnabled();
        return _coordinator is not null;
    }

    private async Task SendCommandAndShutdownAsync(IReadOnlyList<string> pipeNames, InstanceCommand command)
    {
        var succeeded = await SingleInstanceService.TrySendAsync(pipeNames, command);
        if (command == InstanceCommand.RevalidateCli)
        {
            MessageBox.Show(
                succeeded == true
                    ? "The configured Codex CLI returned a verified usage response. This does not compare it with Codex Desktop and does not enable live usage."
                    : "The configured Codex CLI could not be revalidated. Usage remains unavailable.",
                "Usage Indicator for Codex",
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

            MessageBox.Show("The configured Codex CLI returned a verified usage response. This does not compare it with Codex Desktop and does not enable live usage.", "Usage Indicator for Codex", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
        }
        catch
        {
            MessageBox.Show("The configured Codex CLI could not be revalidated. Usage remains unavailable.", "Usage Indicator for Codex", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    private static string GetExecutablePath() =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("The companion executable path is unavailable.");

    internal static bool TryMigrateLegacyStartup(
        Func<string> executablePathResolver,
        Func<string, bool> migration)
    {
        ArgumentNullException.ThrowIfNull(executablePathResolver);
        ArgumentNullException.ThrowIfNull(migration);
        try
        {
            return migration(executablePathResolver());
        }
        catch
        {
            return false;
        }
    }
}
