namespace UsageIndicatorForCodex.Update;

internal interface IUpdateOutput
{
    void WriteLine(string message);
    void WriteError(string message);
}

internal sealed record UpdatePaths(
    string InstallRoot,
    string WorkingRoot,
    string LogRoot);

internal sealed record UpdateOutcome(int ExitCode);

internal interface IUpdateWorkingDirectoryCleaner
{
    void Delete(string workingDirectory);
}

internal sealed class UpdateWorkingDirectoryCleaner : IUpdateWorkingDirectoryCleaner
{
    public void Delete(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        var fullPath = Path.GetFullPath(workingDirectory);
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}

internal sealed class UpdateFailureException : Exception
{
    internal UpdateFailureException(
        string message,
        string? installerLogPath = null,
        Exception? innerException = null)
        : base(
            installerLogPath is null
                ? message
                : $"{message} Installer log: {installerLogPath}",
            innerException)
    {
        InstallerLogPath = installerLogPath;
    }

    internal string? InstallerLogPath { get; }
}

internal sealed class UpdateOrchestrator
{
    private readonly IReleaseUpdateClient _releaseClient;
    private readonly Func<IUpdateMutexLease> _mutexFactory;
    private readonly IIndicatorController _indicator;
    private readonly IInstallerRunner _installer;
    private readonly IInstalledVersionValidator _versionValidator;
    private readonly IUpdateWorkingDirectoryCleaner _workingDirectoryCleaner;
    private readonly IUpdateOutput _output;
    private readonly UpdatePaths _paths;

    internal UpdateOrchestrator(
        IReleaseUpdateClient releaseClient,
        Func<IUpdateMutexLease> mutexFactory,
        IIndicatorController indicator,
        IInstallerRunner installer,
        IInstalledVersionValidator versionValidator,
        IUpdateWorkingDirectoryCleaner workingDirectoryCleaner,
        IUpdateOutput output,
        UpdatePaths paths)
    {
        _releaseClient = releaseClient ?? throw new ArgumentNullException(nameof(releaseClient));
        _mutexFactory = mutexFactory ?? throw new ArgumentNullException(nameof(mutexFactory));
        _indicator = indicator ?? throw new ArgumentNullException(nameof(indicator));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _versionValidator = versionValidator ?? throw new ArgumentNullException(nameof(versionValidator));
        _workingDirectoryCleaner = workingDirectoryCleaner
            ?? throw new ArgumentNullException(nameof(workingDirectoryCleaner));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    internal async Task<UpdateOutcome> CheckAsync(CancellationToken cancellationToken)
    {
        _output.WriteLine("Checking for updates...");
        var check = await _releaseClient.CheckAsync(cancellationToken);
        _output.WriteLine(check.Message);
        return new UpdateOutcome(0);
    }

    internal async Task<UpdateOutcome> UpdateAsync(CancellationToken cancellationToken)
    {
        using var updateMutex = _mutexFactory();
        if (!updateMutex.IsAcquired)
        {
            _output.WriteError("An update is already in progress.");
            return new UpdateOutcome(1);
        }

        _output.WriteLine("Checking for updates...");
        var check = await _releaseClient.CheckAsync(cancellationToken);
        _output.WriteLine(check.Message);
        if (!check.IsAvailable)
        {
            return new UpdateOutcome(0);
        }

        _output.WriteLine("Downloading installer...");
        var prepared = await _releaseClient.PrepareAsync(
            check.LatestRelease,
            _paths.WorkingRoot,
            () => _output.WriteLine("Verifying SHA-256..."),
            cancellationToken);

        try
        {
            var wasRunning = _indicator.IsRunning();
            var indicatorStopped = false;
            if (wasRunning)
            {
                _output.WriteLine("Stopping Usage Indicator for Codex...");
                await _indicator.StopAsync(cancellationToken);
                indicatorStopped = true;
            }

            var target = prepared.TargetVersion.ToString(3);
            var logPath = CreateInstallerLogPath(_paths.LogRoot, prepared.TargetVersion);
            int installerExitCode;
            try
            {
                _output.WriteLine($"Installing {target}...");
                try
                {
                    installerExitCode = await _installer.RunAsync(
                        prepared.InstallerPath,
                        logPath,
                        ProductConstants.BootstrapProtocolVersion,
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    throw new UpdateFailureException(
                        $"The verified installer could not complete. {exception.Message}",
                        logPath,
                        exception);
                }

                if (installerExitCode is not 0 and not 3010)
                {
                    throw new UpdateFailureException(
                        $"The installer exited with code {installerExitCode}.",
                        logPath);
                }

                _output.WriteLine("Validating installed version...");
                try
                {
                    _versionValidator.Validate(_paths.InstallRoot, prepared.TargetVersion);
                }
                catch (Exception exception)
                {
                    throw new UpdateFailureException(
                        $"Installed version validation failed. {exception.Message}",
                        logPath,
                        exception);
                }
            }
            catch (UpdateFailureException)
            {
                if (indicatorStopped)
                {
                    await TryRestoreAfterFailureAsync();
                }

                throw;
            }

            if (installerExitCode == 3010)
            {
                _output.WriteLine(
                    $"Update installed and validated: {check.CurrentVersion.ToString(3)} -> {target}. "
                    + $"Windows must be restarted before Usage Indicator for Codex can be restarted. "
                    + $"Installer log: {logPath}");
                return new UpdateOutcome(3010);
            }

            if (wasRunning)
            {
                _output.WriteLine("Restarting Usage Indicator for Codex...");
                try
                {
                    await _indicator.StartAsync(cancellationToken);
                }
                catch (Exception exception)
                {
                    throw new UpdateFailureException(
                        $"The update installed, but the application could not be restarted. {exception.Message}",
                        logPath,
                        exception);
                }
            }

            _output.WriteLine(
                $"Updated successfully: {check.CurrentVersion.ToString(3)} -> {target}.");
            return new UpdateOutcome(0);
        }
        finally
        {
            TryDeleteWorkingDirectory(prepared.WorkingDirectory);
        }
    }

    private async Task TryRestoreAfterFailureAsync()
    {
        WriteLineWithoutThrowing(
            "Restoring Usage Indicator for Codex after update failure...");
        try
        {
            await _indicator.StartAsync(CancellationToken.None);
            WriteLineWithoutThrowing(
                "Restoration succeeded after update failure.");
        }
        catch (Exception exception)
        {
            WriteErrorWithoutThrowing(
                $"Restoration also failed: {exception.Message}");
        }
    }

    private void TryDeleteWorkingDirectory(string workingDirectory)
    {
        try
        {
            _workingDirectoryCleaner.Delete(workingDirectory);
        }
        catch (Exception exception)
        {
            WriteErrorWithoutThrowing(
                $"Downloaded installer working directory could not be deleted: "
                + $"{workingDirectory}. {exception.Message}");
        }
    }

    private void WriteLineWithoutThrowing(string message)
    {
        try
        {
            _output.WriteLine(message);
        }
        catch
        {
        }
    }

    private void WriteErrorWithoutThrowing(string message)
    {
        try
        {
            _output.WriteError(message);
        }
        catch
        {
        }
    }

    internal static string CreateInstallerLogPath(string logRoot, Version targetVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logRoot);
        ArgumentNullException.ThrowIfNull(targetVersion);
        return Path.Combine(
            Path.GetFullPath(logRoot),
            $"UsageIndicatorForCodex-update-v{targetVersion.ToString(3)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
    }
}
