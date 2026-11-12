using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.Instruments.AssetOperations;

/// <summary>
/// Reviewed canonical lots and their full-close disposal instruction. The ordinary disposal
/// authority rereads these lots and commits quantity and basis relief with the approved journal.
/// </summary>
public sealed record CorporateActionRedemptionLotPlan(
    IReadOnlyList<OpenLotDto> Lots,
    AssetLotMutationInstructionDto Disposal);

public sealed partial class CorporateActionAssetAccountingEventMapper
{
    private static AssetLotMutationInstructionDto? ResolveRedemption(
        CorporateActionAssetAccountingEventMapRequest request,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        var projection = request.Projection;
        var isRedemption = projection.Treatment.ActionType is CorporateActionAccountingTypeDto.CallRedemption
            or CorporateActionAccountingTypeDto.PutRedemption;
        if (!isRedemption && request.Redemption is null)
            return null;
        void Block(string message) => blockers.Add(new CorporateActionProjectionBlockerDto(
            "corporate-action.canonical-redemption-required", message));
        if (!isRedemption || !projection.CanonicalLotRedemptionJournal || request.Redemption is not { } plan)
        {
            Block("Call and put redemption posting requires reviewed canonical lots and a full-close disposal instruction.");
            return null;
        }
        var instruction = plan.Disposal;
        if (instruction is null || plan.Lots is not { Count: > 0 } || plan.Lots.Any(lot => lot is null)
            || instruction.Direction != OpenLotDirection.Long
            || request.SuccessorInstruction is not null || request.CapitalReturnInstruction is not null
            || request.Correction is not null || instruction.Amortization is not null
            || instruction.CorporateAction is not null || instruction.CapitalReturn is not null
            || instruction.FactorPaydown is not null || instruction.CorrectsMutationBatchId is not null
            || instruction.CorrectsJournalEntryId is not null || instruction.CorrectionApproval is not null)
        {
            Block("Redemption requires held Long lots without successor, capital-return or correction inputs.");
            return null;
        }
        foreach (var issue in AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.Disposal,
            instruction, projection.EventAmount, projection.EconomicEvent!.EffectiveDate, request.RetainedEvidence))
            Block(issue);
        if (projection.PostingSet!.Components.Any(component => component.Amount != 0m && component.Kind is not
            (CorporateActionPostingComponentKindDto.Cash or CorporateActionPostingComponentKindDto.CarryingValueRelief
             or CorporateActionPostingComponentKindDto.RedemptionPrincipal or CorporateActionPostingComponentKindDto.RealizedGain
             or CorporateActionPostingComponentKindDto.RealizedLoss)))
            Block("Canonical redemption supports principal and realized result; accrued income and make-whole income require a separate reviewed workflow.");
        if (plan.Lots.Count != instruction.DisposalSelections.Count
            || plan.Lots.Select(lot => lot.TaxLotRecordId).Distinct().Count() != plan.Lots.Count
            || projection.LotMutations!.Mutations.Count != plan.Lots.Count)
        {
            Block("Every redeemed canonical lot requires one full-close selection and exact projected lot mutation.");
            return null;
        }
        foreach (var lot in plan.Lots)
        {
            try
            { OpenLotValidation.Validate(lot); }
            catch (ArgumentException exception) { Block(exception.Message); continue; }
            var selection = instruction.DisposalSelections.FirstOrDefault(item => item.TaxLotRecordId == lot.TaxLotRecordId);
            var mutation = projection.LotMutations.Mutations.FirstOrDefault(item => item.SourceLotId == lot.TaxLotRecordId);
            var scale = lot.Acquisition.QuantityBasis == LotQuantityBasis.Face
                ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
            if (lot.SecurityId != request.Scope.SecurityId || lot.BookPositionId != request.Scope.BookPositionId
                || lot.LedgerBookId != request.Scope.LedgerBookId || lot.AcquiredDate > projection.EconomicEvent.EffectiveDate
                || lot.Acquisition.Direction != OpenLotDirection.Long || lot.OpenQuantity <= 0m
                || lot.Acquisition.AcquisitionCurrency != projection.PostingSet.Currency
                || lot.Acquisition.FunctionalCurrency != projection.PostingSet.Currency
                || lot.OpenTransactionCostBasis != lot.OpenFunctionalCostBasis
                || lot.Acquisition.Evidence.Any(evidence => !request.RetainedEvidence.Contains(evidence))
                || selection is null || selection.LotId != lot.LotId || selection.ExpectedVersion != lot.Version
                || selection.Quantity * scale != lot.OpenQuantity || selection.ExpectedOpenQuantity != selection.Quantity
                || selection.ExpectedUnitCost * (lot.OriginalQuantity / scale) != lot.Acquisition.TransactionCostBasis
                || selection.ExpectedCostBasis != lot.OpenFunctionalCostBasis
                || mutation is null || mutation.Kind != CorporateActionLotMutationKindDto.Dispose
                || mutation.ExpectedSourceLotVersion != lot.Version || mutation.Quantity != lot.OpenQuantity
                || mutation.CarryingAmount != lot.OpenFunctionalCostBasis || mutation.BasisAmount != lot.OpenTransactionCostBasis
                || mutation.SourceBefore != new CorporateActionLotStateSnapshotDto(lot.OpenQuantity,
                    lot.OpenFunctionalCostBasis, lot.OpenTransactionCostBasis)
                || mutation.SourceAfter is not null)
                Block("Redemption must close each exact same-currency canonical lot's full open quantity and both retained bases.");
        }
        if (instruction.DisposalSalePrice is not { } price
            || price * instruction.DisposalSelections.Sum(selection => selection.Quantity) != projection.EventAmount)
            Block("Redemption disposal execution price and selected durable units must equal the projected principal cash.");
        return instruction;
    }
}
