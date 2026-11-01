using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Application.ProviderRouting;

/// <summary>Retained ownership facts for one provider family, evaluated once from configuration.</summary>
public sealed record ProviderOwnershipEvaluation(int RuntimeBindingCount, bool OwnershipRequiredMarker, bool HasScopedOrAmbiguousConnections)
{
    /// <summary>
    /// True when the family can no longer use provider-wide legacy credentials: a host runtime
    /// binding exists, the ownership-required marker survives a deleted connection, or any family
    /// connection carries tenant/environment ownership, a blank identity, or a duplicated identity.
    /// </summary>
    public bool RequiresScopedOwnership => RuntimeBindingCount > 0 || OwnershipRequiredMarker || HasScopedOrAmbiguousConnections;
}

/// <summary>
/// The single definition of "entirely legacy family" shared by host composition, provider setup,
/// runtime credential resolution and lifecycle vault access. Every caller must agree, so none of
/// them re-implements the predicate.
/// </summary>
public static class ProviderOwnershipRules
{
    public static ProviderOwnershipEvaluation Evaluate(ProviderConnectionsConfig? section, string providerId)
    {
        var family = ProviderCredentialCatalog.NormalizeProviderId(providerId);
        var connections = section?.Connections ?? [];
        var duplicates = DuplicateConnectionIds(connections);
        var bindings = (section?.RuntimeCredentials ?? []).Count(binding => SameFamily(binding.ProviderId, family));
        var marker = (section?.OwnershipRequiredProviders ?? []).Any(candidate => SameFamily(candidate, family));
        var scoped = connections.Any(connection => SameFamily(connection.ProviderFamilyId, family)
            && IsScopedOrAmbiguous(connection, duplicates));
        return new(bindings, marker, scoped);
    }

    /// <summary>Any owned state across every family; a host with such state requires a credential vault.</summary>
    public static bool AnyScopedOwnership(ProviderConnectionsConfig? section)
    {
        var connections = section?.Connections ?? [];
        var duplicates = DuplicateConnectionIds(connections);
        return section?.RuntimeCredentials is { Length: > 0 } || section?.OwnershipRequiredProviders is { Length: > 0 }
            || connections.Any(connection => IsScopedOrAmbiguous(connection, duplicates));
    }

    private static bool IsScopedOrAmbiguous(ProviderConnectionConfig connection, HashSet<string> duplicates)
        // CredentialEnvironment is retained only by the owned-connection workflow. Losing the tenant
        // part of that ownership must not reactivate the legacy provider-wide path.
        => connection.TenantId is not null || connection.CredentialEnvironment is not null
            || string.IsNullOrWhiteSpace(connection.ConnectionId) || duplicates.Contains(connection.ConnectionId);

    private static HashSet<string> DuplicateConnectionIds(IReadOnlyList<ProviderConnectionConfig> connections)
        => connections.Where(static connection => !string.IsNullOrWhiteSpace(connection.ConnectionId))
            .GroupBy(static connection => connection.ConnectionId, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() != 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool SameFamily(string candidate, string family)
        => string.Equals(ProviderCredentialCatalog.NormalizeProviderId(candidate), family, StringComparison.OrdinalIgnoreCase);
}
