using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Contracts.Accounting.Lots;

[JsonConverter(typeof(JsonStringEnumConverter<BondIncomeFloaterPolicy>))]
public enum BondIncomeFloaterPolicy { AcquisitionIndex, ProspectiveReset }

[JsonConverter(typeof(JsonStringEnumConverter<BondIncomeFeeTreatment>))]
public enum BondIncomeFeeTreatment { EligibleDirectAcquisitionCost, Expense, WithholdingTax }

/// <summary>Explicit book policy, read from AccountingPolicyDto.RulesJson.bondIncome.</summary>
public sealed record BondIncomePolicyDto(
    string ModelVersion,
    BondIncomeFloaterPolicy FloaterPolicy,
    bool CapitalizeEligibleDirectCosts,
    bool RecognizeObservedIndexation,
    bool AmortizeQualifyingCallablePremium,
    string? AmortizationIncomeAccountPath = null)
{
    public string BookedTargetPrecisionVersion => BondIncomeBookedPrecision.PolicyVersion;
}

public sealed record BondIncomeFeeDto(string SourceReference, decimal Amount,
    BondIncomeFeeTreatment Treatment, RetainedEvidenceIdentityDto Evidence);

/// <summary>Reviewed acquisition facts retained on the authoritative lot, never supplied to a preview.</summary>
public sealed record BondIncomeAcquisitionDto(DateTimeOffset KnownAt, decimal CleanConsideration,
    decimal PurchasedAccruedInterest, decimal IndexRatio, IReadOnlyList<BondIncomeFeeDto> Fees,
    IReadOnlyList<RetainedEvidenceIdentityDto> Evidence);

/// <summary>A retained frozen-index schedule at an observed contractual reset.</summary>
public sealed record BondIncomeResetDto(DateOnly Date, BondWorkflowResult Projection);
public sealed record BondIncomeCallDto(DateOnly Date, decimal PricePer100);

/// <summary>
/// Server-prepared, immutable calculation inputs. This is not posting authority; the canonical
/// posting boundary resolves and locks its lot, book, position and Security Master again.
/// </summary>
public sealed record BondIncomeInstructionDto(
    Guid LedgerBookId,
    string AccountingPolicyId,
    string AccountingPolicyVersion,
    BondIncomePolicyDto Policy,
    DateOnly AcquisitionDate,
    DateOnly ThroughDate,
    DateTimeOffset KnownAt,
    BondWorkflowResult AcquisitionProjection,
    BondWorkflowResult CurrentProjection,
    BondWorkflowResult AmortizationProjection,
    IReadOnlyList<BondIncomeResetDto> Resets,
    IReadOnlyList<BondIncomeCallDto> Calls,
    IReadOnlyList<BondIncomeFeeDto> Fees,
    decimal AcquisitionAccruedInterest,
    decimal CurrentAccruedInterest,
    decimal AcquisitionIndexRatio,
    decimal CurrentIndexRatio,
    decimal PrincipalForIndexation,
    IReadOnlyList<RetainedEvidenceIdentityDto> Evidence,
    Guid AccountId = default,
    int PaymentsPerYear = 1,
    bool LevelCoupon = false,
    DayCountConvention DayCount = DayCountConvention.Actual365,
    bool ZeroCoupon = false)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BondIncomeCouponAllocationDto>? CouponCashAllocations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BondIncomeBookedTargetDto? BookedTarget { get; init; }
}

/// <summary>Signed transaction-currency changes, separate from cash settlement or market P&amp;L.</summary>
public sealed record BondIncomeResultDto(
    string ModelVersion,
    BondIncomeFloaterPolicy FloaterPolicy,
    decimal EffectiveAnnualYield,
    decimal OpeningCarryingBasis,
    decimal ClosingCarryingBasis,
    decimal CouponIncome,
    decimal PremiumDiscountIncome,
    decimal IndexationIncome,
    decimal ScheduledPrincipal,
    decimal CapitalizedFees,
    decimal ExpensedFees,
    decimal WithholdingTax,
    decimal InterestReceivable,
    decimal InterestPayable,
    IReadOnlyList<BondIncomeJournalComponentDto> JournalComponents)
{
    /// <summary>Coupon, premium/discount and indexation income are cumulative from this date.</summary>
    public DateOnly CumulativeIncomeFromDate { get; init; }
    /// <summary>The incremental movement from the current authoritative carrying basis.</summary>
    public decimal CarryingBasisMovement => ClosingCarryingBasis - OpeningCarryingBasis;
    /// <summary>Allocated native coupon cash less raw completed coupon economics.</summary>
    public decimal CouponCashRoundingAdjustment { get; init; }
    public BondIncomeBookedTargetDto? BookedTarget { get; init; }
    public decimal? BookedCarryingBasisMovement => BookedTarget?.BookedTransactionTarget - OpeningCarryingBasis;
    public decimal TotalAccountingIncome => CouponIncome + PremiumDiscountIncome + IndexationIncome;
}

/// <summary>Named economic legs to map through a governed book rule pack; not executable journals.</summary>
public sealed record BondIncomeJournalComponentDto(string Component, decimal Debit, decimal Credit);

public sealed record BondIncomePreviewRequestDto(Guid LedgerBookId, Guid TaxLotRecordId,
    DateOnly ThroughDate, DateTimeOffset KnownAt, Guid AccountId = default);

public sealed record BondIncomePreviewDto(Guid LedgerBookId, Guid TaxLotRecordId,
    string Fingerprint, OpenLotAmortizationInstructionDto Instruction, BondIncomeResultDto Income,
    AssetLotMutationInstructionDto LotMutation)
{
    public AssetAccountingPostingCandidateDto? PreparedCandidate { get; init; }
    public BondIncomePreparationArtifactDto? PreparedArtifact { get; init; }
}

public sealed record BondIncomePreparationRequestDto(BondIncomePreviewRequestDto Request,
    string ExpectedFingerprint, Guid? PeriodId = null);

public sealed record BondIncomePreparationArtifactDto(string TenantId, string CompanyId, Guid AccountId,
    Guid LedgerBookId, Guid PeriodId, Guid EventId, long EventVersion, string PreviewFingerprint,
    OpenLotAmortizationInstructionDto Instruction)
{
    public string Kind => "SystemDerivedPrepared";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BondCouponRecognitionInstructionDto? CouponRecognition { get; init; }
}

/// <summary>Acceptance of an exact prepared calculation, never independent journal approval.</summary>
public sealed record BondIncomeOperatorFingerprintReviewDto(string Actor, string PreviewFingerprint,
    DateTimeOffset ReviewedAtUtc);

public interface IBondIncomePreparationEvidenceStore
{
    Task<RetainedEvidenceIdentityDto> RetainAsync(BondIncomePreparationArtifactDto artifact, string actor,
        DateTimeOffset preparedAtUtc, bool mustExist, CancellationToken ct = default);
    Task VerifyAsync(RetainedEvidenceIdentityDto expected, OpenLotAmortizationInstructionDto instruction,
        Guid periodId, Guid eventId, long eventVersion, CancellationToken ct = default);
}

public interface IBondIncomeService
{
    Task VerifyPreparedSourceAsync(OpenLotAmortizationInstructionDto instruction,
        EconomicEventReferenceDto economicEvent, Guid periodId, IReadOnlyList<RetainedEvidenceIdentityDto> evidence,
        CancellationToken ct = default);
    Task RequireAcquisitionPolicyAsync(Guid ledgerBookId, DateOnly acquiredDate,
        BondIncomeAcquisitionDto acquisition, decimal retainedCost, CancellationToken ct = default);
    Task<BondIncomePreviewDto> PreviewAsync(BondIncomePreviewRequestDto request, CancellationToken ct = default);
    /// <summary>Rebuilds from server authorities and refuses stale review before returning canonical input.</summary>
    Task<BondIncomePreviewDto> PrepareAsync(BondIncomePreparationRequestDto request, CancellationToken ct = default);
}
