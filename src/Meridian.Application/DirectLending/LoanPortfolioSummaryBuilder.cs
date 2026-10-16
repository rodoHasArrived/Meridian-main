using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

/// <summary>Aggregates retained amounts without adding unlike currencies or implying FX conversion.</summary>
internal static class LoanPortfolioSummaryBuilder
{
    public static LoanPortfolioSummaryDto Build(
        IReadOnlyList<LoanSummaryDto> loans,
        IReadOnlyList<CollateralDto> collateral)
    {
        var currencyTotals = loans.Select(loan => loan.BaseCurrency)
            .Concat(collateral.Select(item => item.Currency))
            .Distinct()
            .OrderBy(currency => currency)
            .Select(currency =>
            {
                if (!HasKnownDenomination(currency))
                {
                    return new LoanPortfolioCurrencyTotalsDto(currency, null, null, null, null, null, null);
                }
                var facilities = loans.Where(loan => loan.BaseCurrency == currency).ToArray();
                return new LoanPortfolioCurrencyTotalsDto(
                    currency,
                    facilities.Sum(loan => loan.CommitmentAmount),
                    facilities.Sum(loan => loan.PrincipalOutstanding),
                    facilities.Sum(loan => loan.InterestAccruedUnpaid),
                    facilities.Sum(loan => loan.PenaltyAccruedUnpaid),
                    facilities.Sum(loan => loan.AvailableToDraw),
                    collateral.Where(item => item.Currency == currency).Sum(item => item.EstimatedValue));
            }).ToArray();

        // Retain scalar compatibility for a single denomination. Mixed sums have no
        // financial meaning, so represent them as unavailable rather than zero.
        var single = currencyTotals.Length == 1 && HasKnownDenomination(currencyTotals[0].Currency)
            ? currencyTotals[0] : null;
        var empty = currencyTotals.Length == 0;
        return new LoanPortfolioSummaryDto(
            loans.Count,
            loans.Count(loan => loan.Status == LoanStatus.Active),
            loans.Count(loan => loan.Status == LoanStatus.Defaulted),
            loans.Count(loan => loan.Status == LoanStatus.NonPerforming),
            loans.Count(loan => loan.Status == LoanStatus.Workout),
            empty ? 0m : single?.TotalCommitment,
            empty ? 0m : single?.TotalPrincipalOutstanding,
            empty ? 0m : single?.TotalInterestAccruedUnpaid,
            empty ? 0m : single?.TotalPenaltyAccruedUnpaid,
            empty ? 0m : single?.TotalAvailableToDraw,
            empty ? 0m : single?.TotalCollateralValue,
            loans,
            currencyTotals,
            single?.Currency);
    }

    private static bool HasKnownDenomination(CurrencyCode currency) =>
        currency is CurrencyCode.USD or CurrencyCode.EUR or CurrencyCode.GBP or CurrencyCode.JPY;
}
