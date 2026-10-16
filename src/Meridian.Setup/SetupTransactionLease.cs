namespace Meridian.Setup;

/// <summary>Serializes install, repair, upgrade, and uninstall on the same user transaction.</summary>
internal sealed class SetupTransactionLease : IDisposable
{
    internal const string MutexName = @"Local\Meridian.Setup.Transaction";
    private readonly Mutex _mutex;

    private SetupTransactionLease(Mutex mutex) => _mutex = mutex;

    internal static SetupTransactionLease Acquire(string mutexName = MutexName)
    {
        var mutex = new Mutex(initiallyOwned: false, mutexName);
        try
        {
            try
            {
                if (!mutex.WaitOne(TimeSpan.Zero))
                    throw new InvalidOperationException("Another Meridian setup transaction is already running.");
            }
            catch (AbandonedMutexException)
            {
                // WaitOne acquired the abandoned transaction; recovery runs while it is owned.
            }

            return new SetupTransactionLease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
