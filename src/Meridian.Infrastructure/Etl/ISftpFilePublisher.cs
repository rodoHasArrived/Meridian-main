using Meridian.Contracts.Etl;

namespace Meridian.Infrastructure.Etl;

public interface ISftpFilePublisher
{
    Task PublishAsync(EtlDestinationDefinition destination, string localPath, CancellationToken ct = default);
}
