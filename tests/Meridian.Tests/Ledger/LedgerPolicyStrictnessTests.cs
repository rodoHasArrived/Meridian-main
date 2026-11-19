using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FSharp.Ledger;
using Meridian.Ledger;
using Xunit;
using LedgerBook = Meridian.Ledger.Ledger;

namespace Meridian.Tests.Ledger;

/// <summary>
/// A ledger bound to a <see cref="LedgerPolicy"/> treats the chart of accounts as the only account
/// registry and requires explicit functional-currency detail on every line. Each gap that an unbound
/// ledger tolerates is a hard error here.
/// </summary>
public sealed class LedgerPolicyStrictnessTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 2, 14, 30, 0, TimeSpan.Zero);
    private readonly IChartOfAccounts _chart = StandardChartOfAccounts.Instance;

    [Fact]
    public void PostLines_RegisteredAccounts_StampFunctionalCurrencyOnEveryLine()
    {
        var ledger = new LedgerBook(LedgerPolicy.Trading("usd"));

        ledger.PostLines(At, "Buy 100 AAPL @ 190", [
            (_chart.Resolve(LedgerAccountRole.Securities, "broker-1", "AAPL"), 19_000m, 0m),
            (_chart.Resolve(LedgerAccountRole.Cash, "broker-1"), 0m, 19_000m)
        ]);

        ledger.Policy!.FunctionalCurrency.Should().Be("USD");
        var lines = ledger.Journal.Single().Lines;
        lines.Should().OnlyContain(line =>
            line.Currency != null &&
            line.Currency.FunctionalCurrency == "USD" &&
            line.Currency.TransactionCurrency == "USD" &&
            line.Currency.FxRateToFunctional == 1m);
        lines[0].Currency!.TransactionDebit.Should().Be(19_000m);
        lines[1].Currency!.TransactionCredit.Should().Be(19_000m);
    }

    [Fact]
    public void Post_UnregisteredAccount_IsRejectedWithoutMutatingTheLedger()
    {
        var ledger = new LedgerBook(LedgerPolicy.Trading());
        var invented = new LedgerAccount("Suspense Clearing", LedgerAccountType.Asset);

        var post = () => ledger.PostLines(At, "Unregistered posting", [
            (invented, 50m, 0m),
            (_chart.Resolve(LedgerAccountRole.Cash), 0m, 50m)
        ]);

        post.Should().Throw<LedgerValidationException>()
            .WithMessage("*Suspense Clearing*not registered in the chart of accounts*");
        ledger.JournalEntryCount.Should().Be(0);
        ledger.Accounts.Should().BeEmpty();
    }

    [Fact]
    public void Post_RegisteredNameWithWrongType_IsRejectedRatherThanReinterpreted()
    {
        var ledger = new LedgerBook(LedgerPolicy.Trading());
        // "Cash" is registered as an asset. A liability named "Cash" is a different, unknown account.
        var miscastCash = new LedgerAccount("Cash", LedgerAccountType.Liability);

        var post = () => ledger.PostLines(At, "Miscast cash", [
            (_chart.Resolve(LedgerAccountRole.Cash), 10m, 0m),
            (miscastCash, 0m, 10m)
        ]);

        post.Should().Throw<LedgerValidationException>().WithMessage("*Cash*Liability*not registered*");
    }

    [Fact]
    public void Post_UnboundLedger_StillAcceptsUnregisteredAccountsWithoutCurrency()
    {
        var ledger = new LedgerBook();

        ledger.PostLines(At, "Legacy fund posting", [
            (new LedgerAccount("Investor Capital", LedgerAccountType.Equity), 0m, 10m),
            (new LedgerAccount("Cash", LedgerAccountType.Asset), 10m, 0m)
        ]);

        ledger.Policy.Should().BeNull();
        ledger.Journal.Single().Lines.Should().OnlyContain(line => line.Currency == null);
    }

    [Fact]
    public void Post_LineWithoutCurrency_IsRejectedByBoundLedger()
    {
        var ledger = new LedgerBook(LedgerPolicy.Trading());
        var journalId = Guid.NewGuid();
        var entry = new JournalEntry(journalId, At, "Capital", [
            new LedgerEntry(Guid.NewGuid(), journalId, At, _chart.Resolve(LedgerAccountRole.Cash), 100m, 0m, "Capital"),
            new LedgerEntry(Guid.NewGuid(), journalId, At, _chart.Resolve(LedgerAccountRole.Capital), 0m, 100m, "Capital")
        ]);

        var post = () => ledger.Post(entry);

        post.Should().Throw<LedgerValidationException>().WithMessage("*has no currency*USD*");
        ledger.JournalEntryCount.Should().Be(0);
    }

    [Fact]
    public void Post_LineInAnotherFunctionalCurrency_IsRejected()
    {
        var ledger = new LedgerBook(LedgerPolicy.Trading("USD"));

        var post = () => ledger.PostLines(At, "EUR-functional capital", [
            (_chart.Resolve(LedgerAccountRole.Cash), 100m, 0m, (LedgerLineDimensionSet?)null,
                new LedgerEntryCurrency("EUR", "EUR", 100m, 0m, 1m)),
            (_chart.Resolve(LedgerAccountRole.Capital), 0m, 100m, (LedgerLineDimensionSet?)null,
                new LedgerEntryCurrency("EUR", "EUR", 0m, 100m, 1m))
        ]);

        post.Should().Throw<LedgerValidationException>().WithMessage("*functional currency 'EUR'*'USD'*");
    }

    [Fact]
    public void Post_ForeignTransactionCurrencyInTheFunctionalCurrency_IsAccepted()
    {
        var ledger = new LedgerBook(LedgerPolicy.Trading("USD"));

        ledger.PostLines(At, "Buy 10 SAP in EUR", [
            (_chart.Resolve(LedgerAccountRole.Securities, symbol: "SAP"), 2_160m, 0m, (LedgerLineDimensionSet?)null,
                new LedgerEntryCurrency("EUR", "USD", 2_000m, 0m, 1.08m)),
            (_chart.Resolve(LedgerAccountRole.Cash), 0m, 2_160m, (LedgerLineDimensionSet?)null,
                new LedgerEntryCurrency("EUR", "USD", 0m, 2_000m, 1.08m))
        ]);

        ledger.JournalEntryCount.Should().Be(1);
    }

    [Fact]
    public void Post_UnbalancedJournal_IsRejectedByBoundLedger()
    {
        var ledger = new LedgerBook(LedgerPolicy.Trading());

        var post = () => ledger.PostLines(At, "Unbalanced", [
            (_chart.Resolve(LedgerAccountRole.Cash), 100m, 0m),
            (_chart.Resolve(LedgerAccountRole.Capital), 0m, 99m)
        ]);

        post.Should().Throw<LedgerValidationException>();
        ledger.JournalEntryCount.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("US1")]
    [InlineData("DOLLAR")]
    public void Policy_RequiresIsoFunctionalCurrency(string currency)
    {
        var create = () => new LedgerPolicy(StandardChartOfAccounts.Instance, currency);

        create.Should().Throw<LedgerValidationException>();
    }

    [Fact]
    public void BalanceTolerance_IsTheSameInTheCSharpModelAndTheFSharpKernel()
    {
        var journalId = Guid.NewGuid();
        var withinTolerance = LedgerToleranceConstants.Balance;
        var beyondTolerance = LedgerToleranceConstants.Balance * 2m;

        static bool KernelAccepts(Guid id, decimal credit) => LedgerInterop.ValidateJournalEntry(
            id, At, "Tolerance",
            [
                new LedgerLineInput(Guid.NewGuid(), id, At, "Cash", 0, null, null, 1m, 0m, "Tolerance", "USD"),
                new LedgerLineInput(Guid.NewGuid(), id, At, "Capital", 2, null, null, 0m, credit, "Tolerance", "USD")
            ],
            Array.Empty<Guid>(), Array.Empty<Guid>()).IsValid;

        KernelAccepts(journalId, 1m - withinTolerance).Should().BeTrue();
        KernelAccepts(journalId, 1m - beyondTolerance).Should().BeFalse();
    }
}
