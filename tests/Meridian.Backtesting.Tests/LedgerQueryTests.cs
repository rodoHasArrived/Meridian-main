using FluentAssertions;
using Meridian.Ledger;

namespace Meridian.Backtesting.Tests;

public sealed class LedgerQueryTests
{
    [Fact]
    public void Accounts_AndSummaries_AreTrackedAcrossPosts()
    {
        var ledger = new BacktestLedger();
        var t1 = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(5);

        ledger.PostLines(
            t1,
            "Initial capital",
            [
                (LedgerAccounts.Cash, 10_000m, 0m),
                (LedgerAccounts.CapitalAccount, 0m, 10_000m)
            ]);

        ledger.PostLines(
            t2,
            "Buy SPY",
            [
                (LedgerAccounts.Securities("SPY"), 4_000m, 0m),
                (LedgerAccounts.Cash, 0m, 4_000m)
            ]);

        ledger.Accounts.Should().BeEquivalentTo(
            [LedgerAccounts.Cash, LedgerAccounts.CapitalAccount, LedgerAccounts.Securities("SPY")]);
        ledger.HasAccount(LedgerAccounts.Cash).Should().BeTrue();
        ledger.HasAccount(LedgerAccounts.RealizedGain).Should().BeFalse();

        var cashSummary = ledger.GetAccountSummary(LedgerAccounts.Cash);
        cashSummary.Balance.Should().Be(6_000m);
        cashSummary.TotalDebits.Should().Be(10_000m);
        cashSummary.TotalCredits.Should().Be(4_000m);
        cashSummary.EntryCount.Should().Be(2);
        cashSummary.FirstPostedAt.Should().Be(t1);
        cashSummary.LastPostedAt.Should().Be(t2);

        ledger.SummarizeAccounts(LedgerAccountType.Asset)
            .Select(summary => summary.Account)
            .Should()
            .BeEquivalentTo([LedgerAccounts.Cash, LedgerAccounts.Securities("SPY")]);
    }

    [Fact]
    public void GetBalanceAsOf_And_TrialBalanceAsOf_RespectTimestampBoundaries()
    {
        var ledger = new BacktestLedger();
        var t1 = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(5);
        var t3 = t2.AddMinutes(5);
        var securities = LedgerAccounts.Securities("SPY");

        ledger.PostLines(
            t1,
            "Initial capital",
            [
                (LedgerAccounts.Cash, 10_000m, 0m),
                (LedgerAccounts.CapitalAccount, 0m, 10_000m)
            ]);

        ledger.PostLines(
            t2,
            "Buy SPY",
            [
                (securities, 4_000m, 0m),
                (LedgerAccounts.Cash, 0m, 4_000m)
            ]);

        ledger.PostLines(
            t3,
            "Commission",
            [
                (LedgerAccounts.CommissionExpense, 5m, 0m),
                (LedgerAccounts.Cash, 0m, 5m)
            ]);

        ledger.GetBalanceAsOf(LedgerAccounts.Cash, t1).Should().Be(10_000m);
        ledger.GetBalanceAsOf(LedgerAccounts.Cash, t2).Should().Be(6_000m);
        ledger.GetBalanceAsOf(LedgerAccounts.Cash, t3).Should().Be(5_995m);

        var trialBalanceAsOfT2 = ledger.TrialBalanceAsOf(t2);
        trialBalanceAsOfT2.Should().Contain(new KeyValuePair<LedgerAccount, decimal>(LedgerAccounts.Cash, 6_000m));
        trialBalanceAsOfT2.Should().Contain(new KeyValuePair<LedgerAccount, decimal>(LedgerAccounts.CapitalAccount, 10_000m));
        trialBalanceAsOfT2.Should().Contain(new KeyValuePair<LedgerAccount, decimal>(securities, 4_000m));
        trialBalanceAsOfT2.Should().NotContainKey(LedgerAccounts.CommissionExpense);
    }

    [Fact]
    public void GetJournalEntries_And_GetEntries_CanFilterByDateAndDescription()
    {
        var ledger = new BacktestLedger();
        var t1 = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(5);
        var t3 = t2.AddMinutes(5);
        var securities = LedgerAccounts.Securities("SPY");

        ledger.PostLines(
            t1,
            "Initial capital",
            [
                (LedgerAccounts.Cash, 10_000m, 0m),
                (LedgerAccounts.CapitalAccount, 0m, 10_000m)
            ]);

        ledger.PostLines(
            t2,
            "Buy SPY",
            [
                (securities, 4_000m, 0m),
                (LedgerAccounts.Cash, 0m, 4_000m)
            ]);

        ledger.PostLines(
            t3,
            "Buy more SPY",
            [
                (securities, 2_000m, 0m),
                (LedgerAccounts.Cash, 0m, 2_000m)
            ]);

        ledger.GetJournalEntries(from: t2, descriptionContains: "buy")
            .Select(entry => entry.Description)
            .Should()
            .Equal("Buy SPY", "Buy more SPY");

        ledger.GetEntries(securities, t2, t2)
            .Should()
            .ContainSingle()
            .Which.Debit.Should().Be(4_000m);
    }

    [Fact]
    public void Post_RejectsDuplicateJournalAndLineIdentifiers()
    {
        var ledger = new BacktestLedger();
        var timestamp = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var journalId = Guid.NewGuid();
        var debitEntryId = Guid.NewGuid();
        var creditEntryId = Guid.NewGuid();

        var first = new JournalEntry(
            journalId,
            timestamp,
            "Seed",
            [
                new LedgerEntry(debitEntryId, journalId, timestamp, LedgerAccounts.Cash, 10m, 0m, "Seed"),
                new LedgerEntry(creditEntryId, journalId, timestamp, LedgerAccounts.CapitalAccount, 0m, 10m, "Seed")
            ]);

        ledger.Post(first);

        var duplicateJournal = new JournalEntry(
            journalId,
            timestamp.AddMinutes(1),
            "Duplicate journal",
            [
                new LedgerEntry(Guid.NewGuid(), journalId, timestamp.AddMinutes(1), LedgerAccounts.Cash, 5m, 0m, "Duplicate journal"),
                new LedgerEntry(Guid.NewGuid(), journalId, timestamp.AddMinutes(1), LedgerAccounts.CapitalAccount, 0m, 5m, "Duplicate journal")
            ]);


        Action duplicateJournalPost = () => ledger.Post(duplicateJournal);
        duplicateJournalPost.Should().Throw<LedgerValidationException>()
            .WithMessage("*already been posted*");

        Action invalidDuplicateLine = () => new JournalEntry(
            Guid.NewGuid(),
            timestamp.AddMinutes(2),
            "Mismatched line metadata",
            [
                new LedgerEntry(debitEntryId, Guid.NewGuid(), timestamp.AddMinutes(2), LedgerAccounts.Cash, 5m, 0m, "Mismatched line metadata"),
                new LedgerEntry(Guid.NewGuid(), Guid.NewGuid(), timestamp.AddMinutes(2), LedgerAccounts.CapitalAccount, 0m, 5m, "Mismatched line metadata")
            ]);
        invalidDuplicateLine.Should().Throw<LedgerValidationException>();

        var duplicateLineJournalId = Guid.NewGuid();
        var duplicateLineEntry = new JournalEntry(
            duplicateLineJournalId,
            timestamp.AddMinutes(3),
            "Duplicate line id",
            [
                new LedgerEntry(debitEntryId, duplicateLineJournalId, timestamp.AddMinutes(3), LedgerAccounts.Cash, 5m, 0m, "Duplicate line id"),
                new LedgerEntry(Guid.NewGuid(), duplicateLineJournalId, timestamp.AddMinutes(3), LedgerAccounts.CapitalAccount, 0m, 5m, "Duplicate line id")
            ]);

        Action duplicateLinePost = () => ledger.Post(duplicateLineEntry);
        duplicateLinePost.Should().Throw<LedgerValidationException>()
            .WithMessage("*already been posted*");
    }

    [Fact]
    public void GetJournalEntries_CanFilterByStructuredAuditMetadata()
    {
        var ledger = new BacktestLedger();
        var orderId = Guid.NewGuid();
        var fillId = Guid.NewGuid();
        var t1 = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(1);

        ledger.PostLines(
            t1,
            "Buy SPY",
            [
                (LedgerAccounts.Securities("SPY"), 4_000m, 0m),
                (LedgerAccounts.Cash, 0m, 4_000m)
            ],
            new JournalEntryMetadata(ActivityType: "buy", Symbol: "spy", OrderId: orderId, FillId: fillId));

        ledger.PostLines(
            t2,
            "Commission – SPY",
            [
                (LedgerAccounts.CommissionExpense, 1m, 0m),
                (LedgerAccounts.Cash, 0m, 1m)
            ],
            new JournalEntryMetadata(ActivityType: "commission", Symbol: "SPY", OrderId: orderId, FillId: fillId));

        ledger.GetJournalEntries(new LedgerQuery(Symbol: "SPY", OrderId: orderId))
            .Select(entry => entry.Description)
            .Should()
            .Equal("Buy SPY", "Commission – SPY");

        ledger.GetJournalEntries(new LedgerQuery(ActivityType: "commission", FillId: fillId))
            .Should()
            .ContainSingle()
            .Which.Metadata.Symbol.Should().Be("SPY");
    }

    [Fact]
    public void GetRunningBalance_And_SnapshotAsOf_ReconstructHistoricalState()
    {
        var ledger = new BacktestLedger();
        var t1 = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(5);
        var t3 = t2.AddMinutes(5);

        ledger.PostLines(
            t1,
            "Initial capital",
            [
                (LedgerAccounts.Cash, 10_000m, 0m),
                (LedgerAccounts.CapitalAccount, 0m, 10_000m)
            ],
            new JournalEntryMetadata(ActivityType: "capital"));

        ledger.PostLines(
            t2,
            "Buy SPY",
            [
                (LedgerAccounts.Securities("SPY"), 4_000m, 0m),
                (LedgerAccounts.Cash, 0m, 4_000m)
            ],
            new JournalEntryMetadata(ActivityType: "buy", Symbol: "SPY"));

        ledger.PostLines(
            t3,
            "Commission",
            [
                (LedgerAccounts.CommissionExpense, 5m, 0m),
                (LedgerAccounts.Cash, 0m, 5m)
            ],
            new JournalEntryMetadata(ActivityType: "commission", Symbol: "SPY"));

        var runningCash = ledger.GetRunningBalance(LedgerAccounts.Cash);
        runningCash.Select(point => point.Balance).Should().Equal(10_000m, 6_000m, 5_995m);
        runningCash[1].Metadata.ActivityType.Should().Be("buy");

        var snapshot = ledger.SnapshotAsOf(t2);
        snapshot.JournalEntryCount.Should().Be(2);
        snapshot.LedgerEntryCount.Should().Be(4);
        snapshot.Balances[LedgerAccounts.Cash].Should().Be(6_000m);
        snapshot.Balances[LedgerAccounts.Securities("SPY")].Should().Be(4_000m);
        snapshot.Balances.Should().NotContainKey(LedgerAccounts.CommissionExpense);
    }


    [Fact]
    public void ScopedAccounts_CanBeSummarizedAndSnapshottedPerFinancialAccount()
    {
        var ledger = new BacktestLedger();
        var t1 = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var broker1Cash = LedgerAccounts.CashAccount("broker-1");
        var broker2Cash = LedgerAccounts.CashAccount("broker-2");

        ledger.PostLines(
            t1,
            "Seed broker one",
            [
                (broker1Cash, 10_000m, 0m),
                (LedgerAccounts.CapitalAccountFor("broker-1"), 0m, 10_000m)
            ],
            new JournalEntryMetadata(ActivityType: "capital", FinancialAccountId: "broker-1"));

        ledger.PostLines(
            t1.AddMinutes(1),
            "Seed broker two",
            [
                (broker2Cash, 15_000m, 0m),
                (LedgerAccounts.CapitalAccountFor("broker-2"), 0m, 15_000m)
            ],
            new JournalEntryMetadata(ActivityType: "capital", FinancialAccountId: "broker-2"));

        ledger.SummarizeAccounts(financialAccountId: "broker-1")
            .Select(summary => summary.Account.FinancialAccountId)
            .Should()
            .OnlyContain(id => id == "broker-1");

        ledger.SnapshotAsOf(t1.AddMinutes(1), "broker-2").Balances
            .Should()
            .ContainKey(broker2Cash)
            .WhoseValue.Should().Be(15_000m);
    }

}
