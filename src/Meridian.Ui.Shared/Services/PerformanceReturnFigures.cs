using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Maps the shared F# return kernel (<see cref="ReturnMeasurement"/>) onto the workstation
/// contract, and supplies the brokerage seam's choice of which cash activity is external capital.
/// Fund, share-class, and investor lanes reuse <see cref="ToDto"/> with their own source label.
/// </summary>
public static class PerformanceReturnFigures
{
    /// <summary>Source label for figures measured from synced brokerage balance snapshots.</summary>
    public const string BrokerageSource = "brokerage-balance-snapshots";

    // Categories produced by BrokeragePortfolioSyncService.CategorizeCashTransaction. Dividends,
    // interest, fees, option premium, and trade settlement are part of the return, not capital.
    private static readonly HashSet<string> ExternalCategories =
        new(StringComparer.Ordinal) { "Deposit", "Withdrawal", "Transfer" };

    private const string UnclassifiedCategory = "Other";

    public static PerformanceReturnFigureDto ToDto(ReturnFigure figure, string source)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        return new PerformanceReturnFigureDto(
            Convention: figure.Convention.ToString(),
            Status: figure.Status.ToString(),
            Currency: figure.Currency,
            PeriodStart: figure.PeriodStart,
            PeriodEnd: figure.PeriodEnd,
            Value: figure.Value,
            AnnualizedValue: figure.AnnualizedValue,
            SubPeriodCount: figure.SubPeriodCount,
            CandidateRates: figure.CandidateRates,
            Source: source,
            Reasons: figure.Reasons);
    }

    /// <summary>
    /// Time-weighted and money-weighted figures for a brokerage account from its balance
    /// snapshots and categorized cash activity. Snapshot equity is end-of-day; deposits,
    /// withdrawals, and transfers are the only external flows. Any cash transaction the
    /// categorizer could not classify makes every figure review-required, because an
    /// unrecognized deposit would otherwise be reported as performance.
    /// </summary>
    public static IReadOnlyList<PerformanceReturnFigureDto> MeasureBrokerage(
        IEnumerable<(DateOnly AsOf, decimal Equity, string Currency)> valuations,
        IEnumerable<BrokerageCashFlowEntryDto> cashEntries,
        string currency)
    {
        ArgumentNullException.ThrowIfNull(valuations);
        ArgumentNullException.ThrowIfNull(cashEntries);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        var points = valuations
            .Select(static v => new ReturnValuationPoint(v.AsOf, v.Equity, v.Currency, ReturnInputBasis.Posted))
            .ToArray();
        var entries = cashEntries as IReadOnlyCollection<BrokerageCashFlowEntryDto> ?? cashEntries.ToArray();
        var flows = entries
            .Where(static e => ExternalCategories.Contains(e.Category))
            .Select(static e => new ReturnExternalFlow(
                DateOnly.FromDateTime(e.PostedAt.UtcDateTime), e.Amount, e.Currency, ReturnInputBasis.Posted))
            .ToArray();
        var unclassified = entries
            .Where(static e => string.Equals(e.Category, UnclassifiedCategory, StringComparison.Ordinal))
            .Select(static e => e.TransactionId)
            .ToArray();

        var figures = new[]
        {
            ReturnMeasurement.timeWeighted(currency, points, flows),
            ReturnMeasurement.moneyWeighted(currency, points, flows),
        };

        return figures
            .Select(figure => ApplyBrokerageCaveats(ToDto(figure, BrokerageSource), unclassified))
            .ToArray();
    }

    private static PerformanceReturnFigureDto ApplyBrokerageCaveats(
        PerformanceReturnFigureDto figure,
        IReadOnlyList<string> unclassifiedTransactionIds)
    {
        var reasons = new List<string>(figure.Reasons)
        {
            "Measured from brokerage balance snapshots, not from Meridian's posted ledger.",
        };

        if (unclassifiedTransactionIds.Count == 0)
        {
            return figure with { Reasons = reasons };
        }

        reasons.Add(
            $"Cash transaction(s) {string.Join(", ", unclassifiedTransactionIds)} could not be classified as external capital or income.");
        return figure with
        {
            Status = nameof(ReturnFigureStatus.ReviewRequired),
            Value = null,
            AnnualizedValue = null,
            Reasons = reasons,
        };
    }
}
