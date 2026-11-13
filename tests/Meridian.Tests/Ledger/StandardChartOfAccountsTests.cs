using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class StandardChartOfAccountsTests
{
    private readonly IChartOfAccounts _chart = StandardChartOfAccounts.Instance;

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Resolve_UnregisteredRole_RejectsInsteadOfDefaultingToAsset(int role)
    {
        var resolve = () => _chart.Resolve((LedgerAccountRole)role, "broker", "AAPL");

        resolve.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("role");
    }

    [Theory]
    [InlineData(LedgerAccountRole.Securities)]
    [InlineData(LedgerAccountRole.ShortSecuritiesPayable)]
    [InlineData(LedgerAccountRole.FuturesMtmSettlement)]
    public void Resolve_InstrumentRoleWithoutSymbol_Rejects(LedgerAccountRole role)
    {
        var resolve = () => _chart.Resolve(role, "broker", " ");

        resolve.Should().Throw<ArgumentException>().WithParameterName("symbol");
    }

    [Fact]
    public void Resolve_CanonicalIdentities_PreserveHistoricalLookupsAndAccountIsolation()
    {
        _chart.Resolve(LedgerAccountRole.Cash).Should().Be(LedgerAccounts.Cash);
        _chart.Resolve(LedgerAccountRole.Capital, " broker-a ").Should().Be(LedgerAccounts.CapitalAccountFor("broker-a"));
        var securities = _chart.Resolve(LedgerAccountRole.Securities, " broker-a ", " aapl ");
        securities.Should().Be(LedgerAccounts.Securities("AAPL", "broker-a"));
        securities.Should().Be(_chart.Resolve(LedgerAccountRole.Securities, "BROKER-A", "AAPL"));
        securities.Should().NotBe(_chart.Resolve(LedgerAccountRole.Securities, "broker-b", "AAPL"));
        securities.Should().NotBe(_chart.Resolve(LedgerAccountRole.Securities, "broker-a", "MSFT"));
        _chart.Resolve(LedgerAccountRole.ShortSecuritiesPayable, "broker-a", "aapl")
            .Should().Be(LedgerAccounts.ShortSecuritiesPayable("AAPL", "broker-a"));
        _chart.Resolve(LedgerAccountRole.FuturesMtmSettlement, "broker-a", "esz6")
            .Should().Be(LedgerAccounts.FuturesMtmSettlement("ESZ6", "broker-a"));
    }

    [Fact]
    public void Resolve_NonInstrumentAccountWithSymbol_RejectsInsteadOfCreatingSeparateCashBucket()
    {
        var resolve = () => _chart.Resolve(LedgerAccountRole.Cash, "broker", "AAPL");

        resolve.Should().Throw<ArgumentException>().WithParameterName("symbol");
    }

    [Fact]
    public void Resolve_LendingRoleWithoutAccount_RejectsUnscopedLoanPosting()
    {
        var resolve = () => _chart.Resolve(LedgerAccountRole.LoanPrincipal, symbol: "LOAN-A");

        resolve.Should().Throw<ArgumentException>().WithParameterName("financialAccountId");
    }

    [Fact]
    public void TradingAndLending_CanShareTrialBalanceWithoutSplittingCashIdentity()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var timestamp = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        const string accountId = "account-a";
        ledger.PostLines(timestamp, "Capital", new[]
        {
            (_chart.Resolve(LedgerAccountRole.Cash, accountId), 1_000m, 0m),
            (_chart.Resolve(LedgerAccountRole.Capital, accountId), 0m, 1_000m)
        });
        ledger.PostLines(timestamp, "Security acquisition", new[]
        {
            (_chart.Resolve(LedgerAccountRole.Securities, accountId, "AAPL"), 200m, 0m),
            (_chart.Resolve(LedgerAccountRole.Cash, accountId), 0m, 200m)
        });
        ledger.PostLines(timestamp, "Loan funding", new[]
        {
            (_chart.Resolve(LedgerAccountRole.LoanPrincipal, accountId, "LOAN-A"), 300m, 0m),
            (_chart.Resolve(LedgerAccountRole.Cash, accountId), 0m, 300m)
        });

        ledger.GetBalance(LedgerAccounts.CashAccount(accountId)).Should().Be(500m);
        var balance = ledger.TrialBalance();
        balance.Should().HaveCount(4);
        balance.Where(row => row.Key.AccountType == LedgerAccountType.Asset).Sum(row => row.Value)
            .Should().Be(1_000m);
        balance.Single(row => row.Key.AccountType == LedgerAccountType.Equity).Value.Should().Be(1_000m);
        ledger.Journal.Should().OnlyContain(journal => journal.IsBalanced);
    }
}
