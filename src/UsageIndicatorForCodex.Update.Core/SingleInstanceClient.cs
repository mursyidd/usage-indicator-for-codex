using System.Diagnostics;
using System.Security.Principal;

namespace UsageIndicatorForCodex.Update;

internal interface IIndicatorController
{
    bool IsRunning();
    Task StopAsync(CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
}

internal sealed class SingleInstanceClient : IIndicatorController
{
    private static readonly TimeSpan StateChangeTimeout = TimeSpan.FromSeconds(5);
    private readonly IReadOnlyList<InstanceIdentity> _identities;
    private readonly string _launcherPath;

    internal SingleInstanceClient(string userIdentity, string launcherPath)
    {
        _identities = InstanceProtocol.CreateIdentities(userIdentity);
        _launcherPath = Path.GetFullPath(launcherPath);
    }

    internal static SingleInstanceClient CreateForCurrentUser(string installRoot)
    {
        var identity = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new InvalidOperationException("The current Windows user identity is unavailable.");
        }

        return new SingleInstanceClient(
            identity,
            Path.Combine(installRoot, ProductConstants.LauncherRelativePath));
    }

    public bool IsRunning()
    {
        foreach (var identity in _identities)
        {
            using var mutex = new Mutex(false, identity.MutexName);
            var acquired = false;
            try
            {
                try
                {
                    acquired = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                if (!acquired)
                {
                    return true;
                }
            }
            finally
            {
                if (acquired)
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        return false;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!IsRunning())
        {
            return;
        }

        if (await InstanceProtocol.TrySendAsync(_identities[0].PipeName, InstanceCommand.Exit) != true)
        {
            throw new InvalidOperationException(
                "The running application did not accept the graceful stop request.");
        }

        await WaitForStateAsync(expectedRunning: false, cancellationToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _launcherPath,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("start");
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The stable launcher could not restart the application.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The stable launcher could not restart the application (exit code {process.ExitCode}).");
        }

        await WaitForStateAsync(expectedRunning: true, cancellationToken);
    }

    private async Task WaitForStateAsync(bool expectedRunning, CancellationToken cancellationToken)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < StateChangeTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsRunning() == expectedRunning)
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new InvalidOperationException(
            expectedRunning
                ? "The application did not start within the allowed time."
                : "The running application did not stop within the allowed time.");
    }
}
