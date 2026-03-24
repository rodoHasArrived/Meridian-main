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
        client.Disconnect();
        return Task.FromResult<IReadOnlyList<EtlRemoteFile>>(files);
    }

    public async Task<EtlStagedFile> StageFileAsync(string jobId, EtlSourceDefinition source, EtlRemoteFile file, CancellationToken ct = default)
    {
        var uri = ParseUri(source.Location);
        using var client = CreateClient(source, uri);
        client.Connect();
        await using var ms = new MemoryStream();
        client.DownloadFile(file.Path, ms);
        ms.Position = 0;
        var staged = await _stagingStore.StageAsync(jobId, file, ms, ct).ConfigureAwait(false);
        client.Disconnect();
        return staged;
    }

    private ISftpClient CreateClient(EtlSourceDefinition source, Uri uri)
    {
        var password = source.SecretRef ?? throw new InvalidOperationException("SFTP secretRef must contain the password in v1.");
        var username = source.Username ?? throw new InvalidOperationException("SFTP username is required.");
        return _clientFactory.Create(uri.Host, uri.Port > 0 ? uri.Port : 22, username, password);
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
