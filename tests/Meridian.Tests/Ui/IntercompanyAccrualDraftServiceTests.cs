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

/// <summary>Resolve, compute and draft over synthetic authorities; nothing here approves or posts.</summary>
public sealed class IntercompanyAccrualDraftServiceTests
{
    private static readonly Guid Interest = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    private static readonly Guid Principal = Guid.Parse("f2222222-2222-2222-2222-222222222222");
    private static readonly Guid Organization = Guid.Parse("f3333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Effective = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 7, 1);

    [Fact]
    public async Task Preview_ResolvesApprovedTermsProfileAndPostedPrincipalIntoAnUnpostedSourceDraft()
    {
        // 1,000,000 x 5% x 30/360 = 4,166.6666... -> 4,166.67
        var proposal = await Service().PreviewAsync("tenant-a", "company-a", "preparer", Request());

        Assert.True(proposal.IsReady);
        Assert.Equal(4166.67m, proposal.Computation.Amount);
        var draft = proposal.Draft!;
        Assert.Equal(ManualJournalEntryStatusDto.Draft, draft.Status);
        Assert.Equal(new DateOnly(2026, 6, 30), draft.AccountingDate);
        Assert.Equal([(AccountingTemplateLineSideDto.Debit, "a-int-recv"), (AccountingTemplateLineSideDto.Credit, "a-int-income")],
            draft.Lines.Select(line => (line.Side, line.Dimensions!.ChartAccountNodeId!)));
        Assert.Equal((Principal, 1_000_000m), (proposal.Evidence!.Principal.PrincipalArrangementId, proposal.Evidence.Principal.OpeningPrincipal));
        Assert.Null(IntercompanyAccrualEvidenceGuard.Validate(draft));
    }

    [Fact]
    public async Task Preview_IsUnavailable_WhenAPostedAccrualAlreadyCoversTheWindow()
    {
        var posted = Journal(new(2026, 6, 30), Interest, "a-int-recv", IntercompanyAccrualEvidenceGuard.TransactionId(From, To), 4166.67m);

        var proposal = await Service(interestJournals: [posted]).PreviewAsync("tenant-a", "company-a", "preparer", Request());

        Assert.False(proposal.IsReady);
        Assert.Null(proposal.Draft);
        Assert.Contains("loan-interest:20260601-20260701", proposal.Diagnostics[^1]);
    }

    [Theory]
    [InlineData("no-period", "open source period")]
    [InlineData("no-principal", "No posted principal")]
    [InlineData("unstructured", "unstructured terms")]
    [InlineData("before-profile", "2026-05-31")]
    [InlineData("same-profile", "separately reviewed")]
    public async Task Preview_RefusesRatherThanEstimates(string variant, string expected)
    {
        var service = Service(openPeriod: variant != "no-period", principalJournals: variant == "no-principal" ? [] : null,
            structured: variant != "unstructured");
        var request = variant switch
        {
            "before-profile" => Request() with { AccrualFrom = new(2026, 5, 31) },
            "same-profile" => Request() with { PrincipalArrangementId = Interest },
            _ => Request()
        };

        var proposal = await service.PreviewAsync("tenant-a", "company-a", "preparer", request);

        Assert.False(proposal.IsReady);
        Assert.Null(proposal.Evidence);
        Assert.Contains(expected, proposal.Diagnostics[^1]);
    }

    [Fact]
    public async Task CreateDraft_RefusesWhenInputsChangedSinceTheOperatorsPreview()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CreateDraftAsync("tenant-a", "company-a",
            "preparer", new(Request(), new string('0', 64))));
        var unavailable = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(openPeriod: false).CreateDraftAsync(
            "tenant-a", "company-a", "preparer", new(Request(), new string('0', 64))));

        Assert.Contains("changed after the preview", error.Message);
        Assert.Contains("open source period", unavailable.Message);
    }

    private static IntercompanyAccrualRequestDto Request() => new(Data.BookA, Interest, Principal, From, To);

    private static IntercompanyAccrualDraftService Service(bool openPeriod = true,
        IReadOnlyList<LedgerJournalEntryRecord>? principalJournals = null,
        IReadOnlyList<LedgerJournalEntryRecord>? interestJournals = null, bool structured = true)
    {
        var interest = Profile(Interest, IntercompanyAccountingTemplateKindDto.LoanInterest,
            [new(Data.EntityA, Data.BookA, "a-int-recv", "a-int-pay", null, "a-int-income", "a-int-expense"),
             new(Data.EntityB, Data.BookB, "b-int-recv", "b-int-pay", null, "b-int-income", "b-int-expense")], structured);
        var principal = Profile(Principal, IntercompanyAccountingTemplateKindDto.Loan,
            [new(Data.EntityA, Data.BookA, "a-receivable", "a-payable", "a-cash"),
             new(Data.EntityB, Data.BookB, "b-receivable", "b-payable", "b-cash")], structured);
        var store = new Mock<IIntercompanyAccountingArrangementStore>();
        store.Setup(x => x.GetAsync("tenant-a", Interest, It.IsAny<CancellationToken>())).ReturnsAsync(History(interest));
        store.Setup(x => x.GetAsync("tenant-a", Principal, It.IsAny<CancellationToken>())).ReturnsAsync(History(principal));
        var book = new LedgerBookRecord(Data.BookA, "source-fund", Data.EntityA, FundStructureNodeKindDto.Entity, "Source", "USD", Data.Now, Data.Now);
        var journals = new Mock<ILedgerJournalStore>();
        journals.Setup(x => x.GetLedgerBookAsync(Data.BookA, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        journals.Setup(x => x.QueryAsync(It.Is<LedgerJournalEntryQuery>(q => q.LineDimensions!.AccountingArrangementId == Principal.ToString("D")),
            It.IsAny<CancellationToken>())).ReturnsAsync(principalJournals ?? [Journal(new(2026, 5, 15), Principal, "a-receivable", "drawdown", 1_000_000m)]);
        journals.Setup(x => x.QueryAsync(It.Is<LedgerJournalEntryQuery>(q => q.LineDimensions!.AccountingArrangementId == Interest.ToString("D")),
            It.IsAny<CancellationToken>())).ReturnsAsync(interestJournals ?? []);
        journals.Setup(x => x.ListPeriodsAsync(Data.BookA, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(openPeriod ? [new LedgerAccountingPeriod(Guid.NewGuid(), Data.BookA, 2026, 6, "June", From, new(2026, 6, 30), "Open", Data.Now, null, 1)] : []);
        IReadOnlyList<ChartOfAccountsNodeDto> chart =
        [
            new("a-int-recv", "Assets:Interest receivable", "Assets:Interest receivable", "Asset", IntercompanyRole: IntercompanyAccountRoleDto.Receivable),
            new("a-int-income", "Income:Interest", "Income:Interest", "Revenue", IntercompanyRole: IntercompanyAccountRoleDto.Revenue)
        ];
        var bindings = new Mock<IIntercompanyAccountBindingStore>();
        bindings.Setup(x => x.ListAsync(Data.BookA, new DateOnly(2026, 6, 30), It.IsAny<CancellationToken>())).ReturnsAsync(chart
            .Select(account => new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, Data.BookA, account.NodeId,
                account.IntercompanyRole!.Value, account.Path, account.AccountName, account.AccountType, new(2026, 1, 1), null,
                [], "tenant-a", "company-a", "reviewer", Data.Now, "Synthetic fixture")).ToArray());
        var configuration = new Mock<IAccountingConfigurationService>();
        configuration.Setup(x => x.GetWorkspaceAsync("source-fund", Data.BookA, It.IsAny<CancellationToken>(), "tenant-a", "company-a"))
            .ReturnsAsync(new AccountingConfigurationWorkspaceDto("source-fund", Data.BookA, default, "v1", Data.Now, [], chart, [], [], [], []));
        return new(IntercompanyArrangementTestData.Service(store.Object, interest), journals.Object, bindings.Object,
            configuration.Object, new InMemoryManualJournalEntryDraftStore(), null!,
            new IntercompanyAccrualPreviewService(new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly])),
            time: new FixedClock(Data.Now));
    }

    private static ResolvedIntercompanyAccountingArrangementDto Profile(Guid id, IntercompanyAccountingTemplateKindDto kind,
        IReadOnlyList<IntercompanyPartyMappingDto> mappings, bool structured)
    {
        var profile = IntercompanyArrangementTestData.Create(id, Organization, kind, Data.EntityA, Data.EntityB, "USD", mappings,
            Guid.NewGuid(), Effective);
        var terms = structured
            ? new IntercompanyAgreementTermsDto
            {
                StructuredTerms = new(new(From, new(2027, 6, 1), IntercompanyFrequencyDto.Monthly, "WEEKENDS", BusinessDayConvention.Following),
                    IntercompanyRateBasisDto.Fixed, 0.05m, DayCount: IntercompanyDayCountDto.Actual360, Compounding: IntercompanyCompoundingDto.Simple)
            }
            : new IntercompanyAgreementTermsDto(RateDescription: "Five percent, monthly");
        return profile with { LegalRevision = profile.LegalRevision with { Terms = profile.LegalRevision.Terms with { PartyOneRole = "Lender", PartyTwoRole = "Borrower", Terms = terms } } };
    }

    private static IntercompanyAccountingArrangementHistoryDto History(ResolvedIntercompanyAccountingArrangementDto profile)
        => new([new(Guid.NewGuid(), "digest", IntercompanyAccountingArrangementActionDto.Approve, "mapping-reviewer",
            profile.EffectiveFrom, null, profile.Arrangement)]);

    private static LedgerJournalEntryRecord Journal(DateOnly date, Guid arrangement, string node, string transaction, decimal debit)
    {
        var id = Guid.NewGuid();
        var timestamp = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var entry = new JournalEntry(id, timestamp, "Synthetic journal",
        [
            new(Guid.NewGuid(), id, timestamp, new("Intercompany", LedgerAccountType.Asset), debit, 0m, "Synthetic journal",
                new LedgerLineDimensionSet(EntityId: Data.EntityA.ToString("D"), CounterpartyId: Data.EntityB.ToString("D"))
                {
                    ChartAccountNodeId = node,
                    AccountingArrangementId = arrangement.ToString("D"),
                    IntercompanyTransactionId = transaction
                }),
            new(Guid.NewGuid(), id, timestamp, new("Assets:Cash", LedgerAccountType.Asset), 0m, debit, "Synthetic journal")
        ], new JournalEntryMetadata(EffectiveDate: date));
        return new(entry, Guid.NewGuid(), Guid.NewGuid(), null, null, 1, timestamp);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
