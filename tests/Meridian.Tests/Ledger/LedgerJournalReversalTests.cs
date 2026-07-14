using FluentAssertions;
using Meridian.Ledger;
using Xunit;
using DomainLedger = Meridian.Ledger.Ledger;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Unit tests for <see cref="LedgerJournalReversal"/> — the primitive that books a correction as a
/// new immutable reversing entry (debit/credit swapped) instead of mutating a posted journal.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LedgerJournalReversalTests
{
    private static readonly DateTimeOffset Ts = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid SecurityId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000010");

    private static JournalEntry OriginalDividend(Guid id)
    {
        const string description = "Dividend declared ABC";
        var meta = new JournalEntryMetadata(
            ActivityType: "Dividend",
            Symbol: "ABC",
            SecurityId: SecurityId,
            LedgerView: LedgerViewKind.SecurityMaster,
            IdempotencyKey: "dividend|ABC|2026-03-01");

        return new JournalEntry(
            id,
            Ts,
            description,
            [
                new LedgerEntry(Guid.NewGuid(), id, Ts, LedgerAccounts.DividendReceivable("ABC"), 100m, 0m, description),
                new LedgerEntry(Guid.NewGuid(), id, Ts, LedgerAccounts.DividendIncome, 0m, 100m, description),
            ],
            meta);
    }

    [Fact]
    public void Reverse_SwapsDebitAndCreditAndStaysBalanced()
    {
        var originalId = Guid.NewGuid();
        var original = OriginalDividend(originalId);
        var reversalId = Guid.NewGuid();

        var reversal = LedgerJournalReversal.Reverse(original, reversalId, Ts, "cancelled");

        reversal.JournalEntryId.Should().Be(reversalId);
        reversal.IsBalanced.Should().BeTrue();

        var receivable = LedgerAccounts.DividendReceivable("ABC");
        reversal.Lines.Single(line => line.Account == receivable).Credit.Should().Be(100m); // was a debit
        reversal.Lines.Single(line => line.Account == receivable).Debit.Should().Be(0m);
        reversal.Lines.Single(line => line.Account == LedgerAccounts.DividendIncome).Debit.Should().Be(100m); // was a credit
    }

    [Fact]
    public void Reverse_LinksToOriginalAndDropsIdempotencyKey()
    {
        var originalId = Guid.NewGuid();
        var reversal = LedgerJournalReversal.Reverse(OriginalDividend(originalId), Guid.NewGuid(), Ts, "cancelled");

        reversal.Description.Should().Contain("Reversal of").And.Contain("cancelled");
        reversal.Metadata.Tags![LedgerJournalReversal.ReversalOfTag].Should().Be(originalId.ToString("D"));
        reversal.Metadata.Tags[LedgerJournalReversal.ReversalReasonTag].Should().Be("cancelled");
        reversal.Metadata.SecurityId.Should().Be(SecurityId);
        reversal.Metadata.IdempotencyKey.Should().BeNull("a reversal is an independently-guarded posting");
    }

    [Fact]
    public void Reverse_ThenPost_NetsOriginalToZero()
    {
        var ledger = new DomainLedger();
        var originalId = Guid.NewGuid();
        var original = OriginalDividend(originalId);
        ledger.Post(original);

        ledger.Post(LedgerJournalReversal.Reverse(original, Guid.NewGuid(), Ts, "cancelled"));

        ledger.GetBalance(LedgerAccounts.DividendReceivable("ABC")).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.DividendIncome).Should().Be(0m);
    }

    [Fact]
    public void Reverse_WithSameIdAsOriginal_Throws()
    {
        var originalId = Guid.NewGuid();
        var act = () => LedgerJournalReversal.Reverse(OriginalDividend(originalId), originalId, Ts);
        act.Should().Throw<ArgumentException>();
    }
}
