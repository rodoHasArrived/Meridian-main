using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class SecurityTradePostingProjectorTests
{
    [Fact]
    public void ShortPartialCover_RelievesOriginalProceedsAndRecognizesGain()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        ledger.PostLines(now, "Short opening", SecurityTradePostingProjector.Project(
            new(SecurityTradePostingKind.OpenShort, 10m, 1_000m), "AAPL", "broker"));
        ledger.PostLines(now, "Partial cover", SecurityTradePostingProjector.Project(
            new(SecurityTradePostingKind.CoverShort, 4m, 360m, 400m), "AAPL", "broker"));

        ledger.GetBalance(LedgerAccounts.CashAccount("broker")).Should().Be(640m);
        ledger.GetBalance(LedgerAccounts.ShortSecuritiesPayable("AAPL", "broker")).Should().Be(600m);
        ledger.GetBalance(LedgerAccounts.RealizedGainFor("broker")).Should().Be(40m);
        ledger.GetBalance(LedgerAccounts.Securities("AAPL", "broker")).Should().Be(0m);
        ledger.Journal.Should().OnlyContain(entry => entry.IsBalanced);
    }

    [Fact]
    public void SevereLossOnMarginSale_CreditsCashRatherThanProducingNegativeDebit()
    {
        var posting = new SecurityTradePosting(SecurityTradePostingKind.CloseLong, 10m, 20m, 100m, LoanRepaid: 50m);

        var lines = SecurityTradePostingProjector.Project(posting, "AAPL", "broker");

        lines.Should().OnlyContain(line => line.Debit >= 0m && line.Credit >= 0m);
        lines.Single(line => line.Account.Name == "Cash").Credit.Should().Be(30m);
        lines.Single(line => line.Account.Name == "Margin Loan Payable").Debit.Should().Be(50m);
        lines.Single(line => line.Account.Name == "Realized Loss").Debit.Should().Be(80m);
        lines.Single(line => line.Account.Name == "Securities").Credit.Should().Be(100m);
        lines.Sum(line => line.Debit).Should().Be(lines.Sum(line => line.Credit));
    }

    [Fact]
    public void ClosingBasis_MissingIsRejectedButExplicitKnownZeroIsSupported()
    {
        var missing = () => SecurityTradePostingProjector.Project(
            new(SecurityTradePostingKind.CloseLong, 1m, 10m), "AAPL");
        missing.Should().Throw<ArgumentException>().WithMessage("*explicit retained carrying value*");

        var knownZero = SecurityTradePostingProjector.Project(
            new(SecurityTradePostingKind.CloseLong, 1m, 10m, 0m), "AAPL");
        knownZero.Should().HaveCount(2);
        knownZero.Single(line => line.Account == LedgerAccounts.RealizedGain).Credit.Should().Be(10m);
    }

    [Theory]
    [InlineData(SecurityTradePostingKind.CloseLong, "Realized Loss")]
    [InlineData(SecurityTradePostingKind.CoverShort, "Realized Gain")]
    public void ZeroProceedsSyntheticClose_RelievesKnownBasisWithoutInventingCash(
        SecurityTradePostingKind kind, string resultAccount)
    {
        var lines = SecurityTradePostingProjector.Project(new(kind, 1m, 0m, 10m), "DELISTED");

        lines.Should().HaveCount(2);
        lines.Should().NotContain(line => line.Account.Name == "Cash");
        var result = lines.Single(line => line.Account.Name == resultAccount);
        (result.Debit + result.Credit).Should().Be(10m);
        lines.Sum(line => line.Debit).Should().Be(lines.Sum(line => line.Credit));
    }

    [Fact]
    public void ExactBasis_IsNotReconstructedFromRoundedQuotes()
    {
        const decimal basis = 1.0000000000000000000000000001m;
        var lines = SecurityTradePostingProjector.Project(
            new(SecurityTradePostingKind.CloseLong, 3m, 2m, basis), "AAPL");

        lines.Single(line => line.Account.Name == "Securities").Credit.Should().Be(basis);
        lines.Single(line => line.Account.Name == "Realized Gain").Credit.Should().Be(2m - basis);
        lines.Sum(line => line.Debit).Should().Be(lines.Sum(line => line.Credit));
    }

    [Theory]
    [InlineData(SecurityTradePostingKind.OpenLong, 1, 0, 0, 0)]
    [InlineData(SecurityTradePostingKind.OpenShort, 1, 10, 1, 0)]
    [InlineData(SecurityTradePostingKind.CoverShort, 1, 10, 10, 1)]
    [InlineData((SecurityTradePostingKind)999, 1, 10, 0, 0)]
    public void InvalidFacts_AreRejectedBeforePosting(
        SecurityTradePostingKind kind, int quantity, int gross, int basis, int borrowed)
    {
        var project = () => SecurityTradePostingProjector.Project(
            new(kind, quantity, gross, basis, borrowed), "AAPL");
        project.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ChartDefinition_ReturnsMetadataWithoutManufacturingLoanScope()
    {
        var chart = StandardChartOfAccounts.Instance;
        var definition = chart.GetDefinition(LedgerAccountRole.LoanPrincipal);
        definition.Name.Should().Be("LoanPrincipal");
        definition.AccountType.Should().Be(LedgerAccountType.Asset);
        definition.Symbol.Should().BeNull();
        definition.FinancialAccountId.Should().BeNull();
        var unknown = () => chart.GetDefinition((LedgerAccountRole)(-1));
        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ClassifiedCashFlow_DoesNotTreatUnknownLiabilityAndGainAsKnownShortCover()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var entry = new JournalEntry(id, now, "Unsupported liability settlement",
        [
            new LedgerEntry(Guid.NewGuid(), id, now, new LedgerAccount("Unknown liability", LedgerAccountType.Liability), 100m, 0m, "Unsupported liability settlement"),
            new LedgerEntry(Guid.NewGuid(), id, now, LedgerAccounts.Cash, 0m, 90m, "Unsupported liability settlement"),
            new LedgerEntry(Guid.NewGuid(), id, now, LedgerAccounts.RealizedGain, 0m, 10m, "Unsupported liability settlement")
        ]);
        ledger.Post(LedgerCashFlowAttribution.WithClassifiedActivity(entry, LedgerCashFlowCategory.Investing, "Unsupported"));

        var report = () => LedgerFinancialStatementBuilder.BuildForPeriod(ledger, now.AddTicks(-1), now.AddTicks(1));

        report.Should().Throw<LedgerValidationException>().WithMessage("*invalid cash-flow attribution*");
    }
}
