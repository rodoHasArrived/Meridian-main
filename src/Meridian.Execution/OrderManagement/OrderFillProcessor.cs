using System.Collections.Concurrent;
using Meridian.Execution.Events;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;
using ExecutionReportDeliveryException = Meridian.Execution.OrderManagementSystem.ExecutionReportDeliveryException;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Owns fill progress and exposure until portfolio application, then completes accounting,
/// session history and report delivery in order. Accepted fills are never cancelled mid-handoff.
/// </summary>
internal sealed class OrderFillProcessor(
    OrderFillContext context,
    IPortfolioState? portfolioState,
    PaperSessionPersistenceService? sessionPersistence,
    AccountingHandoffCoordinator accounting,
    ExecutionReportHub executionReports,
    OrderExecutionAudit audit,
    ILogger logger)
{
    private readonly OrderFillContext _context = context;
    private readonly IPortfolioState? _portfolioState = portfolioState;
    private readonly PaperSessionPersistenceService? _sessionPersistence = sessionPersistence;
    private readonly AccountingHandoffCoordinator _accounting = accounting;
    private readonly ExecutionReportHub _executionReports = executionReports;
    private readonly OrderExecutionAudit _audit = audit;
    private readonly ILogger _logger = logger;
    private readonly ConcurrentDictionary<ExecutionReport, FillProcessingProgress> _fillProcessing = new();
    private readonly ConcurrentDictionary<ExecutionReport, ExecutionReport> _pendingFillReservations = new();
    private readonly ConcurrentQueue<ExecutionReport> _completedFillReportOrder = new();
    private const int MaxTrackedFillReports = 4096;

    internal IReadOnlyList<OrderState> GetExposureReservations()
    {
        var reserving = new List<OrderState>();
        // A fill flips the tracked order terminal before OrderFillProcessor.ProcessAsync applies it
        // to the portfolio. Reports whose precise increment is not tracked yet reserve
        // their whole reported fill; once tracked, the increment below supersedes them.
        foreach (var pending in _pendingFillReservations.Values)
        {
            if (_fillProcessing.ContainsKey(pending) || pending.FilledQuantity <= 0m)
            {
                continue;
            }

            reserving.Add(BuildHandoffReservation(
                pending.ClientOrderId ?? pending.OrderId,
                pending.Symbol,
                pending.Side,
                pending.FilledQuantity,
                pending.FillPrice,
                pending.Timestamp));
        }

        // During that window the exposure exists at the broker but sits in neither book,
        // so surface the un-applied increment as a reservation.
        foreach (var progress in _fillProcessing.Values)
        {
            if (progress.PortfolioApplied)
            {
                continue;
            }

            var increment = progress.FillIncrement;
            if (increment.FilledQuantity <= 0m)
            {
                continue;
            }

            reserving.Add(BuildHandoffReservation(
                increment.ClientOrderId ?? increment.OrderId,
                increment.Symbol,
                increment.Side,
                increment.FilledQuantity,
                increment.FillPrice,
                increment.Timestamp));
        }

        return reserving;
    }

    internal void ReservePending(ExecutionReport report) => _pendingFillReservations[report] = report;
    internal void ReleasePending(ExecutionReport report) => _pendingFillReservations.TryRemove(report, out _);

    internal HashSet<string> GetIncompleteOrderIds() => _pendingFillReservations.Keys
        .Concat(_fillProcessing.Where(static entry => !entry.Value.IsComplete).Select(static entry => entry.Key))
        .Select(static report => report.ClientOrderId ?? report.OrderId)
        .Where(static id => !string.IsNullOrWhiteSpace(id))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private OrderState BuildHandoffReservation(
        string orderId,
        string symbol,
        OrderSide side,
        decimal filledQuantity,
        decimal? fillPrice,
        DateTimeOffset timestamp)
    {
        _context.TryGetOrder(orderId, out var tracked);
        return new OrderState
        {
            OrderId = orderId,
            Symbol = symbol,
            Side = side,
            Type = OrderType.Market,
            Quantity = filledQuantity,
            LimitPrice = fillPrice,
            Status = OrderStatus.PendingNew,
            CreatedAt = timestamp,
            FundAccountId = tracked?.FundAccountId,
            StrategyId = tracked?.StrategyId,
            RunId = tracked?.RunId,
            ContractMultiplier = tracked?.ContractMultiplier ?? 1m,
            UsesFaceValuePercentageOfPar = tracked?.UsesFaceValuePercentageOfPar ?? false,
            OptionContract = tracked?.OptionContract,
            Legs = tracked?.Legs
        };
    }

    internal async Task ProcessAsync(
        string? sessionId,
        ExecutionReport report,
        decimal previousFilledQuantity)
    {
        var orderId = report.ClientOrderId ?? report.OrderId;
        if (!_fillProcessing.TryGetValue(report, out var progress))
        {
            // Gateways report FilledQuantity cumulatively (e.g. IB CumulativeQuantity,
            // Alpaca filled_qty) while fill consumers treat each report as a discrete
            // trade. The locally accepted quantity is authoritative when an amended
            // order caps a broker report above the accepted order quantity.
            var acceptedFilledQuantity = report.FilledQuantity;
            if (!string.IsNullOrWhiteSpace(orderId)
                && _context.TryGetOrder(orderId, out var currentOrder))
            {
                acceptedFilledQuantity = currentOrder.FilledQuantity;
            }

            var incrementQuantity = acceptedFilledQuantity - previousFilledQuantity;
            if (incrementQuantity <= 0m)
                return;

            var fillIncrement = incrementQuantity == report.FilledQuantity
                ? report
                : report with { FilledQuantity = incrementQuantity };
            if (fillIncrement.ReportType is not (ExecutionReportType.Fill or ExecutionReportType.PartialFill))
            {
                var isCompleteFill = report.OrderQuantity > 0m
                    && acceptedFilledQuantity >= report.OrderQuantity;
                fillIncrement = fillIncrement with
                {
                    ReportType = isCompleteFill ? ExecutionReportType.Fill : ExecutionReportType.PartialFill,
                    OrderStatus = isCompleteFill ? OrderStatus.Filled : OrderStatus.PartiallyFilled
                };
            }

            // Stamp the gateway-resolved sizing semantics onto the increment itself: the paper
            // book, the accounting event, and the durable session record all consume this one
            // report, and the session record is replayed after a restart when the sidecar
            // dictionary no longer exists. The flag is not part of the canonical fill identity,
            // so a replayed broker report still resolves to the same FillId.
            if (!string.IsNullOrWhiteSpace(orderId)
                && _context.UsesFaceValueSizing(orderId)
                && !fillIncrement.UsesFaceValuePercentageOfPar)
            {
                fillIncrement = fillIncrement with { UsesFaceValuePercentageOfPar = true };
            }

            progress = _fillProcessing.GetOrAdd(
                report,
                _ => new FillProcessingProgress(
                    fillIncrement,
                    acceptedFilledQuantity,
                    !string.IsNullOrWhiteSpace(orderId) && _context.ContainsOrder(orderId)));
        }

        // A broker-accepted fill may not be abandoned because the caller or report-pump token
        // was cancelled after dequeue. Every post-accept side effect in this funnel is therefore
        // non-cancellable; shutdown awaits the admitted operation/report instead of truncating it.
        await progress.Gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (progress.IsComplete)
                return;

            var fillIncrement = progress.FillIncrement;

            // Gateway-resolved sizing semantics for this order: quantity routed as face value,
            // price quoted as a percentage of par. Stamped onto the increment when its
            // processing state was created, so the paper book, the accounting event, and the
            // durable session record all read the same classification.
            var usesFaceValuePercentageOfPar = fillIncrement.UsesFaceValuePercentageOfPar;

            if (!progress.PortfolioApplied)
            {
                var realisedPnlBefore = _portfolioState?.RealisedPnl ?? 0m;

                // Only fills for orders this OMS placed may mutate the paper portfolio;
                // stream reports for external/untracked orders are still published below.
                if (_portfolioState is PaperTradingPortfolio paperPortfolio
                    && progress.IsTrackedOrder)
                {
                    // The fill carries which fund owns it and, for a derivative, what one
                    // contract is worth. Without both, the shared execution book reports a
                    // position no fund can be shown to own and an option position measured
                    // as if each contract were a single share.
                    var fillOrderId = fillIncrement.ClientOrderId ?? fillIncrement.OrderId;
                    paperPortfolio.ApplyFill(
                        fillIncrement,
                        ownerAccountId: _context.GetFinancialAccountId(fillOrderId),
                        contractMultiplier: _context.GetContractMultiplier(fillOrderId),
                        usesFaceValuePercentageOfPar: usesFaceValuePercentageOfPar);
                    progress.RealizedPnl = paperPortfolio.RealisedPnl - realisedPnlBefore;
                }

                progress.NewCash = _portfolioState?.Cash ?? 0m;
                progress.PortfolioApplied = true;
            }

            if (!progress.TradeEventPublished)
            {
                // Materialise the deterministic identity even when no accounting publisher is
                // configured. Paper-session durability uses the same FillId as the accounting
                // handoff rather than inventing a second replay identity.
                if (progress.IsTrackedOrder
                    && (_accounting.HasPublisher || !string.IsNullOrWhiteSpace(sessionId)))
                {
                    progress.TradeEvent ??= CreateTradeExecutedEvent(
                        fillIncrement,
                        progress.CumulativeFilledQuantity,
                        progress.RealizedPnl,
                        progress.NewCash,
                        ResolveFinancialAccountId(orderId),
                        usesFaceValuePercentageOfPar);
                }

                if (_accounting.HasPublisher && progress.IsTrackedOrder)
                {
                    await _accounting.PublishAsync(progress.TradeEvent!).ConfigureAwait(false);
                }

                progress.TradeEventPublished = true;
            }

            if (!progress.SessionRecorded)
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    progress.SessionRecorded = true;
                }

                try
                {
                    if (!progress.SessionRecorded)
                    {
                        await RecordSessionFillAsync(
                                sessionId,
                                progress.TradeEvent!.FillId,
                                fillIncrement,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        progress.SessionRecorded = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(
                        ex,
                        "Accounting accepted fill {FillId}, but paper-session fill history could not be recorded; fill completion remains unacknowledged and will fail closed",
                        progress.TradeEvent?.FillId);
                    throw;
                }
            }

            await _executionReports.PublishAsync(
                fillIncrement, progress.Delivery, progress.TradeEvent?.FillId).ConfigureAwait(false);

            if (progress.SessionRecorded && progress.Delivery.IsComplete)
            {
                progress.IsComplete = true;
                TrackCompletedFill(report);
            }
        }
        catch (AccountingHandoffException)
        {
            throw;
        }
        catch (ExecutionReportDeliveryException)
        {
            // The subscription already attempted its explicit durable recovery/fail-closed seam.
            // Do not relabel a strategy-delivery failure as an accounting-publisher failure.
            throw;
        }
        catch (Exception ex)
        {
            _context.TryGetOrder(orderId ?? string.Empty, out var state);
            var evidenceRetained = await _audit.RetainUnresolvedFillAsync(
                    orderId,
                    state,
                    report,
                    ex)
                .ConfigureAwait(false);
            throw new AccountingHandoffException(orderId ?? report.OrderId, evidenceRetained, ex);
        }
        finally
        {
            progress.Gate.Release();
        }
    }

    private void TrackCompletedFill(ExecutionReport report)
    {
        _completedFillReportOrder.Enqueue(report);
        while (_completedFillReportOrder.Count > MaxTrackedFillReports
            && _completedFillReportOrder.TryDequeue(out var oldest))
        {
            if (_fillProcessing.TryGetValue(oldest, out var progress) && progress.IsComplete)
                _fillProcessing.TryRemove(oldest, out _);
        }
    }

    private string? ResolveFinancialAccountId(string? orderId)
        => !string.IsNullOrWhiteSpace(orderId)
            ? _context.GetFinancialAccountId(orderId)
            : null;

    private async Task RecordSessionFillAsync(
        string? sessionId,
        Guid fillId,
        ExecutionReport report,
        CancellationToken ct)
    {
        if (_sessionPersistence is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await _sessionPersistence.RecordFillAsync(sessionId, fillId, report, ct).ConfigureAwait(false);
    }

    private sealed class FillProcessingProgress(
        ExecutionReport fillIncrement,
        decimal cumulativeFilledQuantity,
        bool isTrackedOrder)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public ExecutionReport FillIncrement { get; } = fillIncrement;
        public decimal CumulativeFilledQuantity { get; } = cumulativeFilledQuantity;
        public bool IsTrackedOrder { get; } = isTrackedOrder;
        public TradeExecutedEvent? TradeEvent { get; set; }
        public decimal RealizedPnl { get; set; }
        public decimal NewCash { get; set; }
        public bool PortfolioApplied { get; set; }
        public bool TradeEventPublished { get; set; }
        public bool SessionRecorded { get; set; }
        public ExecutionReportHub.DeliveryCheckpoint Delivery { get; } = new();
        public volatile bool IsComplete;
    }

    private static TradeExecutedEvent CreateTradeExecutedEvent(
        ExecutionReport fillIncrement,
        decimal cumulativeFilledQuantity,
        decimal realizedPnl,
        decimal newCash,
        string? financialAccountId,
        bool usesFaceValuePercentageOfPar)
    {
        if (fillIncrement.FillPrice is not { } fillPrice)
        {
            throw new InvalidOperationException(
                $"Fill report '{fillIncrement.OrderId}' for '{fillIncrement.Symbol}' has no execution price.");
        }

        var fillId = CreateDeterministicFillId(
            fillIncrement,
            cumulativeFilledQuantity,
            financialAccountId);

        return new TradeExecutedEvent(
            fillId,
            fillIncrement.ClientOrderId ?? fillIncrement.OrderId,
            fillIncrement.Symbol,
            fillIncrement.Side,
            fillIncrement.FilledQuantity,
            fillPrice,
            fillIncrement.Commission ?? 0m,
            realizedPnl,
            newCash,
            fillIncrement.Timestamp,
            financialAccountId,
            usesFaceValuePercentageOfPar);
    }

    /// <summary>
    /// Derives the canonical OMS fill identity without requiring an accounting publisher.
    /// Paper-session persistence uses this same identity so retries and restart replay share
    /// the accounting handoff's idempotency key.
    /// </summary>
    private static Guid CreateDeterministicFillId(
        ExecutionReport fillIncrement,
        decimal cumulativeFilledQuantity,
        string? financialAccountId)
    {
        // PaperSessionFillRecord is the one canonical identity authority. Cumulative portfolio
        // state and account scope are deliberately compatibility-only inputs here: including
        // either would give the same broker fill different ids across producer paths or retries.
        _ = cumulativeFilledQuantity;
        _ = financialAccountId;
        return PaperSessionFillRecord.ComputeCanonicalFillId(fillIncrement);
    }
}
