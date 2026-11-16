using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;

namespace Meridian.Backtesting.Tests;

public sealed class LedgerIntegrityDisclosureTests
{
    private static readonly BiasDisclosureReport Baseline = new(
        FillTiming.NextBar,
        FillConservatism.Conservative,
        DelistingPolicy.LiquidateAtLastPrice,
        BiasDisclosureReport.UniverseSourceExplicit,
        CorporateActionsAdjusted: false,
        [],
        [],
        [],
        [new BiasDisclosureItem("in-sample", BiasSeverity.Caution, "In sample", "Not validated out-of-sample.")]);

    [Fact]
    public void ReconciledRun_LeavesTheDisclosureUntouched()
    {
        BacktestEngine.WithLedgerIntegrityDisclosure(Baseline, []).Should().BeSameAs(Baseline);
    }

    [Fact]
    public void RunWithBreaks_DisclosesThemAsTheMostSevereItem()
    {
        var day1 = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero);
        LedgerIntegrityBreak[] breaks =
        [
            new(day1, "AccountCash", "acct-1", null, 100m, 90m, "Ledger Cash does not match the retained financial account."),
            new(day1.AddDays(1), "AccountCash", "acct-1", null, 100m, 90m, "Ledger Cash does not match the retained financial account."),
            new(day1.AddDays(1), "SecuritiesBasis", "acct-1", "SPY", 50m, 60m, "Ledger Securities does not match the retained financial account.")
        ];

        var disclosed = BacktestEngine.WithLedgerIntegrityDisclosure(Baseline, breaks);

        disclosed.MaxSeverity.Should().Be(BiasSeverity.Warning);
        var item = disclosed.Items[0];
        item.Code.Should().Be("ledger-integrity-break");
        item.Title.Should().Be("3 ledger integrity difference(s) on 2 day(s)");
        item.Detail.Should().Contain("AccountCash, SecuritiesBasis")
            .And.Contain("2024-01-02")
            .And.Contain("Expected 100, ledger 90");
        disclosed.Items.Should().HaveCount(2).And.Contain(Baseline.Items[0]);
    }
}
