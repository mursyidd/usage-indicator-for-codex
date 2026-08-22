using System.Diagnostics;
using System.IO;
using System.Windows;
using UsageIndicatorForCodex.Services;
using UsageIndicatorForCodex.Update;
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
        if (TryHandleBeforeInitialization(options))
        {
            return;
        }

        base.OnStartup(eventArgs);
        if (TryHandleAfterInitialization(options))
        {
            return;
        }

        StartIndicator(options);
    }

    // Commands answered without initializing WPF. base.OnStartup has not run
    // yet, so these must not touch framework state.
    private bool TryHandleBeforeInitialization(CommandLineOptions options)
    {
        switch (options.Action)
        {
            case CommandLineAction.Help:
            case CommandLineAction.Invalid:
                CommandLineOutput.Show(options.Message, options.Action == CommandLineAction.Invalid);
                Shutdown(options.ExitCode);
                return true;
            case CommandLineAction.Version:
                CommandLineOutput.Show($"usage-indicator {ProductInfo.Version}", isError: false);
                Shutdown(0);
                return true;
            default:
                return false;
        }
    }

    // Commands that run after base.OnStartup because they depend on the
    // dispatcher, but that shut down instead of showing the overlay.
    private bool TryHandleAfterInitialization(CommandLineOptions options)
    {
        switch (options.Action)
        {
            case CommandLineAction.EnableCreditExpiry:
            case CommandLineAction.DisableCreditExpiry:
                _ = SetCreditExpiryAndShutdownAsync(options.Action == CommandLineAction.EnableCreditExpiry);
                return true;
            case CommandLineAction.EnableStartup:
                RunStartupCommand(
                    StartupTaskManager.Install,
                    "Startup enabled.",
                    "Startup could not be enabled.");
                return true;
            case CommandLineAction.DisableStartup:
                RunStartupCommand(
                    StartupTaskManager.Uninstall,
                    "Startup disabled.",
                    "Startup could not be disabled.");
                return true;
            case CommandLineAction.Status:
                ShowStatus();
                return true;
            case CommandLineAction.Stop:
                _ = StopAndShutdownAsync();
                return true;
            case CommandLineAction.CheckUpdate:
            case CommandLineAction.Update:
                CommandLineOutput.Show(
                    "Update commands must be invoked through usage-indicator.exe.",
                    isError: true);
                Shutdown(1);
                return true;
            default:
                return false;
        }
    }

    private void ShowStatus()
    {
        try
        {
            using var statusInstance = SingleInstanceService.CreateForCurrentUser();
            var settings = new UserSettingsStore().Inspect();
            var snapshot = new ApplicationStatusSnapshot(
                !statusInstance.IsPrimary,
                settings.Enabled,
                settings.CreditExpiryEnabled,
                StartupTaskManager.Inspect(GetExecutablePath()));
            CommandLineOutput.Show(snapshot.Format(), isError: false);
            Shutdown(snapshot.ExitCode);
        }
        catch (Exception exception)
        {
            CommandLineOutput.Show(
                $"Status inspection failed. {exception.Message}",
                isError: true);
            Shutdown(1);
        }
    }

    private void StartIndicator(CommandLineOptions options)
    {
        if (options.Action == CommandLineAction.Run)
        {
            _ = TryMigrateLegacyStartup(GetExecutablePath, StartupTaskManager.TryMigrateLegacyTask);
        }

        var settingsStore = new UserSettingsStore();
        var singleInstance = SingleInstanceService.CreateForCurrentUser();
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
        var operation = Dispatcher.InvokeAsync(() => ApplyInstanceCommand(_coordinator, command));
        return await operation.Task;
    }

    internal static bool ApplyInstanceCommand(IndicatorCoordinator? coordinator, InstanceCommand command)
    {
        try
        {
            switch (command)
            {
                case InstanceCommand.Exit:
                    return true;
                case InstanceCommand.EnableCreditExpiry when coordinator is not null:
                    coordinator.SetCreditExpiryEnabled(true);
                    return true;
                case InstanceCommand.DisableCreditExpiry when coordinator is not null:
                    coordinator.SetCreditExpiryEnabled(false);
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
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

    private async Task StopAndShutdownAsync()
    {
        try
        {
            if (!await StopRunningInstanceAsync())
            {
                throw new InvalidOperationException("The running instance did not stop.");
            }

            CommandLineOutput.Show("stopped", isError: false);
            Shutdown(0);
        }
        catch (Exception exception)
        {
            CommandLineOutput.Show($"Stop failed. {exception.Message}", isError: true);
            Shutdown(1);
        }
    }

    private async Task SetCreditExpiryAndShutdownAsync(bool enabled)
    {
        try
        {
            using var instance = SingleInstanceService.CreateForCurrentUser();
            var succeeded = await ApplyCreditExpirySettingAsync(
                enabled,
                new UserSettingsStore(),
                instance);
            if (!succeeded)
            {
                throw new InvalidOperationException("The running instance did not apply the setting.");
            }

            CommandLineOutput.Show(GetCreditExpirySuccessMessage(enabled), isError: false);
            Shutdown(0);
        }
        catch (Exception exception)
        {
            CommandLineOutput.Show(
                $"Credit expiry could not be {(enabled ? "enabled" : "disabled")}. {exception.Message}",
                isError: true);
            Shutdown(1);
        }
    }

    internal static async Task<bool> ApplyCreditExpirySettingAsync(
        bool enabled,
        UserSettingsStore settingsStore,
        SingleInstanceService instance)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(instance);
        if (instance.IsPrimary)
        {
            var settings = settingsStore.Load();
            settingsStore.Save(settings with { CreditExpiryEnabled = enabled });
            return true;
        }

        var command = enabled
            ? InstanceCommand.EnableCreditExpiry
            : InstanceCommand.DisableCreditExpiry;
        return await SingleInstanceService.TrySendAsync(instance.PipeNames, command) == true;
    }

    internal static string GetCreditExpirySuccessMessage(bool enabled) =>
        enabled ? "Credit expiry enabled." : "Credit expiry disabled.";

    private static async Task<bool> StopRunningInstanceAsync()
    {
        var instance = SingleInstanceService.CreateForCurrentUser();
        if (instance.IsPrimary)
        {
            instance.Dispose();
            return true;
        }

        var pipeNames = instance.GetPipeNamesForExit();
        instance.Dispose();
        if (await SingleInstanceService.TrySendAsync(pipeNames, InstanceCommand.Exit) != true)
        {
            return false;
        }

        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            using var probe = SingleInstanceService.CreateForCurrentUser();
            if (probe.IsPrimary)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    private void RunStartupCommand(
        Action<string> operation,
        string successMessage,
        string failureMessage)
    {
        try
        {
            operation(GetExecutablePath());
            CommandLineOutput.Show(successMessage, isError: false);
            Shutdown(0);
        }
        catch (StartupTaskOwnershipException exception)
        {
            CommandLineOutput.Show(exception.Message, isError: true);
            Shutdown(exception.ExitCode);
        }
        catch (Exception exception)
        {
            CommandLineOutput.Show(
                $"{failureMessage} {exception.Message}",
                isError: true);
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
