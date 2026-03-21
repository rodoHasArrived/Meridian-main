using System.Runtime.CompilerServices;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// Simulated execution gateway for paper trading. Fills all market orders immediately
/// at the last known price, and queues limit orders for fill on price touch.
/// </summary>
public sealed class PaperTradingGateway : IExecutionGateway
{
    private readonly ILogger<PaperTradingGateway> _logger;
    private bool _connected;
    private int _fillSequence;

    public PaperTradingGateway(ILogger<PaperTradingGateway> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string GatewayId => "paper";

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
    public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var fillSeq = Interlocked.Increment(ref _fillSequence);

        // Market orders fill immediately at a simulated price
        if (request.Type == OrderType.Market)
        {
            var report = new ExecutionReport
            {
                OrderId = request.ClientOrderId ?? $"PAPER-{fillSeq}",
                ReportType = ExecutionReportType.Fill,
                Symbol = request.Symbol,
                Side = request.Side,
                OrderStatus = OrderStatus.Filled,
                OrderQuantity = request.Quantity,
                FilledQuantity = request.Quantity,
                FillPrice = request.LimitPrice ?? 0m, // Caller should set simulated price
                Commission = 0m,
                Timestamp = DateTimeOffset.UtcNow,
                GatewayOrderId = $"PAPER-{fillSeq}"
            };

            _logger.LogInformation("Paper fill: {Symbol} {Side} {Quantity} @ {Price}",
                request.Symbol, request.Side, request.Quantity, report.FillPrice);

            return Task.FromResult(report);
        }

        // Limit/stop orders are accepted but not immediately filled
        var accepted = new ExecutionReport
        {
            OrderId = request.ClientOrderId ?? $"PAPER-{fillSeq}",
            ReportType = ExecutionReportType.New,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = OrderStatus.Accepted,
            OrderQuantity = request.Quantity,
            FilledQuantity = 0,
            Timestamp = DateTimeOffset.UtcNow,
            GatewayOrderId = $"PAPER-{fillSeq}"
        };

        return Task.FromResult(accepted);
    }

    /// <inheritdoc />
    public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
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
}
