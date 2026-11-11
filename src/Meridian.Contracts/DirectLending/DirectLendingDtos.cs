using System.Text.Json.Serialization;

namespace Meridian.Contracts.DirectLending;

public enum CurrencyCode : byte
{
    USD = 0,
    EUR = 1,
    GBP = 2,
    JPY = 3,
    Other = 4
}

public enum LoanStatus : byte
{
    Draft = 0,
    Approved = 1,
    Active = 2,
    Suspended = 3,
    Matured = 4,
    Closed = 5,
    Defaulted = 6,
    NonPerforming = 7,
    Workout = 8
}

public enum DayCountBasis : byte
{
    Act360 = 0,
    Act365F = 1,
    Thirty360 = 2,
    ActualActualISDA = 3
}

public enum CollateralType : byte
{
    RealEstate = 0,
    Equipment = 1,
    Inventory = 2,
    AccountsReceivable = 3,
    FinancialInstrument = 4,
    Other = 5
}

public enum RestructuringType : byte
{
    MaturityExtension = 0,
    RateReduction = 1,
    PrincipalHaircut = 2,
    DebtForEquitySwap = 3,
    PikConversion = 4,
    Full = 5
}

public enum RateTypeKind : byte
{
    Fixed = 0,
    Floating = 1
}

public enum PaymentFrequency : byte
{
    Monthly = 0,
    Quarterly = 1,
    SemiAnnual = 2,
    Annual = 3,
    Bullet = 4
}

public enum AmortizationType : byte
{
    Bullet = 0,
    InterestOnly = 1,
    StraightLine = 2,
    CustomSchedule = 3
}

/// <summary>
/// Where the part of a payment left after every amount owed is settled goes. See the PRD-115
/// blueprint (<c>docs/engineering/blueprints/direct-lending-excess-payment-application.md</c>).
/// </summary>
public enum ExcessPaymentApplication : byte
{
    /// <summary>Credit unapplied cash; the operator applies or refunds it later. The default.</summary>
    HoldUnapplied = 0,

    /// <summary>Apply to outstanding principal, subject to the loan's prepayment terms.</summary>
    PrincipalPrepayment = 1,

    /// <summary>Carry as prepaid interest, consumed by later daily accruals.</summary>
    PrepaidInterest = 2
}

public sealed record CollateralDto(
    Guid CollateralId,
    CollateralType CollateralType,
    string Description,
    decimal EstimatedValue,
    CurrencyCode Currency,
    DateOnly AppraisalDate);

public sealed record BorrowerInfoDto(
    Guid BorrowerId,
    string BorrowerName,
    Guid? LegalEntityId);

public sealed record DirectLendingCommandMetadataDto(
    Guid? CommandId,
    Guid? CorrelationId,
    Guid? CausationId,
    string? SourceSystem,
    bool ReplayFlag);

public sealed record DirectLendingCommandEnvelope<TCommand>(
    TCommand Command,
    DirectLendingCommandMetadataDto? Metadata);

public sealed record DirectLendingTermsDto(
    DateOnly OriginationDate,
    DateOnly MaturityDate,
    decimal CommitmentAmount,
    CurrencyCode BaseCurrency,
    RateTypeKind RateTypeKind,
    decimal? FixedAnnualRate,
    string? InterestIndexName,
    decimal? SpreadBps,
    decimal? FloorRate,
    decimal? CapRate,
    DayCountBasis DayCountBasis,
    PaymentFrequency PaymentFrequency,
    AmortizationType AmortizationType,
    decimal? CommitmentFeeRate,
    decimal? DefaultRateSpreadBps,
    bool PrepaymentAllowed,
    string? CovenantsJson,
    int InterestOnlyMonths = 0,
    int? GracePeriodDays = null,
    decimal? EffectiveRateFloor = null,
    decimal? EffectiveRateCap = null,
    decimal? PrepaymentPenaltyRate = null,
    decimal? PurchasePrice = null,
    DirectLendingSecurityMasterReferenceDto? SecurityMasterReference = null)
{
    /// <summary>
    /// How payment excess is applied; <see langword="null"/> (not specified) behaves as
    /// <see cref="DirectLending.ExcessPaymentApplication.HoldUnapplied"/>.
    /// </summary>
    /// <remarks>
    /// Expand-only member of a hashed record. The terms hash is the SHA-256 of this record's JSON, so
    /// the property is an <c>init</c> member omitted from JSON while null: every terms version
    /// recorded before it existed keeps its hash, and setting a rule (a material term) changes it.
    /// Pinned by <c>DirectLendingTermsHashCharacterizationTests</c> and its F# counterpart.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExcessPaymentApplication? ExcessPaymentApplication { get; init; }

    /// <summary>The rule in force, treating "not specified" as <see cref="DirectLending.ExcessPaymentApplication.HoldUnapplied"/>.</summary>
    [JsonIgnore]
    public ExcessPaymentApplication EffectiveExcessPaymentApplication
        => ExcessPaymentApplication ?? DirectLending.ExcessPaymentApplication.HoldUnapplied;

    /// <summary>
    /// Contractual PIK capitalization rule; <see langword="null"/> keeps the legacy PIK toggle,
    /// which capitalizes the whole day's interest into principal on each accrual day.
    /// </summary>
    /// <remarks>Expand-only, hash-stable member: omitted from JSON (and the terms hash) while null.</remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PikCapitalizationPolicy? PikCapitalization { get; init; }

    /// <summary>
    /// Floating-rate reset schedule executed by the daily accrual worker; <see langword="null"/>
    /// keeps manual resets only.
    /// </summary>
    /// <remarks>Expand-only, hash-stable member: omitted from JSON (and the terms hash) while null.</remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FloatingRateResetSchedule? RateResetSchedule { get; init; }
}

public sealed record DirectLendingSecurityMasterReferenceDto(
    Guid SecurityId,
    string Symbol,
    string Provenance,
    string ApprovalReference,
    string LedgerMappingReference,
    string Status = "Active");

public sealed record LoanTermsVersionDto(
    int VersionNumber,
    string TermsHash,
    DirectLendingTermsDto Terms,
    string SourceAction,
    string? AmendmentReason,
    DateTimeOffset RecordedAt);

public sealed record LoanContractDetailDto(
    Guid LoanId,
    string FacilityName,
    BorrowerInfoDto Borrower,
    LoanStatus Status,
    DateOnly EffectiveDate,
    DateOnly? ActivationDate,
    DateOnly? CloseDate,
    int CurrentTermsVersion,
    DirectLendingTermsDto CurrentTerms,
    IReadOnlyList<LoanTermsVersionDto> TermsVersions);

public sealed record LoanEventLineageDto(
    Guid EventId,
    long AggregateVersion,
    string EventType,
    int EventSchemaVersion,
    DateOnly? EffectiveDate,
    DateTimeOffset RecordedAt,
    string PayloadJson,
    Guid? CausationId,
    Guid? CorrelationId,
    Guid? CommandId,
    string? SourceSystem,
    bool ReplayFlag);

public sealed record LoanAggregateSnapshotDto(
    Guid LoanId,
    long AggregateVersion,
    LoanContractDetailDto Contract,
    LoanServicingStateDto Servicing);

/// <summary>
/// Servicing balances for a loan. The first five members are receivables owed by the borrower.
/// <see cref="PrepaidInterest"/> and <see cref="UnappliedCash"/> (PRD-115) are amounts owed
/// <em>to</em> the borrower, so read models must not add them to outstanding exposure. Both
/// default to 0, so state persisted before PRD-115 deserializes unchanged.
/// </summary>
public sealed record OutstandingBalancesDto(
    decimal PrincipalOutstanding,
    decimal InterestAccruedUnpaid,
    decimal CommitmentFeeAccruedUnpaid,
    decimal FeesAccruedUnpaid,
    decimal PenaltyAccruedUnpaid,
    decimal PrepaidInterest = 0m,
    decimal UnappliedCash = 0m);

public sealed record DrawdownLotDto(
    Guid LotId,
    DateOnly DrawdownDate,
    DateOnly SettleDate,
    decimal OriginalPrincipal,
    decimal RemainingPrincipal,
    string? ExternalRef);

public sealed record RateResetDto(
    DateOnly EffectiveDate,
    string IndexName,
    decimal ObservedRate,
    decimal SpreadBps,
    decimal AllInRate,
    string? SourceRef)
{
    /// <summary>Manual (operator request) or Scheduled (reset schedule); null on resets recorded before schedules existed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RateResetKind? ResetKind { get; init; }

    /// <summary>The date the applied fixing was observed (reset date minus lookback) for a scheduled reset.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? FixingObservationDate { get; init; }

    /// <summary>The stored fixing a scheduled reset applied, so a later fixing revision cannot obscure which one was used.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? FixingId { get; init; }

    /// <summary>The revision of <see cref="FixingId"/>'s (index, observation date) that was applied.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FixingRevision { get; init; }
}

public sealed record ServicingRevisionDto(
    long RevisionNumber,
    string RevisionSourceType,
    DateOnly EffectiveAsOfDate,
    DateTimeOffset CreatedAt,
    string Notes);

public sealed record DailyAccrualEntryDto(
    Guid AccrualEntryId,
    DateOnly AccrualDate,
    decimal InterestAmount,
    decimal CommitmentFeeAmount,
    decimal PenaltyAmount,
    decimal AnnualRateApplied,
    DateTimeOffset RecordedAt,
    decimal PikInterestAmount = 0m,
    bool PikCapitalizationDeferred = false);

public sealed record LoanServicingStateDto(
    Guid LoanId,
    LoanStatus Status,
    decimal CurrentCommitment,
    decimal TotalDrawn,
    decimal AvailableToDraw,
    OutstandingBalancesDto Balances,
    IReadOnlyList<DrawdownLotDto> DrawdownLots,
    RateResetDto? CurrentRateReset,
    DateOnly? LastAccrualDate,
    DateOnly? LastPaymentDate,
    long ServicingRevision,
    IReadOnlyList<ServicingRevisionDto> RevisionHistory,
    IReadOnlyList<DailyAccrualEntryDto> AccrualEntries,
    IReadOnlyList<CollateralDto>? Collateral = null,
    decimal UnamortizedDiscount = 0m,
    decimal UnamortizedPremium = 0m,
    bool IsPikToggled = false,
    decimal PikInterestAccruedUnpaid = 0m,
    DateOnly? LastPikCapitalizationDate = null,
    IReadOnlyList<RateResetDto>? RateResetHistory = null,
    IReadOnlyList<PikCapitalizationDto>? PikCapitalizations = null);

public sealed record CreateLoanRequest(
    Guid? LoanId,
    string FacilityName,
    BorrowerInfoDto Borrower,
    DateOnly EffectiveDate,
    DirectLendingTermsDto Terms);

public sealed record AmendLoanTermsRequest(
    DirectLendingTermsDto Terms,
    string AmendmentReason);

public sealed record ActivateLoanRequest(
    DateOnly ActivationDate);

public sealed record BookDrawdownRequest(
    decimal Amount,
    DateOnly TradeDate,
    DateOnly SettleDate,
    string? ExternalRef);

public sealed record ApplyRateResetRequest(
    DateOnly EffectiveDate,
    decimal ObservedRate,
    decimal? SpreadBps,
    string? SourceRef);

public sealed record ApplyPrincipalPaymentRequest(
    decimal Amount,
    DateOnly EffectiveDate,
    string? ExternalRef);

public sealed record PostDailyAccrualRequest(
    DateOnly AccrualDate);

public sealed record ChargePrepaymentPenaltyRequest(
    decimal OutstandingPrincipal,
    DateOnly EffectiveDate,
    string? ExternalRef);

/// <summary>
/// One loan in the portfolio summary. <see cref="PrepaidInterest"/> and <see cref="UnappliedCash"/>
/// (PRD-115) are owed to the borrower and are shown alongside the receivables, but are never added
/// to the portfolio's exposure totals.
/// </summary>
public sealed record LoanSummaryDto(
    Guid LoanId,
    string FacilityName,
    Guid BorrowerId,
    string BorrowerName,
    LoanStatus Status,
    CurrencyCode BaseCurrency,
    decimal CommitmentAmount,
    decimal PrincipalOutstanding,
    decimal InterestAccruedUnpaid,
    decimal PenaltyAccruedUnpaid,
    decimal AvailableToDraw,
    DateOnly OriginationDate,
    DateOnly MaturityDate,
    DateOnly? LastAccrualDate,
    DateOnly? LastPaymentDate,
    decimal PrepaidInterest = 0m,
    decimal UnappliedCash = 0m);

public sealed record LoanPortfolioSummaryDto(
    int TotalLoans,
    int ActiveLoans,
    int DefaultedLoans,
    int NonPerformingLoans,
    int WorkoutLoans,
    decimal? TotalCommitment,
    decimal? TotalPrincipalOutstanding,
    decimal? TotalInterestAccruedUnpaid,
    decimal? TotalPenaltyAccruedUnpaid,
    decimal? TotalAvailableToDraw,
    decimal? TotalCollateralValue,
    IReadOnlyList<LoanSummaryDto> Loans,
    IReadOnlyList<LoanPortfolioCurrencyTotalsDto>? CurrencyTotals = null,
    CurrencyCode? TotalCurrency = null);

/// <summary>
/// Unconverted totals in the retained loan or collateral currency. Portfolio scalar totals
/// are null when more than one currency occurs; no reporting currency or FX rate is implied.
/// Collateral is grouped by its own appraisal currency, independently of facility currency.
/// Other or unsupported currencies retain an unavailable bucket with null amounts, since
/// their common denomination cannot be proved.
/// </summary>
public sealed record LoanPortfolioCurrencyTotalsDto(
    CurrencyCode Currency,
    decimal? TotalCommitment,
    decimal? TotalPrincipalOutstanding,
    decimal? TotalInterestAccruedUnpaid,
    decimal? TotalPenaltyAccruedUnpaid,
    decimal? TotalAvailableToDraw,
    decimal? TotalCollateralValue);

public sealed record DirectLendingOperationsReadModelDto(
    DateTimeOffset AsOf,
    string Status,
    string StatusDetail,
    IReadOnlyList<DirectLendingOperationsLoanHealthDto> LoanHealth,
    IReadOnlyList<DirectLendingOperationsCollateralCoverageDto> CollateralCoverage,
    IReadOnlyList<DirectLendingOperationsStatusPostureDto> CovenantStatusPosture,
    IReadOnlyList<DirectLendingOperationsCalendarItemDto> ServicingCalendar,
    IReadOnlyList<DirectLendingOperationsEvidenceItemDto> Evidence,
    IReadOnlyList<DirectLendingOperationsJournalPostureDto> Journals,
    IReadOnlyList<DirectLendingOperationsReconciliationExceptionDto> ReconciliationExceptions,
    IReadOnlyList<DirectLendingOperationsCloseBlockerDto> CloseBlockers,
    IReadOnlyList<DirectLendingOperationsServicerStatementDto>? ServicerStatements = null,
    Guid? LedgerBookId = null);

public sealed record DirectLendingOperationsLoanHealthDto(
    Guid LoanId,
    string FacilityName,
    string BorrowerName,
    LoanStatus Status,
    CurrencyCode Currency,
    decimal CommitmentAmount,
    decimal PrincipalOutstanding,
    decimal InterestAccruedUnpaid,
    decimal PenaltyAccruedUnpaid,
    DateOnly MaturityDate,
    DateOnly? LastAccrualDate,
    DateOnly? LastPaymentDate,
    string HealthStatus,
    string HealthDetail,
    string Route,
    Guid? SecurityId = null);

public sealed record DirectLendingOperationsCollateralCoverageDto(
    Guid LoanId,
    string FacilityName,
    decimal PrincipalOutstanding,
    decimal CollateralValue,
    decimal CoverageRatio,
    string Status,
    string Detail,
    string Route);

public sealed record DirectLendingOperationsStatusPostureDto(
    Guid LoanId,
    string FacilityName,
    LoanStatus LoanStatus,
    string CovenantStatus,
    bool PikEnabled,
    AmortizationType AmortizationType,
    string RestructurePosture,
    string Detail,
    string Route,
    Guid? SecurityId = null);

public sealed record DirectLendingOperationsCalendarItemDto(
    Guid LoanId,
    string FacilityName,
    string ItemType,
    DateOnly DueDate,
    string Status,
    string Detail,
    string Route);

public sealed record DirectLendingOperationsEvidenceItemDto(
    Guid LoanId,
    string FacilityName,
    string EvidenceType,
    string Label,
    string Status,
    string Route);

public sealed record DirectLendingOperationsJournalPostureDto(
    Guid LoanId,
    string FacilityName,
    int DraftCount,
    int PostedCount,
    string Status,
    string Detail,
    string Route);

public sealed record DirectLendingOperationsReconciliationExceptionDto(
    Guid ExceptionId,
    Guid LoanId,
    string FacilityName,
    string ExceptionType,
    decimal DifferenceAmount,
    string Status,
    string AssignedTo,
    string Detail,
    string Route);

public sealed record DirectLendingOperationsCloseBlockerDto(
    Guid LoanId,
    string FacilityName,
    string BlockerType,
    string Severity,
    string Detail,
    string Route);

public sealed record DirectLendingOperationsServicerStatementDto(
    Guid BatchId,
    ServicerStatementKind Kind,
    string ServicerName,
    DateOnly StatementDate,
    string Status,
    int RowCount,
    int UnmappedRowCount,
    int ExceptionCount,
    int ReadyToApplyRowCount,
    int AppliedRowCount,
    string EvidenceRoute,
    string Detail);

public sealed record AddCollateralRequest(
    CollateralType CollateralType,
    string Description,
    decimal EstimatedValue,
    CurrencyCode Currency,
    DateOnly AppraisalDate);

public sealed record RemoveCollateralRequest(
    Guid CollateralId,
    string Reason);

public sealed record UpdateCollateralValueRequest(
    Guid CollateralId,
    decimal NewEstimatedValue,
    DateOnly NewAppraisalDate);

public sealed record TransitionLoanStatusRequest(
    LoanStatus NewStatus,
    string Reason,
    DateOnly EffectiveDate);

public sealed record TogglePikRequest(
    bool EnablePik,
    DateOnly EffectiveDate,
    string? Reason = null);

public sealed record RestructureLoanRequest(
    RestructuringType RestructuringType,
    string Reason,
    DateOnly EffectiveDate,
    DirectLendingTermsDto? NewTerms = null,
    decimal? HaircutAmount = null);

public sealed record AmortizeDiscountPremiumRequest(
    DateOnly AccrualDate);
