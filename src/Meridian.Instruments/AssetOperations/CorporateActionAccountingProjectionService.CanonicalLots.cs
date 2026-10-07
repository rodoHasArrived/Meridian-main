using Meridian.Contracts.AssetOperations;

namespace Meridian.Instruments.AssetOperations;

public sealed partial class CorporateActionAccountingProjectionService
{
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
