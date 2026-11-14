using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Backtesting;

/// <summary>
/// Supplies portfolio asset events (dividends, splits) for a backtest universe.
/// </summary>
public interface IBacktestAssetEventSource
{
    /// <summary>
    /// Returns the asset events effective within <paramref name="from"/>..<paramref name="to"/>
    /// (inclusive) for <paramref name="symbols"/>, ordered by effective time.
    /// </summary>
    Task<IReadOnlyList<AssetEvent>> GetAssetEventsAsync(
        IReadOnlyCollection<string> symbols,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default) => LoadAsync(symbols, from, to, ct);

    /// <summary>
    /// Compatibility entry point for sources implementing the original automatic-replay contract.
    /// The engine calls GetAssetEventsAsync, which forwards to an existing LoadAsync implementation.
    /// A source implementing neither entry point fails explicitly rather than recursing.
    /// </summary>
    Task<IReadOnlyList<AssetEvent>> LoadAsync(
        IReadOnlyCollection<string> symbols,
        DateOnly from,
        DateOnly through,
        CancellationToken ct = default)
        => throw new NotSupportedException("The corporate-action source must implement an asset-event loading entry point.");
}

/// <summary>
/// Builds backtest asset events from confirmed Security Master corporate actions, so a raw-price
/// run does not depend on a hand-maintained event list.
/// </summary>
/// <remarks>
/// Only cash distributions and share splits are mapped. They are the same actions
/// <see cref="CorporateActionAdjustmentService"/> folds into back-adjusted prices, which is why the
/// engine refuses to combine this source with price adjustment: doing both would count each
/// action twice. Mergers, spin-offs and other position transformations are not mapped because the
/// Security Master terms do not carry everything <see cref="AssetEvent"/> needs for them; they are
/// logged and skipped rather than approximated.
/// </remarks>
public sealed class SecurityMasterAssetEventSource : IBacktestAssetEventSource
{
    private readonly ISecurityCorporateActionsQueryService _queryService;
    private readonly ISecurityResolver _resolver;
    private readonly ILogger<SecurityMasterAssetEventSource> _logger;

    public SecurityMasterAssetEventSource(
        ISecurityCorporateActionsQueryService queryService,
        ISecurityResolver resolver,
        ILogger<SecurityMasterAssetEventSource> logger)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(logger);

        _queryService = queryService;
        _resolver = resolver;
        _logger = logger;
    }

    public Task<IReadOnlyList<AssetEvent>> LoadAsync(
        IReadOnlyCollection<string> symbols, DateOnly from, DateOnly to, CancellationToken ct = default)
        => GetAssetEventsAsync(symbols, from, to, ct);

    public async Task<IReadOnlyList<AssetEvent>> GetAssetEventsAsync(
        IReadOnlyCollection<string> symbols,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        var effectiveThroughUtc = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var events = new List<AssetEvent>();
        foreach (var symbol in symbols
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(static symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static symbol => symbol, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var securityId = await _resolver.ResolveAsync(
                new ResolveSecurityRequest(
                    IdentifierKind: SecurityIdentifierKind.Ticker,
                    IdentifierValue: symbol,
                    Provider: null,
                    AsOfUtc: effectiveThroughUtc),
                ct).ConfigureAwait(false);
            if (securityId is null)
            {
                _logger.LogWarning(
                    "No Security Master record for {Symbol}; no asset events were generated for it",
                    symbol);
                continue;
            }

            var actions = await _queryService.GetCorporateActionsAsync(securityId.Value, ct).ConfigureAwait(false);
            // Same authority rule as price adjustment: confirmed (or legacy, state-less) terms
            // only, with superseded and cancelled revisions folded away.
            foreach (var action in CorporateActionEffectiveStateProjector
                .Project(actions, effectiveThroughUtc)
                .Where(static state =>
                    !state.IsCancelled &&
                    state.Effective.LifecycleState is null or CorporateActionLifecycleStates.Confirmed)
                .Select(static state => state.Effective)
                .Where(action => action.ExDate >= from && action.ExDate <= to)
                .OrderBy(static action => action.ExDate)
                .ThenBy(static action => action.CorpActId))
            {
                if (TryMap(symbol, action) is { } assetEvent)
                {
                    events.Add(assetEvent);
                }
                else
                {
                    _logger.LogInformation(
                        "Corporate action {CorpActId} ({EventType}) for {Symbol} is not mapped to a backtest asset event",
                        action.CorpActId,
                        action.EventType,
                        symbol);
                }
            }
        }

        return events
            .OrderBy(static assetEvent => assetEvent.EffectiveAt)
            .ThenBy(static assetEvent => assetEvent.Symbol, StringComparer.Ordinal)
            .ToArray();
    }

    internal static AssetEvent? TryMap(string symbol, CorporateActionDto action)
    {
        if (!CorporateActionTypeDescriptorCatalog.TryNormalize(action.EventType, out var descriptor))
        {
            return null;
        }

        var effectiveAt = new DateTimeOffset(action.ExDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var description = $"{action.EventType} (Security Master {action.CorpActId:D})";
        return descriptor.AdjustmentBehavior switch
        {
            CorporateActionAdjustmentBehavior.PriceScaling when action.SplitRatio is > 0m and not 1m =>
                new AssetEvent(
                    effectiveAt,
                    symbol,
                    action.SplitRatio.Value > 1m ? AssetEventType.Split : AssetEventType.ReverseSplit,
                    PositionFactor: action.SplitRatio.Value,
                    Description: description),
            CorporateActionAdjustmentBehavior.CashDistribution when action.DividendPerShare is > 0m =>
                new AssetEvent(
                    effectiveAt,
                    symbol,
                    string.Equals(
                        CorporateActionEventTypes.Normalize(action.EventType),
                        CorporateActionEventTypes.ReturnOfCapital,
                        StringComparison.Ordinal)
                        ? AssetEventType.ReturnOfCapital
                        : AssetEventType.Dividend,
                    CashPerShare: action.DividendPerShare.Value,
                    Description: description),
            _ => null
        };
    }
}
