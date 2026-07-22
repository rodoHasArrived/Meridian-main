using System.Threading.Channels;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution.Live;
using Meridian.Execution.Services;
using ExecutionSdk = Meridian.Execution.Sdk;
using OrderType = Meridian.Backtesting.Sdk.OrderType;
using TimeInForce = Meridian.Backtesting.Sdk.TimeInForce;

namespace Meridian.Strategies.Live;

/// <summary>
/// Executes one promoted paper/live run: consumes the live market event stream, drives the
/// strategy callbacks in event order (the live analogue of <c>BacktestEngine</c>'s replay
/// loop), routes queued orders through the governed order manager, and feeds execution-report
/// fills back into <c>IBacktestStrategy.OnOrderFill</c>. Terminal run state and summary
/// metrics are durably recorded on the promoted run entry.
/// </summary>
internal sealed class LiveStrategyRunSession
{
    private const string ActorId = "live-trading-engine";

    /// <summary>Client-order-id prefix marker for orders originated by the live engine.</summary>
    internal const string ClientOrderIdPrefix = "mlt";

    private readonly StrategyRunEntry _run;
    private readonly ILiveStrategy _strategy;
    private readonly LiveStrategyExecutionContext _context;
    private readonly ILiveMarketEventFeed _feed;
    private readonly ExecutionSdk.IOrderManager _orderManager;
    private readonly IStrategyRepository _repository;
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly ILogger _logger;
    private readonly bool _useSynchronousFillFallback;
    private readonly Channel<ExecutionSdk.ExecutionReport> _fillReports;
    private readonly CancellationTokenSource _stopRequested = new();
    private readonly Dictionary<string, Order> _ordersByClientId = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _clientIdsByOrderId = new();
    private readonly LiveRunMetricsTracker _metrics;
    private readonly string _clientOrderIdPrefix;

    private DateOnly? _currentDate;
    private volatile bool _completeRunOnExit = true;

    public LiveStrategyRunSession(
        StrategyRunEntry run,
        ILiveStrategy strategy,
        LiveStrategyExecutionContext context,
        ILiveMarketEventFeed feed,
        ExecutionSdk.IOrderManager orderManager,
        IStrategyRepository repository,
        ExecutionAuditTrailService? auditTrail,
        ILogger logger,
        bool useSynchronousFillFallback,
        int fillReportQueueCapacity)
    {
        _run = run;
        _strategy = strategy;
        _context = context;
        _feed = feed;
        _orderManager = orderManager;
        _repository = repository;
        _auditTrail = auditTrail;
        _logger = logger;
        _useSynchronousFillFallback = useSynchronousFillFallback;
        _metrics = new LiveRunMetricsTracker(context.PortfolioValue, DateTimeOffset.UtcNow);
        _clientOrderIdPrefix = BuildClientOrderIdPrefix(run.RunId);
        _fillReports = Channel.CreateBounded<ExecutionSdk.ExecutionReport>(new BoundedChannelOptions(fillReportQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public string RunId => _run.RunId;

    public string StrategyId => _run.StrategyId;

    internal static string BuildClientOrderIdPrefix(string runId) => $"{ClientOrderIdPrefix}-{runId}-";

    /// <summary>Extracts the run id from an engine-originated client order id, if it is one.</summary>
    internal static bool TryParseRunId(string? clientOrderId, out string runId)
    {
        runId = string.Empty;
        if (string.IsNullOrEmpty(clientOrderId)
            || !clientOrderId.StartsWith(ClientOrderIdPrefix + "-", StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = clientOrderId.AsSpan(ClientOrderIdPrefix.Length + 1);
        var separatorIndex = remainder.LastIndexOf('-');
        if (separatorIndex <= 0)
        {
            return false;
        }

        runId = remainder[..separatorIndex].ToString();
        return true;
    }

    /// <summary>Queues an execution report for processing on the session's event loop.</summary>
    public void EnqueueExecutionReport(ExecutionSdk.ExecutionReport report)
    {
        if (!_fillReports.Writer.TryWrite(report))
        {
            _logger.LogError(
                "Run {RunId} could not queue execution report for order {OrderId}; the session inbox is full or completed.",
                _run.RunId,
                report.OrderId);
        }
    }

    /// <summary>
    /// Requests the session to stop. When <paramref name="completeRun"/> is false (host
    /// shutdown), the run entry is left open so a restarted host can resume it.
    /// </summary>
    public void RequestStop(bool completeRun)
    {
        _completeRunOnExit = completeRun;
        _stopRequested.Cancel();
    }

    /// <summary>Runs the session to completion. Never throws for operational failures.</summary>
    public async Task ExecuteAsync(CancellationToken engineToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(engineToken, _stopRequested.Token);
        var ct = linked.Token;
        try
        {
            await _strategy.StartAsync(_context, ct).ConfigureAwait(false);
            _strategy.Initialize(_context);
            await _repository.RecordLifecycleEventAsync(
                _run.Started(ActorId, "Run activated on the live trading engine."),
                StrategyRunLifecycleEventType.Started,
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Live session started: run {RunId}, strategy {StrategyId} ({StrategyName}), mode {RunType}, universe [{Universe}]",
                _run.RunId, _run.StrategyId, _strategy.Name, _run.RunType, string.Join(", ", _context.Universe));

            await RunEventLoopAsync(ct).ConfigureAwait(false);
            await FinishAsync(faulted: null).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopRequested.IsCancellationRequested || engineToken.IsCancellationRequested)
        {
            await FinishAsync(faulted: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live session for run {RunId} failed.", _run.RunId);
            await FinishAsync(faulted: ex).ConfigureAwait(false);
        }
    }

    private async Task RunEventLoopAsync(CancellationToken ct)
    {
        await using var events = _feed.SubscribeAsync(_context.Universe, ct).GetAsyncEnumerator(ct);
        Task<bool>? moveNext = null;
        Task<bool>? fillReady = null;
        var fillChannelCompleted = false;
        try
        {
            while (!ct.IsCancellationRequested && _strategy.Status != StrategyStatus.Stopped)
            {
                moveNext ??= events.MoveNextAsync().AsTask();
                // Track the pending wait across iterations: the fill channel is
                // SingleReader, so only one WaitToReadAsync may ever be in flight.
                if (!fillChannelCompleted)
                {
                    fillReady ??= _fillReports.Reader.WaitToReadAsync(ct).AsTask();
                }

                if (fillReady is null)
                {
                    await moveNext.ConfigureAwait(false);
                }
                else
                {
                    await Task.WhenAny(moveNext, fillReady).ConfigureAwait(false);
                    if (fillReady.IsCompleted)
                    {
                        fillChannelCompleted = !await fillReady.ConfigureAwait(false);
                        fillReady = null;
                        while (_fillReports.Reader.TryRead(out var report))
                        {
                            HandleExecutionReport(report);
                        }
                    }
                }

                if (!moveNext.IsCompleted)
                {
                    continue;
                }

                var hasEvent = await moveNext.ConfigureAwait(false);
                moveNext = null;
                if (!hasEvent)
                {
                    break;
                }

                await ProcessMarketEventAsync(events.Current, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // The enumerator cannot be disposed while a MoveNextAsync is still pending;
            // cancellation flows into the feed subscription, so the pending advance
            // completes (or faults) promptly once the linked token fires.
            if (moveNext is not null)
            {
                try
                {
                    await moveNext.ConfigureAwait(false);
                }
                catch
                {
                    // Cancellation/teardown of the pending advance is expected here.
                }
            }

            if (fillReady is not null)
            {
                try
                {
                    await fillReady.ConfigureAwait(false);
                }
                catch
                {
                    // Cancellation of the pending fill wait is expected here.
                }
            }
        }
    }

    private async Task ProcessMarketEventAsync(LiveMarketEvent evt, CancellationToken ct)
    {
        _metrics.EventsProcessed++;

        var eventDate = DateOnly.FromDateTime(evt.Timestamp.UtcDateTime);
        if (_currentDate is { } currentDate && eventDate > currentDate)
        {
            CloseDay(currentDate);
        }

        _currentDate ??= eventDate;
        if (eventDate > _currentDate.Value)
        {
            _currentDate = eventDate;
        }

        _context.Advance(evt.Timestamp);

        // Pause gates market-event processing but never fill processing: fills belong to
        // orders that already reached the broker and the strategy state must stay coherent.
        if (_strategy.Status != StrategyStatus.Running)
        {
            return;
        }

        switch (evt.Payload)
        {
            case Trade trade:
                _strategy.OnTrade(trade, _context);
                break;
            case BboQuotePayload quote:
                _strategy.OnQuote(quote, _context);
                break;
            case HistoricalBar bar:
                _strategy.OnBar(bar, _context);
                break;
            case LOBSnapshot orderBook:
                _strategy.OnOrderBook(orderBook, _context);
                break;
            default:
                return;
        }

        await RouteQueuedOrdersAsync(ct).ConfigureAwait(false);
    }

    private void CloseDay(DateOnly date)
    {
        if (_strategy.Status == StrategyStatus.Running)
        {
            _strategy.OnDayEnd(date, _context);
        }

        _metrics.RecordDayEnd(
            date,
            _context.Cash,
            _context.PortfolioValue,
            _context.Positions,
            _context.Accounts);
    }

    private async Task RouteQueuedOrdersAsync(CancellationToken ct)
    {
        foreach (var cancelledOrderId in _context.DrainPendingCancellations())
        {
            if (!_clientIdsByOrderId.TryGetValue(cancelledOrderId, out var clientOrderId))
            {
                continue;
            }

            try
            {
                await _orderManager.CancelOrderAsync(clientOrderId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Run {RunId} could not cancel order {ClientOrderId}.", _run.RunId, clientOrderId);
            }
        }

        foreach (var order in _context.DrainPendingOrders())
        {
            var clientOrderId = $"{_clientOrderIdPrefix}{order.OrderId:N}";
            var request = MapOrder(order, clientOrderId);
            _ordersByClientId[clientOrderId] = order;
            _clientIdsByOrderId[order.OrderId] = clientOrderId;

            try
            {
                var result = await _orderManager.PlaceOrderAsync(request, ct).ConfigureAwait(false);
                if (!result.Success)
                {
                    _logger.LogWarning(
                        "Run {RunId} order {ClientOrderId} for {Symbol} was rejected: {Reason}",
                        _run.RunId, clientOrderId, order.Symbol, result.ErrorMessage ?? "no reason given");
                    ForgetOrder(clientOrderId, order.OrderId);
                }
                else if (_useSynchronousFillFallback
                         && result.OrderState is { FilledQuantity: > 0m } state)
                {
                    // Without an OMS report pump (e.g. a stub order manager in tests), the
                    // synchronous fill on the placement result is the only fill signal.
                    EnqueueExecutionReport(new ExecutionSdk.ExecutionReport
                    {
                        OrderId = state.OrderId,
                        ClientOrderId = state.OrderId,
                        ReportType = state.Status == ExecutionSdk.OrderStatus.Filled
                            ? ExecutionSdk.ExecutionReportType.Fill
                            : ExecutionSdk.ExecutionReportType.PartialFill,
                        Symbol = state.Symbol,
                        Side = state.Side,
                        OrderStatus = state.Status,
                        OrderQuantity = state.Quantity,
                        FilledQuantity = state.FilledQuantity,
                        FillPrice = state.AverageFillPrice,
                        Timestamp = state.LastUpdatedAt ?? DateTimeOffset.UtcNow
                    });
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Run {RunId} order {ClientOrderId} for {Symbol} could not be submitted.",
                    _run.RunId, clientOrderId, order.Symbol);
                ForgetOrder(clientOrderId, order.OrderId);
            }
        }
    }

    private void HandleExecutionReport(ExecutionSdk.ExecutionReport report)
    {
        var clientOrderId = report.ClientOrderId ?? report.OrderId;
        if (string.IsNullOrEmpty(clientOrderId) || !_ordersByClientId.TryGetValue(clientOrderId, out var order))
        {
            return;
        }

        if (report.OrderStatus is ExecutionSdk.OrderStatus.Filled or ExecutionSdk.OrderStatus.PartiallyFilled
            && report.FilledQuantity > 0m)
        {
            var unsignedQuantity = (long)report.FilledQuantity;
            var signedQuantity = report.Side == ExecutionSdk.OrderSide.Sell ? -unsignedQuantity : unsignedQuantity;
            var fillPrice = report.FillPrice
                ?? _context.GetLastPrice(report.Symbol)
                ?? order.LimitPrice
                ?? 0m;
            var fill = new FillEvent(
                FillId: Guid.NewGuid(),
                OrderId: order.OrderId,
                Symbol: report.Symbol,
                FilledQuantity: signedQuantity,
                FillPrice: fillPrice,
                Commission: report.Commission ?? 0m,
                FilledAt: report.Timestamp,
                AccountId: order.AccountId);

            _metrics.RecordFill(fill, report.Timestamp);
            if (_strategy.Status != StrategyStatus.Stopped)
            {
                _strategy.OnOrderFill(fill, _context);
            }
        }

        if (report.OrderStatus is ExecutionSdk.OrderStatus.Filled
            or ExecutionSdk.OrderStatus.Cancelled
            or ExecutionSdk.OrderStatus.Rejected
            or ExecutionSdk.OrderStatus.Expired)
        {
            ForgetOrder(clientOrderId, order.OrderId);
        }
    }

    private void ForgetOrder(string clientOrderId, Guid orderId)
    {
        _ordersByClientId.Remove(clientOrderId);
        _clientIdsByOrderId.Remove(orderId);
    }

    private ExecutionSdk.OrderRequest MapOrder(Order order, string clientOrderId) => new()
    {
        Symbol = order.Symbol,
        Side = order.Quantity > 0 ? ExecutionSdk.OrderSide.Buy : ExecutionSdk.OrderSide.Sell,
        Type = order.Type switch
        {
            OrderType.Limit => ExecutionSdk.OrderType.Limit,
            OrderType.StopMarket => ExecutionSdk.OrderType.StopMarket,
            OrderType.StopLimit => ExecutionSdk.OrderType.StopLimit,
            _ => ExecutionSdk.OrderType.Market
        },
        Quantity = Math.Abs(order.Quantity),
        LimitPrice = order.LimitPrice,
        StopPrice = order.StopPrice,
        TimeInForce = order.TimeInForce switch
        {
            TimeInForce.GoodTilCancelled => ExecutionSdk.TimeInForce.GoodTilCancelled,
            TimeInForce.ImmediateOrCancel => ExecutionSdk.TimeInForce.ImmediateOrCancel,
            TimeInForce.FillOrKill => ExecutionSdk.TimeInForce.FillOrKill,
            _ => ExecutionSdk.TimeInForce.Day
        },
        ClientOrderId = clientOrderId,
        StrategyId = _run.StrategyId,
        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["actor"] = ActorId,
            ["runId"] = _run.RunId,
            ["correlationId"] = _run.RunId
        }
    };

    private async Task FinishAsync(Exception? faulted)
    {
        // Drain any fills that raced session shutdown so metrics stay complete.
        while (_fillReports.Reader.TryRead(out var report))
        {
            HandleExecutionReport(report);
        }

        if (_currentDate is { } lastDay)
        {
            CloseDay(lastDay);
        }

        try
        {
            _strategy.OnFinished(_context);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Run {RunId} strategy OnFinished callback failed.", _run.RunId);
        }

        if (_strategy.Status != StrategyStatus.Stopped)
        {
            try
            {
                await _strategy.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Run {RunId} strategy stop failed during session teardown.", _run.RunId);
            }
        }

        await RecordTerminalRunStateAsync(faulted).ConfigureAwait(false);
        await RecordSessionAuditAsync(faulted).ConfigureAwait(false);
    }

    private async Task RecordTerminalRunStateAsync(Exception? faulted)
    {
        try
        {
            var latest = await _repository.GetRunByIdAsync(_run.RunId, CancellationToken.None).ConfigureAwait(false);
            if (latest is { EndedAt: not null })
            {
                // Another surface (e.g. the lifecycle manager stop endpoint) already
                // finalised this run; do not overwrite its terminal evidence.
                return;
            }

            var current = latest ?? _run;
            if (faulted is not null)
            {
                await _repository.RecordLifecycleEventAsync(
                    current.Fail(faulted, "Live session failed.") with { ActorId = ActorId },
                    StrategyRunLifecycleEventType.Failed,
                    CancellationToken.None).ConfigureAwait(false);
                return;
            }

            if (!_completeRunOnExit)
            {
                // Host shutdown: leave the run open so a restarted engine resumes it.
                return;
            }

            var metrics = _metrics.Build(
                _run.Engine ?? (_run.RunType == RunType.Live ? "BrokerLive" : "BrokerPaper"),
                _context.Universe,
                _context.PortfolioValue,
                _context.Ledger,
                DateTimeOffset.UtcNow);
            await _repository.RecordLifecycleEventAsync(
                current.Complete(metrics) with { ActorId = ActorId },
                StrategyRunLifecycleEventType.Completed,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run {RunId} terminal state could not be recorded.", _run.RunId);
        }
    }

    private async Task RecordSessionAuditAsync(Exception? faulted)
    {
        if (_auditTrail is null)
        {
            return;
        }

        try
        {
            await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: Guid.NewGuid().ToString("N"),
                Category: "Execution",
                Action: faulted is null ? "LiveRunSessionEnded" : "LiveRunSessionFailed",
                Outcome: faulted is null ? "Completed" : "Failed",
                OccurredAt: DateTimeOffset.UtcNow,
                Actor: ActorId,
                RunId: _run.RunId,
                CorrelationId: _run.RunId,
                Message: faulted?.Message ?? $"Processed {_metrics.EventsProcessed} events.",
                Scope: $"run:{_run.RunId}/strategy:{_run.StrategyId}"), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Run {RunId} session audit could not be recorded.", _run.RunId);
        }
    }
}
