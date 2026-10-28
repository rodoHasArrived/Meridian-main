namespace Meridian.Contracts.Etl;

/// <summary>
/// Publishes a local file to the configured SFTP destination.
/// </summary>
public interface ISftpFilePublisher
{
    Task PublishAsync(EtlDestinationDefinition destination, string localPath, CancellationToken ct = default);
}
