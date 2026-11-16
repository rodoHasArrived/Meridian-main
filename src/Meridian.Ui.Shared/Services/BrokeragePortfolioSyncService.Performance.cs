using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

// Brokerage performance read: the existing cash-adjusted figure plus W10-PERF-001 return figures
// from the shared kernel. Split from BrokeragePortfolioSyncService.cs to keep that file under the
// no-new-god-file cap.
public sealed partial class BrokeragePortfolioSyncService
{
    public async Task<BrokeragePortfolioPerformanceDto> GetPerformanceAsync(
        Guid fundAccountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var projection = await LoadProjectionAsync(fundAccountId, ct).ConfigureAwait(false);
        var timeline = await GetBalanceHistoryAsync(fundAccountId, from, to, ct).ConfigureAwait(false);
        var cashEntries = FilterCashTransactions(projection, from, to)
            .Select(ToCashFlowEntry)
            .ToArray();
        var cashByDate = cashEntries
            .GroupBy(static entry => DateOnly.FromDateTime(entry.PostedAt.UtcDateTime))
            .ToDictionary(static group => group.Key, static group => group.Sum(entry => entry.Amount));
        static decimal Equity(AccountBalanceSnapshotDto snapshot)
            => snapshot.CashBalance + (snapshot.SecuritiesMarketValue ?? 0m)
                + (HasDisjointBalanceComponents(snapshot.Source) ? snapshot.AccruedInterest ?? 0m : 0m);

        var points = timeline
            .OrderBy(static snapshot => snapshot.AsOfDate)
            .Select(snapshot => new BrokeragePortfolioPerformancePointDto(
                snapshot.AsOfDate,
                Equity(snapshot),
                snapshot.CashBalance,
                cashByDate.GetValueOrDefault(snapshot.AsOfDate)))
            .ToArray();

        var warnings = new List<string>();
        if (projection is null && points.Length == 0)
        {
            warnings.Add("No brokerage sync projection exists for this fund account.");
        }

        if (points.Length < 2)
        {
            warnings.Add("At least two balance snapshots are required for cash-adjusted performance.");
        }

        var beginningEquity = points.Length >= 2 ? points[0].Equity : (decimal?)null;
        var endingEquity = points.Length >= 2 ? points[^1].Equity : (decimal?)null;
        var netCashFlow = cashEntries.Sum(static entry => entry.Amount);
        var cashAdjustedReturn = beginningEquity.HasValue && endingEquity.HasValue
            ? endingEquity.Value - beginningEquity.Value - netCashFlow
            : (decimal?)null;
        var cashAdjustedReturnPercent = cashAdjustedReturn.HasValue && beginningEquity.GetValueOrDefault() != 0m
            ? cashAdjustedReturn.Value / beginningEquity!.Value
            : (decimal?)null;

        return new BrokeragePortfolioPerformanceDto(
            FundAccountId: fundAccountId,
            ProviderId: projection?.Link.ProviderId,
            ExternalAccountId: projection?.Link.ExternalAccountId,
            AccountKind: projection?.Link.AccountKind ?? BrokerageAccountKindDto.Unknown,
            From: from,
            To: to,
            HasSufficientHistory: points.Length >= 2,
            BeginningEquity: beginningEquity,
            EndingEquity: endingEquity,
            NetCashFlow: netCashFlow,
            CashAdjustedReturn: cashAdjustedReturn,
            CashAdjustedReturnPercent: cashAdjustedReturnPercent,
            Points: points,
            Warnings: warnings,
            ReturnFigures: PerformanceReturnFigures.MeasureBrokerage(
                timeline.Select(static snapshot => (snapshot.AsOfDate, Equity(snapshot), snapshot.Currency)),
                cashEntries,
                projection?.Balance?.Currency
                    ?? timeline.Select(static snapshot => snapshot.Currency).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
                    ?? "USD"));
    }
}
