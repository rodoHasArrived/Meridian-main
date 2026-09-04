using System.Threading.Channels;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution;
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
    private readonly Lock _stopSync = new();
    private readonly Dictionary<string, Order> _ordersByClientId = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _clientIdsByOrderId = new();
    private readonly HashSet<string> _parkedClientOrderIds = new(StringComparer.Ordinal);
    private readonly LiveRunMetricsTracker _metrics;
    private readonly string _clientOrderIdPrefix;

    private DateOnly? _currentDate;
    private volatile bool _completeRunOnExit = true;
    private Exception? _requestedFailure;
    private int _outboundOrderAdmissionClosed;

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

    /// <summary>
    /// Queues an execution report for processing on the session's event loop. A saturated inbox
    /// applies asynchronous backpressure; it never turns an authoritative fill into a failed
    /// <c>TryWrite</c> that the strategy silently misses.
    /// </summary>
    public async ValueTask EnqueueExecutionReportAsync(
        ExecutionSdk.ExecutionReport report,
        CancellationToken ct = default)
        => await _fillReports.Writer.WriteAsync(report, ct).ConfigureAwait(false);

    /// <summary>Completes this session's fill inbox after its engine-owned delivery worker drains.</summary>
    public void CompleteExecutionReportAdmission()
        => _fillReports.Writer.TryComplete();

    /// <summary>
    /// Stops strategy callbacks from creating new broker work while keeping the event loop alive
    /// to consume fills for operations admitted before shutdown.
    /// </summary>
    public void CloseOutboundOrderAdmission()
        => Interlocked.Exchange(ref _outboundOrderAdmissionClosed, 1);

    /// <summary>
    /// Fails the run because an accepted broker report could not be delivered to its event loop.
    /// The first delivery failure owns the terminal reason; later reports are retained against the
    /// same failed outcome by the engine.
    /// </summary>
    public void RequestFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        lock (_stopSync)
        {
            _requestedFailure ??= failure;
            _completeRunOnExit = true;
        }

        _stopRequested.Cancel();
    }

    /// <summary>
    /// Requests the session to stop. When <paramref name="completeRun"/> is false (host
    /// shutdown), the run entry is left open so a restarted host can resume it.
    /// </summary>
    public void RequestStop(bool completeRun)
    {
        lock (_stopSync)
        {
            if (_requestedFailure is null)
            {
                _completeRunOnExit = completeRun;
            }
        }

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
            Exception? requestedFailure;
            lock (_stopSync)
            {
                requestedFailure = _requestedFailure;
            }

            await FinishAsync(requestedFailure).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live session for run {RunId} failed.", _run.RunId);
            await FinishAsync(faulted: ex).ConfigureAwait(false);
        }
    }

    private async Task RunEventLoopAsync(CancellationToken ct)
    {
        using var loopStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loopToken = loopStop.Token;
        await using var events = _feed
            .SubscribeAsync(_context.Universe, loopToken)
            .GetAsyncEnumerator(loopToken);
        Task<bool>? moveNext = null;
        Task<bool>? fillReady = null;
        var fillChannelCompleted = false;
        try
        {
            while (!loopToken.IsCancellationRequested && _strategy.Status != StrategyStatus.Stopped)
            {
                moveNext ??= events.MoveNextAsync().AsTask();
                // Track the pending wait across iterations: the fill channel is
                // SingleReader, so only one WaitToReadAsync may ever be in flight.
                if (!fillChannelCompleted)
                {
                    fillReady ??= _fillReports.Reader.WaitToReadAsync(loopToken).AsTask();
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

                await ProcessMarketEventAsync(events.Current, loopToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // A market feed may complete normally while no fills are pending. Complete the
            // private inbox before awaiting its outstanding read so that teardown cannot
            // wait indefinitely for a writer that no longer exists.
            _fillReports.Writer.TryComplete();

            // A fill callback can fail while the market-feed advance is still pending. Cancel
            // this loop's owned token before awaiting that advance so the original fill failure
            // reaches ExecuteAsync and is retained instead of hanging behind an idle feed.
            try
            {
                await loopStop.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live session {RunId} event-loop cancellation callback failed.", _run.RunId);
            }

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

        if (Volatile.Read(ref _outboundOrderAdmissionClosed) != 0)
        {
            return;
        }

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
        if (Volatile.Read(ref _outboundOrderAdmissionClosed) != 0)
        {
            return;
        }

        RetireDeclinedEscalations();

        // Orders cancelled while still queued never reached the gateway, so this is the only point
        // at which a strategy holding the symbol can learn they are dead.
        foreach (var locallyCancelledOrderId in _context.DrainLocallyCancelledOrders())
        {
            NotifyOrderTerminated(locallyCancelledOrderId, LiveOrderOutcome.Cancelled);
        }

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
            if (Volatile.Read(ref _outboundOrderAdmissionClosed) != 0)
            {
                break;
            }

            ExecutionSdk.ExecutionReport? synchronousFill = null;
            var clientOrderId = $"{_clientOrderIdPrefix}{order.OrderId:N}";
            var request = MapOrder(order, clientOrderId);
            _ordersByClientId[clientOrderId] = order;
            _clientIdsByOrderId[order.OrderId] = clientOrderId;

            try
            {
                var result = await _orderManager.PlaceOrderAsync(request, ct).ConfigureAwait(false);
                if (result.RequiresApproval)
                {
                    // Parked for governed approval, not rejected: the order can still be
                    // released later, and its execution report will carry this client
                    // order id. Keep the mapping alive so the run receives the fill, and
                    // remember the park so a decline — which produces no report at all —
                    // still retires it.
                    _parkedClientOrderIds.Add(clientOrderId);
                    _logger.LogInformation(
                        "Run {RunId} order {ClientOrderId} for {Symbol} is parked for governed risk approval ({EscalationId})",
                        _run.RunId, clientOrderId, order.Symbol, result.EscalationId ?? "unknown");
                }
                else if (!result.Success)
                {
                    _logger.LogWarning(
                        "Run {RunId} order {ClientOrderId} for {Symbol} was rejected: {Reason}",
                        _run.RunId, clientOrderId, order.Symbol, result.ErrorMessage ?? "no reason given");

                    // A synchronous rejection never enters the report stream, so this is the only
                    // point at which a strategy holding the symbol can learn the order is dead.
                    NotifyOrderTerminated(order.OrderId, LiveOrderOutcome.Rejected);
                    ForgetOrder(clientOrderId, order.OrderId);
                }
                else if (_useSynchronousFillFallback
                         && result.OrderState is { FilledQuantity: > 0m } state)
                {
                    // Without an OMS report pump (e.g. a stub order manager in tests), the
                    // synchronous fill on the placement result is the only fill signal. This
                    // method already runs on the sole session event loop, so writing to the
                    // bounded channel that only this loop drains would self-deadlock once full.
                    synchronousFill = new ExecutionSdk.ExecutionReport
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
                        Timestamp = state.LastUpdatedAt ?? DateTimeOffset.UtcNow,
                        // Carried from the tracked state so this fallback path books the same
                        // par-scaled cash flow the OMS-stamped report stream would deliver.
                        UsesFaceValuePercentageOfPar = state.UsesFaceValuePercentageOfPar
                    };
                }
            }
            catch (OrderManagementSystem.ExecutionReportDeliveryException)
            {
                // The venue accepted a fill but authoritative strategy delivery could not be
                // durably accounted. This is a run failure, not an ordinary submit rejection.
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Run {RunId} order {ClientOrderId} for {Symbol} could not be submitted.",
                    _run.RunId, clientOrderId, order.Symbol);

                // Same reasoning as the rejection path: the mapping is being dropped, so no later
                // report can release a strategy's marker for this order.
                NotifyOrderTerminated(order.OrderId, LiveOrderOutcome.SubmissionFailed);
                ForgetOrder(clientOrderId, order.OrderId);
            }

            // Keep fill-contract failures outside the submission catch above. A fractional or
            // otherwise invalid broker fill must fail the run, not be logged as a submit error and
            // silently discarded.
            if (synchronousFill is not null)
            {
                HandleExecutionReport(synchronousFill);
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
            var signedQuantity = ConvertToBacktestFillQuantity(report);
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

            // The report's OMS-stamped sizing classification rides along so a fixed-income
            // fill (face value at percent-of-par) books its par-scaled cash flow instead of
            // a 100x raw quantity-times-price movement.
            _metrics.RecordFill(fill, report.Timestamp, report.UsesFaceValuePercentageOfPar);
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
            // A strategy that blocks a symbol while its order works needs the terminal outcome to
            // unblock it; a fill already reaches it through OnOrderFill, so only the non-filling
            // endings are reported.
            if (report.OrderStatus is not ExecutionSdk.OrderStatus.Filled)
            {
                NotifyOrderTerminated(order.OrderId, report.OrderStatus switch
                {
                    ExecutionSdk.OrderStatus.Rejected => LiveOrderOutcome.Rejected,
                    ExecutionSdk.OrderStatus.Expired => LiveOrderOutcome.Expired,
                    _ => LiveOrderOutcome.Cancelled
                });
            }

            ForgetOrder(clientOrderId, order.OrderId);
        }
    }

    /// <summary>
    /// Tells a strategy that tracks its own working orders that one of them ended without a
    /// completing fill. Strategies that do not implement the seam are unaffected.
    /// </summary>
    private void NotifyOrderTerminated(Guid orderId, LiveOrderOutcome outcome)
    {
        if (_strategy is not ILiveOrderOutcomeObserver observer)
        {
            return;
        }

        try
        {
            observer.OnOrderTerminated(orderId, outcome);
        }
        catch (Exception ex)
        {
            // A strategy fault while releasing its own bookkeeping must not abort the report loop:
            // the remaining reports still have to be applied to metrics and position state.
            _logger.LogWarning(
                ex,
                "Run {RunId} strategy faulted handling the terminal outcome {Outcome} for order {OrderId}.",
                _run.RunId, outcome, orderId);
        }
    }

    /// <summary>
    /// The shared execution contract represents broker quantities as decimal, while the retained
    /// Backtesting SDK <see cref="FillEvent"/> contract is whole-unit <see cref="long"/>. This is
    /// the true narrowing boundary: fractional or out-of-range live fills fail the run explicitly
    /// instead of being truncated into a different economic event.
    /// </summary>
    private static long ConvertToBacktestFillQuantity(ExecutionSdk.ExecutionReport report)
    {
        var quantity = report.FilledQuantity;
        if (quantity != decimal.Truncate(quantity))
        {
            throw new InvalidOperationException(
                $"Execution fill for order '{report.OrderId}' has fractional quantity {quantity}; "
                + "the strategy FillEvent contract supports whole units only, so the live run failed closed.");
        }

        if (quantity > long.MaxValue)
        {
            throw new InvalidOperationException(
                $"Execution fill for order '{report.OrderId}' has quantity {quantity}, which exceeds "
                + "the strategy FillEvent whole-unit range; the live run failed closed.");
        }

        var unsignedQuantity = decimal.ToInt64(quantity);
        return report.Side == ExecutionSdk.OrderSide.Sell ? -unsignedQuantity : unsignedQuantity;
    }

    private void ForgetOrder(string clientOrderId, Guid orderId)
    {
        _ordersByClientId.Remove(clientOrderId);
        _clientIdsByOrderId.Remove(orderId);
        _parkedClientOrderIds.Remove(clientOrderId);
    }

    /// <summary>
    /// Drops bookkeeping for parked orders whose governed approval was declined. Only a
    /// released escalation re-enters the report stream; a declined one is terminal and
    /// silent, so without this the run would hold its order mapping for the whole session.
    /// </summary>
    private void RetireDeclinedEscalations()
    {
        if (_parkedClientOrderIds.Count == 0)
        {
            return;
        }

        foreach (var clientOrderId in _parkedClientOrderIds.ToArray())
        {
            if (!_orderManager.WasRiskApprovalDeclined(clientOrderId))
            {
                continue;
            }

            _logger.LogInformation(
                "Run {RunId} order {ClientOrderId} was declined for governed risk approval; retiring its tracking.",
                _run.RunId, clientOrderId);

            if (_ordersByClientId.TryGetValue(clientOrderId, out var order))
            {
                NotifyOrderTerminated(order.OrderId, LiveOrderOutcome.ApprovalDeclined);
                ForgetOrder(clientOrderId, order.OrderId);
            }
            else
            {
                _parkedClientOrderIds.Remove(clientOrderId);
            }
        }
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

        // Only when the run is actually ending. A host shutdown calls RequestStop with
        // completeRun: false precisely so the run can be resumed, and denying its parked
        // orders there would durably discard live approvals and trade intentions on every
        // deployment.
        if (_completeRunOnExit)
        {
            // An escalation that survives teardown can still route an order into a run that
            // no longer exists, whose fills reach no session. If any withdrawal fails the
            // run is not cleanly complete, and must not be recorded as if it were.
            var unwithdrawn = await WithdrawParkedOrdersAsync().ConfigureAwait(false);
            if (unwithdrawn > 0)
            {
                faulted ??= new InvalidOperationException(
                    $"{unwithdrawn} governed risk escalation(s) could not be withdrawn as the run ended; "
                    + "an approval could still route an order this run cannot receive.");
            }
        }
        await RecordTerminalRunStateAsync(faulted).ConfigureAwait(false);
        await RecordSessionAuditAsync(faulted).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels orders still awaiting governed approval as the run ends. The run is about to
    /// be removed from the engine, so an approval granted after this point would route an
    /// order for a strategy that is no longer running — and its fills would reach no
    /// session at all. Cancelling withdraws the escalation, which is what makes the
    /// approval unreachable rather than merely unattended.
    /// </summary>
    /// <returns>How many escalations remain actionable because withdrawal failed.</returns>
    private async Task<int> WithdrawParkedOrdersAsync()
    {
        if (_parkedClientOrderIds.Count == 0)
        {
            return 0;
        }

        var unwithdrawn = 0;

        foreach (var clientOrderId in _parkedClientOrderIds.ToArray())
        {
            try
            {
                // Ask before cancelling, not after. An escalation the desk already denied
                // needs no withdrawal, and cancelling one drops its parked reservation on
                // the way to a gateway cancel that fails for an order which never routed —
                // after which the denial is no longer recognizable and an otherwise clean
                // run is recorded Failed.
                if (_orderManager.WasRiskApprovalDeclined(clientOrderId))
                {
                    _parkedClientOrderIds.Remove(clientOrderId);
                    continue;
                }

                var cancelled = await _orderManager
                    .CancelOrderAsync(clientOrderId, CancellationToken.None)
                    .ConfigureAwait(false);
                if (!cancelled.Success)
                {
                    unwithdrawn++;
                    _logger.LogError(
                        "Run {RunId} ended with order {ClientOrderId} parked, and its escalation could not be withdrawn: {Reason}",
                        _run.RunId, clientOrderId, cancelled.ErrorMessage ?? "no reason given");
                    continue;
                }

                _parkedClientOrderIds.Remove(clientOrderId);
                _logger.LogInformation(
                    "Run {RunId} ended; withdrew the governed escalation still holding order {ClientOrderId}.",
                    _run.RunId, clientOrderId);
            }
            catch (Exception ex)
            {
                unwithdrawn++;
                _logger.LogError(ex,
                    "Run {RunId} could not withdraw parked order {ClientOrderId} during teardown.",
                    _run.RunId, clientOrderId);
            }
        }

        // Ids that failed to withdraw stay tracked: they are still actionable, and clearing
        // them would lose the only record that they need resolving.
        return unwithdrawn;
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
