using System.IO.Pipes;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace CodexUsageIndicator.Services;

internal enum InstanceCommand
{
    Toggle,
    RevalidateCli
}

internal sealed class SingleInstanceService : IDisposable
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RevalidationResponseTimeout = TimeSpan.FromSeconds(25);
    private readonly Mutex _mutex;
    private CancellationTokenSource? _serverCancellation;
    private Task? _serverTask;
    private bool _disposed;

    internal SingleInstanceService(string userIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentity);
        MutexName = $"Local\\CodexUsageIndicator-{userIdentity}";
        PipeName = $"CodexUsageIndicator-{userIdentity}";
        _mutex = new Mutex(false, MutexName);
        try
        {
            IsPrimary = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            IsPrimary = true;
        }
    }

    internal bool IsPrimary { get; }
    internal string MutexName { get; }
    internal string PipeName { get; }

    internal static SingleInstanceService CreateForCurrentUser()
    {
        var identity = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new InvalidOperationException("The current Windows user identity is unavailable.");
        }

        return new SingleInstanceService(identity);
    }

    internal void Start(Func<InstanceCommand, CancellationToken, Task<bool>> commandHandler)
    {
        ArgumentNullException.ThrowIfNull(commandHandler);
        if (!IsPrimary)
        {
            throw new InvalidOperationException("Only the primary instance can host commands.");
        }

        if (_serverTask is not null)
        {
            return;
        }

        _serverCancellation = new CancellationTokenSource();
        _serverTask = Task.Run(() => RunServerAsync(commandHandler, _serverCancellation.Token));
    }

    internal static async Task<bool?> TrySendAsync(string pipeName, InstanceCommand command)
    {
        using var connectionTimeout = new CancellationTokenSource(ConnectionTimeout);
        NamedPipeClientStream? client = null;
        while (!connectionTimeout.IsCancellationRequested)
        {
            try
            {
                client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(connectionTimeout.Token);
                break;
            }
            catch (IOException) when (!connectionTimeout.IsCancellationRequested)
            {
                client?.Dispose();
                client = null;
                try
                {
                    await Task.Delay(50, connectionTimeout.Token);
                }
                catch (OperationCanceledException) when (connectionTimeout.IsCancellationRequested)
                {
                    return null;
                }
            }
            catch (OperationCanceledException) when (connectionTimeout.IsCancellationRequested)
            {
                return null;
            }
        }

        if (client is null)
        {
            return null;
        }

        using (client)
        using (var responseTimeout = new CancellationTokenSource(command == InstanceCommand.RevalidateCli ? RevalidationResponseTimeout : ConnectionTimeout))
        {
            try
            {
                using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true);
                await writer.WriteLineAsync(command.ToString());
                await writer.FlushAsync(responseTimeout.Token);
                var response = await reader.ReadLineAsync(responseTimeout.Token);
                return response == "ok";
            }
            catch (IOException)
            {
                return false;
            }
            catch (OperationCanceledException) when (responseTimeout.IsCancellationRequested)
            {
                return false;
            }
        }
    }

    private async Task RunServerAsync(Func<InstanceCommand, CancellationToken, Task<bool>> commandHandler, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                await HandleConnectionAsync(server, commandHandler, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task HandleConnectionAsync(
        NamedPipeServerStream server,
        Func<InstanceCommand, CancellationToken, Task<bool>> commandHandler,
        CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true);
            var request = await reader.ReadLineAsync(cancellationToken);
            if (!Enum.TryParse<InstanceCommand>(request, ignoreCase: false, out var command))
            {
                await writer.WriteLineAsync("unknown");
                await writer.FlushAsync(cancellationToken);
                return;
            }

            var succeeded = await commandHandler(command, cancellationToken);
            await writer.WriteLineAsync(succeeded ? "ok" : "failed");
            await writer.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _serverCancellation?.Cancel();
        _serverCancellation?.Dispose();
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
