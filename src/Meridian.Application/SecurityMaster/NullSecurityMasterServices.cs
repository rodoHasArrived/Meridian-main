using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// No-op implementations of all Security Master service interfaces, registered when
/// <c>MERIDIAN_SECURITY_MASTER_CONNECTION_STRING</c> is not configured.
/// Query operations return <c>null</c> / empty collections so that the Minimal API
/// endpoint handlers can return 404 / empty JSON as appropriate.
/// Command and write operations throw <see cref="InvalidOperationException"/> so that
/// the caller receives an HTTP 500 that surfaces the configuration requirement.
/// These stubs ensure that ASP.NET Core Minimal API routing initializes correctly even
/// without a Security Master database, preventing startup failures in unconfigured or
/// test environments.
/// </summary>

// ──────────────────────────────────────────────────────────────────────────────
// Query service (read-only) — returns null / empty so endpoint callers see 404
// ──────────────────────────────────────────────────────────────────────────────

public sealed class NullSecurityMasterQueryService
    : ISecurityMasterQueryService,
      Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService,
      ISecurityMasterRuntimeStatus
{
    internal const string NotConfiguredMessage =
        "Security Master is not configured. Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to enable this feature.";

    private static readonly IReadOnlyList<SecuritySummaryDto> _emptySummaries =
        Array.Empty<SecuritySummaryDto>();

    private static readonly IReadOnlyList<SecurityMasterEventEnvelope> _emptyHistory =
        Array.Empty<SecurityMasterEventEnvelope>();

    private static readonly IReadOnlyList<CorporateActionDto> _emptyActions =
        Array.Empty<CorporateActionDto>();

    public bool IsAvailable => false;

    public string AvailabilityDescription =>
        "Security Master is unavailable because MERIDIAN_SECURITY_MASTER_CONNECTION_STRING is not configured.";

    public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<SecurityDetailDto?>(null);

    public Task<SecurityDetailDto?> GetByIdentifierAsync(
        SecurityIdentifierKind identifierKind,
        string identifierValue,
        string? provider,
        CancellationToken ct = default)
        => Task.FromResult<SecurityDetailDto?>(null);

    public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(
        SecuritySearchRequest request,
        CancellationToken ct = default)
        => Task.FromResult(_emptySummaries);

    public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(
        SecurityHistoryRequest request,
        CancellationToken ct = default)
        => Task.FromResult(_emptyHistory);

    public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(
        Guid securityId,
        CancellationToken ct = default)
        => Task.FromResult<SecurityEconomicDefinitionRecord?>(null);

    public Task<TradingParametersDto?> GetTradingParametersAsync(
        Guid securityId,
        DateTimeOffset asOf,
        CancellationToken ct = default)
        => Task.FromResult<TradingParametersDto?>(null);

    public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(
        Guid securityId,
        CancellationToken ct = default)
        => Task.FromResult(_emptyActions);

    public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(
        Guid securityId,
        CancellationToken ct = default)
        => Task.FromResult<PreferredEquityTermsDto?>(null);

    public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(
        Guid securityId,
        CancellationToken ct = default)
        => Task.FromResult<ConvertibleEquityTermsDto?>(null);
}

// ──────────────────────────────────────────────────────────────────────────────
// Command service — throws when Security Master is not configured
// ──────────────────────────────────────────────────────────────────────────────

public sealed class NullSecurityMasterService : Meridian.Contracts.SecurityMaster.ISecurityMasterService
{
    private static Task<T> NotConfigured<T>() =>
        Task.FromException<T>(new InvalidOperationException(
            NullSecurityMasterQueryService.NotConfiguredMessage));

    public Task<SecurityDetailDto> CreateAsync(CreateSecurityRequest request, CancellationToken ct = default)
        => NotConfigured<SecurityDetailDto>();

    public Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default)
        => NotConfigured<SecurityDetailDto>();

    public Task<SecurityDetailDto> AmendPreferredEquityTermsAsync(Guid securityId, AmendPreferredEquityTermsRequest request, CancellationToken ct = default)
        => NotConfigured<SecurityDetailDto>();

    public Task DeactivateAsync(DeactivateSecurityRequest request, CancellationToken ct = default)
        => Task.FromException(new InvalidOperationException(
            NullSecurityMasterQueryService.NotConfiguredMessage));

    public Task<SecurityAliasDto> UpsertAliasAsync(UpsertSecurityAliasRequest request, CancellationToken ct = default)
        => NotConfigured<SecurityAliasDto>();
}

// ──────────────────────────────────────────────────────────────────────────────
// Conflict service — returns empty lists (no conflicts to show when not configured)
// ──────────────────────────────────────────────────────────────────────────────

internal sealed class NullSecurityMasterConflictService : ISecurityMasterConflictService
{
    private static readonly IReadOnlyList<SecurityMasterConflict> _empty =
        Array.Empty<SecurityMasterConflict>();

    public Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct)
        => Task.FromResult(_empty);

    public Task<SecurityMasterConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct)
        => Task.FromResult<SecurityMasterConflict?>(null);

    public Task<SecurityMasterConflict?> ResolveAsync(ResolveConflictRequest request, CancellationToken ct)
        => Task.FromResult<SecurityMasterConflict?>(null);

    public Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct)
        => Task.CompletedTask;
}

// ──────────────────────────────────────────────────────────────────────────────
// Import service — returns error result when Security Master is not configured
// ──────────────────────────────────────────────────────────────────────────────

public sealed class NullSecurityMasterImportService : ISecurityMasterImportService
{
    public Task<SecurityMasterImportResult> ImportAsync(
        string fileContent,
        string fileExtension,
        IProgress<SecurityMasterImportProgress>? progress = null,
        CancellationToken ct = default)
        => Task.FromResult(new SecurityMasterImportResult(
            Imported: 0,
            Skipped: 0,
            Failed: 1,
            ConflictsDetected: 0,
            Errors: [NullSecurityMasterQueryService.NotConfiguredMessage]));
}

/// <summary>
/// No-op backfill service registered when either Security Master or Polygon credentials are unavailable.
/// </summary>
public sealed class NullTradingParametersBackfillService : ITradingParametersBackfillService
{
    public Task BackfillAllAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task BackfillTickerAsync(string ticker, Guid securityId, CancellationToken ct = default)
        => Task.CompletedTask;
}

// ──────────────────────────────────────────────────────────────────────────────
// Event store — returns empty collections; throws on write operations
// ──────────────────────────────────────────────────────────────────────────────

internal sealed class NullSecurityMasterEventStore : ISecurityMasterEventStore
{
    private static readonly IReadOnlyList<SecurityMasterEventEnvelope> _emptyEnvelopes =
        Array.Empty<SecurityMasterEventEnvelope>();

    private static readonly IReadOnlyList<CorporateActionDto> _emptyActions =
        Array.Empty<CorporateActionDto>();

    private static Task NotConfigured() =>
        Task.FromException(new InvalidOperationException(
            NullSecurityMasterQueryService.NotConfiguredMessage));

    public Task AppendAsync(
        Guid securityId,
        long expectedVersion,
        IReadOnlyList<SecurityMasterEventEnvelope> events,
        CancellationToken ct = default)
        => NotConfigured();

    public Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult(_emptyEnvelopes);

    public Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadSinceSequenceAsync(
        long sequenceExclusive,
        int take,
        CancellationToken ct = default)
        => Task.FromResult(_emptyEnvelopes);

    public Task<long> GetLatestSequenceAsync(CancellationToken ct = default)
        => Task.FromResult(0L);

    public Task AppendCorporateActionAsync(CorporateActionDto action, CancellationToken ct = default)
        => NotConfigured();

    public Task<IReadOnlyList<CorporateActionDto>> LoadCorporateActionsAsync(
        Guid securityId,
        CancellationToken ct = default)
        => Task.FromResult(_emptyActions);
}
