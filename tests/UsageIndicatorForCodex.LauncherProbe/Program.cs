using System.Diagnostics;
using System.Text.Json;

if (args is ["--verify-launcher", var launcherPath])
{
    try
    {
        VerifyLauncher(Path.GetFullPath(launcherPath));
        Console.WriteLine("PASS installed native launcher argument, exit-code, and asynchronous process contract");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL native launcher contract: {exception.Message}");
        return 1;
    }
}

var outputPath = Environment.GetEnvironmentVariable(ProbeEnvironment.Output);
if (string.IsNullOrWhiteSpace(outputPath))
{
    return WriteManagedConsoleResult(args);
}

File.WriteAllText(
    outputPath,
    JsonSerializer.Serialize(new ProbeObservation(
        args,
        Environment.ProcessPath
            ?? throw new InvalidOperationException("The probe process path is unavailable."))));

var standardOutput = Environment.GetEnvironmentVariable(ProbeEnvironment.StandardOutput);
if (!string.IsNullOrEmpty(standardOutput))
{
    Console.Out.WriteLine(standardOutput);
}

var standardError = Environment.GetEnvironmentVariable(ProbeEnvironment.StandardError);
if (!string.IsNullOrEmpty(standardError))
{
    Console.Error.WriteLine(standardError);
}

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

static void VerifyLauncher(string launcherPath)
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
        var launcherDirectory = Path.Combine(testDirectory, "bin");
        var guiDirectory = Path.Combine(testDirectory, "app");
        var updaterDirectory = Path.Combine(testDirectory, "updater");
        Directory.CreateDirectory(launcherDirectory);
        Directory.CreateDirectory(guiDirectory);
        Directory.CreateDirectory(updaterDirectory);
        var disposableLauncher = Path.Combine(launcherDirectory, "usage-indicator.exe");
        File.Copy(launcherPath, disposableLauncher);

        foreach (var sourcePath in Directory.EnumerateFiles(probeDirectory))
        {
            var fileName = Path.GetFileName(sourcePath);
            File.Copy(sourcePath, Path.Combine(guiDirectory, fileName));
        }
        File.Copy(probePath, Path.Combine(guiDirectory, "UsageIndicatorForCodex.Gui.exe"), overwrite: true);
        File.Copy(
            probePath,
            Path.Combine(updaterDirectory, "UsageIndicatorForCodex.UpdateHost.exe"),
            overwrite: true);

        var installedCases = new[]
        {
            new[] { "stop" },
            new[] { "status" },
            new[] { "version" },
            new[] { "enable-startup" },
            new[] { "disable-startup" },
            new[] { "enable-credit-expiry" },
            new[] { "disable-credit-expiry" },
            new[] { "help" },
            new[] { "help", "status" },
            new[] { "check-update", "extra" },
            new[] { "update", "extra" },
            new[] { "--unknown" },
            new[] { @"C:\path with spaces\item.txt" },
            new[] { "literal\"quote" },
            new[] { string.Empty },
            new[] { @"trailing-backslash\" },
            new[] { "\"malformed" }
        };

        foreach (var expectedArguments in installedCases)
        {
            VerifySynchronousCase(disposableLauncher, testDirectory, expectedArguments);
        }

        VerifyUpdateHostCase(disposableLauncher, testDirectory, "check-update");
        VerifyUpdateHostCase(disposableLauncher, testDirectory, "update");
        VerifyUpdateHostOutputCase(disposableLauncher, testDirectory);
        VerifyConcurrentUpdateHostCases(disposableLauncher, testDirectory);
        VerifyUpdateHostFailureCases(disposableLauncher, testDirectory);
        VerifySynchronousCase(disposableLauncher, testDirectory, ["help"], []);
        VerifyAsynchronousCase(disposableLauncher, testDirectory, ["start"]);
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

    var observation = ReadObservation(outputPath);
    if (!expectedArguments.SequenceEqual(observation.Arguments, StringComparer.Ordinal))
    {
        throw new InvalidOperationException(
            $"Argument mismatch. Expected {Format(expectedArguments)}; received {Format(observation.Arguments)}.");
    }

    var expectedGuiPath = Path.GetFullPath(
        Path.Combine(testDirectory, "app", "UsageIndicatorForCodex.Gui.exe"));
    if (!string.Equals(
        Path.GetFullPath(observation.ProcessPath),
        expectedGuiPath,
        StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"GUI command ran from {observation.ProcessPath}, not {expectedGuiPath}.");
    }
}

static void VerifyUpdateHostCase(
    string launcherPath,
    string testDirectory,
    string command)
{
    var outputPath = Path.Combine(testDirectory, $"update-arguments-{Guid.NewGuid():N}.json");
    using var process = StartLauncher(
        launcherPath,
        [command],
        outputPath,
        exitCode: 37,
        readyPath: null,
        holdMilliseconds: null);

    if (!process.WaitForExit(10_000))
    {
        TryTerminate(process);
        throw new TimeoutException($"Launcher did not exit for update command {command}.");
    }

    if (process.ExitCode != 37)
    {
        throw new InvalidOperationException(
            $"Launcher returned {process.ExitCode}, not cached-host exit code 37, for {command}.");
    }

    var observation = ReadObservation(outputPath);
    var expectedArguments = new[]
    {
        "--command",
        command,
        "--install-root",
        Path.GetFullPath(testDirectory),
        "--bootstrap-version",
        "1"
    };
    if (!expectedArguments.SequenceEqual(observation.Arguments, StringComparer.Ordinal))
    {
        throw new InvalidOperationException(
            $"Cached-host argument mismatch. Expected {Format(expectedArguments)}; "
            + $"received {Format(observation.Arguments)}.");
    }

    var installedHost = Path.GetFullPath(
        Path.Combine(testDirectory, "updater", "UsageIndicatorForCodex.UpdateHost.exe"));
    var versionInfo = FileVersionInfo.GetVersionInfo(installedHost);
    if (versionInfo.ProductPrivatePart != 0)
    {
        throw new InvalidOperationException(
            $"Probe product version must have a zero revision: {versionInfo.ProductVersion}");
    }

    var expectedCacheRoot = Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UsageIndicatorForCodex",
        "update-host",
        $"v{versionInfo.ProductMajorPart}.{versionInfo.ProductMinorPart}.{versionInfo.ProductBuildPart}"));
    if (string.Equals(observation.ProcessPath, installedHost, StringComparison.OrdinalIgnoreCase)
        || !Path.GetFullPath(observation.ProcessPath).StartsWith(
            $"{expectedCacheRoot}{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
        || !Path.GetFileName(observation.ProcessPath).StartsWith(
            "UsageIndicatorForCodex.UpdateHost.",
            StringComparison.Ordinal)
        || !Path.GetFileName(observation.ProcessPath).EndsWith(".exe", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Update host did not run from the process-unique versioned cache: {observation.ProcessPath}");
    }

    TryDeleteCachedProbe(observation.ProcessPath, expectedCacheRoot);
}

static void VerifyUpdateHostOutputCase(string launcherPath, string testDirectory)
{
    const string stdoutMarker = "CACHED_HOST_STDOUT_MARKER";
    const string stderrMarker = "CACHED_HOST_STDERR_MARKER";
    var outputPath = Path.Combine(testDirectory, $"update-output-{Guid.NewGuid():N}.json");
    using var process = StartLauncher(
        launcherPath,
        ["check-update"],
        outputPath,
        exitCode: 23,
        readyPath: null,
        holdMilliseconds: null,
        standardOutput: stdoutMarker,
        standardError: stderrMarker,
        redirectOutput: true);
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();

    if (!process.WaitForExit(10_000)
        || !Task.WaitAll([stdout, stderr], 2_000))
    {
        TryTerminate(process);
        throw new TimeoutException("Launcher did not synchronously forward cached-host output.");
    }

    if (process.ExitCode != 23
        || !stdout.Result.Contains(stdoutMarker, StringComparison.Ordinal)
        || !stderr.Result.Contains(stderrMarker, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Cached-host output/exit forwarding failed: exit={process.ExitCode}, "
            + $"stdout={stdout.Result}, stderr={stderr.Result}");
    }

    var observation = ReadObservation(outputPath);
    TryDeleteCachedProbe(
        observation.ProcessPath,
        Path.GetDirectoryName(observation.ProcessPath)
            ?? throw new InvalidOperationException("Cached host has no parent directory."));
}

static void VerifyConcurrentUpdateHostCases(string launcherPath, string testDirectory)
{
    var outputPaths = new[]
    {
        Path.Combine(testDirectory, $"update-concurrent-{Guid.NewGuid():N}.json"),
        Path.Combine(testDirectory, $"update-concurrent-{Guid.NewGuid():N}.json")
    };
    using var first = StartLauncher(
        launcherPath,
        ["check-update"],
        outputPaths[0],
        exitCode: 29,
        readyPath: null,
        holdMilliseconds: 250);
    using var second = StartLauncher(
        launcherPath,
        ["update"],
        outputPaths[1],
        exitCode: 31,
        readyPath: null,
        holdMilliseconds: 250);

    if (!first.WaitForExit(10_000) || !second.WaitForExit(10_000))
    {
        TryTerminate(first);
        TryTerminate(second);
        throw new TimeoutException("Concurrent update-host launcher invocations did not complete.");
    }

    if (first.ExitCode != 29 || second.ExitCode != 31)
    {
        throw new InvalidOperationException(
            $"Concurrent updater exit codes were {first.ExitCode} and {second.ExitCode}.");
    }

    var observations = outputPaths.Select(ReadObservation).ToArray();
    if (string.Equals(
        observations[0].ProcessPath,
        observations[1].ProcessPath,
        StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Concurrent updater invocations reused one cache file.");
    }

    foreach (var observation in observations)
    {
        TryDeleteCachedProbe(
            observation.ProcessPath,
            Path.GetDirectoryName(observation.ProcessPath)
                ?? throw new InvalidOperationException("Cached host has no parent directory."));
    }
}

static void VerifyUpdateHostFailureCases(string launcherPath, string testDirectory)
{
    var installedHost = Path.Combine(
        testDirectory,
        "updater",
        "UsageIndicatorForCodex.UpdateHost.exe");
    var backupHost = $"{installedHost}.valid";
    File.Move(installedHost, backupHost);
    try
    {
        VerifyUpdateHostFailure(launcherPath, testDirectory, "missing");
        File.WriteAllText(installedHost, "not a versioned executable");
        VerifyUpdateHostFailure(launcherPath, testDirectory, "invalid");
    }
    finally
    {
        File.Delete(installedHost);
        File.Move(backupHost, installedHost);
    }
}

static void VerifyUpdateHostFailure(
    string launcherPath,
    string testDirectory,
    string caseName)
{
    var outputPath = Path.Combine(testDirectory, $"update-failure-{caseName}-{Guid.NewGuid():N}.json");
    using var process = StartLauncher(
        launcherPath,
        ["update"],
        outputPath,
        exitCode: 0,
        readyPath: null,
        holdMilliseconds: null,
        standardOutput: null,
        standardError: null,
        redirectOutput: true);
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();

    if (!process.WaitForExit(10_000)
        || !Task.WaitAll([stdout, stderr], 2_000))
    {
        TryTerminate(process);
        throw new TimeoutException($"Launcher did not fail promptly for {caseName} update host.");
    }

    if (process.ExitCode == 0
        || File.Exists(outputPath)
        || !string.IsNullOrEmpty(stdout.Result)
        || !stderr.Result.Contains("cached update host", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Launcher did not fail without GUI fallback for {caseName} update host.");
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

        var observation = ReadObservation(outputPath);
        if (!expectedArguments.SequenceEqual(observation.Arguments, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Argument mismatch. Expected {Format(expectedArguments)}; received {Format(observation.Arguments)}.");
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
    int? holdMilliseconds,
    string? standardOutput = null,
    string? standardError = null,
    bool redirectOutput = false)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = launcherPath,
        UseShellExecute = false,
        RedirectStandardOutput = redirectOutput,
        RedirectStandardError = redirectOutput
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
    if (standardOutput is not null)
    {
        startInfo.Environment[ProbeEnvironment.StandardOutput] = standardOutput;
    }
    if (standardError is not null)
    {
        startInfo.Environment[ProbeEnvironment.StandardError] = standardError;
    }

    return Process.Start(startInfo)
        ?? throw new InvalidOperationException("The launcher process could not be started.");
}

static ProbeObservation ReadObservation(string outputPath) =>
    JsonSerializer.Deserialize<ProbeObservation>(File.ReadAllText(outputPath))
        ?? throw new InvalidOperationException("Probe observation JSON was empty.");

static void TryDeleteCachedProbe(string processPath, string versionDirectory)
{
    try
    {
        File.Delete(processPath);
        if (!Directory.EnumerateFileSystemEntries(versionDirectory).Any())
        {
            Directory.Delete(versionDirectory);
        }
    }
    catch (Exception exception) when (
        exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException)
    {
    }
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

static int WriteManagedConsoleResult(IReadOnlyList<string> arguments)
{
    const string usage = """
        Usage Indicator for Codex

        Commands:
          usage-indicator start
          usage-indicator stop
          usage-indicator status
          usage-indicator version
          usage-indicator check-update
          usage-indicator update
          usage-indicator enable-startup
          usage-indicator disable-startup
          usage-indicator enable-credit-expiry
          usage-indicator disable-credit-expiry
          usage-indicator help

        Keyboard shortcut:
          Ctrl+Alt+U    Turn the indicator display on or off while running

        Running usage-indicator without arguments shows this help.
        """;

    if (arguments.Count == 1 && arguments[0] == "help")
    {
        Console.Out.WriteLine(usage);
        return 0;
    }

    var error = arguments.Count == 1
        ? $"Unknown argument: {arguments[0]}"
        : "Exactly one command may be specified.";
    Console.Error.WriteLine($"{error}{Environment.NewLine}{Environment.NewLine}{usage}");
    return 2;
}

internal static class ProbeEnvironment
{
    internal const string Output = "USAGE_INDICATOR_LAUNCHER_PROBE_OUTPUT";
    internal const string ExitCode = "USAGE_INDICATOR_LAUNCHER_PROBE_EXIT_CODE";
    internal const string Ready = "USAGE_INDICATOR_LAUNCHER_PROBE_READY";
    internal const string HoldMilliseconds = "USAGE_INDICATOR_LAUNCHER_PROBE_HOLD_MS";
    internal const string StandardOutput = "USAGE_INDICATOR_LAUNCHER_PROBE_STDOUT";
    internal const string StandardError = "USAGE_INDICATOR_LAUNCHER_PROBE_STDERR";
}

internal sealed record ProbeObservation(
    IReadOnlyList<string> Arguments,
    string ProcessPath);
