using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;

namespace Meridian.Contracts.FixedIncome;

/// <summary>Signed contractual cash, scoped by the enclosing immutable account/book snapshot.</summary>
public sealed record BondMonitoringObligationDto(string ObligationId, Guid SecurityId, string FlowType,
    DateOnly DueDate, decimal Amount, string Currency, string ModelVersion,
    IReadOnlyList<BondCalculationInput> Inputs, bool IsForecast = false, bool IsCurrentHolding = true);

public sealed record BondMonitoringResetDto(Guid SecurityId, DateOnly AccrualDate, DateOnly ObservationDate,
    decimal Rate, bool IsObserved, string Source, IReadOnlyList<BondCalculationInput> Inputs)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? CouponPaymentDate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? CouponAmount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PreviousRate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PreviousCouponAmount { get; init; }
}

/// <summary>Accepted holding factors; distinct conflicting values remain explicit.</summary>
public sealed record BondMonitoringMarketHoldingDto(Guid SecurityId, IReadOnlyList<decimal> PrincipalFactors);

/// <summary>Tenant market-store evidence is separate from account Evidence Vault observations.</summary>
public sealed record BondMonitoringMarketWatchDto(string HoldingsContentSha256,
    IReadOnlyList<BondMonitoringMarketHoldingDto> Holdings, IReadOnlyList<BondMarketObservationDto> Observations);

/// <summary>Agency-specific notices and revisions; eligible call dates are distinct from issuer notices.</summary>
public sealed record BondWatchObservationDto(string ObservationId, Guid SecurityId, string Kind,
    DateOnly EffectiveDate, string Value, string? Agency, AccountCashProvenanceDto Provenance,
    string? SupersedesObservationId = null);

public sealed record BondMonitoringCaptureRequest(string RunId, AccountCashForecastScopeDto Scope,
    DateTimeOffset RecordedAtUtc, IReadOnlyList<BondMonitoringObligationDto> Obligations,
    IReadOnlyList<AccountCashFlowDto> Payments, IReadOnlyList<AccountCashPaymentAllocationDto> Allocations,
    IReadOnlyList<AccountCashReceiptRevisionDto> ReceiptRevisions, IReadOnlyList<BondWatchObservationDto> Observations,
    string? PreviousSnapshotId = null, string? CorrectionRationale = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlanningRunId { get; init; }
    public IReadOnlyList<BondMonitoringResetDto> Resets { get; init; } = [];
    public BondCalculationContext? CalculationContext { get; init; }
    public IReadOnlyList<BondExerciseNoticeWatchResult> ExerciseWindows { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BondMonitoringMarketWatchDto? MarketWatch { get; init; }
}

public sealed record BondCollectionStatusDto(string ObligationId, Guid SecurityId, string FlowType,
    DateOnly DueDate, decimal ExpectedAmount, decimal NetCollected, decimal GrossCollected,
    decimal WithholdingTax, decimal Fees, decimal RemainingAmount, string Currency,
    string Status, int AgeDays, DateOnly? RevisedReceiptDate, bool IncludedInSpendableForecast,
    IReadOnlyList<string> PaymentVersionIds);

public sealed record BondWatchItemDto(string WatchId, Guid SecurityId, string Kind, DateOnly Date,
    string Summary, string? Agency, string Source, bool RequiresAttention,
    IReadOnlyList<string> AffectedObligationIds);

public sealed record BondMonitoringSnapshotDto(string SnapshotId, string EngineVersion,
    BondMonitoringCaptureRequest Inputs, IReadOnlyList<BondCollectionStatusDto> Collections,
    IReadOnlyList<BondWatchItemDto> Watch, IReadOnlyList<AccountCashForecastIssueDto> Issues);

public interface IBondMonitoringStore
{
    Task<BondMonitoringSnapshotDto> AppendAsync(BondMonitoringSnapshotDto snapshot, CancellationToken ct = default);
    Task<IReadOnlyList<BondMonitoringSnapshotDto>> GetHistoryAsync(AccountCashForecastScopeDto scope, CancellationToken ct = default);
}

public interface IBondMonitoringService
{
    Task<BondMonitoringSnapshotDto> CaptureAsync(BondMonitoringCaptureRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<BondMonitoringSnapshotDto>> GetHistoryAsync(AccountCashForecastScopeDto scope, CancellationToken ct = default);
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BondMonitoringCaptureRequest))]
[JsonSerializable(typeof(BondMonitoringSnapshotDto))]
[JsonSerializable(typeof(BondMonitoringSnapshotDto[]))]
[JsonSerializable(typeof(BondMonitoringObligationDto))]
public partial class BondMonitoringJsonContext : JsonSerializerContext;
