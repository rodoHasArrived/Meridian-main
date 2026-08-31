using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;

namespace Meridian.Instruments.AssetOperations;

/// <summary>
/// Versioned interpretation of the supplied Clearwater corporate-action methodology. This is a
/// projection profile, not GAAP, statutory, or tax authority. Policy-dependent branches remain
/// explicit dependencies so callers cannot mistake a vendor default for an approved conclusion.
/// </summary>
public static class ClearwaterCorporateActionRuleProfileV1
{
    public const string ProfileId = "clearwater-corporate-actions";
    public const string ProfileVersion = "v1";
    public const string ProfileKey = ProfileId + "/" + ProfileVersion;
    public const string SelectedRuleVersion = "v1";

    /// <summary>
    /// Meridian adoption date for this captured methodology profile. It is not an assertion about
    /// when Clearwater's underlying methodology first became effective.
    /// </summary>
    public static readonly DateOnly EffectiveFrom = new(2026, 8, 25);

    public static bool IsEffectiveOn(DateOnly evaluationDate) => evaluationDate >= EffectiveFrom;

    private const string VendorAuthorityCaveat =
        "Clearwater methodology is a source-proposed treatment and not final GAAP, statutory, or tax authority.";

    public static CorporateActionTreatmentDecisionDto Resolve(
        CorporateActionAccountingTypeDto actionType,
        AccountingBasisKindDto accountingBasis)
    {
        if (!Enum.IsDefined(actionType))
        {
            throw new ArgumentOutOfRangeException(nameof(actionType));
        }

        if (!Enum.IsDefined(accountingBasis))
        {
            throw new ArgumentOutOfRangeException(nameof(accountingBasis));
        }

        return actionType switch
        {
            CorporateActionAccountingTypeDto.RegS144AExchange => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.StraightThroughCandidate,
                CorporateActionTaxClassificationDto.PreliminaryNonTaxable,
                CorporateActionBookValueTreatmentDto.CarryOver,
                CorporateActionHoldingPeriodTreatmentDto.CarryOver,
                [CorporateActionEconomicOperationKindDto.ExchangeOut, CorporateActionEconomicOperationKindDto.ExchangeIn],
                caveats: ["Cash, fees, or changed economics invalidate straight-through treatment."]),

            CorporateActionAccountingTypeDto.AdvanceRefunding => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.PreliminaryNonTaxable,
                CorporateActionBookValueTreatmentDto.Allocate,
                CorporateActionHoldingPeriodTreatmentDto.CarryOver,
                [CorporateActionEconomicOperationKindDto.ExchangeOut, CorporateActionEconomicOperationKindDto.ExchangeIn],
                caveats:
                [
                    "The refunded and unrefunded successors require an approved allocation.",
                    "Under the supplied methodology only the refunded successor is tracked on Schedule D."
                ]),

            CorporateActionAccountingTypeDto.BankruptcyDistribution => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ManualReview,
                CorporateActionTaxClassificationDto.Unknown,
                CorporateActionBookValueTreatmentDto.PolicyDefined,
                CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
                [],
                [CorporateActionPolicyDependencyDto.BankruptcyMethod],
                ["The supplied methodology explicitly has no standard bankruptcy treatment."]),

            CorporateActionAccountingTypeDto.CallRedemption => CallDecision(actionType, accountingBasis),

            CorporateActionAccountingTypeDto.ConsentSolicitation => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.PreliminaryTaxable,
                CorporateActionBookValueTreatmentDto.Unchanged,
                CorporateActionHoldingPeriodTreatmentDto.Unchanged,
                [CorporateActionEconomicOperationKindDto.OtherIncome],
                caveats:
                [
                    "A material modification or extinguishment assessment is outside the supplied methodology.",
                    "A consent event without a holder payment is reference-only."
                ]),

            CorporateActionAccountingTypeDto.DebtToEquityConversion => ConversionDecision(actionType, accountingBasis),

            CorporateActionAccountingTypeDto.CashDividend => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.StraightThroughCandidate,
                CorporateActionTaxClassificationDto.PreliminaryTaxable,
                CorporateActionBookValueTreatmentDto.Unchanged,
                CorporateActionHoldingPeriodTreatmentDto.Unchanged,
                [CorporateActionEconomicOperationKindDto.DividendIncome]),

            CorporateActionAccountingTypeDto.StockDividend => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.Unknown,
                CorporateActionBookValueTreatmentDto.PolicyDefined,
                CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
                [CorporateActionEconomicOperationKindDto.SplitAdjustment],
                [CorporateActionPolicyDependencyDto.StockDividendBasisTreatment],
                ["The source calls processing split-like while also acknowledging different accounting treatment."]),

            CorporateActionAccountingTypeDto.DividendReinvestment => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.PreliminaryTaxable,
                CorporateActionBookValueTreatmentDto.NewPurchase,
                CorporateActionHoldingPeriodTreatmentDto.NewLot,
                [CorporateActionEconomicOperationKindDto.DividendIncome, CorporateActionEconomicOperationKindDto.Purchase],
                caveats: ["Gross dividend income and the new purchase remain distinct even when net cash is zero."]),

            CorporateActionAccountingTypeDto.ScripDividend => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.Unknown,
                CorporateActionBookValueTreatmentDto.PolicyDefined,
                CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
                [CorporateActionEconomicOperationKindDto.TransferIn, CorporateActionEconomicOperationKindDto.TransferOut],
                [CorporateActionPolicyDependencyDto.ScripDividendTreatment],
                ["Scrip basis and the ultimate distribution treatment are not supplied."]),

            CorporateActionAccountingTypeDto.ExchangeOffer => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ManualReview,
                CorporateActionTaxClassificationDto.FactDependent,
                CorporateActionBookValueTreatmentDto.PolicyDefined,
                CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
                [],
                [
                    CorporateActionPolicyDependencyDto.ExchangeOfferMethod,
                    CorporateActionPolicyDependencyDto.ExchangeOfferMaterialityAssessment,
                    CorporateActionPolicyDependencyDto.ExchangeOfferTaxAssessment
                ],
                ["The supplied 10% cash-flow and tax tests do not define their formulas or boundary conventions."]),

            CorporateActionAccountingTypeDto.FractionalCashInLieu => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.Unknown,
                CorporateActionBookValueTreatmentDto.Dispose,
                CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
                [CorporateActionEconomicOperationKindDto.CorporateActionSale],
                caveats: ["Tax characterization and fractional-lot allocation are not supplied."]),

            CorporateActionAccountingTypeDto.MergerStock => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.PreliminaryNonTaxable,
                CorporateActionBookValueTreatmentDto.CarryOver,
                CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
                [CorporateActionEconomicOperationKindDto.ExchangeOut, CorporateActionEconomicOperationKindDto.ExchangeIn],
                [CorporateActionPolicyDependencyDto.HoldingPeriodInstruction],
                ["Holding-period carryover is not stated in the supplied merger methodology."]),

            CorporateActionAccountingTypeDto.MergerCash => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.Unknown,
                CorporateActionBookValueTreatmentDto.Dispose,
                CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
                [CorporateActionEconomicOperationKindDto.CorporateActionSale],
                caveats: ["The source describes ordinary-sale accounting but does not state a tax conclusion."]),

            CorporateActionAccountingTypeDto.MergerMixed => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ManualReview,
                CorporateActionTaxClassificationDto.FactDependent,
                CorporateActionBookValueTreatmentDto.PolicyDefined,
                CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
                [
                    CorporateActionEconomicOperationKindDto.ExchangeOut,
                    CorporateActionEconomicOperationKindDto.ExchangeIn,
                    CorporateActionEconomicOperationKindDto.CashFromCorporateAction
                ],
                [CorporateActionPolicyDependencyDto.MergerRecognition],
                ["The source offers capped-at-cash and full-recognition models without selection criteria."]),

            CorporateActionAccountingTypeDto.NameIdentifierChange => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.StraightThroughCandidate,
                CorporateActionTaxClassificationDto.Unknown,
                CorporateActionBookValueTreatmentDto.CarryOver,
                CorporateActionHoldingPeriodTreatmentDto.CarryOver,
                [CorporateActionEconomicOperationKindDto.ExchangeOut, CorporateActionEconomicOperationKindDto.ExchangeIn],
                caveats: ["Straight-through treatment requires evidence that no economics changed."]),

            CorporateActionAccountingTypeDto.PaymentInKind => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.Unknown,
                CorporateActionBookValueTreatmentDto.NewPurchase,
                CorporateActionHoldingPeriodTreatmentDto.NewLot,
                [CorporateActionEconomicOperationKindDto.CouponIncome, CorporateActionEconomicOperationKindDto.Purchase],
                caveats: ["Tax treatment and any fair-value departure from the source's par convention are unresolved."]),

            CorporateActionAccountingTypeDto.PutRedemption => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.PreliminaryTaxable,
                CorporateActionBookValueTreatmentDto.Dispose,
                CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
                [CorporateActionEconomicOperationKindDto.Redemption]),

            CorporateActionAccountingTypeDto.ReturnOfCapital => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.PreliminaryNonTaxable,
                CorporateActionBookValueTreatmentDto.Reduce,
                CorporateActionHoldingPeriodTreatmentDto.Unchanged,
                [CorporateActionEconomicOperationKindDto.ReturnOfCapital],
                [CorporateActionPolicyDependencyDto.ExcessReturnOfCapitalTreatment],
                ["The source does not define treatment after carrying value or tax basis reaches zero."]),

            CorporateActionAccountingTypeDto.ReverseStockSplit => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.StraightThroughCandidate,
                CorporateActionTaxClassificationDto.PreliminaryNonTaxable,
                CorporateActionBookValueTreatmentDto.CarryOver,
                CorporateActionHoldingPeriodTreatmentDto.CarryOver,
                [CorporateActionEconomicOperationKindDto.SplitAdjustment],
                caveats: ["An identifier change changes the recipe to a book-value-conserving exchange."]),

            CorporateActionAccountingTypeDto.RightsDistribution => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ManualReview,
                CorporateActionTaxClassificationDto.PreliminaryNonTaxable,
                CorporateActionBookValueTreatmentDto.Unchanged,
                CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
                [CorporateActionEconomicOperationKindDto.TransferIn],
                [CorporateActionPolicyDependencyDto.RightsValuation],
                ["The zero-value convention reflects a Clearwater pricing limitation."]),

            CorporateActionAccountingTypeDto.RightsExercise => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.PreliminaryNonTaxable,
                CorporateActionBookValueTreatmentDto.NewPurchase,
                CorporateActionHoldingPeriodTreatmentDto.NewLot,
                [CorporateActionEconomicOperationKindDto.TransferOut, CorporateActionEconomicOperationKindDto.Purchase]),

            CorporateActionAccountingTypeDto.RightsExpiration => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ConditionalApproval,
                CorporateActionTaxClassificationDto.Unknown,
                CorporateActionBookValueTreatmentDto.PolicyDefined,
                CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
                [CorporateActionEconomicOperationKindDto.TransferOut],
                caveats: ["The supplied methodology does not state the tax consequence of expiration."]),

            CorporateActionAccountingTypeDto.SpinOff => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.ManualReview,
                CorporateActionTaxClassificationDto.FactDependent,
                CorporateActionBookValueTreatmentDto.PolicyDefined,
                CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
                [],
                [CorporateActionPolicyDependencyDto.SpinOffTaxTreatment],
                ["Holding-period treatment and the issuer basis-allocation method must be supplied."]),

            CorporateActionAccountingTypeDto.StockSplit => Decision(
                actionType,
                accountingBasis,
                CorporateActionAutomationDispositionDto.StraightThroughCandidate,
                CorporateActionTaxClassificationDto.PreliminaryNonTaxable,
                CorporateActionBookValueTreatmentDto.Unchanged,
                CorporateActionHoldingPeriodTreatmentDto.Unchanged,
                [CorporateActionEconomicOperationKindDto.SplitAdjustment],
                caveats: ["Fractional residuals require a separate cash-in-lieu event."]),

            CorporateActionAccountingTypeDto.TenderOffer => TenderDecision(actionType, accountingBasis),

            _ => throw new ArgumentOutOfRangeException(nameof(actionType))
        };
    }

    private static CorporateActionTreatmentDecisionDto CallDecision(
        CorporateActionAccountingTypeDto actionType,
        AccountingBasisKindDto accountingBasis)
        => Decision(
            actionType,
            accountingBasis,
            CorporateActionAutomationDispositionDto.ConditionalApproval,
            accountingBasis == AccountingBasisKindDto.Statutory
                ? CorporateActionTaxClassificationDto.Unknown
                : CorporateActionTaxClassificationDto.PreliminaryTaxable,
            CorporateActionBookValueTreatmentDto.Dispose,
            CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
            accountingBasis == AccountingBasisKindDto.Statutory
                ?
                [
                    CorporateActionEconomicOperationKindDto.Redemption,
                    CorporateActionEconomicOperationKindDto.CouponIncome,
                    CorporateActionEconomicOperationKindDto.PrepaymentPenaltyIncome
                ]
                : [CorporateActionEconomicOperationKindDto.Redemption, CorporateActionEconomicOperationKindDto.CouponIncome],
            caveats:
            [
                "Unamortized premium or discount treatment is not supplied.",
                "Make-whole decomposition applies only to the statutory branch."
            ]);

    private static CorporateActionTreatmentDecisionDto ConversionDecision(
        CorporateActionAccountingTypeDto actionType,
        AccountingBasisKindDto accountingBasis)
        => Decision(
            actionType,
            accountingBasis,
            accountingBasis == AccountingBasisKindDto.Statutory
                ? CorporateActionAutomationDispositionDto.ManualReview
                : CorporateActionAutomationDispositionDto.ConditionalApproval,
            CorporateActionTaxClassificationDto.PreliminaryNonTaxable,
            CorporateActionBookValueTreatmentDto.PolicyDefined,
            CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
            [CorporateActionEconomicOperationKindDto.ExchangeOut, CorporateActionEconomicOperationKindDto.ExchangeIn],
            accountingBasis == AccountingBasisKindDto.Statutory
                ? [CorporateActionPolicyDependencyDto.StatutoryConversionTreatment]
                : [],
            ["The phrase 'except in the statutory accounting basis' does not define the statutory treatment."]);

    private static CorporateActionTreatmentDecisionDto TenderDecision(
        CorporateActionAccountingTypeDto actionType,
        AccountingBasisKindDto accountingBasis)
        => Decision(
            actionType,
            accountingBasis,
            CorporateActionAutomationDispositionDto.ConditionalApproval,
            CorporateActionTaxClassificationDto.PreliminaryTaxable,
            CorporateActionBookValueTreatmentDto.Dispose,
            CorporateActionHoldingPeriodTreatmentDto.PolicyDefined,
            accountingBasis == AccountingBasisKindDto.Statutory
                ? [CorporateActionEconomicOperationKindDto.Redemption, CorporateActionEconomicOperationKindDto.OtherIncome]
                : [CorporateActionEconomicOperationKindDto.CorporateActionSale],
            accountingBasis == AccountingBasisKindDto.Statutory
                ? [CorporateActionPolicyDependencyDto.StatutoryTenderAllocation]
                : [],
            ["Accrued interest and client-specific below-par allocation require explicit inputs."]);

    private static CorporateActionTreatmentDecisionDto Decision(
        CorporateActionAccountingTypeDto actionType,
        AccountingBasisKindDto accountingBasis,
        CorporateActionAutomationDispositionDto automationDisposition,
        CorporateActionTaxClassificationDto taxClassification,
        CorporateActionBookValueTreatmentDto bookValueTreatment,
        CorporateActionHoldingPeriodTreatmentDto holdingPeriodTreatment,
        IReadOnlyList<CorporateActionEconomicOperationKindDto> recipe,
        IReadOnlyList<CorporateActionPolicyDependencyDto>? policyDependencies = null,
        IReadOnlyList<string>? caveats = null)
        => new(
            actionType,
            accountingBasis,
            new AccountingRulePackReferenceDto(
                ProfileId,
                ProfileVersion,
                $"clearwater.{actionType}",
                SelectedRuleVersion),
            EffectiveFrom,
            automationDisposition,
            taxClassification,
            bookValueTreatment,
            holdingPeriodTreatment,
            recipe,
            policyDependencies ?? [],
            (caveats ?? []).Append(VendorAuthorityCaveat).ToArray());
}
