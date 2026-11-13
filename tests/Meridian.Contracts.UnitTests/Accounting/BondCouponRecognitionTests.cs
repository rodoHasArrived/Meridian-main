using Meridian.Contracts.Accounting.Lots;

namespace Meridian.Contracts.UnitTests.Accounting;

public sealed class BondCouponRecognitionTests
{
    private static readonly BondCouponBookAccountsDto Accounts = new("Receivable", "Payable", "Income", "Cash", "Tax", "Fees");

    [Fact]
    public void RepeatedNegativeCouponAccruesOnlyIncrementalPayable()
    {
        // Previous cumulative earning -.5; new cumulative earning -1: only -.5 is new income.
        var lines = BondCouponRecognitionCalculator.Lines("USD", Accounts, -.5m, -1m, []);
        Assert.Equal(.5m, lines.Single(l => l.AccountId == "Payable").Credit);
        Assert.Equal(.5m, lines.Single(l => l.AccountId == "Income").Debit);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.Empty(BondCouponRecognitionCalculator.Lines("USD", Accounts, 0m, -1m, []));
    }

    [Fact]
    public void OutgoingNegativeCouponClearsPayableAndSeparatesPositiveBankFee()
    {
        var lines = BondCouponRecognitionCalculator.Lines("USD", Accounts, 0m, -100m,
            [new("allocation", "observed-bank-payment", -105m, -100m, Fees: 5m)]);
        Assert.Equal(100m, lines.Single(l => l.AccountId == "Payable").Debit);
        Assert.Equal(105m, lines.Single(l => l.AccountId == "Cash").Credit);
        Assert.Equal(5m, lines.Single(l => l.AccountId == "Fees").Debit);
        Assert.DoesNotContain(lines, l => l.AccountId == "Income");
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    [Fact]
    public void PartialNetCouponCollectionLeavesRemainingReceivableWithoutNewIncome()
    {
        var lines = BondCouponRecognitionCalculator.Lines("USD", Accounts, 0m, 100m,
            [new("allocation", "observed-receipt", 36m, 40m, WithholdingTax: 3m, Fees: 1m)]);
        Assert.Equal(40m, lines.Single(l => l.AccountId == "Receivable").Credit);
        Assert.Equal(36m, lines.Single(l => l.AccountId == "Cash").Debit);
        Assert.Equal(3m, lines.Single(l => l.AccountId == "Tax").Debit);
        Assert.Equal(1m, lines.Single(l => l.AccountId == "Fees").Debit);
        Assert.DoesNotContain(lines, l => l.AccountId == "Income");
    }

    [Fact]
    public void SameDayAccrualAndClearingNetBalancesWithoutDuplicatingIncome()
    {
        var lines = BondCouponRecognitionCalculator.Lines("USD", Accounts, -1m, -1m,
            [new("allocation", "observed-payment", -1m, -1m)]);
        Assert.DoesNotContain(lines, l => l.AccountId is "Receivable" or "Payable");
        Assert.Equal(1m, lines.Single(l => l.AccountId == "Income").Debit);
        Assert.Equal(1m, lines.Single(l => l.AccountId == "Cash").Credit);
    }

    [Theory]
    [InlineData(-.5, .5, .5, 0)]
    [InlineData(-1.5, -.5, 1, .5)]
    public void CorrectionsReduceExistingReceivableBeforeCreatingPayable(double delta, double outstanding,
        double receivableCredit, double payableCredit)
    {
        var lines = BondCouponRecognitionCalculator.Lines("USD", Accounts, (decimal)delta, (decimal)outstanding, []);
        Assert.Equal((decimal)receivableCredit, lines.Single(l => l.AccountId == "Receivable").Credit);
        Assert.Equal((decimal)payableCredit, lines.Where(l => l.AccountId == "Payable").Sum(l => l.Credit));
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    [Fact]
    public void OvercollectionAndInventedNegativeFeesRefuse()
    {
        Assert.Throws<ArgumentException>(() => BondCouponRecognitionCalculator.Lines("USD", Accounts, 0m, 100m,
            [new("allocation", "receipt", 101m, 101m)]));
        Assert.Throws<ArgumentException>(() => BondCouponRecognitionCalculator.Lines("USD", Accounts, 0m, -100m,
            [new("allocation", "payment", -95m, -100m, Fees: -5m)]));
    }

    [Fact]
    public void NewNegativePeriodAndItsPartialPaymentNeverSetOffPriorPositiveReceivable()
    {
        var lines = BondCouponRecognitionCalculator.LinesBySide("USD", Accounts, 0m, 5m, 10m, 5m,
            [new("outgoing", "bank", -2m, -2m)]);
        Assert.DoesNotContain(lines, l => l.AccountId == "Receivable");
        Assert.Equal(3m, lines.Single(l => l.AccountId == "Payable").Credit);
        Assert.Equal(5m, lines.Single(l => l.AccountId == "Income").Debit);
        Assert.Equal(2m, lines.Single(l => l.AccountId == "Cash").Credit);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    [Fact]
    public void PositiveAndNegativeCollectionsClearOnlyTheirOwnSide()
    {
        var lines = BondCouponRecognitionCalculator.LinesBySide("USD", Accounts, 0m, 0m, 10m, 5m,
            [new("incoming", "bank-in", 4m, 4m), new("outgoing", "bank-out", -3m, -3m)]);
        Assert.Equal(4m, lines.Single(l => l.AccountId == "Receivable").Credit);
        Assert.Equal(3m, lines.Single(l => l.AccountId == "Payable").Debit);
        Assert.DoesNotContain(lines, l => l.AccountId == "Income");
        Assert.Throws<ArgumentException>(() => BondCouponRecognitionCalculator.LinesBySide("USD", Accounts, 0m, 0m, 10m, 5m,
            [new("overpaid", "bank", -6m, -6m)]));
    }

    [Fact]
    public void SharedOutgoingSourceRetainsEveryDeductionBudgetAndRejectsDirectionCancellation()
    {
        var source = new Meridian.Contracts.AssetOperations.AccountCashPaymentAllocationDto("shared", "obligation", "bank",
            -105m, -100m, Fees: 5m);
        var share = new BondCouponCashAllocationDto("shared", "bank", -42m, -40m, Fees: 2m);
        Assert.True(BondCouponRecognitionCalculator.IsAvailableShare(share, source, new(-63m, -60m, 0m, 3m)));
        Assert.False(BondCouponRecognitionCalculator.IsAvailableShare(share, source, new(-64m, -61m, 0m, 3m)));
        Assert.False(BondCouponRecognitionCalculator.IsAvailableShare(share, source, new(-61m, -60m, 0m, 4m)));
        Assert.False(BondCouponRecognitionCalculator.IsAvailableShare(share, source, new(42m, 40m, 0m, 2m)));
    }
}
