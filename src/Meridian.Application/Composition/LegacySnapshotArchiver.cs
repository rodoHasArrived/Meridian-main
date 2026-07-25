using System.Security.Cryptography;

namespace Meridian.Application.Composition;

internal enum LegacySnapshotArchiveResult : byte
{
    Archived = 0,
    AlreadyArchived = 1
}

/// <summary>
/// Claims and archives the exact legacy snapshot bytes whose hash was committed by a
/// transactional startup import. The pending name is deliberately stable so a later
/// startup can complete an archive after process failure.
/// </summary>
internal static class LegacySnapshotArchiver
{
    private const string PendingSuffix = ".archive-pending";
    private const string ImportedSuffix = ".imported";

    internal static string? ResolveReadableSnapshotPath(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var pendingPath = GetPendingPath(sourcePath);
        var sourceExists = File.Exists(sourcePath);
        var pendingExists = File.Exists(pendingPath);
        if (sourceExists && pendingExists)
        {
            throw new IOException(
                $"Legacy snapshot archival conflict: both '{sourcePath}' and its pending claim '{pendingPath}' exist.");
        }

        if (sourceExists)
            return sourcePath;
        if (pendingExists)
            return pendingPath;

        return null;
    }

    internal static async Task<LegacySnapshotArchiveResult> ArchiveCommittedSnapshotAsync(
        string sourcePath,
        string expectedSourceHash,
        int maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        var expectedHash = NormalizeSourceHash(expectedSourceHash);
        var pendingPath = GetPendingPath(sourcePath);
        var importedPath = GetImportedPath(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();

        var sourceExists = File.Exists(sourcePath);
        var pendingExists = File.Exists(pendingPath);
        if (sourceExists && pendingExists)
        {
            throw new IOException(
                $"Legacy snapshot archival conflict: both '{sourcePath}' and its pending claim '{pendingPath}' exist.");
        }

        if (sourceExists)
        {
            try
            {
                File.Move(sourcePath, pendingPath);
                pendingExists = true;
            }
            catch (FileNotFoundException) when (!File.Exists(sourcePath))
            {
                pendingExists = File.Exists(pendingPath);
            }
        }

        if (!pendingExists)
        {
            if (await FileMatchesHashAsync(
                    importedPath,
                    expectedHash,
                    maximumBytes,
                    cancellationToken).ConfigureAwait(false))
            {
                return LegacySnapshotArchiveResult.AlreadyArchived;
            }

            throw new IOException(
                $"Legacy snapshot '{sourcePath}' disappeared before its committed bytes could be archived.");
        }

        byte[] pendingHash;
        try
        {
            pendingHash = await ComputeFileHashAsync(
                pendingPath,
                maximumBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException) when (!File.Exists(pendingPath))
        {
            if (await FileMatchesHashAsync(
                    importedPath,
                    expectedHash,
                    maximumBytes,
                    cancellationToken).ConfigureAwait(false))
            {
                return LegacySnapshotArchiveResult.AlreadyArchived;
            }

            throw;
        }
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, pendingHash))
        {
            if (!File.Exists(sourcePath) && File.Exists(pendingPath))
                File.Move(pendingPath, sourcePath);

            throw new IOException(
                $"Legacy snapshot '{sourcePath}' changed after it was hashed; the replacement was not archived.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            File.Move(pendingPath, importedPath, overwrite: true);
            return LegacySnapshotArchiveResult.Archived;
        }
        catch (FileNotFoundException) when (!File.Exists(pendingPath))
        {
            if (await FileMatchesHashAsync(
                    importedPath,
                    expectedHash,
                    maximumBytes,
                    cancellationToken).ConfigureAwait(false))
            {
                return LegacySnapshotArchiveResult.AlreadyArchived;
            }

            throw;
        }
    }

    internal static string GetPendingPath(string sourcePath) => sourcePath + PendingSuffix;

    internal static string GetImportedPath(string sourcePath) => sourcePath + ImportedSuffix;

    private static async Task<bool> FileMatchesHashAsync(
        string path,
        byte[] expectedHash,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return false;

        var actualHash = await ComputeFileHashAsync(path, maximumBytes, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    private static async Task<byte[]> ComputeFileHashAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Legacy snapshot file was not found.", path);
        if (fileInfo.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"Legacy snapshot '{path}' is {fileInfo.Length} bytes; maximum supported size is {maximumBytes} bytes.");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"Legacy snapshot '{path}' exceeded the {maximumBytes}-byte limit while being read.");
        }

        return SHA256.HashData(bytes);
    }

    private static byte[] NormalizeSourceHash(string sourceHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHash);
        var normalized = sourceHash.Trim();
        if (normalized.Length != 64 || normalized.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Legacy import source hash must be a 64-character SHA-256 hexadecimal value.",
                nameof(sourceHash));
        }

        return Convert.FromHexString(normalized);
    }
}
