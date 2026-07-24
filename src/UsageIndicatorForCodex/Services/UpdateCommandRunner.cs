using System.IO;

namespace UsageIndicatorForCodex.Services;

internal sealed record UpdateCommandResult(int ExitCode, string Message, bool IsError);

internal static class UpdateCommandRunner
{
    internal static async Task<UpdateCommandResult> ExecuteAsync(
        Func<IUpdateMutexLease> mutexFactory,
        Func<CancellationToken, Task<string?>> prepareUpdate,
        Func<Task<bool>> stopRunningInstance,
        Action<string> launchInstaller,
        string currentVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutexFactory);
        ArgumentNullException.ThrowIfNull(prepareUpdate);
        ArgumentNullException.ThrowIfNull(stopRunningInstance);
        ArgumentNullException.ThrowIfNull(launchInstaller);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);

        using var updateMutex = mutexFactory();
        if (!updateMutex.IsAcquired)
        {
            return new UpdateCommandResult(
                1,
                "An update is already in progress.",
                true);
        }

        var installerPath = await prepareUpdate(cancellationToken);
        if (installerPath is null)
        {
            return new UpdateCommandResult(
                0,
                $"Up to date: {currentVersion}.",
                false);
        }

        if (!await stopRunningInstance())
        {
            throw new InvalidOperationException(
                "The running application could not be stopped before launching the installer.");
        }

        launchInstaller(installerPath);
        return new UpdateCommandResult(
            0,
            $"Launching verified installer {Path.GetFileName(installerPath)}.",
            false);
    }
}
