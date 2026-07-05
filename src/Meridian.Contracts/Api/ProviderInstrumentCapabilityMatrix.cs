namespace Meridian.Contracts.Api;

/// <summary>
/// Read-model contract for the provider × instrument-type capability matrix shown in the
/// Data workspace. Computed from the provider capability descriptor catalog joined with the
/// instrument-type descriptor catalog, with data-source discovery failures attached so an
/// operator can see both what a provider covers and why a provider failed to register.
/// </summary>
public interface IProviderInstrumentCapabilityMatrixService
{
    ProviderInstrumentCapabilityMatrixDto GetMatrix();
}

/// <summary>
/// Provider × instrument-type capability matrix with discovery failures.
/// </summary>
public sealed record ProviderInstrumentCapabilityMatrixDto(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<string> InstrumentTypes,
    IReadOnlyList<ProviderInstrumentCapabilityRowDto> Providers,
    IReadOnlyList<ProviderDiscoveryFailureDto> DiscoveryFailures);

/// <summary>
/// One provider row: its declared instrument coverage, the capability surfaces its adapters
/// implement, and one cell per instrument type.
/// </summary>
public sealed record ProviderInstrumentCapabilityRowDto(
    string ProviderId,
    IReadOnlyList<string> DeclaredInstrumentTypes,
    ProviderCapabilitySurfaceDto Surfaces,
    IReadOnlyList<ProviderInstrumentCapabilityCellDto> Cells);

/// <summary>
/// Capability surfaces implemented by a provider's adapters, independent of instrument type.
/// </summary>
public sealed record ProviderCapabilitySurfaceDto(
    bool Streaming,
    bool Historical,
    bool SymbolSearch,
    bool CorporateActions,
    bool OptionsChain,
    bool Brokerage);

/// <summary>
/// One matrix cell: whether the provider declares coverage for the instrument type, and which
/// capability surfaces apply to it. Corporate-action and options-chain flags are additionally
/// gated by whether the instrument type itself carries those capabilities.
/// </summary>
public sealed record ProviderInstrumentCapabilityCellDto(
    string InstrumentType,
    bool Supported,
    bool Stream,
    bool Backfill,
    bool CorporateActions,
    bool SymbolSearch,
    bool OptionsChain);

/// <summary>
/// A data-source discovery or registration failure surfaced alongside the matrix so a gray
/// provider row has a visible explanation.
/// </summary>
public sealed record ProviderDiscoveryFailureDto(
    string Stage,
    string Subject,
    string? ModuleId,
    string ErrorType,
    string ErrorMessage);
