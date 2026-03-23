namespace Meridian.Contracts.DirectLending;

public sealed record PaymentBreakdownDto(
    decimal ToInterest,
    decimal ToCommitmentFee,
    decimal ToFees,
    decimal ToPenalty,
    decimal ToPrincipal)
{
    public decimal TotalAllocated => ToInterest + ToCommitmentFee + ToFees + ToPenalty + ToPrincipal;
}

public sealed record MixedPaymentResolutionDto(
    PaymentBreakdownDto Breakdown,
    string ResolutionBasis,
    string? RuleVersion,
    decimal UnappliedAmount);

public sealed record ApplyMixedPaymentRequest(
    decimal Amount,
    DateOnly EffectiveDate,
    PaymentBreakdownDto? Breakdown,
    string? ExternalRef);

public sealed record AssessFeeRequest(
    string FeeType,
    decimal Amount,
    DateOnly EffectiveDate,
    string? Note);

public sealed record ApplyWriteOffRequest(
    decimal Amount,
    DateOnly EffectiveDate,
    string Reason);

public sealed record CashTransactionDto(
    Guid CashTransactionId,
    Guid LoanId,
    string TransactionType,
    DateOnly EffectiveDate,
    DateOnly TransactionDate,
    DateOnly SettlementDate,
    decimal Amount,
    CurrencyCode Currency,
    string? ExternalRef,
    Guid SourceEventId,
    DateTimeOffset RecordedAt,
    bool IsVoided);

public sealed record PaymentAllocationDto(
    Guid AllocationId,
    Guid LoanId,
    Guid CashTransactionId,
    int AllocationSequenceNumber,
    string TargetType,
    string TargetReference,
    decimal AllocatedAmount,
    string AllocationRule,
    Guid SourceEventId,
    DateTimeOffset CreatedAt);

public sealed record FeeBalanceDto(
    Guid FeeBalanceId,
    Guid LoanId,
    string FeeType,
    DateOnly EffectiveDate,
    decimal OriginalAmount,
    decimal UnpaidAmount,
    Guid SourceEventId,
    string? Note,
    DateTimeOffset CreatedAt);

public enum ProjectionRunStatus : byte
{
    Requested = 0,
    Completed = 1,
    Failed = 2,
    Superseded = 3
}

public sealed record RequestProjectionRunRequest(
    DateOnly ProjectionAsOf,
    DateOnly? MarketDataAsOf,
    string? TriggerType,
    string? EngineVersion);

public sealed record ProjectionRunDto(
    Guid ProjectionRunId,
    Guid LoanId,
    int LoanTermsVersion,
    long ServicingRevision,
    DateOnly ProjectionAsOf,
    DateOnly? MarketDataAsOf,
    Guid? TriggerEventId,
    string TriggerType,
    string TermsHash,
    string EngineVersion,
    ProjectionRunStatus Status,
    Guid? SupersedesProjectionRunId,
    DateTimeOffset GeneratedAt);

public sealed record ProjectedCashFlowDto(
    Guid ProjectedCashFlowId,
    Guid ProjectionRunId,
    Guid LoanId,
    int FlowSequenceNumber,
    string FlowType,
    DateOnly DueDate,
    DateOnly? AccrualStartDate,
    DateOnly? AccrualEndDate,
    decimal Amount,
    CurrencyCode Currency,
    decimal? PrincipalBasis,
    decimal? AnnualRate,
    string? FormulaTraceJson,
    DateTimeOffset CreatedAt);

public enum JournalEntryStatus : byte
{
    Draft = 0,
    Posted = 1,
    Failed = 2
}

public sealed record JournalLineDto(
    Guid JournalLineId,
    int LineNumber,
    string AccountCode,
    decimal DebitAmount,
    decimal CreditAmount,
    CurrencyCode Currency,
    string? DimensionsJson);

public sealed record JournalEntryDto(
    Guid JournalEntryId,
    Guid? LoanId,
    DateOnly AccountingDate,
    DateOnly EffectiveDate,
    Guid SourceEventId,
    string EntryType,
    string LedgerBasis,
    string Description,
    DateTimeOffset RecordedAt,
    DateTimeOffset? PostedAt,
    JournalEntryStatus Status,
    IReadOnlyList<JournalLineDto> Lines);

public sealed record AccountingPeriodLockDto(
    string LedgerBasis,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    string Status,
    string? LockedBy,
    DateTimeOffset? LockedAt,
    string? ReopenedBy,
    DateTimeOffset? ReopenedAt,
    string? Reason);

public sealed record ReconcileLoanRequest(
    decimal AmountTolerance,
    int DateToleranceDays);

public sealed record ReconciliationRunDto(
    Guid ReconciliationRunId,
    Guid LoanId,
    Guid? ProjectionRunId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    string Status);

public sealed record ReconciliationResultDto(
    Guid ReconciliationResultId,
    Guid ReconciliationRunId,
    Guid LoanId,
    Guid? ProjectedCashFlowId,
    Guid? CashTransactionId,
    string MatchStatus,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal? VarianceAmount,
    DateOnly? ExpectedDate,
    DateOnly? ActualDate,
    string? MatchRule,
    string? ToleranceJson,
    IReadOnlyList<string> Notes,
    DateTimeOffset CreatedAt);

public sealed record ReconciliationExceptionDto(
    Guid ExceptionId,
    Guid ReconciliationResultId,
    string ExceptionType,
    string Severity,
    string Status,
    string? AssignedTo,
    string? ResolutionNote,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

public sealed record ResolveReconciliationExceptionRequest(
    string ResolutionNote,
    string AssignedTo);

public sealed record CreateServicerReportBatchRequest(
    string ServicerName,
    string ReportType,
    string SourceFormat,
    DateOnly ReportAsOfDate,
    string? FileName,
    string? FileHash,
    string? Notes,
    IReadOnlyList<ServicerPositionReportLineImportDto>? PositionLines,
    IReadOnlyList<ServicerTransactionReportLineImportDto>? TransactionLines);

public sealed record ServicerReportBatchDto(
    Guid ServicerReportBatchId,
    string ServicerName,
    string ReportType,
    string SourceFormat,
    DateOnly ReportAsOfDate,
    DateTimeOffset ReceivedAt,
    string? FileName,
    string? FileHash,
    int RowCount,
    string Status,
    string? LoadedBy,
    string? Notes);

public sealed record ServicerPositionReportLineImportDto(
    Guid LoanId,
    decimal? PrincipalOutstanding,
    decimal? InterestAccruedUnpaid,
    decimal? FeesAccruedUnpaid,
    decimal? PenaltyAccruedUnpaid,
    decimal? CommitmentAvailable,
    DateOnly? NextDueDate,
    decimal? NextDueAmount,
    string? DelinquencyStatus,
    string RawPayloadJson);

public sealed record ServicerPositionReportLineDto(
    Guid ServicerReportLineId,
    Guid ServicerReportBatchId,
    Guid LoanId,
    DateOnly ReportAsOfDate,
    decimal? PrincipalOutstanding,
    decimal? InterestAccruedUnpaid,
    decimal? FeesAccruedUnpaid,
    decimal? PenaltyAccruedUnpaid,
    decimal? CommitmentAvailable,
    DateOnly? NextDueDate,
    decimal? NextDueAmount,
    string? DelinquencyStatus,
    string RawPayloadJson);

public sealed record ServicerTransactionReportLineImportDto(
    Guid LoanId,
    string TransactionType,
    DateOnly EffectiveDate,
    DateOnly? TransactionDate,
    DateOnly? SettlementDate,
    decimal GrossAmount,
    decimal? PrincipalAmount,
    decimal? InterestAmount,
    decimal? FeeAmount,
    decimal? PenaltyAmount,
    CurrencyCode? Currency,
    string? ExternalRef,
    string RawPayloadJson);

public sealed record ServicerTransactionReportLineDto(
    Guid ServicerTransactionLineId,
    Guid ServicerReportBatchId,
    Guid LoanId,
    string? ServicerTransactionId,
    string TransactionType,
    DateOnly EffectiveDate,
    DateOnly? TransactionDate,
    DateOnly? SettlementDate,
    decimal GrossAmount,
    decimal? PrincipalAmount,
    decimal? InterestAmount,
    decimal? FeeAmount,
    decimal? PenaltyAmount,
    CurrencyCode? Currency,
    string? ExternalRef,
    string RawPayloadJson);

public sealed record DirectLendingOutboxMessageDto(
    Guid OutboxMessageId,
    string Topic,
    string MessageKey,
    string PayloadJson,
    DateTimeOffset OccurredAt,
    DateTimeOffset VisibleAfter,
    DateTimeOffset? ProcessedAt,
    int ErrorCount,
    string? LastError);

public sealed record DirectLendingReplayCheckpointDto(
    string ProjectionName,
    long LastProcessedPosition,
    Guid? LastEventId,
    DateTimeOffset? LastRebuiltAt,
    string Status,
    string? Details);

public sealed record RebuildCheckpointDto(
    string ProjectionName,
    long LastProcessedPosition,
    Guid? LastEventId,
    DateTimeOffset? LastRebuiltAt,
    string Status,
    string? Details);

public sealed record ReplayDirectLendingRequest(
    bool RecomputeDerivedArtifacts);

public sealed record ReplayDirectLendingResultDto(
    int LoansProcessed,
    int EventsProcessed,
    DateTimeOffset CompletedAt);
