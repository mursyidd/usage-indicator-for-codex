using System.Diagnostics;
using System.IO;
using System.Text.Json;
using UsageIndicatorForCodex.Core;

namespace UsageIndicatorForCodex.Services;

public interface IUsageProvider
{
    Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken);
}

public sealed class CodexAppServerUsageProvider : IUsageProvider
{
    public static bool IsLiveUsageEnabled => true;

    public Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken) =>
        new CodexCliAppServerReader().ReadAsync(cancellationToken);
}

public sealed class CodexCliAppServerReader
{
    internal const string CliPathEnvironmentVariable = "CODEX_CLI_PATH";
    private readonly IReadOnlyList<string> _cliPaths;
    private readonly bool _isConfiguredPath;
    private readonly string? _codexHome;
    private readonly TimeSpan _timeout;

    public CodexCliAppServerReader(string? codexHome = null)
    {
        var configuredPath = Environment.GetEnvironmentVariable(CliPathEnvironmentVariable);
        _cliPaths = ResolveCliPaths(
            configuredPath,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        _isConfiguredPath = !string.IsNullOrWhiteSpace(configuredPath);
        _codexHome = codexHome;
        _timeout = TimeSpan.FromSeconds(20);
    }

    internal static string ResolveCliPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(CliPathEnvironmentVariable);
        var cliPaths = ResolveCliPaths(
            configuredPath,
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        return cliPaths.FirstOrDefault() ?? throw new InvalidOperationException("No Codex CLI launcher could be found.");
    }

    internal static IReadOnlyList<string> ResolveCliPaths(string? configuredPath, string? path, string appDataPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return new[] { ValidateCliPath(configuredPath) };
        }

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddPathCandidates(paths, seen, path, "codex.exe");
        AddCandidate(paths, seen, Path.Combine(appDataPath, "npm", "codex.cmd"));
        AddPathCandidates(paths, seen, path, "codex.cmd");
        return paths;
    }

    private static string ValidateCliPath(string cliPath)
    {
        var extension = Path.GetExtension(cliPath);
        if (!Path.IsPathFullyQualified(cliPath) ||
            (!string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"The {CliPathEnvironmentVariable} value must be a fully qualified .exe or .cmd path.");
        }

        return Path.GetFullPath(cliPath);
    }

    internal CodexCliAppServerReader(string cliPath, string? codexHome, TimeSpan timeout)
        : this(new[] { ValidateCliPath(cliPath) }, true, codexHome, timeout)
    {
    }

    internal CodexCliAppServerReader(IReadOnlyList<string> cliPaths, bool isConfiguredPath, string? codexHome, TimeSpan timeout)
    {
        _cliPaths = cliPaths;
        _isConfiguredPath = isConfiguredPath;
        _codexHome = codexHome;
        _timeout = timeout;
    }

    public async Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        using var process = StartAppServer();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await using var writer = process.StandardInput;
            using var reader = process.StandardOutput;

            await SendAsync(writer, 1, "initialize", new
            {
                clientInfo = new { name = "usage-indicator-for-codex", version = ProductInfo.Version },
                capabilities = new { experimentalApi = false }
            }, timeout.Token);
            _ = await ReadResponseAsync(reader, 1, timeout.Token);

            await SendAsync(writer, 2, "account/read", new { refreshToken = false }, timeout.Token);
            using var accountResponse = await ReadResponseAsync(reader, 2, timeout.Token);
            var fingerprint = AppServerResponses.CreateAccountFingerprint(accountResponse.RootElement.GetProperty("result"));

            await SendAsync(writer, 3, "account/rateLimits/read", null, timeout.Token);
            using var usageResponse = await ReadResponseAsync(reader, 3, timeout.Token);
            var windows = AppServerResponses.ExtractRateLimitWindows(usageResponse.RootElement.GetProperty("result"));

            return IndicatorPresentation.SelectMostRestrictive(fingerprint, windows);
        }
        finally
        {
            await StopProcessTreeAsync(process);
            _ = await stderrTask;
        }
    }

    private Process StartAppServer()
    {
        Exception? finalLaunchException = null;
        foreach (var cliPath in _cliPaths)
        {
            try
            {
                return StartAppServer(cliPath);
            }
            catch (Exception exception) when (IsLaunchFailure(exception))
            {
                finalLaunchException = exception;
                if (_isConfiguredPath)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException(
            "Codex app-server could not be started.",
            finalLaunchException ?? new FileNotFoundException("No Codex CLI launcher could be found."));
    }

    private Process StartAppServer(string cliPath)
    {
        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException("The configured Codex CLI is absent.", cliPath);
        }

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (string.Equals(Path.GetExtension(cliPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = cliPath;
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");
        }
        else
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
            startInfo.Arguments = CreateAppServerArguments(cliPath);
        }
        if (!string.IsNullOrWhiteSpace(_codexHome))
        {
            startInfo.Environment["CODEX_HOME"] = _codexHome;
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Codex app-server could not be started.");
        return process;
    }

    internal static string CreateAppServerCommand(string cliPath) =>
        $"\"{cliPath}\" app-server --stdio";

    internal static string CreateAppServerArguments(string cliPath) =>
        $"/d /s /c \"{CreateAppServerCommand(cliPath)}\"";

    private static void AddPathCandidates(ICollection<string> paths, ISet<string> seen, string? path, string fileName)
    {
        foreach (var directory in (path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                AddCandidate(paths, seen, Path.Combine(directory.Trim().Trim('"'), fileName));
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries and continue checking the remaining entries.
            }
        }
    }

    private static void AddCandidate(ICollection<string> paths, ISet<string> seen, string cliPath)
    {
        var fullPath = Path.GetFullPath(cliPath);
        if (File.Exists(fullPath) && seen.Add(fullPath))
        {
            paths.Add(fullPath);
        }
    }

    private static bool IsLaunchFailure(Exception exception) =>
        exception is FileNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception;

    internal static async Task StopProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and termination request.
        }
    }

    private static async Task SendAsync(StreamWriter writer, int id, string method, object? parameters, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });
        await writer.WriteLineAsync(request.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static async Task<JsonDocument> ReadResponseAsync(StreamReader reader, int expectedId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new InvalidOperationException("Codex app-server closed before returning usage data.");
            }

            var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number || idElement.GetInt32() != expectedId)
            {
                document.Dispose();
                continue;
            }

            if (root.TryGetProperty("error", out _))
            {
                document.Dispose();
                throw new InvalidOperationException("Codex app-server rejected the usage request.");
            }

            if (!root.TryGetProperty("result", out _))
            {
                document.Dispose();
                throw new InvalidOperationException("Codex app-server returned no usage result.");
            }

            return document;
        }
    }

}
