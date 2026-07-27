using System.IO.Pipes;
using System.Text;

namespace UsageIndicatorForCodex.Update;

internal enum InstanceCommand
{
    Exit
}

internal sealed record InstanceIdentity(string MutexName, string PipeName);

internal static class InstanceProtocol
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(2);

    internal static IReadOnlyList<InstanceIdentity> CreateIdentities(string userIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentity);
        return
        [
            new InstanceIdentity(
                $"Local\\UsageIndicatorForCodex-{userIdentity}",
                $"UsageIndicatorForCodex-{userIdentity}"),
            new InstanceIdentity(
                $"Local\\CodexUsageIndicator-{userIdentity}",
                $"CodexUsageIndicator-{userIdentity}")
        ];
    }

    internal static async Task<bool?> TrySendAsync(string pipeName, InstanceCommand command)
    {
        using var connectionTimeout = new CancellationTokenSource(ConnectionTimeout);
        NamedPipeClientStream? client = null;
        while (!connectionTimeout.IsCancellationRequested)
        {
            try
            {
                client = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
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
        using (var responseTimeout = new CancellationTokenSource(ConnectionTimeout))
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
}
