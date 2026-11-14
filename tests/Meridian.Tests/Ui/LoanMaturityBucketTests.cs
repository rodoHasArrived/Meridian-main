using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.Tests.Ui;

public sealed class LoanMaturityBucketTests
{
    [Fact]
    public void BucketsRetainExactDecimalSumsQuarterBoundariesAndSeparateCurrencies()
    {
        var first = Loan(CurrencyCode.USD, new(2028, 3, 31), 0.1m);
        var second = Loan(CurrencyCode.USD, new(2028, 2, 29), 0.2m);
        var april = Loan(CurrencyCode.USD, new(2028, 4, 1), 5m);
        var euro = Loan(CurrencyCode.EUR, new(2028, 3, 31), 999m);
        var result = LoanPortfolioSummaryBuilder.Build([first, second, april, euro], []);

        result.MaturityBuckets.Should().HaveCount(3);
        var quarter = result.MaturityBuckets!.Single(item => item.Currency == CurrencyCode.USD && item.Quarter == 1);
        quarter.Year.Should().Be(2028);
        quarter.PrincipalOutstanding.Should().Be("0.3");
        quarter.LoanIds.Should().BeEquivalentTo([first.LoanId, second.LoanId]);
        result.MaturityBuckets!.Single(item => item.Currency == CurrencyCode.USD && item.Quarter == 2)
            .PrincipalOutstanding.Should().Be("5");
        result.TotalPrincipalOutstanding.Should().BeNull();
    }

    [Fact]
    public void UnknownDenominationAndMissingMaturityRemainExplicit()
    {
        var result = LoanPortfolioSummaryBuilder.Build([Loan(CurrencyCode.Other, default, 500m)], []);
        var bucket = result.MaturityBuckets.Should().ContainSingle().Which;
        bucket.PrincipalOutstanding.Should().BeNull();
        bucket.Year.Should().BeNull();
        bucket.Quarter.Should().BeNull();
        bucket.LoanIds.Should().ContainSingle();
    }

    private static LoanSummaryDto Loan(CurrencyCode currency, DateOnly maturity, decimal principal) =>
        new(Guid.NewGuid(), "Synthetic facility", Guid.NewGuid(), "Synthetic borrower", LoanStatus.Active,
            currency, 1000m, principal, 0m, 0m, 1000m - principal, new(2026, 1, 1), maturity, null, null);
}
