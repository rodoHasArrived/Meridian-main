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
