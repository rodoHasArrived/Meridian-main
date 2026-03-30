using Meridian.Contracts.Workstation;
using Meridian.FSharp.CashFlowInterop;

namespace Meridian.Strategies.Services;

/// <summary>
/// Projects cash flows for a strategy run by mapping its recorded
/// <see cref="CashFlowEntry"/> items through the F# <c>CashLadder.build</c> module,
/// then returning a time-bucketed <see cref="RunCashFlowSummary"/>.
/// </summary>
public sealed class CashFlowProjectionService
{
    private const string DefaultCurrency = "USD";
    private const int DefaultBucketDays = 7;

    private readonly IStrategyRepository _repository;

    public CashFlowProjectionService(IStrategyRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// Returns a cash-flow projection for the run identified by <paramref name="runId"/>,
    /// or <c>null</c> when no run with that id exists.
    /// </summary>
    /// <param name="runId">The strategy run identifier.</param>
    /// <param name="asOf">
    /// The projection reference date. Only cash flows on or after this date appear in the
    /// bucketed ladder. Defaults to the run's <c>StartedAt</c> timestamp when <c>null</c>.
    /// </param>
    /// <param name="currency">Currency filter applied to the ladder. Defaults to <c>"USD"</c>.</param>
    /// <param name="bucketDays">Width of each time bucket in days. Defaults to 7.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<RunCashFlowSummary?> GetAsync(
        string runId,
        DateTimeOffset? asOf = null,
        string? currency = null,
        int? bucketDays = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await foreach (var run in _repository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (!string.Equals(run.RunId, runId, StringComparison.Ordinal))
            {
                continue;
            }

            var effectiveAsOf = asOf ?? run.StartedAt;
            var effectiveCcy = string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency!;
            var effectiveBuckets = Math.Max(1, bucketDays ?? DefaultBucketDays);

            var cashFlows = run.Metrics?.CashFlows ?? [];
            var entries = BuildEntries(cashFlows, effectiveCcy);
            var inputs = BuildProjectionInputs(cashFlows, effectiveCcy);
            var ladder = CashFlowProjector.BuildLadder(effectiveAsOf, effectiveCcy, effectiveBuckets, inputs);

            var totalInflows = entries.Sum(static e => e.Amount > 0m ? e.Amount : 0m);
            var totalOutflows = entries.Sum(static e => e.Amount < 0m ? -e.Amount : 0m);

            return new RunCashFlowSummary(
                RunId: run.RunId,
                AsOf: effectiveAsOf,
                Currency: effectiveCcy,
                TotalEntries: entries.Length,
                TotalInflows: totalInflows,
                TotalOutflows: totalOutflows,
                NetCashFlow: totalInflows - totalOutflows,
                Entries: entries,
                Ladder: MapLadder(ladder, effectiveCcy, effectiveBuckets));
        }

        return null;
    }

    private static CashFlowEntryDto[] BuildEntries(
        IReadOnlyList<CashFlowEntry> cashFlows,
        string currency)
    {
        return cashFlows
            .OrderBy(static f => f.Timestamp)
            .Select(f => new CashFlowEntryDto(
                Timestamp: f.Timestamp,
                Amount: f.Amount,
                EventKind: GetEventKindLabel(f),
                Symbol: GetSymbol(f),
                Currency: currency,
                AccountId: f.AccountId,
                Description: GetDescription(f)))
            .ToArray();
    }

    private static CashFlowProjectionInput[] BuildProjectionInputs(
        IReadOnlyList<CashFlowEntry> cashFlows,
        string currency)
    {
        return cashFlows
            .Select(f => new CashFlowProjectionInput
            {
                FlowId = Guid.NewGuid(),
                SecurityGuid = GetSecurityGuid(f),
                EventKindLabel = GetEventKindLabel(f),
                ExpectedAmount = f.Amount,
                ExpectedCurrency = currency,
                DueDate = f.Timestamp,
                IsPrincipalFlow = IsPrincipalFlow(f),
                IsIncomeFlow = IsIncomeFlow(f),
                Notes = GetDescription(f) ?? string.Empty
            })
            .ToArray();
    }

    private static RunCashLadder MapLadder(CashLadderInterop ladder, string currency, int bucketDays)
    {
        var buckets = ladder.Buckets
            .Select(static b => new CashLadderBucketDto(
                BucketStart: b.BucketStart,
                BucketEnd: b.BucketEnd,
                ProjectedInflows: b.ProjectedInflows,
                ProjectedOutflows: b.ProjectedOutflows,
                NetFlow: b.NetFlow,
                Currency: b.Currency,
                EventCount: b.EventCount))
            .ToArray();

        return new RunCashLadder(
            AsOf: ladder.AsOf,
            Currency: currency,
            BucketDays: bucketDays,
            TotalProjectedInflows: ladder.TotalProjectedInflows,
            TotalProjectedOutflows: ladder.TotalProjectedOutflows,
            NetPosition: ladder.NetPosition,
            Buckets: buckets);
    }

    private static string GetEventKindLabel(CashFlowEntry entry) => entry switch
    {
        TradeCashFlow => "Trade",
        CommissionCashFlow => "Commission",
        DividendCashFlow => "Dividend",
        MarginInterestCashFlow => "MarginInterest",
        ShortRebateCashFlow => "ShortRebate",
        CashInterestCashFlow => "CashInterest",
        AssetEventCashFlow aec => MapAssetEventKindLabel(aec.EventType),
        _ => "Other"
    };

    private static string MapAssetEventKindLabel(AssetEventType eventType) => eventType switch
    {
        AssetEventType.Dividend => "Dividend",
        AssetEventType.Coupon => "Coupon",
        AssetEventType.Payment => "Proceeds",
        AssetEventType.CashDistribution => "Proceeds",
        AssetEventType.ReturnOfCapital => "Principal",
        AssetEventType.Fee => "Fee",
        _ => "Other"
    };

    private static string? GetSymbol(CashFlowEntry entry) => entry switch
    {
        TradeCashFlow t => t.Symbol,
        CommissionCashFlow c => c.Symbol,
        DividendCashFlow d => d.Symbol,
        ShortRebateCashFlow s => s.Symbol,
        AssetEventCashFlow a => a.Symbol,
        _ => null
    };

    private static string? GetDescription(CashFlowEntry entry) => entry switch
    {
        TradeCashFlow t => $"Trade {t.Symbol} qty={t.Quantity} @ {t.Price}",
        CommissionCashFlow c => $"Commission {c.Symbol}",
        DividendCashFlow d => $"Dividend {d.Symbol} {d.DividendPerShare:F4}/share",
        MarginInterestCashFlow m => $"Margin interest {m.AnnualRate:P2} on {m.MarginBalance:C}",
        ShortRebateCashFlow s => $"Short rebate {s.Symbol}",
        CashInterestCashFlow ci => $"Cash interest {ci.AnnualRate:P2}",
        AssetEventCashFlow a => a.Description ?? $"{a.EventType} {a.Symbol}",
        _ => null
    };

    private static Guid GetSecurityGuid(CashFlowEntry entry)
    {
        // Deterministically derive a Guid from the symbol string so that
        // all flows for the same symbol map to the same SecurityGuid in the ladder.
        var symbol = GetSymbol(entry);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return Guid.Empty;
        }

        return new Guid(System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(symbol.ToUpperInvariant())));
    }

    private static bool IsPrincipalFlow(CashFlowEntry entry) => entry switch
    {
        AssetEventCashFlow aec when
            aec.EventType is AssetEventType.ReturnOfCapital => true,
        TradeCashFlow => true,
        _ => false
    };

    private static bool IsIncomeFlow(CashFlowEntry entry) => entry switch
    {
        DividendCashFlow => true,
        CashInterestCashFlow => true,
        AssetEventCashFlow aec when
            aec.EventType is AssetEventType.Dividend
                          or AssetEventType.Coupon => true,
        _ => false
    };
}
