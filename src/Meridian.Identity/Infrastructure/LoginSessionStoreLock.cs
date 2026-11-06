using Meridian.Core.IO;

namespace Meridian.Identity.Infrastructure;

/// <summary>Serializes durable identity state access across processes sharing a session store.</summary>
/// <remarks>
/// Synchronous by design: the caller takes this lease while holding its in-process persistence
/// monitor. Retry, backoff, and the contention filter come from <see cref="ExclusiveFileLock"/>.
/// </remarks>
internal static class LoginSessionStoreLock
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static FileStream Acquire(string path)
    {
        try
        {
            // Retain the lock file: deleting it could create two lock identities. The
            // operating system releases the handle if its owning process terminates.
            return ExclusiveFileLock.Acquire(path, Timeout);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException("Timed out acquiring the authoritative login session store.", exception.InnerException);
        }
    }
}
