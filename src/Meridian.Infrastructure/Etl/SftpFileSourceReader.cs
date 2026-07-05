using Meridian.Contracts.Etl;
using Meridian.Infrastructure.Etl.Sftp;
using Meridian.Storage.Etl;

namespace Meridian.Infrastructure.Etl;

public sealed class SftpFileSourceReader : IEtlSourceReader
{
    private readonly EtlStagingStore _stagingStore;
    private readonly ISftpClientFactory _clientFactory;
    private readonly ISftpCredentialResolver _credentialResolver;

    public SftpFileSourceReader(EtlStagingStore stagingStore, ISftpClientFactory clientFactory)
        : this(stagingStore, clientFactory, new EnvironmentSftpCredentialResolver())
    {
    }

    public SftpFileSourceReader(EtlStagingStore stagingStore, ISftpClientFactory clientFactory, ISftpCredentialResolver credentialResolver)
    {
        _stagingStore = stagingStore;
        _clientFactory = clientFactory;
        _credentialResolver = credentialResolver;
    }

    public EtlSourceKind Kind => EtlSourceKind.Sftp;

    public async Task<IReadOnlyList<EtlRemoteFile>> ListFilesAsync(EtlSourceDefinition source, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var location = SftpRemoteLocation.ParseRequired(source.Location, "source");
        var credential = await _credentialResolver.ResolveAsync(source, ct).ConfigureAwait(false);
        using var client = CreateClient(source, location, credential);
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
            return files;
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

        var credential = await _credentialResolver.ResolveAsync(source, ct).ConfigureAwait(false);
        using var client = CreateClient(source, location, credential);
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

    public async Task PostProcessFileAsync(EtlSourceDefinition source, EtlRemoteFile file, bool succeeded, CancellationToken ct = default)
    {
        if (source.PostProcessingAction == EtlSourcePostProcessingAction.LeaveInPlace ||
            (succeeded && source.PostProcessingAction == EtlSourcePostProcessingAction.MoveToError) ||
            (!succeeded && source.PostProcessingAction != EtlSourcePostProcessingAction.MoveToError))
        {
            return;
        }

        var location = SftpRemoteLocation.ParseRequired(source.Location, "source");
        if (!location.ContainsFile(file.Path))
            throw new InvalidOperationException("SFTP remote file must be under the configured source path.");

        var credential = await _credentialResolver.ResolveAsync(source, ct).ConfigureAwait(false);
        using var client = CreateClient(source, location, credential);
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
                        client.UploadFile(marker, file.Path + ".done", canOverwrite: true);
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

        var normalizedDirectory = SftpRemoteLocation.NormalizePath(remoteDirectory.TrimEnd('/'));
        EnsureRemoteDirectory(client, normalizedDirectory);

        client.RenameFile(file.Path, SftpRemoteLocation.Combine(normalizedDirectory, file.Name), canOverwrite: true);
    }

    private static void EnsureRemoteDirectory(ISftpClient client, string normalizedDirectory)
    {
        var segments = normalizedDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = normalizedDirectory.StartsWith('/', StringComparison.Ordinal) ? "/" : string.Empty;
        foreach (var segment in segments)
        {
            current = current == "/"
                ? "/" + segment
                : string.IsNullOrEmpty(current)
                    ? segment
                    : current + "/" + segment;
            if (!client.Exists(current))
                client.CreateDirectory(current);
        }
    }

    private ISftpClient CreateClient(EtlSourceDefinition source, SftpRemoteLocation location, SftpCredentialMaterial credential)
    {
        return _clientFactory.Create(SftpConnectionOptions.Create(
            location.Host,
            location.Port,
            credential.Username,
            credential.Password,
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
