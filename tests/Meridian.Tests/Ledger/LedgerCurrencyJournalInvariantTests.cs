using FluentAssertions;
using Meridian.Ledger;

namespace Meridian.Tests.Ledger;

public sealed class LedgerCurrencyJournalInvariantTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OpposingAmountsInDifferentFunctionalCurrencies_DoNotBalance()
    {
        var entry = Journal(
            100m, new LedgerEntryCurrency("USD", "USD", 100m, 0m, 1m),
            100m, new LedgerEntryCurrency("EUR", "EUR", 0m, 100m, 1m));

        entry.IsBalanced.Should().BeFalse("unlike functional currencies cannot offset by nominal amount");
        var ledger = new Meridian.Ledger.Ledger();
        var post = () => ledger.Post(entry);
        post.Should().Throw<LedgerValidationException>();
        ledger.Journal.Should().BeEmpty();
    }

    [Fact]
    public void FxExchange_BalancesInItsDeclaredFunctionalCurrency()
    {
        var entry = Journal(
            110m, new LedgerEntryCurrency("EUR", "USD", 100m, 0m, 1.1m),
            110m, new LedgerEntryCurrency("USD", "USD", 0m, 110m, 1m));

        entry.IsBalanced.Should().BeTrue("EUR bought with USD is an exchange, not unequal nominal currencies");
        var ledger = new Meridian.Ledger.Ledger();
        ledger.Post(entry);
        ledger.Journal.Should().ContainSingle();
    }

    [Fact]
    public void LegacyLinesCanBalanceExplicitFunctionalAmounts()
    {
        var entry = Journal(
            110m, new LedgerEntryCurrency("EUR", "USD", 100m, 0m, 1.1m),
            110m, null);

        entry.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void OneTransactionCurrency_CannotHideMissingAmountsWithDifferentRates()
    {
        var entry = Journal(
            110m, new LedgerEntryCurrency("EUR", "USD", 100m, 0m, 1.1m),
            110m, new LedgerEntryCurrency("EUR", "USD", 0m, 110m, 1m));

        entry.IsBalanced.Should().BeFalse("functional balance alone hides an unexplained EUR 10 credit");
    }

    [Fact]
    public void SameCurrencyAmounts_CannotDeclareAConversionRate()
    {
        var create = () => new LedgerEntryCurrency("USD", "USD", 100m, 0m, 2m);

        create.Should().Throw<LedgerValidationException>().WithMessage("*FX rate of one*");
    }

    private static JournalEntry Journal(decimal debit, LedgerEntryCurrency? debitCurrency,
        decimal credit, LedgerEntryCurrency? creditCurrency)
    {
        var id = Guid.NewGuid();
        return new JournalEntry(id, Timestamp, "Currency invariant",
        [
            new LedgerEntry(Guid.NewGuid(), id, Timestamp, LedgerAccounts.Cash, debit, 0m,
                "Currency invariant", currency: debitCurrency),
            new LedgerEntry(Guid.NewGuid(), id, Timestamp, LedgerAccounts.CapitalAccount, 0m, credit,
                "Currency invariant", currency: creditCurrency)
        ]);
    }
}
