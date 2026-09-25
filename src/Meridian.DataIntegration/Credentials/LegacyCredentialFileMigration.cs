using Meridian.Core.Logging;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.DataIntegration.Credentials;

/// <summary>Serializes legacy secret import and restartable plaintext cleanup across processes.</summary>
public static class LegacyCredentialFileMigration
{
    private static readonly ILogger Log = LoggingSetup.ForContext(typeof(LegacyCredentialFileMigration));
    private const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Imports a legacy JSON snapshot once it has been exclusively claimed. The callback must
    /// retain the imported data in both encrypted generations and finish its audit before returning.
    /// Completed imports move out of the discovery path before erasure, so cleanup can resume
    /// after interruption without parsing an erased or partially erased source.
    /// </summary>
    public static async Task MigrateAsync(string sourcePath,
        Func<string, CancellationToken, Task> importSnapshot, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(importSnapshot);
        sourcePath = Path.GetFullPath(sourcePath);
        var directory = Path.GetDirectoryName(sourcePath)!;
        var cleanupPath = sourcePath + ".migrated";
        if (!File.Exists(sourcePath) && !File.Exists(cleanupPath))
            return;

        using var migrationLock = await AcquireLockAsync(sourcePath + ".migration.lock", ct).ConfigureAwait(false);
        if (File.Exists(cleanupPath))
        {
            SecurelyRemove(cleanupPath);
            await AtomicFileWriter.SyncDirectoryAsync(directory, CancellationToken.None).ConfigureAwait(false);
        }
        // Another process may have finished the import while this caller awaited the lock.
        if (!File.Exists(sourcePath))
            return;

        RestrictExistingSource(sourcePath);
        var json = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
        await importSnapshot(json, ct).ConfigureAwait(false);
        File.Move(sourcePath, cleanupPath);
        await AtomicFileWriter.SyncDirectoryAsync(directory, CancellationToken.None).ConfigureAwait(false);
        SecurelyRemove(cleanupPath);
        await AtomicFileWriter.SyncDirectoryAsync(directory, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Keep the lock file in place so all processes continue to lock one identity.
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33 && started.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private static void RestrictExistingSource(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            var exposed = File.GetUnixFileMode(path) & ~OwnerOnlyFileMode;
            if (exposed == UnixFileMode.None)
                return;
            File.SetUnixFileMode(path, OwnerOnlyFileMode);
            Log.Warning("Legacy credentials at {CredentialPath} were reachable beyond their owner ({ExposedMode}); tightened to owner-only. Treat the credentials as disclosed and rotate them.", path, exposed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Log.Warning("Could not verify legacy credential permissions at {CredentialPath} ({FailureType})", path, ex.GetType().Name);
        }
    }

    private static void SecurelyRemove(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            var zeros = new byte[64 * 1024];
            var remaining = stream.Length;
            while (remaining > 0)
            {
                var count = (int)Math.Min(remaining, zeros.Length);
                stream.Write(zeros, 0, count);
                remaining -= count;
            }
            stream.Flush(flushToDisk: true);
        }
        File.Delete(path);
    }
}
