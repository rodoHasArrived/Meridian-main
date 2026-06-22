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
        var uri = ParseUri(source.Location);
        using var client = CreateClient(source, uri);
        client.Connect();
        try
        {
            var pattern = string.IsNullOrWhiteSpace(source.FilePattern) ? ".csv" : source.FilePattern!;
            var files = client.ListDirectory(uri.AbsolutePath)
                .Where(f => !f.IsDirectory && !f.IsSymbolicLink)
                .Where(f => Matches(f.Name, pattern))
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
        var uri = ParseUri(source.Location);
        using var client = CreateClient(source, uri);
        client.Connect();
        var tempPath = Path.Combine(Path.GetTempPath(), "meridian-sftp-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose))
            {
                client.DownloadFile(file.Path, temp);
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

    private ISftpClient CreateClient(EtlSourceDefinition source, Uri uri)
    {
        return _clientFactory.Create(SftpConnectionOptions.Create(
            uri.Host,
            uri.Port > 0 ? uri.Port : 22,
            source.Username ?? string.Empty,
            source.SecretRef ?? string.Empty,
            source.HostKeySha256Fingerprint));
    }

    private static Uri ParseUri(string location)
        => new(location.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase)
            ? location
            : throw new InvalidOperationException("SFTP source paths must be full sftp:// URIs in v1."));

    private static bool Matches(string fileName, string pattern)
        => pattern.StartsWith("*.", StringComparison.Ordinal)
            ? fileName.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)
            : fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
}
