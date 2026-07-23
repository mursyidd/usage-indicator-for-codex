using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CodexUsageIndicator.Core;

namespace CodexUsageIndicator.Services;

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
    private readonly string _cliPath;
    private readonly string? _codexHome;
    private readonly TimeSpan _timeout;

    public CodexCliAppServerReader(string? codexHome = null)
    {
        _cliPath = ResolveCliPath();
        _codexHome = codexHome;
        _timeout = TimeSpan.FromSeconds(20);
    }

    internal static string ResolveCliPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(CliPathEnvironmentVariable);
        var cliPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd")
            : configuredPath;

        if (!Path.IsPathFullyQualified(cliPath) || !string.Equals(Path.GetExtension(cliPath), ".cmd", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The {CliPathEnvironmentVariable} value must be a fully qualified .cmd path.");
        }

        return Path.GetFullPath(cliPath);
    }

    internal CodexCliAppServerReader(string cliPath, string? codexHome, TimeSpan timeout)
    {
        _cliPath = cliPath;
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
                clientInfo = new { name = "codex-usage-indicator", version = "1.0.0" },
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
        if (!File.Exists(_cliPath))
        {
            throw new InvalidOperationException("The configured Codex CLI is absent.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(CreateAppServerCommand(_cliPath));
        if (!string.IsNullOrWhiteSpace(_codexHome))
        {
            startInfo.Environment["CODEX_HOME"] = _codexHome;
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Codex app-server could not be started.");
        return process;
    }

    internal static string CreateAppServerCommand(string cliPath) =>
        $"\"{cliPath}\" app-server --stdio";

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
