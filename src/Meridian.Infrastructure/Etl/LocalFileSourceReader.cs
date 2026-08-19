using Meridian.Contracts.Etl;
using Meridian.Storage.Etl;

namespace Meridian.Infrastructure.Etl;

public sealed class LocalFileSourceReader : IEtlSourceReader
{
    private readonly EtlStagingStore _stagingStore;

    public LocalFileSourceReader(EtlStagingStore stagingStore)
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
        return await _stagingStore.StageAsync(jobId, file, stream, ct).ConfigureAwait(false);
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

        switch (action)
        {
            case EtlSourcePostProcessingAction.Delete when File.Exists(file.Path):
                File.Delete(file.Path);
                break;
            case EtlSourcePostProcessingAction.MoveToArchive when !string.IsNullOrWhiteSpace(source.ArchiveLocation):
                await MoveLocalFileAsync(file.Path, source.ArchiveLocation, ct).ConfigureAwait(false);
                break;
            case EtlSourcePostProcessingAction.MoveToError when !string.IsNullOrWhiteSpace(source.ErrorLocation):
                await MoveLocalFileAsync(file.Path, source.ErrorLocation, ct).ConfigureAwait(false);
                break;
            case EtlSourcePostProcessingAction.WriteDoneMarker:
                await File.WriteAllTextAsync(file.Path + ".done", DateTimeOffset.UtcNow.ToString("O"), ct)
                    .ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Moves a processed source into its retention directory without ever destroying what is
    /// already there. A free name is used as-is; a name held by identical content means the move
    /// already ran, so the source is simply dropped; a name held by different content resolves to
    /// a deterministic content-addressed sibling so both sources survive.
    /// </summary>
    private static async Task MoveLocalFileAsync(string path, string directory, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Path.GetFileName(path));
        if (!File.Exists(destination))
        {
            File.Move(path, destination);
            return;
        }

        var sourceHash = await EtlArchiveNaming.ComputeFileHashAsync(path, ct).ConfigureAwait(false);
        var retainedHash = await EtlArchiveNaming.ComputeFileHashAsync(destination, ct).ConfigureAwait(false);
        if (string.Equals(sourceHash, retainedHash, StringComparison.Ordinal))
        {
            // Already retained by an earlier attempt; completing the move is the idempotent result.
            File.Delete(path);
            return;
        }

        var disambiguated = Path.Combine(
            directory,
            EtlArchiveNaming.BuildCollisionSafeName(Path.GetFileName(path), sourceHash));
        if (File.Exists(disambiguated))
        {
            // The name is derived from this content, so an occupant should carry it. Verify rather
            // than assume: the name uses a hash prefix, and overwriting on an unverified match is
            // the very loss this path exists to prevent.
            var disambiguatedHash = await EtlArchiveNaming.ComputeFileHashAsync(disambiguated, ct).ConfigureAwait(false);
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

    private static IEnumerable<string> ExpandPatterns(string pattern)
        => pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
