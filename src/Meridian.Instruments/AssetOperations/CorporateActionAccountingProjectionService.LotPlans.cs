using Meridian.Contracts.AssetOperations;

namespace Meridian.Instruments.AssetOperations;

public sealed partial class CorporateActionAccountingProjectionService
{
    private static ProjectionComputation BindAuthoritativeLotMutations(
        ProjectionComputation computation,
        IReadOnlyList<CorporateActionLotMutationDto> authoritativeLotMutations,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        if (authoritativeLotMutations.Count != computation.LotMutations.Count)
        {
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
