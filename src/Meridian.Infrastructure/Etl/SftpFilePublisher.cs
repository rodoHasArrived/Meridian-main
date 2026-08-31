using Meridian.Contracts.Etl;
using Meridian.Infrastructure.Etl.Sftp;

namespace Meridian.Infrastructure.Etl;

public sealed class SftpFilePublisher : ISftpFilePublisher
{
    private readonly ISftpClientFactory _clientFactory;
    private readonly ISftpCredentialResolver _credentialResolver;
    private readonly ISftpCapabilityService _capability;

    /// <remarks>
    /// All three collaborators are required. A convenience overload that defaulted the
    /// capability service would bind the build-time SFTP flag even when the caller supplied
    /// its own working client, and defaulting the credential resolver is how the destination
    /// secret model drifted from the source one in the first place.
    /// </remarks>
    public SftpFilePublisher(
        ISftpClientFactory clientFactory,
        ISftpCredentialResolver credentialResolver,
        ISftpCapabilityService capability)
    {
        _clientFactory = clientFactory;
        _credentialResolver = credentialResolver;
        _capability = capability;
    }

    public async Task PublishAsync(EtlDestinationDefinition destination, string localPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ct.ThrowIfCancellationRequested();

        // Fail closed with the readiness issues rather than letting the disabled-build stub
        // throw NotSupportedException from inside the client factory: the operator needs to
        // know SFTP is not in this build before an export job reports a transport failure.
        var status = _capability.Evaluate(destination);
        if (!status.Ready)
        {
            throw new InvalidOperationException(
                "SFTP publishing is not available for this destination: " + string.Join(" ", status.Issues));
        }

        var location = SftpRemoteLocation.ParseRequired(destination.Location, "destination");
        var credential = await _credentialResolver.ResolveAsync(destination, ct).ConfigureAwait(false);
        var client = _clientFactory.Create(SftpConnectionOptions.Create(
            location.Host,
            location.Port,
            credential.Username,
            credential.Password,
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
