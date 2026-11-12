using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Tests.AssetOperations;

public sealed partial class CorporateActionAssetAccountingEventMapperTests
{
    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.CallRedemption, 1)]
    [InlineData(CorporateActionAccountingTypeDto.PutRedemption, 2)]
    public void Redemption_RoutesFullCanonicalLotsThroughGovernedDisposal(CorporateActionAccountingTypeDto action, int count)
    {
        var request = CreateRedemptionMap(action, count);
        var result = _sut.Map(request);

        result.Blockers.Should().BeEmpty();
        result.IsMapped.Should().BeTrue();
        var mapped = result.Projection!;
        mapped.Event.EventKind.Should().Be(AssetAccountingEventKindDto.Disposal);
        mapped.Event.EconomicEvent.EventType.Should().Be(AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Disposal));
        mapped.Event.ProjectionLineage.TriggerEvent.Should().Be(mapped.Event.EconomicEvent);
        mapped.Event.Disposal.Should().BeSameAs(request.Redemption!.Disposal);
        mapped.LotMutation.Should().BeSameAs(request.Redemption.Disposal);
        mapped.LotMutation!.DisposalSelections.Should().OnlyContain(selection => selection.Quantity == selection.ExpectedOpenQuantity);
        mapped.LotMutation.DisposalSelections.Sum(selection => selection.ExpectedCostBasis).Should().Be(98m * count);
        AssetLotMutationInstructionValidator.Validate(AssetAccountingEventKindDto.Disposal, mapped.LotMutation,
            mapped.Event.EventAmount, mapped.Event.EconomicEvent.EffectiveDate, mapped.Event.RetainedEvidence).Should().BeEmpty();
        var retained = JsonSerializer.Deserialize<ProjectAssetAccountingEventRequestDto>(JsonSerializer.Serialize(mapped.Event))!;
        JsonSerializer.Serialize(retained.Disposal).Should().Be(JsonSerializer.Serialize(mapped.LotMutation));
        _sut.Map(request).Projection!.PostingIdempotencyKey.Should().Be(mapped.PostingIdempotencyKey);
    }

    [Fact]
    public void Redemption_RequiresTheCanonicalPlanAndRefusesChangedCashBasisOrPartialClose()
    {
        var request = CreateRedemptionMap(CorporateActionAccountingTypeDto.CallRedemption, 1);
        var plan = request.Redemption!;
        var selection = plan.Disposal.DisposalSelections.Single();
        var attempts = new[]
        {
            request with { Redemption = null },
            request with { Redemption = plan with { Disposal = plan.Disposal with { CorrectsMutationBatchId = Guid.NewGuid() } } },
            request with { Redemption = plan with { Disposal = plan.Disposal with { DisposalSalePrice = decimal.MaxValue,
                DisposalSelections = [selection with { Quantity = 2m }] } } },
            request with { Redemption = plan with { Disposal = plan.Disposal with { DisposalSalePrice = 99m } } },
            request with { Redemption = plan with { Disposal = plan.Disposal with { DisposalSelections = [selection with { ExpectedCostBasis = 97m }] } } },
            request with { Redemption = plan with { Disposal = plan.Disposal with { DisposalSelections = [selection with { Quantity = 0.5m }] } } },
            request with { Redemption = plan with { Lots = [plan.Lots[0] with { SecurityId = Guid.NewGuid() }] } },
            request with { Redemption = plan with { Lots = [plan.Lots[0] with { Acquisition = plan.Lots[0].Acquisition with { Direction = OpenLotDirection.Short } }] } }
        };
        foreach (var attempt in attempts)
        {
            var result = _sut.Map(attempt);
            result.IsMapped.Should().BeFalse();
            result.Blockers.Should().Contain(blocker => blocker.Code == "corporate-action.canonical-redemption-required");
        }
    }

    [Fact]
    public void Redemption_RefusesDuplicateSelectionIdsWithTypedBlockers()
    {
        var request = CreateRedemptionMap(CorporateActionAccountingTypeDto.PutRedemption, 2);
        var plan = request.Redemption!;
        var first = plan.Disposal.DisposalSelections[0];
        var result = _sut.Map(request with
        {
            Redemption = plan with
            { Disposal = plan.Disposal with { DisposalSelections = [first, first] } }
        });

        result.IsMapped.Should().BeFalse();
        result.Blockers.Should().Contain(blocker => blocker.Code == "corporate-action.canonical-redemption-required");
    }

    [Fact]
    public void Redemption_OptInPreservesLegacyProjectionAndRefusesIncomeAllocation()
    {
        var request = CreateProjectionRequest(CorporateActionAccountingTypeDto.CallRedemption,
            new CorporateActionEconomicsDto(AffectedQuantity: 100m, CarryingAmount: 98m, ParAmount: 100m,
                GrossCashConsideration: 103m, AccruedIncome: 2m));
        var legacy = _projector.Project(request);
        var canonical = _projector.Project(request with { CanonicalLotRedemptionJournal = true });

        legacy.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        legacy.PostingSet!.Components.Should().Contain(component => component.Kind == CorporateActionPostingComponentKindDto.RedemptionPrincipal);
        JsonSerializer.Serialize(legacy).Should().NotContain("CanonicalLotRedemptionJournal");
        canonical.Status.Should().Be(CorporateActionProjectionStatusDto.Blocked);
        canonical.Blockers.Should().Contain(blocker => blocker.Code == "corporate-action.canonical-redemption-unsupported");
        canonical.ProjectionInputHash.Should().NotBe(legacy.ProjectionInputHash);
    }

    [Fact]
    public void Redemption_AcceptsRoundedSuccessorQuoteWhileRetainingExactBasis()
    {
        var request = CreateRedemptionMap(CorporateActionAccountingTypeDto.CallRedemption, 1,
            quantity: 3m, basis: 100m, proceeds: 102m, quantityBasis: LotQuantityBasis.Units, successor: true);
        var result = _sut.Map(request);

        result.Blockers.Should().BeEmpty();
        result.IsMapped.Should().BeTrue();
        var selection = result.Projection!.LotMutation!.DisposalSelections.Single();
        selection.ExpectedUnitCost.Should().Be(33.333333333333m);
        selection.Quantity.Should().Be(3m);
        selection.ExpectedCostBasis.Should().Be(100m);
        request.Redemption!.Lots.Single().OpenFunctionalCostBasis.Should().Be(100m);
        request.Redemption.Lots.Single().Acquisition.CorporateActionLineage.Should().NotBeNull();
    }

    [Theory]
    [InlineData("wrong-successor-quote")]
    [InlineData("wrong-exact-basis")]
    [InlineData("regular-acquisition")]
    [InlineData("invalid-successor-lineage")]
    public void Redemption_RefusesWrongQuotesAndChangedExactBasis(string scenario)
    {
        var request = CreateRedemptionMap(CorporateActionAccountingTypeDto.CallRedemption, 1,
            quantity: 3m, basis: 100m, proceeds: 102m, quantityBasis: LotQuantityBasis.Units,
            successor: scenario != "regular-acquisition");
        var plan = request.Redemption!;
        var selection = plan.Disposal.DisposalSelections.Single();
        if (scenario == "wrong-successor-quote")
            selection = selection with { ExpectedUnitCost = 33.333333333334m };
        if (scenario == "wrong-exact-basis")
            selection = selection with { ExpectedCostBasis = 99.999999999999m };
        if (scenario == "invalid-successor-lineage")
        {
            var lot = plan.Lots.Single();
            plan = plan with
            {
                Lots = [lot with { Acquisition = lot.Acquisition with
            { CorporateActionLineage = lot.Acquisition.CorporateActionLineage! with { PredecessorTaxLotRecordId = Guid.Empty } } }]
            };
        }
        request = request with
        {
            Redemption = plan with
            { Disposal = plan.Disposal with { DisposalSelections = [selection] } }
        };

        var result = _sut.Map(request);

        result.IsMapped.Should().BeFalse();
        result.Blockers.Should().Contain(blocker => blocker.Code == "corporate-action.canonical-redemption-required");
    }

    internal CorporateActionAssetAccountingEventMapRequest CreateRedemptionMap(CorporateActionAccountingTypeDto action, int count,
        decimal quantity = 100m, decimal basis = 98m, decimal proceeds = 101m,
        LotQuantityBasis quantityBasis = LotQuantityBasis.Face, bool successor = false)
    {
        var ids = Enumerable.Range(1, count).Select(index => Guid.Parse($"99999999-9999-9999-9999-{index:000000000000}")).ToArray();
        var mutations = ids.Select(id => new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.Dispose,
            SecurityId, Quantity: quantity, CarryingAmount: basis, SourceLotId: id, ExpectedSourceLotVersion: 1,
            SourceBefore: new CorporateActionLotStateSnapshotDto(quantity, basis, basis), BasisAmount: basis)).ToArray();
        var projection = _projector.Project(CreateProjectionRequest(action,
            new CorporateActionEconomicsDto(AffectedQuantity: quantity * count, CarryingAmount: basis * count,
                ParAmount: quantity * count, GrossCashConsideration: proceeds * count)) with
        { CanonicalLotRedemptionJournal = true, AuthoritativeLotMutations = mutations });
        projection.Status.Should().Be(CorporateActionProjectionStatusDto.Projected);
        var evidence = CreateRetainedEvidence(projection, projection.EconomicEvent!).ToList();
        var lots = ids.Select((id, index) =>
        {
            var acquisitionEvidence = evidence[0] with
            {
                EvidenceId = $"lot-acquisition-{index}",
                SubjectType = "OpenLotAcquisition",
                SubjectId = id.ToString("D")
            };
            evidence.Add(acquisitionEvidence);
            return new OpenLotDto(id, SecurityId, PositionId, LedgerBookId, $"lot-{index}", new DateOnly(2026, 8, 25),
                quantity, quantity, basis, basis, 1, new OpenLotAcquisitionDto(quantityBasis, "USD", "USD", 1m, basis, basis,
                    new DateOnly(2026, 8, 25), quantityBasis == LotQuantityBasis.Face
                        ? new FaceValueAcquisitionTermsDto(100m, 1m, BondAmortizationMethod.StraightLine, null) : null,
                    [acquisitionEvidence])
                {
                    CorporateActionLineage = successor ? new OpenLotCorporateActionLineageDto(
                        Guid.Parse("aaaaaaaa-9999-9999-9999-999999999999"), CorporateActionAccountingTypeDto.StockSplit,
                        new DateOnly(2026, 8, 25), Guid.Parse("bbbbbbbb-9999-9999-9999-999999999999"), 1,
                        100m, CorporateActionSuccessorRoleDto.Successor, []) : null
                });
        }).ToArray();
        var scale = quantityBasis == LotQuantityBasis.Face ? 100m : 1m;
        var durableQuantity = quantity / scale;
        var quote = decimal.Round(basis / durableQuantity, 12, MidpointRounding.ToEven);
        var disposal = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Dispose,
            DisposalSelections: lots.Select((lot, index) => new AssetDisposalLotSelectionDto(lot.TaxLotRecordId, lot.LotId,
                lot.Version, durableQuantity, durableQuantity, index, lot.Acquisition.Evidence[0].EvidenceId, quote, basis)).ToArray(),
            ReliefMethod: "Fifo", PolicyRevision: "reviewed-v1", AssetAccountId: "Securities", DisposalSalePrice: proceeds / durableQuantity);
        var scope = new AssetAccountingEventScopeDto(SecurityId, 2, PositionId, 7, LedgerBookId, PeriodId,
            AccountingBasisKindDto.Gaap, "fund-alpha", "tenant-alpha", "company-alpha");
        var lineage = projection.ProjectionLineage!;
        var effect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion,
            lineage.ProjectionAsOfDate, proceeds * count, proceeds * count, "USD",
            [new("Cash", proceeds * count, 0m, "USD"), new("Securities", 0m, basis * count, "USD"),
                new("RealizedGain", 0m, (proceeds - basis) * count, "USD")]);
        var mappings = projection.PostingSet!.Components.Select((component, index) =>
            new CorporateActionPostingComponentLineMappingDto(index, component.Kind,
                [new CorporateActionPostingComponentLineAllocationDto(index, component.Amount)], component.Kind.ToString())).ToArray();
        var mapped = CorporateActionMappedAccountingEffectAttestor.Create(projection, scope, effect,
            new AccountingRulePackReferenceDto("redemption-gaap", "v1", "principal-redemption", "v1"), mappings);
        return new CorporateActionAssetAccountingEventMapRequest(projection, scope, mapped, 3, "controller", MappedAtUtc,
            evidence, Redemption: new CorporateActionRedemptionLotPlan(lots, disposal));
    }
}
