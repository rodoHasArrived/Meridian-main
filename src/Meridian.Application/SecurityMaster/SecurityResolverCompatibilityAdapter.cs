namespace Meridian.Application.SecurityMaster;

/// <summary>Exposes a canonical resolver through the former Application contract.</summary>
#pragma warning disable CS0618 // This adapter intentionally implements the compatibility contract.
public sealed class SecurityResolverCompatibilityAdapter : ISecurityResolver
#pragma warning restore CS0618
{
    private readonly Meridian.Contracts.SecurityMaster.ISecurityResolver _resolver;

    public SecurityResolverCompatibilityAdapter(Meridian.Contracts.SecurityMaster.ISecurityResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public Task<Guid?> ResolveAsync(
        Meridian.Contracts.SecurityMaster.ResolveSecurityRequest request, CancellationToken ct = default)
        => _resolver.ResolveAsync(request, ct);
}
