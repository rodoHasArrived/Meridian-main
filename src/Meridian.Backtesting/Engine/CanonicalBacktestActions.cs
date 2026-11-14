using System.Collections.ObjectModel;
using System.Text.Json;
using Meridian.Backtesting.Portfolio;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Backtesting.Engine;

/// <summary>Run-owned effective terms; no provider calls or mutable queries occur during replay.</summary>
internal sealed class CanonicalBacktestActions(BacktestCanonicalActionSnapshot snapshot)
{
    public BacktestCanonicalActionSnapshot Snapshot { get; } = snapshot;
    // Keep every effective canonical source ID in evidence, but execute agreeing economics once.
    private readonly IReadOnlyList<BacktestCanonicalAction> _instructions = snapshot.Actions
        .DistinctBy(static item => CorporateActionEconomicFingerprint.Compute(item.Action)).ToArray();

    public static async Task<CanonicalBacktestActions> PrepareAsync(
        BacktestRequest request,
        IReadOnlySet<string> universe,
        ISecurityIdentityQueryService? identities,
        ISecurityCorporateActionsQueryService? actions,
        CancellationToken ct)
    {
        if (identities is null || actions is null)
            throw new InvalidOperationException("RawPricesWithCanonicalEvents requires Security Master identity and corporate-action query services.");
        if (request.AssetEvents?.Any(static item => item.EventType != AssetEventType.Fee) == true)
            throw new InvalidOperationException("RawPricesWithCanonicalEvents accepts only manual Fee AssetEvents; canonical dividends and splits cannot be mixed with unidentified manual corporate actions.");
        if (request.ResolveAccounts().Any(static account => account.InitialCash < 0m))
            throw new NotSupportedException("Canonical raw-price replay supports cash accounts with nonnegative opening cash only.");

        var through = new DateTimeOffset(request.To, TimeOnly.MaxValue, TimeSpan.Zero);
        var retained = new List<BacktestCanonicalAction>();
        var securities = new HashSet<Guid>();
        var effectiveIds = new HashSet<Guid>();
        foreach (var symbol in universe.Order(StringComparer.OrdinalIgnoreCase))
        {
            var security = await identities.GetByIdentifierAsync(
                SecurityIdentifierKind.Ticker, symbol, null, ct, through).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Canonical replay requires a mastered security for '{symbol}'.");
            if (security.SecurityId == Guid.Empty || security.Version <= 0 || security.Status != SecurityStatusDto.Active ||
                !string.Equals(security.AssetClass, "Equity", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(security.Currency, "USD", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Canonical replay requires an active USD cash equity: '{symbol}'.");
            if (!securities.Add(security.SecurityId))
                throw new InvalidOperationException("Canonical replay cannot replay multiple aliases for the same security.");

            var queried = await actions.GetCorporateActionsAsync(security.SecurityId, ct).ConfigureAwait(false);
            var unique = new Dictionary<Guid, CorporateActionDto>();
            foreach (var action in queried)
            {
                if (action.CorpActId == Guid.Empty || action.SecurityId != security.SecurityId)
                    throw new InvalidOperationException($"Corporate-action identity is missing or foreign for '{symbol}'.");
                if (unique.TryGetValue(action.CorpActId, out var prior) &&
                    JsonSerializer.Serialize(prior) != JsonSerializer.Serialize(action))
                    throw new InvalidOperationException($"Conflicting corporate-action ID '{action.CorpActId}' for '{symbol}'.");
                // Own JsonElement payload memory as well as the containing immutable record.
                unique[action.CorpActId] = action with { Payload = action.Payload?.Clone() };
            }

            var effective = CorporateActionEffectiveStateProjector.Project(unique.Values.ToArray(), through)
                .Where(static state => !state.IsCancelled &&
                    state.Effective.LifecycleState is null or CorporateActionLifecycleStates.Confirmed)
                .Select(static state => state.Effective)
                .Where(action => action.ExDate >= request.From && action.ExDate <= request.To)
                .OrderBy(static action => action.ExDate).ThenBy(static action => action.CorpActId).ToArray();
            var instructions = effective.DistinctBy(CorporateActionEconomicFingerprint.Compute).ToArray();
            if (instructions.GroupBy(static action => (action.ExDate, action.EventType)).Any(static group => group.Count() > 1))
                throw new InvalidOperationException($"Conflicting corporate-action economics for '{symbol}'; same-date/type instructions require review before replay.");
            if (instructions.Where(IsSplit).GroupBy(static action => action.ExDate).Any(static group => group.Count() > 1))
                throw new InvalidOperationException($"Multiple same-day corporate-action transformations for '{symbol}' require reviewed ordering.");

            foreach (var action in effective)
            {
                if (action.EventType == CorporateActionEventTypes.Dividend)
                {
                    if (action.DividendPerShare is not > 0m || action.PayDate is null || action.PayDate < action.ExDate ||
                        !string.Equals(action.Currency, "USD", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Dividend '{action.CorpActId}' requires positive USD terms and a pay date on or after ex-date.");
                }
                else if (action.EventType is CorporateActionEventTypes.StockSplit or CorporateActionEventTypes.ReverseStockSplit)
                {
                    if (action.SplitRatio is not > 0m)
                        throw new InvalidOperationException($"Split '{action.CorpActId}' requires a positive conversion factor.");
                }
                else
                {
                    throw new NotSupportedException($"Canonical raw-price replay does not support '{action.EventType}' for '{symbol}'.");
                }
                if (!effectiveIds.Add(action.CorpActId))
                    throw new InvalidOperationException("A canonical corporate-action ID cannot belong to multiple replay securities.");
                retained.Add(new BacktestCanonicalAction(symbol.Trim().ToUpperInvariant(), security.Version, action));
            }
        }

        var ordered = retained.OrderBy(static item => item.Action.ExDate)
            .ThenBy(static item => item.Symbol, StringComparer.Ordinal)
            .ThenBy(static item => item.Action.CorpActId).ToArray();
        var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            Mode = BacktestCorporateActionMode.RawPricesWithCanonicalEvents,
            EffectiveThroughUtc = through,
            Actions = ordered
        }));
        return new CanonicalBacktestActions(new BacktestCanonicalActionSnapshot(
            through, hash, new ReadOnlyCollection<BacktestCanonicalAction>(ordered)));
    }

    public void Apply(DateOnly date, SimulatedPortfolio portfolio, IReadOnlyList<Order>? pendingOrders)
    {
        if (_instructions.Any(item => item.Action.ExDate == date && IsSplit(item.Action) &&
            pendingOrders?.Any(order => !order.IsComplete && order.Symbol.Equals(item.Symbol, StringComparison.OrdinalIgnoreCase)) == true))
            throw new NotSupportedException("Canonical replay cannot carry pending orders across a split; cancel them before ex-date.");
        // Same-day split terms are applied first; dividend terms are per post-split share.
        foreach (var item in _instructions.Where(item => item.Action.ExDate == date && IsSplit(item.Action)))
            portfolio.ApplyCanonicalSplit(item, Snapshot.ContentHash);
        foreach (var item in _instructions.Where(item => item.Action.ExDate == date && item.Action.EventType == CorporateActionEventTypes.Dividend))
            portfolio.DeclareCanonicalDividend(item, Snapshot.ContentHash);
        foreach (var item in _instructions.Where(item => item.Action.PayDate == date && item.Action.EventType == CorporateActionEventTypes.Dividend))
            portfolio.SettleCanonicalDividend(item, Snapshot.ContentHash);
    }

    private static bool IsSplit(CorporateActionDto action) =>
        action.EventType is CorporateActionEventTypes.StockSplit or CorporateActionEventTypes.ReverseStockSplit;
}
