using Meridian.Tests.TestSupport;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.FinancialOperations.Intercompany;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class IntercompanyProposalReversalDeliveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceReversal_AfterAgreementEnded_UsesExactEventManualReplacementAndOriginalFx(bool targetAlreadyReversed)
    {
        var fixture = IntercompanyCounterpartReversalBuilderTests.Fixture(foreign: true);
        var source = IntercompanyCounterpartReversalBuilderTests.SourceRecord(fixture.Prior.Source);
        var inverse = IntercompanyCounterpartReversalBuilderTests.InverseRecord(source, fixture.Proposal.Source.SourceJournalId, fixture.Proposal.Source.EffectiveDate);
        var targets = new List<LedgerJournalEntryRecord> { fixture.Original };
        if (targetAlreadyReversed)
            targets.Add(IntercompanyCounterpartReversalBuilderTests.InverseRecord(fixture.Original, Guid.NewGuid(), fixture.Proposal.Source.EffectiveDate));
        var claim = fixture.Proposal with { ClaimToken = "worker-lease" };
        var proposals = new Mock<IIntercompanyProposalStore>();
        proposals.Setup(x => x.ClaimAsync("worker", 20, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync([claim]);
        proposals.Setup(x => x.ListAsync("tenant-a", IntercompanyProposalTestData.BookA, It.IsAny<CancellationToken>())).ReturnsAsync([
            fixture.Prior,
            fixture.Prior with { Source = fixture.Prior.Source with { ProposalId = Guid.NewGuid(), TransactionId = "other-equal-amount-event" } },
            fixture.Prior with { Source = fixture.Prior.Source with { ProposalId = Guid.NewGuid(), AccountingArrangementRevisionId = Guid.NewGuid() } }]);
        string? failure = null;
        proposals.Setup(x => x.FailAsync("tenant-a", claim.Source.ProposalId, claim.ClaimToken!, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string, string, CancellationToken>((_, _, _, error, _) => failure = error).Returns(Task.CompletedTask);
        proposals.Setup(x => x.CompleteAsync("tenant-a", claim.Source.ProposalId, claim.ClaimToken!, IntercompanyProposalTestData.BookB,
            claim.Source.ProposalId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var journals = new Mock<ILedgerJournalStore>();
        journals.Setup(x => x.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
            .Returns((LedgerJournalEntryQuery query, CancellationToken _) => Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                (query.LedgerBookId == IntercompanyProposalTestData.BookA ? new[] { source, inverse } : targets.ToArray())
                    .Where(record => query.JournalEntryId is null || record.Entry.JournalEntryId == query.JournalEntryId).ToArray()));
        journals.Setup(x => x.GetLedgerBookAsync(IntercompanyProposalTestData.BookB, It.IsAny<CancellationToken>())).ReturnsAsync(IntercompanyProposalTestData.TargetBook());
        var period = IntercompanyProposalTestData.Period() with { StartDate = new(2026, 7, 1), EndDate = new(2026, 7, 31) };
        journals.Setup(x => x.ListPeriodsAsync(IntercompanyProposalTestData.BookB, null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([period]);
        var agreementStore = new Mock<IIntercompanyAccountingArrangementStore>();
        agreementStore.Setup(x => x.GetAsync("tenant-a", fixture.Agreement.ArrangementId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new IntercompanyAccountingArrangementHistoryDto([new(Guid.NewGuid(), "approval", IntercompanyAccountingArrangementActionDto.Approve,
                "reviewer", IntercompanyProposalTestData.Now, "Retained synthetic approval", fixture.Agreement.Arrangement)]));
        var agreements = IntercompanyArrangementTestData.Service(agreementStore.Object, fixture.Agreement);
        var bindings = new Mock<IIntercompanyAccountBindingStore>();
        bindings.Setup(x => x.ListAsync(IntercompanyProposalTestData.BookB, claim.Source.EffectiveDate, It.IsAny<CancellationToken>())).ReturnsAsync(IntercompanyProposalTestData.TargetBindings());
        var configuration = new Mock<IAccountingConfigurationService>();
        configuration.Setup(x => x.GetWorkspaceAsync("target-fund", IntercompanyProposalTestData.BookB, It.IsAny<CancellationToken>(), "tenant-a", "company-a"))
            .ReturnsAsync(new AccountingConfigurationWorkspaceDto("target-fund", IntercompanyProposalTestData.BookB, AccountingConfigurationStatusDto.Active,
                "fixture-v1", IntercompanyProposalTestData.Now, [], IntercompanyProposalTestData.Chart(), [], [], [], [], TenantId: "tenant-a", CompanyId: "company-a"));
        var owners = new Mock<IFundProfileTenancyRegistry>();
        owners.Setup(x => x.ResolveAsync("target-fund", It.IsAny<CancellationToken>())).ReturnsAsync(new FundProfileOwnership("target-fund", "tenant-a", "company-a"));
        var drafts = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration.Object, new InMemoryAccountingActionAuditStore(), journalStore: journals.Object);
        var fx = new Mock<IConsolidationFxAuthority>(MockBehavior.Strict);
        var delivery = new IntercompanyProposalDeliveryService(proposals.Object, agreements, journals.Object, bindings.Object,
            configuration.Object, drafts, workbench, owners.Object, fx.Object);

        var count = await delivery.RunOnceAsync("worker");

        Assert.Equal(targetAlreadyReversed ? 0 : 1, count);
        fx.VerifyNoOtherCalls();
        if (targetAlreadyReversed)
        {
            Assert.Contains("already has posted corrections", failure);
            Assert.Null(await drafts.GetAsync("target-fund", claim.Source.ProposalId, tenantId: "tenant-a", companyId: "company-a"));
        }
        else
        {
            Assert.Null(failure);
            var saved = await drafts.GetAsync("target-fund", claim.Source.ProposalId, tenantId: "tenant-a", companyId: "company-a");
            Assert.NotNull(saved);
            Assert.Equal(fixture.Prior.ResolvedTargetJournalId, saved!.ReversalOfJournalEntryId);
            Assert.All(saved.Lines, line => Assert.Equal(1.25m, line.TransactionCurrency!.FxRateToFunctional));
        }
    }
}
