using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Diagnostics;
using Meridian.Core.Config;
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
    private readonly UI.ConfigStore? _configStore;

    public ProviderInstrumentCapabilityMatrixService(DataSourceRegistry? dataSourceRegistry = null, UI.ConfigStore? configStore = null)
    {
        _dataSourceRegistry = dataSourceRegistry;
        _configStore = configStore;
    }

    public ProviderInstrumentCapabilityMatrixDto GetMatrix() => BuildMatrix(null);

    public ProviderInstrumentCapabilityMatrixDto GetMatrixForTenant(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return BuildMatrix(tenantId.Trim());
    }

    private ProviderInstrumentCapabilityMatrixDto BuildMatrix(string? tenantId)
    {
        // No unscoped configuration read: callers without an authenticated tenant see catalog
        // declarations only. A failed authoritative read must not invent a clean default state.
        var section = tenantId is not null ? _configStore?.LoadRequired().ProviderConnections : null;
        var instrumentDescriptors = InstrumentTypeDescriptorCatalog.All;
        var instrumentTypeNames = instrumentDescriptors
            .Select(static descriptor => descriptor.InstrumentType.ToString())
            .ToArray();

        var rows = ProviderCapabilityDescriptorCatalog.Descriptors
            // The catalog also inventories symbol resolvers and compatibility-only sources.
            // Only project families with a surface that this matrix can represent.
            .Where(static provider => provider.HasStreaming || provider.HasHistorical || provider.HasSearch ||
                provider.HasCorporateActions || provider.HasOptions || provider.HasBrokerage)
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
                    .ToArray(),
                FixedIncomeTasks: BuildFixedIncomeTasks(provider, section, tenantId)))
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

    private static IReadOnlyList<ProviderFixedIncomeTaskCapabilityDto> BuildFixedIncomeTasks(
        ProviderCapabilityDescriptor provider, ProviderConnectionsConfig? section, string? tenantId)
    {
        string[] tasks = ["Terms", "EvaluatedPrices", "ExecutableQuotes", "Trades", "Curves", "Fixings", "Factors", "LifecycleNotices"];
        var connections = (section?.Connections ?? [])
            .GroupBy(static connection => connection.ConnectionId, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .Select(static group => group.Single())
            .Where(connection => tenantId is not null && string.Equals(connection.TenantId, tenantId, StringComparison.Ordinal)
                && string.Equals(ProviderIdentity.NormalizeId(connection.ProviderFamilyId), provider.ProviderId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static connection => connection.ConnectionId, StringComparer.Ordinal)
            .ToArray();

        return tasks.Select(task =>
        {
            var (support, detail) = DescribeFixedIncomeTask(provider.ProviderId, task);
            return new ProviderFixedIncomeTaskCapabilityDto(task, support, detail,
                connections.Select(connection =>
                {
                    var certificates = (section?.Certifications ?? [])
                        .Where(certificate => string.Equals(certificate.ConnectionId, connection.ConnectionId, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    var evidence = new List<ProviderFixedIncomeEvidenceDto>
                    {
                        new("AdapterDeclaration", detail),
                        new("TaskEvidence", "No retained instrument-specific entitlement, observation, or verification receipt is attached to this task.")
                    };
                    if (certificates.Length == 1)
                    {
                        var certificate = certificates[0];
                        evidence.Add(new("ConnectionCertification",
                            $"General connection certification: {Sanitize(certificate.Status)}; expires {certificate.ExpiresAt:O}. This does not verify this fixed-income task.",
                            certificate.LastRunAt));
                        foreach (var check in certificate.Checks ?? [])
                            evidence.Add(new("ConnectionCheck", Sanitize(check), certificate.LastRunAt));
                    }
                    else if (certificates.Length > 1)
                        evidence.Add(new("ConnectionCertification", "Conflicting certification records; review the connection evidence."));

                    var unsupported = support == "Unsupported";
                    return new ProviderFixedIncomeConnectionEvidenceDto(
                        connection.ConnectionId, Sanitize(connection.DisplayName), connection.Enabled,
                        connection.Enabled ? "Configured" : "Disabled",
                        unsupported ? "NotApplicable" : "Unknown",
                        unsupported ? "NotApplicable" : "Unknown",
                        unsupported ? "unsupported" : "live-untested",
                        null, null, evidence);
                }).ToArray());
        }).ToArray();
    }

    private static (string Support, string Detail) DescribeFixedIncomeTask(string providerId, string task)
        => (providerId.ToLowerInvariant(), task) switch
        {
            ("ibkr", "Terms") => ("AdapterDeclared", "IBDataServices contract details can supply bond reference terms. Validate the required conventions against the retained response; a configured TWS connection does not establish completeness or entitlement."),
            ("ibkr", "ExecutableQuotes" or "Trades") => ("Unsupported", "The current IBKR streaming adapter profiles omit Bond. Broad bond coverage and historical bars do not establish executable bond quotes or bond trade observations."),
            ("fred", "Curves") => ("AdapterDeclared", "Typed FRED rate observations can supply curve inputs. Treasury constant-maturity yields require an explicit reviewed construction method before discount factors are published; OHLC compatibility bars are not curve evidence."),
            ("fred", "Fixings") => ("AdapterDeclared", "Typed FRED observations preserve rate units, observation and vintage dates. Select an observed fixing explicitly; forecasts and assumptions are separate inputs, and OHLC compatibility bars are not fixing evidence."),
            _ => ("Unsupported", "No implemented fixed-income task adapter is declared. General market-data, brokerage, or corporate-action coverage does not prove this input is available.")
        };

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
            Stream: supported && provider.HasStreaming && provider.SupportedStreamingInstrumentTypes.Contains(instrument.InstrumentType),
            Backfill: supported && provider.HasHistorical,
            CorporateActions: supported && provider.HasCorporateActions && carriesCorporateActions,
            SymbolSearch: supported && provider.HasSearch && provider.SupportedSearchInstrumentTypes.Contains(instrument.InstrumentType),
            OptionsChain: supported && provider.HasOptions && isOptionInstrument);
    }
}
