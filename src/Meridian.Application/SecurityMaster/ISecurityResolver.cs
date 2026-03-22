using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityResolver
{
    Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default);
}
