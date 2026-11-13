using Meridian.Tests.TestSupport;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.FinancialOperations.Intercompany;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class IntercompanyProposalResolutionTests
{
    [Fact]
    public async Task Breaks_ReopenAfterSourceReversal_AndResolveOnlyAfterTargetFamilyReverses_AsOf()
    {
        var fixture = Fixture();
        fixture.SourceRecords.Add(Record(IntercompanyProposalTestData.BookA, fixture.Proposal.Source.SourceJournalId,
            new(2026, 7, 1), -100m, 2));
        fixture.TargetRecords.Add(Record(IntercompanyProposalTestData.BookB, fixture.Proposal.TargetJournalId,
            new(2026, 7, 2), 100m, 2));

        Assert.Empty(await fixture.Service.BreaksAsync("tenant-a", IntercompanyProposalTestData.BookA, new(2026, 6, 30)));
        var broken = Assert.Single(await fixture.Service.BreaksAsync("tenant-a", IntercompanyProposalTestData.BookA, new(2026, 7, 1)));
        Assert.Equal("AmountOrIdentityMismatch", broken.Classification);
        Assert.Contains("correction families", broken.Detail, StringComparison.Ordinal);
        Assert.Empty(await fixture.Service.BreaksAsync("tenant-a", IntercompanyProposalTestData.BookA, new(2026, 7, 2)));
    }

    [Fact]
    public async Task LegacyCanonicalReversalMetadata_ReopensActualFamilyBreak()
    {
        var fixture = Fixture();
        var reversal = Record(IntercompanyProposalTestData.BookA, fixture.Proposal.Source.SourceJournalId,
            new(2026, 7, 1), -100m, 2);
        fixture.SourceRecords.Add(reversal with
        {
            SourceJournalEntryId = null,
            Entry = new(reversal.Entry.JournalEntryId, reversal.Entry.Timestamp, reversal.Entry.Description, reversal.Entry.Lines,
                reversal.Entry.Metadata with
                {
                    Tags = new Dictionary<string, string>
                    { [LedgerJournalReversal.ReversalOfTag] = fixture.Proposal.Source.SourceJournalId.ToString("D") }
                })
        });

        Assert.Single(await fixture.Service.BreaksAsync("tenant-a", IntercompanyProposalTestData.BookA, new(2026, 7, 1)));
    }

    [Fact]
    public async Task SeparateSettlementWithSameTransactionLabel_IsNotMergedIntoCorrectionFamily()
    {
        var fixture = Fixture();
        fixture.SourceRecords.Add(Record(IntercompanyProposalTestData.BookA, null, new(2026, 7, 1), -30m, 2));

        Assert.Empty(await fixture.Service.BreaksAsync("tenant-a", IntercompanyProposalTestData.BookA, new(2026, 7, 1)));
    }

    [Fact]
    public async Task SourceJournalWithSeveralAttributedEvents_MatchesOnlyItsRetainedTransaction()
    {
        var fixture = Fixture();
        var original = fixture.SourceRecords[0];
        var other = Record(IntercompanyProposalTestData.BookA, null, new(2026, 6, 30), 100m, 2);
        var otherLines = other.Entry.Lines.Select(line => new LedgerEntry(Guid.NewGuid(), original.Entry.JournalEntryId,
            original.Entry.Timestamp, line.Account, line.Debit, line.Credit, line.Description,
            line.Dimensions is { } dimensions ? dimensions with { IntercompanyTransactionId = "another-equal-invoice" } : null)).ToArray();
        fixture.SourceRecords[0] = original with
        {
            Entry = new(original.Entry.JournalEntryId, original.Entry.Timestamp,
            original.Entry.Description, [.. original.Entry.Lines, .. otherLines], original.Entry.Metadata)
        };

        Assert.Empty(await fixture.Service.BreaksAsync("tenant-a", IntercompanyProposalTestData.BookA, new(2026, 6, 30)));
    }

    [Fact]
    public async Task ActualBookPopulations_AreReadOnceAcrossSeveralProposalChecks()
    {
        var fixture = Fixture();
        var secondSource = Record(IntercompanyProposalTestData.BookA, null, new(2026, 6, 30), 100m, 2);
        var secondTarget = Record(IntercompanyProposalTestData.BookB, null, new(2026, 6, 30), -100m, 2);
        fixture.SourceRecords.Add(secondSource);
        fixture.TargetRecords.Add(secondTarget);
        var second = fixture.Proposal with { Source = fixture.Proposal.Source with { ProposalId = Guid.NewGuid(), SourceJournalId = secondSource.Entry.JournalEntryId }, TargetJournalId = secondTarget.Entry.JournalEntryId };
        fixture.Proposals.Setup(x => x.ListAsync("tenant-a", IntercompanyProposalTestData.BookA, It.IsAny<CancellationToken>())).ReturnsAsync([fixture.Proposal, second]);

        Assert.Empty(await fixture.Service.BreaksAsync("tenant-a", IntercompanyProposalTestData.BookA, new(2026, 6, 30)));

        fixture.Journals.Verify(x => x.QueryAsync(It.Is<LedgerJournalEntryQuery>(query => query.LedgerBookId == IntercompanyProposalTestData.BookA), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Journals.Verify(x => x.QueryAsync(It.Is<LedgerJournalEntryQuery>(query => query.LedgerBookId == IntercompanyProposalTestData.BookB), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(IntercompanyProposalStatusDto.Drafted)]
    [InlineData(IntercompanyProposalStatusDto.Pending)]
    public async Task RejectedGeneratedDraft_CanBeResolvedByPostedManualCounterpart_IncludingCrashBeforeComplete(IntercompanyProposalStatusDto status)
    {
        var fixture = Fixture();
        fixture.TargetRecords.Clear();
        var manual = Record(IntercompanyProposalTestData.BookB, null, new(2026, 6, 30), -100m, 1);
        fixture.TargetRecords.Add(manual);
        fixture.SetProposal(fixture.Proposal with
        {
            Status = status,
            TargetBookId = status == IntercompanyProposalStatusDto.Pending ? null : IntercompanyProposalTestData.BookB,
            TargetJournalId = status == IntercompanyProposalStatusDto.Pending ? null : fixture.Proposal.Source.ProposalId
        });
        await fixture.Drafts.SaveAsync(GeneratedDraft(fixture.Proposal, ManualJournalEntryStatusDto.Rejected));
        RejectedIntercompanyDraftEvidenceDto? retained = null;
        fixture.Proposals.Setup(x => x.LinkRejectedDraftAsync("tenant-a", fixture.Proposal.Source.ProposalId,
            It.IsAny<LinkIntercompanyCounterpartRequest>(), "reviewer", It.IsAny<RejectedIntercompanyDraftEvidenceDto>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, LinkIntercompanyCounterpartRequest, string, RejectedIntercompanyDraftEvidenceDto, CancellationToken>((_, _, _, _, evidence, _) => retained = evidence)
            .Returns(Task.CompletedTask);

        await fixture.Service.LinkAsync("tenant-a", fixture.Proposal.Source.ProposalId,
            new(Guid.NewGuid(), IntercompanyProposalTestData.BookB, manual.Entry.JournalEntryId, "Rejected generated draft; reviewed manual journal"), "reviewer");

        Assert.NotNull(retained);
        Assert.Equal(fixture.Proposal.Source.ProposalId, retained!.JournalEntryId);
        Assert.Equal(2, retained.Version);
        Assert.Equal(ManualJournalEntryStatusDto.Rejected,
            (await fixture.Drafts.GetAsync("target-fund", retained.JournalEntryId, tenantId: "tenant-a", companyId: "company-a"))!.Status);
    }

    [Fact]
    public async Task PendingDelivery_WithSavedApprovedGeneratedDraft_CannotLinkAnotherCounterpart()
    {
        var fixture = Fixture();
        fixture.SetProposal(fixture.Proposal with { Status = IntercompanyProposalStatusDto.Pending, TargetBookId = null, TargetJournalId = null });
        await fixture.Drafts.SaveAsync(GeneratedDraft(fixture.Proposal, ManualJournalEntryStatusDto.Approved));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.LinkAsync("tenant-a", fixture.Proposal.Source.ProposalId,
            new(Guid.NewGuid(), IntercompanyProposalTestData.BookB, Guid.NewGuid(), "Cannot replace an approved generated draft"), "reviewer"));

        fixture.Proposals.Verify(x => x.LinkAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<LinkIntercompanyCounterpartRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Proposals.Verify(x => x.LinkRejectedDraftAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<LinkIntercompanyCounterpartRequest>(), It.IsAny<string>(), It.IsAny<RejectedIntercompanyDraftEvidenceDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectedDraftAuthority_HoldsCommandLeaseUntilLinkIsRetained()
    {
        var fixture = Fixture();
        await fixture.Drafts.SaveAsync(GeneratedDraft(fixture.Proposal, ManualJournalEntryStatusDto.Rejected));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = fixture.Workbench.WithIntercompanyDraftLinkAuthorityAsync("target-fund", "tenant-a", "company-a",
            IntercompanyProposalTestData.BookB, fixture.Proposal.Source.ProposalId, fixture.Proposal.Source.ProposalId,
            fixture.Proposal.SourceDigest, true, async _ => { entered.SetResult(); await release.Task; });
        await entered.Task;
        var competingCommand = fixture.Recovery.OpenSessionAsync();
        Assert.False(competingCommand.IsCompleted);
        release.SetResult();
        await first;
        await using var nextCommand = await competingCommand;
    }

    private static ManualJournalEntryDraftDto GeneratedDraft(IntercompanyProposalDto proposal, ManualJournalEntryStatusDto status)
        => IntercompanyProposalDraftBuilder.Build(proposal, IntercompanyProposalTestData.Agreement(), IntercompanyProposalTestData.TargetBook(),
            IntercompanyProposalTestData.Period(), [], IntercompanyProposalTestData.TargetBindings(), IntercompanyProposalTestData.Chart(), null, IntercompanyProposalTestData.Now)
            with
        { Status = status, Version = 2, TenantId = "tenant-a", CompanyId = "company-a" };

    private static ResolutionFixture Fixture()
    {
        var proposal = IntercompanyProposalTestData.Proposal();
        var source = Record(IntercompanyProposalTestData.BookA, null, new(2026, 6, 30), 100m, 1);
        var target = Record(IntercompanyProposalTestData.BookB, null, new(2026, 6, 30), -100m, 1, proposal.Source.ProposalId);
        proposal = proposal with
        {
            Source = proposal.Source with { SourceJournalId = source.Entry.JournalEntryId },
            Status = IntercompanyProposalStatusDto.Drafted,
            TargetBookId = IntercompanyProposalTestData.BookB,
            TargetJournalId = target.Entry.JournalEntryId
        };
        var initial = proposal;
        var sourceRecords = new List<LedgerJournalEntryRecord> { source };
        var targetRecords = new List<LedgerJournalEntryRecord> { target };
        var journals = new Mock<ILedgerJournalStore>();
        var sourceBook = new LedgerBookRecord(IntercompanyProposalTestData.BookA, "source-fund", IntercompanyProposalTestData.EntityA,
            FundStructureNodeKindDto.Entity, "Source", "USD", IntercompanyProposalTestData.Now, IntercompanyProposalTestData.Now);
        journals.Setup(x => x.GetLedgerBookAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns((Guid book, CancellationToken _) =>
            Task.FromResult<LedgerBookRecord?>(book == IntercompanyProposalTestData.BookA ? sourceBook : IntercompanyProposalTestData.TargetBook()));
        journals.Setup(x => x.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>())).Returns((LedgerJournalEntryQuery query, CancellationToken _) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>((query.LedgerBookId == IntercompanyProposalTestData.BookA ? sourceRecords : targetRecords).ToArray()));
        var proposals = new Mock<IIntercompanyProposalStore>();
        proposals.Setup(x => x.GetAsync("tenant-a", proposal.Source.ProposalId, It.IsAny<CancellationToken>())).ReturnsAsync(() => proposal);
        proposals.Setup(x => x.ListAsync("tenant-a", IntercompanyProposalTestData.BookA, It.IsAny<CancellationToken>())).ReturnsAsync(() => new[] { proposal });
        var agreementStore = new Mock<IIntercompanyAccountingArrangementStore>();
        agreementStore.Setup(x => x.GetAsync("tenant-a", IntercompanyProposalTestData.AccountingArrangementId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntercompanyAccountingArrangementHistoryDto([new(Guid.NewGuid(), "synthetic", IntercompanyAccountingArrangementActionDto.Approve,
                "reviewer", IntercompanyProposalTestData.Now, "Approved synthetic source", IntercompanyProposalTestData.Agreement().Arrangement)]));
        var agreements = IntercompanyArrangementTestData.Service(agreementStore.Object, IntercompanyProposalTestData.Agreement());
        var owners = new Mock<IFundProfileTenancyRegistry>();
        owners.Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns((string fund, CancellationToken _) =>
            Task.FromResult<FundProfileOwnership?>(new(fund, "tenant-a", "company-a")));
        var drafts = new InMemoryManualJournalEntryDraftStore();
        var configuration = Mock.Of<IAccountingConfigurationService>();
        var recovery = new InMemoryManualJournalMutationRecoveryStore();
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration, new InMemoryAccountingActionAuditStore(), journalStore: journals.Object, mutationRecovery: recovery);
        var service = new IntercompanyProposalDeliveryService(proposals.Object, agreements, journals.Object,
            Mock.Of<IIntercompanyAccountBindingStore>(), configuration, drafts, workbench, owners.Object, Mock.Of<IConsolidationFxAuthority>(), new FixtureClock());
        return new(service, proposals, journals, drafts, workbench, recovery, sourceRecords, targetRecords, initial, value => proposal = value);
    }

    private static LedgerJournalEntryRecord Record(Guid book, Guid? parent, DateOnly date, decimal signed, long sequence, Guid? id = null)
    {
        var journal = id ?? Guid.NewGuid();
        var source = book == IntercompanyProposalTestData.BookA;
        var timestamp = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var dimensions = new LedgerLineDimensionSet(EntityId: (source ? IntercompanyProposalTestData.EntityA : IntercompanyProposalTestData.EntityB).ToString("D"),
            CounterpartyId: (source ? IntercompanyProposalTestData.EntityB : IntercompanyProposalTestData.EntityA).ToString("D"))
        {
            IntercompanyAgreementId = IntercompanyProposalTestData.Agreement().CanonicalAgreementId,
            IntercompanyAgreementVersion = IntercompanyProposalTestData.Agreement().CanonicalAgreementVersion,
            AccountingArrangementId = IntercompanyProposalTestData.AccountingArrangementId.ToString("D"),
            AccountingArrangementRevisionId = IntercompanyProposalTestData.RevisionId.ToString("D"),
            ChartAccountNodeId = source ? "a-receivable" : "b-payable",
            IntercompanyTransactionId = "invoice-100"
        };
        var entry = new JournalEntry(journal, timestamp, "Posted finance", [
            new(Guid.NewGuid(), journal, timestamp, new(source ? "Assets:Due from B" : "Liabilities:Due to A", source ? LedgerAccountType.Asset : LedgerAccountType.Liability),
                Math.Max(0m, signed), Math.Max(0m, -signed), "Posted finance", dimensions),
            new(Guid.NewGuid(), journal, timestamp, new("Assets:Cash", LedgerAccountType.Asset), Math.Max(0m, -signed), Math.Max(0m, signed), "Posted finance")],
            new JournalEntryMetadata(EffectiveDate: date));
        return new(entry, book, Guid.NewGuid(), null, null, sequence, timestamp,
            SourceJournalEntryId: parent, PostingKind: parent is null ? LedgerPostingKindDto.Originating : LedgerPostingKindDto.Adjustment);
    }

    private sealed record ResolutionFixture(IntercompanyProposalDeliveryService Service, Mock<IIntercompanyProposalStore> Proposals,
        Mock<ILedgerJournalStore> Journals, InMemoryManualJournalEntryDraftStore Drafts, ManualJournalEntryWorkbenchService Workbench,
        InMemoryManualJournalMutationRecoveryStore Recovery, List<LedgerJournalEntryRecord> SourceRecords,
        List<LedgerJournalEntryRecord> TargetRecords, IntercompanyProposalDto Proposal, Action<IntercompanyProposalDto> SetProposal);
    private sealed class FixtureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 7, 3, 0, 0, 0, TimeSpan.Zero);
    }
}
