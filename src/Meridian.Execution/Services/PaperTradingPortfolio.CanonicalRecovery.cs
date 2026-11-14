using Meridian.Execution.Adapters;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;

namespace Meridian.Execution.Services;

public sealed record CanonicalPaperLotRecovery(string LotId, decimal Quantity, decimal UnitCost,
    decimal ExactBasis, DateTimeOffset AcquiredAtUtc, Guid? CanonicalTaxLotRecordId = null);
public sealed record CanonicalPaperPositionRecovery(string Symbol, IReadOnlyList<CanonicalPaperLotRecovery> Lots,
    decimal ObservedPrice, DateTimeOffset ObservedAtUtc, string ObservationSource);

public sealed partial class PaperTradingPortfolio
{
    private bool _canonicalAccountingRequired;
    internal bool UsesCanonicalAccounting => _canonicalAccountingRequired;
    private string? _canonicalPostingScope;
    private bool _canonicalAccountingInitialized;
    private LiveMarketDataCache? _canonicalMarkCache;
    private static readonly TimeSpan CanonicalMarkMaxAge = TimeSpan.FromMinutes(5);
    private Func<OrderRequest, CancellationToken, Task<string?>>? _canonicalAdmissionAuthority;

    internal void SetCanonicalAdmissionAuthority(Func<OrderRequest, CancellationToken, Task<string?>> authority)
        => _canonicalAdmissionAuthority = authority;

    internal Task<string?> ValidateCanonicalOrderAsync(OrderRequest request, CancellationToken ct)
        => !_canonicalAccountingRequired ? Task.FromResult<string?>(null)
            : _canonicalAdmissionAuthority?.Invoke(request, ct)
                ?? Task.FromResult<string?>("Canonical admission authority has not completed recovery.");

    internal bool MatchesCanonicalAuthority(decimal cash, IReadOnlyDictionary<string, (decimal Quantity, decimal Basis)> holdings)
    {
        lock (_lock)
        {
            var account = _accounts[_primaryAccountId];
            var positions = account.Positions.Values.Where(position => position.Quantity != 0m).ToArray();
            return account.Cash == cash && positions.Length == holdings.Count
                && positions.All(position => holdings.TryGetValue(position.Symbol, out var holding)
                    && holding.Quantity == position.Quantity && holding.Basis == position.OpenCostBasis);
        }
    }

    internal string? ValidateCanonicalFunding(OrderRequest request, decimal explicitCost)
    {
        lock (_lock)
        {
            var account = _accounts[_primaryAccountId];
            var quantity = account.Positions.TryGetValue(request.Symbol, out var position) ? position.Quantity : 0m;
            if (request.Side == OrderSide.Sell && request.Quantity > quantity)
                return "Canonical cash-equity sales cannot exceed the account's proven long holdings.";
            var gross = request.Quantity * request.LimitPrice!.Value;
            if (account.Cash + (request.Side == OrderSide.Sell ? gross : -gross) < explicitCost)
                return "Canonical paper execution requires proven cash covering the complete fill and configured explicit costs.";
            return null;
        }
    }

    internal void RequireCanonicalRecovery(LiveMarketDataCache? cache)
    {
        _canonicalAccountingRequired = true;
        _canonicalMarkCache = cache;
    }

    internal void RestoreCanonicalState(string accountId, decimal cash, decimal realizedPnl,
        IReadOnlyList<CanonicalPaperPositionRecovery> positions, string postingScope)
    {
        lock (_lock)
        {
            if (!_canonicalAccountingRequired || _canonicalAccountingInitialized || accountId != _primaryAccountId
                || _accounts.Count != 1 || !_accounts.TryGetValue(accountId, out var account)
                || account.Positions.Count != 0 || account.Cash != 0m || cash < 0m)
                throw new InvalidOperationException("Canonical paper recovery requires its exact untouched single cash account.");
            foreach (var restored in positions)
            {
                if (restored.ObservedPrice <= 0m || restored.ObservedAtUtc == default || restored.Lots.Count == 0
                    || restored.Lots.Any(lot => lot.Quantity <= 0m || lot.ExactBasis <= 0m))
                    throw new InvalidOperationException("Canonical recovery requires positive long lots, exact basis and retained price observations.");
                var position = new PaperPosition(restored.Symbol, restored.ObservedPrice)
                {
                    MarkObservedAtUtc = restored.ObservedAtUtc,
                    MarkSource = restored.ObservationSource,
                    LotUnit = SecurityLotUnitKind.Shares,
                    UsesCanonicalLotOrdering = true
                };
                foreach (var lot in restored.Lots)
                {
                    position.RestoreLot(lot.LotId, lot.Quantity, lot.UnitCost, lot.ExactBasis, lot.AcquiredAtUtc,
                        lot.CanonicalTaxLotRecordId ?? throw new InvalidOperationException("Canonical recovery requires exact durable lot identities."));
                    position.AttributeFill(accountId, lot.Quantity, 1m);
                }
                account.Positions.Add(restored.Symbol, position);
            }
            account.Cash = cash;
            account.RealisedPnl = realizedPnl;
            _canonicalPostingScope = postingScope;
            _canonicalAccountingInitialized = true;
        }
    }

    internal bool CanAdmitCanonicalOrders(DateTimeOffset now, out string reason)
    {
        lock (_lock)
        {
            reason = "";
            if (!_canonicalAccountingRequired)
                return true;
            if (!_canonicalAccountingInitialized)
            { reason = "Canonical accounting cash and holdings recovery has not completed."; return false; }
            foreach (var position in _accounts[_primaryAccountId].Positions.Values.Where(position => position.Quantity != 0m))
            {
                if (LatestObservation(_canonicalMarkCache, position.Symbol, now) is { } observed
                    && (position.MarkObservedAtUtc is null || observed.At >= position.MarkObservedAtUtc))
                {
                    position.MarketPrice = observed.Price;
                    position.MarkObservedAtUtc = observed.At;
                    position.MarkSource = "MarketDataCache";
                }
                if (position.MarkObservedAtUtc is not { } at || at > now || now - at > CanonicalMarkMaxAge)
                { reason = $"A fresh retained market observation is required for recovered holding '{position.Symbol}'."; return false; }
            }
            return true;
        }
    }

    internal static (decimal Price, DateTimeOffset At)? LatestObservation(LiveMarketDataCache? cache, string symbol, DateTimeOffset now)
    {
        if (cache is null)
            return null;
        var snapshot = cache.GetSnapshot(symbol);
        var observations = new List<(decimal Price, DateTimeOffset At)>();
        if (snapshot.Trade is { Price: > 0m } trade)
            observations.Add((trade.Price, trade.Timestamp));
        if (snapshot.Quote is { } quote && quote.BidPrice > 0m && quote.AskPrice >= quote.BidPrice)
            observations.Add(((quote.BidPrice + quote.AskPrice) / 2m, quote.Timestamp));
        return observations.Where(observation => observation.At <= now).OrderByDescending(observation => observation.At)
            .Select(observation => ((decimal Price, DateTimeOffset At)?)observation).FirstOrDefault();
    }
}
