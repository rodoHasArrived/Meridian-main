using Meridian.Contracts.SecurityMaster;
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
    private static readonly IReadOnlyList<SecuritySummaryDto> _emptySummaries =
        Array.Empty<SecuritySummaryDto>();

    private static readonly IReadOnlyList<SecurityMasterEventEnvelope> _emptyHistory =
        Array.Empty<SecurityMasterEventEnvelope>();

    private static readonly IReadOnlyList<CorporateActionDto> _emptyActions =
        Array.Empty<CorporateActionDto>();

    public bool IsAvailable => false;

    public string AvailabilityDescription =>
        "Security Master is not configured. Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to enable runtime-backed security workflows.";

    public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<SecurityDetailDto?>(null);

    public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
        => Task.FromResult<SecurityDetailDto?>(null);

    public Task<SecurityDetailDto?> GetRecordedByIdAsOfAsync(
        Guid securityId,
        DateTimeOffset asOfUtc,
        CancellationToken ct = default)
        => Task.FromResult<SecurityDetailDto?>(null);

    public Task<SecurityDetailDto?> GetByIdentifierAsync(
        SecurityIdentifierKind identifierKind,
        string identifierValue,
        string? provider,
        CancellationToken ct = default,
        DateTimeOffset? asOfUtc = null)
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

public sealed class NullSecurityMasterService : Meridian.Contracts.SecurityMaster.ISecurityMasterService, Meridian.Contracts.SecurityMaster.ISecurityMasterAmender
{
    private static Task<T> NotConfigured<T>() =>
        Task.FromException<T>(new InvalidOperationException(
            "Security Master is not configured. " +
            "Set the MERIDIAN_SECURITY_MASTER_CONNECTION_STRING environment variable to enable this feature."));

    public Task<SecurityDetailDto> CreateAsync(CreateSecurityRequest request, CancellationToken ct = default)
        => NotConfigured<SecurityDetailDto>();

    public Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default)
        => NotConfigured<SecurityDetailDto>();

    public Task<SecurityDetailDto> AmendPreferredEquityTermsAsync(Guid securityId, AmendPreferredEquityTermsRequest request, CancellationToken ct = default)
        => NotConfigured<SecurityDetailDto>();

    public Task<SecurityDetailDto> AmendConvertibleEquityTermsAsync(Guid securityId, AmendConvertibleEquityTermsRequest request, CancellationToken ct = default)
        => NotConfigured<SecurityDetailDto>();

    public Task DeactivateAsync(DeactivateSecurityRequest request, CancellationToken ct = default)
        => Task.FromException(new InvalidOperationException(
            "Security Master is not configured. " +
            "Set the MERIDIAN_SECURITY_MASTER_CONNECTION_STRING environment variable to enable this feature."));

    public Task<SecurityAliasDto> UpsertAliasAsync(UpsertSecurityAliasRequest request, CancellationToken ct = default)
        => NotConfigured<SecurityAliasDto>();
}

public sealed class NullCorporateActionCommandService : ICorporateActionCommandService
{
    public Task<CorporateActionAppendResult> AppendAsync(
        Guid securityId,
        CorporateActionDto action,
        string? actor,
        string source,
        CancellationToken ct = default)
        => Task.FromException<CorporateActionAppendResult>(new InvalidOperationException(
            "Security Master is not configured. " +
            "Set the MERIDIAN_SECURITY_MASTER_CONNECTION_STRING environment variable to enable this feature."));
}

public sealed class NullSecurityMasterCorporateActionCommandService : ISecurityMasterCorporateActionCommandService
{
    public Task<SecurityMasterCorporateActionAppendResultDto> AppendAsync(
        SecurityMasterCorporateActionAppendRequestDto request,
        CancellationToken ct = default)
        => Task.FromException<SecurityMasterCorporateActionAppendResultDto>(new InvalidOperationException(
            "Security Master is not configured. " +
            "Set the MERIDIAN_SECURITY_MASTER_CONNECTION_STRING environment variable to enable corporate action appends."));
}

public sealed class NullCorporateActionOperationsService : ICorporateActionOperationsService
{
    private static Task<T> NotConfigured<T>() =>
        Task.FromException<T>(new CorporateActionOperationException(
            CorporateActionProblemCodes.PersistenceUnavailable,
            "Security Master corporate-action persistence is not configured."));

    public Task<CorporateActionSourceProposalDto> RecordSourceProposalAsync(RecordCorporateActionSourceProposalRequestDto request, CancellationToken ct = default) =>
        NotConfigured<CorporateActionSourceProposalDto>();

    public Task<CorporateActionSourceProposalDto?> GetSourceProposalAsync(Guid proposalId, CancellationToken ct = default) =>
        NotConfigured<CorporateActionSourceProposalDto?>();

    public Task<IReadOnlyList<CorporateActionSourceProposalDto>> ListSourceProposalsAsync(Guid? securityId, string? state, int take, CancellationToken ct = default) =>
        NotConfigured<IReadOnlyList<CorporateActionSourceProposalDto>>();

    public Task<IReadOnlyList<CorporateActionSourceProposalDto>> ListActionableSourceProposalsAsync(Guid? securityId, int take, CancellationToken ct = default) =>
        NotConfigured<IReadOnlyList<CorporateActionSourceProposalDto>>();

    public Task<CorporateActionDurableInboxDto> GetInboxAsync(CorporateActionCaseScopeDto acceptanceScope, int take, CancellationToken ct = default) =>
        NotConfigured<CorporateActionDurableInboxDto>();

    public Task<CorporateActionSourceProposalAcceptanceResultDto> AcceptSourceProposalAsync(AcceptCorporateActionSourceProposalRequestDto request, CancellationToken ct = default) =>
        NotConfigured<CorporateActionSourceProposalAcceptanceResultDto>();

    public Task<CorporateActionSourceProposalDecisionResultDto> RejectSourceProposalAsync(RejectCorporateActionSourceProposalRequestDto request, CancellationToken ct = default) =>
        NotConfigured<CorporateActionSourceProposalDecisionResultDto>();

    public Task<CorporateActionProcessingCaseDto?> GetCaseAsync(Guid caseId, string tenantId, string companyId, CancellationToken ct = default) =>
        NotConfigured<CorporateActionProcessingCaseDto?>();

    public Task<IReadOnlyList<CorporateActionProcessingCaseDto>> ListCasesAsync(string tenantId, string companyId, Guid? securityId, string? state, int take, CancellationToken ct = default) =>
        NotConfigured<IReadOnlyList<CorporateActionProcessingCaseDto>>();

    public Task<CorporateActionConflictDto?> GetConflictAsync(Guid caseId, Guid conflictId, string tenantId, string companyId, CancellationToken ct = default) =>
        NotConfigured<CorporateActionConflictDto?>();

    public Task<IReadOnlyList<CorporateActionConflictDto>> ListConflictsAsync(Guid caseId, string tenantId, string companyId, string? state, int take, CancellationToken ct = default) =>
        NotConfigured<IReadOnlyList<CorporateActionConflictDto>>();

    public Task<CorporateActionEvidenceMutationResultDto> AddEvidenceAsync(AddCorporateActionEvidenceRequestDto request, CancellationToken ct = default) =>
        NotConfigured<CorporateActionEvidenceMutationResultDto>();

    public Task<CorporateActionConflictMutationResultDto> RecordConflictAsync(RecordCorporateActionConflictRequestDto request, CancellationToken ct = default) =>
        NotConfigured<CorporateActionConflictMutationResultDto>();

    public Task<CorporateActionConflictResolutionResultDto> ResolveConflictAsync(ResolveCorporateActionConflictRequestDto request, CancellationToken ct = default) =>
        NotConfigured<CorporateActionConflictResolutionResultDto>();

    public Task<CorporateActionProcessingOptionMutationResultDto> UpsertOptionAsync(UpsertCorporateActionProcessingOptionRequestDto request, CancellationToken ct = default) =>
        NotConfigured<CorporateActionProcessingOptionMutationResultDto>();

    public Task<CorporateActionCaseTransitionResultDto> TransitionCaseAsync(TransitionCorporateActionCaseRequestDto request, CancellationToken ct = default) =>
        NotConfigured<CorporateActionCaseTransitionResultDto>();
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

    public Task RecordFieldConflictsAsync(SecurityProjectionRecord previous, SecurityProjectionRecord incoming, CancellationToken ct)
        => Task.CompletedTask;

    public Task ReconcileOpenFieldConflictsAsync(SecurityProjectionRecord persisted, CancellationToken ct)
        => Task.CompletedTask;
}

// ──────────────────────────────────────────────────────────────────────────────
// Import service — returns error result when Security Master is not configured
// ──────────────────────────────────────────────────────────────────────────────

public sealed class NullSecurityMasterImportService : ISecurityMasterImportService, ISecurityMasterIngestStatusService
{
    public SecurityMasterIngestStatusSnapshot GetSnapshot()
        => new(null, null);

    public Task<SecurityMasterImportResult> ImportAsync(
        string fileContent,
        string fileExtension,
        string actor,
        IProgress<SecurityMasterImportProgress>? progress = null,
        CancellationToken ct = default)
        => Task.FromResult(new SecurityMasterImportResult(
            Imported: 0,
            Skipped: 0,
            Failed: 1,
            ConflictsDetected: 0,
            Errors: ["Security Master is not configured. Set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING to enable."]));
}

/// <summary>
/// No-op trading-parameter backfill service used when Security Master is not configured.
/// </summary>
public sealed class NullTradingParametersBackfillService : Meridian.Infrastructure.Adapters.Polygon.ITradingParametersBackfillService
{
    public Task BackfillAllAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task BackfillTickerAsync(string ticker, Guid securityId, CancellationToken ct = default) => Task.CompletedTask;
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
            "Security Master is not configured. " +
            "Set the MERIDIAN_SECURITY_MASTER_CONNECTION_STRING environment variable to enable this feature."));

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

// ──────────────────────────────────────────────────────────────────────────────
// Operator overrides store — reports as missing when Security Master is offline
// ──────────────────────────────────────────────────────────────────────────────

internal sealed class NullOperatorOverridesStore : IOperatorOverridesStore
{
    public Task<OperatorOverridesDto?> GetAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<OperatorOverridesDto?>(null);

    public Task<OperatorOverridesDto> PatchAsync(
        Guid securityId,
        OperatorOverridesPatchRequest request,
        string updatedBy,
        CancellationToken ct = default,
        long? expectedCanonicalVersion = null)
        => Task.FromException<OperatorOverridesDto>(new InvalidOperationException(
            "Security Master is not configured. " +
            "Set the MERIDIAN_SECURITY_MASTER_CONNECTION_STRING environment variable to enable operator overrides."));

    public Task<OperatorOverridesDto> RecordApprovalDecisionAsync(
        Guid securityId,
        OperatorOverrideDecision decision,
        CancellationToken ct = default)
        => Task.FromException<OperatorOverridesDto>(new InvalidOperationException(
            "Security Master is not configured. " +
            "Set the MERIDIAN_SECURITY_MASTER_CONNECTION_STRING environment variable to enable operator overrides."));
}

// ──────────────────────────────────────────────────────────────────────────────
// Field-level provenance store — inert when Security Master is offline
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Field provenance is best-effort lineage, not a workflow gate: with no Security Master backend
/// there is nothing to attribute, so writes are no-ops and reads are empty rather than failures.
/// </summary>
internal sealed class NullSecurityFieldProvenanceStore : ISecurityFieldProvenanceStore
{
    private static readonly IReadOnlyList<SecurityFieldProvenanceRecord> Empty = [];

    public Task UpsertAsync(SecurityFieldProvenanceRecord record, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RemoveAsync(
        Guid securityId, string fieldPath, string origin, DateTimeOffset clearedAt,
        long? maxSourceVersion = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<SecurityFieldProvenanceRecord>> GetAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult(Empty);
}
