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
        var pattern = string.IsNullOrWhiteSpace(source.FilePattern) ? "*.csv" : source.FilePattern!;
        var files = Directory.EnumerateFiles(source.Location, pattern, SearchOption.TopDirectoryOnly)
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
}
