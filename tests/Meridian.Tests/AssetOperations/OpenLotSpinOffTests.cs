using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Instruments.AssetOperations;
using Meridian.Ledger;

namespace Meridian.Tests.AssetOperations;

public sealed class OpenLotSpinOffTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void NonTaxableSpinOff_AfterPartialReliefPreservesExactEventAndBothBases(int children)
    {
        var source = PartiallyRelievedPredecessor();
        var instruction = Build(children, source);

        source.OpenTransactionCostBasis.Should().Be(423.2151m);
        source.OpenFunctionalCostBasis.Should().Be(465.53661m);
        OpenLotSuccessors.Validate(instruction);
        instruction.Projection.EventAmount.Should().Be(source.OpenFunctionalCostBasis);
        instruction.Projection.PostingSet!.Components.Should()
            .OnlyContain(component => component.Amount == source.OpenFunctionalCostBasis);
        var mapped = new CorporateActionAssetAccountingEventMapper().Map(OpenLotSuccessorTestData.MapRequest(instruction));
        mapped.Blockers.Should().BeEmpty();
        mapped.Projection!.Event.ProjectedEffect.TotalDebits.Should().Be(source.OpenFunctionalCostBasis);
        mapped.Projection.Event.ProjectedEffect.TotalCredits.Should().Be(source.OpenFunctionalCostBasis);
        instruction.Successors.Sum(target => target.Lot.OpenTransactionCostBasis).Should().Be(source.OpenTransactionCostBasis);
        instruction.Successors.Sum(target => target.Lot.OpenFunctionalCostBasis).Should().Be(source.OpenFunctionalCostBasis);
        var fraction = source.OpenQuantity / source.OriginalQuantity;
        instruction.Successors.Sum(target => target.Lot.Acquisition.TransactionCostBasis)
            .Should().Be(source.Acquisition.TransactionCostBasis * fraction);
        instruction.Successors.Sum(target => target.Lot.Acquisition.FunctionalCostBasis)
            .Should().Be(source.Acquisition.FunctionalCostBasis * fraction);
        instruction.Successors[^1].Lot.OpenFunctionalCostBasis.Should()
            .NotBe(decimal.Round(instruction.Successors[^1].Lot.OpenFunctionalCostBasis, 2));
    }

    [Theory]
    [InlineData("USD", 2)]
    [InlineData("JPY", 0)]
    [InlineData("CLF", 4)]
    public void SpinOff_OnlyCanonicalTransferPreservesSubMinorUnitEventAmount(string currency, int decimals)
    {
        var source = PartiallyRelievedPredecessor();
        source = source with { Acquisition = source.Acquisition with { FunctionalCurrency = currency } };
        var canonical = Build(source: source);
        var ordinary = Build(source: source, canonicalLotTransferJournal: false);

        canonical.Projection.EventAmount.Should().Be(source.OpenFunctionalCostBasis);
        OpenLotSuccessors.Validate(canonical);
        ordinary.Projection.CanonicalLotTransferJournal.Should().BeFalse();
        ordinary.Projection.EventAmount.Should().Be(decimal.Round(source.OpenFunctionalCostBasis, decimals, MidpointRounding.AwayFromZero));
        ordinary.Projection.EventAmount.Should().NotBe(source.OpenFunctionalCostBasis);
    }

    [Fact]
    public void NonTaxableSpinOff_RoundedEventStillFailsExactSuccessorValidation()
    {
        var instruction = Build(source: PartiallyRelievedPredecessor());
        var changed = instruction with
        {
            Projection = instruction.Projection with { EventAmount = decimal.Round(instruction.Projection.EventAmount, 2) }
        };

        var validate = () => OpenLotSuccessors.Validate(changed);

        validate.Should().Throw<ArgumentException>().WithMessage("*exact versioned predecessor and functional carrying basis*");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void NonTaxableSpinOff_MapsParentAndChildrenWithBothBasesAndRetainedHoldingPeriod(int children)
    {
        var instruction = Build(children);
        var source = instruction.ExpectedLot;

        OpenLotSuccessors.Validate(instruction);
        var mapped = new CorporateActionAssetAccountingEventMapper().Map(OpenLotSuccessorTestData.MapRequest(instruction));

        mapped.Blockers.Should().BeEmpty();
        mapped.Projection!.Event.CorporateAction.Should().BeEquivalentTo(instruction);
        var lots = instruction.Successors.Select(target => OpenLotSuccessors.WithLineage(instruction, target.Lot)).ToArray();
        lots.Should().HaveCount(children + 1);
        foreach (var lot in lots)
        {
            OpenLotValidation.Validate(lot);
            lot.AcquiredDate.Should().Be(source.AcquiredDate);
            lot.Acquisition.HoldingPeriodStartDate.Should().Be(source.Acquisition.HoldingPeriodStartDate);
            lot.Acquisition.AcquisitionFxRateToFunctional.Should().Be(source.Acquisition.AcquisitionFxRateToFunctional);
            lot.Acquisition.CorporateActionLineage!.ReportingTags.Should().BeEmpty();
        }
        var parent = lots.Single(lot => lot.SecurityId == source.SecurityId);
        parent.BookPositionId.Should().Be(source.BookPositionId);
        parent.OpenQuantity.Should().Be(source.OpenQuantity);
        parent.TaxLotRecordId.Should().NotBe(source.TaxLotRecordId);
        parent.Acquisition.CorporateActionLineage!.Role.Should().Be(CorporateActionSuccessorRoleDto.Successor);
        lots.Where(lot => lot.SecurityId != source.SecurityId)
            .Should().OnlyContain(lot => lot.Acquisition.CorporateActionLineage!.Role == CorporateActionSuccessorRoleDto.Child);
        lots.Sum(lot => lot.OpenTransactionCostBasis).Should().Be(source.OpenTransactionCostBasis);
        lots.Sum(lot => lot.OpenFunctionalCostBasis).Should().Be(source.OpenFunctionalCostBasis);
        var fraction = source.OpenQuantity / source.OriginalQuantity;
        lots.Sum(lot => lot.Acquisition.TransactionCostBasis).Should().Be(source.Acquisition.TransactionCostBasis * fraction);
        lots.Sum(lot => lot.Acquisition.FunctionalCostBasis).Should().Be(source.Acquisition.FunctionalCostBasis * fraction);
    }

    [Fact]
    public void NonTaxableSpinOff_ChangedParentQuantityIsRejectedEvenWhenTheRecipeAndTargetAgree()
    {
        var instruction = Build();
        var parent = instruction.Successors[0];
        var quantity = parent.Lot.OpenQuantity + 1m;
        var plan = instruction.Projection.LotMutations!;
        var changed = instruction with
        {
            Successors = [parent with { Lot = parent.Lot with { OpenQuantity = quantity, OriginalQuantity = quantity } }, instruction.Successors[1]],
            Projection = instruction.Projection with
            {
                Recipe = instruction.Projection.Recipe.Select(operation => operation.Kind == CorporateActionEconomicOperationKindDto.ExchangeIn
                    && operation.SecurityId == parent.Lot.SecurityId ? operation with { Quantity = quantity } : operation).ToArray(),
                LotMutations = plan with
                {
                    Mutations = plan.Mutations.Select(mutation => mutation.TargetLotId == parent.Lot.TaxLotRecordId
                        ? mutation with { Quantity = quantity, TargetAfter = mutation.TargetAfter! with { Quantity = quantity } }
                        : mutation).ToArray()
                }
            }
        };

        var validate = () => OpenLotSuccessors.Validate(changed);

        validate.Should().Throw<ArgumentException>().WithMessage("*parent*quantity*");
    }

    [Theory]
    [InlineData("parent-position")]
    [InlineData("child-role")]
    [InlineData("cash-case")]
    [InlineData("taxable-recipe")]
    [InlineData("holding-period")]
    public void NonTaxableSpinOff_RefusesOtherScopesRolesAndRecognitionRecipes(string alteration)
    {
        var instruction = Build();
        var projection = instruction.Projection;
        var plan = projection.LotMutations!;
        var changed = alteration switch
        {
            "parent-position" => instruction with
            {
                Successors = [instruction.Successors[0] with
                { Lot = instruction.Successors[0].Lot with { BookPositionId = Guid.NewGuid() } }, instruction.Successors[1]]
            },
            "child-role" => instruction with
            {
                Projection = projection with
                {
                    Recipe = projection.Recipe.Select(operation => operation.SuccessorRole == CorporateActionSuccessorRoleDto.Child
                        ? operation with { SuccessorRole = CorporateActionSuccessorRoleDto.Acquirer } : operation).ToArray()
                }
            },
            "taxable-recipe" => instruction with
            {
                Projection = projection with
                {
                    Recipe = projection.Recipe.Select(operation => operation.Kind == CorporateActionEconomicOperationKindDto.ExchangeOut
                        ? operation with { Kind = CorporateActionEconomicOperationKindDto.DividendIncome } : operation).ToArray()
                }
            },
            _ => instruction with
            {
                Projection = projection with
                {
                    LotMutations = plan with
                    {
                        Mutations = plan.Mutations.Select(mutation => alteration == "cash-case"
                            ? mutation with { LinkedCaseId = Guid.NewGuid() }
                            : mutation with { HoldingPeriodTreatment = CorporateActionHoldingPeriodTreatmentDto.NewLot }).ToArray()
                    }
                }
            }
        };

        var validate = () => OpenLotSuccessors.Validate(changed);

        validate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NonTaxableSpinOff_OptedInProjectionCannotMapWithoutReviewedLotInstruction()
    {
        var request = OpenLotSuccessorTestData.MapRequest(Build()) with { SuccessorInstruction = null };

        var result = new CorporateActionAssetAccountingEventMapper().Map(request);

        result.IsMapped.Should().BeFalse();
        result.Blockers.Should().NotBeEmpty();
    }

    private static OpenLotDto PartiallyRelievedPredecessor()
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        source = source with { Acquisition = source.Acquisition with { QuantityBasis = LotQuantityBasis.Units, FaceValueTerms = null } };
        var relief = new OpenLotReliefService().Select([source], 17.251m, OpenLotReliefMethod.SpecificId, [source.TaxLotRecordId]);
        return source with
        {
            OpenQuantity = source.OpenQuantity - relief.Quantity,
            OpenTransactionCostBasis = source.OpenTransactionCostBasis - relief.TransactionCostBasis,
            OpenFunctionalCostBasis = source.OpenFunctionalCostBasis - relief.FunctionalCostBasis,
            Version = source.Version + 1
        };
    }

    private static OpenLotSuccessorInstructionDto Build(int children = 1, OpenLotDto? source = null,
        bool canonicalLotTransferJournal = true)
    {
        source ??= OpenLotSuccessorTestData.Predecessor();
        source = source with { Acquisition = source.Acquisition with { QuantityBasis = LotQuantityBasis.Units, FaceValueTerms = null } };
        return OpenLotSuccessorTestData.Build(source, OpenLotSpinOffTestData.Targets(source, children),
            allocationPercents: OpenLotSpinOffTestData.Allocations(children), actionType: CorporateActionAccountingTypeDto.SpinOff,
            policyInputs: new(CarryHoldingPeriod: true, SpinOffTaxTreatment: CorporateActionSpinOffTaxTreatmentDto.NonTaxableBasisAllocation),
            canonicalLotTransferJournal: canonicalLotTransferJournal);
    }
}

internal static class OpenLotSpinOffTestData
{
    internal static decimal[] Allocations(int children) => children == 1 ? [0.7m, 0.3m] : [0.3333m, 0.3333m, 0.3334m];

    internal static IReadOnlyList<OpenLotSuccessorTargetDto> Targets(OpenLotDto source, int children)
    {
        var allocations = Allocations(children);
        var result = new List<OpenLotSuccessorTargetDto>();
        var remainingFraction = source.OpenQuantity / source.OriginalQuantity;
        for (var index = 0; index < allocations.Length; index++)
        {
            var target = OpenLotSuccessorTestData.Successor(source, allocations[index]);
            var lot = target.Lot;
            var quantity = index == 0 ? source.OpenQuantity : 17.5m / index;
            lot = lot with
            {
                OriginalQuantity = quantity,
                OpenQuantity = quantity,
                SecurityId = index == 0 ? source.SecurityId : lot.SecurityId,
                BookPositionId = index == 0 ? source.BookPositionId : lot.BookPositionId
            };
            if (index == allocations.Length - 1)
                lot = lot with
                {
                    OpenTransactionCostBasis = source.OpenTransactionCostBasis - result.Sum(item => item.Lot.OpenTransactionCostBasis),
                    OpenFunctionalCostBasis = source.OpenFunctionalCostBasis - result.Sum(item => item.Lot.OpenFunctionalCostBasis),
                    Acquisition = lot.Acquisition with
                    {
                        TransactionCostBasis = source.Acquisition.TransactionCostBasis * remainingFraction - result.Sum(item => item.Lot.Acquisition.TransactionCostBasis),
                        FunctionalCostBasis = source.Acquisition.FunctionalCostBasis * remainingFraction - result.Sum(item => item.Lot.Acquisition.FunctionalCostBasis)
                    }
                };
            result.Add(target with { Lot = lot });
        }
        return result;
    }
}
