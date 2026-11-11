using Meridian.Contracts.SecurityMaster;

namespace Meridian.Backtesting;

/// <summary>Loads authoritative, economically effective corporate actions for portfolio replay.</summary>
public interface IBacktestAssetEventSource
{
    Task<IReadOnlyList<AssetEvent>> LoadAsync(
        IReadOnlyCollection<string> symbols, DateOnly from, DateOnly through, CancellationToken ct = default);
}

/// <summary>
/// Bridges the governed Security Master ingest pipeline to simulation. Provider proposals and
/// announcements cannot grant accounting authority; only confirmed effective actions are consumed.
/// This is an economic cutoff, not a reconstruction of historical transaction visibility.
/// </summary>
public sealed class SecurityMasterBacktestAssetEventSource(
    ISecurityIdentityQueryService identityQuery,
    ISecurityCorporateActionsQueryService actionQuery,
    ISecurityMasterQueryService? securityQuery = null) : IBacktestAssetEventSource
{
    public async Task<IReadOnlyList<AssetEvent>> LoadAsync(
        IReadOnlyCollection<string> symbols, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        if (through < from)
            throw new ArgumentException("Corporate-action cutoff must be on or after the start date.");

        var cutoff = new DateTimeOffset(through.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var result = new List<AssetEvent>();
        var processed = new HashSet<Guid>();
        var pending = new Queue<(SecurityDetailDto Security, string Symbol, DateOnly ActiveFrom)>();
        foreach (var symbol in symbols.Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
                     .Select(static symbol => symbol.Trim().ToUpperInvariant())
                     .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var security = await identityQuery.GetByIdentifierAsync(
                SecurityIdentifierKind.Ticker, symbol, null, ct,
                new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)).ConfigureAwait(false);
            if (security is not null)
                pending.Enqueue((security, symbol, from));
        }

        while (pending.TryDequeue(out var item))
        {
            var (security, symbol, activeFrom) = item;
            if (!processed.Add(security.SecurityId))
                continue;
            var currentSymbol = symbol;
            var observed = await actionQuery.GetCorporateActionsAsync(security.SecurityId, ct).ConfigureAwait(false);
            var actions = CorporateActionEffectiveStateProjector.Project(observed, cutoff)
                .Where(static state => !state.IsCancelled &&
                    state.Effective.LifecycleState is null or CorporateActionLifecycleStates.Confirmed)
                .Select(static state => state.Effective)
                .Where(action => action.ExDate >= activeFrom && action.ExDate <= through)
                .DistinctBy(CorporateActionEconomicFingerprint.Compute)
                .OrderBy(static action => action.ExDate)
                .ThenBy(static action => IsTransformation(action.EventType) ? 0 : 1)
                .ThenBy(static action => action.CorpActId).ToArray();

            if (actions.GroupBy(static action => (action.ExDate, action.EventType)).Any(static group => group.Count() > 1))
                throw new InvalidOperationException($"Conflicting corporate-action economics for {symbol}; distinct same-date/type instructions require review before replay.");
            if (actions.Where(static action => IsTransformation(action.EventType))
                .GroupBy(static action => action.ExDate).Any(static group => group.Count() > 1))
                throw new InvalidOperationException($"Multiple same-day corporate-action transformations for {symbol} require reviewed ordering.");

            foreach (var action in actions)
            {
                if (action.SecurityId != security.SecurityId)
                    throw new InvalidOperationException($"Corporate action {action.CorpActId} belongs to another security.");
                if (activeFrom > from && action.ExDate == activeFrom)
                    throw InvalidTerms(action, "same-day successor actions require an explicit reviewed event ordering");
                var mapped = await MapAsync(action, currentSymbol, ct).ConfigureAwait(false);
                if (mapped is null)
                    continue;
                result.Add(mapped);
                if (mapped.HasPositionTransformation && mapped.TargetSymbol is not null)
                {
                    currentSymbol = mapped.DestinationSymbol;
                    var successorId = action.AcquirerSecurityId ?? action.NewSecurityId ?? security.SecurityId;
                    if (successorId != security.SecurityId)
                    {
                        var successor = await securityQuery!.GetByIdAsOfAsync(successorId, mapped.EffectiveAt, ct).ConfigureAwait(false)
                            ?? throw InvalidTerms(action, "successor details are unavailable");
                        pending.Enqueue((successor, currentSymbol, action.ExDate));
                        break; // The source security has been absorbed; subsequent events belong to its successor.
                    }
                }
            }
        }

        return Merge(result, []);
    }

    /// <summary>Deduplicates identical economics and refuses ambiguous stored/manual instructions.</summary>
    public static IReadOnlyList<AssetEvent> Merge(
        IReadOnlyList<AssetEvent> stored, IReadOnlyList<AssetEvent> supplied)
    {
        var result = new List<AssetEvent>();
        foreach (var group in stored.Concat(supplied).GroupBy(static item =>
                     (Symbol: item.Symbol.Trim().ToUpperInvariant(),
                         Date: DateOnly.FromDateTime(item.EffectiveAt.UtcDateTime), item.EventType)))
        {
            var events = group.ToArray();
            var first = events[0];
            if (events.Any(item => item.CashPerShare != first.CashPerShare ||
                    item.PositionFactor != first.PositionFactor || item.ReferencePrice != first.ReferencePrice ||
                    !string.Equals(item.DestinationSymbol, first.DestinationSymbol, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    $"Conflicting corporate-action economics for {group.Key.Symbol} on {group.Key.Date:yyyy-MM-dd} ({group.Key.EventType}); resolve the source disagreement before replay.");
            result.Add(first with { Symbol = group.Key.Symbol });
        }
        return result.OrderBy(static item => item.EffectiveAt)
            .ThenBy(static item => item.HasPositionTransformation ? 0 : 1).ToArray();
    }

    private async Task<AssetEvent?> MapAsync(CorporateActionDto action, string symbol, CancellationToken ct)
    {
        var type = CorporateActionEventTypes.Normalize(action.EventType);
        var at = new DateTimeOffset(action.ExDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var description = $"Security Master {action.CorpActId:D}: {type} (ex-date entitlement)";
        switch (type)
        {
            case CorporateActionEventTypes.Dividend:
            case CorporateActionEventTypes.SpecialDividend:
            case CorporateActionEventTypes.ReturnOfCapital:
                var cash = type == CorporateActionEventTypes.ReturnOfCapital
                    ? CorporateActionPayloads.ReadDecimal(action.Payload, CorporateActionPayloads.CapitalReturnPerShare)
                        ?? action.DividendPerShare
                    : action.DividendPerShare;
                if (cash is not >= 0m)
                    throw InvalidTerms(action, "nonnegative per-unit distribution is required");
                // The simulation's account cash is USD; silently treating FX cash as USD would
                // fabricate both return and cash reconciliation. Currency conversion is explicit.
                if (!string.Equals(action.Currency, "USD", StringComparison.OrdinalIgnoreCase))
                    throw InvalidTerms(action, "USD distribution currency is required by this simulation");
                return new AssetEvent(at, symbol,
                    type == CorporateActionEventTypes.ReturnOfCapital ? AssetEventType.ReturnOfCapital : AssetEventType.Dividend,
                    CashPerShare: cash.Value, Description: description);
            case CorporateActionEventTypes.StockSplit:
            case CorporateActionEventTypes.ReverseStockSplit:
                if (action.SplitRatio is not > 0m)
                    throw InvalidTerms(action, "positive split ratio is required");
                return new AssetEvent(at, symbol,
                    type == CorporateActionEventTypes.StockSplit ? AssetEventType.Split : AssetEventType.ReverseSplit,
                    PositionFactor: action.SplitRatio.Value, Description: description);
            case CorporateActionEventTypes.MergerAbsorption:
            case CorporateActionEventTypes.SymbolChange:
                var targetId = action.AcquirerSecurityId ?? action.NewSecurityId ??
                    (type == CorporateActionEventTypes.SymbolChange ? action.SecurityId : (Guid?)null);
                if (targetId is null || securityQuery is null)
                    throw InvalidTerms(action, "a resolvable successor security is required");
                var target = await securityQuery.GetByIdAsOfAsync(targetId.Value, at, ct).ConfigureAwait(false);
                var targetSymbol = target?.Identifiers.FirstOrDefault(id => id.Kind == SecurityIdentifierKind.Ticker &&
                    id.ValidFrom <= at && (id.ValidTo is null || id.ValidTo > at))?.Value;
                if (string.IsNullOrWhiteSpace(targetSymbol))
                    throw InvalidTerms(action, "the successor has no ticker at the effective date");
                var ratio = type == CorporateActionEventTypes.SymbolChange ? 1m : action.ExchangeRatio;
                if (ratio is not > 0m)
                    throw InvalidTerms(action, "positive exchange ratio is required");
                var consideration = CorporateActionPayloads.ReadDecimal(action.Payload, CorporateActionPayloads.CashConsiderationPerUnit) ?? 0m;
                if (consideration != 0m && !string.Equals(action.Currency, "USD", StringComparison.OrdinalIgnoreCase))
                    throw InvalidTerms(action, "USD cash-consideration currency is required by this simulation");
                return new AssetEvent(at, symbol,
                    type == CorporateActionEventTypes.SymbolChange ? AssetEventType.SymbolChange : AssetEventType.Merger,
                    CashPerShare: consideration, PositionFactor: ratio.Value,
                    TargetSymbol: targetSymbol.Trim().ToUpperInvariant(), Description: description);
            case CorporateActionEventTypes.NameChange:
                return null; // Display metadata has no simulation accounting effect.
            default:
                throw InvalidTerms(action, $"automatic {type} simulation accounting is not supported; supply reviewed AssetEvents and disable ApplyStoredCorporateActions");
        }
    }

    private static InvalidOperationException InvalidTerms(CorporateActionDto action, string reason) =>
        new($"Corporate action {action.CorpActId:D}: {reason}.");

    private static bool IsTransformation(string eventType) => eventType is
        CorporateActionEventTypes.StockSplit or CorporateActionEventTypes.ReverseStockSplit or
        CorporateActionEventTypes.MergerAbsorption or CorporateActionEventTypes.SymbolChange;
}
