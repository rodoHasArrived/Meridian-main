using Meridian.Contracts.Api;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;

namespace Meridian.Infrastructure.Adapters.Core;

public static partial class ProviderServiceExtensions
{
    private static IReadOnlyList<ProviderCatalogEntry> BuildMergedProviderCatalog(
        ProviderRegistry registry,
        IEnumerable<IOptionsChainProvider> optionProviders,
        IEnumerable<DataSourceMetadata>? discoveredSources = null)
    {
        var merged = new Dictionary<string, ProviderCatalogEntry>(StringComparer.Ordinal);

        void Merge(ProviderCatalogEntry entry)
        {
            var id = ProviderIdentity.NormalizeId(entry.ProviderId);
            merged[id] = MergeCatalogEntries(merged.GetValueOrDefault(id) ?? entry, entry);
        }

        foreach (var entry in ProviderCatalog.GetStaticEntries())
            Merge(entry);

        foreach (var entry in registry.GetProviderCatalog())
            Merge(entry);

        foreach (var provider in optionProviders)
            Merge(ProviderTemplateFactory.ToCatalogEntry(provider));

        void MergeInventory(ProviderCapabilityDescriptor descriptor, DataSourceMetadata? discoveredMetadata = null)
        {
            var id = ProviderIdentity.NormalizeId(descriptor.ProviderId);
            if (!merged.TryGetValue(id, out var entry))
            {
                var metadata = discoveredMetadata ?? descriptor.Implementations()
                    .Select(static implementation => implementation.GetDataSourceMetadata())
                    .FirstOrDefault(static metadata => metadata is not null);
                var credentials = descriptor.Implementations()
                    .SelectMany(AttributeCredentialResolver.GetAttributes)
                    .GroupBy(static attribute => attribute.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(static group => group.First())
                    .Select(static attribute => new CredentialFieldInfo(
                        attribute.Name,
                        attribute.EnvironmentVariables.FirstOrDefault(),
                        attribute.DisplayName ?? attribute.Name,
                        !attribute.Optional,
                        EnvironmentVariableAliases: attribute.EnvironmentVariables.Skip(1).ToArray()))
                    .ToArray();
                entry = new ProviderCatalogEntry
                {
                    ProviderId = id,
                    DisplayName = metadata?.DisplayName ?? id,
                    Description = metadata?.Description ?? string.Empty,
                    CredentialFields = credentials,
                    RequiresCredentials = credentials.Any(static field => field.Required)
                };
            }

            merged[id] = MergeCatalogEntries(entry, entry, descriptor);
        }

        // This is declared factory inventory, not a runtime availability or entitlement result.
        // Keep disabled/unconfigured families visible without constructing their adapters.
        var builtInFamilies = ProviderCapabilityDescriptorCatalog.Descriptors
            .Select(static descriptor => ProviderIdentity.NormalizeId(descriptor.ProviderId))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
            MergeInventory(descriptor);

        // External modules own their configured factories, including contracts that do not
        // implement IProviderMetadata. Reflect their declared slots without constructing an
        // adapter or treating discovery as evidence that a module is enabled or connected.
        foreach (var family in (discoveredSources ?? [])
                     .GroupBy(static source => ProviderIdentity.NormalizeId(source.Id), StringComparer.Ordinal)
                     .Where(group => !builtInFamilies.Contains(group.Key)))
        {
            var sources = family.OrderBy(static source => source.ImplementationType.FullName, StringComparer.Ordinal).ToArray();
            Type? Implementation(string contract) => sources
                .FirstOrDefault(source => source.CapabilityKeys.Contains(contract))?.ImplementationType;
            var descriptor = new ProviderCapabilityDescriptor(
                family.Key,
                Streaming: Implementation(DataSourceCapabilityContracts.MarketDataClient),
                Historical: Implementation(DataSourceCapabilityContracts.HistoricalDataProvider),
                Search: Implementation(DataSourceCapabilityContracts.SymbolSearchProvider),
                CorporateActions: Implementation(DataSourceCapabilityContracts.CorporateActionProvider),
                Options: Implementation(DataSourceCapabilityContracts.OptionsChainProvider),
                Brokerage: Implementation(DataSourceCapabilityContracts.BrokerageGateway),
                FactoryOwner: ProviderCapabilityFactoryOwner.Module);
            MergeInventory(descriptor, sources[0]);
        }

        return merged.Values
            .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ProviderCatalogEntry? GetMergedProviderCatalogEntry(
        ProviderRegistry registry,
        IEnumerable<IOptionsChainProvider> optionProviders,
        string providerId,
        IEnumerable<DataSourceMetadata>? discoveredSources = null)
    {
        return BuildMergedProviderCatalog(registry, optionProviders, discoveredSources)
            .FirstOrDefault(entry => ProviderIdentity.EqualsId(entry.ProviderId, providerId));
    }

    private static ProviderCatalogEntry MergeCatalogEntries(
        ProviderCatalogEntry existing,
        ProviderCatalogEntry overlay,
        ProviderCapabilityDescriptor? descriptor = null)
    {
        var mergedCredentials = existing.CredentialFields
            .Concat(overlay.CredentialFields)
            .GroupBy(
                field => $"{field.Name}|{field.EnvironmentVariable}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var mergedNotes = existing.Notes
            .Concat(overlay.Notes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedWarnings = existing.Warnings
            .Concat(overlay.Warnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedMarkets = existing.SupportedMarkets
            .Concat(overlay.SupportedMarkets)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedDataTypes = existing.DataTypes
            .Concat(overlay.DataTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existingCaps = existing.Capabilities;
        var overlayCaps = overlay.Capabilities;
        var maxDepthLevels = Math.Max(existingCaps.MaxDepthLevels ?? 0, overlayCaps.MaxDepthLevels ?? 0);
        var mergedCapabilities = new CapabilityInfo
        {
            SupportsStreaming = descriptor?.HasStreaming ?? (existingCaps.SupportsStreaming || overlayCaps.SupportsStreaming),
            SupportsBackfill = descriptor?.HasHistorical ?? (existingCaps.SupportsBackfill || overlayCaps.SupportsBackfill),
            SupportsSymbolSearch = descriptor?.HasSearch ?? (existingCaps.SupportsSymbolSearch || overlayCaps.SupportsSymbolSearch),
            SupportsCorporateActions = descriptor?.HasCorporateActions ?? (existingCaps.SupportsCorporateActions || overlayCaps.SupportsCorporateActions),
            SupportsMarketDepth = existingCaps.SupportsMarketDepth || overlayCaps.SupportsMarketDepth,
            MaxDepthLevels = maxDepthLevels > 0 ? maxDepthLevels : null,
            SupportsAdjustedPrices = existingCaps.SupportsAdjustedPrices || overlayCaps.SupportsAdjustedPrices,
            SupportsDividends = existingCaps.SupportsDividends || overlayCaps.SupportsDividends,
            SupportsSplits = existingCaps.SupportsSplits || overlayCaps.SupportsSplits,
            SupportsIntraday = existingCaps.SupportsIntraday || overlayCaps.SupportsIntraday,
            SupportsTrades = existingCaps.SupportsTrades || overlayCaps.SupportsTrades,
            SupportsQuotes = existingCaps.SupportsQuotes || overlayCaps.SupportsQuotes,
            SupportsOptionsChain = descriptor?.HasOptions ?? (existingCaps.SupportsOptionsChain || overlayCaps.SupportsOptionsChain),
            SupportsBrokerage = descriptor?.HasBrokerage ?? (existingCaps.SupportsBrokerage || overlayCaps.SupportsBrokerage),
            SupportsAuctions = existingCaps.SupportsAuctions || overlayCaps.SupportsAuctions,
            MarketDataCapabilities = existingCaps.MarketDataCapabilities
                .Concat(overlayCaps.MarketDataCapabilities)
                .Distinct()
                .ToArray()
        };

        return new ProviderCatalogEntry
        {
            ProviderId = ProviderIdentity.NormalizeId(existing.ProviderId),
            DisplayName = string.IsNullOrWhiteSpace(existing.DisplayName) ? overlay.DisplayName : existing.DisplayName,
            Description = string.Equals(existing.Description, overlay.Description, StringComparison.OrdinalIgnoreCase)
                ? existing.Description
                : $"{existing.Description} {overlay.Description}".Trim(),
            ProviderType = descriptor is null ? existing.ProviderType :
                (descriptor.HasStreaming, descriptor.HasHistorical) switch
                {
                    (true, true) => ProviderTypeKind.Hybrid,
                    (true, false) => ProviderTypeKind.Streaming,
                    (false, true) => ProviderTypeKind.Backfill,
                    _ => ProviderTypeKind.SymbolSearch
                },
            RequiresCredentials = existing.RequiresCredentials || overlay.RequiresCredentials,
            CredentialFields = mergedCredentials,
            RateLimit = existing.RateLimit ?? overlay.RateLimit,
            Notes = mergedNotes,
            Warnings = mergedWarnings,
            SupportedMarkets = mergedMarkets,
            DataTypes = mergedDataTypes,
            Capabilities = mergedCapabilities
        };
    }

}
