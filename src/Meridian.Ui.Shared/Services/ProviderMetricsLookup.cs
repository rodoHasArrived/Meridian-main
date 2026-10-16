using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.DataIntegration.Monitoring;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Ui.Shared.Services;

/// <summary>Joins connection telemetry to provider families without treating source IDs as families.</summary>
internal static class ProviderMetricsLookup
{
    public static ProviderMetrics? Find(
        ProviderMetricsStatus? metrics,
        IReadOnlyList<DataSourceConfig> sources,
        string providerId)
    {
        var familyId = ProviderCredentialCatalog.NormalizeProviderId(providerId);
        return metrics?.Providers.FirstOrDefault(provider =>
            string.Equals(ResolveFamily(provider, sources), familyId, StringComparison.Ordinal));
    }

    private static string ResolveFamily(ProviderMetrics metrics, IReadOnlyList<DataSourceConfig> sources)
    {
        var providerType = ProviderCredentialCatalog.NormalizeProviderId(metrics.ProviderType);
        if (ProviderIdentity.CanonicalFamilyIds.Contains(providerType) || ProviderCredentialCatalog.Find(providerType) is not null)
            return providerType;

        // ProviderId is an instance ID when it matches configured source ownership.
        var source = sources.FirstOrDefault(source =>
            string.Equals(source.Id, metrics.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (source is not null)
            return ProviderCredentialCatalog.NormalizeProviderId(source.Provider.ToString());

        // Legacy records use kinds such as "Streaming" in ProviderType rather than a
        // provider family. Their family-valued ProviderId remains the compatibility path.
        return ProviderCredentialCatalog.NormalizeProviderId(metrics.ProviderId);
    }
}
