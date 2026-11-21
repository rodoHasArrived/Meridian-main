using Meridian.Contracts.AssetOperations;

namespace Meridian.Instruments.AssetOperations;

public sealed partial class CorporateActionAccountingProjectionService
{
    private static ProjectionComputation BindCanonicalRedemptionLots(
        ProjectionComputation computation, IReadOnlyList<CorporateActionLotMutationDto> lots,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        var intent = computation.LotMutations.Single();
        try
        {
            if (lots.Any(lot => lot.Kind != CorporateActionLotMutationKindDto.Dispose
                    || lot.SecurityId != intent.SecurityId || lot.TargetLotId is not null || lot.TargetSecurityId is not null
                    || lot.SourceAfter is not null)
                || lots.Sum(lot => lot.Quantity ?? 0m) != intent.Quantity
                || lots.Sum(lot => lot.CarryingAmount ?? 0m) != intent.CarryingAmount)
                blockers.Add(new CorporateActionProjectionBlockerDto("corporate-action.redemption-lot-intent-mismatch",
                    "Canonical redemption lots must fully close and sum to the projected security quantity and carrying basis."));
            foreach (var blocker in CorporateActionLotMutationPlanValidator.Validate(lots))
                blockers.Add(blocker);
            return blockers.Count == 0 ? computation with { LotMutations = lots.ToArray() } : computation;
        }
        catch (OverflowException)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto("corporate-action.amount-overflow",
                "The canonical redemption lot quantities or bases exceed the supported decimal range."));
            return computation;
        }
    }

    private static ProjectionComputation ProjectCanonicalRedemption(
        CorporateActionAccountingProjectionRequest request, string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, request.ActionType is not (CorporateActionAccountingTypeDto.CallRedemption
            or CorporateActionAccountingTypeDto.PutRedemption) || request.CanonicalLotTransferJournal
            || request.Economics.AccruedIncome is not (null or 0m) || request.Economics.IsMakeWhole,
            "corporate-action.canonical-redemption-unsupported",
            "Canonical redemption supports principal-only calls and puts; accrued or make-whole income requires a separate reviewed workflow.");
        return blockers.Count > 0 ? ProjectionComputation.Empty
            : ProjectDisposition(request, currency, blockers, CorporateActionEconomicOperationKindDto.Redemption);
    }

    private static ProjectionComputation ProjectCanonicalSplitTransfer(
        CorporateActionAccountingProjectionRequest request, string currency,
        List<CorporateActionProjectionBlockerDto> blockers, decimal positionQuantity, decimal splitRatio)
    {
        var quantity = positionQuantity * splitRatio;
        AddIf(blockers, request.PolicyInputs.CarryHoldingPeriod != true
            || request.Economics.CarryingAmount is not > 0m
            || request.Economics.GrossCashConsideration is > 0m
            || request.PolicyInputs.FractionalCashInLieuCaseId is not null
            || decimal.Truncate(quantity) != quantity,
            "corporate-action.canonical-split-treatment-required",
            "Canonical split lot posting requires reviewed carrying basis, carried holding period and whole-unit successors; cash-in-lieu requires a separate workflow.");
        AddIf(blockers, request.Economics.Successors.Count != 1
            || request.Economics.Successors[0].Quantity != quantity
            || request.Economics.Successors[0].Role != CorporateActionSuccessorRoleDto.Successor
            || request.Economics.Successors[0].SecurityId != request.SecurityId,
            "corporate-action.canonical-split-successor-required",
            "A canonical same-identity split requires one explicit successor lot quantity under the predecessor Security Master identity.");
        if (blockers.Count > 0)
            return ProjectionComputation.Empty;
        // This is an asset-to-asset basis transfer, mapped by the promoted rule pack and independently
        // approved by the ordinary event-spine workflow. It creates no cash, income or realized gain.
        return ProjectBookValueExchange(request with
        {
            Economics = request.Economics with { AffectedQuantity = positionQuantity, GrossCashConsideration = null }
        }, currency, blockers, requireAllocatedSuccessors: false);
    }
}
