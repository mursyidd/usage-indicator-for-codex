namespace CodexUsageIndicator.Services;

internal sealed class CoalescingRefreshRunner
{
    private readonly object _sync = new();
    private Task _activeRun = Task.CompletedTask;
    private Func<CancellationToken, Task>? _pendingOperation;
    private CancellationTokenSource? _activeCancellation;

    public Task RunAsync(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_sync)
        {
            if (!_activeRun.IsCompleted)
            {
                _pendingOperation = operation;
                return _activeRun;
            }

            _activeRun = DrainAsync(operation);
            return _activeRun;
        }
    }

    public Task ReplaceAsync(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_sync)
        {
            if (!_activeRun.IsCompleted)
            {
                _pendingOperation = operation;
                _activeCancellation?.Cancel();
                return _activeRun;
            }

            _activeRun = DrainAsync(operation);
            return _activeRun;
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _pendingOperation = null;
            _activeCancellation?.Cancel();
        }
    }

    private async Task DrainAsync(Func<CancellationToken, Task> operation)
    {
        while (true)
        {
            using var cancellation = new CancellationTokenSource();
            lock (_sync)
            {
                _activeCancellation = cancellation;
            }

            try
            {
                await operation(cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }

            lock (_sync)
            {
                if (ReferenceEquals(_activeCancellation, cancellation))
                {
                    _activeCancellation = null;
                }

                if (_pendingOperation is null)
                {
                    return;
                }

                operation = _pendingOperation;
                _pendingOperation = null;
            }
        }
    }
}
