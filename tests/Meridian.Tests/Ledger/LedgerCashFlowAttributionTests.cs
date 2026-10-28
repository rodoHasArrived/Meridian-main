using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerCashFlowAttributionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static readonly DateTimeOffset End = Start.AddMonths(1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransferBatchedWithNoncashActivity_RequiresAttribution(bool reclassification)
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(Start.AddDays(1), "transfer and independent noncash activity",
            [(LedgerAccounts.Cash, 100m, 0m),
             (new LedgerAccount("Cash reserve", LedgerAccountType.Asset), 0m, 100m),
             (reclassification ? LedgerAccounts.Securities("AAPL") : LedgerAccounts.ManagementFeeExpenseFor("fund-a"), 25m, 0m),
             (LedgerAccounts.ManagementFeePayableFor("fund-a"), 0m, 25m)]);

        var report = () => LedgerFinancialStatementBuilder.BuildForPeriod(ledger, Start, End);

        report.Should().Throw<LedgerValidationException>().WithMessage("*cash-flow attribution*");
    }
}
