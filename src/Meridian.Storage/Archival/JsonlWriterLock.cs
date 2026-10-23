namespace Meridian.Storage.Archival;

/// <summary>
/// A cross-process writers-only lock beside a plain JSONL source. Every appending sink, in any
/// process sharing the data root, holds it shared while its append stream is open; compression
/// holds it exclusively for the whole verify-publish-delete window. <see cref="JsonlWriteFence"/>
/// gives the same exclusion inside one process; this extends it to writers in other processes,
/// so compression never deletes a source another process can still append to.
/// </summary>
/// <remarks>
/// <para>Readers never take it, so a replay may keep reading a source while it is compressed. The
/// lock is the open handle on <see cref="PathFor"/>: shared holders open it with
/// <see cref="FileShare.ReadWrite"/> and the exclusive holder with <see cref="FileShare.None"/>,
/// which Windows enforces with share modes and Unix with flock LOCK_SH/LOCK_EX. A crashed holder
/// releases when the operating system closes its handles.</para>
/// <para>The file is never deleted, so every writer keeps contending on the same inode; it carries
/// no state, and its suffix is not a data extension, so storage scanners ignore it. Take it after
/// the in-process fence, as compression does, so the two locks are always ordered the same way.</para>
/// </remarks>
internal static class JsonlWriterLock
{
    public static string PathFor(string sourcePath) => sourcePath + ".write-lock";

    /// <summary>
    /// Waits for any compression of <paramref name="sourcePath"/> to finish, then holds the source
    /// open for appending until the returned handle is disposed.
    /// </summary>
    public static Task<FileStream> AcquireSharedAsync(string sourcePath, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        return ArchiveAppendLock.AcquireLockFileAsync(PathFor(sourcePath), FileShare.ReadWrite, timeout, ct);
    }

    /// <summary>
    /// Takes the source exclusively without waiting. Returns false while any writer, in this
    /// process or another, holds it shared.
    /// </summary>
    /// <remarks>With <paramref name="createIfMissing"/> false, a source no writer has ever locked
    /// yields true with no handle and no lock file is created.</remarks>
    public static bool TryAcquireExclusive(string sourcePath, out FileStream? handle, bool createIfMissing = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        try
        {
            handle = new FileStream(PathFor(sourcePath), createIfMissing ? FileMode.OpenOrCreate : FileMode.Open,
                FileAccess.ReadWrite, FileShare.None, bufferSize: 1, options: FileOptions.Asynchronous | FileOptions.WriteThrough);
            return true;
        }
        catch (Exception exception) when (!createIfMissing && exception is FileNotFoundException or DirectoryNotFoundException)
        {
            handle = null;
            return true;
        }
        catch (IOException exception) when (ArchiveAppendLock.IsContention(exception))
        {
            handle = null;
            return false;
        }
    }
}
