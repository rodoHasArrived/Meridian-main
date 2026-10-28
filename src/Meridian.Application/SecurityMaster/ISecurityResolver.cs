using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Resolves an external security identity request to a canonical security identifier.
/// </summary>
public interface ISecurityResolver
{
    Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default);
}
