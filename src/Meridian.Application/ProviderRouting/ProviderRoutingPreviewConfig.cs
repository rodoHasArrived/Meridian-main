using Meridian.Contracts.Api;
using Meridian.Core.Config;

namespace Meridian.Application.ProviderRouting;

/// <summary>Copies only bounded operational settings; never merges arbitrary JSON into host configuration.</summary>
public static class ProviderRoutingPreviewConfig
{
    public static AppConfig ApplyBindings(AppConfig config, string tenantId, ProviderRoutingProposedConfig proposed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (proposed.BackfillProviders is { Length: > 0 })
            throw new ArgumentException("Host backfill changes require a backfill preview.");
        var section = config.ProviderConnections ?? new ProviderConnectionsConfig();
        var connections = section.Connections ?? [];
        var bindings = (section.Bindings ?? []).ToArray();
        var changes = proposed.Bindings ?? [];
        if (changes.Length > 100 || changes.Any(c => c is null) ||
            changes.Select(c => c.BindingId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != changes.Length)
            throw new ArgumentException("Supply at most 100 distinct binding changes.");
        foreach (var change in changes)
        {
            if (change.Priority is < 0 or > 10000)
                throw new ArgumentException("Priority must be between 0 and 10000.");
            var matches = bindings.Select((binding, index) => (binding, index))
                .Where(row => string.Equals(row.binding.BindingId, change.BindingId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("The proposed binding is unavailable in this tenant scope.");
            var (binding, index) = matches[0];
            var owners = connections.Where(c => string.Equals(c.ConnectionId, binding.ConnectionId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (owners.Length != 1 || !string.Equals(owners[0].TenantId, tenantId, StringComparison.Ordinal))
                throw new ArgumentException("The proposed binding is unavailable in this tenant scope.");
            bindings[index] = binding with { Priority = change.Priority ?? binding.Priority, Enabled = change.Enabled ?? binding.Enabled };
        }
        return config with { ProviderConnections = section with { Bindings = bindings } };
    }

    public static AppConfig ApplyBackfill(AppConfig config, ProviderRoutingProposedConfig? proposed)
    {
        if (proposed?.Bindings is { Length: > 0 })
            throw new ArgumentException("Tenant bindings cannot be changed in a host backfill preview.");
        var backfill = config.Backfill ?? new BackfillConfig();
        var providers = backfill.Providers ?? new BackfillProvidersConfig();
        var changes = proposed?.BackfillProviders ?? [];
        if (changes.Length > 30 || changes.Any(c => c is null) ||
            changes.Select(c => c.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != changes.Length)
            throw new ArgumentException("Supply at most 30 distinct provider changes.");
        foreach (var change in changes)
        {
            var id = change.ProviderId?.Trim().ToLowerInvariant() ?? "";
            if (id == "openfigi")
                throw new ArgumentException("OpenFIGI resolves identifiers and cannot serve historical bars.");
            var current = BackfillProviderCatalog.Read(providers, id);
            providers = BackfillProviderCatalog.Apply(providers, id, current with
            {
                Priority = change.Priority ?? current.Priority,
                Enabled = change.Enabled ?? current.Enabled
            });
        }
        return config with { Backfill = backfill with { Providers = providers } };
    }
}
