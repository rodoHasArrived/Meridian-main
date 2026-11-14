using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.FinancialOperations.Intercompany;
using Meridian.Tests.TestSupport;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Ui;

/// <summary>
/// A retained counterpart proposal whose accounting arrangement is no longer returned by the
/// tenant's arrangement registry. Guards against one unreadable arrangement taking down the whole
/// aged-break list or aborting a delivery worker run instead of being recorded against that
/// single proposal.
/// </summary>
public sealed class IntercompanyProposalMissingArrangementTests
{
    [Fact]
    public async Task Breaks_ArrangementMissingFromTenant_IsReportedAsAgreementAuthorityUnavailableInsteadOfFailingTheList()
    {
        var fixture = new IntercompanyProposalBreakAgingTests.BreakFixture(agreementAvailable: false);

        var aged = (await fixture.Breaks(new(2026, 7, 15))).Should().ContainSingle().Subject;

        aged.Classification.Should().Be("AgreementAuthorityUnavailable");
        aged.AgingBasis.Should().Be("Source effective date");
        aged.AgeFrom.Should().Be(IntercompanyProposalTestData.Date);
        aged.AgeDays.Should().Be(15);
    }

    [Fact]
    public async Task Delivery_ArrangementMissingFromTenant_FailsThatProposalInsteadOfAbortingTheWorkerRun()
    {
        var fixture = new IntercompanyProposalDeliveryRetryTests.DeliveryFixture { ArrangementRetained = false };

        var delivered = await fixture.Service.RunOnceAsync("worker");

        delivered.Should().Be(0);
        fixture.Completions.Should().BeEmpty();
        fixture.Failures.Should().ContainSingle();
        (await fixture.StoredDrafts()).Should().BeEmpty();
    }

    /// <summary>
    /// One worker claim carries three month-end drawdowns. The arrangement of one of them is no
    /// longer returned by the tenant registry; the other two must still reach the destination
    /// review queue in the same run, each under its own claim token and proposal identity.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Delivery_ClaimOfSeveralProposals_FailsOnlyTheOneWithTheMissingArrangementAndStillDeliversTheRest(int missingAt)
    {
        var fixture = new BatchFixture(missingAt);
        var missing = fixture.Claims[missingAt];
        var deliverable = fixture.Claims.Where(claim => claim != missing).ToArray();

        var delivered = await fixture.Service.RunOnceAsync("worker");

        delivered.Should().Be(2);
        var failure = fixture.Failures.Should().ContainSingle().Subject;
        failure.Proposal.Should().Be(missing.Source.ProposalId);
        failure.Token.Should().Be(missing.ClaimToken);
        failure.Error.Should().Be("Accounting arrangement was not found in this tenant.");
        // Later proposals are completed in claim order, each against its own deterministic draft.
        fixture.Completions.Should().Equal(deliverable.Select(claim =>
            (claim.Source.ProposalId, claim.ClaimToken!, IntercompanyProposalTestData.BookB, claim.Source.ProposalId)));
        var drafts = await fixture.StoredDrafts();
        drafts.Select(draft => draft.JournalEntryId).Should().BeEquivalentTo(deliverable.Select(claim => claim.Source.ProposalId));
        drafts.Should().NotContain(draft => draft.IntercompanyProposalId == missing.Source.ProposalId);
        foreach (var claim in deliverable)
        {
            var draft = drafts.Should().ContainSingle(item => item.IntercompanyProposalId == claim.Source.ProposalId).Subject;
            draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
            draft.LedgerBookId.Should().Be(IntercompanyProposalTestData.BookB);
            draft.Lines.Should().HaveCount(2);
            draft.Lines.Sum(line => line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : -line.Amount).Should().Be(0m);
            draft.Lines.Should().Contain(line => line.Dimensions != null && line.Dimensions.IntercompanyTransactionId == claim.Source.TransactionId);
        }
        fixture.VerifyNothingPosted();
    }

    /// <summary>Several retained sources handed to one worker in a single claim.</summary>
    private sealed class BatchFixture
    {
        private readonly Mock<ILedgerJournalStore> _journals = new();

        internal BatchFixture(int missingAt)
        {
            var agreement = IntercompanyProposalTestData.Agreement();
            var period = IntercompanyProposalTestData.Period();
            var targetBindings = IntercompanyProposalTestData.TargetBindings();
            Claims = Enumerable.Range(0, 3).Select(index =>
            {
                var proposal = IntercompanyProposalTestData.Proposal(transaction: $"invoice-10{index}");
                // The missing proposal names an arrangement this tenant's registry does not return.
                return proposal with
                {
                    ClaimToken = $"worker-lease-{index}",
                    Source = index == missingAt ? proposal.Source with { AccountingArrangementId = Guid.NewGuid() } : proposal.Source
                };
            }).ToArray();

            var proposals = new Mock<IIntercompanyProposalStore>();
            proposals.Setup(store => store.ClaimAsync("worker", 20, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(Claims);
            proposals.Setup(store => store.CompleteAsync("tenant-a", It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid proposal, string token, Guid book, Guid journal, CancellationToken _) =>
                {
                    Completions.Add((proposal, token, book, journal));
                    return Task.CompletedTask;
                });
            proposals.Setup(store => store.FailAsync("tenant-a", It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid proposal, string token, string error, CancellationToken _) =>
                {
                    Failures.Add((proposal, token, error));
                    return Task.CompletedTask;
                });

            var sourceBook = new LedgerBookRecord(IntercompanyProposalTestData.BookA, "source-fund", IntercompanyProposalTestData.EntityA,
                FundStructureNodeKindDto.Entity, "Source", "USD", IntercompanyProposalTestData.Now, IntercompanyProposalTestData.Now);
            _journals.Setup(store => store.GetLedgerBookAsync(IntercompanyProposalTestData.BookA, It.IsAny<CancellationToken>())).ReturnsAsync(sourceBook);
            _journals.Setup(store => store.GetLedgerBookAsync(IntercompanyProposalTestData.BookB, It.IsAny<CancellationToken>()))
                .ReturnsAsync(IntercompanyProposalTestData.TargetBook());
            _journals.Setup(store => store.GetPeriodAsync(period.PeriodId, It.IsAny<CancellationToken>())).ReturnsAsync(period);
            _journals.As<IIntercompanyAgreementJournalValidation>()
                .Setup(validation => validation.ValidateIntercompanyAgreementReferencesAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _journals.Setup(store => store.ListPeriodsAsync(IntercompanyProposalTestData.BookB, null, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync([period]);
            _journals.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<LedgerJournalEntryRecord>());

            var arrangementStore = new Mock<IIntercompanyAccountingArrangementStore>();
            arrangementStore.Setup(store => store.GetAsync("tenant-a", agreement.ArrangementId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IntercompanyAccountingArrangementHistoryDto([new(Guid.NewGuid(), "approval", IntercompanyAccountingArrangementActionDto.Approve,
                    "reviewer", IntercompanyProposalTestData.Now, "Retained synthetic approval", agreement.Arrangement)]));
            var bindings = new Mock<IIntercompanyAccountBindingStore>();
            bindings.Setup(store => store.ListAsync(IntercompanyProposalTestData.BookB, IntercompanyProposalTestData.Date, It.IsAny<CancellationToken>()))
                .ReturnsAsync(targetBindings);
            bindings.Setup(store => store.ListAsync(IntercompanyProposalTestData.BookA, IntercompanyProposalTestData.Date, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            var configuration = new Mock<IAccountingConfigurationService>();
            configuration.Setup(service => service.GetWorkspaceAsync("target-fund", IntercompanyProposalTestData.BookB, It.IsAny<CancellationToken>(), "tenant-a", "company-a"))
                .ReturnsAsync(new AccountingConfigurationWorkspaceDto("target-fund", IntercompanyProposalTestData.BookB, AccountingConfigurationStatusDto.Active,
                    "fixture-v1", IntercompanyProposalTestData.Now, [], IntercompanyProposalTestData.Chart(), [], [], [], [], TenantId: "tenant-a", CompanyId: "company-a"));
            configuration.Setup(service => service.GetWorkspaceAsync("source-fund", IntercompanyProposalTestData.BookA, It.IsAny<CancellationToken>(), "tenant-a", "company-a"))
                .ReturnsAsync(new AccountingConfigurationWorkspaceDto("source-fund", IntercompanyProposalTestData.BookA, AccountingConfigurationStatusDto.Active,
                    "fixture-v1", IntercompanyProposalTestData.Now, [], [], [], [], [], [], TenantId: "tenant-a", CompanyId: "company-a"));
            var owners = new Mock<IFundProfileTenancyRegistry>();
            owners.Setup(registry => registry.ResolveAsync("target-fund", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundProfileOwnership("target-fund", "tenant-a", "company-a"));
            var workbench = new ManualJournalEntryWorkbenchService(Drafts, configuration.Object, new InMemoryAccountingActionAuditStore(),
                journalStore: _journals.Object);
            Service = new IntercompanyProposalDeliveryService(proposals.Object,
                IntercompanyArrangementTestData.Service(arrangementStore.Object, agreement), _journals.Object, bindings.Object,
                configuration.Object, Drafts, workbench, owners.Object, new Mock<IConsolidationFxAuthority>(MockBehavior.Strict).Object);
        }

        internal IntercompanyProposalDeliveryService Service { get; }
        internal InMemoryManualJournalEntryDraftStore Drafts { get; } = new();
        internal IReadOnlyList<IntercompanyProposalDto> Claims { get; }
        internal List<(Guid Proposal, string Token, Guid Book, Guid Journal)> Completions { get; } = [];
        internal List<(Guid Proposal, string Token, string Error)> Failures { get; } = [];

        internal Task<IReadOnlyList<ManualJournalEntryDraftDto>> StoredDrafts()
            => Drafts.ListAsync("target-fund", tenantId: "tenant-a", companyId: "company-a");

        internal void VerifyNothingPosted()
            => _journals.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
