using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.FillModels;
using Meridian.Backtesting.Metrics;
using Meridian.Backtesting.Portfolio;
using Meridian.Contracts.Backtesting;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Core.Serialization;
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
    IBacktestPreflightService? backtestPreflightService = null)
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
        var stageTimer = new StageTimer(BacktestStage.ValidatingRequest);
        logger.LogInformation("Backtesting '{Strategy}' from {From} to {To} in {DataRoot}",
            strategy.Name, request.From, request.To, request.DataRoot);

        if (backtestPreflightService is not null)
        {
            var preflightReport = await backtestPreflightService
                .RunAsync(new BacktestPreflightRequestDto(request.From, request.To, request.DataRoot, request.Symbols, request.DefaultExecutionModel.ToString()), ct)
                .ConfigureAwait(false);

            progress?.Report(new BacktestProgressEvent(
                ProgressFraction: 0d,
                CurrentDate: request.From,
                PortfolioValue: request.InitialCash,
                EventsProcessed: 0,
                Message: preflightReport.SummaryMessage,
                LiveMetrics: null,
                Stage: stageTimer.CurrentStage,
                StageElapsed: stageTimer.StageElapsed,
                TotalElapsed: stageTimer.TotalElapsed,
                StageTelemetry: BuildStageTelemetry(stageTimer, preflightReport.SummaryMessage)));

            if (!preflightReport.IsReadyToRun)
            {
                var failures = string.Join("; ",
                    preflightReport.Checks
                        .Where(c => c.Status == BacktestPreflightCheckStatusDto.Failed)
                        .Select(c => $"{c.Name}: {c.Message}"));

                throw new InvalidOperationException($"Backtest preflight failed: {failures}");
            }
        }

        // 1. Discover universe
        stageTimer.Transition(BacktestStage.ValidatingCoverage);
        var universe = await UniverseDiscovery.DiscoverAsync(
            catalogService, request.DataRoot, request.Symbols, request.From, request.To, ct)
            .ConfigureAwait(false);

        if (universe.Count == 0 && request.AssetEvents is not { Count: > 0 })
        {
            logger.LogWarning("No symbols found in data root '{DataRoot}' for the requested date range", request.DataRoot);
            stageTimer.Transition(BacktestStage.Completed);
            stageTimer.Stop();
            return CreateEmptyResult(request, universe, sw.Elapsed);
        }

        logger.LogInformation("Universe contains {Count} symbols: {Symbols}",
            universe.Count, universe.Count == 0 ? "(asset-event-only run)" : string.Join(", ", universe.Take(10)) + (universe.Count > 10 ? "…" : string.Empty));

        // 1b. Pre-flight Security Master validation — resolve all universe symbols before the
        //     event loop begins so bad symbol lists surface immediately. Missing and inactive
        //     symbols feed the bias-disclosure report attached to the result.
        var (missingSecurityMasterSymbols, inactiveSecurityMasterSymbols) =
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
        var ctx = new BacktestContext(
            portfolio,
            universe,
            ledger,
            request.DefaultBrokerageAccountId,
            commissionModel);
        var orderBookFillModel = new OrderBookFillModel(
            commissionModel,
            tickSizes,
            request.OrderBookQueueAheadFraction);
        var barFillModel = new BarMidpointFillModel(commissionModel, request.SlippageBasisPoints, spreadAware: true, tickSizes: tickSizes, maxParticipationRate: request.MaxParticipationRate, conservatism: request.FillConservatism);
        var marketImpactFillModel = new MarketImpactFillModel(
            commissionModel,
            request.MarketImpactCoefficient,
            request.SlippageBasisPoints,
            maxParticipationRate: request.MaxParticipationRate,
            conservatism: request.FillConservatism);
        var delistingMonitor = new DelistingMonitor(request.DelistingPolicy, request.DelistingHaircutPercent, request.DelistingGraceDays);

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
        stageTimer.Transition(BacktestStage.LoadingData);
        var replaySymbols = ResolveReplaySymbolOrder(universe, request.Symbols);
        var streams = await BuildSymbolStreamsAsync(replaySymbols, request, ct).ConfigureAwait(false);

        // 5. Replay loop — multi-symbol chronological merge
        stageTimer.Transition(BacktestStage.Replaying);
        var currentDay = request.From;
        long eventsProcessed = 0;
        var totalDays = (request.To.ToDateTime(TimeOnly.MinValue) - request.From.ToDateTime(TimeOnly.MinValue)).Days + 1;
        var rollingState = new RollingMetricsState(portfolio.ComputeCurrentEquity());
        var lastEventTimestamps = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        await foreach (var evt in MultiSymbolMergeEnumerator.MergeAsync(streams, ct))
        {
            ct.ThrowIfCancellationRequested();

            var evtDate = DateOnly.FromDateTime(evt.Timestamp.UtcDateTime);

            // Day boundary — close out the previous day and apply any gap-day asset events.
            if (evtDate > currentDay)
            {
                AdvanceDays(currentDay, evtDate, portfolio, ctx, strategy, allSnapshots, allCashFlows, assetEventsByDate, progress, request.From, totalDays, eventsProcessed, rollingState, stageTimer, delistingMonitor, allFills, logger, request.FillTiming, lastEventTimestamps, ct);
                currentDay = evtDate;
            }

            ctx.CurrentTime = evt.Timestamp;
            ctx.CurrentDate = evtDate;
            eventsProcessed++;
            delistingMonitor.RecordEvent(evt.EffectiveSymbol, evtDate);
            lastEventTimestamps[evt.EffectiveSymbol] = evt.Timestamp;

            // Update last known price from event
            UpdateLastPrice(portfolio, evt);

            // Dispatch to strategy
            DispatchEvent(strategy, ctx, evt);

            // Try to fill the context-owned authoritative working orders against this event.
            ProcessPendingOrders(evt, orderBookFillModel, barFillModel, marketImpactFillModel, portfolio, strategy, ctx, allFills, logger, rollingState, request.DefaultExecutionModel, request.FillTiming);
        }

        // Final day-end for the last processed day and any remaining asset-event-only dates.
        ProcessDayEnd(currentDay, portfolio, ctx, strategy, allSnapshots, allCashFlows, delistingMonitor, allFills, logger, request.FillTiming, lastEventTimestamps, ct);
        for (var date = currentDay.AddDays(1); date <= request.To; date = date.AddDays(1))
        {
            ApplyScheduledAssetEvents(date, assetEventsByDate, portfolio, ctx);
            ProcessDayEnd(date, portfolio, ctx, strategy, allSnapshots, allCashFlows, delistingMonitor, allFills, logger, request.FillTiming, lastEventTimestamps, ct);
        }

        strategy.OnFinished(ctx);

        // 6. Compute metrics
        stageTimer.Transition(BacktestStage.ComputingMetrics);
        var metrics = BacktestMetricsEngine.Compute(allSnapshots, allCashFlows, allFills, request);
        sw.Stop();

        stageTimer.Transition(BacktestStage.Completed);
        stageTimer.Stop();
        progress?.Report(new BacktestProgressEvent(
            ProgressFraction: 1.0,
            CurrentDate: request.To,
            PortfolioValue: portfolio.ComputeCurrentEquity(),
            EventsProcessed: eventsProcessed,
            Message: "Complete",
            LiveMetrics: null,
            Stage: stageTimer.CurrentStage,
            StageElapsed: stageTimer.StageElapsed,
            TotalElapsed: stageTimer.TotalElapsed,
            StageTelemetry: BuildStageTelemetry(stageTimer, "Complete")));

        if (double.IsNaN(metrics.Xirr))
            logger.LogWarning("XIRR bisection did not converge for this backtest run; Xirr will be reported as NaN. Check cash-flow patterns for non-standard sign changes.");

        logger.LogInformation(
            "Backtest complete: {Events} events, final equity {Equity:C}, net PnL {NetPnl:C} in {Elapsed}ms",
            eventsProcessed, metrics.FinalEquity, metrics.NetPnl, sw.ElapsedMilliseconds);

        var tradeTickets = BuildTradeTickets(allCashFlows);
        var tcaReport = PostSimulationTcaReporter.Generate(request, allFills);
        var biasDisclosure = BuildBiasDisclosure(
            request,
            corporateActionsApplied: request.AdjustForCorporateActions && corporateActionAdjustment is not null,
            missingSecurityMasterSymbols,
            inactiveSecurityMasterSymbols,
            delistingMonitor);
        return new BacktestResult(request, universe, allSnapshots, allCashFlows, allFills, metrics, ledger, sw.Elapsed, eventsProcessed, tradeTickets, tcaReport, BiasDisclosure: biasDisclosure);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static IReadOnlyList<string> ResolveReplaySymbolOrder(
        IReadOnlySet<string> universe,
        IReadOnlyList<string>? requestedSymbols)
    {
        if (requestedSymbols is { Count: > 0 })
        {
            return requestedSymbols
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && universe.Contains(symbol))
                .Select(static symbol => symbol.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return universe
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private Task<IReadOnlyList<IAsyncEnumerable<MarketEvent>>> BuildSymbolStreamsAsync(
        IReadOnlyList<string> replaySymbols,
        BacktestRequest request,
        CancellationToken ct)
    {
        var streams = new List<IAsyncEnumerable<MarketEvent>>();
        foreach (var symbol in replaySymbols)
        {
            var symbolRoot = Path.Combine(request.DataRoot, symbol.ToUpperInvariant());
            if (!Directory.Exists(symbolRoot))
                symbolRoot = request.DataRoot;  // flat layout fallback

            if (request.AdjustForCorporateActions && corporateActionAdjustment != null)
            {
                streams.Add(CapturePrepareAndReplayAsync(symbolRoot, symbol, request, ct));
                continue;
            }

            var replayReader = new JsonlReplayer(symbolRoot);
            var symbolStream = FilterBySymbolAndDate(
                replayReader.ReadEventsAsync(ct),
                symbol,
                request.From,
                request.To,
                ct);
            streams.Add(symbolStream);
        }

        return Task.FromResult<IReadOnlyList<IAsyncEnumerable<MarketEvent>>>(streams);
    }

    /// <summary>
    /// Captures the exact filtered replay used to prepare a corporate-action plan, then executes
    /// from that immutable snapshot so concurrently appended or replaced partitions cannot make
    /// preparation and execution observe different market data.
    /// </summary>
    private async IAsyncEnumerable<MarketEvent> CapturePrepareAndReplayAsync(
        string symbolRoot,
        string symbol,
        BacktestRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var snapshotPath = Path.Combine(
            Path.GetTempPath(),
            $"meridian-backtest-snapshot-{Guid.NewGuid():N}.jsonl");
        try
        {
            var historicalBars = new List<HistoricalBar>();
            await using (var writer = new StreamWriter(new FileStream(
                             snapshotPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             useAsync: true)))
            {
                var preparationReader = new JsonlReplayer(symbolRoot);
                await foreach (var evt in FilterBySymbolAndDate(
                                   preparationReader.ReadEventsAsync(ct),
                                   symbol,
                                   request.From,
                                   request.To,
                                   ct).ConfigureAwait(false))
                {
                    if (evt.Payload is HistoricalBar bar)
                        historicalBars.Add(bar);

                    var json = JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions);
                    await writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
                }
            }

            var asOfUtc = new DateTimeOffset(
                request.To.ToDateTime(TimeOnly.MaxValue),
                TimeSpan.Zero);
            var adjustmentPlan = await corporateActionAdjustment!
                .PrepareAsync(historicalBars, symbol, asOfUtc, ct)
                .ConfigureAwait(false);
            var snapshotReader = new JsonlReplayer(snapshotPath);
            await foreach (var evt in ApplyCorporateActionPlanAsync(
                               snapshotReader.ReadEventsAsync(ct),
                               symbol,
                               adjustmentPlan,
                               ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            if (File.Exists(snapshotPath))
                File.Delete(snapshotPath);
        }
    }

    /// <summary>
    /// Applies a prepared immutable corporate-action plan to HistoricalBar events while preserving
    /// streaming for the execution replay pass.
    /// </summary>
    internal static async IAsyncEnumerable<MarketEvent> ApplyCorporateActionPlanAsync(
        IAsyncEnumerable<MarketEvent> source,
        string symbol,
        CorporateActionAdjustmentPlan adjustmentPlan,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in source.WithCancellation(ct).ConfigureAwait(false))
        {
            if (evt.Payload is HistoricalBar bar)
                yield return evt with { Symbol = symbol, Payload = adjustmentPlan.Apply(bar) };
            else
                yield return evt;
        }
    }

    /// <summary>
    /// Compatibility wrapper for callers that explicitly use the legacy per-bar service seam.
    /// </summary>
    internal static async IAsyncEnumerable<MarketEvent> ApplyCorporateActionAdjustmentsAsync(
        IAsyncEnumerable<MarketEvent> source,
        string symbol,
        ICorporateActionAdjustmentService adjustmentService,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in source.WithCancellation(ct))
        {
            if (evt.Payload is HistoricalBar bar)
            {
                // Hot replay path: adjust one bar at a time so large mixed streams do not retain
                // MarketEvent windows while waiting for corporate-action batch flushes.
                var adjustedBar = await adjustmentService.AdjustBarAsync(bar, symbol, ct).ConfigureAwait(false);
                yield return evt with { Symbol = symbol, Payload = adjustedBar };
            }
            else
            {
                yield return evt;
            }
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
        List<PortfolioSnapshot> snapshots,
        List<CashFlowEntry> allCashFlows,
        IReadOnlyDictionary<DateOnly, List<AssetEvent>> assetEventsByDate,
        IProgress<BacktestProgressEvent>? progress,
        DateOnly requestFrom,
        int totalDays,
        long eventsProcessed,
        RollingMetricsState rollingState,
        StageTimer stageTimer,
        DelistingMonitor delistingMonitor,
        List<FillEvent> allFills,
        ILogger logger,
        FillTiming fillTiming,
        IReadOnlyDictionary<string, DateTimeOffset> lastEventTimestamps,
        CancellationToken ct)
    {
        ProcessDayEnd(fromDay, portfolio, ctx, strategy, snapshots, allCashFlows, delistingMonitor, allFills, logger, fillTiming, lastEventTimestamps, ct);

        for (var date = fromDay.AddDays(1); date <= toDay; date = date.AddDays(1))
        {
            ApplyScheduledAssetEvents(date, assetEventsByDate, portfolio, ctx);

            if (date < toDay)
                ProcessDayEnd(date, portfolio, ctx, strategy, snapshots, allCashFlows, delistingMonitor, allFills, logger, fillTiming, lastEventTimestamps, ct);

            var equity = portfolio.ComputeCurrentEquity();
            var daysElapsed = (date.ToDateTime(TimeOnly.MinValue) - requestFrom.ToDateTime(TimeOnly.MinValue)).Days;

            // Update rolling metrics state with the daily equity observation.
            rollingState.RecordDay(equity);

            // Emit intermediate metrics every 20 trading days once at least 60 have elapsed.
            IntermediateMetrics? liveMetrics = null;
            if (progress != null && rollingState.TradingDays >= 60 && rollingState.TradingDays % 20 == 0)
                liveMetrics = rollingState.Snapshot();

            progress?.Report(new BacktestProgressEvent(
                ProgressFraction: (double)daysElapsed / totalDays,
                CurrentDate: date,
                PortfolioValue: equity,
                EventsProcessed: eventsProcessed,
                Message: null,
                LiveMetrics: liveMetrics,
                Stage: stageTimer.CurrentStage,
                StageElapsed: stageTimer.StageElapsed,
                TotalElapsed: stageTimer.TotalElapsed,
                StageTelemetry: BuildStageTelemetry(stageTimer)));
        }
    }

    private static BacktestStageTelemetryDto BuildStageTelemetry(StageTimer stageTimer, string? stageMessage = null)
        => new(stageTimer.CurrentStage, stageTimer.StageElapsed, stageTimer.TotalElapsed, stageMessage);

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
        MarketEvent evt,
        IFillModel lobModel,
        IFillModel barModel,
        IFillModel marketImpactModel,
        SimulatedPortfolio portfolio,
        IBacktestStrategy strategy,
        BacktestContext ctx,
        List<FillEvent> allFills,
        ILogger<BacktestEngine> logger,
        RollingMetricsState rollingState,
        ExecutionModel requestDefault = ExecutionModel.Auto,
        FillTiming fillTiming = FillTiming.NextBar)
    {
        // Iterate a stable snapshot. Strategy fill callbacks may submit or cancel orders; every
        // mutation goes through BacktestContext and newly submitted orders are first eligible on a
        // later market event (or are excluded by the next-bar timestamp rule).
        foreach (var snapshotOrder in ctx.GetWorkingOrdersSnapshot())
        {
            if (!ctx.TryGetWorkingOrder(snapshotOrder.OrderId, out var order))
                continue;

            if (!order.Symbol.Equals(evt.EffectiveSymbol, StringComparison.OrdinalIgnoreCase))
                continue;

            // Next-bar semantics: an order may only fill against events strictly later than the
            // event that was being dispatched when it was placed. This blocks the same-bar
            // look-ahead of signalling on a bar's close and filling inside that very bar.
            if (fillTiming == FillTiming.NextBar && evt.Timestamp <= order.SubmittedAt)
                continue;

            var model = SelectFillModel(order, evt, lobModel, barModel, marketImpactModel, requestDefault);
            var result = model.TryFill(order, evt);
            var acceptedFills = new List<FillEvent>(result.Fills.Count);
            var acceptedCandidateCount = 0;
            var proposedFilledQuantity = result.Fills.Sum(static fill => fill.FilledQuantity);
            var proposalRequiresAtomicCompletion =
                !order.AllowPartialFills || order.TimeInForce == TimeInForce.FillOrKill;
            var proposalIsComplete =
                Math.Abs(proposedFilledQuantity) == order.RemainingQuantity;

            if (proposalRequiresAtomicCompletion &&
                proposalIsComplete &&
                result.Fills.Count > 0)
            {
                try
                {
                    var authoritativeFills = portfolio.ProcessFillsAtomically(result.Fills);
                    acceptedFills.AddRange(authoritativeFills);
                    acceptedCandidateCount = authoritativeFills.Count;
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogWarning(ex,
                        "Atomic non-partial fill batch rejected for order {OrderId} on {Symbol}: {Message}. No slices were accepted.",
                        order.OrderId, order.Symbol, ex.Message);
                }
            }
            else if (order.TimeInForce != TimeInForce.FillOrKill && order.AllowPartialFills)
            {
                foreach (var candidateFill in result.Fills)
                {
                    try
                    {
                        var authoritativeFill = portfolio.ProcessFill(candidateFill);
                        acceptedFills.Add(authoritativeFill);
                        acceptedCandidateCount++;
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Account rule violation (e.g. short-selling or margin disabled).
                        // Reject this fill rather than crashing the entire backtest run.
                        logger.LogWarning(ex,
                            "Fill rejected for order {OrderId} on {Symbol}: {Message}. The fill has been discarded.",
                            candidateFill.OrderId, candidateFill.Symbol, ex.Message);
                    }
                }
            }

            var acceptedFilledQuantity = acceptedFills.Sum(static fill => fill.FilledQuantity);
            var updatedOrder = BuildAuthoritativeOrderState(
                order,
                result.UpdatedOrder,
                acceptedFilledQuantity,
                result.Fills.Count);
            updatedOrder = ApplyTimeInForceTerminalState(order, updatedOrder);
            var allProposedFillsAccepted = acceptedCandidateCount == result.Fills.Count;
            var removeOrder = ShouldRemoveOrder(
                order,
                result,
                updatedOrder,
                allProposedFillsAccepted);

            if (removeOrder)
                ctx.RemoveWorkingOrder(order.OrderId);
            else
                ctx.UpdateWorkingOrder(updatedOrder);

            foreach (var fill in acceptedFills)
            {
                ContingentOrderManager.ReconcileOcoSiblings(ctx, order, fill);
                ctx.AddWorkingOrders(ContingentOrderManager.CreateContingentOrders(order, fill));

                allFills.Add(fill);
                rollingState.IncrementFills();
            }

            // All authoritative fills and their contingent exposure are reconciled before the
            // first callback. A strategy can therefore cancel the parent or all of its children
            // from any fill callback without later slices in the same fill result recreating them.
            foreach (var fill in acceptedFills)
            {
                strategy.OnOrderFill(fill, ctx);
            }
        }
    }

    private static Order BuildAuthoritativeOrderState(
        Order originalOrder,
        Order modelUpdatedOrder,
        long acceptedFilledQuantity,
        int proposedFillCount)
    {
        // When the fill model did not propose a fill, its updated state is authoritative for
        // lifecycle-only transitions such as a stop trigger or an IOC/FOK cancellation.
        if (proposedFillCount == 0)
            return modelUpdatedOrder;

        var filledQuantity = originalOrder.FilledQuantity + acceptedFilledQuantity;
        var remainingQuantity = Math.Max(0L, Math.Abs(modelUpdatedOrder.Quantity) - Math.Abs(filledQuantity));
        var status = modelUpdatedOrder.Status switch
        {
            OrderStatus.Cancelled or OrderStatus.Expired or OrderStatus.Rejected => modelUpdatedOrder.Status,
            _ when remainingQuantity == 0 => OrderStatus.Filled,
            _ when filledQuantity != 0 => OrderStatus.PartiallyFilled,
            _ => originalOrder.Status
        };

        return modelUpdatedOrder with
        {
            FilledQuantity = filledQuantity,
            Status = status
        };
    }

    private static bool ShouldRemoveOrder(
        Order originalOrder,
        OrderFillResult result,
        Order updatedOrder,
        bool allProposedFillsAccepted)
    {
        if (updatedOrder.Status is OrderStatus.Cancelled or OrderStatus.Expired or OrderStatus.Rejected)
            return true;
        if (updatedOrder.IsComplete)
            return true;
        if (originalOrder.TimeInForce is TimeInForce.ImmediateOrCancel or TimeInForce.FillOrKill)
            return true;
        if (!result.RemoveOrder)
            return false;

        // For ordinary orders, a model's "complete" removal cannot be applied when the portfolio
        // rejected any proposed fill; the accepted remainder must stay working.
        return allProposedFillsAccepted;
    }

    private static Order ApplyTimeInForceTerminalState(Order originalOrder, Order updatedOrder)
    {
        if (originalOrder.TimeInForce is not (TimeInForce.ImmediateOrCancel or TimeInForce.FillOrKill))
            return updatedOrder;
        if (updatedOrder.IsComplete || updatedOrder.Status is OrderStatus.Rejected or OrderStatus.Expired)
            return updatedOrder;

        return updatedOrder with { Status = OrderStatus.Cancelled };
    }

    private static void ProcessDayEnd(
        DateOnly date,
        SimulatedPortfolio portfolio,
        BacktestContext ctx,
        IBacktestStrategy strategy,
        List<PortfolioSnapshot> snapshots,
        List<CashFlowEntry> allCashFlows,
        DelistingMonitor delistingMonitor,
        List<FillEvent> allFills,
        ILogger logger,
        FillTiming fillTiming,
        IReadOnlyDictionary<string, DateTimeOffset> lastEventTimestamps,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var ordersAtStartOfDayEnd = ctx.GetWorkingOrdersSnapshot();
        portfolio.AccrueDailyInterest(date);
        ctx.CurrentDate = date;
        strategy.OnDayEnd(date, ctx);

        // Delisting sweep runs before the snapshot so forced liquidations are reflected in the
        // day's equity instead of carrying a stale mark forward.
        delistingMonitor.ProcessDayEnd(date, portfolio, ctx, allFills, logger);

        foreach (var orderAtStart in ordersAtStartOfDayEnd)
        {
            if (!ctx.TryGetWorkingOrder(orderAtStart.OrderId, out var order))
                continue;
            if (order.TimeInForce != TimeInForce.Day)
                continue;

            // Under next-bar timing a Day order signalled on day N is intended for the next
            // session, so it only expires at the end of the first day on which its symbol traded
            // after submission — otherwise a Day order on daily bars could never fill at all.
            if (fillTiming == FillTiming.NextBar)
            {
                var hadEligibleEventToday =
                    lastEventTimestamps.TryGetValue(order.Symbol, out var lastEventAt) &&
                    DateOnly.FromDateTime(lastEventAt.UtcDateTime) == date &&
                    lastEventAt > order.SubmittedAt;

                if (!hadEligibleEventToday)
                    continue;
            }

            ctx.RemoveWorkingOrder(order.OrderId);
        }

        var ts = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var snapshot = portfolio.TakeSnapshot(ts, date);
        snapshots.Add(snapshot);
        allCashFlows.AddRange(snapshot.DayCashFlows);
    }

    private BacktestResult CreateEmptyResult(BacktestRequest request, IReadOnlySet<string> universe, TimeSpan elapsed)
    {
        var metrics = BacktestMetricsEngine.Compute([], [], [], request);
        var biasDisclosure = BuildBiasDisclosure(
            request,
            corporateActionsApplied: request.AdjustForCorporateActions && corporateActionAdjustment is not null,
            missingSecurityMasterSymbols: [],
            inactiveSecurityMasterSymbols: [],
            delistingMonitor: null);
        return new BacktestResult(request, universe, [], [], [], metrics, new BacktestLedger(), elapsed, 0, [], BiasDisclosure: biasDisclosure);
    }

    /// <summary>
    /// Builds the honest-assumptions report attached to every result: which execution semantics the
    /// run used, where the universe came from, and every detected data-quality issue that could
    /// flatter the numbers. Items are ordered most severe first.
    /// </summary>
    private static BiasDisclosureReport BuildBiasDisclosure(
        BacktestRequest request,
        bool corporateActionsApplied,
        IReadOnlyList<string> missingSecurityMasterSymbols,
        IReadOnlyList<string> inactiveSecurityMasterSymbols,
        DelistingMonitor? delistingMonitor)
    {
        var items = new List<BiasDisclosureItem>();
        var universeIsExplicit = request.Symbols is { Count: > 0 };
        var delistingLiquidations = delistingMonitor?.Liquidations ?? [];

        items.Add(request.FillTiming == FillTiming.SameBar
            ? new BiasDisclosureItem(
                "fill-timing", BiasSeverity.Warning, "Same-bar execution",
                "Orders fill against the same bar that generated the signal. A strategy reacting to a bar's close can trade inside that bar, which is impossible live — results may embed look-ahead bias.")
            : new BiasDisclosureItem(
                "fill-timing", BiasSeverity.Info, "Next-bar execution",
                "Orders placed in reaction to an event are eligible to fill no earlier than the symbol's next event, eliminating same-bar look-ahead."));

        items.Add(request.FillConservatism == FillConservatism.Optimistic
            ? new BiasDisclosureItem(
                "fill-conservatism", BiasSeverity.Warning, "Optimistic limit/stop fills",
                "Limit orders fill on a bare touch of the limit price and triggered stops execute at the bar midpoint, which can beat the stop. Fill rates and prices are flattered relative to live trading.")
            : new BiasDisclosureItem(
                "fill-conservatism", BiasSeverity.Info, "Conservative limit/stop fills",
                "Limit orders require the bar to trade through the limit (a bare touch does not fill) and stop fills are anchored to the worse of the stop and the open, so simulated executions cannot beat prices achievable live."));

        items.Add(universeIsExplicit
            ? new BiasDisclosureItem(
                "universe", BiasSeverity.Caution, "Universe fixed by the caller",
                "The symbol list was supplied explicitly. If it was chosen with knowledge of later performance (e.g. today's index members or known winners), the run embeds selection/survivorship bias.")
            : new BiasDisclosureItem(
                "universe", BiasSeverity.Warning, "Universe discovered from local data",
                "The tradeable universe is whatever has data on disk for the period. Unless the dataset includes delisted and acquired names, losers are silently excluded and the run has survivorship bias."));

        if (!request.AdjustForCorporateActions)
        {
            items.Add(new BiasDisclosureItem(
                "corporate-actions", BiasSeverity.Warning, "Corporate-action adjustment disabled",
                "Bar prices are not adjusted for splits or dividends; price jumps around corporate actions will distort signals and returns."));
        }
        else if (!corporateActionsApplied)
        {
            items.Add(new BiasDisclosureItem(
                "corporate-actions", BiasSeverity.Warning, "Corporate-action adjustment unavailable",
                "Adjustment was requested but no corporate-action adjustment service is configured, so prices replayed unadjusted."));
        }
        else
        {
            items.Add(new BiasDisclosureItem(
                "corporate-actions", BiasSeverity.Info, "Corporate-action adjusted prices",
                "Bar prices were split/dividend adjusted during replay using Security Master data."));
        }

        if (missingSecurityMasterSymbols.Count > 0)
        {
            items.Add(new BiasDisclosureItem(
                "security-master-missing", BiasSeverity.Caution, $"{missingSecurityMasterSymbols.Count} symbol(s) missing from Security Master",
                $"No Security Master record for: {string.Join(", ", missingSecurityMasterSymbols.Take(10))}{(missingSecurityMasterSymbols.Count > 10 ? ", …" : string.Empty)}. Corporate-action adjustment and tick-size resolution were unavailable for these symbols."));
        }

        if (inactiveSecurityMasterSymbols.Count > 0)
        {
            items.Add(new BiasDisclosureItem(
                "security-master-inactive", BiasSeverity.Caution, $"{inactiveSecurityMasterSymbols.Count} inactive symbol(s) in universe",
                $"Marked Inactive in the Security Master (delisted or renamed): {string.Join(", ", inactiveSecurityMasterSymbols.Take(10))}{(inactiveSecurityMasterSymbols.Count > 10 ? ", …" : string.Empty)}."));
        }

        if (request.DelistingPolicy == DelistingPolicy.Hold)
        {
            items.Add(new BiasDisclosureItem(
                "delisting-policy", BiasSeverity.Warning, "Delisted positions held at stale marks",
                "Positions in symbols whose data ends mid-run stay open and marked at the last observed price, overstating equity for delisted names."));
        }

        if (delistingLiquidations.Count > 0)
        {
            var symbols = delistingLiquidations.Select(static l => l.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            items.Add(new BiasDisclosureItem(
                "delisting-liquidations", BiasSeverity.Caution, $"{symbols.Count} position(s) force-liquidated on data end",
                $"Data ended before the backtest range did for: {string.Join(", ", symbols.Take(10))}{(symbols.Count > 10 ? ", …" : string.Empty)}. Positions were closed at the last observed price (haircut {request.DelistingHaircutPercent:P0}); actual delisting proceeds may differ."));
        }

        if (delistingMonitor is { FailedLiquidationSymbols.Count: > 0 })
        {
            items.Add(new BiasDisclosureItem(
                "delisting-liquidation-failed", BiasSeverity.Warning, "Delisting liquidation rejected by account rules",
                $"Forced liquidation failed for: {string.Join(", ", delistingMonitor.FailedLiquidationSymbols.Distinct(StringComparer.OrdinalIgnoreCase))}. These positions remain open at stale marks."));
        }

        items.Add(new BiasDisclosureItem(
            "in-sample", BiasSeverity.Caution, "Single-period result — not validated out-of-sample",
            "This is one simulation over the full requested period. If parameters were tuned on the same period, the result is in-sample; use the walk-forward harness for out-of-sample evidence."));

        return new BiasDisclosureReport(
            request.FillTiming,
            request.FillConservatism,
            request.DelistingPolicy,
            universeIsExplicit ? BiasDisclosureReport.UniverseSourceExplicit : BiasDisclosureReport.UniverseSourceDiscovered,
            corporateActionsApplied,
            missingSecurityMasterSymbols,
            inactiveSecurityMasterSymbols,
            delistingLiquidations,
            items.OrderByDescending(static item => item.Severity).ToList());
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

    internal static IReadOnlyList<TradeTicket> BuildTradeTickets(IReadOnlyList<CashFlowEntry> cashFlows)
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
                        assetEvent.CashPerShare,
                        assetEvent.AccountId));
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
    /// Returns the lists of missing and Inactive symbols for the bias-disclosure report.
    /// </summary>
    private async Task<(IReadOnlyList<string> Missing, IReadOnlyList<string> Inactive)> PreResolveUniverseAsync(
        IReadOnlySet<string> universe,
        BacktestRequest request,
        CancellationToken ct)
    {
        if (securityMasterQueryService is null || universe.Count == 0)
            return ([], []);

        var missing = new List<string>();
        var inactive = new List<string>();

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
                    inactive.Add(symbol);
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

        return (missing, inactive);
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

/// <summary>
/// Mutable state bag for computing O(1) rolling Sharpe ratio and drawdown during backtest replay.
/// Not thread-safe; updated exclusively on the engine thread.
/// </summary>
internal sealed class RollingMetricsState
{
    private decimal _prevEquity;
    private decimal _peakEquity;
    private double _sumExcess;
    private double _sumSqExcess;

    public int TradingDays { get; private set; }
    public int FillCount { get; private set; }

    public RollingMetricsState(decimal initialEquity)
    {
        _prevEquity = initialEquity;
        _peakEquity = initialEquity;
    }

    /// <summary>Records one day's equity observation and updates running statistics.</summary>
    public void RecordDay(decimal equity)
    {
        TradingDays++;

        if (_prevEquity > 0)
        {
            var dailyReturn = (double)((equity - _prevEquity) / _prevEquity);
            // excess return vs risk-free 0
            _sumExcess += dailyReturn;
            _sumSqExcess += dailyReturn * dailyReturn;
        }

        if (equity > _peakEquity)
            _peakEquity = equity;

        _prevEquity = equity;
    }

    public void IncrementFills(int count = 1) => FillCount += count;

    /// <summary>Computes a snapshot of current rolling metrics.</summary>
    public IntermediateMetrics Snapshot()
    {
        var n = TradingDays;
        double sharpe = 0;
        if (n >= 2)
        {
            var mean = _sumExcess / n;
            var variance = (_sumSqExcess / n) - (mean * mean);
            var stdDev = variance > 0 ? Math.Sqrt(variance) : 0;
            sharpe = stdDev > 0 ? mean / stdDev * Math.Sqrt(365) : 0;
        }

        var drawdownPct = _peakEquity > 0
            ? (double)((_peakEquity - _prevEquity) / _peakEquity)
            : 0;

        return new IntermediateMetrics(sharpe, drawdownPct, FillCount, n);
    }
}
