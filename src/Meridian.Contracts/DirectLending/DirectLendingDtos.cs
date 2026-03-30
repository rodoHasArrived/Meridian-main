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
    decimal? PurchasePrice = null);

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

public sealed record OutstandingBalancesDto(
    decimal PrincipalOutstanding,
    decimal InterestAccruedUnpaid,
    decimal CommitmentFeeAccruedUnpaid,
    decimal FeesAccruedUnpaid,
    decimal PenaltyAccruedUnpaid);

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
    string? SourceRef);

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
    DateTimeOffset RecordedAt);

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
    bool IsPikToggled = false);

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
    DateOnly? LastPaymentDate);

public sealed record LoanPortfolioSummaryDto(
    int TotalLoans,
    int ActiveLoans,
    int DefaultedLoans,
    int NonPerformingLoans,
    int WorkoutLoans,
    decimal TotalCommitment,
    decimal TotalPrincipalOutstanding,
    decimal TotalInterestAccruedUnpaid,
    decimal TotalPenaltyAccruedUnpaid,
    decimal TotalAvailableToDraw,
    decimal TotalCollateralValue,
    IReadOnlyList<LoanSummaryDto> Loans);

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
