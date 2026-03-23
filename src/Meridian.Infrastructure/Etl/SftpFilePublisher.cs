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
        var uri = new Uri(destination.Location!.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase)
            ? destination.Location
            : $"sftp://{destination.Location!.TrimStart('/')}");
        var client = _clientFactory.Create(uri.Host, uri.Port > 0 ? uri.Port : 22,
            destination.Username ?? throw new InvalidOperationException("SFTP username is required."),
            destination.SecretRef ?? throw new InvalidOperationException("SFTP secretRef must contain the password in v1."));
        using (client)
        {
            client.Connect();
            if (Directory.Exists(localPath))
            {
                foreach (var file in Directory.EnumerateFiles(localPath, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(localPath, file).Replace('\\', '/');
                    var remotePath = CombineRemote(uri.AbsolutePath, relative);
                    EnsureRemoteDirectory(client, Path.GetDirectoryName(remotePath)!.Replace('\\', '/'));
                    using var fs = File.OpenRead(file);
                    client.UploadFile(fs, remotePath, true);
                }
            }
            else
            {
                EnsureRemoteDirectory(client, uri.AbsolutePath);
                using var fs = File.OpenRead(localPath);
                client.UploadFile(fs, CombineRemote(uri.AbsolutePath, Path.GetFileName(localPath)), destination.OverwriteIfExists);
            }
            client.Disconnect();
        }

        return Task.CompletedTask;
    }

    private static void EnsureRemoteDirectory(Renci.SshNet.SftpClient client, string remoteDirectory)
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

    private static string CombineRemote(string left, string right)
        => (left.TrimEnd('/') + "/" + right.TrimStart('/')).Replace("//", "/");
}
