using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerEntryCurrencyTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 3, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LegacyLeg_HasNoCurrencyDetail()
    {
        var entry = new LedgerEntry(Guid.NewGuid(), Guid.NewGuid(), Timestamp, LedgerAccounts.Cash, 100m, 0m, "cash");
        entry.Currency.Should().BeNull();
    }

    [Fact]
    public void CurrencyAwareLeg_PreservesTransactionDetail()
    {
        var currency = new LedgerEntryCurrency("EUR", "USD", transactionDebit: 100m, transactionCredit: 0m, fxRateToFunctional: 1.1m);
        var entry = new LedgerEntry(Guid.NewGuid(), Guid.NewGuid(), Timestamp, LedgerAccounts.Cash, 110m, 0m, "cash", dimensions: null, currency: currency);

        entry.Currency.Should().NotBeNull();
        entry.Currency!.TransactionCurrency.Should().Be("EUR");
        entry.Currency.FunctionalCurrency.Should().Be("USD");
        entry.Currency.TransactionDebit.Should().Be(100m);
        entry.Currency.FxRateToFunctional.Should().Be(1.1m);
    }

    [Fact]
    public void CurrencyDetail_MustMatchDebitCreditSide()
    {
        var currencyOnCreditSide = new LedgerEntryCurrency("EUR", "USD", transactionDebit: 0m, transactionCredit: 100m, fxRateToFunctional: 1.1m);
        var construct = () => new LedgerEntry(Guid.NewGuid(), Guid.NewGuid(), Timestamp, LedgerAccounts.Cash, 110m, 0m, "cash", dimensions: null, currency: currencyOnCreditSide);

        construct.Should().Throw<LedgerValidationException>();
    }

    [Fact]
    public void MultiCurrencyProjection_ProducesCurrencyAwareLines()
    {
        var input = new MultiCurrencyJournalInput(
            Timestamp,
            "eur cash purchase",
            "USD",
            [
                new MultiCurrencyJournalLineInput(LedgerAccounts.CashInCurrency("EUR"), "EUR", localDebit: 100m, localCredit: 0m, fxRateToBase: 1.1m),
                new MultiCurrencyJournalLineInput(LedgerAccounts.Cash, "USD", localDebit: 0m, localCredit: 110m, fxRateToBase: 1m),
            ]);
        var projection = MultiCurrencyJournalProjector.Project(input);

        var lines = projection.ToCurrencyAwareLedgerLines();
        lines.Should().HaveCount(2);
        var eurLine = lines.Single(line => line.currency!.TransactionCurrency == "EUR");
        eurLine.debit.Should().Be(110m); // functional (base) amount
        eurLine.currency!.TransactionDebit.Should().Be(100m); // local amount preserved
        eurLine.currency.FxRateToFunctional.Should().Be(1.1m);
    }

    [Fact]
    public void PostLines_AcceptsCurrencyAwareLines()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var currency = new LedgerEntryCurrency("EUR", "USD", transactionDebit: 100m, transactionCredit: 0m, fxRateToFunctional: 1.1m);
        ledger.PostLines(
            Timestamp,
            "eur cash",
            [
                (LedgerAccounts.CashInCurrency("EUR"), 110m, 0m, (LedgerLineDimensionSet?)null, (LedgerEntryCurrency?)currency),
                (LedgerAccounts.Cash, 0m, 110m, null, null),
            ]);

        var posted = ledger.Journal.Single();
        posted.Lines.Should().Contain(line => line.Currency != null && line.Currency.TransactionCurrency == "EUR");
    }
}
