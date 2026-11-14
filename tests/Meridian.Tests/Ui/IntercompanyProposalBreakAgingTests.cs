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
/// Aged intercompany break review after a quarter-end loan drawdown whose counterpart is late,
/// blocked, unposted or booked at a different rate. Guards against a break being aged from an
/// undeclared date, a not-yet-due item showing as overdue, delivery states being collapsed into
/// one bucket, and a pure booking-rate (translation) difference being reported as an
/// intercompany break when the original-currency amounts reciprocate.
/// </summary>
public sealed class IntercompanyProposalBreakAgingTests
{
    private static readonly DateOnly Effective = IntercompanyProposalTestData.Date;

    [Fact]
    public async Task UndeliveredSource_AgesFromContractualDueDateAndDeclaresThatBasis()
    {
        var fixture = new BreakFixture();

        (await fixture.Breaks(Effective.AddDays(-1))).Should().BeEmpty("a source effective after the as-of date is not yet a break");
        var notYetDue = (await fixture.Breaks(new(2026, 7, 15))).Should().ContainSingle().Subject;
        var overdue = (await fixture.Breaks(new(2026, 8, 14))).Should().ContainSingle().Subject;

        notYetDue.AgingBasis.Should().Be("Contractual due date");
        notYetDue.AgeFrom.Should().Be(Effective.AddDays(30));
        notYetDue.AgeDays.Should().Be(0);
        overdue.AgingBasis.Should().Be("Contractual due date");
        overdue.AgeFrom.Should().Be(new DateOnly(2026, 7, 30));
        overdue.AgeDays.Should().Be(15);
        overdue.Classification.Should().Be("MissingCounterpart");
        overdue.ProposalId.Should().Be(fixture.Proposal.Source.ProposalId);
        overdue.AccountingArrangementId.Should().Be(IntercompanyProposalTestData.AccountingArrangementId);
        overdue.TransactionId.Should().Be("invoice-100");
        overdue.SourceJournalId.Should().Be(fixture.Proposal.Source.SourceJournalId);
        overdue.TargetJournalId.Should().BeNull();
        overdue.IntercompanyAgreementId.Should().Be(fixture.Proposal.Source.IntercompanyAgreementId);
        overdue.IntercompanyAgreementVersion.Should().Be(fixture.Proposal.Source.IntercompanyAgreementVersion);
        overdue.IntercompanyAgreementPayloadHash.Should().Be(fixture.Proposal.Source.IntercompanyAgreementPayloadHash);
    }

    [Fact]
    public async Task RetainedDueDate_TakesPrecedenceOverAgreementSettlementTerms()
    {
        var fixture = new BreakFixture();
        fixture.Proposal = fixture.Proposal with { DueDate = new DateOnly(2026, 7, 10) };

        var aged = (await fixture.Breaks(new(2026, 7, 15))).Should().ContainSingle().Subject;

        aged.AgingBasis.Should().Be("Contractual due date");
        aged.AgeFrom.Should().Be(new DateOnly(2026, 7, 10));
        aged.AgeDays.Should().Be(5);
    }

    [Fact]
    public async Task AgreementWithoutSettlementTerms_AgesFromSourceEffectiveDateAndDeclaresThatBasis()
    {
        var baseline = IntercompanyProposalTestData.Agreement();
        var legal = baseline.LegalRevision;
        var fixture = new BreakFixture(baseline with
        {
            LegalRevision = legal with { Terms = legal.Terms with { Terms = legal.Terms.Terms! with { SettlementDays = null } } }
        });

        var aged = (await fixture.Breaks(new(2026, 7, 15))).Should().ContainSingle().Subject;

        aged.AgingBasis.Should().Be("Source effective date");
        aged.AgeFrom.Should().Be(Effective);
        aged.AgeDays.Should().Be(15);
        aged.Classification.Should().Be("MissingCounterpart");
    }

    [Fact]
    public async Task AgreementEndedBeforeSourceDate_IsClassifiedSeparatelyAndNeverInventsADueDate()
    {
        // The approved arrangement window closed the day before the source's economic date.
        var fixture = new BreakFixture(IntercompanyProposalTestData.Agreement(effectiveTo: IntercompanyProposalTestData.Now.AddDays(-1)));

        var aged = (await fixture.Breaks(new(2026, 7, 15))).Should().ContainSingle().Subject;

        aged.Classification.Should().Be("AgreementAuthorityUnavailable");
        aged.AgingBasis.Should().Be("Source effective date");
        aged.AgeFrom.Should().Be(Effective);
        aged.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task BlockedAndUnpostedDeliveries_KeepDistinctClassificationsAndRetainedDetail()
    {
        var blocked = new BreakFixture();
        blocked.Proposal = blocked.Proposal with { Status = IntercompanyProposalStatusDto.NeedsFix, LastError = "Destination period is closed" };
        var unposted = new BreakFixture();
        var generatedDraft = unposted.Proposal.Source.ProposalId;
        unposted.Proposal = unposted.Proposal with
        {
            Source = unposted.Proposal.Source with { SourceJournalId = unposted.Post(IntercompanyProposalTestData.BookA, 100m, "USD", 1m, debit: true) },
            Status = IntercompanyProposalStatusDto.Drafted,
            TargetBookId = IntercompanyProposalTestData.BookB,
            TargetJournalId = generatedDraft
        };

        var blockedBreak = (await blocked.Breaks(new(2026, 7, 15))).Should().ContainSingle().Subject;
        var unpostedBreak = (await unposted.Breaks(new(2026, 7, 15))).Should().ContainSingle().Subject;

        blockedBreak.Classification.Should().Be("DeliveryBlocked");
        blockedBreak.Detail.Should().Be("Destination period is closed");
        blockedBreak.TargetJournalId.Should().BeNull();
        unpostedBreak.Classification.Should().Be("AwaitingCounterpartPosting");
        unpostedBreak.TargetJournalId.Should().Be(generatedDraft);
    }

    [Theory]
    [InlineData(100, "EUR", null)]
    [InlineData(90, "EUR", "AmountOrIdentityMismatch")]
    [InlineData(100, "USD", "CurrencyMismatch")]
    public async Task PostedCounterpart_BookingRateDifferenceIsNotABreak_ButOriginalAmountOrCurrencyDifferenceIs(
        int counterpartOriginalAmount, string counterpartCurrency, string? expectedClassification)
    {
        var fixture = new BreakFixture(IntercompanyProposalTestData.Agreement(currency: "EUR"));
        // The lender booked EUR 100 at 1.10; the borrower booked its side at 1.25.
        var source = fixture.Post(IntercompanyProposalTestData.BookA, 100m, "EUR", 1.10m, debit: true);
        var target = fixture.Post(IntercompanyProposalTestData.BookB, counterpartOriginalAmount, counterpartCurrency, 1.25m, debit: false);
        fixture.Proposal = fixture.Proposal with
        {
            Source = fixture.Proposal.Source with { SourceJournalId = source },
            Status = IntercompanyProposalStatusDto.Drafted,
            TargetBookId = IntercompanyProposalTestData.BookB,
            TargetJournalId = target
        };

        var breaks = await fixture.Breaks(new(2026, 8, 14));

        if (expectedClassification is null)
        {
            breaks.Should().BeEmpty("USD 110 against USD 125 is a translation difference; the EUR amounts reciprocate");
        }
        else
        {
            var reported = breaks.Should().ContainSingle().Subject;
            reported.Classification.Should().Be(expectedClassification);
            reported.TargetJournalId.Should().Be(target);
            reported.AgingBasis.Should().Be("Contractual due date");
            reported.AgeDays.Should().Be(15);
        }
    }

    internal sealed class BreakFixture
    {
        private readonly List<LedgerJournalEntryRecord> _source = [];
        private readonly List<LedgerJournalEntryRecord> _target = [];
        private readonly ResolvedIntercompanyAccountingArrangementDto _agreement;
        private readonly IntercompanyProposalDeliveryService _service;

        internal BreakFixture(ResolvedIntercompanyAccountingArrangementDto? agreement = null, bool agreementAvailable = true)
        {
            _agreement = agreement ?? IntercompanyProposalTestData.Agreement();
            Proposal = IntercompanyProposalTestData.Proposal();
            var proposals = new Mock<IIntercompanyProposalStore>();
            proposals.Setup(store => store.ListAsync("tenant-a", IntercompanyProposalTestData.BookA, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => [Proposal]);
            var sourceBook = new LedgerBookRecord(IntercompanyProposalTestData.BookA, "source-fund", IntercompanyProposalTestData.EntityA,
                FundStructureNodeKindDto.Entity, "Source", "USD", IntercompanyProposalTestData.Now, IntercompanyProposalTestData.Now);
            var journals = new Mock<ILedgerJournalStore>();
            journals.Setup(store => store.GetLedgerBookAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid book, CancellationToken _) =>
                book == IntercompanyProposalTestData.BookA ? sourceBook : IntercompanyProposalTestData.TargetBook());
            journals.Setup(store => store.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((LedgerJournalEntryQuery query, CancellationToken _) =>
                    (query.LedgerBookId == IntercompanyProposalTestData.BookA ? _source : _target).ToArray());
            var arrangementStore = new Mock<IIntercompanyAccountingArrangementStore>();
            if (agreementAvailable)
                arrangementStore.Setup(store => store.GetAsync("tenant-a", IntercompanyProposalTestData.AccountingArrangementId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new IntercompanyAccountingArrangementHistoryDto([new(Guid.NewGuid(), "synthetic", IntercompanyAccountingArrangementActionDto.Approve,
                        "reviewer", IntercompanyProposalTestData.Now, "Approved synthetic source", _agreement.Arrangement)]));
            var owners = new Mock<IFundProfileTenancyRegistry>();
            owners.Setup(registry => registry.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string fund, CancellationToken _) => new FundProfileOwnership(fund, "tenant-a", "company-a"));
            var drafts = new InMemoryManualJournalEntryDraftStore();
            var configuration = Mock.Of<IAccountingConfigurationService>();
            var workbench = new ManualJournalEntryWorkbenchService(drafts, configuration, new InMemoryAccountingActionAuditStore(),
                journalStore: journals.Object);
            _service = new IntercompanyProposalDeliveryService(proposals.Object,
                IntercompanyArrangementTestData.Service(arrangementStore.Object, _agreement), journals.Object,
                Mock.Of<IIntercompanyAccountBindingStore>(), configuration, drafts, workbench, owners.Object,
                Mock.Of<IConsolidationFxAuthority>());
        }

        internal IntercompanyProposalDto Proposal { get; set; }

        internal Task<IReadOnlyList<IntercompanyBreakDto>> Breaks(DateOnly asOf)
            => _service.BreaksAsync("tenant-a", IntercompanyProposalTestData.BookA, asOf);

        /// <summary>Posts one side of the loan in its original currency at that book's own booking rate.</summary>
        internal Guid Post(Guid book, decimal originalAmount, string originalCurrency, decimal rate, bool debit)
        {
            var source = book == IntercompanyProposalTestData.BookA;
            var journal = Guid.NewGuid();
            var timestamp = new DateTimeOffset(Effective.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var foreign = originalCurrency != "USD";
            var functional = foreign ? originalAmount * rate : originalAmount;
            var dimensions = new LedgerLineDimensionSet(
                EntityId: (source ? IntercompanyProposalTestData.EntityA : IntercompanyProposalTestData.EntityB).ToString("D"),
                CounterpartyId: (source ? IntercompanyProposalTestData.EntityB : IntercompanyProposalTestData.EntityA).ToString("D"))
            {
                IntercompanyAgreementId = _agreement.CanonicalAgreementId,
                IntercompanyAgreementVersion = _agreement.CanonicalAgreementVersion,
                AccountingArrangementId = IntercompanyProposalTestData.AccountingArrangementId.ToString("D"),
                AccountingArrangementRevisionId = IntercompanyProposalTestData.RevisionId.ToString("D"),
                ChartAccountNodeId = source ? "a-receivable" : "b-payable",
                IntercompanyTransactionId = "invoice-100"
            };
            var currency = foreign
                ? new LedgerEntryCurrency(originalCurrency, "USD", debit ? originalAmount : 0m, debit ? 0m : originalAmount, rate)
                : null;
            var entry = new JournalEntry(journal, timestamp, "Posted loan drawdown", [
                new(Guid.NewGuid(), journal, timestamp,
                    new(source ? "Assets:Due from B" : "Liabilities:Due to A", source ? LedgerAccountType.Asset : LedgerAccountType.Liability),
                    debit ? functional : 0m, debit ? 0m : functional, "Posted loan drawdown", dimensions, currency),
                new(Guid.NewGuid(), journal, timestamp, new("Assets:Cash", LedgerAccountType.Asset),
                    debit ? 0m : functional, debit ? functional : 0m, "Posted loan drawdown")],
                new JournalEntryMetadata(EffectiveDate: Effective));
            (source ? _source : _target).Add(new(entry, book, Guid.NewGuid(), null, null, 1, timestamp));
            return journal;
        }
    }
}
