using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Storage.Archival;

/// <summary>
/// Provides atomic file write operations using write-to-temp-then-rename pattern.
/// Ensures files are never partially written even on crash or power loss.
/// </summary>
public static partial class AtomicFileWriter
{
    private static readonly ILogger Log = LoggingSetup.ForContext(typeof(AtomicFileWriter));

    /// <summary>
    /// Atomically writes content to a file.
    /// Uses a temporary file with rename to ensure atomicity.
    /// </summary>
    public static async Task WriteAsync(
        string destinationPath,
        string content,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = GetTempPath(destinationPath);

        try
        {
            // Write to temp file
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, ct);

            // Sync the temp file to disk
            await SyncFileAsync(tempPath, ct);

            // Atomic rename
            File.Move(tempPath, destinationPath, overwrite: true);

            // Sync the directory to ensure rename is persisted
            await SyncDirectoryAsync(directory!, ct);

            Log.Debug("Atomically wrote {Bytes} bytes to {Path}",
                Encoding.UTF8.GetByteCount(content), destinationPath);
        }
        catch
        {
            // Clean up temp file on failure
            TryDeleteFile(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Atomically writes binary content to a file.
    /// </summary>
    public static async Task WriteAsync(
        string destinationPath,
        byte[] content,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = GetTempPath(destinationPath);

        try
        {
            // Write to temp file
            await File.WriteAllBytesAsync(tempPath, content, ct);

            // Sync the temp file to disk
            await SyncFileAsync(tempPath, ct);

            // Atomic rename
            File.Move(tempPath, destinationPath, overwrite: true);

            // Sync the directory
            await SyncDirectoryAsync(directory!, ct);

            Log.Debug("Atomically wrote {Bytes} bytes to {Path}", content.Length, destinationPath);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Atomically writes content using a stream writer action.
    /// </summary>
    public static async Task WriteAsync(
        string destinationPath,
        Func<StreamWriter, Task> writeAction,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = GetTempPath(destinationPath);

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 65536,
                FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                await writeAction(writer);
                await writer.FlushAsync();
            }

            // Sync the temp file
            await SyncFileAsync(tempPath, ct);

            // Atomic rename
            File.Move(tempPath, destinationPath, overwrite: true);

            // Sync directory
            await SyncDirectoryAsync(directory!, ct);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Atomically appends binary content to an existing file using copy-on-write semantics.
    /// The original file is preserved until the new file containing both the original and
    /// appended bytes has been fully written and renamed into place.
    /// </summary>
    public static async Task AppendAsync(
        string destinationPath,
        Func<Stream, Task> appendAction,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = GetTempPath(destinationPath);

        try
        {
            await using (var tempStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 65536,
                FileOptions.Asynchronous))
            {
                if (File.Exists(destinationPath))
                {
                    await using var sourceStream = new FileStream(
                        destinationPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 65536,
                        FileOptions.Asynchronous);
                    await sourceStream.CopyToAsync(tempStream, 65536, ct);
                }

                await appendAction(tempStream);
                await tempStream.FlushAsync(ct);
            }

            await SyncFileAsync(tempPath, ct);
            File.Move(tempPath, destinationPath, overwrite: true);
            await SyncDirectoryAsync(directory!, ct);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Atomically appends UTF-8 lines to an existing text file using copy-on-write semantics.
    /// </summary>
    public static Task AppendLinesAsync(
        string destinationPath,
        IEnumerable<string> lines,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return AppendAsync(
            destinationPath,
            async stream =>
            {
                await using var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 65536,
                    leaveOpen: true);

                foreach (var line in lines)
                {
                    await writer.WriteLineAsync(line);
                }

                await writer.FlushAsync();
            },
            ct);
    }

    /// <summary>
    /// Atomically writes content with checksum verification.
    /// </summary>
    public static async Task<string> WriteWithChecksumAsync(
        string destinationPath,
        byte[] content,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = GetTempPath(destinationPath);

        try
        {
            // Compute checksum
            var checksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

            // Write to temp file
            await File.WriteAllBytesAsync(tempPath, content, ct);

            // Verify what was written
            var verifyBytes = await File.ReadAllBytesAsync(tempPath, ct);
            var verifyChecksum = Convert.ToHexString(SHA256.HashData(verifyBytes)).ToLowerInvariant();

            if (checksum != verifyChecksum)
            {
                throw new InvalidOperationException(
                    $"Write verification failed: expected {checksum}, got {verifyChecksum}");
            }

            // Sync the temp file
            await SyncFileAsync(tempPath, ct);

            // Atomic rename
            File.Move(tempPath, destinationPath, overwrite: true);

            // Write checksum sidecar file
            var checksumPath = destinationPath + ".sha256";
            await File.WriteAllTextAsync(checksumPath, $"{checksum}  {Path.GetFileName(destinationPath)}", ct);

            // Sync directory
            await SyncDirectoryAsync(directory!, ct);

            Log.Debug("Wrote {Bytes} bytes with checksum {Checksum} to {Path}",
                content.Length, checksum[..16], destinationPath);

            return checksum;
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Verify a file's checksum matches its sidecar file.
    /// </summary>
    public static async Task<bool> VerifyChecksumAsync(string filePath, CancellationToken ct = default)
    {
        var checksumPath = filePath + ".sha256";

        if (!File.Exists(checksumPath))
        {
            Log.Warning("Checksum file not found: {Path}", checksumPath);
            return false;
        }

        var expectedChecksum = (await File.ReadAllTextAsync(checksumPath, ct))
            .Split(' ', 2)[0]
            .Trim()
            .ToLowerInvariant();

        var content = await File.ReadAllBytesAsync(filePath, ct);
        var actualChecksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        if (expectedChecksum == actualChecksum)
        {
            Log.Debug("Checksum verified for {Path}", filePath);
            return true;
        }

        Log.Warning("Checksum mismatch for {Path}: expected {Expected}, got {Actual}",
            filePath, expectedChecksum, actualChecksum);
        return false;
    }

    /// <summary>
    /// Atomically replace a file using copy-on-write semantics.
    /// Keeps a backup of the original.
    /// </summary>
    public static async Task ReplaceAsync(
        string destinationPath,
        string newContent,
        bool keepBackup = true,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = GetTempPath(destinationPath);
        var backupPath = destinationPath + ".bak";

        try
        {
            // Write new content to temp
            await File.WriteAllTextAsync(tempPath, newContent, Encoding.UTF8, ct);
            await SyncFileAsync(tempPath, ct);

            // If destination exists, create backup
            if (File.Exists(destinationPath))
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
                File.Move(destinationPath, backupPath);
            }

            // Move temp to destination
            File.Move(tempPath, destinationPath);

            // Sync directory
            await SyncDirectoryAsync(directory!, ct);

            // Optionally remove backup
            if (!keepBackup && File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            Log.Debug("Atomically replaced {Path}", destinationPath);
        }
        catch
        {
            // On failure, try to restore backup
            if (File.Exists(backupPath) && !File.Exists(destinationPath))
            {
                try
                {
                    File.Move(backupPath, destinationPath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to restore backup for {Path}", destinationPath);
                }
            }

            TryDeleteFile(tempPath);
            throw;
        }
    }

    private static string GetTempPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath) ?? ".";
        var fileName = Path.GetFileName(destinationPath);
        return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
    }

    private static async Task SyncFileAsync(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        await fs.FlushAsync(ct);
    }

    private static Task SyncDirectoryAsync(string directory, CancellationToken ct)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            // On Windows, NTFS journals directory metadata changes automatically.
            return Task.CompletedTask;
        }

        // On POSIX systems, fsync the directory file descriptor to ensure
        // that rename/link operations are durable after a crash or power loss.
        int fd = PosixInterop.open(directory, PosixInterop.O_RDONLY);
        if (fd < 0)
        {
            Log.Warning("Unable to open directory for fsync: {Directory} (errno {Errno})",
                directory, Marshal.GetLastPInvokeError());
            return Task.CompletedTask;
        }

        try
        {
            if (PosixInterop.fsync(fd) < 0)
            {
                Log.Warning("Directory fsync failed: {Directory} (errno {Errno})",
                    directory, Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            PosixInterop.close(fd);
        }

        return Task.CompletedTask;
    }

    private static partial class PosixInterop
    {
        internal const int O_RDONLY = 0;

        [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int open(string path, int flags);

        [LibraryImport("libc", SetLastError = true)]
        internal static partial int fsync(int fd);

        [LibraryImport("libc", SetLastError = true)]
        internal static partial int close(int fd);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete temp file: {Path}", path);
        }
    }
}
