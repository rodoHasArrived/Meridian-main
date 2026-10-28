using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>Compatibility contract for hosts using the former Application-owned resolver.</summary>
[Obsolete("Use Meridian.Contracts.SecurityMaster.ISecurityResolver.")]
public interface ISecurityResolver : Meridian.Contracts.SecurityMaster.ISecurityResolver
{
    // Retain the declared member as well as the type identity for existing compiled implementations.
    new Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default);

    Task<Guid?> Meridian.Contracts.SecurityMaster.ISecurityResolver.ResolveAsync(
        ResolveSecurityRequest request, CancellationToken ct) => ResolveAsync(request, ct);
}
