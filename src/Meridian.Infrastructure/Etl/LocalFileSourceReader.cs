using System.Runtime.CompilerServices;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Integrity;

namespace Meridian.Infrastructure.Etl;

public sealed class LocalFileSourceReader : IEtlSourceReader
{
    private readonly IEtlStagingStore _stagingStore;
    private readonly ConditionalWeakTable<EtlRemoteFile, StagedIdentity> _stagedIdentities = new();

    public LocalFileSourceReader(IEtlStagingStore stagingStore)
    {
        _stagingStore = stagingStore;
    }

    public EtlSourceKind Kind => EtlSourceKind.Local;

    public Task<IReadOnlyList<EtlRemoteFile>> ListFilesAsync(EtlSourceDefinition source, CancellationToken ct = default)
    {
        var pattern = string.IsNullOrWhiteSpace(source.FilePattern) ? "*.csv;*.xlsx" : source.FilePattern!;
        var files = ExpandPatterns(pattern)
            .SelectMany(p => Directory.EnumerateFiles(source.Location, p, SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new EtlRemoteFile
                {
                    Path = path,
                    Name = info.Name,
                    SizeBytes = info.Length,
                    LastModifiedUtc = info.LastWriteTimeUtc
                };
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<EtlRemoteFile>>(files);
    }

    public async Task<EtlStagedFile> StageFileAsync(string jobId, EtlSourceDefinition source, EtlRemoteFile file, CancellationToken ct = default)
    {
        await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var lastWriteTime = File.GetLastWriteTimeUtc(stream.SafeFileHandle);
        var creationTime = File.GetCreationTimeUtc(stream.SafeFileHandle);
        var staged = await _stagingStore.StageAsync(jobId, file, stream, ct).ConfigureAwait(false);
        _stagedIdentities.AddOrUpdate(file, new StagedIdentity(staged.ChecksumSha256, staged.SizeBytes, lastWriteTime, creationTime));
        return staged;
    }

    public async Task PostProcessFileAsync(EtlSourceDefinition source, EtlRemoteFile file, bool succeeded, CancellationToken ct = default)
    {
        var action = source.DeleteAfterSuccess ? EtlSourcePostProcessingAction.Delete : source.PostProcessingAction;
        if (action == EtlSourcePostProcessingAction.LeaveInPlace ||
            (succeeded && action == EtlSourcePostProcessingAction.MoveToError) ||
            (!succeeded && action != EtlSourcePostProcessingAction.MoveToError))
        {
            return;
        }

        if (!_stagedIdentities.TryGetValue(file, out var identity))
            throw new InvalidOperationException("Local ETL post-processing requires the identity captured while staging this file.");

        await identity.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(file.Path))
                return; // The same staged input may already have been retained by an earlier attempt.

            await VerifyStagedIdentityAsync(file.Path, identity, ct).ConfigureAwait(false);
            if (action == EtlSourcePostProcessingAction.WriteDoneMarker)
            {
                await File.WriteAllTextAsync(file.Path + ".done", DateTimeOffset.UtcNow.ToString("O"), ct).ConfigureAwait(false);
                return;
            }

            // Claim the directory entry atomically, then verify the claimed bytes. A producer can
            // replace positions.csv after the first check, but cleanup never acts on that new entry.
            var claimedPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file.Path))!,
                ".etl-processing-" + Guid.NewGuid().ToString("N"));
            File.Move(file.Path, claimedPath);
            try
            {
                await VerifyStagedIdentityAsync(claimedPath, identity, ct).ConfigureAwait(false);
                switch (action)
                {
                    case EtlSourcePostProcessingAction.Delete:
                        File.Delete(claimedPath);
                        break;
                    case EtlSourcePostProcessingAction.MoveToArchive when !string.IsNullOrWhiteSpace(source.ArchiveLocation):
                        await MoveLocalFileAsync(claimedPath, source.ArchiveLocation, Path.GetFileName(file.Path), ct).ConfigureAwait(false);
                        break;
                    case EtlSourcePostProcessingAction.MoveToError when !string.IsNullOrWhiteSpace(source.ErrorLocation):
                        await MoveLocalFileAsync(claimedPath, source.ErrorLocation, Path.GetFileName(file.Path), ct).ConfigureAwait(false);
                        break;
                }
            }
            finally
            {
                // A mismatch, cancellation or retention error must preserve the claimed input.
                // Never overwrite another drop that arrived while this entry was claimed.
                if (File.Exists(claimedPath))
                {
                    if (!File.Exists(file.Path))
                        File.Move(claimedPath, file.Path);
                    else
                        throw new IOException($"ETL source changed during post-processing; preserved the claimed file at '{claimedPath}' and the new drop at '{file.Path}'.");
                }
            }
        }
        finally
        {
            identity.Gate.Release();
        }
    }

    private static async Task VerifyStagedIdentityAsync(string path, StagedIdentity identity, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if (stream.Length != identity.SizeBytes ||
            File.GetLastWriteTimeUtc(stream.SafeFileHandle) != identity.LastWriteTimeUtc ||
            File.GetCreationTimeUtc(stream.SafeFileHandle) != identity.CreationTimeUtc ||
            !string.Equals(await Sha256Digest.ComputeAsync(stream, ct).ConfigureAwait(false), identity.ChecksumSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Local ETL source changed since staging; post-processing left the unprocessed replacement in place.");
        }
    }

    private sealed record StagedIdentity(string ChecksumSha256, long SizeBytes, DateTime LastWriteTimeUtc, DateTime CreationTimeUtc)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    /// <summary>
    /// Moves a processed source into its retention directory without ever destroying what is
    /// already there. A free name is used as-is; a name held by identical content means the move
    /// already ran, so the source is simply dropped; a name held by different content resolves to
    /// a deterministic content-addressed sibling so both sources survive.
    /// </summary>
    private static async Task MoveLocalFileAsync(string path, string directory, string fileName, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, fileName);
        if (!File.Exists(destination))
        {
            File.Move(path, destination);
            return;
        }

        var sourceHash = await ComputeFileHashAsync(path, ct).ConfigureAwait(false);
        var retainedHash = await ComputeFileHashAsync(destination, ct).ConfigureAwait(false);
        if (string.Equals(sourceHash, retainedHash, StringComparison.Ordinal))
        {
            // Already retained by an earlier attempt; completing the move is the idempotent result.
            File.Delete(path);
            return;
        }

        var disambiguated = Path.Combine(
            directory,
            EtlArchiveNaming.BuildCollisionSafeName(fileName, sourceHash));
        if (File.Exists(disambiguated))
        {
            // The name is derived from this content, so an occupant should carry it. Verify rather
            // than assume: the name uses a hash prefix, and overwriting on an unverified match is
            // the very loss this path exists to prevent.
            var disambiguatedHash = await ComputeFileHashAsync(disambiguated, ct).ConfigureAwait(false);
            if (!string.Equals(sourceHash, disambiguatedHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ETL retention path '{disambiguated}' already holds different content than source '{path}'. " +
                    "The source was left in place; resolve the retained file before retrying.");
            }

            File.Delete(path);
            return;
        }

        File.Move(path, disambiguated);
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return await Sha256Digest.ComputeAsync(stream, ct).ConfigureAwait(false);
    }

    private static IEnumerable<string> ExpandPatterns(string pattern)
        => pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
