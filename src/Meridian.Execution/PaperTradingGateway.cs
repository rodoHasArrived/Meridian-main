using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// Simulated execution gateway for paper trading. Market orders fill immediately at the
/// caller-provided simulated price (<see cref="OrderRequest.LimitPrice"/>) or, when none is
/// supplied, the configured scaffold notional price. Limit and stop orders are accepted and rest
/// in memory but are never simulated to fill — this gateway performs no price-touch matching and
/// exposes no execution-report stream. Cancel and modify act only on orders currently resting in
/// this gateway; an unknown order id is rejected rather than acknowledged.
/// </summary>
public sealed class PaperTradingGateway : IExecutionGateway, IExecutionGatewayModeProvider
{
    private readonly ILogger<PaperTradingGateway> _logger;
    private readonly Adapters.PaperTradingGatewayTradingParameters _tradingParameters;
    private readonly decimal _scaffoldMarketFillPrice;
    // Ids of limit/stop orders accepted and resting in this gateway, so cancel/modify can
    // distinguish a real resting order from an unknown id instead of fabricating success.
    private readonly ConcurrentDictionary<string, byte> _restingOrders = new(StringComparer.Ordinal);
    private int _scaffoldPriceWarningIssued;
    private bool _connected;
    private int _fillSequence;

    public PaperTradingGateway(
        ILogger<PaperTradingGateway> logger,
        ISecurityMasterQueryService? securityMaster = null,
        Adapters.PaperTradingGatewayOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tradingParameters = new Adapters.PaperTradingGatewayTradingParameters(
            _logger,
            securityMaster,
            LogLevel.Warning);
        _scaffoldMarketFillPrice = Adapters.PaperTradingGatewayScaffoldPricing.ResolveScaffoldMarketFillPrice(options);
    }

    /// <inheritdoc />
    public string GatewayId => "paper";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.Paper;

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken ct = default)
    {
        _connected = true;
        _logger.LogInformation("Paper trading gateway connected");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        _logger.LogInformation("Paper trading gateway disconnected");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var fillSeq = Interlocked.Increment(ref _fillSequence);

        var lotSizeError = await _tradingParameters.ValidateLotSizeAsync(request, ct).ConfigureAwait(false);
        if (lotSizeError is not null)
        {
            throw new InvalidOperationException(lotSizeError);
        }

        if (request.Type is OrderType.MarketOnOpen or OrderType.MarketOnClose or OrderType.LimitOnOpen or OrderType.LimitOnClose)
        {
            throw new NotSupportedException(
                $"Paper trading gateway does not preserve the {request.Type} session timing qualifier.");
        }

        // Market-style orders fill immediately at a simulated price
        if (request.Type is OrderType.Market)
        {
            // Callers should set a simulated price via LimitPrice; otherwise the
            // configured scaffold notional price applies (with a one-time warning).
            if (request.LimitPrice is null)
            {
                WarnScaffoldPriceUsed();
            }

            var report = new ExecutionReport
            {
                OrderId = request.ClientOrderId ?? $"PAPER-{fillSeq}",
                ReportType = ExecutionReportType.Fill,
                Symbol = request.Symbol,
                Side = request.Side,
                OrderStatus = OrderStatus.Filled,
                OrderQuantity = request.Quantity,
                FilledQuantity = request.Quantity,
                FillPrice = request.LimitPrice ?? _scaffoldMarketFillPrice,
                Commission = 0m,
                Timestamp = DateTimeOffset.UtcNow,
                GatewayOrderId = $"PAPER-{fillSeq}"
            };

            _logger.LogInformation("Paper fill: {Symbol} {Side} {Quantity} @ {Price}",
                request.Symbol, request.Side, request.Quantity, report.FillPrice);

            return report;
        }

        // Limit/stop orders are accepted but not immediately filled; track them as resting so
        // cancel/modify can validate the order id later.
        var restingOrderId = request.ClientOrderId ?? $"PAPER-{fillSeq}";
        var accepted = new ExecutionReport
        {
            OrderId = restingOrderId,
            ReportType = ExecutionReportType.New,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = OrderStatus.Accepted,
            OrderQuantity = request.Quantity,
            FilledQuantity = 0,
            Timestamp = DateTimeOffset.UtcNow,
            GatewayOrderId = $"PAPER-{fillSeq}"
        };

        _restingOrders.TryAdd(restingOrderId, 0);
        return accepted;
    }

    /// <inheritdoc />
    public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        if (!_restingOrders.TryRemove(orderId, out _))
        {
            // No resting order with this id: unknown, already filled (market order), or already
            // cancelled. Report a rejection rather than fabricating a successful cancellation.
            return Task.FromResult(new ExecutionReport
            {
                OrderId = orderId,
                ReportType = ExecutionReportType.Rejected,
                Symbol = string.Empty,
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Rejected,
                RejectReason = "No resting paper order with this id to cancel.",
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        return Task.FromResult(new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Cancelled,
            Symbol = string.Empty,
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Cancelled,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <inheritdoc />
    public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default)
    {
        if (!_restingOrders.ContainsKey(orderId))
        {
            // Only orders still resting in this gateway can be modified; reject unknown ids
            // rather than fabricating an acceptance.
            return Task.FromResult(new ExecutionReport
            {
                OrderId = orderId,
                ReportType = ExecutionReportType.Rejected,
                Symbol = string.Empty,
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Rejected,
                RejectReason = "No resting paper order with this id to modify.",
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        return Task.FromResult(new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Modified,
            Symbol = string.Empty,
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Accepted,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Paper gateway doesn't have a persistent stream — reports are returned synchronously
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>
    /// Emits a one-time loud warning when a market fill is priced from the scaffold
    /// notional price instead of a caller-provided simulated price.
    /// </summary>
    private void WarnScaffoldPriceUsed()
    {
        Adapters.PaperTradingGatewayScaffoldPricing.WarnIfFirstUse(
            ref _scaffoldPriceWarningIssued,
            _logger,
            _scaffoldMarketFillPrice,
            "Paper execution gateway");
    }

}
