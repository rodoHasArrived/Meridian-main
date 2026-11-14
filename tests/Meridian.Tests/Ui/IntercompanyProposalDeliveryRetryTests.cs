using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.FinancialOperations.Intercompany;
using Meridian.Tests.TestSupport;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Ui;

/// <summary>
/// Month-end intercompany loan drawdown: entity A posts its receivable and the retained proposal
/// is delivered into entity B's review queue. Guards against a worker crash or lease re-claim
/// creating a second counterpart, a generated draft overwriting a reviewer's decision, an
/// already-booked manual counterpart being duplicated, delivery into a book the source tenant or
/// company does not own, and the accounting date being moved to find an open period.
/// </summary>
public sealed class IntercompanyProposalDeliveryRetryTests
{
    [Fact]
    public async Task Delivery_SavesOneBalancedDestinationDraftForReviewAndNeverPosts()
    {
        var fixture = new DeliveryFixture();

        var delivered = await fixture.Service.RunOnceAsync("worker");

        delivered.Should().Be(1);
        fixture.Failures.Should().BeEmpty();
        fixture.Completions.Should().Equal((fixture.Claim.ClaimToken!, IntercompanyProposalTestData.BookB, fixture.ProposalId));
        var draft = (await fixture.StoredDrafts()).Should().ContainSingle().Subject;
        draft.JournalEntryId.Should().Be(fixture.ProposalId);
        draft.ValidationIssues.Where(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            .Select(issue => $"{issue.Code}: {issue.Message}").Should().BeEmpty();
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        draft.ApprovedBy.Should().BeNull();
        draft.LedgerBookId.Should().Be(IntercompanyProposalTestData.BookB);
        draft.TenantId.Should().Be("tenant-a");
        draft.CompanyId.Should().Be("company-a");
        draft.IntercompanyProposalId.Should().Be(fixture.ProposalId);
        draft.IntercompanyProposalSourceDigest.Should().Be(fixture.Claim.SourceDigest);
        draft.Lines.Should().HaveCount(2);
        draft.Lines.Sum(line => line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : -line.Amount).Should().Be(0m);
        var payable = draft.Lines.Should().ContainSingle(line => line.Dimensions!.ChartAccountNodeId == "b-payable").Subject;
        payable.Side.Should().Be(AccountingTemplateLineSideDto.Credit);
        payable.Amount.Should().Be(100m);
        payable.Currency.Should().Be("USD");
        payable.Dimensions!.EntityId.Should().Be(IntercompanyProposalTestData.EntityB.ToString("D"));
        payable.Dimensions.CounterpartyId.Should().Be(IntercompanyProposalTestData.EntityA.ToString("D"));
        payable.Dimensions.AccountingArrangementId.Should().Be(IntercompanyProposalTestData.AccountingArrangementId.ToString("D"));
        payable.Dimensions.AccountingArrangementRevisionId.Should().Be(IntercompanyProposalTestData.RevisionId.ToString("D"));
        payable.Dimensions.IntercompanyAgreementId.Should().Be(fixture.Claim.Source.IntercompanyAgreementId);
        payable.Dimensions.IntercompanyAgreementVersion.Should().Be(fixture.Claim.Source.IntercompanyAgreementVersion);
        payable.Dimensions.IntercompanyTransactionId.Should().Be("invoice-100");
        payable.Dimensions.AccountBindingRevisionId.Should().NotBeNullOrWhiteSpace();
        fixture.AgreementReviews.Should().NotBeEmpty("the destination draft is reviewed against agreement authority before it is retained");
        fixture.AgreementReviews.Should().OnlyContain(write => write.Entry.JournalEntryId == fixture.ProposalId
            && write.Entry.Lines.Any(line => line.Dimensions != null
                && line.Dimensions.IntercompanyAgreementId == fixture.Claim.Source.IntercompanyAgreementId
                && line.Dimensions.IntercompanyAgreementVersion == fixture.Claim.Source.IntercompanyAgreementVersion));
        fixture.VerifyNothingPosted();
    }

    [Fact]
    public async Task RestartAfterDraftSavedButBeforeReceipt_RecoversSameDraftWithoutOverwritingReviewerDecision()
    {
        var fixture = new DeliveryFixture();
        fixture.FailNextCompletionWith(new TimeoutException("Connection lost before the delivery receipt committed."));

        await FluentActions.Awaiting(() => fixture.Service.RunOnceAsync("worker")).Should().ThrowAsync<TimeoutException>();

        fixture.Completions.Should().BeEmpty();
        var saved = (await fixture.StoredDrafts()).Should().ContainSingle().Subject;
        // A reviewer acts on the generated draft while the delivery receipt is still missing.
        var reviewed = saved with { Status = ManualJournalEntryStatusDto.Rejected, Version = saved.Version + 1, Memo = "Reviewer rejected the generated counterpart" };
        await fixture.Drafts.SaveAsync(reviewed);
        fixture.Reclaim("restarted-worker-lease");

        var delivered = await fixture.Service.RunOnceAsync("worker");

        delivered.Should().Be(1);
        fixture.Failures.Should().BeEmpty();
        fixture.Completions.Should().Equal(("restarted-worker-lease", IntercompanyProposalTestData.BookB, fixture.ProposalId));
        var retained = (await fixture.StoredDrafts()).Should().ContainSingle().Subject;
        retained.JournalEntryId.Should().Be(saved.JournalEntryId);
        retained.Status.Should().Be(ManualJournalEntryStatusDto.Rejected);
        retained.Version.Should().Be(reviewed.Version);
        retained.Memo.Should().Be(reviewed.Memo);
        ConsolidationHash(retained.Lines).Should().Be(ConsolidationHash(saved.Lines));
        fixture.VerifyNothingPosted();
    }

    [Fact]
    public async Task DuplicateClaimAfterCompletedDelivery_AcknowledgesExistingDraftInsteadOfCreatingAnother()
    {
        var fixture = new DeliveryFixture();
        (await fixture.Service.RunOnceAsync("worker")).Should().Be(1);
        var first = (await fixture.StoredDrafts()).Should().ContainSingle().Subject;
        fixture.Reclaim("second-worker-lease");

        (await fixture.Service.RunOnceAsync("worker")).Should().Be(1);

        var retained = (await fixture.StoredDrafts()).Should().ContainSingle().Subject;
        retained.Should().BeEquivalentTo(first);
        fixture.Completions.Select(item => (item.Book, item.Journal)).Distinct().Should()
            .Equal((IntercompanyProposalTestData.BookB, fixture.ProposalId));
        fixture.Failures.Should().BeEmpty();
        fixture.VerifyNothingPosted();
    }

    [Fact]
    public async Task RedeliveryAgainstDraftRetainingDifferentSourceDigest_IsRefusedWithoutOverwrite()
    {
        var fixture = new DeliveryFixture();
        var foreign = IntercompanyProposalDraftBuilder.Build(fixture.Claim with { SourceDigest = "different-retained-source-digest" },
            fixture.Agreement, IntercompanyProposalTestData.TargetBook(), fixture.Period, [], fixture.TargetBindings,
            IntercompanyProposalTestData.Chart(), null, IntercompanyProposalTestData.Now);
        await fixture.Drafts.SaveAsync(foreign);

        var delivered = await fixture.Service.RunOnceAsync("worker");

        delivered.Should().Be(0);
        fixture.Completions.Should().BeEmpty();
        fixture.Failures.Should().ContainSingle().Which.Should().Contain("already retains different source authority");
        (await fixture.StoredDrafts()).Should().ContainSingle()
            .Which.IntercompanyProposalSourceDigest.Should().Be("different-retained-source-digest");
    }

    [Theory]
    [InlineData("invoice-100", false)]
    [InlineData("another-equal-amount-invoice", true)]
    public async Task PostedManualCounterpart_BlocksGenerationOnlyForTheSameAgreementAndTransactionIdentity(
        string postedTransaction, bool expectDraft)
    {
        var fixture = new DeliveryFixture();
        // Same agreement, period and amount in both rows; only the transaction identity differs.
        fixture.TargetRecords.Add(fixture.ManualCounterpart(postedTransaction, 100m));

        var delivered = await fixture.Service.RunOnceAsync("worker");

        var candidateQuery = fixture.Queries.Should().ContainSingle(query => query.LineDimensions != null).Subject;
        candidateQuery.LedgerBookId.Should().Be(IntercompanyProposalTestData.BookB);
        candidateQuery.LineDimensions!.AccountingArrangementId.Should().Be(IntercompanyProposalTestData.AccountingArrangementId.ToString("D"));
        candidateQuery.LineDimensions.IntercompanyTransactionId.Should().Be("invoice-100");
        if (expectDraft)
        {
            delivered.Should().Be(1);
            fixture.Failures.Should().BeEmpty();
            (await fixture.StoredDrafts()).Should().ContainSingle();
        }
        else
        {
            delivered.Should().Be(0);
            fixture.Completions.Should().BeEmpty();
            fixture.Failures.Should().ContainSingle().Which.Should().Contain("possible manually booked counterpart");
            (await fixture.StoredDrafts()).Should().BeEmpty();
        }
        fixture.VerifyNothingPosted();
    }

    [Theory]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-company")]
    [InlineData("unbound")]
    public async Task DestinationBookOutsideSourceTenantOrCompany_FailsDeliveryWithoutCreatingDraft(string ownership)
    {
        var fixture = new DeliveryFixture();
        fixture.TargetOwner = ownership switch
        {
            "foreign-tenant" => new("target-fund", "tenant-b", "company-a"),
            "foreign-company" => new("target-fund", "tenant-a", "company-b"),
            _ => null
        };

        var delivered = await fixture.Service.RunOnceAsync("worker");

        delivered.Should().Be(0);
        fixture.Completions.Should().BeEmpty();
        fixture.Failures.Should().ContainSingle().Which.Should().Contain("retained owner differs");
        (await fixture.Drafts.ListAsync("target-fund")).Should().BeEmpty();
        fixture.VerifyNothingPosted();
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("other-month")]
    [InlineData("overlapping")]
    public async Task DestinationWithoutExactlyOneOpenPeriodForSourceDate_FailsInsteadOfMovingTheDate(string periods)
    {
        var fixture = new DeliveryFixture();
        fixture.Periods = periods switch
        {
            "closed" => [fixture.Period with { Status = "Closed" }],
            "other-month" => [fixture.Period with { PeriodNo = 7, StartDate = new(2026, 7, 1), EndDate = new(2026, 7, 31) }],
            _ => [fixture.Period, fixture.Period with { PeriodId = Guid.NewGuid() }]
        };

        var delivered = await fixture.Service.RunOnceAsync("worker");

        delivered.Should().Be(0);
        fixture.Completions.Should().BeEmpty();
        fixture.Failures.Should().ContainSingle().Which.Should().Contain("Exactly one open destination period");
        (await fixture.StoredDrafts()).Should().BeEmpty();
    }

    private static string ConsolidationHash(IReadOnlyList<ManualJournalEntryLineDto> lines)
        => Meridian.FinancialOperations.Consolidation.ConsolidationService.Hash(lines);

    internal sealed class DeliveryFixture
    {
        private readonly Mock<IIntercompanyProposalStore> _proposals = new();
        private readonly Mock<ILedgerJournalStore> _journals = new();
        private Exception? _completionFailure;

        internal DeliveryFixture()
        {
            Agreement = IntercompanyProposalTestData.Agreement();
            Period = IntercompanyProposalTestData.Period();
            Periods = [Period];
            TargetBindings = IntercompanyProposalTestData.TargetBindings();
            TargetOwner = new("target-fund", "tenant-a", "company-a");
            Claim = IntercompanyProposalTestData.Proposal() with { ClaimToken = "worker-lease" };

            _proposals.Setup(store => store.ClaimAsync("worker", 20, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => [Claim]);
            _proposals.Setup(store => store.CompleteAsync("tenant-a", ProposalId, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, string token, Guid book, Guid journal, CancellationToken _) =>
                {
                    if (_completionFailure is { } failure)
                    {
                        _completionFailure = null;
                        return Task.FromException(failure);
                    }
                    Completions.Add((token, book, journal));
                    return Task.CompletedTask;
                });
            _proposals.Setup(store => store.FailAsync("tenant-a", ProposalId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string _, Guid _, string _, string error, CancellationToken _) =>
                {
                    Failures.Add(error);
                    return Task.CompletedTask;
                });

            var sourceBook = new LedgerBookRecord(IntercompanyProposalTestData.BookA, "source-fund", IntercompanyProposalTestData.EntityA,
                FundStructureNodeKindDto.Entity, "Source", "USD", IntercompanyProposalTestData.Now, IntercompanyProposalTestData.Now);
            _journals.Setup(store => store.GetLedgerBookAsync(IntercompanyProposalTestData.BookA, It.IsAny<CancellationToken>())).ReturnsAsync(sourceBook);
            _journals.Setup(store => store.GetLedgerBookAsync(IntercompanyProposalTestData.BookB, It.IsAny<CancellationToken>()))
                .ReturnsAsync(IntercompanyProposalTestData.TargetBook());
            _journals.Setup(store => store.GetPeriodAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => Periods.FirstOrDefault(period => period.PeriodId == id));
            // The durable ledger owns agreement authority; this seam records what the workbench asks it to review.
            _journals.As<IIntercompanyAgreementJournalValidation>()
                .Setup(validation => validation.ValidateIntercompanyAgreementReferencesAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()))
                .Returns((LedgerJournalEntryWrite write, CancellationToken _) =>
                {
                    AgreementReviews.Add(write);
                    return Task.CompletedTask;
                });
            _journals.Setup(store => store.ListPeriodsAsync(IntercompanyProposalTestData.BookB, null, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Periods);
            _journals.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
                .Returns((LedgerJournalEntryQuery query, CancellationToken _) =>
                {
                    Queries.Add(query);
                    var population = query.LedgerBookId == IntercompanyProposalTestData.BookB ? TargetRecords : [];
                    return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(population.Where(record => query.LineDimensions is not { } wanted
                        || record.Entry.Lines.Any(line => line.Dimensions is { } actual
                            && (wanted.AccountingArrangementId is null || actual.AccountingArrangementId == wanted.AccountingArrangementId)
                            && (wanted.IntercompanyTransactionId is null || actual.IntercompanyTransactionId == wanted.IntercompanyTransactionId))).ToArray());
                });

            var arrangementStore = new Mock<IIntercompanyAccountingArrangementStore>();
            var history = new IntercompanyAccountingArrangementHistoryDto([new(Guid.NewGuid(), "approval", IntercompanyAccountingArrangementActionDto.Approve,
                "reviewer", IntercompanyProposalTestData.Now, "Retained synthetic approval", Agreement.Arrangement)]);
            arrangementStore.Setup(store => store.GetAsync("tenant-a", Agreement.ArrangementId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ArrangementRetained ? history : null);
            var bindings = new Mock<IIntercompanyAccountBindingStore>();
            bindings.Setup(store => store.ListAsync(IntercompanyProposalTestData.BookB, Claim.Source.EffectiveDate, It.IsAny<CancellationToken>()))
                .ReturnsAsync(TargetBindings);
            bindings.Setup(store => store.ListAsync(IntercompanyProposalTestData.BookA, Claim.Source.EffectiveDate, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            var configuration = new Mock<IAccountingConfigurationService>();
            configuration.Setup(service => service.GetWorkspaceAsync("target-fund", IntercompanyProposalTestData.BookB, It.IsAny<CancellationToken>(), "tenant-a", "company-a"))
                .ReturnsAsync(new AccountingConfigurationWorkspaceDto("target-fund", IntercompanyProposalTestData.BookB, AccountingConfigurationStatusDto.Active,
                    "fixture-v1", IntercompanyProposalTestData.Now, [], IntercompanyProposalTestData.Chart(), [], [], [], [], TenantId: "tenant-a", CompanyId: "company-a"));
            configuration.Setup(service => service.GetWorkspaceAsync("source-fund", IntercompanyProposalTestData.BookA, It.IsAny<CancellationToken>(), "tenant-a", "company-a"))
                .ReturnsAsync(new AccountingConfigurationWorkspaceDto("source-fund", IntercompanyProposalTestData.BookA, AccountingConfigurationStatusDto.Active,
                    "fixture-v1", IntercompanyProposalTestData.Now, [], [], [], [], [], [], TenantId: "tenant-a", CompanyId: "company-a"));
            var owners = new Mock<IFundProfileTenancyRegistry>();
            owners.Setup(registry => registry.ResolveAsync("target-fund", It.IsAny<CancellationToken>())).ReturnsAsync(() => TargetOwner);
            var workbench = new ManualJournalEntryWorkbenchService(Drafts, configuration.Object, new InMemoryAccountingActionAuditStore(),
                journalStore: _journals.Object);
            Service = new IntercompanyProposalDeliveryService(_proposals.Object,
                IntercompanyArrangementTestData.Service(arrangementStore.Object, Agreement), _journals.Object, bindings.Object,
                configuration.Object, Drafts, workbench, owners.Object, new Mock<IConsolidationFxAuthority>(MockBehavior.Strict).Object);
        }

        internal IntercompanyProposalDeliveryService Service { get; }
        internal InMemoryManualJournalEntryDraftStore Drafts { get; } = new();
        internal ResolvedIntercompanyAccountingArrangementDto Agreement { get; }
        internal LedgerAccountingPeriod Period { get; }
        internal IReadOnlyList<LedgerAccountingPeriod> Periods { get; set; }
        internal IReadOnlyList<IntercompanyAccountBindingDto> TargetBindings { get; }
        internal FundProfileOwnership? TargetOwner { get; set; }
        /// <summary>When false, the tenant's arrangement store no longer returns the source's arrangement.</summary>
        internal bool ArrangementRetained { get; set; } = true;
        internal IntercompanyProposalDto Claim { get; private set; }
        internal Guid ProposalId => Claim.Source.ProposalId;
        internal List<LedgerJournalEntryRecord> TargetRecords { get; } = [];
        internal List<LedgerJournalEntryQuery> Queries { get; } = [];
        internal List<(string Token, Guid Book, Guid Journal)> Completions { get; } = [];
        internal List<string> Failures { get; } = [];
        internal List<LedgerJournalEntryWrite> AgreementReviews { get; } = [];

        internal void FailNextCompletionWith(Exception failure) => _completionFailure = failure;

        /// <summary>The same retained source is handed out again under a new lease after expiry or restart.</summary>
        internal void Reclaim(string claimToken) => Claim = Claim with { ClaimToken = claimToken, Attempts = Claim.Attempts + 1 };

        internal Task<IReadOnlyList<ManualJournalEntryDraftDto>> StoredDrafts()
            => Drafts.ListAsync("target-fund", tenantId: "tenant-a", companyId: "company-a");

        internal void VerifyNothingPosted()
            => _journals.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);

        /// <summary>A counterpart the destination team already posted by hand under the same agreement.</summary>
        internal LedgerJournalEntryRecord ManualCounterpart(string transaction, decimal amount)
        {
            var id = Guid.NewGuid();
            var dimensions = new LedgerLineDimensionSet(EntityId: IntercompanyProposalTestData.EntityB.ToString("D"),
                CounterpartyId: IntercompanyProposalTestData.EntityA.ToString("D"))
            {
                IntercompanyAgreementId = Agreement.CanonicalAgreementId,
                IntercompanyAgreementVersion = Agreement.CanonicalAgreementVersion,
                AccountingArrangementId = Agreement.ArrangementId.ToString("D"),
                AccountingArrangementRevisionId = Agreement.RevisionId.ToString("D"),
                ChartAccountNodeId = "b-payable",
                IntercompanyTransactionId = transaction
            };
            var entry = new JournalEntry(id, IntercompanyProposalTestData.Now, "Manually booked counterpart", [
                new(Guid.NewGuid(), id, IntercompanyProposalTestData.Now, new("Liabilities:Due to A", LedgerAccountType.Liability), 0m, amount, "Manually booked counterpart", dimensions),
                new(Guid.NewGuid(), id, IntercompanyProposalTestData.Now, new("Assets:Cash", LedgerAccountType.Asset), amount, 0m, "Manually booked counterpart")],
                new JournalEntryMetadata(EffectiveDate: IntercompanyProposalTestData.Date));
            return new(entry, IntercompanyProposalTestData.BookB, Period.PeriodId, null, null, 1, IntercompanyProposalTestData.Now);
        }
    }
}
