using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;

namespace Meridian.Contracts.Ledger;

/// <summary>Retained identities and explicit policy choices; all lot economics are resolved server-side.</summary>
public sealed record PrepareCanonicalAmortizationRequestDto(
    Guid LedgerBookId, Guid TaxLotRecordId, Guid PeriodId, long ExpectedPeriodVersion,
    AccountingBasisKindDto AccountingBasis, string FundProfileId, DateOnly AsOfDate,
    RetainedEvidenceIdentityDto SecurityEvidence, RetainedEvidenceIdentityDto EventEvidence,
    string AssetAccountPath, string IncomeAccountPath,
    bool AmortizeRemainingFace = false, bool AutomaticYieldToWorst = false,
    DateOnly? ScheduleEndDate = null, string ScheduleFrequency = "Monthly");

public sealed record CanonicalAmortizationPreparationDto(
    OpenLotAmortizationInstructionDto Instruction, OpenLotAmortizationProjectionDto Projection,
    string PreparationFingerprint, IReadOnlyList<ProjectedAccountingEffectLineDto> Lines, AssetAccountingEventScopeDto Scope,
    IReadOnlyList<CanonicalAmortizationOccurrenceDto> Occurrences);

public sealed record CanonicalAmortizationOccurrenceDto(DateOnly Date, decimal TransactionCostBasis, decimal FunctionalCostBasis);

public sealed record CreateCanonicalAmortizationDraftRequestDto(
    PrepareCanonicalAmortizationRequestDto Request, string ExpectedPreparationFingerprint, string Description);
