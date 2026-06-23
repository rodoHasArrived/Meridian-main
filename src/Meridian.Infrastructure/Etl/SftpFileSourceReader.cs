using Meridian.Contracts.Etl;
using Meridian.Infrastructure.Etl.Sftp;
using Meridian.Storage.Etl;

namespace Meridian.Infrastructure.Etl;

public sealed class SftpFileSourceReader : IEtlSourceReader
{
    private readonly EtlStagingStore _stagingStore;
    private readonly ISftpClientFactory _clientFactory;
    private readonly ISftpCredentialResolver _credentialResolver;

    public SftpFileSourceReader(EtlStagingStore stagingStore, ISftpClientFactory clientFactory, ISftpCredentialResolver credentialResolver)
    {
        _stagingStore = stagingStore;
        _clientFactory = clientFactory;
        _credentialResolver = credentialResolver;
    }

    public EtlSourceKind Kind => EtlSourceKind.Sftp;

    public async Task<IReadOnlyList<EtlRemoteFile>> ListFilesAsync(EtlSourceDefinition source, CancellationToken ct = default)
    {
        var uri = ParseUri(source.Location);
        var credential = await _credentialResolver.ResolveAsync(source, ct).ConfigureAwait(false);
        using var client = CreateClient(source, uri, credential);
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
            return files;
        }
        finally
        {
            client.Disconnect();
        }
    }

    public async Task<EtlStagedFile> StageFileAsync(string jobId, EtlSourceDefinition source, EtlRemoteFile file, CancellationToken ct = default)
    {
        var uri = ParseUri(source.Location);
        var credential = await _credentialResolver.ResolveAsync(source, ct).ConfigureAwait(false);
        using var client = CreateClient(source, uri, credential);
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


    public async Task PostProcessFileAsync(EtlSourceDefinition source, EtlRemoteFile file, bool succeeded, CancellationToken ct = default)
    {
        if (!succeeded || source.PostProcessingAction == EtlSourcePostProcessingAction.LeaveInPlace)
        {
            return;
        }

        var uri = ParseUri(source.Location);
        var credential = await _credentialResolver.ResolveAsync(source, ct).ConfigureAwait(false);
        using var client = CreateClient(source, uri, credential);
        client.Connect();
        try
        {
            switch (source.PostProcessingAction)
            {
                case EtlSourcePostProcessingAction.Delete:
                    client.DeleteFile(file.Path);
                    break;
                case EtlSourcePostProcessingAction.MoveToArchive:
                    MoveRemoteFile(client, file, source.ArchiveLocation);
                    break;
                case EtlSourcePostProcessingAction.MoveToError:
                    MoveRemoteFile(client, file, source.ErrorLocation);
                    break;
                case EtlSourcePostProcessingAction.WriteDoneMarker:
                    using (var marker = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"))))
                    {
                        client.UploadFile(marker, file.Path + ".done", canOverwrite: true);
                    }
                    break;
            }
        }
        finally
        {
            client.Disconnect();
        }
    }

    private static void MoveRemoteFile(ISftpClient client, EtlRemoteFile file, string? remoteDirectory)
    {
        if (string.IsNullOrWhiteSpace(remoteDirectory))
            throw new InvalidOperationException("Remote post-processing move requires an archive or error location.");

        var normalizedDirectory = remoteDirectory.TrimEnd('/');
        if (!client.Exists(normalizedDirectory))
            client.CreateDirectory(normalizedDirectory);

        client.RenameFile(file.Path, normalizedDirectory + "/" + file.Name);
    }

    private ISftpClient CreateClient(EtlSourceDefinition source, Uri uri, SftpCredentialMaterial credential)
    {
        return _clientFactory.Create(SftpConnectionOptions.Create(
            uri.Host,
            uri.Port > 0 ? uri.Port : 22,
            credential.Username,
            credential.Password,
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
