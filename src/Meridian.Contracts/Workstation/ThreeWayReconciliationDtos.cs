using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

/// <summary>A comparison request; source snapshots and owner identities are resolved by the server.</summary>
public sealed record ThreeWayReconciliationRequestDto(
    Guid LedgerBookId,
    Guid AccountingPeriodId,
    string OperationId,
    DateTimeOffset? CutoffUtc = null);

public sealed record ThreeWayReconciliationScopeDto(
    string TenantId,
    string CompanyId,
    string FundProfileId,
    Guid EntityId,
    Guid FinancialAccountId,
    Guid LedgerBookId,
    Guid AccountingPeriodId,
    string Currency,
    string ProviderId = "unlinked",
    string ExternalAccountId = "unlinked");

public sealed record ThreeWayPositionDto(Guid SecurityId, string Symbol,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Quantity,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? CostBasis);

/// <summary>Retained independent source operands, including coverage failures rather than invented zeroes.</summary>
public sealed record ThreeWaySourceSnapshotDto(
    string Source,
    DateTimeOffset AsOfUtc,
    string EvidenceReference,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? Cash,
    IReadOnlyList<ThreeWayPositionDto> Positions,
    string CostBasisConvention,
    IReadOnlyList<string> CoverageFailures,
    string DataProvenanceToken = "real")
{
    public IReadOnlyList<ThreeWayCashActivityDto> CashActivities { get; init; } = [];
    public IReadOnlyList<string> PostedExternalTransactionIds { get; init; } = [];
    public bool TransactionIdentityCoverageComplete { get; init; }
    public long? AccountingPeriodVersion { get; init; }
}

public sealed record ThreeWayReconciliationCheckDto(
    string CheckId,
    string Measure,
    Guid? SecurityId,
    string? Symbol,
    string LeftSource,
    string RightSource,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? LeftAmount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? RightAmount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Tolerance,
    string Status,
    string Explanation);

public sealed record ThreeWayReconciliationResultDto(
    string RunId,
    string OperationId,
    ThreeWayReconciliationScopeDto Scope,
    DateTimeOffset CutoffUtc,
    DateTimeOffset RecordedAtUtc,
    string Status,
    IReadOnlyList<ThreeWaySourceSnapshotDto> Sources,
    IReadOnlyList<ThreeWayReconciliationCheckDto> Checks,
    IReadOnlyList<string> BreakIds,
    string DataProvenanceToken = "real")
{
    public IReadOnlyList<ThreeWayAdjustmentProposalDto> AdjustmentProposals { get; init; } = [];
    public long? AccountingPeriodVersion { get; init; }
}

public sealed record ThreeWayCashActivityDto(string TransactionId, string TransactionType,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Amount,
    string Currency, DateTimeOffset PostedAtUtc, string EvidenceReference);

public sealed record ThreeWayAdjustmentProposalDto(string CheckId, string Status, string Description,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? Amount,
    string Currency, IReadOnlyList<string> EvidenceIds, string EvidenceHash);

public sealed record PrepareThreeWayAdjustmentRequestDto(Guid LedgerBookId, Guid AccountingPeriodId,
    string OperationId, string CheckId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long ExpectedPeriodVersion,
    string EvidenceHash);

/// <summary>Retained economic identity; copies cannot create a second effect for one broker fee.</summary>
public sealed record ThreeWayFeeCorrectionEvidenceDto(string EconomicKey, string TenantId, string CompanyId,
    string ProviderId, string ExternalAccountId, string FeeTransactionId, Guid FinancialAccountId, Guid LedgerBookId,
    string SourceRunId, string SourceEvidenceHash,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal Amount,
    string Currency, DateTimeOffset FeePostedAtUtc);
