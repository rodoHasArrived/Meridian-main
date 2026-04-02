using System.Diagnostics;
using System.Reflection;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.FillModels;
using Meridian.Backtesting.Metrics;
using Meridian.Backtesting.Portfolio;
using Meridian.Contracts.SecurityMaster;
using Meridian.Domain.Events;
using Meridian.Storage.Replay;
using Meridian.Storage.Services;

namespace Meridian.Backtesting.Engine;

/// <summary>
/// Core backtesting engine. Drives a multi-symbol chronological merge over locally-stored
/// JSONL data, dispatches events to the strategy, processes fills, and records cash flows.
/// </summary>
public sealed class BacktestEngine(
    ILogger<BacktestEngine> logger,
    StorageCatalogService catalogService,
    Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService? securityMasterQueryService = null,
    ICorporateActionAdjustmentService? corporateActionAdjustment = null,
    Meridian.Infrastructure.Adapters.Core.IOptionsChainProvider? optionsProvider = null)
{
    /// <summary>
    /// Runs a complete backtest, replaying all events in the requested date/symbol range.
    /// </summary>
    /// <param name="request">Backtest parameters.</param>
    /// <param name="strategy">Strategy implementation to drive.</param>
    /// <param name="progress">Optional real-time progress notifications.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<BacktestResult> RunAsync(
        BacktestRequest request,
        IBacktestStrategy strategy,
        IProgress<BacktestProgressEvent>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(strategy);

        var sw = Stopwatch.StartNew();
        logger.LogInformation("Backtesting '{Strategy}' from {From} to {To} in {DataRoot}",
            strategy.Name, request.From, request.To, request.DataRoot);

        // 1. Discover universe
        var universe = await UniverseDiscovery.DiscoverAsync(
            catalogService, request.DataRoot, request.Symbols, request.From, request.To, ct)
            .ConfigureAwait(false);

        if (universe.Count == 0 && request.AssetEvents is not { Count: > 0 })
        {
            logger.LogWarning("No symbols found in data root '{DataRoot}' for the requested date range", request.DataRoot);
            return CreateEmptyResult(request, universe, sw.Elapsed);
        }

        logger.LogInformation("Universe contains {Count} symbols: {Symbols}",
            universe.Count, universe.Count == 0 ? "(asset-event-only run)" : string.Join(", ", universe.Take(10)) + (universe.Count > 10 ? "…" : string.Empty));

        // 1b. Pre-flight Security Master validation — resolve all universe symbols before the
        //     event loop begins so bad symbol lists surface immediately.
        await PreResolveUniverseAsync(universe, request, ct).ConfigureAwait(false);

        // 2. Resolve per-symbol tick sizes from Security Master (best-effort; missing symbols are silently skipped).
        var tickSizes = await ResolveTickSizesAsync(universe, request.To.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct)
            .ConfigureAwait(false);

        // 3. Set up portfolio, fill models, context
        var commissionModel = BuildCommissionModel(request);
        var ledger = new BacktestLedger();
        var startTimestamp = new DateTimeOffset(request.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var accounts = request.ResolveAccounts();
        var portfolio = new SimulatedPortfolio(accounts, request.DefaultBrokerageAccountId, commissionModel, ledger, startTimestamp);
        var ctx = new BacktestContext(portfolio, universe, ledger, request.DefaultBrokerageAccountId, optionsProvider);
        var orderBookFillModel = new OrderBookFillModel(commissionModel, tickSizes);
        var barFillModel = new BarMidpointFillModel(commissionModel, request.SlippageBasisPoints, spreadAware: true, tickSizes: tickSizes, maxParticipationRate: request.MaxParticipationRate);
        var marketImpactFillModel = new MarketImpactFillModel(commissionModel, request.MarketImpactCoefficient, request.SlippageBasisPoints);

        var pendingOrders = new List<Order>();
        var allSnapshots = new List<PortfolioSnapshot>();
        var allCashFlows = new List<CashFlowEntry>();
        var allFills = new List<FillEvent>();
        var assetEventsByDate = BuildAssetEventIndex(request.AssetEvents, request.From, request.To);

        // 3. Initialise strategy
        ctx.CurrentTime = startTimestamp;
        ctx.CurrentDate = request.From;
        strategy.Initialize(ctx);
        ApplyScheduledAssetEvents(request.From, assetEventsByDate, portfolio, ctx);

        // 4. Build per-symbol replay streams (with corporate action adjustments if enabled)
        var streams = await BuildSymbolStreamsAsync(universe, request, ct).ConfigureAwait(false);

        // 5. Replay loop — multi-symbol chronological merge
        var currentDay = request.From;
        long eventsProcessed = 0;
        var totalDays = (request.To.ToDateTime(TimeOnly.MinValue) - request.From.ToDateTime(TimeOnly.MinValue)).Days + 1;

        await foreach (var evt in MultiSymbolMergeEnumerator.MergeAsync(streams, ct))
        {
            ct.ThrowIfCancellationRequested();

            var evtDate = DateOnly.FromDateTime(evt.Timestamp.UtcDateTime);

            // Day boundary — close out the previous day and apply any gap-day asset events.
            if (evtDate > currentDay)
            {
                AdvanceDays(currentDay, evtDate, portfolio, ctx, strategy, pendingOrders, allSnapshots, allCashFlows, assetEventsByDate, progress, request.From, totalDays, eventsProcessed, ct);
                currentDay = evtDate;
            }

            ctx.CurrentTime = evt.Timestamp;
            ctx.CurrentDate = evtDate;
            eventsProcessed++;

            // Update last known price from event
            UpdateLastPrice(portfolio, evt);

            // Dispatch to strategy
            DispatchEvent(strategy, ctx, evt);

            // Collect new orders placed by strategy
            var newOrders = ctx.DrainPendingOrders();
            pendingOrders.AddRange(newOrders);

            // Try to fill pending orders against current event
            ProcessPendingOrders(pendingOrders, evt, orderBookFillModel, barFillModel, marketImpactFillModel, portfolio, strategy, ctx, allFills, logger, request.DefaultExecutionModel);
        }

        // Final day-end for the last processed day and any remaining asset-event-only dates.
        ProcessDayEnd(currentDay, portfolio, pendingOrders, ctx, strategy, allSnapshots, allCashFlows, ct);
        for (var date = currentDay.AddDays(1); date <= request.To; date = date.AddDays(1))
        {
            ApplyScheduledAssetEvents(date, assetEventsByDate, portfolio, ctx);
            ProcessDayEnd(date, portfolio, pendingOrders, ctx, strategy, allSnapshots, allCashFlows, ct);
        }

        strategy.OnFinished(ctx);
        progress?.Report(new BacktestProgressEvent(1.0, request.To, portfolio.ComputeCurrentEquity(), eventsProcessed, "Complete"));

        // 6. Compute metrics
        var metrics = BacktestMetricsEngine.Compute(allSnapshots, allCashFlows, allFills, request);
        sw.Stop();

        if (double.IsNaN(metrics.Xirr))
            logger.LogWarning("XIRR bisection did not converge for this backtest run; Xirr will be reported as NaN. Check cash-flow patterns for non-standard sign changes.");

        logger.LogInformation(
            "Backtest complete: {Events} events, final equity {Equity:C}, net PnL {NetPnl:C} in {Elapsed}ms",
            eventsProcessed, metrics.FinalEquity, metrics.NetPnl, sw.ElapsedMilliseconds);

        var tradeTickets = BuildTradeTickets(allCashFlows);
        var tcaReport = PostSimulationTcaReporter.Generate(request, allFills);
        var allClosedLots = portfolio.GetAllClosedLots();
        return new BacktestResult(request, universe, allSnapshots, allCashFlows, allFills, metrics, ledger, sw.Elapsed, eventsProcessed, tradeTickets, tcaReport, allClosedLots)
        {
            Coverage = BuildNativeArtifactCoverage(),
            EngineMetadata = BuildNativeEngineMetadata()
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private Task<IReadOnlyList<IAsyncEnumerable<MarketEvent>>> BuildSymbolStreamsAsync(
        IReadOnlySet<string> universe,
        BacktestRequest request,
        CancellationToken ct)
    {
        var streams = new List<IAsyncEnumerable<MarketEvent>>();
        foreach (var symbol in universe)
        {
            var symbolRoot = Path.Combine(request.DataRoot, symbol.ToUpperInvariant());
            if (!Directory.Exists(symbolRoot))
                symbolRoot = request.DataRoot;  // flat layout fallback

            var reader = new JsonlReplayer(symbolRoot);
            var symbolStream = FilterBySymbolAndDate(reader.ReadEventsAsync(), symbol, request.From, request.To);

            // Apply corporate action adjustments if enabled
            if (request.AdjustForCorporateActions && corporateActionAdjustment != null)
            {
                symbolStream = ApplyCorporateActionAdjustmentsAsync(symbolStream, symbol, ct);
            }

            streams.Add(symbolStream);
        }
        return Task.FromResult<IReadOnlyList<IAsyncEnumerable<MarketEvent>>>(streams);
    }

    /// <summary>
    /// Wraps a symbol stream to apply corporate action adjustments to all HistoricalBar events.
    /// </summary>
    private async IAsyncEnumerable<MarketEvent> ApplyCorporateActionAdjustmentsAsync(
        IAsyncEnumerable<MarketEvent> source,
        string symbol,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Buffer all bars for this symbol
        var bars = new List<HistoricalBar>();
        var nonBarEvents = new List<MarketEvent>();

        await foreach (var evt in source.WithCancellation(ct))
        {
            if (evt.Payload is HistoricalBar bar)
            {
                bars.Add(bar);
            }
            else
            {
                nonBarEvents.Add(evt);
            }
        }

        // Apply adjustments to all bars
        var adjustedBars = await corporateActionAdjustment!.AdjustAsync(bars, symbol, ct)
            .ConfigureAwait(false);

        // Merge adjusted bars back with non-bar events
        // Re-create events in chronological order
        var barsByTimestamp = adjustedBars.OrderBy(b => b.SessionDate).ToList();
        var nonBarsByTimestamp = nonBarEvents.OrderBy(e => e.Timestamp).ToList();

        int bIdx = 0, nbIdx = 0;
        while (bIdx < barsByTimestamp.Count && nbIdx < nonBarsByTimestamp.Count)
        {
            var bar = barsByTimestamp[bIdx];
            var nonBar = nonBarsByTimestamp[nbIdx];
            var barTimestamp = bar.SessionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            if (barTimestamp <= nonBar.Timestamp.UtcDateTime)
            {
                yield return MarketEvent.HistoricalBar(
                    new DateTimeOffset(barTimestamp, TimeSpan.Zero),
                    symbol,
                    bar);
                bIdx++;
            }
            else
            {
                yield return nonBar;
                nbIdx++;
            }
        }

        while (bIdx < barsByTimestamp.Count)
        {
            var bar = barsByTimestamp[bIdx];
            yield return MarketEvent.HistoricalBar(
                new DateTimeOffset(bar.SessionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero),
                symbol,
                bar);
            bIdx++;
        }

        while (nbIdx < nonBarsByTimestamp.Count)
        {
            yield return nonBarsByTimestamp[nbIdx];
            nbIdx++;
        }
    }

    private static Dictionary<DateOnly, List<AssetEvent>> BuildAssetEventIndex(
        IReadOnlyList<AssetEvent>? assetEvents,
        DateOnly from,
        DateOnly to)
    {
        if (assetEvents is not { Count: > 0 })
            return [];

        return assetEvents
            .Where(assetEvent =>
            {
                var eventDate = DateOnly.FromDateTime(assetEvent.EffectiveAt.UtcDateTime);
                return eventDate >= from && eventDate <= to;
            })
            .GroupBy(assetEvent => DateOnly.FromDateTime(assetEvent.EffectiveAt.UtcDateTime))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(assetEvent => assetEvent.EffectiveAt).ToList());
    }

    private static void ApplyScheduledAssetEvents(
        DateOnly date,
        IReadOnlyDictionary<DateOnly, List<AssetEvent>> assetEventsByDate,
        SimulatedPortfolio portfolio,
        BacktestContext ctx)
    {
        if (!assetEventsByDate.TryGetValue(date, out var assetEvents))
            return;

        foreach (var assetEvent in assetEvents)
        {
            ctx.CurrentDate = date;
            ctx.CurrentTime = assetEvent.EffectiveAt;
            portfolio.ApplyAssetEvent(assetEvent);
        }
    }

    private static void AdvanceDays(
        DateOnly fromDay,
        DateOnly toDay,
        SimulatedPortfolio portfolio,
        BacktestContext ctx,
        IBacktestStrategy strategy,
        List<Order> pendingOrders,
        List<PortfolioSnapshot> snapshots,
        List<CashFlowEntry> allCashFlows,
        IReadOnlyDictionary<DateOnly, List<AssetEvent>> assetEventsByDate,
        IProgress<BacktestProgressEvent>? progress,
        DateOnly requestFrom,
        int totalDays,
        long eventsProcessed,
        CancellationToken ct)
    {
        ProcessDayEnd(fromDay, portfolio, pendingOrders, ctx, strategy, snapshots, allCashFlows, ct);

        for (var date = fromDay.AddDays(1); date <= toDay; date = date.AddDays(1))
        {
            ApplyScheduledAssetEvents(date, assetEventsByDate, portfolio, ctx);

            if (date < toDay)
                ProcessDayEnd(date, portfolio, pendingOrders, ctx, strategy, snapshots, allCashFlows, ct);

            var daysElapsed = (date.ToDateTime(TimeOnly.MinValue) - requestFrom.ToDateTime(TimeOnly.MinValue)).Days;
            progress?.Report(new BacktestProgressEvent(
                (double)daysElapsed / totalDays,
                date,
                portfolio.ComputeCurrentEquity(),
                eventsProcessed));
        }
    }

    private static async IAsyncEnumerable<MarketEvent> FilterBySymbolAndDate(
        IAsyncEnumerable<MarketEvent> source,
        string symbol,
        DateOnly from,
        DateOnly to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in source.WithCancellation(ct))
        {
            if (!evt.EffectiveSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                continue;
            var date = DateOnly.FromDateTime(evt.Timestamp.UtcDateTime);
            if (date < from || date > to)
                continue;
            yield return evt;
        }
    }

    private static void UpdateLastPrice(SimulatedPortfolio portfolio, MarketEvent evt)
    {
        decimal? price = evt.Payload switch
        {
            Trade t => t.Price,
            BboQuotePayload bbo => bbo.MidPrice ?? (bbo.BidPrice + bbo.AskPrice) / 2m,
            HistoricalBar bar => bar.Close,
            _ => null
        };
        if (price.HasValue && price.Value > 0)
            portfolio.UpdateLastPrice(evt.EffectiveSymbol, price.Value);
    }

    private static void DispatchEvent(IBacktestStrategy strategy, BacktestContext ctx, MarketEvent evt)
    {
        switch (evt.Payload)
        {
            case Trade t:
                strategy.OnTrade(t, ctx);
                break;
            case BboQuotePayload q:
                strategy.OnQuote(q, ctx);
                break;
            case HistoricalBar bar:
                strategy.OnBar(bar, ctx);
                break;
            case LOBSnapshot lob:
                strategy.OnOrderBook(lob, ctx);
                break;
        }
    }

    private static void ProcessPendingOrders(
        List<Order> pendingOrders,
        MarketEvent evt,
        IFillModel lobModel,
        IFillModel barModel,
        IFillModel marketImpactModel,
        SimulatedPortfolio portfolio,
        IBacktestStrategy strategy,
        BacktestContext ctx,
        List<FillEvent> allFills,
        ILogger<BacktestEngine> logger,
        ExecutionModel requestDefault = ExecutionModel.Auto)
    {
        var filled = new List<Guid>();
        for (var i = pendingOrders.Count - 1; i >= 0; i--)
        {
            var order = pendingOrders[i];
            if (!order.Symbol.Equals(evt.EffectiveSymbol, StringComparison.OrdinalIgnoreCase))
                continue;

            var model = SelectFillModel(order, evt, lobModel, barModel, marketImpactModel, requestDefault);
            var result = model.TryFill(order, evt);

            foreach (var fill in result.Fills)
            {
                try
                {
                    portfolio.ProcessFill(fill);
                }
                catch (InvalidOperationException ex)
                {
                    // Account rule violation (e.g. short-selling or margin disabled).
                    // Reject this fill rather than crashing the entire backtest run.
                    logger.LogWarning(ex,
                        "Fill rejected for order {OrderId} on {Symbol}: {Message}. The fill has been discarded.",
                        fill.OrderId, fill.Symbol, ex.Message);
                    continue;
                }

                ContingentOrderManager.ReconcileOcoSiblings(pendingOrders, order, fill);
                allFills.Add(fill);
                strategy.OnOrderFill(fill, ctx);

                foreach (var contingentOrder in ContingentOrderManager.CreateContingentOrders(order, fill))
                    pendingOrders.Add(contingentOrder);
            }

            if (result.RemoveOrder)
            {
                filled.Add(order.OrderId);
                continue;
            }

            pendingOrders[i] = result.UpdatedOrder;
        }

        pendingOrders.RemoveAll(o =>
            filled.Contains(o.OrderId) ||
            o.Status is OrderStatus.Cancelled or OrderStatus.Expired or OrderStatus.Rejected ||
            (o.Status == OrderStatus.Filled && o.IsComplete));
    }

    private static void ProcessDayEnd(
        DateOnly date,
        SimulatedPortfolio portfolio,
        List<Order> pendingOrders,
        BacktestContext ctx,
        IBacktestStrategy strategy,
        List<PortfolioSnapshot> snapshots,
        List<CashFlowEntry> allCashFlows,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        portfolio.AccrueDailyInterest(date);
        ctx.CurrentDate = date;
        strategy.OnDayEnd(date, ctx);

        for (var i = pendingOrders.Count - 1; i >= 0; i--)
        {
            if (pendingOrders[i].TimeInForce != TimeInForce.Day)
                continue;

            pendingOrders.RemoveAt(i);
        }

        var ts = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var snapshot = portfolio.TakeSnapshot(ts, date);
        snapshots.Add(snapshot);
        allCashFlows.AddRange(snapshot.DayCashFlows);
    }

    private static BacktestResult CreateEmptyResult(BacktestRequest request, IReadOnlySet<string> universe, TimeSpan elapsed)
    {
        var metrics = BacktestMetricsEngine.Compute([], [], [], request);
        var tcaReport = PostSimulationTcaReporter.Generate(request, []);
        return new BacktestResult(request, universe, [], [], [], metrics, new BacktestLedger(), elapsed, 0, [], tcaReport)
        {
            Coverage = BuildNativeArtifactCoverage(),
            EngineMetadata = BuildNativeEngineMetadata()
        };
    }

    private static BacktestArtifactCoverage BuildNativeArtifactCoverage() => new(
        Snapshots: BacktestArtifactStatus.Complete,
        CashFlows: BacktestArtifactStatus.Complete,
        Fills: BacktestArtifactStatus.Complete,
        TradeTickets: BacktestArtifactStatus.Complete,
        Ledger: BacktestArtifactStatus.Complete,
        TcaReport: BacktestArtifactStatus.Complete);

    private static BacktestEngineMetadata BuildNativeEngineMetadata()
    {
        var assembly = typeof(BacktestEngine).Assembly;
        var version =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        return new BacktestEngineMetadata(
            EngineId: "MeridianNative",
            EngineVersion: version,
            SourceFormat: "Meridian.Backtesting.BacktestResult",
            Diagnostics: new Dictionary<string, string>());
    }

    private static IFillModel SelectFillModel(
        Order order,
        MarketEvent evt,
        IFillModel lobModel,
        IFillModel barModel,
        IFillModel marketImpactModel,
        ExecutionModel requestDefault = ExecutionModel.Auto)
    {
        // Order-level setting takes precedence; fall back to request default, then auto-select.
        var effective = order.ExecutionModel == ExecutionModel.Auto ? requestDefault : order.ExecutionModel;
        return effective switch
        {
            ExecutionModel.OrderBook => lobModel,
            ExecutionModel.BarMidpoint => barModel,
            ExecutionModel.MarketImpact => marketImpactModel,
            _ => evt.Payload is LOBSnapshot ? lobModel : barModel
        };
    }

    private static ICommissionModel BuildCommissionModel(BacktestRequest request) =>
        request.CommissionKind switch
        {
            BacktestCommissionKind.Free => new FixedCommissionModel(0m),
            BacktestCommissionKind.Percentage => new PercentageCommissionModel(
                basisPoints: request.CommissionRate,
                minimumPerOrder: request.CommissionMinimum),
            _ => new PerShareCommissionModel(
                perShare: request.CommissionRate,
                minimumPerOrder: request.CommissionMinimum,
                maximumPerOrder: request.CommissionMaximum)
        };

    private static IReadOnlyList<TradeTicket> BuildTradeTickets(IReadOnlyList<CashFlowEntry> cashFlows)
    {
        var tickets = new List<TradeTicket>(cashFlows.Count);

        foreach (var flow in cashFlows.OrderBy(flow => flow.Timestamp))
        {
            switch (flow)
            {
                case TradeCashFlow trade:
                    tickets.Add(new TradeTicket(
                        Guid.NewGuid(),
                        trade.Timestamp,
                        "trade_cash_flow",
                        trade.Symbol,
                        $"Trade execution cash impact for {trade.Symbol} ({trade.Quantity} @ {trade.Price:F4}).",
                        trade.Amount,
                        trade.Quantity,
                        trade.Price,
                        trade.AccountId));
                    break;
                case CommissionCashFlow commission:
                    tickets.Add(new TradeTicket(
                        Guid.NewGuid(),
                        commission.Timestamp,
                        "commission",
                        commission.Symbol,
                        $"Commission charged for order {commission.OrderId} on {commission.Symbol}.",
                        commission.Amount,
                        AccountId: commission.AccountId,
                        OrderId: commission.OrderId));
                    break;
                case AssetEventCashFlow assetEvent:
                    tickets.Add(new TradeTicket(
                        Guid.NewGuid(),
                        assetEvent.Timestamp,
                        $"asset_event:{assetEvent.EventType}".ToLowerInvariant(),
                        assetEvent.Symbol,
                        BuildAssetEventNarrative(assetEvent),
                        assetEvent.Amount,
                        assetEvent.UnitsImpacted,
                        assetEvent.CashPerShare));
                    break;
                case DividendCashFlow dividend:
                    tickets.Add(new TradeTicket(
                        Guid.NewGuid(),
                        dividend.Timestamp,
                        "dividend",
                        dividend.Symbol,
                        $"Dividend receipt/charge for {dividend.Symbol} ({dividend.Shares} shares @ {dividend.DividendPerShare:F4}).",
                        dividend.Amount,
                        dividend.Shares,
                        dividend.DividendPerShare,
                        dividend.AccountId));
                    break;
                case MarginInterestCashFlow margin:
                    tickets.Add(new TradeTicket(
                        Guid.NewGuid(),
                        margin.Timestamp,
                        "margin_interest",
                        Symbol: null,
                        Narrative: $"Margin interest accrual at {margin.AnnualRate:P2} annualized rate.",
                        CashImpact: margin.Amount,
                        AccountId: margin.AccountId));
                    break;
                case CashInterestCashFlow cashInterest:
                    tickets.Add(new TradeTicket(
                        Guid.NewGuid(),
                        cashInterest.Timestamp,
                        "cash_interest",
                        Symbol: null,
                        Narrative: $"Cash interest accrual at {cashInterest.AnnualRate:P2} annualized rate.",
                        CashImpact: cashInterest.Amount,
                        AccountId: cashInterest.AccountId));
                    break;
                case ShortRebateCashFlow shortRebate:
                    tickets.Add(new TradeTicket(
                        Guid.NewGuid(),
                        shortRebate.Timestamp,
                        "short_rebate",
                        shortRebate.Symbol,
                        $"Short rebate on {shortRebate.Symbol} ({shortRebate.ShortShares} shares @ {shortRebate.AnnualRebateRate:P2}).",
                        shortRebate.Amount,
                        shortRebate.ShortShares,
                        AccountId: shortRebate.AccountId));
                    break;
            }
        }

        return tickets;
    }

    private static string BuildAssetEventNarrative(AssetEventCashFlow assetEvent)
    {
        if (!string.IsNullOrWhiteSpace(assetEvent.Description))
            return assetEvent.Description!;

        var related = string.IsNullOrWhiteSpace(assetEvent.RelatedSymbol)
            ? string.Empty
            : $" related symbol {assetEvent.RelatedSymbol}.";

        return $"{assetEvent.EventType} on {assetEvent.Symbol}: {assetEvent.UnitsImpacted} units impacted at {assetEvent.CashPerShare:F4} cash/share.{related}";
    }

    /// <summary>
    /// Pre-flight check: resolves every symbol in the universe against the Security Master
    /// before the event loop starts.  When a symbol is absent and
    /// <see cref="BacktestRequest.FailOnUnknownSymbols"/> is <see langword="true"/>, throws
    /// so the caller gets a clear error before wasting time on a long replay.
    /// When <see langword="false"/> (default), logs a warning and continues.
    /// </summary>
    private async Task PreResolveUniverseAsync(
        IReadOnlySet<string> universe,
        BacktestRequest request,
        CancellationToken ct)
    {
        if (securityMasterQueryService is null || universe.Count == 0)
            return;

        var missing = new List<string>();

        foreach (var symbol in universe)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var detail = await securityMasterQueryService.GetByIdentifierAsync(
                    SecurityIdentifierKind.Ticker, symbol, provider: null, ct).ConfigureAwait(false);

                if (detail is null)
                {
                    missing.Add(symbol);
                    logger.LogWarning(
                        "Backtest symbol {Symbol} is not registered in the Security Master. " +
                        "Price adjustments and tick-size resolution will be unavailable for this symbol.",
                        symbol);
                }
                else if (detail.Status == SecurityStatusDto.Inactive)
                {
                    logger.LogWarning(
                        "Backtest symbol {Symbol} (SecurityId={SecurityId}) is marked Inactive in the Security Master. " +
                        "It may represent a delisted or renamed instrument.",
                        symbol, detail.SecurityId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Security Master lookup for symbol {Symbol} failed during pre-flight; continuing.", symbol);
            }
        }

        if (missing.Count > 0 && request.FailOnUnknownSymbols)
        {
            throw new InvalidOperationException(
                $"Backtest aborted: {missing.Count} symbol(s) not found in the Security Master " +
                $"and FailOnUnknownSymbols=true. Missing: {string.Join(", ", missing)}. " +
                "Import the securities via POST /api/security-master/import or set FailOnUnknownSymbols=false to warn and continue.");
        }
    }

    /// <summary>
    /// Resolves per-symbol tick sizes from the Security Master (best-effort).
    /// Returns an empty dictionary when no Security Master is configured or when a symbol is not found.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, decimal>> ResolveTickSizesAsync(
        IReadOnlySet<string> universe,
        DateTime asOf,
        CancellationToken ct)
    {
        if (securityMasterQueryService is null || universe.Count == 0)
            return new Dictionary<string, decimal>();

        var result = new Dictionary<string, decimal>(universe.Count, StringComparer.OrdinalIgnoreCase);
        var asOfOffset = new DateTimeOffset(asOf, TimeSpan.Zero);

        foreach (var symbol in universe)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var detail = await securityMasterQueryService.GetByIdentifierAsync(
                    SecurityIdentifierKind.Ticker, symbol, provider: null, ct);

                if (detail is null)
                    continue;

                var tradingParams = await securityMasterQueryService.GetTradingParametersAsync(
                    detail.SecurityId, asOfOffset, ct);

                if (tradingParams?.TickSize is { } tickSize && tickSize > 0m)
                    result[symbol] = tickSize;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to resolve tick size for symbol {Symbol}; using default", symbol);
            }
        }

        return result;
    }
}
