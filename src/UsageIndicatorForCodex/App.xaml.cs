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
            catch (StartupTaskOwnershipException exception)
            {
                CommandLineOutput.Show(exception.Message, isError: true);
                Shutdown(exception.ExitCode);
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
                StartupTaskManager.Uninstall(GetExecutablePath());
                CommandLineOutput.Show("Startup disabled.", isError: false);
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
                    $"Startup could not be disabled. {exception.Message}",
                    isError: true);
                Shutdown(1);
            }
            return;
        }

        if (options.Action == CommandLineAction.Status)
        {
            try
            {
                using var statusInstance = SingleInstanceService.CreateForCurrentUser();
                var snapshot = new ApplicationStatusSnapshot(
                    !statusInstance.IsPrimary,
                    new UserSettingsStore().InspectEnabled(),
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

            return command == InstanceCommand.Exit;
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
            var result = await UpdateCommandRunner.ExecuteAsync(
                UpdateMutexService.CreateForCurrentUser,
                cancellationToken => updateService.PrepareUpdateAsync(
                    updateRoot,
                    cancellationToken),
                StopRunningInstanceAsync,
                installerPath =>
                {
                    _ = Process.Start(new ProcessStartInfo
                    {
                        FileName = installerPath,
                        UseShellExecute = true
                    }) ?? throw new InvalidOperationException(
                        "The installer process could not be started.");
                },
                ProductInfo.Version,
                CancellationToken.None);
            CommandLineOutput.Show(result.Message, result.IsError);
            Shutdown(result.ExitCode);
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
