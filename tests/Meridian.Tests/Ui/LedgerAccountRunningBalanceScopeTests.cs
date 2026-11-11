using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Tests.Ui;

public sealed class LedgerAccountRunningBalanceScopeTests
{
    private static readonly Guid PeriodId = Guid.NewGuid();
    private static readonly Guid BookId = Guid.NewGuid();

    [Fact]
    public void BuildAccountRunningBalance_TreatsAnOmittedFilterAsAnyAndAnEmptyFilterAsUnscoped()
    {
        // The same account name posted once without a financial account and once with one: the
        // trial balance shows two rows, and each must drill down to its own postings.
        var entries = new[]
        {
            Record(1, "Unscoped receipt", financialAccountId: null, amount: 100m),
            Record(2, "Account 1000 receipt", financialAccountId: "1000", amount: 40m)
        };

        var any = LedgerEndpoints.BuildAccountRunningBalance(entries, PeriodId, BookId, "Cash", symbol: null, financialAccountId: null);
        var unscoped = LedgerEndpoints.BuildAccountRunningBalance(entries, PeriodId, BookId, "Cash", symbol: "", financialAccountId: "");
        var scoped = LedgerEndpoints.BuildAccountRunningBalance(entries, PeriodId, BookId, "Cash", symbol: null, financialAccountId: "1000");

        any.EndingBalance.Should().Be(140m);
        unscoped.Points.Select(point => point.Description).Should().Equal("Unscoped receipt");
        unscoped.EndingBalance.Should().Be(100m);
        unscoped.FinancialAccountId.Should().BeNull();
        scoped.Points.Select(point => point.Description).Should().Equal("Account 1000 receipt");
        scoped.EndingBalance.Should().Be(40m);
    }

    private static LedgerJournalEntryRecord Record(long sequence, string description, string? financialAccountId, decimal amount)
    {
        var journalEntryId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(sequence);
        var entry = new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            [
                new LedgerEntry(Guid.NewGuid(), journalEntryId, timestamp,
                    new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: financialAccountId), amount, 0m, description),
                new LedgerEntry(Guid.NewGuid(), journalEntryId, timestamp,
                    new LedgerAccount("Capital", LedgerAccountType.Equity), 0m, amount, description)
            ]);
        return new LedgerJournalEntryRecord(
            entry, Guid.NewGuid(), PeriodId, null, null, sequence, timestamp,
            AccountingBasisKindDto.Primary, "legacy-v1", "legacy-v1", null, null, null, null,
            LedgerPostingKindDto.Originating, null);
    }
}
