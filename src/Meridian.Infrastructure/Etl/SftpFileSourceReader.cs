using Meridian.Contracts.Etl;
using Meridian.Infrastructure.Etl.Sftp;
using Meridian.Storage.Etl;

namespace Meridian.Infrastructure.Etl;

public sealed class SftpFileSourceReader : IEtlSourceReader
{
    private readonly EtlStagingStore _stagingStore;
    private readonly ISftpClientFactory _clientFactory;

    public SftpFileSourceReader(EtlStagingStore stagingStore, ISftpClientFactory clientFactory)
    {
        _stagingStore = stagingStore;
        _clientFactory = clientFactory;
    }

    public EtlSourceKind Kind => EtlSourceKind.Sftp;

    public Task<IReadOnlyList<EtlRemoteFile>> ListFilesAsync(EtlSourceDefinition source, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var location = SftpRemoteLocation.ParseRequired(source.Location, "source");
        using var client = CreateClient(source, location);
        ct.ThrowIfCancellationRequested();
        client.Connect();
        try
        {
            ct.ThrowIfCancellationRequested();
            var pattern = string.IsNullOrWhiteSpace(source.FilePattern) ? "*.csv;*.xlsx" : source.FilePattern!;
            var files = client.ListDirectory(location.RemotePath)
                .Where(f => !f.IsDirectory && !f.IsSymbolicLink)
                .Where(f => MatchesAny(f.Name, pattern))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => new EtlRemoteFile
                {
                    Path = f.FullName,
                    Name = f.Name,
                    SizeBytes = f.Length,
                    LastModifiedUtc = f.LastWriteTimeUtc
                })
                .ToList();
            return Task.FromResult<IReadOnlyList<EtlRemoteFile>>(files);
        }
        finally
        {
            client.Disconnect();
        }
    }

    public async Task<EtlStagedFile> StageFileAsync(string jobId, EtlSourceDefinition source, EtlRemoteFile file, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var location = SftpRemoteLocation.ParseRequired(source.Location, "source");
        if (!location.ContainsFile(file.Path))
            throw new InvalidOperationException("SFTP remote file must be under the configured source path.");

        using var client = CreateClient(source, location);
        ct.ThrowIfCancellationRequested();
        client.Connect();
        var tempPath = Path.Combine(Path.GetTempPath(), "meridian-sftp-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose))
            {
                ct.ThrowIfCancellationRequested();
                client.DownloadFile(file.Path, temp);
                ct.ThrowIfCancellationRequested();
                temp.Position = 0;
                return await _stagingStore.StageAsync(jobId, file, temp, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            client.Disconnect();
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private ISftpClient CreateClient(EtlSourceDefinition source, SftpRemoteLocation location)
    {
        return _clientFactory.Create(SftpConnectionOptions.Create(
            location.Host,
            location.Port,
            source.Username ?? string.Empty,
            source.SecretRef ?? string.Empty,
            source.HostKeySha256Fingerprint));
    }

    private static bool MatchesAny(string fileName, string pattern)
        => pattern
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => Matches(fileName, part));

    private static bool Matches(string fileName, string pattern)
        => pattern.StartsWith("*.", StringComparison.Ordinal)
            ? fileName.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)
            : fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
}
