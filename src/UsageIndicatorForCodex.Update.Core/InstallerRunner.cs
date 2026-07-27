using System.Diagnostics;

namespace UsageIndicatorForCodex.Update;

internal interface IInstallerRunner
{
    Task<int> RunAsync(
        string installerPath,
        string logPath,
        int bootstrapVersion,
        CancellationToken cancellationToken);
}

internal sealed class InstallerRunner : IInstallerRunner
{
    internal static IReadOnlyList<string> CreateArguments(
        string logPath,
        int bootstrapVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        if (bootstrapVersion != ProductConstants.BootstrapProtocolVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bootstrapVersion),
                bootstrapVersion,
                "The bootstrap protocol version is unsupported.");
        }

        return
        [
            "/VERYSILENT",
            "/SUPPRESSMSGBOXES",
            "/SP-",
            "/NORESTART",
            "/RESTARTEXITCODE=3010",
            "/CLOSEAPPLICATIONS",
            "/NOFORCECLOSEAPPLICATIONS",
            "/NORESTARTAPPLICATIONS",
            $"/LOG={Path.GetFullPath(logPath)}",
            "/CLIUPDATE",
            $"/BOOTSTRAPVERSION={bootstrapVersion}"
        ];
    }

    public async Task<int> RunAsync(
        string installerPath,
        string logPath,
        int bootstrapVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        var fullLogPath = Path.GetFullPath(logPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullLogPath)
                ?? throw new InvalidOperationException("The installer log directory is unavailable."));

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(installerPath),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in CreateArguments(fullLogPath, bootstrapVersion))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var installer = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The verified installer process could not be started.");
        await installer.WaitForExitAsync(cancellationToken);
        return installer.ExitCode;
    }
}
