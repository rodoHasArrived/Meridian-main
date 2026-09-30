namespace Meridian.ProviderSdk;

/// <summary>
/// Top-level runtime abstraction representing a provider family that can expose many capabilities.
/// </summary>
public interface IProviderFamilyAdapter
{
    string ProviderFamilyId { get; }

    string DisplayName { get; }

    string Description { get; }

    IReadOnlyList<ProviderCapabilityDescriptor> CapabilityDescriptors { get; }

    bool SupportsCapability(ProviderCapabilityKind capability);

    Task InitializeConnectionAsync(string connectionId, ProviderConnectionScope scope, CancellationToken ct = default);

    Task<ProviderConnectionTestResult> TestConnectionAsync(string connectionId, CancellationToken ct = default);

    ValueTask<object?> ResolveCapabilityAsync(ProviderCapabilityKind capability, CancellationToken ct = default);
}

/// <summary>
/// Canonical capability router.
/// </summary>
public interface ICapabilityRouter
{
    ValueTask<ProviderRouteResult> RouteAsync(ProviderRouteContext context, CancellationToken ct = default);

    /// <summary>
    /// Routes using only connections retained for the authorized tenant. Tenant-bound consumers must call
    /// this instead of <see cref="RouteAsync"/>. The default refuses rather than falling back to unscoped
    /// routing, so a router that does not implement tenant scoping cannot disclose another tenant's connections.
    /// </summary>
    ValueTask<ProviderRouteResult> RouteForTenantAsync(ProviderRouteContext context, string tenantId, CancellationToken ct = default)
        => throw new NotSupportedException("This capability router does not support tenant-scoped routing.");
}

/// <summary>
/// Source of per-connection health snapshots.
/// </summary>
public interface IProviderConnectionHealthSource
{
    ValueTask<ProviderConnectionHealthSnapshot> GetHealthAsync(
        string connectionId,
        string providerFamilyId,
        CancellationToken ct = default);

    /// <summary>
    /// Health reported for exactly this connection, never inferred from telemetry shared by other
    /// owners of the same provider family. Tenant-scoped routing and trust reads use this so another
    /// tenant's family-wide metric cannot rank this tenant's connection. The default delegates to
    /// <see cref="GetHealthAsync"/>, which is correct for sources keyed by connection; a source that
    /// falls back to provider-family telemetry must override it.
    /// </summary>
    ValueTask<ProviderConnectionHealthSnapshot> GetConnectionHealthAsync(
        string connectionId,
        string providerFamilyId,
        CancellationToken ct = default)
        => GetHealthAsync(connectionId, providerFamilyId, ct);
}

/// <summary>
/// Executes a certification run for a provider connection.
/// </summary>
public interface IProviderCertificationRunner
{
    Task<ProviderCertificationRunResult> RunAsync(
        string connectionId,
        IProviderFamilyAdapter adapter,
        CancellationToken ct = default);
}

/// <summary>
/// Convenience helpers for typed capability resolution.
/// </summary>
public static class ProviderFamilyAdapterExtensions
{
    public static async ValueTask<TCapability?> ResolveCapabilityAsync<TCapability>(
        this IProviderFamilyAdapter adapter,
        ProviderCapabilityKind capability,
        CancellationToken ct = default)
        where TCapability : class
    {
        var resolved = await adapter.ResolveCapabilityAsync(capability, ct).ConfigureAwait(false);
        return resolved as TCapability;
    }
}
