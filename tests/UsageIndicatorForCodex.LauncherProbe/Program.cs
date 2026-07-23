using System.Diagnostics;
using System.Text.Json;

if (args is ["--verify-launcher", var launcherPath])
{
    try
    {
        VerifyLauncher(Path.GetFullPath(launcherPath), LauncherLayout.Portable);
        Console.WriteLine("PASS native launcher argument, exit-code, and asynchronous process contract");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL native launcher contract: {exception.Message}");
        return 1;
    }
}

if (args is ["--verify-launcher", var layoutName, var installedLauncherPath]
    && Enum.TryParse<LauncherLayout>(layoutName, ignoreCase: true, out var layout))
{
    try
    {
        VerifyLauncher(Path.GetFullPath(installedLauncherPath), layout);
        Console.WriteLine(
            $"PASS {layout} native launcher layout, argument, exit-code, and asynchronous process contract");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL {layout} native launcher contract: {exception.Message}");
        return 1;
    }
}

var outputPath = Environment.GetEnvironmentVariable(ProbeEnvironment.Output)
    ?? throw new InvalidOperationException($"{ProbeEnvironment.Output} is required in probe mode.");
File.WriteAllText(outputPath, JsonSerializer.Serialize(args));

var readyPath = Environment.GetEnvironmentVariable(ProbeEnvironment.Ready);
if (!string.IsNullOrWhiteSpace(readyPath))
{
    var temporaryReadyPath = $"{readyPath}.{Guid.NewGuid():N}.tmp";
    File.WriteAllText(temporaryReadyPath, Environment.ProcessId.ToString());
    File.Move(temporaryReadyPath, readyPath);
}

if (int.TryParse(Environment.GetEnvironmentVariable(ProbeEnvironment.HoldMilliseconds), out var holdMilliseconds)
    && holdMilliseconds > 0)
{
    Thread.Sleep(holdMilliseconds);
}

return int.TryParse(Environment.GetEnvironmentVariable(ProbeEnvironment.ExitCode), out var exitCode)
    ? exitCode
    : 0;

static void VerifyLauncher(string launcherPath, LauncherLayout layout)
{
    if (!File.Exists(launcherPath))
    {
        throw new FileNotFoundException("The launcher does not exist.", launcherPath);
    }

    var probePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("The launcher probe executable path is unavailable.");
    var probeDirectory = Path.GetDirectoryName(probePath)
        ?? throw new InvalidOperationException("The launcher probe directory is unavailable.");
    var testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"Usage Indicator Launcher Contract {Guid.NewGuid():N}");
    Directory.CreateDirectory(testDirectory);

    try
    {
        var launcherDirectory = layout == LauncherLayout.Installed
            ? Path.Combine(testDirectory, "bin")
            : testDirectory;
        var guiDirectory = layout == LauncherLayout.Installed
            ? Path.Combine(testDirectory, "app")
            : testDirectory;
        Directory.CreateDirectory(launcherDirectory);
        Directory.CreateDirectory(guiDirectory);
        var disposableLauncher = Path.Combine(
            launcherDirectory,
            layout == LauncherLayout.Installed
                ? "usage-indicator.exe"
                : "UsageIndicatorForCodex.exe");
        File.Copy(launcherPath, disposableLauncher);

        foreach (var sourcePath in Directory.EnumerateFiles(probeDirectory))
        {
            var fileName = Path.GetFileName(sourcePath);
            var destinationName = string.Equals(sourcePath, probePath, StringComparison.OrdinalIgnoreCase)
                ? "UsageIndicatorForCodex.Gui.exe"
                : fileName;
            File.Copy(sourcePath, Path.Combine(guiDirectory, destinationName));
        }

        var portableCases = new[]
        {
            new[] { "--help" },
            new[] { "--install" },
            new[] { "--uninstall" },
            new[] { "--toggle" },
            new[] { "--revalidate-cli" },
            new[] { "--exit" },
            new[] { @"C:\path with spaces\item.txt" },
            new[] { "literal\"quote" },
            new[] { string.Empty },
            new[] { @"trailing-backslash\" },
            new[] { "--help", string.Empty },
            new[] { "--toggle", "--exit" },
            new[] { "\"malformed" }
        };
        var installedCases = new[]
        {
            new[] { "stop" },
            new[] { "status" },
            new[] { "version" },
            new[] { "check-update" },
            new[] { "update" },
            new[] { "enable-startup" },
            new[] { "disable-startup" },
            new[] { "help" },
            new[] { "help", "status" },
            new[] { "--unknown" }
        };

        foreach (var expectedArguments in layout == LauncherLayout.Installed
            ? installedCases
            : portableCases)
        {
            VerifySynchronousCase(disposableLauncher, testDirectory, expectedArguments);
        }

        if (layout == LauncherLayout.Installed)
        {
            VerifySynchronousCase(disposableLauncher, testDirectory, ["help"], []);
            VerifyAsynchronousCase(disposableLauncher, testDirectory, ["start"]);
        }
        else
        {
            VerifyAsynchronousCase(disposableLauncher, testDirectory, []);
            VerifyAsynchronousCase(disposableLauncher, testDirectory, ["--background"]);
        }
    }
    finally
    {
        Directory.Delete(testDirectory, recursive: true);
    }
}

static void VerifySynchronousCase(
    string launcherPath,
    string testDirectory,
    IReadOnlyList<string> expectedArguments,
    IReadOnlyList<string>? launcherArguments = null)
{
    var outputPath = Path.Combine(testDirectory, $"arguments-{Guid.NewGuid():N}.json");
    using var process = StartLauncher(
        launcherPath,
        launcherArguments ?? expectedArguments,
        outputPath,
        exitCode: 37,
        readyPath: null,
        holdMilliseconds: null);

    if (!process.WaitForExit(10_000))
    {
        TryTerminate(process);
        throw new TimeoutException($"Launcher did not exit for arguments {Format(expectedArguments)}.");
    }

    if (process.ExitCode != 37)
    {
        throw new InvalidOperationException(
            $"Launcher returned {process.ExitCode}, not child exit code 37, for {Format(expectedArguments)}.");
    }

    var actualArguments = JsonSerializer.Deserialize<string[]>(File.ReadAllText(outputPath))
        ?? throw new InvalidOperationException("Probe argument JSON was empty.");
    if (!expectedArguments.SequenceEqual(actualArguments, StringComparer.Ordinal))
    {
        throw new InvalidOperationException(
            $"Argument mismatch. Expected {Format(expectedArguments)}; received {Format(actualArguments)}.");
    }
}

static void VerifyAsynchronousCase(
    string launcherPath,
    string testDirectory,
    IReadOnlyList<string> expectedArguments)
{
    var outputPath = Path.Combine(testDirectory, $"arguments-{Guid.NewGuid():N}.json");
    var readyPath = Path.Combine(testDirectory, $"ready-{Guid.NewGuid():N}.txt");
    using var launcher = StartLauncher(
        launcherPath,
        expectedArguments,
        outputPath,
        exitCode: 0,
        readyPath,
        holdMilliseconds: 10_000);

    Process? child = null;
    try
    {
        if (!launcher.WaitForExit(2_000))
        {
            TryTerminate(launcher);
            throw new TimeoutException(
                $"Asynchronous launcher remained alive for {Format(expectedArguments)}.");
        }

        if (launcher.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Asynchronous launcher returned {launcher.ExitCode} for {Format(expectedArguments)}.");
        }

        var deadline = Stopwatch.StartNew();
        while (child is null && deadline.Elapsed < TimeSpan.FromSeconds(2))
        {
            child = TryOpenSignaledProcess(readyPath);
            Thread.Sleep(10);
        }

        if (child is null)
        {
            throw new InvalidOperationException(
                $"The asynchronous child did not signal readiness for {Format(expectedArguments)}.");
        }

        if (child.HasExited)
        {
            throw new InvalidOperationException(
                $"The asynchronous child exited before verification for {Format(expectedArguments)}.");
        }

        var actualArguments = JsonSerializer.Deserialize<string[]>(File.ReadAllText(outputPath))
            ?? throw new InvalidOperationException("Probe argument JSON was empty.");
        if (!expectedArguments.SequenceEqual(actualArguments, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Argument mismatch. Expected {Format(expectedArguments)}; received {Format(actualArguments)}.");
        }
    }
    finally
    {
        if (child is null)
        {
            var cleanupDeadline = Stopwatch.StartNew();
            while (child is null && cleanupDeadline.Elapsed < TimeSpan.FromSeconds(2))
            {
                child = TryOpenSignaledProcess(readyPath);
                Thread.Sleep(10);
            }
        }

        if (child is not null)
        {
            TryTerminate(child);
            child.Dispose();
        }
    }
}

static Process? TryOpenSignaledProcess(string readyPath)
{
    try
    {
        if (!File.Exists(readyPath)
            || !int.TryParse(File.ReadAllText(readyPath), out var processId))
        {
            return null;
        }

        return Process.GetProcessById(processId);
    }
    catch (Exception exception) when (
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException)
    {
        return null;
    }
}

static Process StartLauncher(
    string launcherPath,
    IReadOnlyList<string> arguments,
    string outputPath,
    int exitCode,
    string? readyPath,
    int? holdMilliseconds)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = launcherPath,
        UseShellExecute = false
    };
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    startInfo.Environment[ProbeEnvironment.Output] = outputPath;
    startInfo.Environment[ProbeEnvironment.ExitCode] = exitCode.ToString();
    if (readyPath is not null)
    {
        startInfo.Environment[ProbeEnvironment.Ready] = readyPath;
    }

    if (holdMilliseconds is not null)
    {
        startInfo.Environment[ProbeEnvironment.HoldMilliseconds] = holdMilliseconds.Value.ToString();
    }

    return Process.Start(startInfo)
        ?? throw new InvalidOperationException("The launcher process could not be started.");
}

static void TryTerminate(Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(2_000);
        }
    }
    catch (InvalidOperationException)
    {
        // The process exited between inspection and termination.
    }
}

static string Format(IEnumerable<string> arguments) =>
    JsonSerializer.Serialize(arguments);

internal static class ProbeEnvironment
{
    internal const string Output = "USAGE_INDICATOR_LAUNCHER_PROBE_OUTPUT";
    internal const string ExitCode = "USAGE_INDICATOR_LAUNCHER_PROBE_EXIT_CODE";
    internal const string Ready = "USAGE_INDICATOR_LAUNCHER_PROBE_READY";
    internal const string HoldMilliseconds = "USAGE_INDICATOR_LAUNCHER_PROBE_HOLD_MS";
}

internal enum LauncherLayout
{
    Portable,
    Installed
}
