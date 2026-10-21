namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Resolves an identifier to a stable Security Master identity without exposing its implementation.
/// </summary>
public interface ISecurityResolver
{
    Task<Guid?> ResolveAsync(ResolveSecurityRequest request, CancellationToken ct = default);
}
