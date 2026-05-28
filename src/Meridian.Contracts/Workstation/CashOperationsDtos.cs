namespace Meridian.Contracts.Workstation;

public enum CashSyncSourceAvailability : byte { Unknown = 0, Available = 1, Delayed = 2, Offline = 3 }
public enum StpProcessingState : byte { Received = 0, Validated = 1, Approved = 2, QueuedForSettlement = 3, Settled = 4, Exception = 5 }

public sealed record CashSyncWindow(
    string Region,
    string TimeZoneId,
    TimeOnly WindowStart,
    TimeOnly WindowEnd,
    IReadOnlyDictionary<string, CashSyncSourceAvailability> SourceAvailability,
    IReadOnlyList<Guid> AccountIds);

public sealed record SyncCompletenessResult(Guid AccountId, string Counterparty, int ExpectedRecords, int ReceivedRecords, bool IsComplete, string Detail);
public sealed record DeltaOutlierResult(Guid AccountId, string MetricName, decimal PriorDayValue, decimal CurrentValue, decimal Delta, decimal DeltaRatio, bool IsOutlier, string Detail);
public sealed record SyncValidationResult(bool Passed, IReadOnlyList<SyncCompletenessResult> CompletenessChecks, IReadOnlyList<DeltaOutlierResult> OutlierChecks, IReadOnlyList<string> Warnings);

public sealed record PaymentInstruction(Guid InstructionId, Guid AccountId, string Currency, decimal Amount, DateOnly TradeDate, DateOnly ValueDate, string Counterparty, string Reference);
public sealed record SettlementInstruction(Guid InstructionId, Guid AccountId, string Currency, decimal GrossAmount, DateOnly SettlementDate, string Counterparty, string SettlementMethod, string Reference);
public sealed record CouponEvent(Guid EventId, Guid AccountId, string SecurityId, string Currency, decimal CouponAmount, DateOnly ExDate, DateOnly PayDate);
public sealed record ApprovalStep(int Sequence, string RoleOrQueue, bool DualControlRequired, decimal? AmountThreshold, string? Currency, Guid? AccountId, string? RiskBucket);

public sealed record ApprovalPolicy(
    decimal? MinimumAmount,
    decimal? HighValueThreshold,
    IReadOnlySet<string>? AllowedCurrencies,
    IReadOnlySet<Guid>? RoutedAccountIds,
    IReadOnlySet<string>? RoutedRiskBuckets,
    bool RequireDualControl);

public sealed record ApprovalDecision(string Queue, bool DualControlRequired, IReadOnlyList<string> Reasons);

public sealed record FxConversionReference(string BaseCurrency, string QuoteCurrency, decimal Rate, DateTimeOffset AsOfUtc, string Source);

public sealed record CashFlowProjectionPoint(Guid AccountId, string Currency, DateOnly Date, decimal ExpectedInflow, decimal ExpectedOutflow, decimal NetFlow, decimal RunningBalance, bool LiquidityWarning, string WarningDetail);
public sealed record CashForecastResult(DateOnly AsOfDate, IReadOnlyList<CashFlowProjectionPoint> Timeline, IReadOnlyList<string> Warnings);

public sealed record StpStateTransition(Guid InstructionId, StpProcessingState State, DateTimeOffset AtUtc, string Detail, string? BreakQueueReason = null);
