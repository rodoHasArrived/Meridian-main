using System.Diagnostics;

namespace Meridian.Identity.Infrastructure;

/// <summary>Serializes durable identity state access across processes sharing a session store.</summary>
internal static class LoginSessionStoreLock
{
    public static FileStream Acquire(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                // Retain the lock file: deleting it could create two lock identities. The
                // operating system releases the handle if its owning process terminates.
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when (IsContention(exception))
            {
                if (elapsed.Elapsed >= TimeSpan.FromSeconds(5))
                    throw new TimeoutException("Timed out acquiring the authoritative login session store.", exception);
                Thread.Sleep(10);
            }
        }
    }

    private static bool IsContention(IOException exception)
        => OperatingSystem.IsWindows()
            ? (exception.HResult & 0xFFFF) is 32 or 33
            : exception.HResult is 11 or 35;
}
