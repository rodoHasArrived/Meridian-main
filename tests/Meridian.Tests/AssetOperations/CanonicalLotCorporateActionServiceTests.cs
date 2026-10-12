using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

public sealed class CanonicalLotCorporateActionServiceTests
{
    private const string AssetAccountPath = "Assets:Investment";

    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit)]
    [InlineData(CorporateActionAccountingTypeDto.ReverseStockSplit)]
    [InlineData(CorporateActionAccountingTypeDto.MergerStock)]
    [InlineData(CorporateActionAccountingTypeDto.AdvanceRefunding)]
    [InlineData(CorporateActionAccountingTypeDto.RegS144AExchange)]
    public async Task Preview_ReloadsEveryAuthorityAndReturnsExactReviewedInstructionWithoutWriting(CorporateActionAccountingTypeDto actionType)
    {
        var fixture = new Fixture(actionType);
        using var cancellation = new CancellationTokenSource();

        var instruction = await fixture.Service.PreviewAsync(fixture.Mapped, fixture.Reviewed, AssetAccountPath, cancellation.Token);

        instruction.Intent.Should().Be(AssetLotMutationIntentDto.CorporateAction);
        instruction.AssetAccountId.Should().Be(AssetAccountPath);
        instruction.CorporateAction.Should().BeSameAs(fixture.Reviewed);
        if (actionType == CorporateActionAccountingTypeDto.AdvanceRefunding)
            fixture.Mapped.LotMutations.Mutations.Select(mutation => mutation.AllocationPercent).Should().Equal(0.6m, 0.4m);
        await fixture.Lots.Received(1).GetTaxLotsByIdsAsync(fixture.Reviewed.ExpectedLot.LedgerBookId,
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == fixture.Reviewed.ExpectedLot.TaxLotRecordId), cancellation.Token);
        foreach (var security in fixture.SecurityRecords.Values)
        {
            var reads = (fixture.Reviewed.ExpectedLot.SecurityId == security.SecurityId ? 1 : 0)
                + fixture.Reviewed.Successors.Count(target => target.Lot.SecurityId == security.SecurityId);
            await fixture.Securities.Received(reads).GetProjectionAsync(security.SecurityId, cancellation.Token);
        }
        foreach (var position in fixture.PositionRecords.Values)
            await fixture.Positions.Received(1).GetBookPositionAsync(position.PositionId, cancellation.Token);
        fixture.Spine.ReceivedCalls().Should().BeEmpty();
        fixture.Lots.ReceivedCalls().Should().OnlyContain(call => call.GetMethodInfo().Name == nameof(ILedgerJournalStore.GetTaxLotsByIdsAsync));
    }

    [Fact]
    public async Task Preview_BindsChartPathWhenDurableAccountHasDifferentNameSymbolAndFinancialAccount()
    {
        var fixture = new Fixture();
        const string assetPath = "Assets:Investments";
        var original = fixture.Mapped.Event.ProjectedEffect;
        var mapped = fixture.Mapped with
        {
            Event = fixture.Mapped.Event with
            {
                ProjectedEffect = original with
                {
                    Lines = original.Lines.Select(line => line.Credit > 0m ? line with { AccountId = assetPath } : line).ToArray()
                }
            }
        };
        fixture.RetainedLot = fixture.RetainedLot with
        {
            Account = new LedgerAccount("Investments", LedgerAccountType.Asset, Symbol: "ACME", FinancialAccountId: "broker-account")
        };
        fixture.RetainedLot.Account.ToString().Should().NotBe(assetPath);

        var instruction = await fixture.Service.PreviewAsync(mapped, fixture.Reviewed, assetPath);

        instruction.AssetAccountId.Should().Be(assetPath);
        instruction.CorporateAction.Should().BeSameAs(fixture.Reviewed);
        fixture.Spine.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_RejectsAccountPathDifferentFromTheMappedPredecessorCredit()
    {
        var fixture = new Fixture();
        var preview = () => fixture.Service.PreviewAsync(fixture.Mapped, fixture.Reviewed, "Assets:Other");

        await preview.Should().ThrowAsync<InvalidOperationException>().WithMessage("*mapped predecessor credit*");
        fixture.Lots.ReceivedCalls().Should().BeEmpty();
        fixture.Spine.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Draft_ProjectsMappedEventThenForwardsExactInstructionAtReturnedSpineVersion()
    {
        var fixture = new Fixture();
        var timestamp = fixture.Mapped.Event.ProjectedAtUtc.AddMinutes(1);
        AssetAccountingPostingCandidateRequestDto? captured = null;
        fixture.Spine.BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<AssetAccountingPostingCandidateRequestDto>();
                return fixture.Candidate;
            });

        var result = await fixture.Service.DraftAsync(fixture.Mapped, fixture.Reviewed, AssetAccountPath,
            "preparer", timestamp, "Reviewed stock merger");

        result.Should().BeSameAs(fixture.Candidate);
        captured.Should().NotBeNull();
        captured!.EconomicEvent.Should().BeSameAs(fixture.Mapped.Event.EconomicEvent);
        captured.ProjectionLineage.Should().BeSameAs(fixture.Mapped.Event.ProjectionLineage);
        captured.Scope.Should().BeSameAs(fixture.Mapped.Event.Scope);
        captured.RetainedEvidence.Should().BeSameAs(fixture.Mapped.Event.RetainedEvidence);
        captured.ExpectedSpineVersion.Should().Be(7);
        captured.ExpectedPeriodVersion.Should().Be(fixture.Mapped.Event.ExpectedPeriodVersion);
        captured.EventAmount.Should().Be(fixture.Reviewed.ExpectedLot.OpenFunctionalCostBasis);
        captured.Actor.Should().Be("preparer");
        captured.AccountingTimestamp.Should().Be(timestamp);
        captured.Description.Should().Be("Reviewed stock merger");
        captured.LotMutation!.CorporateAction.Should().BeSameAs(fixture.Reviewed);
        captured.LotMutation.AssetAccountId.Should().Be(AssetAccountPath);
        fixture.Spine.ReceivedCalls().Select(call => call.GetMethodInfo().Name)
            .Should().Equal(nameof(IAssetAccountingEventSpineService.ProjectAsync), nameof(IAssetAccountingEventSpineService.BuildPostingCandidateAsync));
        fixture.Lots.ReceivedCalls().Should().OnlyContain(call => call.GetMethodInfo().Name == nameof(ILedgerJournalStore.GetTaxLotsByIdsAsync));
        result.Spine.PostedJournalImpact.Should().BeNull();
    }

    [Theory]
    [InlineData("predecessor-version")]
    [InlineData("predecessor-acquisition")]
    [InlineData("source-security-version")]
    [InlineData("source-security-hash")]
    [InlineData("successor-security-version")]
    [InlineData("successor-security-hash")]
    [InlineData("source-position")]
    [InlineData("successor-position")]
    public async Task Draft_RejectsStalePredecessorOrReferenceBeforeAnySpineWrite(string changed)
    {
        var fixture = new Fixture();
        var sourceId = fixture.Reviewed.ExpectedLot.SecurityId;
        var successorId = fixture.Reviewed.Successors[0].Lot.SecurityId;
        switch (changed)
        {
            case "predecessor-version":
                fixture.RetainedLot = fixture.RetainedLot with { Version = fixture.RetainedLot.Version + 1 };
                break;
            case "predecessor-acquisition":
                fixture.RetainedLot = fixture.RetainedLot with
                {
                    Acquisition = fixture.RetainedLot.Acquisition! with { HoldingPeriodStartDate = new DateOnly(2020, 1, 1) }
                };
                break;
            case "source-security-version":
                fixture.SecurityRecords[sourceId] = fixture.SecurityRecords[sourceId] with { Version = 99 };
                break;
            case "source-security-hash":
                fixture.SecurityRecords[sourceId] = fixture.SecurityRecords[sourceId] with { DisplayName = "Changed source terms" };
                break;
            case "successor-security-version":
                fixture.SecurityRecords[successorId] = fixture.SecurityRecords[successorId] with { Version = 99 };
                break;
            case "successor-security-hash":
                fixture.SecurityRecords[successorId] = fixture.SecurityRecords[successorId] with { DisplayName = "Changed successor terms" };
                break;
            case "source-position":
            case "successor-position":
                var positionId = changed == "source-position" ? fixture.Reviewed.ExpectedLot.BookPositionId
                    : fixture.Reviewed.Successors[0].Lot.BookPositionId;
                fixture.PositionRecords[positionId] = fixture.PositionRecords[positionId] with { Version = 99 };
                break;
        }

        var draft = () => fixture.Service.DraftAsync(fixture.Mapped, fixture.Reviewed, AssetAccountPath,
            "preparer", fixture.Mapped.Event.ProjectedAtUtc, "Reviewed stock merger");

        await draft.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stale*");
        fixture.Spine.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("event")]
    [InlineData("plan")]
    [InlineData("no-journal")]
    [InlineData("event-instruction-missing")]
    [InlineData("event-instruction-changed")]
    [InlineData("mutation-instruction-missing")]
    [InlineData("mutation-instruction-changed")]
    [InlineData("mutation-account-changed")]
    [InlineData("mutation-correction")]
    [InlineData("fund-scope")]
    public async Task Preview_RejectsMismatchedMappedEventPlanOrInstructionBeforeAuthorityReads(string changed)
    {
        var fixture = new Fixture();
        var changedInstruction = fixture.Reviewed with { ExpectedSecurityVersion = 99 };
        var mapped = changed switch
        {
            "event" => fixture.Mapped with { Event = fixture.Mapped.Event with { EconomicEvent = fixture.Mapped.Event.EconomicEvent with { EventId = Guid.NewGuid() } } },
            "plan" => fixture.Mapped with { LotMutations = fixture.Mapped.LotMutations with { Mutations = [fixture.Mapped.LotMutations.Mutations[0] with { SourceLotId = Guid.NewGuid() }] } },
            "no-journal" => fixture.Mapped with { PostingSet = fixture.Mapped.PostingSet with { RequiresJournalCandidate = false } },
            "event-instruction-missing" => fixture.Mapped with { Event = fixture.Mapped.Event with { CorporateAction = null } },
            "event-instruction-changed" => fixture.Mapped with { Event = fixture.Mapped.Event with { CorporateAction = changedInstruction } },
            "mutation-instruction-missing" => fixture.Mapped with { LotMutation = null },
            "mutation-instruction-changed" => fixture.Mapped with { LotMutation = fixture.Mapped.LotMutation! with { CorporateAction = changedInstruction } },
            "mutation-account-changed" => fixture.Mapped with { LotMutation = fixture.Mapped.LotMutation! with { AssetAccountId = "Assets:Other" } },
            "mutation-correction" => fixture.Mapped with { LotMutation = fixture.Mapped.LotMutation! with { CorrectsMutationBatchId = Guid.NewGuid() } },
            "fund-scope" => fixture.Mapped with { Event = fixture.Mapped.Event with { Scope = fixture.Mapped.Event.Scope with { FundProfileId = "another-fund" } } },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };

        var preview = () => fixture.Service.PreviewAsync(mapped, fixture.Reviewed, AssetAccountPath);

        await preview.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exact mapped event*");
        fixture.Lots.ReceivedCalls().Should().BeEmpty();
        fixture.Securities.ReceivedCalls().Should().BeEmpty();
        fixture.Positions.ReceivedCalls().Should().BeEmpty();
        fixture.Spine.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("book")]
    [InlineData("fund")]
    [InlineData("security")]
    [InlineData("side")]
    [InlineData("inactive")]
    [InlineData("not-effective")]
    [InlineData("expired")]
    public async Task Draft_RejectsSuccessorPositionOutsideMappedScopeBeforeAnySpineWrite(string changed)
    {
        var fixture = new Fixture();
        var targetId = fixture.Reviewed.Successors[0].Lot.BookPositionId;
        var position = fixture.PositionRecords[targetId];
        var date = fixture.Mapped.Event.EconomicEvent.EffectiveDate;
        fixture.PositionRecords[targetId] = changed switch
        {
            "book" => position with { BookContext = position.BookContext with { LedgerBookId = Guid.NewGuid() } },
            "fund" => position with { BookContext = position.BookContext with { FundProfileId = "another-fund" } },
            "security" => position with { SecurityId = Guid.NewGuid() },
            "side" => position with { PositionSide = BookPositionSides.Short },
            "inactive" => position with { Status = "Inactive" },
            "not-effective" => position with { EffectiveFrom = date.AddDays(1) },
            "expired" => position with { EffectiveTo = date.AddDays(-1) },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };

        var draft = () => fixture.Service.DraftAsync(fixture.Mapped, fixture.Reviewed, AssetAccountPath,
            "preparer", fixture.Mapped.Event.ProjectedAtUtc, "Reviewed stock merger");

        await draft.Should().ThrowAsync<InvalidOperationException>().WithMessage("*mapped book scope*");
        fixture.Spine.ReceivedCalls().Should().BeEmpty();
    }

    private sealed class Fixture
    {
        public ILedgerJournalStore Lots { get; } = Substitute.For<ILedgerJournalStore>();
        public ISecurityMasterStore Securities { get; } = Substitute.For<ISecurityMasterStore>();
        public IInstrumentPositionProjectionStore Positions { get; } = Substitute.For<IInstrumentPositionProjectionStore>();
        public IAssetAccountingEventSpineService Spine { get; } = Substitute.For<IAssetAccountingEventSpineService>();
        public OpenLotSuccessorInstructionDto Reviewed { get; }
        public Dictionary<Guid, SecurityProjectionRecord> SecurityRecords { get; } = [];
        public Dictionary<Guid, BookPositionDto> PositionRecords { get; } = [];
        public LedgerTaxLotRecord RetainedLot { get; set; }
        public CorporateActionAssetAccountingEventProjectionDto Mapped { get; }
        public AssetAccountingPostingCandidateDto Candidate { get; }
        public CanonicalLotCorporateActionService Service { get; }

        public Fixture(CorporateActionAccountingTypeDto actionType = CorporateActionAccountingTypeDto.MergerStock)
        {
            var predecessor = OpenLotSuccessorTestData.Predecessor();
            var unitAction = actionType is CorporateActionAccountingTypeDto.StockSplit
                or CorporateActionAccountingTypeDto.ReverseStockSplit or CorporateActionAccountingTypeDto.MergerStock;
            if (unitAction)
                predecessor = predecessor with { Acquisition = predecessor.Acquisition with { QuantityBasis = LotQuantityBasis.Units, FaceValueTerms = null } };
            var targets = actionType == CorporateActionAccountingTypeDto.AdvanceRefunding
                ? new[] { OpenLotSuccessorTestData.Successor(predecessor, 0.6m), OpenLotSuccessorTestData.Successor(predecessor, 0.4m) }
                : new[] { OpenLotSuccessorTestData.Successor(predecessor, 1m) };
            decimal? splitRatio = null;
            if (unitAction)
            {
                var ratio = actionType == CorporateActionAccountingTypeDto.ReverseStockSplit ? 0.5m
                    : actionType == CorporateActionAccountingTypeDto.StockSplit ? 2m : 1.5m;
                var target = targets[0];
                targets[0] = target with
                {
                    Lot = target.Lot with
                    {
                        SecurityId = actionType == CorporateActionAccountingTypeDto.MergerStock ? target.Lot.SecurityId : predecessor.SecurityId,
                        BookPositionId = actionType == CorporateActionAccountingTypeDto.MergerStock ? target.Lot.BookPositionId : predecessor.BookPositionId,
                        OriginalQuantity = predecessor.OpenQuantity * ratio,
                        OpenQuantity = predecessor.OpenQuantity * ratio
                    }
                };
                if (actionType != CorporateActionAccountingTypeDto.MergerStock)
                    splitRatio = ratio;
            }
            SecurityRecords[predecessor.SecurityId] = Security(predecessor.SecurityId, predecessor.Acquisition.AcquisitionCurrency);
            for (var index = 0; index < targets.Length; index++)
            {
                var target = targets[index];
                if (!SecurityRecords.ContainsKey(target.Lot.SecurityId))
                    SecurityRecords[target.Lot.SecurityId] = Security(target.Lot.SecurityId, predecessor.Acquisition.AcquisitionCurrency);
                targets[index] = target with { ExpectedSecurityHash = OpenLotAmortization.SecurityHash(SecurityRecords[target.Lot.SecurityId]) };
            }
            Reviewed = OpenLotSuccessorTestData.Build(predecessor, targets,
                advanceRefunding: actionType == CorporateActionAccountingTypeDto.AdvanceRefunding,
                actionType: actionType, policyInputs: new(CarryHoldingPeriod: true), splitRatio: splitRatio,
                expectedSecurityHash: OpenLotAmortization.SecurityHash(SecurityRecords[predecessor.SecurityId]));
            var mapped = new CorporateActionAssetAccountingEventMapper().Map(OpenLotSuccessorTestData.MapRequest(Reviewed));
            mapped.Blockers.Should().BeEmpty();
            Mapped = mapped.Projection!;
            var lot = Reviewed.ExpectedLot;
            var source = Mapped.Event;
            var at = source.ProjectedAtUtc;
            var face = lot.Acquisition.QuantityBasis == LotQuantityBasis.Face;
            var scale = face ? 100m : 1m;
            RetainedLot = new(lot.TaxLotRecordId, lot.LedgerBookId, new(AssetAccountPath, LedgerAccountType.Asset), lot.LotId,
                lot.AcquiredDate, lot.OriginalQuantity / scale, lot.OpenQuantity / scale, lot.Acquisition.TransactionCostBasis / (lot.OriginalQuantity / scale),
                lot.Acquisition.AcquisitionCurrency, at, at, Version: lot.Version, SecurityId: lot.SecurityId,
                BookPositionId: lot.BookPositionId, OriginalFace: face ? lot.OriginalQuantity : null,
                BookedFactor: lot.Acquisition.FaceValueTerms?.BookedFactor, ParBasis: lot.Acquisition.FaceValueTerms?.ParBasis,
                Acquisition: lot.Acquisition,
                BasisAdjustment: new(Guid.NewGuid(), OpenLotBasisAdjustmentReasons.AverageCostRedistribution,
                    lot.OpenQuantity / scale, lot.OpenTransactionCostBasis, lot.OpenFunctionalCostBasis));
            var book = new AccountingBookContextDto(lot.LedgerBookId, source.Scope.FundProfileId, Guid.NewGuid(), FundStructureNodeKindDto.Fund,
                "Fund", "USD", source.Scope.AccountingBasis, "policy", "v1");
            PositionRecords[lot.BookPositionId] = new(lot.BookPositionId, lot.SecurityId, Guid.NewGuid(), book,
                BookPositionSides.Asset, "Active", lot.AcquiredDate, Version: source.Scope.ExpectedBookPositionVersion);
            foreach (var target in targets)
                PositionRecords[target.Lot.BookPositionId] = new(target.Lot.BookPositionId, target.Lot.SecurityId,
                    Guid.NewGuid(), book, BookPositionSides.Long, "Active", lot.AcquiredDate, Version: target.ExpectedBookPositionVersion);
            Lots.GetTaxLotsByIdsAsync(lot.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlyList<LedgerTaxLotRecord>>([RetainedLot]));
            Securities.GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<SecurityProjectionRecord?>(SecurityRecords.GetValueOrDefault(call.Arg<Guid>())));
            Positions.GetBookPositionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<BookPositionDto?>(PositionRecords.GetValueOrDefault(call.Arg<Guid>())));
            var spine = new AssetAccountingEventSpineDto(source.EconomicEvent.EventId, source.EventKind, 1, 7,
                source.EconomicEvent.EffectiveDate, source.EventAmount, "USD", source.Scope, source.EconomicEvent,
                source.ProjectionLineage, source.RetainedEvidence, ProjectedEffect: source.ProjectedEffect, CorporateAction: Reviewed);
            Spine.ProjectAsync(source, Arg.Any<CancellationToken>()).Returns(new AssetAccountingEventSpineAppendResultDto(spine, false));
            var dryRun = new RuleDryRunResultDto(source.Scope.FundProfileId, lot.LedgerBookId, actionType.ToString(),
                source.EconomicEvent.EffectiveDate, source.EventAmount, "USD", true, "corporate-action-rule", [], [], []);
            Candidate = new(spine, new(dryRun, "corporate-action-rule", "v1", [], null, null, source.EventAmount,
                source.EventAmount, 0m, true, false, true, false, [], []));
            Service = new(Lots, Securities, Positions, Spine);
        }

        private static SecurityProjectionRecord Security(Guid id, string currency)
        {
            var empty = JsonSerializer.SerializeToElement(new { });
            return new(id, "Bond", SecurityStatusDto.Active, "Reviewed security", currency, "ISIN", id.ToString("N"),
                empty, empty, empty, 1, new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), null, [], []);
        }
    }
}
