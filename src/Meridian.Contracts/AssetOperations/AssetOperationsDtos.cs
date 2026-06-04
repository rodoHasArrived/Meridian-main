using System.Text.Json;

namespace Meridian.Contracts.AssetOperations;

public sealed class AssetOperationsOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string Schema { get; set; } = "asset_operations";
}

public sealed record AssetOperationSubjectDto(
    Guid SecurityId,
    string AssetClass,
    string DisplayName,
    string? PrimaryIdentifier,
    IReadOnlyList<string> OperationalProfile);

public sealed record AssetTermsVersionDto(
    Guid TermsVersionId,
    Guid SecurityId,
    int VersionNumber,
    string TermsHash,
    DateOnly EffectiveDate,
    DateTimeOffset RecordedAt,
    string SourceDomain,
    string? SourceEntityId,
    string Summary,
    JsonElement? ExtensionPayload = null);

public sealed record AssetLifecycleEventDto(
    Guid LifecycleEventId,
    Guid SecurityId,
    string EventType,
    string LifecycleState,
    DateOnly EffectiveDate,
    DateTimeOffset RecordedAt,
    string SourceDomain,
    string? SourceEntityId,
    string Summary,
    JsonElement? ExtensionPayload = null);

public sealed record AssetCashFlowProjectionRunDto(
    Guid ProjectionRunId,
    Guid SecurityId,
    DateOnly ProjectionAsOf,
    string EngineVersion,
    string Status,
    DateTimeOffset GeneratedAt,
    string SourceDomain,
    string? SourceEntityId,
    JsonElement? ExtensionPayload = null);

public sealed record AssetProjectedCashFlowDto(
    Guid ProjectedCashFlowId,
    Guid ProjectionRunId,
    Guid SecurityId,
    int SequenceNumber,
    string FlowType,
    DateOnly DueDate,
    decimal Amount,
    string Currency,
    string Status,
    DateOnly? AccrualStartDate = null,
    DateOnly? AccrualEndDate = null,
    decimal? PrincipalBasis = null,
    decimal? AnnualRate = null,
    string? SourceDomain = null,
    string? SourceEntityId = null,
    JsonElement? ExtensionPayload = null);

public sealed record AssetActualActivityDto(
    Guid ActivityId,
    Guid SecurityId,
    string ActivityType,
    DateOnly EffectiveDate,
    DateOnly? SettlementDate,
    decimal Amount,
    string Currency,
    string Status,
    string SourceDomain,
    string? SourceEntityId,
    string? EvidenceLink,
    JsonElement? ExtensionPayload = null);

public sealed record AssetReconciliationRunDto(
    Guid ReconciliationRunId,
    Guid SecurityId,
    Guid? ProjectionRunId,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    string SourceDomain,
    string? SourceEntityId,
    JsonElement? ExtensionPayload = null);

public sealed record AssetReconciliationResultDto(
    Guid ReconciliationResultId,
    Guid ReconciliationRunId,
    Guid SecurityId,
    string MatchStatus,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal? VarianceAmount,
    DateOnly? ExpectedDate,
    DateOnly? ActualDate,
    string SourceDomain,
    string? SourceEntityId,
    string? EvidenceLink,
    JsonElement? ExtensionPayload = null);

public sealed record AssetLedgerProjectionDto(
    Guid LedgerProjectionId,
    Guid SecurityId,
    string ProjectionType,
    DateOnly AccountingDate,
    string LedgerBasis,
    string Status,
    decimal? DebitAmount,
    decimal? CreditAmount,
    string Currency,
    string SourceDomain,
    string? SourceEntityId,
    string? LedgerReferenceId,
    JsonElement? ExtensionPayload = null);

public sealed record AssetOperationsReadinessDto(
    Guid SecurityId,
    string Status,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> ReadyCapabilities,
    IReadOnlyList<string> MissingCapabilities,
    IReadOnlyList<string> Warnings,
    DateTimeOffset EvaluatedAt,
    string SourceDomain,
    string? SourceEntityId,
    JsonElement? ExtensionPayload = null);

public sealed record AssetOperationsDetailDto(
    AssetOperationSubjectDto Subject,
    IReadOnlyList<AssetTermsVersionDto> TermsHistory,
    IReadOnlyList<AssetLifecycleEventDto> LifecycleEvents,
    IReadOnlyList<AssetCashFlowProjectionRunDto> CashFlowProjectionRuns,
    IReadOnlyList<AssetProjectedCashFlowDto> ProjectedCashFlows,
    IReadOnlyList<AssetActualActivityDto> ActualActivity,
    IReadOnlyList<AssetReconciliationRunDto> ReconciliationRuns,
    IReadOnlyList<AssetReconciliationResultDto> ReconciliationResults,
    IReadOnlyList<AssetLedgerProjectionDto> LedgerProjections,
    AssetOperationsReadinessDto Readiness,
    IReadOnlyList<AssetLifecycleEventDto> WorkflowAudit);

public sealed record AssetOperationsProjectionDto(
    AssetOperationSubjectDto Subject,
    IReadOnlyList<AssetTermsVersionDto> TermsHistory,
    IReadOnlyList<AssetLifecycleEventDto> LifecycleEvents,
    IReadOnlyList<AssetCashFlowProjectionRunDto> CashFlowProjectionRuns,
    IReadOnlyList<AssetProjectedCashFlowDto> ProjectedCashFlows,
    IReadOnlyList<AssetActualActivityDto> ActualActivity,
    IReadOnlyList<AssetReconciliationRunDto> ReconciliationRuns,
    IReadOnlyList<AssetReconciliationResultDto> ReconciliationResults,
    IReadOnlyList<AssetLedgerProjectionDto> LedgerProjections,
    AssetOperationsReadinessDto Readiness,
    IReadOnlyList<AssetLifecycleEventDto> WorkflowAudit);

public sealed record AssetOperationsWriteApprovalDto(
    string Actor,
    string ApprovalReference,
    string Rationale,
    DateTimeOffset ApprovedAt);

public interface IAssetOperationsQueryService
{
    Task<AssetOperationsDetailDto?> GetOperationsAsync(Guid securityId, CancellationToken ct = default);

    Task<AssetOperationsReadinessDto?> GetReadinessAsync(Guid securityId, CancellationToken ct = default);
}

public interface IAssetOperationsCommandService
{
    Task<AssetOperationsDetailDto> UpsertProjectionAsync(
        AssetOperationsProjectionDto projection,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct = default);
}
