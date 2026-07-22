using Meridian.Contracts.Etl;
using Meridian.Infrastructure.Etl.Sftp;

namespace Meridian.Infrastructure.Etl;

public sealed class SftpFilePublisher : ISftpFilePublisher
{
    private readonly ISftpClientFactory _clientFactory;

    public SftpFilePublisher(ISftpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public Task PublishAsync(EtlDestinationDefinition destination, string localPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var location = SftpRemoteLocation.ParseRequired(destination.Location, "destination");
        var client = _clientFactory.Create(SftpConnectionOptions.Create(
            location.Host,
            location.Port,
            destination.Username ?? string.Empty,
            destination.SecretRef ?? string.Empty,
            destination.HostKeySha256Fingerprint));
        using (client)
        {
            ct.ThrowIfCancellationRequested();
            client.Connect();
            try
            {
                if (Directory.Exists(localPath))
                {
                    foreach (var file in Directory.EnumerateFiles(localPath, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(localPath, file).Replace('\\', '/');
                        var remotePath = SftpRemoteLocation.Combine(location.RemotePath, relative);
                        EnsureRemoteDirectory(client, Path.GetDirectoryName(remotePath)!.Replace('\\', '/'));
                        UploadAtomic(client, file, remotePath, canOverwrite: true, ct);
                    }
                }
                else
                {
                    ct.ThrowIfCancellationRequested();
                    EnsureRemoteDirectory(client, location.RemotePath);
                    var remotePath = SftpRemoteLocation.Combine(location.RemotePath, Path.GetFileName(localPath));
                    UploadAtomic(client, localPath, remotePath, destination.OverwriteIfExists, ct);
                }
            }
            finally
            {
                client.Disconnect();
            }
        }

        return Task.CompletedTask;
    }

    private static void UploadAtomic(ISftpClient client, string localPath, string remotePath, bool canOverwrite, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var tempPath = remotePath + ".meridian-upload-" + Guid.NewGuid().ToString("N") + ".tmp";
        var uploaded = false;
        try
        {
            using var fs = File.OpenRead(localPath);
            client.UploadFile(fs, tempPath, canOverwrite: true);
            uploaded = true;
            ct.ThrowIfCancellationRequested();
            client.RenameFile(tempPath, remotePath, canOverwrite);
        }
        finally
        {
            if (uploaded && client.Exists(tempPath))
                client.DeleteFile(tempPath);
        }
    }

    private static void EnsureRemoteDirectory(ISftpClient client, string remoteDirectory)
    {
        if (string.IsNullOrWhiteSpace(remoteDirectory) || remoteDirectory == "/")
            return;

        var segments = remoteDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        foreach (var segment in segments)
        {
            current += "/" + segment;
            if (!client.Exists(current))
                client.CreateDirectory(current);
        }
    }

}
