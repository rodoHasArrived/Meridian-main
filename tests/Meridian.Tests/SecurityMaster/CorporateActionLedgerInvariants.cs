using FluentAssertions;
using FluentAssertions.Execution;
using Meridian.Ledger;
using DomainLedger = Meridian.Ledger.Ledger;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Ledger-side invariant assertions for the golden corporate-action pack: balance, receivable
/// settlement, and duplicate-posting checks over whatever the Security Master ledger bridge
/// produced for a scenario.
/// </summary>
internal static class CorporateActionLedgerInvariants
{
    /// <summary>Every posted journal entry balances: total debits equal total credits.</summary>
    public static void AssertAllJournalsBalanced(DomainLedger ledger)
    {
        using var scope = new AssertionScope();
        foreach (var entry in ledger.Journal)
        {
            entry.IsBalanced.Should().BeTrue(
                "journal entry '{0}' ({1}) must balance", entry.JournalEntryId, entry.Description);
        }
    }

    /// <summary>
    /// The dividend receivable for <paramref name="ticker"/> nets to zero: every declaration
    /// with a pay date must be relieved in full by its receipt entry.
    /// </summary>
    public static void AssertReceivablesSettled(DomainLedger ledger, string ticker)
    {
        var receivable = LedgerAccounts.DividendReceivable(ticker.Trim().ToUpperInvariant());
        ledger.GetBalance(receivable).Should().Be(0m,
            "pay-date settlement must zero the dividend receivable for {0}", ticker);
    }

    /// <summary>No journal entry id appears twice in the posted journal.</summary>
    public static void AssertNoDuplicateJournalIds(DomainLedger ledger)
    {
        var ids = ledger.Journal.Select(static entry => entry.JournalEntryId).ToList();
        ids.Should().OnlyHaveUniqueItems("the bridge must never double-book a corporate action");
    }
}
