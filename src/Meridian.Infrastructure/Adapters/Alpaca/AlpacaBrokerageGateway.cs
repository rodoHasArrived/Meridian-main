using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.Logging;
using AlpacaOptions = Meridian.Application.Config.AlpacaOptions;
using OrderSide = Meridian.Execution.Sdk.OrderSide;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;
using OrderType = Meridian.Execution.Sdk.OrderType;
using TimeInForce = Meridian.Execution.Sdk.TimeInForce;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// Alpaca brokerage gateway for live order execution via the Alpaca Trading API.
/// Uses REST API for order management and a bounded channel for execution report streaming.
/// </summary>
/// <remarks>
/// Alpaca Trading API v2:
/// - Paper trading: paper-api.alpaca.markets
/// - Live trading: api.alpaca.markets
/// - Supports market, limit, stop, stop-limit orders
/// - Supports fractional shares
/// - Supports extended hours trading
/// - Supports US equities and fixed income (US Treasuries, corporate bonds)
/// - Fixed income orders may use notional sizing: set Metadata["notional"] = "true" on the OrderRequest
///   and pass the dollar amount as Quantity. Optionally set Metadata["asset_class"] = "us_treasury"
///   to route treasury orders explicitly.
/// - Rate limit: 200 req/min
/// </remarks>
[DataSource("alpaca-brokerage", "Alpaca Brokerage", DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 10, Description = "Alpaca Markets order execution gateway")]
[ImplementsAdr("ADR-001", "Alpaca brokerage provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory for HTTP connections")]
public sealed class AlpacaBrokerageGateway : IBrokerageGateway, IBrokerageAccountCatalog, IBrokeragePortfolioSync, IBrokerageActivitySync
{
    private const string PaperBaseUrl = "https://paper-api.alpaca.markets";
    private const string LiveBaseUrl = "https://api.alpaca.markets";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AlpacaOptions _options;
    private readonly ILogger<AlpacaBrokerageGateway> _logger;
    private readonly Channel<ExecutionReport> _reportChannel;
    private volatile bool _connected;
    private bool _disposed;

    public AlpacaBrokerageGateway(
        IHttpClientFactory httpClientFactory,
        AlpacaOptions options,
        ILogger<AlpacaBrokerageGateway> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_options.KeyId) || string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            _logger.LogWarning(
                "Alpaca brokerage credentials are missing or incomplete; gateway will remain unavailable until valid credentials are provided.");
        }

        _reportChannel = EventPipelinePolicy.Default.CreateChannel<ExecutionReport>(
            singleReader: false, singleWriter: false);
    }

    /// <inheritdoc />
    public string GatewayId => "alpaca";

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public string BrokerDisplayName => "Alpaca Markets";

    /// <inheritdoc />
    public BrokerageCapabilities BrokerageCapabilities { get; } =
        BrokerageCapabilities.UsEquityAndFixedIncome(
            modification: true,
            partialFills: true,
            shortSelling: true,
            fractional: true,
            extendedHours: true);

    string IBrokerageAccountCatalog.ProviderId => GatewayId;

    string IBrokerageAccountCatalog.ProviderDisplayName => BrokerDisplayName;

    string IBrokeragePortfolioSync.ProviderId => GatewayId;

    string IBrokerageActivitySync.ProviderId => GatewayId;

    private string BaseUrl => _options.UseSandbox ? PaperBaseUrl : LiveBaseUrl;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connected)
            return;

        if (string.IsNullOrWhiteSpace(_options.KeyId) || string.IsNullOrWhiteSpace(_options.SecretKey))
            throw new InvalidOperationException(
                "Alpaca KeyId and SecretKey are required for brokerage. Configure credentials before calling ConnectAsync.");

        var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
        _connected = true;
        _logger.LogInformation("Alpaca brokerage connected: account {AccountId}, status {Status}",
            account.AccountId, account.Status);
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        _logger.LogInformation("Alpaca brokerage disconnected");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();
        RejectSessionScopedOrderType(request.Type);

        _logger.LogInformation(
            "Alpaca submitting order: {Side} {Quantity} {Symbol} @ {Type}",
            request.Side, request.Quantity, request.Symbol, request.Type);

        var isNotional = request.Metadata?.TryGetValue("notional", out var notionalFlag) == true
            && string.Equals(notionalFlag, "true", StringComparison.OrdinalIgnoreCase);

        string? assetClass = request.Metadata is not null
            && request.Metadata.TryGetValue("asset_class", out var ac) ? ac : null;

        var payload = new AlpacaOrderPayload
        {
            Symbol = request.Symbol,
            Qty = isNotional ? null : request.Quantity.ToString("G"),
            Notional = isNotional ? request.Quantity.ToString("G") : null,
            Side = request.Side == OrderSide.Buy ? "buy" : "sell",
            Type = MapOrderType(request.Type),
            TimeInForce = MapTimeInForce(request.TimeInForce),
            LimitPrice = request.LimitPrice?.ToString("G"),
            StopPrice = request.StopPrice?.ToString("G"),
            ClientOrderId = request.ClientOrderId,
            AssetClass = assetClass,
        };

        using var client = CreateHttpClient();
        var response = await client.PostAsJsonAsync(
            $"{BaseUrl}/v2/orders", payload, AlpacaBrokerageSerializerContext.Default.AlpacaOrderPayload, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogError("Alpaca order rejected: {StatusCode} {Body}", response.StatusCode, errorBody);

            var rejectReport = new ExecutionReport
            {
                OrderId = request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
                ClientOrderId = request.ClientOrderId,
                ReportType = ExecutionReportType.Rejected,
                Symbol = request.Symbol,
                Side = request.Side,
                OrderStatus = OrderStatus.Rejected,
                RejectReason = $"Alpaca API error: {response.StatusCode} — {errorBody}",
                Timestamp = DateTimeOffset.UtcNow,
            };
            await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
            return rejectReport;
        }

        var order = await response.Content.ReadFromJsonAsync(
            AlpacaBrokerageSerializerContext.Default.AlpacaOrderResponse, ct).ConfigureAwait(false);

        var report = new ExecutionReport
        {
            OrderId = order?.Id ?? request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
            ClientOrderId = request.ClientOrderId,
            ReportType = ExecutionReportType.New,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = MapAlpacaStatus(order?.Status),
            OrderQuantity = request.Quantity,
            GatewayOrderId = order?.Id,
            Timestamp = order?.CreatedAt ?? DateTimeOffset.UtcNow,
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        using var client = CreateHttpClient();

        // Fetch current order details so the report carries the correct symbol and side.
        AlpacaOrderResponse? existing = null;
        try
        {
            var getResponse = await client.GetAsync($"{BaseUrl}/v2/orders/{orderId}", ct).ConfigureAwait(false);
            if (getResponse.IsSuccessStatusCode)
                existing = await getResponse.Content.ReadFromJsonAsync(
                    AlpacaBrokerageSerializerContext.Default.AlpacaOrderResponse, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Alpaca failed to fetch order {OrderId} before cancel", orderId);
        }

        var deleteResponse = await client.DeleteAsync($"{BaseUrl}/v2/orders/{orderId}", ct).ConfigureAwait(false);

        if (!deleteResponse.IsSuccessStatusCode)
        {
            var errorBody = await deleteResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("Alpaca cancel failed for {OrderId}: {Body}", orderId, errorBody);
        }

        var report = new ExecutionReport
        {
            OrderId = orderId,
            ClientOrderId = existing?.ClientOrderId,
            ReportType = deleteResponse.IsSuccessStatusCode ? ExecutionReportType.Cancelled : ExecutionReportType.Rejected,
            Symbol = existing?.Symbol ?? string.Empty,
            Side = existing?.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
            OrderStatus = deleteResponse.IsSuccessStatusCode ? OrderStatus.Cancelled : OrderStatus.Rejected,
            RejectReason = deleteResponse.IsSuccessStatusCode ? null : "Cancel request failed",
            GatewayOrderId = orderId,
            Timestamp = DateTimeOffset.UtcNow,
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> ModifyOrderAsync(
        string orderId, OrderModification modification, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(modification);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        var payload = new AlpacaOrderModifyPayload
        {
            Qty = modification.NewQuantity?.ToString("G"),
            LimitPrice = modification.NewLimitPrice?.ToString("G"),
            StopPrice = modification.NewStopPrice?.ToString("G"),
        };

        using var client = CreateHttpClient();
        var response = await client.PatchAsJsonAsync(
            $"{BaseUrl}/v2/orders/{orderId}", payload, AlpacaBrokerageSerializerContext.Default.AlpacaOrderModifyPayload, ct)
            .ConfigureAwait(false);

        var order = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync(
                AlpacaBrokerageSerializerContext.Default.AlpacaOrderResponse, ct).ConfigureAwait(false)
            : null;

        var report = new ExecutionReport
        {
            OrderId = order?.Id ?? orderId,
            ReportType = response.IsSuccessStatusCode ? ExecutionReportType.Modified : ExecutionReportType.Rejected,
            Symbol = order?.Symbol ?? string.Empty,
            Side = order?.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
            OrderStatus = response.IsSuccessStatusCode ? MapAlpacaStatus(order?.Status) : OrderStatus.Rejected,
            Timestamp = DateTimeOffset.UtcNow,
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var report in _reportChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return report;
        }
    }

    /// <inheritdoc />
    public async Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default)
    {
        using var client = CreateHttpClient();
        var response = await client.GetAsync($"{BaseUrl}/v2/account", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var account = await response.Content.ReadFromJsonAsync(
            AlpacaBrokerageSerializerContext.Default.AlpacaAccountResponse, ct).ConfigureAwait(false);

        return new AccountInfo
        {
            AccountId = account?.AccountNumber ?? "unknown",
            Equity = ParseDecimal(account?.Equity),
            Cash = ParseDecimal(account?.Cash),
            BuyingPower = ParseDecimal(account?.BuyingPower),
            Currency = account?.Currency ?? "USD",
            Status = account?.Status ?? "unknown",
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default)
    {
        using var client = CreateHttpClient();
        var response = await client.GetAsync($"{BaseUrl}/v2/positions", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var positions = await response.Content.ReadFromJsonAsync(
            AlpacaBrokerageSerializerContext.Default.AlpacaPositionResponseArray, ct).ConfigureAwait(false);

        if (positions is null)
            return Array.Empty<BrokerPosition>();

        return positions.Select(p => new BrokerPosition
        {
            Symbol = p.Symbol ?? string.Empty,
            Quantity = ParseDecimal(p.Qty),
            AverageEntryPrice = ParseDecimal(p.AvgEntryPrice),
            MarketPrice = ParseDecimal(p.CurrentPrice),
            MarketValue = ParseDecimal(p.MarketValue),
            UnrealizedPnl = ParseDecimal(p.UnrealizedPl),
            AssetClass = p.AssetClass ?? "equity",
            AccruedInterest = string.IsNullOrEmpty(p.AccruedInterest)
                ? null
                : ParseDecimal(p.AccruedInterest),
        }).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        using var client = CreateHttpClient();
        var response = await client.GetAsync($"{BaseUrl}/v2/orders?status=open", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var orders = await response.Content.ReadFromJsonAsync(
            AlpacaBrokerageSerializerContext.Default.AlpacaOrderResponseArray, ct).ConfigureAwait(false);

        if (orders is null)
            return Array.Empty<BrokerOrder>();

        return orders.Select(o => new BrokerOrder
        {
            OrderId = o.Id ?? string.Empty,
            ClientOrderId = o.ClientOrderId,
            Symbol = o.Symbol ?? string.Empty,
            Side = o.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
            Type = ParseOrderType(o.Type),
            Quantity = ParseDecimal(o.Qty),
            FilledQuantity = ParseDecimal(o.FilledQty),
            LimitPrice = string.IsNullOrEmpty(o.LimitPrice) ? null : ParseDecimal(o.LimitPrice),
            StopPrice = string.IsNullOrEmpty(o.StopPrice) ? null : ParseDecimal(o.StopPrice),
            Status = MapAlpacaStatus(o.Status),
            CreatedAt = o.CreatedAt ?? DateTimeOffset.UtcNow,
        }).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            if (!_connected)
                return BrokerHealthStatus.Unhealthy("Not connected");
            var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
            return account.Status == "active"
                ? BrokerHealthStatus.Healthy($"Account {account.AccountId} active")
                : BrokerHealthStatus.Unhealthy($"Account status: {account.Status}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alpaca health check failed");
            return BrokerHealthStatus.Unhealthy(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerageExternalAccountDto>> GetAccountsAsync(CancellationToken ct = default)
    {
        var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
        return
        [
            new BrokerageExternalAccountDto(
                ProviderId: GatewayId,
                AccountId: account.AccountId,
                DisplayName: string.IsNullOrWhiteSpace(account.AccountId) ? BrokerDisplayName : $"{BrokerDisplayName} {account.AccountId}",
                Status: account.Status,
                Currency: account.Currency,
                RetrievedAt: account.RetrievedAt,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["environment"] = _options.UseSandbox ? "paper" : "live",
                    ["broker"] = BrokerDisplayName
                })
        ];
    }

    /// <inheritdoc />
    public async Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(
        string externalAccountId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);

        var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
        var positions = await GetPositionsAsync(ct).ConfigureAwait(false);
        var accountDto = new BrokerageExternalAccountDto(
            ProviderId: GatewayId,
            AccountId: account.AccountId,
            DisplayName: string.IsNullOrWhiteSpace(account.AccountId) ? BrokerDisplayName : $"{BrokerDisplayName} {account.AccountId}",
            Status: account.Status,
            Currency: account.Currency,
            RetrievedAt: account.RetrievedAt,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requestedAccountId"] = externalAccountId,
                ["environment"] = _options.UseSandbox ? "paper" : "live"
            });

        return new BrokeragePortfolioSnapshotDto(
            Account: accountDto,
            Balance: new BrokerageBalanceSnapshotDto(
                Cash: account.Cash,
                Equity: account.Equity,
                BuyingPower: account.BuyingPower,
                Currency: account.Currency),
            Positions: positions
                .Select(static position => new BrokeragePositionSnapshotDto(
                    Symbol: position.Symbol,
                    Quantity: position.Quantity,
                    AverageEntryPrice: position.AverageEntryPrice,
                    MarketPrice: position.MarketPrice,
                    MarketValue: position.MarketValue,
                    UnrealizedPnl: position.UnrealizedPnl,
                    AssetClass: position.AssetClass,
                    Description: position.Description,
                    PositionId: position.PositionId,
                    Metadata: position.Metadata))
                .ToArray(),
            RetrievedAt: DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
        string externalAccountId,
        DateTimeOffset? since = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);

        var openOrdersTask = GetOpenOrdersAsync(ct);
        var activitiesTask = GetAccountActivitiesAsync(since, ct);

        await Task.WhenAll(openOrdersTask, activitiesTask).ConfigureAwait(false);

        var openOrders = await openOrdersTask.ConfigureAwait(false);
        var activities = await activitiesTask.ConfigureAwait(false);

        var fills = activities
            .Where(static activity => string.Equals(activity.ActivityType, "FILL", StringComparison.OrdinalIgnoreCase))
            .Select(activity => new BrokerageFillSnapshotDto(
                FillId: activity.Id ?? Guid.NewGuid().ToString("N"),
                OrderId: activity.OrderId,
                Symbol: activity.Symbol ?? string.Empty,
                Side: string.Equals(activity.Side, "sell", StringComparison.OrdinalIgnoreCase) ? OrderSide.Sell : OrderSide.Buy,
                Quantity: ParseDecimal(activity.Qty),
                Price: ParseDecimal(activity.Price ?? activity.PerShareAmount),
                FilledAt: ParseActivityTimestamp(activity),
                Venue: activity.Exchange,
                Commission: ParseNullableDecimal(activity.Commission)))
            .ToArray();

        var cashTransactions = activities
            .Where(static activity => !string.Equals(activity.ActivityType, "FILL", StringComparison.OrdinalIgnoreCase))
            .Select(activity => new BrokerageCashTransactionDto(
                TransactionId: activity.Id ?? Guid.NewGuid().ToString("N"),
                TransactionType: activity.ActivityType ?? "unknown",
                Amount: ParseDecimal(activity.NetAmount),
                Currency: activity.Currency ?? "USD",
                PostedAt: ParseActivityTimestamp(activity),
                Symbol: activity.Symbol,
                Description: activity.Description))
            .ToArray();

        return new BrokerageActivitySnapshotDto(
            ProviderId: GatewayId,
            AccountId: externalAccountId,
            RetrievedAt: DateTimeOffset.UtcNow,
            Orders: openOrders
                .Select(static order => new BrokerageOrderSnapshotDto(
                    OrderId: order.OrderId,
                    ClientOrderId: order.ClientOrderId,
                    Symbol: order.Symbol,
                    Side: order.Side,
                    Type: order.Type,
                    Status: order.Status,
                    Quantity: order.Quantity,
                    FilledQuantity: order.FilledQuantity,
                    LimitPrice: order.LimitPrice,
                    StopPrice: order.StopPrice,
                    CreatedAt: order.CreatedAt,
                    UpdatedAt: null))
                .ToArray(),
            Fills: fills,
            CashTransactions: cashTransactions);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        _connected = false;
        _reportChannel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private void EnsureConnected()
    {
        if (!_connected)
            throw new InvalidOperationException("Alpaca brokerage gateway is not connected. Call ConnectAsync first.");
    }

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient("AlpacaBrokerage");
        client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", _options.KeyId);
        client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", _options.SecretKey);
        return client;
    }

    private async Task<IReadOnlyList<AlpacaAccountActivityResponse>> GetAccountActivitiesAsync(
        DateTimeOffset? since,
        CancellationToken ct)
    {
        using var client = CreateHttpClient();
        var path = $"{BaseUrl}/v2/account/activities?direction=desc&page_size=100";
        if (since.HasValue)
        {
            path += $"&after={Uri.EscapeDataString(since.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}";
        }

        var response = await client.GetAsync(path, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var activities = await response.Content.ReadFromJsonAsync(
            AlpacaBrokerageSerializerContext.Default.AlpacaAccountActivityResponseArray, ct).ConfigureAwait(false);

        return activities ?? Array.Empty<AlpacaAccountActivityResponse>();
    }

    private static string MapOrderType(OrderType type) => type switch
    {
        OrderType.Market => "market",
        OrderType.Limit => "limit",
        OrderType.StopMarket => "stop",
        OrderType.StopLimit => "stop_limit",
        OrderType.MarketOnOpen or OrderType.MarketOnClose or OrderType.LimitOnOpen or OrderType.LimitOnClose
            => throw new NotSupportedException(
                $"Alpaca order mapping does not preserve the {type} session timing qualifier."),
        _ => throw new NotSupportedException($"Alpaca order mapping does not support {type}.")
    };

    private static void RejectSessionScopedOrderType(OrderType type)
    {
        if (type is OrderType.MarketOnOpen or OrderType.MarketOnClose or OrderType.LimitOnOpen or OrderType.LimitOnClose)
        {
            throw new NotSupportedException(
                $"Alpaca gateway does not currently preserve the {type} session timing qualifier.");
        }
    }

    private static string MapTimeInForce(TimeInForce tif) => tif switch
    {
        TimeInForce.Day => "day",
        TimeInForce.GoodTilCancelled => "gtc",
        TimeInForce.ImmediateOrCancel => "ioc",
        TimeInForce.FillOrKill => "fok",
        _ => "day"
    };

    private static OrderStatus MapAlpacaStatus(string? status) => status switch
    {
        "new" => OrderStatus.Accepted,
        "accepted" => OrderStatus.Accepted,
        "pending_new" => OrderStatus.PendingNew,
        "partially_filled" => OrderStatus.PartiallyFilled,
        "filled" => OrderStatus.Filled,
        "pending_cancel" => OrderStatus.PendingCancel,
        "canceled" => OrderStatus.Cancelled,
        "expired" => OrderStatus.Expired,
        "rejected" => OrderStatus.Rejected,
        _ => OrderStatus.PendingNew
    };

    private static OrderType ParseOrderType(string? type) => type switch
    {
        "market" => OrderType.Market,
        "limit" => OrderType.Limit,
        "stop" => OrderType.StopMarket,
        "stop_limit" => OrderType.StopLimit,
        _ => OrderType.Market
    };

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var result) ? result : 0m;

    private static decimal? ParseNullableDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var result) ? result : null;

    private static DateTimeOffset ParseActivityTimestamp(AlpacaAccountActivityResponse activity)
    {
        if (activity.TransactionTime.HasValue)
        {
            return activity.TransactionTime.Value;
        }

        if (activity.Date.HasValue)
        {
            return new DateTimeOffset(activity.Date.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }

        return DateTimeOffset.UtcNow;
    }

    // ── JSON DTOs (ADR-014: source generators) ─────────────────────────

    internal sealed class AlpacaOrderPayload
    {
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("qty")] public string? Qty { get; set; }
        /// <summary>Dollar-amount order size used for fixed income instruments.</summary>
        [JsonPropertyName("notional")] public string? Notional { get; set; }
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("time_in_force")] public string? TimeInForce { get; set; }
        [JsonPropertyName("limit_price")] public string? LimitPrice { get; set; }
        [JsonPropertyName("stop_price")] public string? StopPrice { get; set; }
        [JsonPropertyName("client_order_id")] public string? ClientOrderId { get; set; }
        /// <summary>Explicit asset class hint used for treasury/bond routing (e.g., "us_treasury").</summary>
        [JsonPropertyName("asset_class")] public string? AssetClass { get; set; }
    }

    internal sealed class AlpacaOrderModifyPayload
    {
        [JsonPropertyName("qty")] public string? Qty { get; set; }
        [JsonPropertyName("limit_price")] public string? LimitPrice { get; set; }
        [JsonPropertyName("stop_price")] public string? StopPrice { get; set; }
    }

    internal sealed class AlpacaOrderResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("client_order_id")] public string? ClientOrderId { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("qty")] public string? Qty { get; set; }
        [JsonPropertyName("filled_qty")] public string? FilledQty { get; set; }
        [JsonPropertyName("limit_price")] public string? LimitPrice { get; set; }
        [JsonPropertyName("stop_price")] public string? StopPrice { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    }

    internal sealed class AlpacaAccountResponse
    {
        [JsonPropertyName("account_number")] public string? AccountNumber { get; set; }
        [JsonPropertyName("equity")] public string? Equity { get; set; }
        [JsonPropertyName("cash")] public string? Cash { get; set; }
        [JsonPropertyName("buying_power")] public string? BuyingPower { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
    }

    internal sealed class AlpacaPositionResponse
    {
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("qty")] public string? Qty { get; set; }
        [JsonPropertyName("avg_entry_price")] public string? AvgEntryPrice { get; set; }
        [JsonPropertyName("current_price")] public string? CurrentPrice { get; set; }
        [JsonPropertyName("market_value")] public string? MarketValue { get; set; }
        [JsonPropertyName("unrealized_pl")] public string? UnrealizedPl { get; set; }
        [JsonPropertyName("asset_class")] public string? AssetClass { get; set; }
        /// <summary>Accrued interest for fixed income positions (bonds, treasuries).</summary>
        [JsonPropertyName("accrued_interest")] public string? AccruedInterest { get; set; }
    }

    internal sealed class AlpacaAccountActivityResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("activity_type")] public string? ActivityType { get; set; }
        [JsonPropertyName("transaction_time")] public DateTimeOffset? TransactionTime { get; set; }
        [JsonPropertyName("date")] public DateOnly? Date { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("qty")] public string? Qty { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("order_id")] public string? OrderId { get; set; }
        [JsonPropertyName("net_amount")] public string? NetAmount { get; set; }
        [JsonPropertyName("per_share_amount")] public string? PerShareAmount { get; set; }
        [JsonPropertyName("commission")] public string? Commission { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("exchange")] public string? Exchange { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }
}

/// <summary>
/// Source-generated JSON serializer context for Alpaca brokerage DTOs (ADR-014).
/// </summary>
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaOrderPayload))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaOrderModifyPayload))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaOrderResponse))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaOrderResponse[]))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaAccountResponse))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaPositionResponse[]))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaAccountActivityResponse[]))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class AlpacaBrokerageSerializerContext : JsonSerializerContext;
