using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Instruments.AssetOperations;
using Meridian.Tests.Storage;

namespace Meridian.Tests.AssetOperations;

public sealed class OpenLotSuccessorCorporateActionTests
{
    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit, 2)]
    [InlineData(CorporateActionAccountingTypeDto.ReverseStockSplit, 0.5)]
    [InlineData(CorporateActionAccountingTypeDto.MergerStock, 1.5)]
    public void CashlessAction_CarriesExactOpenBasisAndRemainingHistoricalAcquisitionFacts(
        CorporateActionAccountingTypeDto action, double quantityRatio)
    {
        var predecessor = UnitPredecessor();
        var source = predecessor with
        {
            OpenTransactionCostBasis = 594.000001m,
            OpenFunctionalCostBasis = 653.4000011m,
            Acquisition = predecessor.Acquisition with
            {
                TransactionCostBasis = 1000.000003m,
                FunctionalCostBasis = 1100.0000033m
            }
        };
        var target = UnitSuccessor(source, source.OpenQuantity * (decimal)quantityRatio,
            sameIdentity: action != CorporateActionAccountingTypeDto.MergerStock);
        var instruction = Build(source, target, action, (decimal)quantityRatio);
        var fingerprint = OpenLotSuccessors.Fingerprint(instruction);

        instruction.Projection.EventAmount.Should().Be(source.OpenFunctionalCostBasis);
        instruction.Projection.PostingSet!.Components.Should().OnlyContain(component => component.Amount == source.OpenFunctionalCostBasis);

        var validate = () => OpenLotSuccessors.Validate(instruction);
        validate.Should().NotThrow();
        var carried = OpenLotSuccessors.WithLineage(instruction, target.Lot);

        carried.OpenQuantity.Should().Be(source.OpenQuantity * (decimal)quantityRatio);
        carried.OriginalQuantity.Should().Be(carried.OpenQuantity);
        carried.OpenTransactionCostBasis.Should().Be(594.000001m);
        carried.OpenFunctionalCostBasis.Should().Be(653.4000011m);
        carried.Acquisition.TransactionCostBasis.Should().Be(600.0000018m);
        carried.Acquisition.FunctionalCostBasis.Should().Be(660.00000198m);
        carried.Acquisition.AcquisitionFxRateToFunctional.Should().Be(1.1m);
        carried.Acquisition.AcquisitionCurrency.Should().Be("EUR");
        carried.Acquisition.FunctionalCurrency.Should().Be("USD");
        carried.AcquiredDate.Should().Be(source.AcquiredDate);
        carried.Acquisition.HoldingPeriodStartDate.Should().Be(source.Acquisition.HoldingPeriodStartDate);
        carried.Acquisition.QuantityBasis.Should().Be(LotQuantityBasis.Units);
        carried.Acquisition.FaceValueTerms.Should().BeNull();
        carried.Acquisition.Evidence.Should().Contain(source.Acquisition.Evidence);
        var origin = carried.Acquisition.CorporateActionLineage!;
        origin.CorporateActionId.Should().Be(instruction.Projection.EconomicEvent!.EventId);
        origin.SourceCorporateActionId.Should().Be(instruction.Projection.SourceCorporateActionId);
        origin.ActionType.Should().Be(action);
        origin.EffectiveDate.Should().Be(OpenLotSuccessorTestData.EffectiveDate);
        origin.PredecessorTaxLotRecordId.Should().Be(source.TaxLotRecordId);
        origin.PredecessorVersion.Should().Be(source.Version);
        origin.BasisAllocationPercent.Should().Be(100m);
        origin.Role.Should().Be(CorporateActionSuccessorRoleDto.Successor);
        origin.ReportingTags.Should().BeEmpty();
        target.Lot.Acquisition.CorporateActionLineage.Should().BeNull("enrichment must leave reviewed inputs immutable");
        OpenLotSuccessors.Fingerprint(instruction).Should().Be(fingerprint);
        OpenLotSuccessors.Validate(instruction with { Successors = [target with { Lot = carried }] });
    }

    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit, 2)]
    [InlineData(CorporateActionAccountingTypeDto.ReverseStockSplit, 0.5)]
    public void SameIdentitySplit_MapsDistinctSourceAndSuccessorLotLegs(
        CorporateActionAccountingTypeDto action, double quantityRatio)
    {
        var source = UnitPredecessor();
        var target = UnitSuccessor(source, source.OpenQuantity * (decimal)quantityRatio, sameIdentity: true);
        var instruction = Build(source, target, action, (decimal)quantityRatio);
        var request = OpenLotSuccessorTestData.MapRequest(instruction);

        var result = new CorporateActionAssetAccountingEventMapper().Map(request);

        result.IsMapped.Should().BeTrue("same-security, same-position splits retain separate durable lot identities: {0}",
            string.Join("; ", result.Blockers.Select(item => item.Message)));
        var lines = result.Projection!.Event.ProjectedEffect.Lines;
        lines.Should().HaveCount(2);
        lines.Should().OnlyContain(line => line.Dimensions!.InstrumentId == source.SecurityId
            && line.Dimensions.PositionId == source.BookPositionId);
        lines.Single(line => line.Credit > 0m).Dimensions!.TaxLotId.Should().Be(source.LotId);
        lines.Single(line => line.Debit > 0m).Dimensions!.TaxLotId.Should().Be(target.Lot.LotId);
    }

    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit, 2)]
    [InlineData(CorporateActionAccountingTypeDto.ReverseStockSplit, 0.5)]
    [InlineData(CorporateActionAccountingTypeDto.MergerStock, 1.5)]
    public void Mapper_RequiresCanonicalInstructionForExplicitlyOptedInSplitAndStockMerger(
        CorporateActionAccountingTypeDto action, double quantityRatio)
    {
        var source = UnitPredecessor();
        var target = UnitSuccessor(source, source.OpenQuantity * (decimal)quantityRatio,
            sameIdentity: action != CorporateActionAccountingTypeDto.MergerStock);
        var instruction = OpenLotSuccessorTestData.Build(source, [target], actionType: action,
            policyInputs: new CorporateActionPolicyInputsDto(CarryHoldingPeriod: true),
            splitRatio: action == CorporateActionAccountingTypeDto.MergerStock ? null : (decimal)quantityRatio,
            canonicalLotTransferJournal: true);
        instruction.Projection.CanonicalLotTransferJournal.Should().BeTrue();
        var request = OpenLotSuccessorTestData.MapRequest(instruction);

        var result = new CorporateActionAssetAccountingEventMapper().Map(request with { SuccessorInstruction = null });

        result.IsMapped.Should().BeFalse();
        result.Blockers.Should().Contain(item => item.Code == "corporate-action.canonical-successors-required");
    }

    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit, 2)]
    [InlineData(CorporateActionAccountingTypeDto.ReverseStockSplit, 0.5)]
    [InlineData(CorporateActionAccountingTypeDto.MergerStock, 1.5)]
    public void LegacyIdentifierChangingSplitAndStockMerger_MapWithoutCanonicalInstruction(
        CorporateActionAccountingTypeDto action, double quantityRatio)
    {
        var source = UnitPredecessor();
        var target = UnitSuccessor(source, source.OpenQuantity * (decimal)quantityRatio, sameIdentity: false);
        var instruction = OpenLotSuccessorTestData.Build(source, [target], actionType: action,
            policyInputs: new CorporateActionPolicyInputsDto(CarryHoldingPeriod: true),
            splitRatio: action == CorporateActionAccountingTypeDto.MergerStock ? null : (decimal)quantityRatio,
            identifierChanged: true, canonicalLotTransferJournal: false);
        instruction.Projection.CanonicalLotTransferJournal.Should().BeFalse();
        System.Text.Json.JsonSerializer.Serialize(instruction.Projection).Should().NotContain("CanonicalLotTransferJournal");
        var request = OpenLotSuccessorTestData.MapRequest(instruction) with { SuccessorInstruction = null };

        var result = new CorporateActionAssetAccountingEventMapper().Map(request);

        result.IsMapped.Should().BeTrue("the existing non-opt-in projection remains mappable: {0}",
            string.Join("; ", result.Blockers.Select(item => item.Message)));
        result.Projection!.Event.CorporateAction.Should().BeNull();
        result.Projection.LotMutation.Should().BeNull();
    }

    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit, 2)]
    [InlineData(CorporateActionAccountingTypeDto.ReverseStockSplit, 0.5)]
    public void Validate_RejectsFractionalSplitEvenWhenTargetAndRecipeAgree(
        CorporateActionAccountingTypeDto action, double quantityRatio)
    {
        var source = UnitPredecessor();
        var target = UnitSuccessor(source, source.OpenQuantity * (decimal)quantityRatio, sameIdentity: true);
        var instruction = Build(source, target, action, (decimal)quantityRatio);
        var fractional = target.Lot with
        {
            OriginalQuantity = target.Lot.OriginalQuantity + 0.5m,
            OpenQuantity = target.Lot.OpenQuantity + 0.5m
        };
        var mutation = instruction.Projection.LotMutations!.Mutations.Single();
        var altered = instruction with
        {
            Successors = [target with { Lot = fractional }],
            Projection = instruction.Projection with
            {
                Recipe = instruction.Projection.Recipe.Select(operation =>
                    operation.Kind == CorporateActionEconomicOperationKindDto.ExchangeIn
                        ? operation with { Quantity = fractional.OpenQuantity } : operation).ToArray(),
                LotMutations = instruction.Projection.LotMutations with
                {
                    Mutations = [mutation with
                    {
                        Quantity = fractional.OpenQuantity,
                        TargetAfter = mutation.TargetAfter! with { Quantity = fractional.OpenQuantity }
                    }]
                }
            }
        };

        var validate = () => OpenLotSuccessors.Validate(altered);

        validate.Should().Throw<ArgumentException>().WithMessage("*whole-unit*");
    }

    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit, 2)]
    [InlineData(CorporateActionAccountingTypeDto.ReverseStockSplit, 0.5)]
    [InlineData(CorporateActionAccountingTypeDto.MergerStock, 1.5)]
    public void Validate_RejectsRefundingReportingTagOnSplitOrStockMerger(
        CorporateActionAccountingTypeDto action, double quantityRatio)
    {
        var source = UnitPredecessor();
        var target = UnitSuccessor(source, source.OpenQuantity * (decimal)quantityRatio,
            sameIdentity: action != CorporateActionAccountingTypeDto.MergerStock);
        var instruction = Build(source, target, action, (decimal)quantityRatio);
        var projection = instruction.Projection;
        var altered = instruction with
        {
            Projection = projection with
            {
                LotMutations = projection.LotMutations! with
                {
                    Mutations = [projection.LotMutations.Mutations.Single() with { ReportingTags = ["ScheduleD"] }]
                }
            }
        };

        var validate = () => OpenLotSuccessors.Validate(altered);

        validate.Should().Throw<ArgumentException>().WithMessage("*Only the refunded successor*");
    }

    [Theory]
    [InlineData("action")]
    [InlineData("source-action")]
    [InlineData("absent-source-action")]
    [InlineData("type")]
    [InlineData("date")]
    [InlineData("predecessor")]
    [InlineData("version")]
    [InlineData("allocation")]
    [InlineData("role")]
    [InlineData("reporting-tags")]
    public void Validate_RejectsSuppliedLineageDifferentFromReviewedAction(string alteredFact)
    {
        var source = UnitPredecessor();
        var target = UnitSuccessor(source, 120m, sameIdentity: true);
        var instruction = Build(source, target, CorporateActionAccountingTypeDto.StockSplit, 2m);
        var origin = OpenLotSuccessors.ExpectedLineage(instruction, target.Lot);
        var changedOrigin = alteredFact switch
        {
            "action" => origin with { CorporateActionId = Guid.NewGuid() },
            "source-action" => origin with { SourceCorporateActionId = Guid.NewGuid() },
            "absent-source-action" => origin with { SourceCorporateActionId = null },
            "type" => origin with { ActionType = CorporateActionAccountingTypeDto.ReverseStockSplit },
            "date" => origin with { EffectiveDate = origin.EffectiveDate.AddDays(1) },
            "predecessor" => origin with { PredecessorTaxLotRecordId = Guid.NewGuid() },
            "version" => origin with { PredecessorVersion = origin.PredecessorVersion + 1 },
            "allocation" => origin with { BasisAllocationPercent = 50m },
            "role" => origin with { Role = CorporateActionSuccessorRoleDto.Refunded },
            "reporting-tags" => origin with { ReportingTags = ["ScheduleD"] },
            _ => throw new ArgumentOutOfRangeException(nameof(alteredFact))
        };
        var changedLot = target.Lot with
        {
            Acquisition = target.Lot.Acquisition with { CorporateActionLineage = changedOrigin }
        };

        var validate = () => OpenLotSuccessors.Validate(instruction with { Successors = [target with { Lot = changedLot }] });

        validate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Successor_CanUndergoLaterActionWhileCarryingHistoricalDatesAndEvidence()
    {
        var initial = UnitPredecessor();
        var firstTarget = UnitSuccessor(initial, 120m, sameIdentity: true);
        var first = Build(initial, firstTarget, CorporateActionAccountingTypeDto.StockSplit, 2m);
        var source = OpenLotSuccessors.WithLineage(first, firstTarget.Lot);
        var onward = UnitSuccessor(source, 60m, sameIdentity: true);
        var instruction = Build(source, onward, CorporateActionAccountingTypeDto.ReverseStockSplit, 0.5m,
            effectiveDate: OpenLotSuccessorTestData.EffectiveDate.AddDays(1));

        var validate = () => OpenLotSuccessors.Validate(instruction);

        validate.Should().NotThrow();
        var carried = OpenLotSuccessors.WithLineage(instruction, onward.Lot);
        carried.AcquiredDate.Should().Be(initial.AcquiredDate);
        carried.Acquisition.HoldingPeriodStartDate.Should().Be(initial.Acquisition.HoldingPeriodStartDate);
        carried.Acquisition.TransactionCostBasis.Should().Be(600m);
        carried.Acquisition.FunctionalCostBasis.Should().Be(660m);
        carried.Acquisition.Evidence.Should().Contain(source.Acquisition.Evidence);
        carried.Acquisition.CorporateActionLineage!.PredecessorTaxLotRecordId.Should().Be(source.TaxLotRecordId);
        source.Acquisition.CorporateActionLineage!.PredecessorTaxLotRecordId.Should().Be(initial.TaxLotRecordId);
    }

    [Theory]
    [InlineData("before-origin")]
    [InlineData("before-acquisition")]
    [InlineData("same-action")]
    public void Validate_RejectsOnwardActionBeforeOriginOrAcquisitionOrRepeatingAction(string invalidTiming)
    {
        var initial = UnitPredecessor();
        var firstTarget = UnitSuccessor(initial, 120m, sameIdentity: true);
        var first = Build(initial, firstTarget, CorporateActionAccountingTypeDto.StockSplit, 2m);
        var source = OpenLotSuccessors.WithLineage(first, firstTarget.Lot);
        var date = invalidTiming == "before-acquisition" ? source.AcquiredDate.AddDays(-1)
            : invalidTiming == "before-origin" ? OpenLotSuccessorTestData.EffectiveDate.AddDays(-1)
            : OpenLotSuccessorTestData.EffectiveDate.AddDays(1);
        var target = UnitSuccessor(source, 60m, sameIdentity: true);
        var instruction = Build(source, target, CorporateActionAccountingTypeDto.ReverseStockSplit, 0.5m, date);
        if (invalidTiming == "same-action")
            instruction = instruction with
            {
                ExpectedLot = source with
                {
                    Acquisition = source.Acquisition with
                    {
                        CorporateActionLineage = source.Acquisition.CorporateActionLineage! with
                        {
                            CorporateActionId = instruction.Projection.EconomicEvent!.EventId
                        }
                    }
                }
            };

        var validate = () => OpenLotSuccessors.Validate(instruction);

        validate.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_RejectsSameSourceActionReprojectedUnderAnotherCaseAndVersion(bool legacyProjection)
    {
        var sourceActionId = Guid.NewGuid();
        var initial = UnitPredecessor();
        var firstTarget = UnitSuccessor(initial, 120m, sameIdentity: true);
        var first = Build(initial, firstTarget, CorporateActionAccountingTypeDto.StockSplit, 2m, actionId: sourceActionId);
        if (legacyProjection)
            first = first with { Projection = first.Projection with { SourceCorporateActionId = null } };
        var source = OpenLotSuccessors.WithLineage(first, firstTarget.Lot);
        var secondTarget = UnitSuccessor(source, 240m, sameIdentity: true);
        var repeated = Build(source, secondTarget, CorporateActionAccountingTypeDto.StockSplit, 2m,
            effectiveDate: OpenLotSuccessorTestData.EffectiveDate.AddDays(1), actionId: sourceActionId,
            sourceEventVersion: 2);
        if (legacyProjection)
            repeated = repeated with { Projection = repeated.Projection with { SourceCorporateActionId = null } };

        repeated.Projection.CaseId.Should().NotBe(first.Projection.CaseId);
        repeated.Projection.EconomicEvent!.EventVersion.Should().NotBe(first.Projection.EconomicEvent!.EventVersion);
        repeated.Projection.EconomicEvent.EventId.Should().NotBe(first.Projection.EconomicEvent.EventId);
        source.Acquisition.CorporateActionLineage!.SourceCorporateActionId.Should().Be(sourceActionId);
        if (legacyProjection)
            System.Text.Json.JsonSerializer.Serialize(repeated.Projection).Should().NotContain("SourceCorporateActionId");

        var validate = () => OpenLotSuccessors.Validate(repeated);

        validate.Should().Throw<ArgumentException>().WithMessage("*repeat the same action*");
    }

    [Theory]
    [InlineData("source-action")]
    [InlineData("empty-source-action")]
    [InlineData("missing-source-evidence")]
    public void Validate_RejectsSourceIdentityDifferentFromRetainedSourceEvent(string alteredFact)
    {
        var source = UnitPredecessor();
        var target = UnitSuccessor(source, 120m, sameIdentity: true);
        var instruction = Build(source, target, CorporateActionAccountingTypeDto.StockSplit, 2m);
        var projection = alteredFact switch
        {
            "source-action" => instruction.Projection with { SourceCorporateActionId = Guid.NewGuid() },
            "empty-source-action" => instruction.Projection with { SourceCorporateActionId = Guid.Empty },
            "missing-source-evidence" => instruction.Projection with
            {
                EvidenceManifest = instruction.Projection.EvidenceManifest
                    .Where(item => item.Role != CorporateActionProjectionEvidenceRoleDto.SourceEvent).ToArray()
            },
            _ => throw new ArgumentOutOfRangeException(nameof(alteredFact))
        };

        var validate = () => OpenLotSuccessors.Validate(instruction with { Projection = projection });

        validate.Should().Throw<ArgumentException>().WithMessage("*stable source corporate action identity*");
    }

    [Fact]
    public void AdvanceRefunding_LineagePreservesAllocationRoleAndScheduleDOnlyOnRefundedSuccessor()
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var targets = new[] { OpenLotSuccessorTestData.Successor(source, 0.6m), OpenLotSuccessorTestData.Successor(source, 0.4m) };
        var instruction = OpenLotSuccessorTestData.Build(source, targets, advanceRefunding: true);

        var refunded = OpenLotSuccessors.WithLineage(instruction, targets[0].Lot).Acquisition.CorporateActionLineage!;
        var unrefunded = OpenLotSuccessors.WithLineage(instruction, targets[1].Lot).Acquisition.CorporateActionLineage!;

        refunded.BasisAllocationPercent.Should().Be(60m);
        refunded.Role.Should().Be(CorporateActionSuccessorRoleDto.Refunded);
        refunded.ReportingTags.Should().Equal("ScheduleD");
        unrefunded.BasisAllocationPercent.Should().Be(40m);
        unrefunded.Role.Should().Be(CorporateActionSuccessorRoleDto.Unrefunded);
        unrefunded.ReportingTags.Should().BeEmpty();
    }

    [Fact]
    public void Amortization_RejectsSuccessorOriginEvenWithValidUnchangedBulletTerms()
    {
        var instruction = AtomicTaxLotJournalStoreTests.AmortPureInstruction(110m, 10m,
            BondAmortizationMethod.StraightLine, null);
        var baseline = () => OpenLotAmortization.Project(instruction);
        baseline.Should().NotThrow();
        var lot = instruction.ExpectedLot;
        var origin = new OpenLotCorporateActionLineageDto(Guid.NewGuid(), CorporateActionAccountingTypeDto.RegS144AExchange,
            lot.AcquiredDate.AddDays(1), Guid.NewGuid(), 1, 100m, CorporateActionSuccessorRoleDto.Successor, []);
        var changed = instruction with
        {
            ExpectedLot = lot with { Acquisition = lot.Acquisition with { CorporateActionLineage = origin } }
        };

        var project = () => OpenLotAmortization.Project(changed);

        project.Should().Throw<ArgumentException>().WithMessage("*successor*");
    }

    private static OpenLotDto UnitPredecessor()
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        return source with { Acquisition = source.Acquisition with { QuantityBasis = LotQuantityBasis.Units, FaceValueTerms = null } };
    }

    private static OpenLotSuccessorTargetDto UnitSuccessor(OpenLotDto source, decimal quantity, bool sameIdentity)
    {
        var target = OpenLotSuccessorTestData.Successor(source, 1m);
        var remaining = source.OpenQuantity / source.OriginalQuantity;
        return target with
        {
            Lot = target.Lot with
            {
                SecurityId = sameIdentity ? source.SecurityId : target.Lot.SecurityId,
                BookPositionId = sameIdentity ? source.BookPositionId : target.Lot.BookPositionId,
                OriginalQuantity = quantity,
                OpenQuantity = quantity,
                OpenTransactionCostBasis = source.OpenTransactionCostBasis,
                OpenFunctionalCostBasis = source.OpenFunctionalCostBasis,
                Acquisition = target.Lot.Acquisition with
                {
                    TransactionCostBasis = source.Acquisition.TransactionCostBasis * remaining,
                    FunctionalCostBasis = source.Acquisition.FunctionalCostBasis * remaining,
                    CorporateActionLineage = null
                }
            }
        };
    }

    private static OpenLotSuccessorInstructionDto Build(OpenLotDto source, OpenLotSuccessorTargetDto target,
        CorporateActionAccountingTypeDto action, decimal ratio, DateOnly? effectiveDate = null,
        Guid? actionId = null, long sourceEventVersion = 1)
        => OpenLotSuccessorTestData.Build(source, [target], actionType: action,
            policyInputs: new CorporateActionPolicyInputsDto(CarryHoldingPeriod: true),
            splitRatio: action == CorporateActionAccountingTypeDto.MergerStock ? null : ratio,
            effectiveDate: effectiveDate, actionId: actionId, sourceEventVersion: sourceEventVersion,
            canonicalLotTransferJournal: true);
}
