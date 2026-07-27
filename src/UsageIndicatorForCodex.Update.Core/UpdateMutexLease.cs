using System.Security.Principal;

namespace UsageIndicatorForCodex.Update;

internal interface IUpdateMutexLease : IDisposable
{
    bool IsAcquired { get; }
}

internal sealed class UpdateMutexLease : IUpdateMutexLease
{
    private readonly ManualResetEventSlim _acquisitionCompleted = new(false);
    private readonly ManualResetEventSlim _releaseRequested = new(false);
    private readonly Thread _ownerThread;
    private Exception? _acquisitionFailure;
    private bool _disposed;

    internal UpdateMutexLease(string userIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentity);
        MutexName = $"Local\\UsageIndicatorForCodex-Update-{userIdentity}";
        _ownerThread = new Thread(HoldMutex)
        {
            IsBackground = true,
            Name = "UsageIndicatorForCodex update mutex"
        };
        _ownerThread.Start();
        _acquisitionCompleted.Wait();
        if (_acquisitionFailure is not null)
        {
            Dispose();
            throw new InvalidOperationException(
                "The update lock could not be acquired.",
                _acquisitionFailure);
        }
    }

    internal string MutexName { get; }

    public bool IsAcquired { get; private set; }

    internal static UpdateMutexLease CreateForCurrentUser()
    {
        var userIdentity = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The Windows user could not be identified.");
        return new UpdateMutexLease(userIdentity);
    }

    private void HoldMutex()
    {
        try
        {
            using var mutex = new Mutex(false, MutexName);
            try
            {
                IsAcquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                IsAcquired = true;
            }

            _acquisitionCompleted.Set();
            if (!IsAcquired)
            {
                return;
            }

            _releaseRequested.Wait();
            mutex.ReleaseMutex();
        }
        catch (Exception exception)
        {
            _acquisitionFailure = exception;
            _acquisitionCompleted.Set();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _releaseRequested.Set();
        _ownerThread.Join();
        _releaseRequested.Dispose();
        _acquisitionCompleted.Dispose();
    }
}
