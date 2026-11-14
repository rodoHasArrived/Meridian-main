using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class BondCouponRecognitionClosingHistoryTests
{
    [Theory]
    [InlineData("canonical", 1)]
    [InlineData("negative", 1)]
    [InlineData("fund-enrichment", 1)]
    [InlineData("reopened", 1)]
    [InlineData("wrong-amount", 0)]
    [InlineData("wrong-dimensions", 0)]
    [InlineData("wrong-key", 0)]
    [InlineData("wrong-basis", 0)]
    [InlineData("source-label-only", 0)]
    [InlineData("receivable", 0)]
    [InlineData("payable", 0)]
    [InlineData("cash", 0)]
    [InlineData("missing-period", 0)]
    [InlineData("missing-sequence", 0)]
    [InlineData("metadata-only-legacy", 0)]
    public async Task OnlyExactCanonicalPostedClosesCanShareCouponIncomeHistory(string scenario, int expected)
    {
        var fixture = Fixture(scenario);
        var result = await BondCouponRecognitionClosingHistory.ResolveAsync(fixture.Ledger, fixture.Scope,
            fixture.Lot, fixture.Paths, fixture.Accounts, fixture.History);
        result.Should().HaveCount(expected);
        if (expected != 0)
            result.Single().Should().Be(fixture.History[1]);
    }

    [Theory]
    [InlineData("approved", 2)]
    [InlineData("unapproved", 1)]
    [InlineData("wrong-inverse", 1)]
    [InlineData("duplicate-inverse", 1)]
    [InlineData("future-approval", 1)]
    public async Task GovernedReopenRequiresOneApprovedExactInverseOfTheIndependentlyProvenClose(string scenario, int expected)
    {
        var fixture = Fixture("reopened");
        var original = fixture.History[1];
        var id = Guid.NewGuid();
        var at = original.Entry.Timestamp;
        var inverse = new JournalEntry(id, at, "Governed close reversal", original.Entry.Lines.Select(line =>
            new LedgerEntry(Guid.NewGuid(), id, at, line.Account,
                line.Credit * (scenario == "wrong-inverse" ? .5m : 1m),
                line.Debit * (scenario == "wrong-inverse" ? .5m : 1m), "Governed close reversal", line.Dimensions)).ToArray(),
            new(EffectiveDate: new(2026, 1, 31)));
        var recorded = new DateTimeOffset(2026, 2, 2, 12, 0, 0, TimeSpan.Zero);
        var row = new LedgerJournalEntryRecord(inverse, original.AggregateId, original.PeriodId, null, null, 3, recorded,
            AccountingBasisKindDto.Gaap, SourceJournalEntryId: original.Entry.JournalEntryId,
            PostingKind: LedgerPostingKindDto.Adjustment,
            AdjustmentApproval: new("approved-close-reopen", scenario == "unapproved" ? LedgerAdjustmentApprovalStatusDto.Pending : LedgerAdjustmentApprovalStatusDto.Approved,
                "controller", scenario == "future-approval" ? recorded.AddDays(1) : recorded.AddMinutes(-1), "governed-reopen"));
        fixture.History.Add(row);
        if (scenario == "duplicate-inverse")
        {
            var secondId = Guid.NewGuid();
            var second = new JournalEntry(secondId, at, inverse.Description, inverse.Lines.Select(line =>
                new LedgerEntry(Guid.NewGuid(), secondId, at, line.Account, line.Debit, line.Credit, inverse.Description, line.Dimensions)).ToArray(), inverse.Metadata);
            fixture.History.Add(row with { Entry = second, GlobalSequence = 4 });
        }
        var result = await BondCouponRecognitionClosingHistory.ResolveAsync(fixture.Ledger, fixture.Scope,
            fixture.Lot, fixture.Paths, fixture.Accounts, fixture.History);
        result.Should().HaveCount(expected);
        result.Should().Contain(original);
    }

    private sealed record Inputs(ILedgerJournalStore Ledger, AccountCashForecastScopeDto Scope, LedgerTaxLotRecord Lot,
        BondCouponBookAccountsDto Paths, BondCouponRecognitionAccountDto[] Accounts, List<LedgerJournalEntryRecord> History);

    private static Inputs Fixture(string scenario)
    {
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var owner = Guid.NewGuid().ToString("D");
        var security = Guid.NewGuid();
        var position = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);
        var scope = new AccountCashForecastScopeDto("tenant", "company", owner, bookId.ToString("D"), "USD");
        var paths = new BondCouponBookAccountsDto("receivable", "payable", "income", "cash", "tax", "fees");
        BondCouponRecognitionAccountDto[] accounts = [new("receivable", "Coupon receivable", "Asset"),
            new("payable", "Coupon payable", "Liability"), new("income", "Bond income", "Revenue"), new("cash", "Cash", "Asset")];
        var lot = new LedgerTaxLotRecord(Guid.NewGuid(), bookId, new("Investment lots", LedgerAccountType.Asset, FinancialAccountId: owner),
            "lot", new(2026, 1, 1), 100m, 100m, 1m, "USD", at, at, SecurityId: security, BookPositionId: position);
        var dimensions = new LedgerLineDimensionSet(FundId: "fund", BookId: scope.BookId, AccountId: owner,
            InstrumentId: security, TaxLotId: "lot", ExternalGlDimensions: new Dictionary<string, string> { ["desk"] = "A" })
        { PositionId = position };
        var income = new LedgerAccount("Bond income", LedgerAccountType.Revenue, FinancialAccountId: owner);
        var amount = scenario == "negative" ? -100m : 100m;
        var prefixId = Guid.NewGuid();
        var priorDimensions = scenario == "metadata-only-legacy" ? null
            : scenario == "fund-enrichment" ? dimensions with { FundId = null } : dimensions;
        var prefix = new JournalEntry(prefixId, at.AddDays(-10), "Canonical coupon",
            [new(Guid.NewGuid(), prefixId, at.AddDays(-10), LedgerAccounts.CashAccount(owner), Math.Max(amount, 0m), Math.Max(-amount, 0m), "Canonical coupon", priorDimensions),
             new(Guid.NewGuid(), prefixId, at.AddDays(-10), income, Math.Max(-amount, 0m), Math.Max(amount, 0m), "Canonical coupon", priorDimensions)], new(EffectiveDate: new(2026, 1, 21)));
        var projection = PeriodCloseProjector.Project(new PeriodCloseInput(periodId.ToString("D"), at.AddDays(1).AddTicks(-1),
            [new PeriodCloseAccountBalance(income, amount, dimensions)], "controller"));
        var draft = PeriodCloseDraftBuilder.BuildDraft(projection)!;
        var closeId = Guid.NewGuid();
        var closing = new JournalEntry(closeId, at, "Actual manual close", projection.JournalLines.Select(line =>
        {
            var account = line.account;
            if (account.AccountType == LedgerAccountType.Equity && scenario is "receivable" or "payable" or "cash")
            {
                var selected = accounts.Single(row => row.Path == scenario);
                account = new(selected.LedgerName, Enum.Parse<LedgerAccountType>(selected.AccountType), FinancialAccountId: owner);
            }
            var dims = scenario == "wrong-dimensions" ? line.dimensions! with { SleeveId = "unrelated-sleeve" } : line.dimensions;
            var scale = scenario == "wrong-amount" ? .5m : 1m;
            return new LedgerEntry(Guid.NewGuid(), closeId, at, account, line.debit * scale, line.credit * scale, "Actual manual close", dims);
        }).ToArray(), new(ActivityType: "ManualJournalEntry", EffectiveDate: new(2026, 1, 31),
            IdempotencyKey: scenario == "wrong-key" ? "period-close|forged" : draft.Metadata.IdempotencyKey));
        var records = new List<LedgerJournalEntryRecord>
        {
            new(prefix, bookId, periodId, null, null, scenario == "missing-sequence" ? 0 : 1, at, AccountingBasisKindDto.Gaap),
            new(closing, bookId, periodId, null, null, 2, at.AddDays(1),
                scenario == "wrong-basis" ? AccountingBasisKindDto.Primary : AccountingBasisKindDto.Gaap,
                PostingKind: scenario == "source-label-only" ? LedgerPostingKindDto.Originating : LedgerPostingKindDto.ClosingEntry)
        };
        var ledger = Substitute.For<ILedgerJournalStore>();
        ledger.GetLedgerBookAsync(bookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookRecord(bookId, "fund", Guid.NewGuid(),
            (FundStructureNodeKindDto)0, "Book", "USD", at, at, AccountingBasis: AccountingBasisKindDto.Gaap));
        ledger.GetPeriodAsync(periodId, Arg.Any<CancellationToken>()).Returns(scenario == "missing-period" ? null
            : new LedgerAccountingPeriod(periodId, bookId, 2026, 1, "January", new(2026, 1, 1), new(2026, 1, 31),
                scenario == "reopened" ? "Open" : "HardClosed", at.AddMonths(-1), at, 3));
        return new(ledger, scope, lot, paths, accounts, records);
    }
}
