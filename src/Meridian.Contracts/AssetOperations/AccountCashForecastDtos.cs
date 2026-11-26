using System.Text.Json.Serialization;

namespace Meridian.Contracts.AssetOperations;

/// <summary>Exact operational scope; fund scope is deliberately not required.</summary>
public sealed record AccountCashForecastScopeDto(
    string TenantId, string CompanyId, string AccountId, string BookId, string BaseCurrency);

public enum AccountCashFlowStatus { Expected, Actual }
public enum AccountCashEvidenceRole { Observation, Projection, DeclaredFx }

/// <summary>Source bytes and accepted retained review used for one forecast input.</summary>
public sealed record AccountCashProvenanceDto(
    string SourceDomain,
    string SourceEntityId,
    string SourceContentHash,
    AccountCashEvidenceRole Role,
    RetainedEvidenceIdentityDto Evidence);

/// <summary>Opening means start-of-day settled cash; closing means end-of-day settled cash.</summary>
public sealed record AccountSettledCashObservationDto(
    string AccountId, string BookId, DateOnly AsOfDate, decimal Amount, string Currency,
    bool IsSettled, AccountCashProvenanceDto Provenance);

/// <summary>
/// Signed account cash movement. All three dates are explicit; only CashReceiptDate changes cash.
/// FlowId links an expectation to its observed movement; VersionId identifies the retained source revision.
/// </summary>
public sealed record AccountCashFlowDto(
    string FlowId,
    string VersionId,
    string AccountId,
    string BookId,
    AccountCashFlowStatus Status,
    string FlowType,
    decimal Amount,
    string Currency,
    DateOnly? EconomicDate,
    DateOnly? SettlementDate,
    DateOnly? CashReceiptDate,
    AccountCashProvenanceDto Provenance,
    Guid? SecurityId = null,
    Guid? ProjectionRunId = null,
    string? ProjectionEngineVersion = null,
    Guid? TermsVersionId = null,
    string? TermsHash = null,
    string? SupersedesVersionId = null);

/// <summary>
/// Declared dated conversion: multiply native Currency units by BaseUnitsPerCurrencyUnit to obtain
/// BaseCurrency units. An inverse pair or another date never substitutes for this declaration.
/// </summary>
public sealed record AccountCashFxConversionDto(
    string Currency, string BaseCurrency, DateOnly ConversionDate, decimal BaseUnitsPerCurrencyUnit,
    AccountCashProvenanceDto Provenance);

/// <summary>Complete retained inputs for an inclusive cash period, suitable for deterministic replay.</summary>
public sealed record AccountCashForecastRequestDto(
    string RunId,
    AccountCashForecastScopeDto Scope,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    AccountSettledCashObservationDto? OpeningCash,
    AccountSettledCashObservationDto? ClosingCash,
    IReadOnlyList<AccountCashFlowDto> Flows,
    IReadOnlyList<AccountCashFxConversionDto> FxConversions,
    DateTimeOffset RecordedAtUtc,
    string? PreviousSnapshotId = null,
    string? CorrectionRationale = null,
    decimal MinimumCashThreshold = 0m,
    int BucketDays = 1)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CalculationVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AccountSettledCashObservationDto>? NativeOpeningBalances { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AccountSettledCashObservationDto>? NativeClosingBalances { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AccountCashPaymentAllocationDto>? PaymentAllocations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AccountCashReceiptRevisionDto>? ReceiptRevisions { get; init; }
}

/// <summary>Signed cash in the obligation currency. Gross = net + nonnegative evidenced tax and fee cash costs.</summary>
public sealed record AccountCashPaymentAllocationDto(
    string AllocationId, string ExpectedFlowId, string ActualVersionId,
    decimal NetAmount, decimal GrossAmount, decimal WithholdingTax = 0m, decimal Fees = 0m,
    AccountCashProvenanceDto? DeductionEvidence = null);

/// <summary>An evidenced revised date never changes the retained original expectation.</summary>
public sealed record AccountCashReceiptRevisionDto(string ExpectedFlowId, DateOnly ReceiptDate, AccountCashProvenanceDto Evidence);

public sealed record AccountCashResidualDto(string ExpectedFlowId, decimal ExpectedAmount, decimal ReceivedGrossAmount,
    decimal RemainingAmount, string Currency, DateOnly? OriginalReceiptDate, DateOnly? RevisedReceiptDate,
    bool IncludedInForecast, string Status);

public sealed record AccountCashForecastIssueDto(string Code, string Message, string? InputId = null);

/// <summary>Immutable forecast evidence. Certification is cash reconciliation, never payment authority.</summary>
public sealed record AccountCashForecastSnapshotDto(
    string SnapshotId,
    string EvidenceHash,
    string EngineVersion,
    AccountCashForecastRequestDto Inputs,
    decimal? OpeningBaseCash,
    decimal? ActualNetBaseFlow,
    decimal? ReconciledClosingBaseCash,
    decimal? ObservedClosingBaseCash,
    decimal? ClosingVariance,
    bool IsCertified,
    PortfolioCashLadderDto ForecastLadder,
    IReadOnlyList<AccountCashForecastIssueDto> Blockers)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PortfolioCashLadderDto>? CurrencyLadders { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AccountCashResidualDto>? Residuals { get; init; }
}

/// <summary>
/// Retains account-scoped cash forecast snapshots and retrieves their captured history.
/// </summary>
public interface IAccountCashForecastStore
{
    Task<AccountCashForecastSnapshotDto> AppendAsync(AccountCashForecastSnapshotDto snapshot, CancellationToken ct = default);
    Task<IReadOnlyList<AccountCashForecastSnapshotDto>> GetHistoryAsync(AccountCashForecastScopeDto scope, CancellationToken ct = default);
}

/// <summary>
/// Captures account cash forecasts and retrieves snapshots within an explicit account and book scope.
/// </summary>
public interface IAccountCashForecastService
{
    Task<AccountCashForecastSnapshotDto> CaptureAsync(AccountCashForecastRequestDto request, CancellationToken ct = default);
    Task<AccountCashForecastSnapshotDto?> GetAsync(AccountCashForecastScopeDto scope, string snapshotId, CancellationToken ct = default);
    Task<IReadOnlyList<AccountCashForecastSnapshotDto>> GetHistoryAsync(AccountCashForecastScopeDto scope, CancellationToken ct = default);
}
