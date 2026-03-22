using System.Collections.Concurrent;
using Meridian.Application.Config;
using Meridian.Application.Coordination;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure;
using Serilog;

namespace Meridian.Application.Subscriptions;

/// <summary>
/// Applies AppConfig symbol changes at runtime (hot reload).
/// Responsible for:
/// - registering symbols with collectors (domain)
/// - subscribing/unsubscribing market depth (infrastructure) via IMarketDataClient
/// - tracking option contract subscriptions for equity and index options
///
/// Trades are currently always accepted by TradeDataCollector, but this class is future-proofed to support
/// explicit per-symbol trade subscriptions once you wire them in (tick-by-tick reqs).
/// </summary>
public sealed class SubscriptionOrchestrator
{
    private readonly MarketDepthCollector _depthCollector;
    private readonly TradeDataCollector _tradeCollector;
    private readonly OptionDataCollector? _optionCollector;
    private readonly IMarketDataClient _ib;
    private readonly string _providerId;
    private readonly ISubscriptionOwnershipService? _ownershipService;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _applyGate = new(1, 1);

    // symbol -> subscription id
    private readonly ConcurrentDictionary<string, int> _tradeSubs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _depthSubs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _optionSubs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SymbolConfig> _lastConfig = new(StringComparer.OrdinalIgnoreCase);

    public SubscriptionOrchestrator(
        MarketDepthCollector depthCollector,
        TradeDataCollector tradeCollector,
        IMarketDataClient ibClient,
        string providerId,
        ISubscriptionOwnershipService? ownershipService = null,
        ILogger? log = null,
        OptionDataCollector? optionCollector = null)
    {
        _depthCollector = depthCollector ?? throw new ArgumentNullException(nameof(depthCollector));
        _tradeCollector = tradeCollector ?? throw new ArgumentNullException(nameof(tradeCollector));
        _ib = ibClient ?? throw new ArgumentNullException(nameof(ibClient));
        _providerId = string.IsNullOrWhiteSpace(providerId) ? "unknown" : providerId.Trim();
        _ownershipService = ownershipService;
        _log = log ?? LoggingSetup.ForContext<SubscriptionOrchestrator>();
        _optionCollector = optionCollector;

        if (_optionCollector is not null)
            _log.Information("OptionDataCollector available; option data will be collected by the option collector, while subscriptions remain managed by SubscriptionOrchestrator");
    }

    public IReadOnlyDictionary<string, int> DepthSubscriptions => _depthSubs;
    public IReadOnlyDictionary<string, int> TradeSubscriptions => _tradeSubs;
    public IReadOnlyDictionary<string, int> OptionSubscriptions => _optionSubs;

    /// <summary>
    /// Gets the total number of active subscriptions (trades + depth + options).
    /// </summary>
    public int ActiveSubscriptionCount => _tradeSubs.Count + _depthSubs.Count + _optionSubs.Count;

    /// <summary>
    /// Returns true if the given symbol config represents an option contract.
    /// </summary>
    public static bool IsOptionSymbol(SymbolConfig sc) =>
        string.Equals(sc.SecurityType, "OPT", StringComparison.OrdinalIgnoreCase)
        || sc.InstrumentType is InstrumentType.EquityOption or InstrumentType.IndexOption;

    public void Apply(AppConfig cfg)
        => ApplyAsync(cfg).GetAwaiter().GetResult();

    public async Task ApplyAsync(AppConfig cfg, CancellationToken ct = default)
    {
        if (cfg is null)
            throw new ArgumentNullException(nameof(cfg));

        await _applyGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var desired = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
                .Where(s => !string.IsNullOrWhiteSpace(s.Symbol))
                .ToDictionary(s => s.Symbol.Trim(), s => s, StringComparer.OrdinalIgnoreCase);

            // Unsubscribe removed symbols
            var allKeys = desired.Keys
                .Concat(_depthSubs.Keys)
                .Concat(_tradeSubs.Keys)
                .Concat(_optionSubs.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var existing in allKeys)
            {
                if (!desired.ContainsKey(existing))
                {
                    if (_depthSubs.TryRemove(existing, out var depthId) && depthId > 0)
                    {
                        try
                        { _ib.UnsubscribeMarketDepth(depthId); }
                        catch (Exception ex) { _log.Debug(ex, "Error unsubscribing market depth for {Symbol}", existing); }
                    }
                    await ReleaseOwnershipAsync("depth", existing, ct).ConfigureAwait(false);
                    if (_tradeSubs.TryRemove(existing, out var tradeId) && tradeId > 0)
                    {
                        try
                        { _ib.UnsubscribeTrades(tradeId); }
                        catch (Exception ex) { _log.Debug(ex, "Error unsubscribing trades for {Symbol}", existing); }
                    }
                    await ReleaseOwnershipAsync("trades", existing, ct).ConfigureAwait(false);
                    if (_optionSubs.TryRemove(existing, out var optionId) && optionId > 0)
                    {
                        try
                        { _ib.UnsubscribeTrades(optionId); }
                        catch (Exception ex) { _log.Debug(ex, "Error unsubscribing option trades for {Symbol}", existing); }
                    }
                    await ReleaseOwnershipAsync("options", existing, ct).ConfigureAwait(false);
                    _depthCollector.UnregisterSubscription(existing);
                    _log.Information("Unsubscribed {Symbol} (removed from configuration)", existing);
                }
            }

            // Apply desired set
            foreach (var kvp in desired)
            {
                var symbol = kvp.Key;
                var sc = kvp.Value;
                _lastConfig.TryGetValue(symbol, out var previous);

                var isOption = IsOptionSymbol(sc);

                if (previous is null)
                {
                    if (isOption)
                    {
                        _log.Information(
                            "Subscribing option {Symbol}: type={InstrumentType}, strike={Strike}, right={Right}, expiry={Expiry}",
                            symbol, sc.InstrumentType, sc.Strike, sc.Right, sc.LastTradeDateOrContractMonth);
                    }
                    else
                    {
                        _log.Information("Subscribing {Symbol}: trades={Trades}, depth={Depth}, levels={Levels}",
                            symbol, sc.SubscribeTrades, sc.SubscribeDepth, sc.DepthLevels);
                    }
                }
                else if (HasChanged(previous, sc))
                {
                    _log.Information(
                        "Updating {Symbol} subscription: trades {PrevTrades}->{Trades}, depth {PrevDepth}->{Depth}, levels {PrevLevels}->{Levels}",
                        symbol,
                        previous.SubscribeTrades,
                        sc.SubscribeTrades,
                        previous.SubscribeDepth,
                        sc.SubscribeDepth,
                        previous.DepthLevels,
                        sc.DepthLevels);
                }

                if (isOption)
                {
                    // Option contract subscription — subscribe to trades via provider
                    if (sc.SubscribeTrades && !_optionSubs.ContainsKey(symbol))
                    {
                        var ownership = await TryAcquireOwnershipAsync("options", symbol, ct).ConfigureAwait(false);
                        if (!ownership.Acquired)
                            continue;

                        try
                        {
                            var id = _ib.SubscribeTrades(sc);
                            if (id > 0)
                                _optionSubs[symbol] = id;
                        }
                        catch (Exception ex)
                        {
                            _log.Warning(ex, "Failed to subscribe option trades for {Symbol}. Provider may be unavailable.", symbol);
                            _optionSubs[symbol] = -1;
                            await ReleaseOwnershipAsync("options", symbol, ct).ConfigureAwait(false);
                        }
                    }

                    // Options do not typically need L2 depth subscriptions
                    continue;
                }

                // Depth (equity only)
                if (sc.SubscribeDepth)
                {
                    _depthCollector.RegisterSubscription(symbol);

                    if (!_depthSubs.ContainsKey(symbol))
                    {
                        var ownership = await TryAcquireOwnershipAsync("depth", symbol, ct).ConfigureAwait(false);
                        if (!ownership.Acquired)
                            continue;

                        try
                        {
                            var id = _ib.SubscribeMarketDepth(sc);
                            if (id > 0)
                                _depthSubs[symbol] = id;
                        }
                        catch (Exception ex)
                        {
                            _log.Warning(ex, "Failed to subscribe market depth for {Symbol}. Provider may be unavailable.", symbol);
                            _depthSubs[symbol] = -1;
                            await ReleaseOwnershipAsync("depth", symbol, ct).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    _depthCollector.UnregisterSubscription(symbol);

                    if (_depthSubs.TryRemove(symbol, out var subId) && subId > 0)
                    {
                        try
                        { _ib.UnsubscribeMarketDepth(subId); }
                        catch (Exception ex) { _log.Debug(ex, "Error unsubscribing market depth for {Symbol}", symbol); }
                    }
                    await ReleaseOwnershipAsync("depth", symbol, ct).ConfigureAwait(false);
                }

                // Trades (tick-by-tick)
                if (sc.SubscribeTrades)
                {
                    if (!_tradeSubs.ContainsKey(symbol))
                    {
                        var ownership = await TryAcquireOwnershipAsync("trades", symbol, ct).ConfigureAwait(false);
                        if (!ownership.Acquired)
                            continue;

                        try
                        {
                            var id = _ib.SubscribeTrades(sc);
                            if (id > 0)
                                _tradeSubs[symbol] = id;
                        }
                        catch (Exception ex)
                        {
                            _log.Warning(ex, "Failed to subscribe trades for {Symbol}. Provider may be unavailable.", symbol);
                            _tradeSubs[symbol] = -1;
                            await ReleaseOwnershipAsync("trades", symbol, ct).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    if (_tradeSubs.TryRemove(symbol, out var tradeId) && tradeId > 0)
                    {
                        try
                        { _ib.UnsubscribeTrades(tradeId); }
                        catch (Exception ex) { _log.Debug(ex, "Error unsubscribing trades for {Symbol}", symbol); }
                    }
                    await ReleaseOwnershipAsync("trades", symbol, ct).ConfigureAwait(false);
                }
            }

            _lastConfig.Clear();
            foreach (var kvp in desired)
            {
                _lastConfig[kvp.Key] = kvp.Value;
            }
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private async Task<LeaseAcquireResult> TryAcquireOwnershipAsync(string kind, string symbol, CancellationToken ct)
    {
        if (_ownershipService is null)
            return new LeaseAcquireResult(true, false, null, null, null, null);

        var result = await _ownershipService.TryAcquireAsync(_providerId, kind, symbol, ct).ConfigureAwait(false);
        if (!result.Acquired)
        {
            _log.Information(
                "Skipped subscribing {Kind} for {Symbol} because lease is owned by {Owner} until {Expiry}",
                kind,
                symbol,
                result.CurrentOwner,
                result.CurrentExpiryUtc);
        }

        return result;
    }

    private Task ReleaseOwnershipAsync(string kind, string symbol, CancellationToken ct)
    {
        if (_ownershipService is null)
            return Task.CompletedTask;

        return _ownershipService.ReleaseAsync(_providerId, kind, symbol, ct);
    }

    private static bool HasChanged(SymbolConfig previous, SymbolConfig current)
    {
        return previous.SubscribeTrades != current.SubscribeTrades
               || previous.SubscribeDepth != current.SubscribeDepth
               || previous.DepthLevels != current.DepthLevels
               || !string.Equals(previous.Exchange, current.Exchange, StringComparison.OrdinalIgnoreCase)
               || !string.Equals(previous.LocalSymbol, current.LocalSymbol, StringComparison.OrdinalIgnoreCase)
               || !string.Equals(previous.PrimaryExchange, current.PrimaryExchange, StringComparison.OrdinalIgnoreCase)
               || previous.Strike != current.Strike
               || previous.Right != current.Right
               || previous.LastTradeDateOrContractMonth != current.LastTradeDateOrContractMonth;
    }
}
