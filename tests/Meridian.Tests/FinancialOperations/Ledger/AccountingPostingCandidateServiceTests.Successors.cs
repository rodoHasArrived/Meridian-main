using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidateServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildAuthoritativeCandidateWriteAsync_SuccessorsPreserveAccountsAndReviewedInstruction(bool advanceRefunding)
    {
        var fixture = await CreateSuccessorCandidateFixtureAsync(advanceRefunding);

        var result = await fixture.Service.BuildAuthoritativeCandidateWriteAsync(fixture.Request, fixture.Authority);

        result.Candidate.HasBlockingIssues.Should().BeFalse("{0}",
            string.Join("; ", result.Candidate.Issues.Select(item => item.Message)));
        result.Candidate.CanSubmitForApproval.Should().BeTrue();
        result.Write.Should().NotBeNull();
        var write = result.Write!;
        var relief = write.Entry.Lines.Single(line => line.Credit > 0m);
        relief.Account.Should().Be(fixture.Predecessor.Account);
        relief.Account.Name.Should().Be("Original investments");
        relief.Account.AccountType.Should().Be(LedgerAccountType.Asset);
        relief.Account.Symbol.Should().Be("PREDECESSOR-EUR");
        relief.Account.FinancialAccountId.Should().Be("custody-account-alpha");
        relief.Dimensions!.InstrumentId.Should().Be(fixture.Instruction.ExpectedLot.SecurityId);
        relief.Dimensions.PositionId.Should().Be(fixture.Instruction.ExpectedLot.BookPositionId);
        relief.Credit.Should().Be(fixture.Instruction.ExpectedLot.OpenFunctionalCostBasis);
        foreach (var successor in fixture.Instruction.Successors)
        {
            var line = write.Entry.Lines.Single(item => item.Dimensions!.InstrumentId == successor.Lot.SecurityId);
            line.Account.Should().Be(new LedgerAccount("Successor investments", LedgerAccountType.Asset,
                FinancialAccountId: "custody-account-alpha"));
            line.Dimensions!.PositionId.Should().Be(successor.Lot.BookPositionId);
            line.Debit.Should().Be(successor.Lot.OpenFunctionalCostBasis);
        }

        var command = write.PostingCommand!;
        command.LotCorporateAction.Should().BeEquivalentTo(fixture.Instruction);
        command.ExpectedVersion.Should().Be(fixture.Authority.ExpectedPeriodVersion);
        command.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.Pending);
        command.Evidence.Select(item => item.EvidenceId).Should()
            .BeEquivalentTo(fixture.Request.RetainedEvidence.Select(item => item.EvidenceId));
        command.Evidence.Should().OnlyContain(item => item.ContentHash != null && item.EvidenceVersion > 0);
        write.Entry.Metadata.Tags!["securityMasterProvenance"].Should().Contain("currency:EUR");
        command.LotCorporateAction!.Successors.Sum(item => item.Lot.OpenTransactionCostBasis)
            .Should().Be(fixture.Instruction.ExpectedLot.OpenTransactionCostBasis);
        command.LotCorporateAction.Successors.Sum(item => item.Lot.OpenFunctionalCostBasis)
            .Should().Be(fixture.Instruction.ExpectedLot.OpenFunctionalCostBasis);
        if (advanceRefunding)
        {
            command.LotCorporateAction.Projection.LotMutations!.Mutations[0].ReportingTags.Should().Equal("ScheduleD");
            command.LotCorporateAction.Projection.LotMutations.Mutations[1].ReportingTags.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("security")]
    [InlineData("position")]
    [InlineData("amount")]
    [InlineData("side")]
    public async Task BuildAuthoritativeCandidateWriteAsync_UnreviewedSuccessorLineBlocksCandidate(string changedFact)
    {
        var fixture = await CreateSuccessorCandidateFixtureAsync(advanceRefunding: false);
        var target = fixture.GeneratedLines[0];
        fixture.GeneratedLines[0] = changedFact switch
        {
            "security" => target with { Dimensions = target.Dimensions! with { InstrumentId = Guid.NewGuid() } },
            "position" => target with { Dimensions = target.Dimensions! with { PositionId = Guid.NewGuid() } },
            "amount" => target with { Amount = target.Amount + 0.01m },
            "side" => target with { Side = AccountingTemplateLineSideDto.Credit },
            _ => throw new ArgumentOutOfRangeException(nameof(changedFact))
        };

        var result = await fixture.Service.BuildAuthoritativeCandidateWriteAsync(fixture.Request, fixture.Authority);

        result.Write.Should().BeNull();
        result.Candidate.HasBlockingIssues.Should().BeTrue();
        result.Candidate.Issues.Should().Contain(item => item.BlocksCandidate &&
            item.Code.StartsWith("posting-candidate.book-context-generated-line-", StringComparison.Ordinal));
    }

    private static async Task<SuccessorCandidateFixture> CreateSuccessorCandidateFixtureAsync(bool advanceRefunding)
    {
        var source = OpenLotSuccessorTestData.Predecessor();
        var targets = advanceRefunding
            ? new[] { OpenLotSuccessorTestData.Successor(source, 0.6m), OpenLotSuccessorTestData.Successor(source, 0.4m) }
            : new[] { OpenLotSuccessorTestData.Successor(source, 1m) };
        var instruction = OpenLotSuccessorTestData.Build(source, targets, advanceRefunding, expectedPeriodVersion: 7);
        var projection = instruction.Projection;
        var scope = projection.AccountingScope!;
        var economicEvent = projection.EconomicEvent!;
        var dimensions = new LedgerDimensionSetDto(FundId: scope.FundProfileId,
            InstrumentId: source.SecurityId, BookId: source.LedgerBookId.ToString("D"))
        { PositionId = source.BookPositionId };
        var mapRequest = OpenLotSuccessorTestData.MapRequest(instruction, dimensions);
        var now = mapRequest.ProjectedAtUtc;
        var basis = projection.Treatment.AccountingBasis;
        var ownerId = Guid.NewGuid();
        var book = new LedgerBookDto(source.LedgerBookId, scope.FundProfileId, ownerId, FundStructureNodeKindDto.Fund,
            "Successor book", "USD", now, now, AccountingBasis: basis,
            AccountingPolicyId: "successor-policy", AccountingPolicyVersion: "v1");
        var period = new LedgerPeriodDto(scope.PeriodId, source.LedgerBookId, 2026, 8, "August 2026",
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), LedgerPeriodStatusDto.Open, now, null,
            scope.ExpectedPeriodVersion, basis, book.AccountingPolicyId, book.AccountingPolicyVersion);
        var policyService = new AccountingPolicyService();
        await policyService.CreatePolicyAsync(new CreateAccountingPolicyRequest(basis, book.AccountingPolicyId,
            "v1", "Successor basis carryover", new DateOnly(2026, 1, 1),
            RulePack: new AccountingPolicyRulePackDto("successor-pack", "v1",
            [new AccountingPolicyRuleDto("posting.successor", AccountingTreatmentKindDto.Reclassification,
                SourceEventType: economicEvent.EventType)])));
        var lines = targets.Select((target, index) => new GeneratedPostingLineDto($"target-{index}", "assets/successor",
            AccountingTemplateLineSideDto.Debit, "source-amount", target.Lot.OpenFunctionalCostBasis, "USD",
            dimensions with { InstrumentId = target.Lot.SecurityId, PositionId = target.Lot.BookPositionId })).Append(
            new GeneratedPostingLineDto("predecessor", "assets/predecessor", AccountingTemplateLineSideDto.Credit,
                "source-amount", source.OpenFunctionalCostBasis, "USD", dimensions)).ToArray();
        var configuration = Substitute.For<IAccountingConfigurationService>();
        configuration.DryRunPostingRuleAsync(Arg.Any<RuleDryRunRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new RuleDryRunResultDto(scope.FundProfileId, source.LedgerBookId, economicEvent.EventType,
                economicEvent.EffectiveDate, projection.EventAmount, "USD", true, "posting.successor",
                [new AccountingRuleDryRunMatchDto("posting.successor", "Successor carryover", "v1", 100, true, [], [])],
                [], [], lines));
        configuration.GetWorkspaceAsync(scope.FundProfileId, source.LedgerBookId, Arg.Any<CancellationToken>(), scope.TenantId, scope.CompanyId)
            .Returns(new AccountingConfigurationWorkspaceDto(scope.FundProfileId, source.LedgerBookId,
                AccountingConfigurationStatusDto.Active, "v1", now, [book],
                [new ChartOfAccountsNodeDto("predecessor", "assets/predecessor", "Original investments", "Asset"),
                 new ChartOfAccountsNodeDto("successor", "assets/successor", "Successor investments", "Asset")],
                [], [], [], []));
        var predecessor = new LedgerTaxLotRecord(source.TaxLotRecordId, source.LedgerBookId,
            new LedgerAccount("Original investments", LedgerAccountType.Asset, "PREDECESSOR-EUR", "custody-account-alpha"),
            source.LotId, source.AcquiredDate, source.OriginalQuantity, source.OpenQuantity,
            source.Acquisition.TransactionCostBasis / source.OriginalQuantity, source.Acquisition.AcquisitionCurrency,
            now, now, Version: source.Version, SecurityId: source.SecurityId, BookPositionId: source.BookPositionId,
            Acquisition: source.Acquisition,
            BasisAdjustment: new OpenLotBasisAdjustmentDto(Guid.NewGuid(), OpenLotBasisAdjustmentReasons.Amortization,
                source.OpenQuantity, source.OpenTransactionCostBasis, source.OpenFunctionalCostBasis));
        var lots = Substitute.For<ILedgerJournalStore>();
        lots.GetTaxLotsByIdsAsync(source.LedgerBookId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([predecessor]);
        var request = new PostingRuleJournalCandidateRequestDto(scope.FundProfileId, economicEvent.EventType,
            projection.EventAmount, "USD", economicEvent.EffectiveDate, "preparer", source.LedgerBookId,
            scope.PeriodId, now, "Exchange predecessor for reviewed successors", basis, source.LedgerBookId, dimensions,
            CorrelationId: economicEvent.CorrelationId, SourceEventId: economicEvent.EventId,
            PolicyId: book.AccountingPolicyId, TreatmentKind: AccountingTreatmentKindDto.Reclassification,
            EvidenceLinks: mapRequest.RetainedEvidence.Select(item => item.EvidenceUri).ToArray(),
            TenantId: scope.TenantId, CompanyId: scope.CompanyId)
        {
            RetainedEvidence = mapRequest.RetainedEvidence,
            BookContext = new AccountingBookContextDto(source.LedgerBookId, scope.FundProfileId, ownerId,
                FundStructureNodeKindDto.Fund, book.DisplayName, "USD", basis, book.AccountingPolicyId,
                book.AccountingPolicyVersion, scope.PeriodId, dimensions),
            BookPositionId = source.BookPositionId,
            EconomicEvent = economicEvent,
            ProjectionLineage = projection.ProjectionLineage,
            RulePackReference = new AccountingRulePackReferenceDto("successor-pack", "v1", "posting.successor", "v1"),
            AssetLotMutation = new AssetLotMutationInstructionDto(AssetLotMutationIntentDto.CorporateAction,
                CorporateAction: instruction),
            ExpectedPeriodVersion = scope.ExpectedPeriodVersion
        };
        var authority = new AssetAccountingCandidateAuthorityContext(economicEvent.EventId, economicEvent.EventVersion,
            2, 3, new string('a', 64), AssetAccountingEventKindDto.CorporateAction, source.SecurityId, source.BookPositionId,
            projection.LotMutations!.ExpectedPositionVersion, source.LedgerBookId, scope.PeriodId, scope.ExpectedPeriodVersion,
            "successor-pack", "v1", instruction.ExpectedSecurityVersion);
        var service = new AccountingPostingCandidateService(configuration,
            new AccountingJournalDraftService(policyService, new AccountingBasisProjectionService(policyService)),
            new StaticLedgerBookService(book, period), policyService, taxLotStore: lots);
        return new SuccessorCandidateFixture(service, request, authority, instruction, predecessor, lines);
    }

    private sealed record SuccessorCandidateFixture(
        AccountingPostingCandidateService Service,
        PostingRuleJournalCandidateRequestDto Request,
        AssetAccountingCandidateAuthorityContext Authority,
        OpenLotSuccessorInstructionDto Instruction,
        LedgerTaxLotRecord Predecessor,
        GeneratedPostingLineDto[] GeneratedLines);
}
