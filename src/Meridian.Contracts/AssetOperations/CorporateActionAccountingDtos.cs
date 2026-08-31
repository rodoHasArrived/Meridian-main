using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.AssetOperations;

/// <summary>
/// Accounting-oriented corporate-action vocabulary. It is intentionally more granular than the
/// Security Master event vocabulary because one announced event can require materially different
/// economic recipes.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionAccountingTypeDto>))]
public enum CorporateActionAccountingTypeDto
{
    RegS144AExchange = 0,
    AdvanceRefunding = 1,
    BankruptcyDistribution = 2,
    CallRedemption = 3,
    ConsentSolicitation = 4,
    DebtToEquityConversion = 5,
    CashDividend = 6,
    StockDividend = 7,
    DividendReinvestment = 8,
    ScripDividend = 9,
    ExchangeOffer = 10,
    FractionalCashInLieu = 11,
    MergerStock = 12,
    MergerCash = 13,
    MergerMixed = 14,
    NameIdentifierChange = 15,
    PaymentInKind = 16,
    PutRedemption = 17,
    ReturnOfCapital = 18,
    ReverseStockSplit = 19,
    RightsDistribution = 20,
    RightsExercise = 21,
    RightsExpiration = 22,
    SpinOff = 23,
    StockSplit = 24,
    TenderOffer = 25
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionAutomationDispositionDto>))]
public enum CorporateActionAutomationDispositionDto
{
    StraightThroughCandidate = 0,
    ConditionalApproval = 1,
    ManualReview = 2
}

/// <summary>
/// Preliminary classification from the selected rule profile. It is not a legal tax conclusion.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionTaxClassificationDto>))]
public enum CorporateActionTaxClassificationDto
{
    Unknown = 0,
    PreliminaryNonTaxable = 1,
    PreliminaryTaxable = 2,
    FactDependent = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionBookValueTreatmentDto>))]
public enum CorporateActionBookValueTreatmentDto
{
    Unchanged = 0,
    CarryOver = 1,
    Allocate = 2,
    Dispose = 3,
    Reduce = 4,
    NewPurchase = 5,
    PolicyDefined = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionHoldingPeriodTreatmentDto>))]
public enum CorporateActionHoldingPeriodTreatmentDto
{
    Unchanged = 0,
    CarryOver = 1,
    NewLot = 2,
    PolicyDefined = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionPolicyDependencyDto>))]
public enum CorporateActionPolicyDependencyDto
{
    BankruptcyMethod = 0,
    StatutoryConversionTreatment = 1,
    StockDividendBasisTreatment = 2,
    ScripDividendTreatment = 3,
    ExchangeOfferMethod = 4,
    ExchangeOfferMaterialityAssessment = 5,
    ExchangeOfferTaxAssessment = 6,
    CashRecognition = 7,
    MergerRecognition = 8,
    HoldingPeriodInstruction = 9,
    RightsValuation = 10,
    SpinOffTaxTreatment = 11,
    ExcessReturnOfCapitalTreatment = 12,
    StatutoryTenderAllocation = 13
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionBankruptcyMethodDto>))]
public enum CorporateActionBankruptcyMethodDto
{
    TransferOutAtZero = 0,
    CashOnlySale = 1,
    EscrowExchange = 2,
    SecuritiesAndCashExchange = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionExchangeOfferMethodDto>))]
public enum CorporateActionExchangeOfferMethodDto
{
    DirectExchange = 0,
    SaleAndPurchase = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionCashRecognitionDto>))]
public enum CorporateActionCashRecognitionDto
{
    Gain = 0,
    Income = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionMergerRecognitionDto>))]
public enum CorporateActionMergerRecognitionDto
{
    GainLimitedToCashNoLoss = 0,
    FullGainLoss = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionSpinOffTaxTreatmentDto>))]
public enum CorporateActionSpinOffTaxTreatmentDto
{
    NonTaxableBasisAllocation = 0,
    TaxableDividendAndPurchase = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionStockDividendBasisTreatmentDto>))]
public enum CorporateActionStockDividendBasisTreatmentDto
{
    PreserveTotalCarryingValue = 0,
    PolicyDefinedAdjustment = 1
}

/// <summary>
/// Explicit operator or promoted-policy selections. Null means no conclusion was supplied and a
/// rule that depends on the value must fail closed.
/// </summary>
public sealed record CorporateActionPolicyInputsDto(
    CorporateActionBankruptcyMethodDto? BankruptcyMethod = null,
    CorporateActionExchangeOfferMethodDto? ExchangeOfferMethod = null,
    CorporateActionCashRecognitionDto? CashRecognition = null,
    CorporateActionMergerRecognitionDto? MergerRecognition = null,
    CorporateActionSpinOffTaxTreatmentDto? SpinOffTaxTreatment = null,
    CorporateActionStockDividendBasisTreatmentDto? StockDividendBasisTreatment = null,
    CorporateActionTaxClassificationDto? ApprovedTaxClassification = null,
    bool? ExchangeOfferIsMaterial = null,
    bool StatutoryConversionTreatmentApproved = false,
    bool ScripDividendTreatmentApproved = false,
    bool RightsZeroValueApproved = false,
    bool? CarryHoldingPeriod = null,
    decimal? StatutoryTenderIncomeAllocationPercent = null,
    bool? ConsentTermsChanged = null,
    bool ConsentModificationAssessmentApproved = false,
    decimal? ApprovedCashRecognitionAmount = null,
    decimal? ApprovedSuccessorBasis = null,
    Guid? ScripFinalDistributionCaseId = null,
    Guid? FractionalCashInLieuCaseId = null);

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionSuccessorRoleDto>))]
public enum CorporateActionSuccessorRoleDto
{
    Successor = 0,
    Refunded = 1,
    Unrefunded = 2,
    Acquirer = 3,
    Child = 4,
    Escrow = 5,
    Underlying = 6,
    Right = 7,
    Scrip = 8
}

public sealed record CorporateActionSuccessorAllocationDto(
    Guid SecurityId,
    CorporateActionSuccessorRoleDto Role,
    decimal Quantity,
    decimal? BookValueAllocationPercent = null,
    decimal? FairValue = null);

/// <summary>
/// Normalized economics used by the accounting projector. Optionality is type-specific; the
/// projector validates the exact fields required for the selected action before producing intent.
/// Gross cash consideration includes separately identified accrued income when the source amount
/// includes it.
/// </summary>
public sealed record CorporateActionEconomicsDto(
    decimal? PositionQuantity = null,
    decimal? AffectedQuantity = null,
    decimal? CarryingAmount = null,
    decimal? ParAmount = null,
    decimal? GrossCashConsideration = null,
    decimal? AccruedIncome = null,
    decimal? Rate = null,
    decimal? CashRatePerUnit = null,
    decimal? SplitRatio = null,
    decimal? DistributionRatio = null,
    decimal? PurchasePricePerUnit = null,
    decimal? SubscriptionPricePerUnit = null,
    bool IdentifierChanged = false,
    bool IsMakeWhole = false,
    bool IsPartial = false,
    IReadOnlyList<CorporateActionSuccessorAllocationDto>? Successors = null)
{
    public IReadOnlyList<CorporateActionSuccessorAllocationDto> Successors { get; init; } =
        Successors ?? [];
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionEconomicOperationKindDto>))]
public enum CorporateActionEconomicOperationKindDto
{
    ExchangeOut = 0,
    ExchangeIn = 1,
    Redemption = 2,
    CorporateActionSale = 3,
    CashFromCorporateAction = 4,
    CouponIncome = 5,
    DividendIncome = 6,
    OtherIncome = 7,
    PrepaymentPenaltyIncome = 8,
    Purchase = 9,
    TransferIn = 10,
    TransferOut = 11,
    ReturnOfCapital = 12,
    SplitAdjustment = 13,
    ReferenceDataChange = 14
}

/// <summary>An economic instruction, not a general-ledger line.</summary>
public sealed record CorporateActionEconomicOperationDto(
    CorporateActionEconomicOperationKindDto Kind,
    Guid? SecurityId = null,
    CorporateActionSuccessorRoleDto? SuccessorRole = null,
    decimal? Quantity = null,
    decimal? Amount = null,
    string? Description = null,
    Guid? LinkedCaseId = null);

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionLotMutationKindDto>))]
public enum CorporateActionLotMutationKindDto
{
    CarryOver = 0,
    Allocate = 1,
    Dispose = 2,
    Acquire = 3,
    ChangeQuantity = 4,
    ReduceCarryingValue = 5,
    TransferOut = 6,
    TransferIn = 7
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionLotTargetOperationDto>))]
public enum CorporateActionLotTargetOperationDto
{
    Create = 0,
    Update = 1
}

/// <summary>
/// Exact before/after state for one lot under the projection's accounting basis. Basis amount is
/// retained separately from carrying value because the two measures can diverge.
/// </summary>
public sealed record CorporateActionLotStateSnapshotDto(
    decimal Quantity,
    decimal CarryingValue,
    decimal BasisAmount);

/// <summary>
/// Lot intent plus the authoritative lot identities, complete state transitions, and version guards
/// required before posting. Source state is absent only for an inbound acquisition. A target is
/// absent only when the mutation changes or relieves the source lot in place. Target creation and
/// update are explicit so an existing lot can never be mistaken for a newly created lot. Storage
/// must still compare-and-swap update versions and commit the journal and lot changes atomically.
/// For source-to-target transformations, Quantity/CarryingAmount/BasisAmount are the target
/// contribution and SourceQuantity/SourceCarryingAmount/SourceBasisAmount are the source relief
/// contribution. Source-only kinds use Quantity/CarryingAmount/BasisAmount directly.
/// </summary>
public sealed record CorporateActionLotMutationDto(
    CorporateActionLotMutationKindDto Kind,
    Guid SecurityId,
    Guid? TargetSecurityId = null,
    decimal? Quantity = null,
    decimal? CarryingAmount = null,
    decimal? AllocationPercent = null,
    CorporateActionHoldingPeriodTreatmentDto HoldingPeriodTreatment =
        CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
    string? Description = null,
    Guid? LinkedCaseId = null,
    IReadOnlyList<string>? ReportingTags = null,
    Guid? SourceLotId = null,
    long? ExpectedSourceLotVersion = null,
    CorporateActionLotStateSnapshotDto? SourceBefore = null,
    CorporateActionLotStateSnapshotDto? SourceAfter = null,
    Guid? TargetLotId = null,
    CorporateActionLotTargetOperationDto? TargetOperation = null,
    long? ExpectedTargetLotVersion = null,
    CorporateActionLotStateSnapshotDto? TargetBefore = null,
    CorporateActionLotStateSnapshotDto? TargetAfter = null,
    decimal? BasisAmount = null,
    decimal? SourceQuantity = null,
    decimal? SourceCarryingAmount = null,
    decimal? SourceBasisAmount = null)
{
    public IReadOnlyList<string> ReportingTags { get; init; } = ReportingTags ?? [];
}

public sealed record CorporateActionLotMutationSetDto(
    Guid PositionId,
    long ExpectedPositionVersion,
    IReadOnlyList<CorporateActionLotMutationDto>? Mutations = null)
{
    public IReadOnlyList<CorporateActionLotMutationDto> Mutations { get; init; } = Mutations ?? [];

    public bool RequiresAuthoritativeLotResolution => Mutations.Count > 0;

    public bool HasAuthoritativeLotResolution =>
        CorporateActionLotMutationPlanValidator.Validate(Mutations).Count == 0;
}

public static class CorporateActionLotMutationPlanValidator
{
    public static IReadOnlyList<CorporateActionProjectionBlockerDto> Validate(
        IReadOnlyList<CorporateActionLotMutationDto> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);

        var blockers = new List<CorporateActionProjectionBlockerDto>();
        for (var index = 0; index < mutations.Count; index++)
        {
            var mutation = mutations[index];
            var prefix = $"Lot mutation {index + 1}";
            if (!Enum.IsDefined(mutation.Kind))
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-kind-invalid",
                    $"{prefix} has an undefined mutation kind."));
                continue;
            }

            if (mutation.SecurityId == Guid.Empty)
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-security-required",
                    $"{prefix} requires an exact Security Master identity."));
            }

            ValidateMutationShape(mutation, index, prefix, blockers);
        }

        ValidateSourceGroups(mutations, blockers);
        ValidateTargetGroups(mutations, blockers);

        return blockers;
    }

    private static void ValidateMutationShape(
        CorporateActionLotMutationDto mutation,
        int index,
        string prefix,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        var requiresSource = mutation.Kind is not CorporateActionLotMutationKindDto.Acquire and
            not CorporateActionLotMutationKindDto.TransferIn;
        var requiresTarget = mutation.Kind is
            CorporateActionLotMutationKindDto.CarryOver or
            CorporateActionLotMutationKindDto.Allocate or
            CorporateActionLotMutationKindDto.Acquire or
            CorporateActionLotMutationKindDto.TransferIn;

        ValidateSource(mutation, index, prefix, requiresSource, blockers);
        ValidateTarget(mutation, index, prefix, requiresTarget, blockers);
        ValidateMutationAmounts(mutation, prefix, blockers);
    }

    private static void ValidateSource(
        CorporateActionLotMutationDto mutation,
        int index,
        string prefix,
        bool requiresSource,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        var hasAnySourceState = mutation.SourceLotId.HasValue ||
                                mutation.ExpectedSourceLotVersion.HasValue ||
                                mutation.SourceBefore is not null ||
                                mutation.SourceAfter is not null;
        if (!requiresSource)
        {
            if (hasAnySourceState)
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-source-not-permitted",
                    $"{prefix} is inbound-only and cannot mutate a source lot."));
            }

            return;
        }

        if (mutation.SourceLotId is not { } sourceLotId || sourceLotId == Guid.Empty)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.lot-mutation-source-required",
                $"{prefix} requires an exact source-lot identity."));
        }

        if (mutation.ExpectedSourceLotVersion is not > 0)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.lot-mutation-source-version-required",
                $"{prefix} requires a positive expected source-lot version."));
        }

        if (mutation.SourceBefore is not { } before)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.lot-mutation-before-snapshot-required",
                $"{prefix} requires the source lot's before snapshot."));
        }
        else
        {
            ValidateSnapshot(before, index, "source-before", blockers);
        }

        if (mutation.SourceAfter is { } after)
        {
            ValidateSnapshot(after, index, "source-after", blockers);
        }
        else if (mutation.Kind is CorporateActionLotMutationKindDto.ChangeQuantity or
                 CorporateActionLotMutationKindDto.ReduceCarryingValue)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.lot-mutation-source-after-snapshot-required",
                $"{prefix} updates the source lot and requires its after snapshot."));
        }
    }

    private static void ValidateTarget(
        CorporateActionLotMutationDto mutation,
        int index,
        string prefix,
        bool requiresTarget,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        var hasAnyTargetState = mutation.TargetLotId.HasValue ||
                                mutation.TargetOperation.HasValue ||
                                mutation.ExpectedTargetLotVersion.HasValue ||
                                mutation.TargetBefore is not null ||
                                mutation.TargetAfter is not null;
        if (!requiresTarget)
        {
            if (hasAnyTargetState)
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-target-not-permitted",
                    $"{prefix} mutates only its source lot and cannot carry a target transition."));
            }

            ValidateSourceOnlyTransition(mutation, prefix, blockers);
            return;
        }

        if (mutation.TargetLotId is not { } targetLotId || targetLotId == Guid.Empty)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.lot-mutation-target-required",
                $"{prefix} requires an exact target-lot identity."));
        }

        if (mutation.SourceLotId.HasValue && mutation.SourceLotId == mutation.TargetLotId)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.lot-mutation-source-target-overlap",
                $"{prefix} must represent a source-to-target transformation with distinct lot identities."));
        }

        if (!mutation.TargetOperation.HasValue || !Enum.IsDefined(mutation.TargetOperation.Value))
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.lot-mutation-target-operation-required",
                $"{prefix} requires an explicit Create or Update target operation."));
        }
        else if (mutation.TargetOperation.Value == CorporateActionLotTargetOperationDto.Create)
        {
            if (mutation.ExpectedTargetLotVersion.HasValue)
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-target-version-not-permitted",
                    $"{prefix} creates a target lot and cannot carry an expected target version."));
            }

            if (mutation.TargetBefore is not null)
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-target-before-not-permitted",
                    $"{prefix} creates a target lot and cannot carry a target-before snapshot."));
            }
        }
        else
        {
            if (mutation.ExpectedTargetLotVersion is not > 0)
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-target-version-required",
                    $"{prefix} updates a target lot and requires its positive expected version."));
            }

            if (mutation.TargetBefore is not { } targetBefore)
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-target-before-required",
                    $"{prefix} updates a target lot and requires its before snapshot."));
            }
            else
            {
                ValidateSnapshot(targetBefore, index, "target-before", blockers);
            }
        }

        if (mutation.TargetAfter is not { } targetAfter)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.lot-mutation-after-snapshot-required",
                $"{prefix} requires the target lot's after snapshot."));
        }
        else
        {
            ValidateSnapshot(targetAfter, index, "target-after", blockers);
        }
    }

    private static void ValidateMutationAmounts(
        CorporateActionLotMutationDto mutation,
        string prefix,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, mutation.BasisAmount is null or < 0m,
            "corporate-action.lot-mutation-basis-amount-required",
            $"{prefix} requires an explicit non-negative basis amount.");

        var isSourceToTarget = mutation.Kind is CorporateActionLotMutationKindDto.CarryOver or
            CorporateActionLotMutationKindDto.Allocate;
        if (isSourceToTarget)
        {
            AddIf(blockers, mutation.SourceQuantity is not > 0m,
                "corporate-action.lot-mutation-source-quantity-required",
                $"{prefix} requires its positive allocated source quantity.");
            AddIf(blockers, mutation.SourceCarryingAmount is null or < 0m,
                "corporate-action.lot-mutation-source-carrying-amount-required",
                $"{prefix} requires its non-negative allocated source carrying relief.");
            AddIf(blockers, mutation.SourceBasisAmount is null or < 0m,
                "corporate-action.lot-mutation-source-basis-amount-required",
                $"{prefix} requires its non-negative allocated source basis relief.");
        }
        else if (mutation.SourceQuantity.HasValue ||
                 mutation.SourceCarryingAmount.HasValue ||
                 mutation.SourceBasisAmount.HasValue)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.lot-mutation-source-allocation-not-permitted",
                $"{prefix} does not use source-allocation amounts."));
        }

        if (mutation.Kind == CorporateActionLotMutationKindDto.ReduceCarryingValue)
        {
            AddIf(blockers, mutation.CarryingAmount is not > 0m,
                "corporate-action.lot-mutation-carrying-amount-required",
                $"{prefix} requires a positive carrying-value reduction.");
            AddIf(blockers, mutation.Quantity.HasValue,
                "corporate-action.lot-mutation-quantity-not-permitted",
                $"{prefix} changes carrying value without changing quantity.");
            return;
        }

        AddIf(blockers, mutation.Quantity is not > 0m,
            "corporate-action.lot-mutation-quantity-required",
            $"{prefix} requires a positive quantity.");

        if (mutation.Kind != CorporateActionLotMutationKindDto.ChangeQuantity)
        {
            AddIf(blockers, mutation.CarryingAmount is null or < 0m,
                "corporate-action.lot-mutation-carrying-amount-required",
                $"{prefix} requires a non-negative carrying amount.");
        }
    }

    private static void ValidateSourceOnlyTransition(
        CorporateActionLotMutationDto mutation,
        string prefix,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        if (mutation.SourceBefore is not { } before)
        {
            return;
        }

        var after = mutation.SourceAfter;
        switch (mutation.Kind)
        {
            case CorporateActionLotMutationKindDto.Dispose:
            case CorporateActionLotMutationKindDto.TransferOut:
                var disposedQuantity = before.Quantity - (after?.Quantity ?? 0m);
                var relievedCarryingValue = before.CarryingValue - (after?.CarryingValue ?? 0m);
                var relievedBasis = before.BasisAmount - (after?.BasisAmount ?? 0m);
                if (disposedQuantity <= 0m || relievedCarryingValue < 0m || relievedBasis < 0m ||
                    mutation.Quantity != disposedQuantity ||
                    mutation.CarryingAmount != relievedCarryingValue ||
                    mutation.BasisAmount != relievedBasis)
                {
                    blockers.Add(new CorporateActionProjectionBlockerDto(
                        "corporate-action.lot-mutation-disposal-delta-invalid",
                        $"{prefix} quantity, carrying amount, and basis amount must equal the source before/after relief."));
                }

                if (after is null && mutation.Quantity != before.Quantity)
                {
                    blockers.Add(new CorporateActionProjectionBlockerDto(
                        "corporate-action.lot-mutation-partial-disposal-after-snapshot-required",
                        $"{prefix} is not an explicitly reconciled full disposal and requires a source-after snapshot."));
                }
                break;

            case CorporateActionLotMutationKindDto.ChangeQuantity:
                if (after is not null &&
                    (mutation.Quantity != after.Quantity ||
                     before.CarryingValue != after.CarryingValue ||
                     before.BasisAmount != after.BasisAmount ||
                     (mutation.CarryingAmount.HasValue && mutation.CarryingAmount != after.CarryingValue) ||
                     mutation.BasisAmount != 0m))
                {
                    blockers.Add(new CorporateActionProjectionBlockerDto(
                        "corporate-action.lot-mutation-quantity-change-invalid",
                        $"{prefix} must set the exact after quantity without changing carrying value or basis."));
                }
                break;

            case CorporateActionLotMutationKindDto.ReduceCarryingValue:
                if (after is not null &&
                    (before.Quantity != after.Quantity ||
                     before.CarryingValue - after.CarryingValue != mutation.CarryingAmount ||
                     before.BasisAmount - after.BasisAmount != mutation.BasisAmount))
                {
                    blockers.Add(new CorporateActionProjectionBlockerDto(
                        "corporate-action.lot-mutation-carrying-reduction-invalid",
                        $"{prefix} must preserve quantity and reconcile carrying-value and basis reductions."));
                }
                break;
        }
    }

    private static void ValidateSourceGroups(
        IReadOnlyList<CorporateActionLotMutationDto> mutations,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        foreach (var group in mutations
                     .Where(static mutation => mutation.SourceLotId.HasValue)
                     .GroupBy(static mutation => mutation.SourceLotId!.Value))
        {
            var first = group.First();
            if (group.Any(mutation =>
                    mutation.SecurityId != first.SecurityId ||
                    mutation.ExpectedSourceLotVersion != first.ExpectedSourceLotVersion ||
                    mutation.SourceBefore != first.SourceBefore ||
                    mutation.SourceAfter != first.SourceAfter))
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-source-transition-conflict",
                    $"Source lot {group.Key:D} has conflicting versions or before/after snapshots."));
                continue;
            }

            var entries = group.ToArray();
            var sourceToTargetEntries = entries.Where(static mutation =>
                    (mutation.Kind is CorporateActionLotMutationKindDto.CarryOver or
                        CorporateActionLotMutationKindDto.Allocate))
                .ToArray();
            if (sourceToTargetEntries.Length > 0 && sourceToTargetEntries.Length != entries.Length)
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-source-kind-conflict",
                    $"Source lot {group.Key:D} cannot mix source-to-target and source-only mutations in one plan."));
                continue;
            }

            if (sourceToTargetEntries.Length == 0)
            {
                if (entries.Length > 1)
                {
                    blockers.Add(new CorporateActionProjectionBlockerDto(
                        "corporate-action.lot-mutation-source-transition-conflict",
                        $"Source lot {group.Key:D} has more than one in-place or disposal transition."));
                }

                continue;
            }

            if (first.SourceBefore is not { } before)
            {
                continue;
            }

            var after = first.SourceAfter;
            var quantityRelief = before.Quantity - (after?.Quantity ?? 0m);
            var carryingRelief = before.CarryingValue - (after?.CarryingValue ?? 0m);
            var basisRelief = before.BasisAmount - (after?.BasisAmount ?? 0m);
            decimal expectedQuantity;
            decimal expectedCarryingValue;
            decimal expectedBasis;
            try
            {
                expectedQuantity = sourceToTargetEntries.Sum(static mutation => mutation.SourceQuantity ?? 0m);
                expectedCarryingValue = sourceToTargetEntries.Sum(static mutation =>
                    mutation.SourceCarryingAmount ?? 0m);
                expectedBasis = sourceToTargetEntries.Sum(static mutation => mutation.SourceBasisAmount ?? 0m);
            }
            catch (OverflowException)
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-source-delta-overflow",
                    $"Source lot {group.Key:D} aggregate quantity, carrying value, or basis exceeds the supported range."));
                continue;
            }

            if (quantityRelief <= 0m || carryingRelief < 0m || basisRelief < 0m ||
                quantityRelief != expectedQuantity ||
                carryingRelief != expectedCarryingValue ||
                basisRelief != expectedBasis)
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-source-transition-invalid",
                    $"Source lot {group.Key:D} before/after relief must equal its aggregate source quantity, carrying, and basis amounts."));
            }
        }
    }

    private static void ValidateTargetGroups(
        IReadOnlyList<CorporateActionLotMutationDto> mutations,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        foreach (var group in mutations
                     .Where(static mutation => mutation.TargetLotId.HasValue)
                     .GroupBy(static mutation => mutation.TargetLotId!.Value))
        {
            var entries = group.ToArray();
            var first = entries[0];
            if (entries.Any(mutation =>
                    (mutation.TargetSecurityId ?? mutation.SecurityId) !=
                    (first.TargetSecurityId ?? first.SecurityId) ||
                    mutation.TargetOperation != first.TargetOperation ||
                    mutation.ExpectedTargetLotVersion != first.ExpectedTargetLotVersion ||
                    mutation.TargetBefore != first.TargetBefore ||
                    mutation.TargetAfter != first.TargetAfter))
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-target-transition-conflict",
                    $"Target lot {group.Key:D} has conflicting operations, versions, or before/after snapshots."));
                continue;
            }

            if (!first.TargetOperation.HasValue || first.TargetAfter is null)
            {
                continue;
            }

            var operation = first.TargetOperation.Value;
            var after = first.TargetAfter!;
            var before = operation == CorporateActionLotTargetOperationDto.Create
                ? new CorporateActionLotStateSnapshotDto(0m, 0m, 0m)
                : first.TargetBefore;
            if (before is null)
            {
                continue;
            }

            decimal expectedQuantity;
            decimal expectedCarryingValue;
            decimal expectedBasis;
            try
            {
                expectedQuantity = entries.Sum(static mutation => mutation.Quantity ?? 0m);
                expectedCarryingValue = entries.Sum(static mutation => mutation.CarryingAmount ?? 0m);
                expectedBasis = entries.Sum(static mutation => mutation.BasisAmount ?? 0m);
            }
            catch (OverflowException)
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-target-delta-overflow",
                    $"Target lot {group.Key:D} aggregate quantity, carrying value, or basis exceeds the supported range."));
                continue;
            }

            var quantityIncrease = after.Quantity - before.Quantity;
            var carryingIncrease = after.CarryingValue - before.CarryingValue;
            var basisIncrease = after.BasisAmount - before.BasisAmount;
            if (quantityIncrease <= 0m || carryingIncrease < 0m || basisIncrease < 0m ||
                quantityIncrease != expectedQuantity ||
                carryingIncrease != expectedCarryingValue ||
                basisIncrease != expectedBasis)
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-target-delta-invalid",
                    $"Target lot {group.Key:D} before/after delta must equal the aggregate mutation quantity, carrying amount, and basis amount."));
            }
        }
    }

    private static void ValidateSnapshot(
        CorporateActionLotStateSnapshotDto snapshot,
        int index,
        string state,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        if (snapshot.Quantity < 0m || snapshot.CarryingValue < 0m || snapshot.BasisAmount < 0m)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.lot-mutation-snapshot-invalid",
                $"Lot mutation {index + 1} has a negative {state} quantity, carrying value, or basis amount."));
        }
    }

    private static void AddIf(
        ICollection<CorporateActionProjectionBlockerDto> blockers,
        bool condition,
        string code,
        string message)
    {
        if (condition)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(code, message));
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionPostingComponentKindDto>))]
public enum CorporateActionPostingComponentKindDto
{
    Cash = 0,
    RedemptionPrincipal = 1,
    AccruedIncome = 2,
    DividendIncome = 3,
    ConsentIncome = 4,
    PrepaymentPenaltyIncome = 5,
    InvestmentIncome = 6,
    RealizedGain = 7,
    RealizedLoss = 8,
    ReturnOfCapital = 9,
    PurchaseCost = 10,
    CarryingValueRelief = 11
}

/// <summary>
/// Basis-aware amount awaiting promoted rule-pack account mapping. Amounts are positive; economic
/// direction is conveyed by the component kind. These components are not balanced journal lines.
/// </summary>
public sealed record CorporateActionPostingComponentDto(
    CorporateActionPostingComponentKindDto Kind,
    decimal Amount,
    string Currency,
    string? Description = null);

public sealed record CorporateActionPostingSetDto(
    AccountingBasisKindDto AccountingBasis,
    string Currency,
    bool RequiresJournalCandidate,
    IReadOnlyList<CorporateActionPostingComponentDto>? Components = null)
{
    public IReadOnlyList<CorporateActionPostingComponentDto> Components { get; init; } = Components ?? [];
}

/// <summary>Exact book boundary whose facts participate in a corporate-action projection hash.</summary>
public sealed record CorporateActionAccountingProjectionScopeDto(
    string TenantId,
    string CompanyId,
    string FundProfileId,
    Guid LedgerBookId,
    Guid PeriodId,
    long ExpectedPeriodVersion,
    string Jurisdiction);

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionProjectionEvidenceRoleDto>))]
public enum CorporateActionProjectionEvidenceRoleDto
{
    SourceEvent = 0,
    PositionSnapshot = 1,
    LotSnapshot = 2,
    Election = 3,
    PolicyDecision = 4
}

/// <summary>
/// Typed evidence dependency. URI, hash, version, and subject stay paired when projection identity
/// is calculated; independently sorted URI/hash lists are deliberately not accepted.
/// </summary>
public sealed record CorporateActionProjectionEvidenceDependencyDto(
    CorporateActionProjectionEvidenceRoleDto Role,
    string EvidenceId,
    string EvidenceUri,
    string ContentHashSha256,
    long EvidenceVersion,
    string SubjectType,
    string SubjectId);

public sealed record CorporateActionPostingComponentLineAllocationDto(
    int EffectLineIndex,
    decimal ComponentAmount);

public sealed record CorporateActionPostingComponentLineMappingDto(
    int ComponentIndex,
    CorporateActionPostingComponentKindDto ComponentKind,
    IReadOnlyList<CorporateActionPostingComponentLineAllocationDto>? Allocations = null,
    string? MappingRole = null)
{
    public IReadOnlyList<CorporateActionPostingComponentLineAllocationDto> Allocations { get; init; } =
        Allocations ?? [];
}

/// <summary>
/// Output of a promoted accounting rule mapper. The attestation binds exact posting intent,
/// selected rule/version, scope, generated lines, and the component-to-line reconciliation map.
/// </summary>
public sealed record CorporateActionMappedAccountingEffectDto(
    ProjectedAccountingEffectDto Effect,
    AccountingRulePackReferenceDto AccountingRulePack,
    string PostingIntentHash,
    string MappingHash,
    IReadOnlyList<CorporateActionPostingComponentLineMappingDto>? ComponentLineMappings = null)
{
    public IReadOnlyList<CorporateActionPostingComponentLineMappingDto> ComponentLineMappings { get; init; } =
        ComponentLineMappings ?? [];
}

/// <summary>
/// Basis-specific conclusion selected from one versioned methodology profile. The source
/// methodology remains an assertion; downstream accounting and tax policy own final authority.
/// </summary>
public sealed record CorporateActionTreatmentDecisionDto(
    CorporateActionAccountingTypeDto ActionType,
    AccountingBasisKindDto AccountingBasis,
    AccountingRulePackReferenceDto RuleProfile,
    DateOnly RuleProfileEffectiveFrom,
    CorporateActionAutomationDispositionDto AutomationDisposition,
    CorporateActionTaxClassificationDto PreliminaryTaxClassification,
    CorporateActionBookValueTreatmentDto BookValueTreatment,
    CorporateActionHoldingPeriodTreatmentDto HoldingPeriodTreatment,
    IReadOnlyList<CorporateActionEconomicOperationKindDto>? DefaultRecipe = null,
    IReadOnlyList<CorporateActionPolicyDependencyDto>? PolicyDependencies = null,
    IReadOnlyList<string>? Caveats = null)
{
    public IReadOnlyList<CorporateActionEconomicOperationKindDto> DefaultRecipe { get; init; } =
        DefaultRecipe ?? [];

    public IReadOnlyList<CorporateActionPolicyDependencyDto> PolicyDependencies { get; init; } =
        PolicyDependencies ?? [];

    public IReadOnlyList<string> Caveats { get; init; } = Caveats ?? [];
}

[JsonConverter(typeof(JsonStringEnumConverter<CorporateActionProjectionStatusDto>))]
public enum CorporateActionProjectionStatusDto
{
    Blocked = 0,
    Projected = 1
}

public sealed record CorporateActionProjectionBlockerDto(string Code, string Message);

/// <summary>
/// Projection output upstream of Financial Operations. It carries no durable journal authority.
/// </summary>
public sealed record CorporateActionAccountingProjectionDto(
    CorporateActionProjectionStatusDto Status,
    CorporateActionTreatmentDecisionDto Treatment,
    decimal EventAmount,
    EconomicEventReferenceDto? EconomicEvent,
    ProjectionLineageDto? ProjectionLineage,
    IReadOnlyList<CorporateActionEconomicOperationDto>? Recipe = null,
    CorporateActionLotMutationSetDto? LotMutations = null,
    CorporateActionPostingSetDto? PostingSet = null,
    IReadOnlyList<CorporateActionProjectionBlockerDto>? Blockers = null,
    Guid CaseId = default,
    long CaseVersion = 0,
    long? ElectionVersion = null,
    long PolicyDecisionVersion = 0,
    Guid PositionSnapshotId = default,
    string? ProjectionInputHash = null,
    string? PostingIntentHash = null,
    CorporateActionAccountingProjectionScopeDto? AccountingScope = null,
    IReadOnlyList<CorporateActionProjectionEvidenceDependencyDto>? EvidenceManifest = null,
    Guid LotSnapshotId = default,
    long LotSnapshotVersion = 0,
    Guid PolicyDecisionId = default,
    Guid? ElectionId = null)
{
    public IReadOnlyList<CorporateActionEconomicOperationDto> Recipe { get; init; } = Recipe ?? [];

    public IReadOnlyList<CorporateActionProjectionBlockerDto> Blockers { get; init; } = Blockers ?? [];

    public IReadOnlyList<CorporateActionProjectionEvidenceDependencyDto> EvidenceManifest { get; init; } =
        EvidenceManifest ?? [];

    public bool CanPreparePostingCandidate =>
        Status == CorporateActionProjectionStatusDto.Projected &&
        PostingSet is { RequiresJournalCandidate: true } &&
        LotMutations is { HasAuthoritativeLotResolution: true } &&
        Blockers.Count == 0;
}

/// <summary>
/// Result of adapting a projected corporate action into the shared event-spine request. The lot
/// and posting sets remain reviewable intent; this contract does not append a journal.
/// </summary>
public sealed record CorporateActionAssetAccountingEventProjectionDto(
    ProjectAssetAccountingEventRequestDto Event,
    CorporateActionTreatmentDecisionDto Treatment,
    CorporateActionLotMutationSetDto LotMutations,
    CorporateActionPostingSetDto PostingSet,
    AccountingRulePackReferenceDto AppliedAccountingRulePack,
    string PostingIdempotencyKey);
