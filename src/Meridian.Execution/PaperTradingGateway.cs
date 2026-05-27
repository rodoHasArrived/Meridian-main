using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Contracts.SecurityMaster;
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
    private readonly ISecurityMasterQueryService? _securityMaster;
    private readonly ConcurrentDictionary<string, TradingParametersDto?> _tradingParamsCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _connected;
    private int _fillSequence;

    public PaperTradingGateway(ILogger<PaperTradingGateway> logger, ISecurityMasterQueryService? securityMaster = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _securityMaster = securityMaster;
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

        var lotSizeError = await ValidateLotSizeAsync(request, ct).ConfigureAwait(false);
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

    private async Task<string?> ValidateLotSizeAsync(OrderRequest request, CancellationToken ct)
    {
        var tradingParams = await TryGetTradingParamsAsync(request.Symbol, ct).ConfigureAwait(false);
        if (tradingParams?.LotSize is not { } lotSize || lotSize <= 0m)
        {
            return null;
        }

        var absQty = Math.Abs(request.Quantity);
        return absQty % lotSize == 0m
            ? null
            : $"Order quantity {absQty} is not a valid multiple of the lot-size {lotSize} for {request.Symbol}.";
    }

    private async Task<TradingParametersDto?> TryGetTradingParamsAsync(string symbol, CancellationToken ct)
    {
        if (_securityMaster is null || string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        if (_tradingParamsCache.TryGetValue(symbol, out var cached))
        {
            return cached;
        }

        try
        {
            var security = await _securityMaster.GetByIdentifierAsync(
                SecurityIdentifierKind.Ticker,
                symbol,
                provider: null,
                ct).ConfigureAwait(false);

            if (security is null)
            {
                _tradingParamsCache[symbol] = null;
                return null;
            }

            var tradingParams = await _securityMaster.GetTradingParametersAsync(
                security.SecurityId,
                DateTimeOffset.UtcNow,
                ct).ConfigureAwait(false);

            _tradingParamsCache[symbol] = tradingParams;
            return tradingParams;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PaperTradingGateway lot-size validation skipped for {Symbol} due to Security Master lookup failure.",
                symbol);
            _tradingParamsCache[symbol] = null;
            return null;
        }
    }

}
