using System.IO.Pipes;
using System.IO;
using System.Security.Principal;
using System.Text;
using UsageIndicatorForCodex.Update;

namespace UsageIndicatorForCodex.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private readonly IReadOnlyList<InstanceIdentity> _identities;
    private readonly List<Mutex> _mutexes = [];
    private readonly List<Mutex> _ownedMutexes = [];
    private CancellationTokenSource? _serverCancellation;
    private Task? _serverTask;
    private bool _disposed;

    internal SingleInstanceService(string userIdentity)
        : this(CreateIdentities(userIdentity))
    {
    }

    internal SingleInstanceService(IReadOnlyList<InstanceIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        if (identities.Count == 0)
        {
            throw new ArgumentException("At least one instance identity is required.", nameof(identities));
        }

        if (identities.Select(identity => identity.MutexName).Distinct(StringComparer.Ordinal).Count() != identities.Count
            || identities.Select(identity => identity.PipeName).Distinct(StringComparer.Ordinal).Count() != identities.Count)
        {
            throw new ArgumentException("Instance mutex and pipe names must be unique.", nameof(identities));
        }

        _identities = identities.ToArray();
        foreach (var identity in _identities)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identity.MutexName);
            ArgumentException.ThrowIfNullOrWhiteSpace(identity.PipeName);
            var mutex = new Mutex(false, identity.MutexName);
            _mutexes.Add(mutex);
            if (!TryAcquire(mutex))
            {
                ReleaseOwnedMutexes();
                return;
            }

            _ownedMutexes.Add(mutex);
        }

        IsPrimary = true;
    }

    internal bool IsPrimary { get; }
    internal string MutexName => _identities[0].MutexName;
    internal string PipeName => _identities[0].PipeName;
    internal string LegacyMutexName => _identities[^1].MutexName;
    internal string LegacyPipeName => _identities[^1].PipeName;
    internal IReadOnlyList<string> PipeNames => _identities.Select(identity => identity.PipeName).ToArray();

    internal static SingleInstanceService CreateForCurrentUser()
    {
        var identity = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new InvalidOperationException("The current Windows user identity is unavailable.");
        }

        return new SingleInstanceService(identity);
    }

    internal void Start(
        Func<InstanceCommand, CancellationToken, Task<bool>> commandHandler,
        Action<InstanceCommand>? responseSent = null)
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
        _serverTask = Task.WhenAll(_identities.Select(identity =>
            Task.Run(() => RunServerAsync(identity.PipeName, commandHandler, responseSent, _serverCancellation.Token))));
    }

    internal static Task<bool?> TrySendAsync(string pipeName, InstanceCommand command) =>
        InstanceProtocol.TrySendAsync(pipeName, command);

    internal static async Task<bool?> TrySendAsync(IEnumerable<string> pipeNames, InstanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(pipeNames);
        foreach (var pipeName in pipeNames)
        {
            var result = await TrySendAsync(pipeName, command);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    internal IReadOnlyList<string> GetPipeNamesForExit() => [PipeName];

    private static IReadOnlyList<InstanceIdentity> CreateIdentities(string userIdentity) =>
        InstanceProtocol.CreateIdentities(userIdentity);

    private static bool TryAcquire(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private async Task RunServerAsync(
        string pipeName,
        Func<InstanceCommand, CancellationToken, Task<bool>> commandHandler,
        Action<InstanceCommand>? responseSent,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                await HandleConnectionAsync(server, commandHandler, responseSent, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task HandleConnectionAsync(
        NamedPipeServerStream server,
        Func<InstanceCommand, CancellationToken, Task<bool>> commandHandler,
        Action<InstanceCommand>? responseSent,
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
            responseSent?.Invoke(command);
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
        _serverTask?.GetAwaiter().GetResult();
        _serverCancellation?.Dispose();
        if (IsPrimary)
        {
            ReleaseOwnedMutexes();
        }

        foreach (var mutex in _mutexes)
        {
            mutex.Dispose();
        }
    }

    private void ReleaseOwnedMutexes()
    {
        for (var index = _ownedMutexes.Count - 1; index >= 0; index--)
        {
            _ownedMutexes[index].ReleaseMutex();
        }

        _ownedMutexes.Clear();
    }
}
