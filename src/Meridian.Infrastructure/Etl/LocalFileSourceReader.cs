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

    public Task PostProcessFileAsync(EtlSourceDefinition source, EtlRemoteFile file, bool succeeded, CancellationToken ct = default)
    {
        if (!succeeded)
        {
            return Task.CompletedTask;
        }

        var action = source.DeleteAfterSuccess ? EtlSourcePostProcessingAction.Delete : source.PostProcessingAction;
        switch (action)
        {
            case EtlSourcePostProcessingAction.Delete when File.Exists(file.Path):
                File.Delete(file.Path);
                break;
            case EtlSourcePostProcessingAction.MoveToArchive when !string.IsNullOrWhiteSpace(source.ArchiveLocation):
                MoveLocalFile(file.Path, source.ArchiveLocation);
                break;
            case EtlSourcePostProcessingAction.WriteDoneMarker:
                File.WriteAllText(file.Path + ".done", DateTimeOffset.UtcNow.ToString("O"));
                break;
        }

        return Task.CompletedTask;
    }

    private static void MoveLocalFile(string path, string directory)
    {
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Path.GetFileName(path));
        if (File.Exists(destination))
            File.Delete(destination);
        File.Move(path, destination);
    }

    private static IEnumerable<string> ExpandPatterns(string pattern)
        => pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
