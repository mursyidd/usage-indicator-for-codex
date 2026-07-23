using System.Diagnostics;
using System.IO;
using System.Net.Http;
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

        if (options.Action == CommandLineAction.Version)
        {
            CommandLineOutput.Show($"usage-indicator {ProductInfo.Version}", isError: false);
            Shutdown(0);
            return;
        }

        base.OnStartup(eventArgs);
        if (options.Action == CommandLineAction.EnableStartup)
        {
            try
            {
                StartupTaskManager.Install(GetExecutablePath());
                CommandLineOutput.Show("Startup enabled.", isError: false);
                Shutdown(0);
            }
            catch (Exception exception)
            {
                CommandLineOutput.Show(
                    $"Startup could not be enabled. {exception.Message}",
                    isError: true);
                Shutdown(1);
            }
            return;
        }

        if (options.Action == CommandLineAction.DisableStartup)
        {
            try
            {
                StartupTaskManager.Uninstall();
                CommandLineOutput.Show("Startup disabled.", isError: false);
                Shutdown(0);
            }
            catch (Exception exception)
            {
                CommandLineOutput.Show(
                    $"Startup could not be disabled. {exception.Message}",
                    isError: true);
                Shutdown(1);
            }
            return;
        }

        if (options.Action == CommandLineAction.Status)
        {
            using var statusInstance = SingleInstanceService.CreateForCurrentUser();
            var isRunning = !statusInstance.IsPrimary;
            CommandLineOutput.Show(isRunning ? "running" : "stopped", isError: false);
            Shutdown(isRunning ? 0 : 1);
            return;
        }

        if (options.Action == CommandLineAction.Stop)
        {
            _ = StopAndShutdownAsync();
            return;
        }

        if (options.Action is CommandLineAction.CheckUpdate or CommandLineAction.Update)
        {
            _ = CheckForUpdateAndShutdownAsync(options.Action == CommandLineAction.Update);
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

    private async Task CheckForUpdateAndShutdownAsync(bool prepareUpdate)
    {
        try
        {
            var repositoryUrl = ProductInfo.RepositoryUrl;
            if (string.IsNullOrWhiteSpace(repositoryUrl))
            {
                throw new InvalidOperationException(
                    "This build does not contain an explicitly configured GitHub repository URL.");
            }

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            var updateService = new ReleaseUpdateService(
                httpClient,
                repositoryUrl,
                ProductInfo.Version);
            if (!prepareUpdate)
            {
                var check = await updateService.CheckAsync(CancellationToken.None);
                CommandLineOutput.Show(check.Message, isError: false);
                Shutdown(0);
                return;
            }

            var updateRoot = Path.Combine(
                Path.GetTempPath(),
                "UsageIndicatorForCodex",
                "updates");
            var installerPath = await updateService.PrepareUpdateAsync(
                updateRoot,
                CancellationToken.None);
            if (installerPath is null)
            {
                CommandLineOutput.Show($"Up to date: {ProductInfo.Version}.", isError: false);
                Shutdown(0);
                return;
            }

            if (!await StopRunningInstanceAsync())
            {
                throw new InvalidOperationException(
                    "The running application could not be stopped before launching the installer.");
            }

            _ = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("The installer process could not be started.");
            CommandLineOutput.Show(
                $"Launching verified installer {Path.GetFileName(installerPath)}.",
                isError: false);
            Shutdown(0);
        }
        catch (Exception exception)
        {
            CommandLineOutput.Show($"Update failed. {exception.Message}", isError: true);
            Shutdown(1);
        }
    }

    private static async Task<bool> StopRunningInstanceAsync()
    {
        var instance = SingleInstanceService.CreateForCurrentUser();
        if (instance.IsPrimary)
        {
            instance.Dispose();
            return true;
        }

        var pipeNames = instance.GetPipeNamesForCommand(InstanceCommand.Exit);
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
