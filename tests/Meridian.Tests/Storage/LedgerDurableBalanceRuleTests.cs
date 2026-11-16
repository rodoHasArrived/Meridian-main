using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class LedgerDurableBalanceRuleTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly LedgerAccount Cash = new("Cash", LedgerAccountType.Asset);
    private static readonly LedgerAccount Capital = new("Capital", LedgerAccountType.Equity);

    [Fact]
    public void ExactlyBalancedEntry_IsAccepted()
    {
        LedgerDurableBalanceRule.Violation(Entry((Cash, 100m, 0m), (Capital, 0m, 100m))).Should().BeNull();
    }

    [Fact]
    public void ResidualInsideTheInMemoryTolerance_IsRefusedWithTheResidual()
    {
        // 0.0000005 passes JournalEntry.IsBalanced but not the journal seal's exact comparison.
        var entry = Entry((Cash, 100.0000005m, 0m), (Capital, 0m, 100m));
        entry.IsBalanced.Should().BeTrue();

        var act = () => LedgerDurableBalanceRule.Ensure(entry);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage($"*{entry.JournalEntryId}*differ by 0.0000005*");
    }

    [Fact]
    public void DifferenceBelowTheStoredScale_IsAcceptedBecauseTheStoreRoundsItAway()
    {
        LedgerDurableBalanceRule.Violation(Entry((Cash, 100.00000000004m, 0m), (Capital, 0m, 100m))).Should().BeNull();
    }

    private static JournalEntry Entry(params (LedgerAccount Account, decimal Debit, decimal Credit)[] lines)
    {
        var id = Guid.NewGuid();
        return new JournalEntry(id, At, "test", lines
            .Select(line => new LedgerEntry(Guid.NewGuid(), id, At, line.Account, line.Debit, line.Credit, "test"))
            .ToArray());
    }
}
