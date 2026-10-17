using System.IO.Compression;
using Meridian.Contracts.Integrity;

namespace Meridian.Storage.Replay;

/// <summary>
/// Read-only discovery for lossless plain-JSONL to gzip publication. The source and its
/// create-only gzip destination may both survive a crash; they are one logical archive.
/// Recovery copies, checksum sidecars and staging files are never directory candidates.
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
            .GroupBy(path => IsGzipArchive(path) ? path[..^3] : path, PathComparer)
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

    internal static bool Exists(string path) => File.Exists(path) ||
        (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) && File.Exists(path + ".gz"));

    /// <summary>
    /// Retains a handle before the caller inspects length or codec. A captured plain path can
    /// resolve to gzip after cleanup. If both survive, prove payload equivalence before any
    /// event is emitted; conflicting evidence fails closed and neither copy is changed.
    /// </summary>
    internal static async Task<FileStream> OpenReadAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var requestedGzip = IsGzipArchive(path);
        if (!requestedGzip && !path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            return Open(path);

        var sourcePath = requestedGzip ? path[..^3] : path;
        var gzipPath = requestedGzip ? path : path + ".gz";
        FileStream? source = null;
        FileStream? compressed = null;
        try
        {
            source = TryOpen(sourcePath);
            compressed = TryOpen(gzipPath);
            if (source is null && compressed is null)
                throw new FileNotFoundException("No representation remains for the JSONL archive.", path);

            if (source is not null && compressed is not null)
            {
                var sourceDigest = await Sha256Digest.ComputeAsync(source, ct).ConfigureAwait(false);
                using (var gzip = new GZipStream(compressed, CompressionMode.Decompress, leaveOpen: true))
                {
                    var compressedDigest = await Sha256Digest.ComputeAsync(gzip, ct).ConfigureAwait(false);
                    if (!Sha256Digest.FixedEquals(sourceDigest, compressedDigest))
                        throw new IOException($"Conflicting JSONL archive representations: {sourcePath}");
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

    private static bool IsGzipArchive(string path) =>
        path.EndsWith(".jsonl.gz", StringComparison.OrdinalIgnoreCase);

    private static FileStream? TryOpen(string path)
    {
        try
        {
            return Open(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static FileStream Open(string path) => new(
        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
        64 * 1024, useAsync: true);
}
