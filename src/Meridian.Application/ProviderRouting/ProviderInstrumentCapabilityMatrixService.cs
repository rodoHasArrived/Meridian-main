using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Diagnostics;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;

namespace Meridian.Application.ProviderRouting;

/// <summary>
/// Computes the provider × instrument-type capability matrix from the declared descriptor
/// catalogs (<see cref="ProviderCapabilityDescriptorCatalog"/> joined with
/// <see cref="InstrumentTypeDescriptorCatalog"/>) and attaches discovery failures captured by
/// the <see cref="DataSourceRegistry"/> so unavailable providers have a visible reason.
/// </summary>
public sealed class ProviderInstrumentCapabilityMatrixService : IProviderInstrumentCapabilityMatrixService
{
    private const string OptionAssetClass = "Option";
    private const string CorporateActionCapability = "CorporateAction";

    private readonly DataSourceRegistry? _dataSourceRegistry;

    public ProviderInstrumentCapabilityMatrixService(DataSourceRegistry? dataSourceRegistry = null)
    {
        _dataSourceRegistry = dataSourceRegistry;
    }

    public ProviderInstrumentCapabilityMatrixDto GetMatrix()
    {
        var instrumentDescriptors = InstrumentTypeDescriptorCatalog.All;
        var instrumentTypeNames = instrumentDescriptors
            .Select(static descriptor => descriptor.InstrumentType.ToString())
            .ToArray();

        var rows = ProviderCapabilityDescriptorCatalog.Descriptors
            .OrderBy(static descriptor => descriptor.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(provider => new ProviderInstrumentCapabilityRowDto(
                ProviderId: provider.ProviderId,
                DeclaredInstrumentTypes: provider.SupportedInstrumentTypes
                    .Select(static type => type.ToString())
                    .ToArray(),
                Surfaces: new ProviderCapabilitySurfaceDto(
                    Streaming: provider.HasStreaming,
                    Historical: provider.HasHistorical,
                    SymbolSearch: provider.HasSearch,
                    CorporateActions: provider.HasCorporateActions,
                    OptionsChain: provider.HasOptions,
                    Brokerage: provider.HasBrokerage),
                Cells: instrumentDescriptors
                    .Select(instrument => BuildCell(provider, instrument))
                    .ToArray()))
            .ToArray();

        var failures = _dataSourceRegistry?.Failures
            .Select(static failure => new ProviderDiscoveryFailureDto(
                Sanitize(failure.Stage),
                Sanitize(failure.Subject),
                failure.ModuleId is null ? null : Sanitize(failure.ModuleId),
                Sanitize(failure.ErrorType),
                Sanitize(failure.ErrorMessage)))
            .ToArray() ?? [];

        return new ProviderInstrumentCapabilityMatrixDto(
            GeneratedAt: DateTimeOffset.UtcNow,
            InstrumentTypes: instrumentTypeNames,
            Providers: rows,
            DiscoveryFailures: failures);
    }

    private static string Sanitize(string value)
    {
        const int maxLength = 512;
        var sanitized = RuntimeDiagnosticRedactor.SanitizeText(value);
        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    private static ProviderInstrumentCapabilityCellDto BuildCell(
        ProviderCapabilityDescriptor provider,
        InstrumentTypeDescriptor instrument)
    {
        var supported = provider.SupportedInstrumentTypes.Contains(instrument.InstrumentType);
        var carriesCorporateActions = instrument.ProviderCapabilities
            .Contains(CorporateActionCapability, StringComparer.OrdinalIgnoreCase);
        var isOptionInstrument = string.Equals(
            instrument.SecurityMasterAssetClass, OptionAssetClass, StringComparison.OrdinalIgnoreCase);

        return new ProviderInstrumentCapabilityCellDto(
            InstrumentType: instrument.InstrumentType.ToString(),
            Supported: supported,
            Stream: supported && provider.HasStreaming,
            Backfill: supported && provider.HasHistorical,
            CorporateActions: supported && provider.HasCorporateActions && carriesCorporateActions,
            SymbolSearch: supported && provider.HasSearch,
            OptionsChain: supported && provider.HasOptions && isOptionInstrument);
    }
}
