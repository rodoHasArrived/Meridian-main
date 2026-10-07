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
    private const string AssetAccountPath = "assets/investments";

    [Theory]
    [InlineData(CorporateActionAccountingTypeDto.StockSplit)]
    [InlineData(CorporateActionAccountingTypeDto.MergerStock)]
    [InlineData(CorporateActionAccountingTypeDto.AdvanceRefunding)]
    public async Task Preview_ReloadsEveryAuthorityAndReturnsExactReviewedInstructionWithoutWriting(CorporateActionAccountingTypeDto actionType)
    {
        var fixture = new Fixture(actionType);
        using var cancellation = new CancellationTokenSource();

        var instruction = await fixture.Service.PreviewAsync(fixture.Mapped, fixture.Reviewed, AssetAccountPath, cancellation.Token);

        instruction.Intent.Should().Be(AssetLotMutationIntentDto.CorporateAction);
        instruction.AssetAccountId.Should().Be(AssetAccountPath);
        instruction.CorporateAction.Should().BeSameAs(fixture.Reviewed);
        if (actionType == CorporateActionAccountingTypeDto.AdvanceRefunding)
        {
            fixture.Mapped.LotMutations.Mutations.Select(m => m.AllocationPercent).Should().Equal(0.6m, 0.4m);
            fixture.Reviewed.Successors.Select(s => s.BasisAllocationPercent).Should().Equal(60m, 40m);
        }
        await fixture.Lots.Received(1).GetTaxLotsByIdsAsync(fixture.Reviewed.ExpectedLot.LedgerBookId,
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == fixture.Reviewed.ExpectedLot.TaxLotRecordId), cancellation.Token);
        foreach (var security in fixture.SecurityRecords.Values)
        {
            var reads = (fixture.Reviewed.Security.SecurityId == security.SecurityId ? 1 : 0)
                + fixture.Reviewed.Successors.Count(s => s.Security.SecurityId == security.SecurityId);
            await fixture.Securities.Received(reads).GetProjectionAsync(security.SecurityId, cancellation.Token);
        }
        foreach (var position in fixture.PositionRecords.Values)
            await fixture.Positions.Received(1).GetBookPositionAsync(position.PositionId, cancellation.Token);
        fixture.Spine.ReceivedCalls().Should().BeEmpty();
        fixture.Lots.ReceivedCalls().Should().OnlyContain(call => call.GetMethodInfo().Name == nameof(ILedgerJournalStore.GetTaxLotsByIdsAsync)
            || call.GetMethodInfo().Name == nameof(ILedgerJournalStore.ListOpenTaxLotsByAssetScopeAsync));
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
        await fixture.Spine.Received(1).ProjectAsync(fixture.Mapped.Event, Arg.Any<CancellationToken>());
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
        fixture.Spine.ReceivedCalls().Select(call => call.GetMethodInfo().Name)
            .Should().Equal(nameof(IAssetAccountingEventSpineService.ProjectAsync), nameof(IAssetAccountingEventSpineService.BuildPostingCandidateAsync));
        fixture.Lots.ReceivedCalls().Should().OnlyContain(call => call.GetMethodInfo().Name == nameof(ILedgerJournalStore.GetTaxLotsByIdsAsync)
            || call.GetMethodInfo().Name == nameof(ILedgerJournalStore.ListOpenTaxLotsByAssetScopeAsync));
        result.Spine.PostedJournalImpact.Should().BeNull();
    }

    [Theory]
    [InlineData("predecessor")]
    [InlineData("source-security")]
    [InlineData("successor-security")]
    [InlineData("successor-position")]
    public async Task Draft_RejectsStalePredecessorOrReferenceBeforeAnySpineWrite(string changed)
    {
        var fixture = new Fixture();
        switch (changed)
        {
            case "predecessor":
                fixture.RetainedLot = fixture.RetainedLot with { Version = fixture.RetainedLot.Version + 1 };
                break;
            case "source-security":
                var source = fixture.Reviewed.Security;
                fixture.SecurityRecords[source.SecurityId] = source with { Version = source.Version + 1 };
                break;
            case "successor-security":
                var successor = fixture.Reviewed.Successors[0].Security;
                fixture.SecurityRecords[successor.SecurityId] = successor with { DisplayName = "Changed since review" };
                break;
            case "successor-position":
                var positionId = fixture.Reviewed.Successors[0].BookPositionId;
                fixture.PositionRecords[positionId] = fixture.PositionRecords[positionId] with { Version = 99 };
                break;
        }

        var draft = () => fixture.Service.DraftAsync(fixture.Mapped, fixture.Reviewed, AssetAccountPath,
            "preparer", fixture.Mapped.Event.ProjectedAtUtc, "Reviewed stock merger");

        await draft.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stale*");
        fixture.Spine.ReceivedCalls().Should().BeEmpty();
        fixture.Lots.ReceivedCalls().Should().OnlyContain(call => call.GetMethodInfo().Name == nameof(ILedgerJournalStore.GetTaxLotsByIdsAsync)
            || call.GetMethodInfo().Name == nameof(ILedgerJournalStore.ListOpenTaxLotsByAssetScopeAsync));
    }

    [Theory]
    [InlineData("event")]
    [InlineData("plan")]
    [InlineData("no-journal")]
    public async Task Preview_RejectsMismatchedMappedEventPlanOrNoJournalActionBeforeAuthorityReads(string changed)
    {
        var fixture = new Fixture();
        var mapped = changed switch
        {
            "event" => fixture.Mapped with { Event = fixture.Mapped.Event with { EconomicEvent = fixture.Mapped.Event.EconomicEvent with { EventId = Guid.NewGuid() } } },
            "plan" => fixture.Mapped with { LotMutations = fixture.Mapped.LotMutations with { Mutations = [fixture.Reviewed.Mutations[0] with { SourceLotId = Guid.NewGuid() }] } },
            "no-journal" => fixture.Mapped with { PostingSet = fixture.Mapped.PostingSet with { RequiresJournalCandidate = false } },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };

        var preview = () => fixture.Service.PreviewAsync(mapped, fixture.Reviewed, AssetAccountPath);

        await preview.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exact mapped event*");
        fixture.Lots.ReceivedCalls().Should().BeEmpty();
        fixture.Securities.ReceivedCalls().Should().BeEmpty();
        fixture.Positions.ReceivedCalls().Should().BeEmpty();
        fixture.Spine.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Draft_MultipleLotsRetainsEveryReviewedPredecessorInOneEventInstruction()
    {
        var fixture = new Fixture();
        fixture.IncludeSecondPredecessor();
        AssetAccountingPostingCandidateRequestDto? captured = null;
        fixture.Spine.BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(call => { captured = call.Arg<AssetAccountingPostingCandidateRequestDto>(); return fixture.Candidate; });

        await fixture.Service.DraftAsync(fixture.Mapped, fixture.Reviewed, AssetAccountPath,
            "preparer", fixture.Mapped.Event.ProjectedAtUtc, "All reviewed lots");

        captured!.EventAmount.Should().Be(fixture.Reviewed.ExpectedLot.OpenFunctionalCostBasis * 2m);
        captured.LotMutation!.CorporateAction.Should().BeSameAs(fixture.Reviewed);
        await fixture.Lots.Received(1).GetTaxLotsByIdsAsync(fixture.Reviewed.ExpectedLot.LedgerBookId,
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2 && ids.Distinct().Count() == 2), Arg.Any<CancellationToken>());
        await fixture.Spine.Received(1).BuildPostingCandidateAsync(Arg.Any<AssetAccountingPostingCandidateRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_RejectsIncompletePositionInventoryBeforeAnySpineWrite(bool staleAdditionalLot)
    {
        var fixture = new Fixture();
        var reviewed = fixture.Reviewed;
        var mapped = fixture.Mapped;
        fixture.IncludeSecondPredecessor();
        if (staleAdditionalLot)
        {
            reviewed = fixture.Reviewed;
            mapped = fixture.Mapped;
            fixture.AdditionalLots[0] = fixture.AdditionalLots[0] with { Version = fixture.AdditionalLots[0].Version + 1 };
        }

        var preview = () => fixture.Service.PreviewAsync(mapped, reviewed, AssetAccountPath);

        await preview.Should().ThrowAsync<InvalidOperationException>();
        fixture.Spine.ReceivedCalls().Should().BeEmpty();
    }

    private sealed class Fixture
    {
        public ILedgerJournalStore Lots { get; } = Substitute.For<ILedgerJournalStore>();
        public ISecurityMasterStore Securities { get; } = Substitute.For<ISecurityMasterStore>();
        public IInstrumentPositionProjectionStore Positions { get; } = Substitute.For<IInstrumentPositionProjectionStore>();
        public IAssetAccountingEventSpineService Spine { get; } = Substitute.For<IAssetAccountingEventSpineService>();
        public OpenLotCorporateActionInstructionDto Reviewed { get; private set; }
        public Dictionary<Guid, SecurityProjectionRecord> SecurityRecords { get; } = [];
        public Dictionary<Guid, BookPositionDto> PositionRecords { get; } = [];
        public LedgerTaxLotRecord RetainedLot { get; set; }
        public List<LedgerTaxLotRecord> AdditionalLots { get; } = [];
        public CorporateActionAssetAccountingEventProjectionDto Mapped { get; private set; }
        public AssetAccountingPostingCandidateDto Candidate { get; }
        public CanonicalLotCorporateActionService Service { get; }

        public void IncludeSecondPredecessor()
        {
            Reviewed = OpenLotCorporateActionTests.AddPredecessor(Reviewed);
            var group = Reviewed.AdditionalPredecessors![0];
            AdditionalLots.Add(RetainedLot with
            {
                TaxLotRecordId = group.ExpectedLot.TaxLotRecordId,
                LotId = group.ExpectedLot.LotId,
                AcquiredDate = group.ExpectedLot.AcquiredDate,
                Acquisition = group.ExpectedLot.Acquisition
            });
            Mapped = Mapped with
            {
                LotMutations = Mapped.LotMutations with { Mutations = Reviewed.Mutations },
                Event = Mapped.Event with
                {
                    EventAmount = OpenLotCorporateAction.Groups(Reviewed).Sum(item => item.ExpectedLot.OpenFunctionalCostBasis),
                    RetainedEvidence = Mapped.Event.RetainedEvidence.Concat(OpenLotCorporateAction.Evidence(Reviewed)).Distinct().ToArray()
                }
            };
        }

        public Fixture(CorporateActionAccountingTypeDto actionType = CorporateActionAccountingTypeDto.MergerStock)
        {
            var input = OpenLotCorporateActionTests.Instruction(actionType, 60m) with { EffectiveDate = new DateOnly(2026, 9, 1) };
            if (actionType == CorporateActionAccountingTypeDto.AdvanceRefunding)
            {
                var sourceSecurity = input.Security with { Currency = "USD" };
                input = input with
                {
                    Security = sourceSecurity,
                    SecurityEvidence = input.SecurityEvidence with { ContentHashSha256 = OpenLotAmortization.SecurityHash(sourceSecurity) },
                    ExpectedLot = input.ExpectedLot with
                    {
                        OpenTransactionCostBasis = input.ExpectedLot.OpenFunctionalCostBasis,
                        Acquisition = input.ExpectedLot.Acquisition with
                        {
                            AcquisitionCurrency = "USD",
                            AcquisitionFxRateToFunctional = 1m,
                            TransactionCostBasis = input.ExpectedLot.Acquisition.FunctionalCostBasis
                        }
                    },
                    Successors = input.Successors.Select(s =>
                    {
                        var security = s.Security with { Currency = "USD" };
                        return s with { Security = security, SecurityEvidence = s.SecurityEvidence with { ContentHashSha256 = OpenLotAmortization.SecurityHash(security) } };
                    }).ToArray()
                };
            }
            input = input with { Successors = input.Successors.Select(s => s with { PostingAccountPath = AssetAccountPath }).ToArray() };
            (Reviewed, Mapped) = ProjectAndMap(input, assetAccountPath: AssetAccountPath);
            var lot = Reviewed.ExpectedLot;
            var at = new DateTimeOffset(Reviewed.EffectiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var face = lot.Acquisition.QuantityBasis == LotQuantityBasis.Face;
            var scale = face ? 100m : 1m;
            RetainedLot = new(lot.TaxLotRecordId, lot.LedgerBookId, new(Reviewed.SourceAssetAccountId, LedgerAccountType.Asset), lot.LotId,
                lot.AcquiredDate, lot.OriginalQuantity / scale, lot.OpenQuantity / scale, lot.Acquisition.TransactionCostBasis / (lot.OriginalQuantity / scale),
                lot.Acquisition.AcquisitionCurrency, at, at, Version: lot.Version, SecurityId: lot.SecurityId,
                BookPositionId: lot.BookPositionId, OriginalFace: face ? lot.OriginalQuantity : null,
                BookedFactor: lot.Acquisition.FaceValueTerms?.BookedFactor, ParBasis: lot.Acquisition.FaceValueTerms?.ParBasis,
                Acquisition: lot.Acquisition,
                BasisAdjustment: new(Guid.NewGuid(), OpenLotBasisAdjustmentReasons.AverageCostRedistribution,
                    lot.OpenQuantity / scale, lot.OpenTransactionCostBasis, lot.OpenFunctionalCostBasis));
            var source = Mapped.Event;
            var scope = source.Scope;
            var economicEvent = source.EconomicEvent;
            var lineage = source.ProjectionLineage;
            var effect = source.ProjectedEffect;
            var book = new AccountingBookContextDto(lot.LedgerBookId, "fund", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
                "Fund", "USD", AccountingBasisKindDto.Gaap, "policy", "v1");
            SecurityRecords.Add(Reviewed.Security.SecurityId, Reviewed.Security);
            PositionRecords.Add(lot.BookPositionId, new(lot.BookPositionId, lot.SecurityId, Guid.NewGuid(), book,
                BookPositionSides.Asset, "Active", lot.AcquiredDate, Version: Reviewed.ExpectedBookPositionVersion));
            foreach (var successor in Reviewed.Successors)
            {
                SecurityRecords[successor.Security.SecurityId] = successor.Security;
                PositionRecords.Add(successor.BookPositionId, new(successor.BookPositionId, successor.Security.SecurityId,
                    Guid.NewGuid(), book, BookPositionSides.Long, "Active", lot.AcquiredDate, Version: successor.ExpectedBookPositionVersion));
            }
            Lots.GetTaxLotsByIdsAsync(lot.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlyList<LedgerTaxLotRecord>>([RetainedLot, .. AdditionalLots]));
            Lots.ListOpenTaxLotsByAssetScopeAsync(lot.LedgerBookId, lot.SecurityId, lot.BookPositionId,
                    Reviewed.EffectiveDate, Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlyList<LedgerTaxLotRecord>>([RetainedLot, .. AdditionalLots]));
            Securities.GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<SecurityProjectionRecord?>(SecurityRecords.GetValueOrDefault(call.Arg<Guid>())));
            Positions.GetBookPositionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<BookPositionDto?>(PositionRecords.GetValueOrDefault(call.Arg<Guid>())));
            var spine = new AssetAccountingEventSpineDto(economicEvent.EventId, source.EventKind, 1, 7, Reviewed.EffectiveDate,
                source.EventAmount, "USD", scope, economicEvent, lineage, source.RetainedEvidence, ProjectedEffect: effect);
            Spine.ProjectAsync(Arg.Any<ProjectAssetAccountingEventRequestDto>(), Arg.Any<CancellationToken>()).Returns(new AssetAccountingEventSpineAppendResultDto(spine, false));
            var dryRun = new RuleDryRunResultDto("fund", lot.LedgerBookId, "MergerStock", Reviewed.EffectiveDate,
                source.EventAmount, "USD", true, "merger-rule", [], [], []);
            Candidate = new(spine, new(dryRun, "merger-rule", "v1", [], null, null, source.EventAmount,
                source.EventAmount, 0m, true, false, true, false, [], []));
            Service = new(Lots, Securities, Positions, Spine);
        }
    }

    internal static (OpenLotCorporateActionInstructionDto Reviewed, CorporateActionAssetAccountingEventProjectionDto Mapped)
        ProjectAndMap(OpenLotCorporateActionInstructionDto input, AssetAccountingEventScopeDto? scopeOverride = null,
            long expectedPeriodVersion = 3, string assetAccountPath = "Investments", string actor = "mapper",
            DateTimeOffset? now = null, bool aggregateJournalLines = false)
    {
        var lot = input.ExpectedLot;
        var groups = OpenLotCorporateAction.Groups(input);
        var totalQuantity = groups.Sum(group => group.ExpectedLot.OpenQuantity);
        var totalBasis = groups.Sum(group => group.ExpectedLot.OpenFunctionalCostBasis);
        var successors = groups.SelectMany(group => group.Successors).ToArray();
        var at = now ?? new DateTimeOffset(input.EffectiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var positionSnapshot = Guid.NewGuid();
        var lotSnapshot = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var scope = scopeOverride ?? new AssetAccountingEventScopeDto(lot.SecurityId, input.Security.Version, lot.BookPositionId,
            input.ExpectedBookPositionVersion, lot.LedgerBookId, Guid.NewGuid(), AccountingBasisKindDto.Gaap,
            "fund", "tenant", "company");
        CorporateActionProjectionEvidenceDependencyDto Dependency(CorporateActionProjectionEvidenceRoleDto role,
            Guid subjectId, long version, string subjectType, char hash)
            => new(role, role.ToString(), "evidence://corporate-action/" + role, new string(hash, 64), version,
                subjectType, subjectId.ToString("D"));
        CorporateActionProjectionEvidenceDependencyDto[] manifest =
        [
            Dependency(CorporateActionProjectionEvidenceRoleDto.SourceEvent, input.CorporateActionId, 1, "SecurityMasterCorporateAction", 'a'),
            Dependency(CorporateActionProjectionEvidenceRoleDto.PositionSnapshot, positionSnapshot, input.ExpectedBookPositionVersion, "PositionSnapshot", 'b'),
            Dependency(CorporateActionProjectionEvidenceRoleDto.LotSnapshot, lotSnapshot, lot.Version, "LotSnapshot", 'c'),
            Dependency(CorporateActionProjectionEvidenceRoleDto.PolicyDecision, policyId, 1, "CorporateActionPolicyDecision", 'd')
        ];
        var request = new CorporateActionAccountingProjectionRequest(input.CorporateActionId, 1, input.ActionType,
            AccountingBasisKindDto.Gaap, lot.SecurityId, lot.BookPositionId, input.ExpectedBookPositionVersion,
            input.ExpectedBookPositionVersion, input.EffectiveDate, input.EffectiveDate, at, "USD", "SecurityMaster",
            "reviewed-corporate-action", new string('a', 64),
            new(PositionQuantity: totalQuantity, AffectedQuantity: totalQuantity,
                CarryingAmount: totalBasis,
                SplitRatio: input.Successors[0].Quantity / lot.OpenQuantity,
                Successors: successors.GroupBy(successor => (successor.Security.SecurityId, successor.Role, successor.BasisAllocationPercent))
                    .Select(group => new CorporateActionSuccessorAllocationDto(group.Key.SecurityId,
                        group.Key.Role, group.Sum(successor => successor.Quantity), group.Key.BasisAllocationPercent / 100m)).ToArray()),
            new(CarryHoldingPeriod: true), manifest, GeneratedAtUtc: at, CaseId: Guid.NewGuid(), CaseVersion: 1,
            PolicyDecisionVersion: 1, PositionSnapshotId: positionSnapshot,
            AccountingScope: new(scope.TenantId!, scope.CompanyId!, scope.FundProfileId, lot.LedgerBookId, scope.PeriodId, expectedPeriodVersion, "US"),
            LotSnapshotId: lotSnapshot, LotSnapshotVersion: lot.Version, PolicyDecisionId: policyId)
        { CanonicalLotTransferJournal = input.ActionType is CorporateActionAccountingTypeDto.StockSplit or CorporateActionAccountingTypeDto.ReverseStockSplit };
        var projector = new CorporateActionAccountingProjectionService();
        var intent = projector.Project(request);
        intent.Blockers.Should().BeEmpty();
        var authoritative = new List<CorporateActionLotMutationDto>();
        foreach (var group in groups)
        {
            var predecessor = group.ExpectedLot;
            decimal allocatedTransaction = 0m, allocatedFunctional = 0m, allocatedQuantity = 0m;
            for (var index = 0; index < group.Successors.Count; index++)
            {
                var successor = group.Successors[index];
                var last = index == group.Successors.Count - 1;
                var fraction = successor.BasisAllocationPercent / 100m;
                var transaction = last ? predecessor.OpenTransactionCostBasis - allocatedTransaction
                    : decimal.Round(predecessor.OpenTransactionCostBasis * fraction, 10, MidpointRounding.ToEven);
                var functional = last ? predecessor.OpenFunctionalCostBasis - allocatedFunctional
                    : decimal.Round(predecessor.OpenFunctionalCostBasis * fraction, 10, MidpointRounding.ToEven);
                var sourceQuantity = last ? predecessor.OpenQuantity - allocatedQuantity
                    : decimal.Round(predecessor.OpenQuantity * fraction, 12, MidpointRounding.ToEven);
                var mutation = intent.LotMutations!.Mutations.Single(item => item.TargetSecurityId == successor.Security.SecurityId);
                authoritative.Add(mutation with
                {
                    SourceLotId = predecessor.TaxLotRecordId,
                    ExpectedSourceLotVersion = predecessor.Version,
                    SourceBefore = new(predecessor.OpenQuantity, predecessor.OpenFunctionalCostBasis, predecessor.OpenTransactionCostBasis),
                    SourceAfter = new(0m, 0m, 0m),
                    SourceQuantity = sourceQuantity,
                    SourceCarryingAmount = functional,
                    SourceBasisAmount = transaction,
                    TargetLotId = successor.TaxLotRecordId,
                    TargetOperation = CorporateActionLotTargetOperationDto.Create,
                    Quantity = successor.Quantity,
                    CarryingAmount = functional,
                    BasisAmount = transaction,
                    TargetAfter = new(successor.Quantity, functional, transaction)
                });
                allocatedTransaction += transaction;
                allocatedFunctional += functional;
                allocatedQuantity += sourceQuantity;
            }
        }
        var projection = projector.Project(request with { AuthoritativeLotMutations = authoritative });
        projection.Blockers.Should().BeEmpty();
        projection.CanPreparePostingCandidate.Should().BeTrue();
        var reviewed = input with { CorporateActionId = projection.EconomicEvent!.EventId, Mutations = projection.LotMutations!.Mutations };
        var lineage = projection.ProjectionLineage!;
        var projected = OpenLotCorporateAction.Project(reviewed);
        var debits = projected.Select(target => new ProjectedAccountingEffectLineDto(
            target.Successor.PostingAccountPath ?? target.Successor.AssetAccountId, target.OpenFunctionalCostBasis, 0m,
            "USD", Dimensions: scope.Dimensions)).ToArray();
        if (aggregateJournalLines)
            debits = debits.GroupBy(line => line.AccountId).Select(group => group.First() with { Debit = group.Sum(line => line.Debit) }).ToArray();
        var lines = new[] { new ProjectedAccountingEffectLineDto(assetAccountPath, 0m, totalBasis, "USD", Dimensions: scope.Dimensions) }
            .Concat(debits).ToArray();
        var effect = new ProjectedAccountingEffectDto(lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion,
            lineage.ProjectionAsOfDate, totalBasis, totalBasis, "USD", lines);
        var rule = new AccountingRulePackReferenceDto("corporate-action-gaap", "v1", "carrying-transfer", "v1");
        var mappings = projection.PostingSet!.Components.Select((component, index) =>
            new CorporateActionPostingComponentLineMappingDto(index, component.Kind,
                component.Kind == CorporateActionPostingComponentKindDto.CarryingValueRelief
                    ? [new CorporateActionPostingComponentLineAllocationDto(0, component.Amount)]
                    : lines.Skip(1).Select((line, lineIndex) => new CorporateActionPostingComponentLineAllocationDto(lineIndex + 1, line.Debit)).ToArray(),
                "carrying-transfer")).ToArray();
        var mappedEffect = CorporateActionMappedAccountingEffectAttestor.Create(projection, scope, effect, rule, mappings);
        var evidence = manifest.Select(dependency => new RetainedEvidenceIdentityDto(dependency.EvidenceId,
            dependency.EvidenceUri, dependency.ContentHashSha256, "SecurityMaster", dependency.EvidenceId,
            "Accepted", "reviewer", at, input.EffectiveDate, dependency.EvidenceVersion, at, "vault",
            dependency.SubjectType, dependency.SubjectId)).Concat(OpenLotCorporateAction.Evidence(reviewed)).ToList();
        evidence.Add(new("mapped-event", "evidence://corporate-action/mapped-event", new string('a', 64),
            "SecurityMaster", "reviewed-corporate-action", "Accepted", "reviewer", at, input.EffectiveDate, 1,
            at, "vault", AssetAccountingEvidenceSubjects.Event, projection.EconomicEvent.EventId.ToString("D")));
        var mapped = new CorporateActionAssetAccountingEventMapper().Map(new(projection, scope, mappedEffect, expectedPeriodVersion,
            actor, at, evidence));
        mapped.Blockers.Should().BeEmpty();
        mapped.IsMapped.Should().BeTrue();
        return (reviewed, mapped.Projection!);
    }
}
