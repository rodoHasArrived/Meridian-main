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

    internal CorporateActionAssetAccountingEventMapRequest CreateRedemptionMap(CorporateActionAccountingTypeDto action, int count)
    {
        var ids = Enumerable.Range(1, count).Select(index => Guid.Parse($"99999999-9999-9999-9999-{index:000000000000}")).ToArray();
        var mutations = ids.Select(id => new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.Dispose,
            SecurityId, Quantity: 100m, CarryingAmount: 98m, SourceLotId: id, ExpectedSourceLotVersion: 1,
            SourceBefore: new CorporateActionLotStateSnapshotDto(100m, 98m, 98m), BasisAmount: 98m)).ToArray();
        var projection = _projector.Project(CreateProjectionRequest(action,
            new CorporateActionEconomicsDto(AffectedQuantity: 100m * count, CarryingAmount: 98m * count,
                ParAmount: 100m * count, GrossCashConsideration: 101m * count)) with
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
                100m, 100m, 98m, 98m, 1, new OpenLotAcquisitionDto(LotQuantityBasis.Face, "USD", "USD", 1m, 98m, 98m,
                    new DateOnly(2026, 8, 25), new FaceValueAcquisitionTermsDto(100m, 1m, BondAmortizationMethod.StraightLine, null),
                    [acquisitionEvidence]));
        }).ToArray();
        var disposal = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.Dispose,
            DisposalSelections: lots.Select((lot, index) => new AssetDisposalLotSelectionDto(lot.TaxLotRecordId, lot.LotId,
                lot.Version, 1m, 1m, index, lot.Acquisition.Evidence[0].EvidenceId, 98m, 98m)).ToArray(),
            ReliefMethod: "Fifo", PolicyRevision: "reviewed-v1", AssetAccountId: "Securities", DisposalSalePrice: 101m);
        var scope = new AssetAccountingEventScopeDto(SecurityId, 2, PositionId, 7, LedgerBookId, PeriodId,
            AccountingBasisKindDto.Gaap, "fund-alpha", "tenant-alpha", "company-alpha");
        var lineage = projection.ProjectionLineage!;
        var effect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion,
            lineage.ProjectionAsOfDate, 101m * count, 101m * count, "USD",
            [new("Cash", 101m * count, 0m, "USD"), new("Securities", 0m, 98m * count, "USD"), new("RealizedGain", 0m, 3m * count, "USD")]);
        var mappings = projection.PostingSet!.Components.Select((component, index) =>
            new CorporateActionPostingComponentLineMappingDto(index, component.Kind,
                [new CorporateActionPostingComponentLineAllocationDto(index, component.Amount)], component.Kind.ToString())).ToArray();
        var mapped = CorporateActionMappedAccountingEffectAttestor.Create(projection, scope, effect,
            new AccountingRulePackReferenceDto("redemption-gaap", "v1", "principal-redemption", "v1"), mappings);
        return new CorporateActionAssetAccountingEventMapRequest(projection, scope, mapped, 3, "controller", MappedAtUtc,
            evidence, Redemption: new CorporateActionRedemptionLotPlan(lots, disposal));
    }
}
