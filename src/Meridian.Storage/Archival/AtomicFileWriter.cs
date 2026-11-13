using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using Meridian.Contracts.Integrity;
using Meridian.Core.Logging;
using Serilog;

namespace Meridian.Storage.Archival;

/// <summary>
/// Provides atomic file write operations using write-to-temp-then-rename pattern.
/// Ensures files are never partially written even on crash or power loss.
/// </summary>
public static partial class AtomicFileWriter
{
    // Storage paths can contain financial account identities. Diagnostics retain operation
    // outcomes only; exception text can expose the same paths and must not be logged either.
    private static ILogger Log => LoggingSetup.ForContext(typeof(AtomicFileWriter));

    // UTF-8 without a byte-order mark. A BOM would prefix text files (e.g. the .sha256 checksum
    // sidecar) with U+FEFF, which token-splitting readers must not see as part of the content.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Share the same bounded publication gates between sync and async overwrite surfaces.
    // Temp-file creation and fsync stay concurrent; only publication and backup ownership are serialized.
    private static readonly SemaphoreSlim[] WindowsPublicationLocks = Enumerable.Range(0, 64)
        .Select(static _ => new SemaphoreSlim(1, 1))
        .ToArray();

    /// <summary>
    /// Atomically writes content to a file.
    /// Uses a temporary file with rename to ensure atomicity.
    /// </summary>
    public static void Write(string destinationPath, string content, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = GetTempPath(destinationPath);

        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 65536,
                FileOptions.None))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            ct.ThrowIfCancellationRequested();
            MoveIntoPlace(tempPath, destinationPath);
            SyncDirectory(directory!);

            Log.Debug("Atomically wrote {Bytes} bytes", Encoding.UTF8.GetByteCount(content));
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Atomically writes content to a file.
    /// Uses a temporary file with rename to ensure atomicity.
    /// </summary>
    public static Task WriteAsync(
        string destinationPath,
        string content,
        CancellationToken ct = default)
        => WriteAsync(destinationPath, content, unixCreateMode: null, ct);

    /// <summary>
    /// Atomically writes text content to a file, creating it with an explicit Unix permission mode.
    /// </summary>
    /// <remarks>
    /// Text counterpart of the <see cref="byte"/>-array overload, with the same contract: the mode
    /// is applied at creation rather than chmod'ed afterwards, and it suppresses the copy of the
    /// destination's security metadata so an existing over-permissive file cannot re-widen its
    /// replacement. Ignored on Windows, where ACLs rather than mode bits govern.
    /// </remarks>
    public static async Task WriteAsync(
        string destinationPath,
        string content,
        UnixFileMode? unixCreateMode,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = GetTempPath(destinationPath);
        var destinationExists = File.Exists(destinationPath);

        try
        {
            // Write to temp file (no BOM: readers token-split the raw content, e.g. checksum sidecars)
            if (unixCreateMode is { } mode && !OperatingSystem.IsWindows())
            {
                await WriteAllBytesWithModeAsync(tempPath, Utf8NoBom.GetBytes(content), mode, ct);
            }
            else
            {
                await File.WriteAllTextAsync(tempPath, content, Utf8NoBom, ct);
            }

            // Sync the temp file to disk while it is still writable, before copying any
            // (possibly read-only) security metadata from the destination onto it.
            await SyncFileAsync(tempPath, ct);

            if (destinationExists && unixCreateMode is null)
            {
                CopySecurityMetadata(destinationPath, tempPath);
            }

            // Atomic rename
            await MoveIntoPlaceAsync(tempPath, destinationPath);

            // Sync the directory to ensure rename is persisted (post-commit: non-cancellable)
            await SyncCommittedDirectoryAsync(directory!);

            Log.Debug("Atomically wrote {Bytes} bytes", Encoding.UTF8.GetByteCount(content));
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
    public static Task WriteAsync(
        string destinationPath,
        byte[] content,
        CancellationToken ct = default)
        => WriteAsync(destinationPath, content, unixCreateMode: null, ct);

    /// <summary>
    /// Atomically writes binary content to a file, creating it with an explicit Unix permission
    /// mode.
    /// </summary>
    /// <remarks>
    /// For secrets the mode has to be in place from the instant the file exists: chmod-after-write
    /// leaves the content readable for the width of the write, and the temp file this method
    /// renames is real, on-disk, and holding the same bytes. <paramref name="unixCreateMode"/> is
    /// therefore applied at creation rather than afterwards, and suppresses the usual copy of the
    /// destination's security metadata so an existing over-permissive file cannot re-widen the
    /// replacement. Ignored on Windows, where ACLs rather than mode bits govern.
    /// </remarks>
    public static async Task WriteAsync(
        string destinationPath,
        byte[] content,
        UnixFileMode? unixCreateMode,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = GetTempPath(destinationPath);
        var destinationExists = File.Exists(destinationPath);

        try
        {
            // Write to temp file
            if (unixCreateMode is { } mode && !OperatingSystem.IsWindows())
            {
                await WriteAllBytesWithModeAsync(tempPath, content, mode, ct);
            }
            else
            {
                await File.WriteAllBytesAsync(tempPath, content, ct);
            }

            // Sync the temp file to disk while it is still writable, before copying any
            // (possibly read-only) security metadata from the destination onto it.
            await SyncFileAsync(tempPath, ct);

            if (destinationExists && unixCreateMode is null)
            {
                CopySecurityMetadata(destinationPath, tempPath);
            }

            // Atomic rename
            await MoveIntoPlaceAsync(tempPath, destinationPath);

            // Sync the directory (post-commit: non-cancellable)
            await SyncCommittedDirectoryAsync(directory!);

            Log.Debug("Atomically wrote {Bytes} bytes", content.Length);
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
        var destinationExists = File.Exists(destinationPath);

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

            // Sync the temp file while it is still writable, before copying any
            // (possibly read-only) security metadata from the destination onto it.
            await SyncFileAsync(tempPath, ct);

            if (destinationExists)
            {
                CopySecurityMetadata(destinationPath, tempPath);
            }

            // Atomic rename
            await MoveIntoPlaceAsync(tempPath, destinationPath);

            // Sync directory (post-commit: non-cancellable)
            await SyncCommittedDirectoryAsync(directory!);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Atomically writes content to a file using a raw stream writer action, overwriting any
    /// existing file. The destination is only replaced once the temporary file has been fully
    /// written, flushed, and fsynced, and the rename is made durable with a directory fsync.
    /// </summary>
    public static async Task WriteStreamAsync(
        string destinationPath,
        Func<Stream, Task> writeAction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(writeAction);

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = GetTempPath(destinationPath);
        var destinationExists = File.Exists(destinationPath);

        try
        {
            // ReadWrite (not Write) so writeAction can read back bytes it copied into the
            // temp stream — required by formats that merge existing content in place,
            // such as Parquet row-group appends.
            await using (var tempStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 65536,
                FileOptions.Asynchronous))
            {
                await writeAction(tempStream);
                await tempStream.FlushAsync(ct);
            }

            // Sync the temp file to disk while it is still writable, before copying any
            // (possibly read-only) security metadata from the destination onto it.
            await SyncFileAsync(tempPath, ct);

            if (destinationExists)
            {
                CopySecurityMetadata(destinationPath, tempPath);
            }

            // Atomic rename
            await MoveIntoPlaceAsync(tempPath, destinationPath);

            // Sync the directory to ensure the rename is persisted (post-commit: non-cancellable).
            await SyncCommittedDirectoryAsync(directory!);
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
    /// <summary>
    /// Atomically appends by copying the ENTIRE existing destination into a temp file, appending,
    /// and renaming — O(destination size) per call. Suitable for small, low-frequency artifacts
    /// only; hot-path day files use the JSONL sink's persistent append streams instead
    /// (JsonlWriteMode.AppendStream), with the WAL as the crash backstop.
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
            await MoveIntoPlaceAsync(tempPath, destinationPath);
            // post-commit: non-cancellable
            await SyncCommittedDirectoryAsync(directory!);
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
            var checksum = Sha256Digest.Compute(content);

            // Write to temp file
            await File.WriteAllBytesAsync(tempPath, content, ct);

            // Verify what was written
            var verifyBytes = await File.ReadAllBytesAsync(tempPath, ct);
            var verifyChecksum = Sha256Digest.Compute(verifyBytes);

            if (checksum != verifyChecksum)
            {
                throw new InvalidOperationException(
                    $"Write verification failed: expected {checksum}, got {verifyChecksum}");
            }

            // Sync the temp file
            await SyncFileAsync(tempPath, ct);

            // Atomic rename
            await MoveIntoPlaceAsync(tempPath, destinationPath);

            // Write the checksum sidecar atomically (temp + fsync + rename). A bare
            // File.WriteAllTextAsync could leave a torn sidecar on a crash mid-write, which a later
            // health check would misread as payload corruption. Post-commit: non-cancellable so a
            // cancelled token cannot report the already-renamed payload as a failed write.
            var checksumPath = destinationPath + ".sha256";
            await WriteAsync(
                checksumPath, $"{checksum}  {Path.GetFileName(destinationPath)}", CancellationToken.None);

            // Sync directory (post-commit: non-cancellable)
            await SyncCommittedDirectoryAsync(directory!);

            Log.Debug("Wrote {Bytes} bytes with a checksum sidecar", content.Length);

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
            Log.Warning("Checksum sidecar file not found");
            return false;
        }

        var expectedChecksum = (await File.ReadAllTextAsync(checksumPath, ct))
            .Split(' ', 2)[0]
            .Trim()
            .ToLowerInvariant();

        var content = await File.ReadAllBytesAsync(filePath, ct);
        var actualChecksum = Sha256Digest.Compute(content);

        if (expectedChecksum == actualChecksum)
        {
            Log.Debug("File checksum verified");
            return true;
        }

        Log.Warning("File checksum mismatch");
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
        destinationPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = GetTempPath(destinationPath);
        var backupPath = destinationPath + ".bak";

        try
        {
            await File.WriteAllTextAsync(tempPath, newContent, Utf8NoBom, ct);
            await SyncFileAsync(tempPath, ct);
            ct.ThrowIfCancellationRequested();

            // Serialize backup ownership with all other publishers for this destination.
            var gate = WindowsPublicationGate(Path.GetFullPath(destinationPath));
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        if (OperatingSystem.IsWindows())
                            await ReplaceWindowsAsync(tempPath, destinationPath, backupPath, keepBackup, ct);
                        else if (File.Exists(destinationPath))
                            File.Replace(tempPath, destinationPath, keepBackup ? backupPath : null);
                        else
                            File.Move(tempPath, destinationPath, overwrite: false);
                        break;
                    }
                    catch (Exception exception) when (attempt < ReplaceAttempts && IsTransientReplaceFailure(exception))
                    {
                        await Task.Delay(ReplaceRetryDelay * attempt).ConfigureAwait(false);
                    }
                }
                // Commit has happened; cancellation or backup cleanup must not report a
                // committed replacement as a failed write.
                await SyncCommittedDirectoryAsync(directory!);
                if (!keepBackup)
                    TryDeleteFile(backupPath);
            }
            finally
            {
                gate.Release();
            }
            Log.Debug("Atomically replaced file");
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task ReplaceWindowsAsync(
        string tempPath, string destinationPath, string backupPath, bool keepBackup, CancellationToken ct)
    {
        string? backupTempPath = null;
        WindowsRename? backupPublication = null;
        try
        {
            var destinationExists = File.Exists(destinationPath);
            FileSecurity? destinationSecurity = null;
            if (destinationExists)
            {
                // File.Replace used to preserve the destination DACL. Keep that contract when
                // publishing by rename, and fail before commit if it cannot be preserved.
                destinationSecurity = new FileInfo(destinationPath).GetAccessControl();
                ApplyWindowsAccessRules(tempPath, destinationSecurity);
            }

            using var publication = WindowsRename.Prepare(tempPath, destinationPath);
            if (destinationExists && keepBackup)
            {
                backupTempPath = Path.Combine(Path.GetDirectoryName(destinationPath)!,
                    $".atomic-recovery.{Guid.NewGuid():N}.bak");
                await using (var source = new FileStream(destinationPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var backup = new FileInfo(backupTempPath).Create(FileMode.CreateNew,
                    FileSystemRights.Write, FileShare.None, 65536, FileOptions.Asynchronous, destinationSecurity))
                {
                    await source.CopyToAsync(backup, ct).ConfigureAwait(false);
                    backup.Flush(flushToDisk: true);
                }
                // Creation enforces the original DACL immediately, but Windows can drop its
                // auto-inherited flag. Persist the complete access descriptor before publication.
                ApplyWindowsAccessRules(backupTempPath, destinationSecurity!);
                // Acquire both source and destination handles before the live commit, so a
                // denied/read-only backup fails without changing either published generation.
                backupPublication = WindowsRename.Prepare(backupTempPath, backupPath);
            }

            ct.ThrowIfCancellationRequested();
            publication.Publish();
            if (backupPublication is not null)
            {
                try
                {
                    backupPublication.Publish();
                }
                catch (Exception exception)
                {
                    // The live rename already committed. Retain the flushed old generation if
                    // a later filesystem fault prevents backup publication; never invite a retry
                    // by reporting the committed live write as failed.
                    Log.Error("Committed replacement retained its previous generation in {BackupName} " +
                        "because backup publication failed (error {ErrorCode})",
                        Path.GetFileName(backupTempPath), exception.HResult);
                    backupTempPath = null;
                }
            }
        }
        finally
        {
            backupPublication?.Dispose();
            if (backupTempPath is not null)
                TryDeleteFile(backupTempPath);
        }
    }

    // Windows refuses to replace a destination while another handle without delete sharing is open on
    // it (antivirus and search indexers scan freshly written files; a reader opened without
    // FileShare.Delete does the same), surfacing as ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION or
    // ERROR_ACCESS_DENIED. Those handles close within milliseconds, so the rename is retried briefly
    // before the failure is reported. POSIX rename never fails for open handles, and EACCES there is
    // a real permission problem, so no retry applies off Windows.
    private const int ReplaceAttempts = 10;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(20);

    private static bool IsTransientReplaceFailure(Exception exception)
        => OperatingSystem.IsWindows()
            && (exception is UnauthorizedAccessException
                || (exception is IOException io && (io.HResult & 0xffff) is 32 or 33));

    private static void MoveIntoPlace(string tempPath, string destinationPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            MoveWithRetry(tempPath, destinationPath, overwrite: true);
            return;
        }
        var destination = Path.GetFullPath(destinationPath);
        var gate = WindowsPublicationGate(destination);
        gate.Wait();
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var publication = WindowsRename.Prepare(tempPath, destination);
                    publication.Publish();
                    return;
                }
                catch (Exception exception) when (attempt < ReplaceAttempts && IsTransientReplaceFailure(exception))
                {
                    Thread.Sleep(ReplaceRetryDelay * attempt);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task MoveIntoPlaceAsync(string tempPath, string destinationPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            await MoveWithRetryAsync(tempPath, destinationPath, overwrite: true).ConfigureAwait(false);
            return;
        }
        var destination = Path.GetFullPath(destinationPath);
        var gate = WindowsPublicationGate(destination);
        // The complete temp file has passed the commit decision; cancellation cannot interrupt
        // publication or turn a committed replacement into a reported cancellation.
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var publication = WindowsRename.Prepare(tempPath, destination);
                    publication.Publish();
                    return;
                }
                catch (Exception exception) when (attempt < ReplaceAttempts && IsTransientReplaceFailure(exception))
                {
                    await Task.Delay(ReplaceRetryDelay * attempt).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static SemaphoreSlim WindowsPublicationGate(string destinationPath)
    {
        // Extended Win32 spellings address the same file as ordinary drive/UNC paths.
        // Normalize them before choosing a gate, including any dot segments after the prefix.
        if (OperatingSystem.IsWindows())
        {
            if (destinationPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                destinationPath = @"\\" + destinationPath[8..];
            else if (destinationPath.StartsWith(@"\\?\", StringComparison.Ordinal)
                && destinationPath.Length >= 7 && char.IsAsciiLetter(destinationPath[4])
                && destinationPath[5] == ':' && destinationPath[6] == '\\')
                destinationPath = destinationPath[4..];
            destinationPath = Path.GetFullPath(destinationPath);
        }
        return WindowsPublicationLocks[(StringComparer.OrdinalIgnoreCase.GetHashCode(destinationPath) & int.MaxValue)
            % WindowsPublicationLocks.Length];
    }

    private static void MoveWithRetry(string sourcePath, string destinationPath, bool overwrite)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, overwrite);
                return;
            }
            catch (Exception exception) when (attempt < ReplaceAttempts && IsTransientReplaceFailure(exception))
            {
                Thread.Sleep(ReplaceRetryDelay * attempt);
            }
        }
    }

    // Post-commit-decision step: the temp file is complete and durable, so a caller cancellation is
    // deliberately not observed here; the bounded attempt count limits retry backoff.
    private static async Task MoveWithRetryAsync(string sourcePath, string destinationPath, bool overwrite)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, overwrite);
                return;
            }
            catch (Exception exception) when (attempt < ReplaceAttempts && IsTransientReplaceFailure(exception))
            {
                await Task.Delay(ReplaceRetryDelay * attempt).ConfigureAwait(false);
            }
        }
    }

    private static string GetTempPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath) ?? ".";
        var fileName = Path.GetFileName(destinationPath);
        return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
    }

    // FileStreamOptions.UnixCreateMode passes the mode to open(2), so the file is never visible at
    // the umask default first. File.WriteAllBytesAsync offers no equivalent, which is why this
    // exists rather than a chmod after the fact.
    private static async Task WriteAllBytesWithModeAsync(
        string path,
        byte[] content,
        UnixFileMode mode,
        CancellationToken ct)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            UnixCreateMode = mode,
        };

        await using var stream = new FileStream(path, options);
        await stream.WriteAsync(content, ct);
    }

    private static void CopySecurityMetadata(string sourcePath, string targetPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var sourceSecurity = new FileInfo(sourcePath).GetAccessControl();
                ApplyWindowsAccessRules(targetPath, sourceSecurity);
                return;
            }

            var sourceMode = File.GetUnixFileMode(sourcePath);
            File.SetUnixFileMode(targetPath, sourceMode);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to preserve file security metadata during atomic write ({FailureType})",
                ex.GetType().Name);
        }
    }

    private static async Task SyncFileAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Open for write and force an OS-level flush to physical disk. A read-only handle with
        // FlushAsync only touches the (empty) managed read buffer and never performs an fsync,
        // so the file contents would remain non-durable before the rename.
        await using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite);
        fs.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Fsyncs a directory so that prior rename/link operations within it are durable after a crash
    /// or power loss. No-op on platforms where directory metadata is journaled automatically.
    /// </summary>
    public static Task SyncDirectoryAsync(string directory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        SyncDirectory(directory);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Fsyncs a directory after the operation it belongs to has already committed (the rename or
    /// delete has succeeded). This durability step must not observe caller cancellation: throwing
    /// here would report an already-committed operation as failed, leading callers to retry or
    /// restore against state that no longer exists.
    /// </summary>
    private static Task SyncCommittedDirectoryAsync(string directory)
        => SyncDirectoryAsync(directory, CancellationToken.None);

    private static void SyncDirectory(string directory)
    {
        // A bare relative path (e.g. "file.txt") yields an empty directory; treat it as the
        // current directory rather than faulting.
        if (string.IsNullOrEmpty(directory))
        {
            directory = ".";
        }

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            // On Windows, NTFS journals directory metadata changes automatically.
            return;
        }

        // On POSIX systems, fsync the directory file descriptor to ensure
        // that rename/link operations are durable after a crash or power loss.
        int fd = PosixInterop.open(directory, PosixInterop.O_RDONLY);
        if (fd < 0)
        {
            Log.Warning("Unable to open directory for fsync (errno {Errno})", Marshal.GetLastPInvokeError());
            return;
        }

        try
        {
            if (PosixInterop.fsync(fd) < 0)
            {
                Log.Warning("Directory fsync failed (errno {Errno})", Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            PosixInterop.close(fd);
        }
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
            Log.Warning("Failed to delete temporary file ({FailureType})", ex.GetType().Name);
        }
    }
}
