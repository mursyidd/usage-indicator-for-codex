namespace CodexUsageIndicator.Services;

internal sealed class CoalescingRefreshRunner
{
    private readonly object _sync = new();
    private Task _activeRun = Task.CompletedTask;
    private Func<Task>? _pendingOperation;

    public Task RunAsync(Func<Task> operation)
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

    private async Task DrainAsync(Func<Task> operation)
    {
        while (true)
        {
            await operation();

            lock (_sync)
            {
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
