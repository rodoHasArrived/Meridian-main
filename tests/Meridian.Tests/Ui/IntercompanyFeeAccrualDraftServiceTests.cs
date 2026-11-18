using Meridian.Application.FundStructure;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;
using Meridian.Ui.Shared.Services;
using Moq;
using Data = Meridian.Tests.FinancialOperations.Intercompany.IntercompanyProposalTestData;

namespace Meridian.Tests.Ui;

/// <summary>
/// Rule intercompany-fee-v1 through the draft service over synthetic authorities; nothing here approves or posts.
/// Entity A is the declared service provider and entity B the recipient. The fee is 30,000 USD per monthly period
/// from 1 June 2026, so June (one full period) earns exactly 30,000.00.
/// </summary>
public sealed class IntercompanyFeeAccrualDraftServiceTests
{
    private static readonly Guid Fee = Guid.Parse("e1111111-1111-1111-1111-111111111111");
    private static readonly Guid Organization = Guid.Parse("e3333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Effective = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 7, 1);

    [Fact]
    public async Task TheProviderBookAccruesReceivableAgainstFeeRevenue_AndTheGuardAcceptsIt()
    {
        var proposal = await Service(Data.BookA).PreviewAsync("tenant-a", "company-a", "preparer", Request(Data.BookA));

        Assert.True(proposal.IsReady);
        Assert.Equal((30_000.00m, IntercompanyFeeAccrualRules.RuleVersion), (proposal.Computation.Amount, proposal.Evidence!.RuleVersion));
        var draft = proposal.Draft!;
        Assert.Equal(new DateOnly(2026, 6, 30), draft.AccountingDate);
        Assert.Equal([(AccountingTemplateLineSideDto.Debit, "a-fee-recv"), (AccountingTemplateLineSideDto.Credit, "a-fee-revenue")],
            draft.Lines.Select(line => (line.Side, line.Dimensions!.ChartAccountNodeId!)));
        Assert.Equal("management-fee:20260601-20260701", draft.Lines[0].Dimensions!.IntercompanyTransactionId);
        Assert.Null(IntercompanyAccrualEvidenceGuard.Validate(draft));
        Assert.True(IntercompanyAccrualEvidenceGuard.IsOriginalAccrualDraft(draft));
    }

    [Fact]
    public async Task TheRecipientBookAccruesFeeExpenseAgainstItsPayable()
    {
        var proposal = await Service(Data.BookB).PreviewAsync("tenant-a", "company-a", "preparer", Request(Data.BookB));

        Assert.True(proposal.IsReady);
        Assert.Equal(IntercompanyAccountRoleDto.Payable, proposal.Evidence!.SourceRole);
        Assert.Equal([(AccountingTemplateLineSideDto.Credit, "b-fee-pay"), (AccountingTemplateLineSideDto.Debit, "b-fee-expense")],
            proposal.Draft!.Lines.Select(line => (line.Side, line.Dimensions!.ChartAccountNodeId!)));
        Assert.Null(IntercompanyAccrualEvidenceGuard.Validate(proposal.Draft));
    }

    [Fact]
    public async Task TheGuardRefusesEditedAmountsAndSwappedDirection()
    {
        var draft = (await Service(Data.BookA).PreviewAsync("tenant-a", "company-a", "preparer", Request(Data.BookA))).Draft!;
        static AccountingTemplateLineSideDto Flip(AccountingTemplateLineSideDto side)
            => side == AccountingTemplateLineSideDto.Debit ? AccountingTemplateLineSideDto.Credit : AccountingTemplateLineSideDto.Debit;

        var inflated = draft with { Lines = draft.Lines.Select(line => line with { Amount = line.Amount + 1m }).ToArray() };
        var swapped = draft with { Lines = draft.Lines.Select(line => line with { Side = Flip(line.Side) }).ToArray() };

        Assert.Contains("amount", IntercompanyAccrualEvidenceGuard.Validate(inflated));
        Assert.Contains("direction", IntercompanyAccrualEvidenceGuard.Validate(swapped));
    }

    [Fact]
    public async Task APostedFeeOfTheWindowBlocksAnotherOne_AndAMissingBasisIsRefused()
    {
        var posted = Journal(new(2026, 6, 30), "a-fee-recv", IntercompanyFeeAccrualEvidenceGuard.TransactionId(From, To), 30_000m);

        var overlap = await Service(Data.BookA, feeJournals: [posted]).PreviewAsync("tenant-a", "company-a", "preparer", Request(Data.BookA));
        var undeclared = await Service(Data.BookA, basis: null).PreviewAsync("tenant-a", "company-a", "preparer", Request(Data.BookA));

        Assert.False(overlap.IsReady);
        Assert.Contains("management-fee:20260601-20260701", overlap.Diagnostics[^1]);
        Assert.False(undeclared.IsReady);
        Assert.Contains("no fixed-fee basis", undeclared.Diagnostics[^1]);
    }

    [Fact]
    public async Task CreateDraftRefusesAPreviewThatNoLongerMatches()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(Data.BookA).CreateDraftAsync("tenant-a", "company-a",
            "preparer", new(Request(Data.BookA), new string('0', 64))));

        Assert.Contains("changed after the preview", error.Message);
    }

    [Fact]
    public async Task ARetiredProfileRevisionStopsTheDraftBeforeReviewOrPosting()
    {
        var evidence = (await Service(Data.BookA).PreviewAsync("tenant-a", "company-a", "preparer", Request(Data.BookA))).Evidence!;
        var fee = Profile(IntercompanyFixedFeeBasisDto.PerPeriod);
        fee = fee with { Arrangement = fee.Arrangement with { RevisionId = evidence.PostingArrangementRevisionId } };
        var retired = new IntercompanyAccountingArrangementHistoryDto([.. History(fee).Events,
            new(Guid.NewGuid(), "digest", IntercompanyAccountingArrangementActionDto.Retire, "mapping-reviewer", Data.Now, "Retired",
                fee.Arrangement with { RetiredAt = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero) })]);

        await Authority(History(fee), fee).VerifyFeeAccrualAuthorityAsync("tenant-a", evidence);
        var stale = await Assert.ThrowsAsync<InvalidOperationException>(() => Authority(retired, fee).VerifyFeeAccrualAuthorityAsync("tenant-a", evidence));

        Assert.Contains("2026-06-30", stale.Message);
    }

    private static IntercompanyFeeAccrualRequestDto Request(Guid book) => new(book, Fee, From, To);

    private static Meridian.FinancialOperations.Intercompany.IntercompanyLedgerPostingAuthority Authority(
        IntercompanyAccountingArrangementHistoryDto history, ResolvedIntercompanyAccountingArrangementDto resolved)
    {
        var store = new Mock<IIntercompanyAccountingArrangementStore>();
        store.Setup(x => x.GetAsync("tenant-a", Fee, It.IsAny<CancellationToken>())).ReturnsAsync(history);
        return new(store.Object, IntercompanyArrangementTestData.Service(store.Object, resolved), Mock.Of<IIntercompanyAccountBindingStore>(),
            null!, Mock.Of<ILedgerJournalStore>(), null!);
    }

    private static IntercompanyFeeAccrualDraftService Service(Guid sourceBook, IReadOnlyList<LedgerJournalEntryRecord>? feeJournals = null,
        IntercompanyFixedFeeBasisDto? basis = IntercompanyFixedFeeBasisDto.PerPeriod)
    {
        var fee = Profile(basis);
        var store = new Mock<IIntercompanyAccountingArrangementStore>();
        store.Setup(x => x.GetAsync("tenant-a", Fee, It.IsAny<CancellationToken>())).ReturnsAsync(History(fee));
        var provider = sourceBook == Data.BookA;
        var book = new LedgerBookRecord(sourceBook, provider ? "provider-fund" : "recipient-fund", provider ? Data.EntityA : Data.EntityB,
            FundStructureNodeKindDto.Entity, "Source", "USD", Data.Now, Data.Now);
        var journals = new Mock<ILedgerJournalStore>();
        journals.Setup(x => x.GetLedgerBookAsync(sourceBook, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        journals.Setup(x => x.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(feeJournals ?? []);
        journals.Setup(x => x.ListPeriodsAsync(sourceBook, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LedgerAccountingPeriod(Guid.NewGuid(), sourceBook, 2026, 6, "June", From, new(2026, 6, 30), "Open", Data.Now, null, 1)]);
        IReadOnlyList<ChartOfAccountsNodeDto> chart = provider
            ?
            [
                new("a-fee-recv", "Assets:Fee receivable", "Assets:Fee receivable", "Asset", IntercompanyRole: IntercompanyAccountRoleDto.Receivable),
                new("a-fee-revenue", "Income:Management fees", "Income:Management fees", "Revenue", IntercompanyRole: IntercompanyAccountRoleDto.Revenue)
            ]
            :
            [
                new("b-fee-pay", "Liabilities:Fee payable", "Liabilities:Fee payable", "Liability", IntercompanyRole: IntercompanyAccountRoleDto.Payable),
                new("b-fee-expense", "Expenses:Management fees", "Expenses:Management fees", "Expense", IntercompanyRole: IntercompanyAccountRoleDto.Expense)
            ];
        var bindings = new Mock<IIntercompanyAccountBindingStore>();
        bindings.Setup(x => x.ListAsync(sourceBook, new DateOnly(2026, 6, 30), It.IsAny<CancellationToken>())).ReturnsAsync(chart
            .Select(account => new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, sourceBook, account.NodeId,
                account.IntercompanyRole!.Value, account.Path, account.AccountName, account.AccountType, new(2026, 1, 1), null,
                [], "tenant-a", "company-a", "reviewer", Data.Now, "Synthetic fixture")).ToArray());
        var configuration = new Mock<IAccountingConfigurationService>();
        configuration.Setup(x => x.GetWorkspaceAsync(book.FundProfileId, sourceBook, It.IsAny<CancellationToken>(), "tenant-a", "company-a"))
            .ReturnsAsync(new AccountingConfigurationWorkspaceDto(book.FundProfileId, sourceBook, default, "v1", Data.Now, [], chart, [], [], [], []));
        return new(IntercompanyArrangementTestData.Service(store.Object, fee), journals.Object, bindings.Object,
            configuration.Object, new InMemoryManualJournalEntryDraftStore(), null!,
            new IntercompanyFeeAccrualPreviewService(new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly])),
            time: new FixedClock(Data.Now));
    }

    private static ResolvedIntercompanyAccountingArrangementDto Profile(IntercompanyFixedFeeBasisDto? basis)
    {
        var profile = IntercompanyArrangementTestData.Create(Fee, Organization, IntercompanyAccountingTemplateKindDto.ManagementFee,
            Data.EntityA, Data.EntityB, "USD",
            [new(Data.EntityA, Data.BookA, "a-fee-recv", "a-fee-pay", null, "a-fee-revenue", "a-fee-expense"),
             new(Data.EntityB, Data.BookB, "b-fee-recv", "b-fee-pay", null, "b-fee-revenue", "b-fee-expense")],
            Guid.NewGuid(), Effective);
        var terms = new IntercompanyAgreementTermsDto
        {
            StructuredTerms = new(new(From, new(2027, 6, 1), IntercompanyFrequencyDto.Monthly, "WEEKENDS", BusinessDayConvention.Following),
                FixedFeeAmount: 30_000m, FixedFeeBasis: basis)
        };
        return profile with
        {
            LegalRevision = profile.LegalRevision with
            {
                Terms = profile.LegalRevision.Terms with { PartyOneRole = "Service provider", PartyTwoRole = "Recipient", Terms = terms }
            }
        };
    }

    private static IntercompanyAccountingArrangementHistoryDto History(ResolvedIntercompanyAccountingArrangementDto profile)
        => new([new(Guid.NewGuid(), "digest", IntercompanyAccountingArrangementActionDto.Approve, "mapping-reviewer",
            profile.EffectiveFrom, null, profile.Arrangement)]);

    private static LedgerJournalEntryRecord Journal(DateOnly date, string node, string transaction, decimal debit)
    {
        var id = Guid.NewGuid();
        var timestamp = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var entry = new JournalEntry(id, timestamp, "Synthetic fee journal",
        [
            new(Guid.NewGuid(), id, timestamp, new("Intercompany", LedgerAccountType.Asset), debit, 0m, "Synthetic fee journal",
                new LedgerLineDimensionSet(EntityId: Data.EntityA.ToString("D"), CounterpartyId: Data.EntityB.ToString("D"))
                {
                    ChartAccountNodeId = node,
                    AccountingArrangementId = Fee.ToString("D"),
                    IntercompanyTransactionId = transaction
                }),
            new(Guid.NewGuid(), id, timestamp, new("Income:Management fees", LedgerAccountType.Revenue), 0m, debit, "Synthetic fee journal")
        ], new JournalEntryMetadata(EffectiveDate: date, Tags: new Dictionary<string, string> { [IntercompanyAccrualEvidenceGuard.DigestTag] = new string('e', 64) }));
        return new(entry, Guid.NewGuid(), Guid.NewGuid(), null, null, 1, timestamp);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
