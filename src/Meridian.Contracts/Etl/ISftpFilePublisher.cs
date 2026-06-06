namespace Meridian.Contracts.Etl;

public interface ISftpFilePublisher
{
    Task PublishAsync(EtlDestinationDefinition destination, string localPath, CancellationToken ct = default);
}
