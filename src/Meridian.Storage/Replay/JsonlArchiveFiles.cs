using System.IO.Compression;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Replay;

/// <summary>
/// Discovery for lossless plain-JSONL to gzip publication. The source and its create-only
/// gzip destination may both survive a crash; they are one logical archive. Readers never
/// change files; only <see cref="TryRetireEqualSourceAsync"/>, used by fenced writers, completes
/// an interrupted cleanup. Recovery copies, checksum sidecars and staging files are never
/// directory candidates.
/// </summary>
internal static class JsonlArchiveFiles
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static bool IsCanonicalArchivePath(string path) =>
        path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ||
        IsGzipArchive(path) ||
        path.EndsWith(".jsonl.gzip", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jsonl.zst", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jsonl.lz4", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jsonl.br", StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> Enumerate(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(IsCanonicalArchivePath)
            .Distinct(PathComparer)
            // Snapshot logical identities before yielding. A scan can observe both names
            // while publication proceeds; filename-level grouping must precede replay.
            .GroupBy(LogicalPath, PathComparer)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var compressed = group.Where(IsGzipArchive).ToArray();
                if (compressed.Length > 1)
                    throw new IOException($"Ambiguous gzip archive representations: {group.Key}");
                // Preserve actual suffix casing on case-sensitive filesystems.
                return compressed.Length == 1 ? compressed[0] : group.Single();
            })
            .ToArray();

    internal static string LogicalPath(string path) => IsGzipArchive(path) ? path[..^3] : path;

    // Symmetric with OpenReadAsync: a retained gzip path whose archive was withdrawn still
    // resolves to its surviving plain source, and a plain path to its published gzip.
    internal static bool Exists(string path) => File.Exists(path) ||
        (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) && File.Exists(path + ".gz")) ||
        (IsGzipArchive(path) && File.Exists(path[..^3]));

    /// <summary>
    /// Retains a handle before the caller inspects length or codec. A captured plain path can
    /// resolve to gzip after cleanup. If both survive, prove payload equivalence before any
    /// event is emitted; conflicting evidence fails closed and neither copy is changed.
    /// </summary>
    // Test seam: runs after the plain source is opened and before the archive is, for one path.
    private static readonly AsyncLocal<Action<string>?> s_afterSourceOpened = new();

    internal static Action<string>? AfterSourceOpenedForTest
    {
        get => s_afterSourceOpened.Value;
        set => s_afterSourceOpened.Value = value;
    }

    internal static Task<FileStream> OpenReadAsync(string path, CancellationToken ct) =>
        OpenReadAsync(path, ct, FileShare.ReadWrite | FileShare.Delete);

    // Replay's closed-capture lease uses None for both representations. Other readers retain
    // their existing shared handle and publication-race behavior through the overload above.
    internal static async Task<FileStream> OpenReadAsync(string path, CancellationToken ct, FileShare share)
    {
        ct.ThrowIfCancellationRequested();
        var requestedGzip = IsGzipArchive(path);
        if (!requestedGzip && !path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            return Open(path, share);

        // The two representations are opened one after the other. Compression can delete the source
        // and a late append can extend the published archive in between, so the archive legitimately
        // holds more than the retained source. That is a later generation, not conflicting evidence:
        // once the source is gone the archive is the only representation, so resolve again.
        for (var attempt = 1; ; attempt++)
        {
            var resolved = await TryOpenOneGenerationAsync(path, requestedGzip, finalAttempt: attempt == 3, ct, share).ConfigureAwait(false);
            if (resolved is not null)
                return resolved;
        }
    }

    private static async Task<FileStream?> TryOpenOneGenerationAsync(string path, bool requestedGzip, bool finalAttempt, CancellationToken ct, FileShare share)
    {
        var sourcePath = requestedGzip ? path[..^3] : path;
        var gzipPath = requestedGzip ? path : path + ".gz";
        FileStream? source = null;
        FileStream? compressed = null;
        try
        {
            source = TryOpen(sourcePath, share);
            if (source is not null)
                s_afterSourceOpened.Value?.Invoke(sourcePath);
            compressed = TryOpen(gzipPath, share);
            if (source is null && compressed is null)
                throw new FileNotFoundException("No representation remains for the JSONL archive.", path);

            if (source is not null && compressed is not null)
            {
                var sourceDigest = await Sha256Digest.ComputeAsync(source, ct).ConfigureAwait(false);
                using (var gzip = new GZipStream(compressed, CompressionMode.Decompress, leaveOpen: true))
                {
                    var compressedDigest = await Sha256Digest.ComputeAsync(gzip, ct).ConfigureAwait(false);
                    if (!Sha256Digest.FixedEquals(sourceDigest, compressedDigest))
                    {
                        if (!finalAttempt && !File.Exists(sourcePath))
                        {
                            source.Dispose();
                            compressed.Dispose();
                            return null;
                        }
                        throw new IOException($"Conflicting JSONL archive representations: {sourcePath}");
                    }
                }
                source.Position = 0;
                compressed.Position = 0;
            }

            var selected = requestedGzip ? compressed ?? source! : source ?? compressed!;
            if (!ReferenceEquals(source, selected))
                source?.Dispose();
            if (!ReferenceEquals(compressed, selected))
                compressed?.Dispose();
            return selected;
        }
        catch
        {
            source?.Dispose();
            compressed?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Completes cleanup interrupted after gzip publication: when <paramref name="sourcePath"/>
    /// and its <c>.gz</c> hold equal payloads, makes the archive durable and removes the source
    /// and its checksum sidecar. Returns false when no archive exists. Unequal representations
    /// fail closed and neither copy is changed. Callers hold the path's write fence.
    /// </summary>
    internal static async Task<bool> TryRetireEqualSourceAsync(string sourcePath, CancellationToken ct)
    {
        var gzipPath = sourcePath + ".gz";
        await using (var source = TryOpen(sourcePath))
        await using (var compressed = TryOpenForSync(gzipPath))
        {
            if (compressed is null)
                return false;
            if (source is not null)
            {
                var sourceDigest = await Sha256Digest.ComputeAsync(source, ct).ConfigureAwait(false);
                await using var gzip = new GZipStream(compressed, CompressionMode.Decompress, leaveOpen: true);
                var compressedDigest = await Sha256Digest.ComputeAsync(gzip, ct).ConfigureAwait(false);
                if (!Sha256Digest.FixedEquals(sourceDigest, compressedDigest))
                    throw new IOException($"Conflicting JSONL archive representations: {sourcePath}");
            }
            compressed.Flush(flushToDisk: true);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        await AtomicFileWriter.SyncDirectoryAsync(directory, CancellationToken.None).ConfigureAwait(false);
        File.Delete(sourcePath);
        File.Delete(sourcePath + ".sha256");
        await AtomicFileWriter.SyncDirectoryAsync(directory, CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private static FileStream? TryOpenForSync(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete,
                64 * 1024, useAsync: true);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static bool IsGzipArchive(string path) =>
        path.EndsWith(".jsonl.gz", StringComparison.OrdinalIgnoreCase);

    private static FileStream? TryOpen(string path, FileShare share = FileShare.ReadWrite | FileShare.Delete)
    {
        try
        {
            return Open(path, share);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static FileStream Open(string path, FileShare share) => new(
        path, FileMode.Open, FileAccess.Read, share,
        64 * 1024, useAsync: true);
}
