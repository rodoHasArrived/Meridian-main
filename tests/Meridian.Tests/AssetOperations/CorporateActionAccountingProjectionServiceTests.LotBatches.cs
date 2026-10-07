using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Tests.AssetOperations;

public sealed partial class CorporateActionAccountingProjectionServiceTests
{
    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit)]
    [InlineData(CorporateActionAccountingTypeDto.ReverseStockSplit)]
    [InlineData(CorporateActionAccountingTypeDto.MergerStock)]
    [InlineData(CorporateActionAccountingTypeDto.AdvanceRefunding)]
    public void Project_BindsEveryPredecessorWithoutChangingAggregateCorporateActionIntent(CorporateActionAccountingTypeDto action)
    {
        var request = CompleteLotBatch(action);

        var result = _sut.Project(request);

        result.Blockers.Should().BeEmpty();
        result.CanPreparePostingCandidate.Should().BeTrue();
        result.EventAmount.Should().Be(2000m);
        result.LotMutations!.Mutations.Should().Equal(request.AuthoritativeLotMutations);
        result.LotMutations.Mutations.Select(mutation => mutation.SourceLotId).Distinct().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit)]
    [InlineData(CorporateActionAccountingTypeDto.ReverseStockSplit)]
    [InlineData(CorporateActionAccountingTypeDto.MergerStock)]
    [InlineData(CorporateActionAccountingTypeDto.AdvanceRefunding)]
    public void Project_RejectsDifferentPerLotRatiosEvenWhenAggregateQuantityAndBasisStillMatch(CorporateActionAccountingTypeDto action)
    {
        var request = CompleteLotBatch(action);
        var mutations = request.AuthoritativeLotMutations.ToArray();
        var targetSecurity = mutations[0].TargetSecurityId;
        var secondIndex = Array.FindLastIndex(mutations, mutation => mutation.TargetSecurityId == targetSecurity);
        mutations[0] = mutations[0] with
        { Quantity = mutations[0].Quantity + 1m, TargetAfter = mutations[0].TargetAfter! with { Quantity = mutations[0].TargetAfter!.Quantity + 1m } };
        mutations[secondIndex] = mutations[secondIndex] with
        { Quantity = mutations[secondIndex].Quantity - 1m, TargetAfter = mutations[secondIndex].TargetAfter! with { Quantity = mutations[secondIndex].TargetAfter!.Quantity - 1m } };
        CorporateActionLotMutationPlanValidator.Validate(mutations).Should().BeEmpty("the changed target snapshots remain internally consistent");

        var result = _sut.Project(request with { AuthoritativeLotMutations = mutations });

        result.CanPreparePostingCandidate.Should().BeFalse();
        result.Blockers.Should().Contain(blocker => blocker.Code == "corporate-action.lot-mutation-plan-intent-mismatch");
    }

    [Theory]
    [InlineData("missing-target")]
    [InlineData("wrong-security")]
    [InlineData("different-allocation")]
    public void Project_RejectsIncompleteOrChangedSuccessorPopulationInLotBatch(string changed)
    {
        var request = CompleteLotBatch(CorporateActionAccountingTypeDto.AdvanceRefunding);
        var mutations = request.AuthoritativeLotMutations.ToArray();
        if (changed == "missing-target")
            mutations = mutations.Skip(1).ToArray();
        if (changed == "wrong-security")
            mutations[0] = mutations[0] with { TargetSecurityId = Guid.NewGuid() };
        if (changed == "different-allocation")
            mutations[0] = mutations[0] with { AllocationPercent = 0.6m };

        var result = _sut.Project(request with { AuthoritativeLotMutations = mutations });

        result.CanPreparePostingCandidate.Should().BeFalse();
        result.Status.Should().Be(CorporateActionProjectionStatusDto.Blocked);
    }

    private CorporateActionAccountingProjectionRequest CompleteLotBatch(CorporateActionAccountingTypeDto action)
    {
        var ratio = action == CorporateActionAccountingTypeDto.ReverseStockSplit ? 0.5m : 2m;
        var request = CreateRequest(action,
            economics: new CorporateActionEconomicsDto(PositionQuantity: 200m, AffectedQuantity: 200m,
                CarryingAmount: 2000m, SplitRatio: ratio,
                Successors: action == CorporateActionAccountingTypeDto.AdvanceRefunding
                    ? [new(SuccessorId, CorporateActionSuccessorRoleDto.Refunded, 100m, 0.5m),
                       new(SecondSuccessorId, CorporateActionSuccessorRoleDto.Unrefunded, 100m, 0.5m)]
                    : [new(action == CorporateActionAccountingTypeDto.MergerStock ? SuccessorId : SecurityId,
                        CorporateActionSuccessorRoleDto.Successor, 200m * ratio, 1m)]),
            policy: new CorporateActionPolicyInputsDto(CarryHoldingPeriod: true)) with
        { CanonicalLotTransferJournal = action is CorporateActionAccountingTypeDto.StockSplit or CorporateActionAccountingTypeDto.ReverseStockSplit };
        var intent = _sut.Project(request);
        intent.Blockers.Should().BeEmpty();
        var mutations = new List<CorporateActionLotMutationDto>();
        foreach (var sourceId in new[] { SourceLotId, SecondSourceLotId })
        {
            foreach (var target in intent.LotMutations!.Mutations)
            {
                var quantity = target.Quantity!.Value / 2m;
                var carrying = target.CarryingAmount!.Value / 2m;
                mutations.Add(target with
                {
                    SourceLotId = sourceId,
                    ExpectedSourceLotVersion = 7,
                    SourceBefore = new(100m, 1000m, 1000m),
                    SourceAfter = new(0m, 0m, 0m),
                    SourceQuantity = target.SourceQuantity / 2m,
                    SourceCarryingAmount = target.SourceCarryingAmount / 2m,
                    SourceBasisAmount = carrying,
                    Quantity = quantity,
                    CarryingAmount = carrying,
                    BasisAmount = carrying,
                    TargetLotId = Guid.NewGuid(),
                    TargetOperation = CorporateActionLotTargetOperationDto.Create,
                    TargetAfter = new(quantity, carrying, carrying)
                });
            }
        }
        return request with { AuthoritativeLotMutations = mutations };
    }
}
