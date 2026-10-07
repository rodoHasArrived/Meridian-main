using Meridian.Contracts.AssetOperations;

namespace Meridian.Instruments.AssetOperations;

public sealed partial class CorporateActionAccountingProjectionService
{
    private static ProjectionComputation BindAuthoritativeLotMutations(
        ProjectionComputation computation,
        CorporateActionAccountingTypeDto actionType,
        IReadOnlyList<CorporateActionLotMutationDto> authoritativeLotMutations,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        if (authoritativeLotMutations.Count != computation.LotMutations.Count)
        {
            if (actionType is CorporateActionAccountingTypeDto.StockSplit or CorporateActionAccountingTypeDto.ReverseStockSplit
                or CorporateActionAccountingTypeDto.MergerStock or CorporateActionAccountingTypeDto.AdvanceRefunding)
                return BindGroupedCorporateActionLots(computation, authoritativeLotMutations, blockers);
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.lot-mutation-plan-count-mismatch",
                "The authoritative lot plan must contain exactly one entry for every projected lot mutation."));
            return computation;
        }

        for (var index = 0; index < computation.LotMutations.Count; index++)
        {
            if (!HasSameProjectedIntent(computation.LotMutations[index], authoritativeLotMutations[index]))
            {
                blockers.Add(new CorporateActionProjectionBlockerDto(
                    "corporate-action.lot-mutation-plan-intent-mismatch",
                    $"Authoritative lot mutation {index + 1} does not match the projected economic intent."));
            }
        }

        foreach (var blocker in CorporateActionLotMutationPlanValidator.Validate(authoritativeLotMutations))
        {
            blockers.Add(blocker);
        }

        return blockers.Count == 0
            ? computation with { LotMutations = authoritativeLotMutations.ToArray() }
            : computation;
    }

    private static ProjectionComputation BindGroupedCorporateActionLots(
        ProjectionComputation computation,
        IReadOnlyList<CorporateActionLotMutationDto> authoritative,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        foreach (var blocker in CorporateActionLotMutationPlanValidator.Validate(authoritative))
            blockers.Add(blocker);
        if (blockers.Count != 0)
            return computation;
        var predecessors = authoritative.Select(mutation => mutation.SourceLotId).Distinct().ToArray();
        if (computation.LotMutations.Count == 0 || predecessors.Length < 2
            || authoritative.Count != predecessors.Length * computation.LotMutations.Count
            || authoritative.Any(mutation => mutation.Kind is not (CorporateActionLotMutationKindDto.CarryOver or CorporateActionLotMutationKindDto.Allocate)
                || mutation.TargetOperation != CorporateActionLotTargetOperationDto.Create
                || mutation.SourceBefore is null || mutation.SourceAfter != new CorporateActionLotStateSnapshotDto(0m, 0m, 0m)))
        {
            blockers.Add(new("corporate-action.lot-mutation-plan-count-mismatch",
                "Each reviewed predecessor must supply a complete full-lot allocation to every projected successor."));
            return computation;
        }
        var matched = new HashSet<Guid>();
        foreach (var projected in computation.LotMutations)
        {
            var legs = authoritative.Where(mutation => HasSameProjectedTreatment(projected, mutation)).ToArray();
            if (legs.Length != predecessors.Length || legs.Select(mutation => mutation.SourceLotId).Distinct().Count() != predecessors.Length
                || legs.Any(mutation => mutation.TargetLotId is not { } targetId || !matched.Add(targetId))
                || projected.Quantity is not > 0m || projected.SourceQuantity is not > 0m
                || projected.Quantity != legs.Sum(mutation => mutation.Quantity)
                || projected.CarryingAmount != legs.Sum(mutation => mutation.CarryingAmount)
                || (projected.BasisAmount.HasValue && projected.BasisAmount != legs.Sum(mutation => mutation.BasisAmount))
                || projected.SourceQuantity != legs.Sum(mutation => mutation.SourceQuantity)
                || projected.SourceCarryingAmount != legs.Sum(mutation => mutation.SourceCarryingAmount)
                || legs.Any(mutation => mutation.Quantity is not > 0m || mutation.SourceQuantity is not > 0m
                    || mutation.Quantity * projected.SourceQuantity != projected.Quantity * mutation.SourceQuantity
                    || mutation.SourceQuantity != mutation.SourceBefore!.Quantity * (projected.AllocationPercent ?? 1m)))
            {
                blockers.Add(new("corporate-action.lot-mutation-plan-intent-mismatch",
                    "Reviewed per-lot mutations must reproduce every projected successor total and the same quantity ratio and allocation for each predecessor."));
            }
        }
        return blockers.Count == 0 && matched.Count == authoritative.Count
            ? computation with { LotMutations = authoritative.ToArray() }
            : computation;
    }

    private static bool HasSameProjectedTreatment(CorporateActionLotMutationDto projected, CorporateActionLotMutationDto authoritative)
        => projected.Kind == authoritative.Kind && projected.SecurityId == authoritative.SecurityId
           && projected.TargetSecurityId == authoritative.TargetSecurityId
           && projected.AllocationPercent == authoritative.AllocationPercent
           && projected.HoldingPeriodTreatment == authoritative.HoldingPeriodTreatment
           && string.Equals(projected.Description, authoritative.Description, StringComparison.Ordinal)
           && projected.LinkedCaseId == authoritative.LinkedCaseId
           && projected.ReportingTags.OrderBy(static tag => tag, StringComparer.Ordinal).SequenceEqual(
               authoritative.ReportingTags.OrderBy(static tag => tag, StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool HasSameProjectedIntent(
        CorporateActionLotMutationDto projected,
        CorporateActionLotMutationDto authoritative)
        => projected.Kind == authoritative.Kind &&
           projected.SecurityId == authoritative.SecurityId &&
           projected.TargetSecurityId == authoritative.TargetSecurityId &&
           projected.Quantity == authoritative.Quantity &&
           projected.CarryingAmount == authoritative.CarryingAmount &&
           (!projected.BasisAmount.HasValue || projected.BasisAmount == authoritative.BasisAmount) &&
           projected.SourceQuantity == authoritative.SourceQuantity &&
           projected.SourceCarryingAmount == authoritative.SourceCarryingAmount &&
           projected.AllocationPercent == authoritative.AllocationPercent &&
           projected.HoldingPeriodTreatment == authoritative.HoldingPeriodTreatment &&
           string.Equals(projected.Description, authoritative.Description, StringComparison.Ordinal) &&
           projected.LinkedCaseId == authoritative.LinkedCaseId &&
           projected.ReportingTags.OrderBy(static tag => tag, StringComparer.Ordinal).SequenceEqual(
               authoritative.ReportingTags.OrderBy(static tag => tag, StringComparer.Ordinal),
               StringComparer.Ordinal);

    private static IReadOnlyList<CorporateActionLotMutationDto> AllocateSourceRelief(
        IReadOnlyList<CorporateActionLotMutationDto> lotMutations,
        decimal sourceQuantity,
        decimal sourceCarryingAmount,
        string currency)
    {
        if (lotMutations.Count == 0)
        {
            return [];
        }

        var allocated = new CorporateActionLotMutationDto[lotMutations.Count];
        var runningQuantity = 0m;
        var runningCarryingAmount = 0m;
        for (var index = 0; index < lotMutations.Count; index++)
        {
            var weight = lotMutations[index].AllocationPercent ?? (1m / lotMutations.Count);
            var quantity = index == lotMutations.Count - 1
                ? sourceQuantity - runningQuantity
                : sourceQuantity * weight;
            var carryingAmount = index == lotMutations.Count - 1
                ? sourceCarryingAmount - runningCarryingAmount
                : Round(sourceCarryingAmount * weight, currency);
            runningQuantity += quantity;
            runningCarryingAmount += carryingAmount;
            allocated[index] = lotMutations[index] with
            {
                SourceQuantity = quantity,
                SourceCarryingAmount = carryingAmount
            };
        }

        return allocated;
    }
}
