using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Meridian.Core.Config;
using Meridian.Core.Pipeline;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.Logging;
using AlpacaOptions = Meridian.Core.Config.AlpacaOptions;
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
/// - Rate limit: 200 req/min
/// </remarks>
[DataSource("alpaca-brokerage", "Alpaca Brokerage", Meridian.Infrastructure.DataSources.DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 10, Description = "Alpaca Markets order execution gateway")]
[ImplementsAdr("ADR-001", "Alpaca brokerage provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory for HTTP connections")]
public sealed class AlpacaBrokerageGateway : IBrokerageGateway, IBrokerageAccountCatalog, IBrokeragePortfolioSync, IBrokerageActivitySync, INotionalOrderSizingGateway
{
    /// <inheritdoc />
    /// <remarks>
    /// Alpaca reads the notional metadata keys and sends the dollar amount as the order's
    /// size — see the <c>payload.Notional</c> assignment in <c>BuildOrderPayload</c> — but
    /// not for fixed income: the same builder clears <c>Notional</c> and restores face-value
    /// <c>Qty</c> for treasury and corporate. Reporting support for those would have the
    /// rails measure the metadata dollars while the broker receives the face value.
    /// </remarks>
    public bool RoutesNotionalMetadata(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return !IsFixedIncomeAssetClass(
            ReadMetadataString(request.Metadata, out var assetClass, "asset_class", "assetClass", "alpaca:asset_class")
                ? NormalizeAssetClass(assetClass)
                : null);
    }

    private const int AccountActivityPageSize = 100;

    private const string PaperBaseUrl = "https://paper-api.alpaca.markets";
    private const string LiveBaseUrl = "https://api.alpaca.markets";
    private const string BrokerApiSandboxBaseUrl = "https://broker-api.sandbox.alpaca.markets";
    private const string BrokerApiLiveBaseUrl = "https://broker-api.alpaca.markets";
    private const int AccountActivityPageSize = 100;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AlpacaOptions _options;
    private readonly ILogger<AlpacaBrokerageGateway> _logger;
    private readonly AlpacaTradeUpdatesClient? _tradeUpdates;
    private readonly Channel<ExecutionReport> _reportChannel;
    private volatile bool _connected;
    private bool _disposed;

    public AlpacaBrokerageGateway(
        IHttpClientFactory httpClientFactory,
        AlpacaOptions options,
        ILogger<AlpacaBrokerageGateway> logger,
        AlpacaTradeUpdatesClient? tradeUpdates = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tradeUpdates = tradeUpdates;
        _tradeUpdates?.ConfigureReconciliation(ReconcileExecutionSnapshotsAsync);

        if (!CurrentCredentials.HasCredentials)
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

    private AlpacaCredentialSnapshot CurrentCredentials => AlpacaCredentialEnvironment.Resolve(_options);

    private string BaseUrl => CurrentCredentials.UseSandbox ? PaperBaseUrl : LiveBaseUrl;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connected)
            return;

        if (!CurrentCredentials.HasCredentials)
            throw new InvalidOperationException(
                "Alpaca KeyId and SecretKey are required for brokerage. Configure credentials before calling ConnectAsync.");

        var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
        if (_tradeUpdates is not null)
            await _tradeUpdates.StartAsync(ct).ConfigureAwait(false);
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
        EnsureExecutionStreamHealthy();

        _logger.LogInformation(
            "Alpaca submitting order: {Side} {Quantity} {Symbol} @ {Type}",
            request.Side, request.Quantity, request.Symbol, request.Type);

        var payload = BuildOrderPayload(request);
        ValidateOrderPayload(request, payload);

        var endpoint = ResolveOrderEndpoint(request, payload);
        if (endpoint.RequiresBrokerApi)
        {
            payload.AssetClass = null;
        }

        using var client = endpoint.RequiresBrokerApi
            ? CreateBrokerApiHttpClient()
            : CreateHttpClient();
        var response = await client.PostAsJsonAsync(
            endpoint.Url, payload, AlpacaBrokerageSerializerContext.Default.AlpacaOrderPayload, ct)
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
            Qty = FormatDecimal(modification.NewQuantity),
            LimitPrice = FormatDecimal(modification.NewLimitPrice),
            StopPrice = FormatDecimal(modification.NewStopPrice),
            Trail = FormatDecimal(modification.NewTrail),
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
        if (_tradeUpdates is not null)
        {
            await foreach (var report in _tradeUpdates.Reports.WithCancellation(ct).ConfigureAwait(false))
                yield return report;
            yield break;
        }
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
            MarginMultiplier = ParseNullableDecimal(account?.Multiplier),
            RegTBuyingPower = ParseNullableDecimal(account?.RegTBuyingPower),
            InitialMargin = ParseNullableDecimal(account?.InitialMargin),
            MaintenanceMargin = ParseNullableDecimal(account?.MaintenanceMargin),
            LastMaintenanceMargin = ParseNullableDecimal(account?.LastMaintenanceMargin),
            SpecialMemorandumAccount = ParseNullableDecimal(account?.SpecialMemorandumAccount),
            LongMarketValue = ParseNullableDecimal(account?.LongMarketValue),
            ShortMarketValue = ParseNullableDecimal(account?.ShortMarketValue),
            NonMarginableBuyingPower = ParseNullableDecimal(account?.NonMarginableBuyingPower),
            TradingBlocked = account?.TradingBlocked ?? false,
            TransfersBlocked = account?.TransfersBlocked ?? false,
            AccountBlocked = account?.AccountBlocked ?? false,
            ShortingEnabled = account?.ShortingEnabled ?? false,
            OptionsApprovedLevel = account?.OptionsApprovedLevel,
            OptionsTradingLevel = account?.OptionsTradingLevel,
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
            if (_tradeUpdates is not null && !_tradeUpdates.IsHealthy)
                return BrokerHealthStatus.Unhealthy(_tradeUpdates.UnhealthyReason!);
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
                    ["environment"] = CurrentCredentials.Environment,
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

        var account = await RequireRequestedAccountAsync(externalAccountId, ct).ConfigureAwait(false);
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
                ["environment"] = CurrentCredentials.Environment
            });

        var retrievedAt = DateTimeOffset.UtcNow;
        var restrictions = BuildAccountRestrictions(account);
        var marginRegime = account.MarginMultiplier switch
        {
            <= 1m => BrokerageMarginRegime.Cash,
            > 1m => BrokerageMarginRegime.RegulationT,
            _ => BrokerageMarginRegime.Unknown
        };

        return new BrokeragePortfolioSnapshotDto(
            Account: accountDto,
            Balance: new BrokerageBalanceSnapshotDto(
                Cash: account.Cash,
                Equity: account.Equity,
                BuyingPower: account.BuyingPower,
                Currency: account.Currency,
                MarginBalance: Math.Max(0m, -account.Cash)),
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
            RetrievedAt: retrievedAt,
            AccountSnapshot: new BrokerageAccountSnapshotDto(
                ProviderId: GatewayId,
                AccountId: account.AccountId,
                AsOf: retrievedAt,
                Currency: account.Currency,
                Status: account.Status,
                MarginRegime: marginRegime,
                Cash: account.Cash,
                Equity: account.Equity,
                BuyingPower: account.BuyingPower,
                LongMarketValue: account.LongMarketValue,
                ShortMarketValue: account.ShortMarketValue,
                RegTBuyingPower: account.RegTBuyingPower,
                InitialMargin: account.InitialMargin,
                MaintenanceMargin: account.MaintenanceMargin,
                LastMaintenanceMargin: account.LastMaintenanceMargin,
                ExcessLiquidity: account.MaintenanceMargin.HasValue
                    ? account.Equity - account.MaintenanceMargin.Value
                    : null,
                SpecialMemorandumAccount: account.SpecialMemorandumAccount,
                MarginLoan: account.Cash < 0m ? -account.Cash : null,
                Multiplier: account.MarginMultiplier,
                TradingBlocked: account.TradingBlocked,
                TransfersBlocked: account.TransfersBlocked,
                AccountBlocked: account.AccountBlocked,
                ShortingEnabled: account.ShortingEnabled,
                OptionsApprovedLevel: account.OptionsApprovedLevel,
                OptionsTradingLevel: account.OptionsTradingLevel,
                Restrictions: restrictions,
                SourceAttributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["environment"] = CurrentCredentials.Environment,
                    ["sourceAuthority"] = "ProviderReported"
                }),
            BorrowPositions: positions
                .Where(static position => position.Quantity < 0m)
                .Select(position => new BrokerageBorrowPositionSnapshotDto(
                    Symbol: position.Symbol,
                    Quantity: position.Quantity,
                    Status: BrokerageBorrowStatus.Unknown,
                    Currency: account.Currency,
                    AccountId: account.AccountId))
                .ToArray());
    }

    /// <inheritdoc />
    public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
        string externalAccountId,
        DateTimeOffset? since = null,
        CancellationToken ct = default)
        => GetActivitySnapshotCoreAsync(externalAccountId, since, untilExclusive: null, ct);

    /// <inheritdoc />
    public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
        string externalAccountId,
        DateTimeOffset? since,
        DateTimeOffset? untilExclusive,
        CancellationToken ct = default)
        => GetActivitySnapshotCoreAsync(externalAccountId, since, untilExclusive, ct);

    private async Task<BrokerageActivitySnapshotDto> GetActivitySnapshotCoreAsync(
        string externalAccountId,
        DateTimeOffset? since,
        DateTimeOffset? untilExclusive,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);
        if (since.HasValue && untilExclusive.HasValue && untilExclusive.Value <= since.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(untilExclusive),
                untilExclusive,
                "The exclusive activity upper bound must be later than the lower bound.");
        }

        var account = await RequireRequestedAccountAsync(externalAccountId, ct).ConfigureAwait(false);
        var providerAfter = untilExclusive.HasValue && since is { } inclusiveStart
            ? inclusiveStart == DateTimeOffset.MinValue
                ? null
                : inclusiveStart.AddTicks(-1)
            : since;
        var openOrdersTask = GetOpenOrdersAsync(ct);
        var activitiesTask = GetAccountActivitiesAsync(providerAfter, untilExclusive, ct);

        await Task.WhenAll(openOrdersTask, activitiesTask).ConfigureAwait(false);

        var openOrders = await openOrdersTask.ConfigureAwait(false);
        var activities = await activitiesTask.ConfigureAwait(false);
        if (untilExclusive.HasValue)
        {
            activities = EnforceHalfOpenActivityWindow(activities, since, untilExclusive.Value);
        }

        var fills = activities
            .Where(static activity => string.Equals(activity.ActivityType, "FILL", StringComparison.OrdinalIgnoreCase))
            .Select(activity => new BrokerageFillSnapshotDto(
                FillId: BuildActivityId(activity),
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
                TransactionId: BuildActivityId(activity),
                TransactionType: activity.ActivityType ?? "unknown",
                Amount: ParseDecimal(activity.NetAmount),
                Currency: activity.Currency ?? "USD",
                PostedAt: ParseActivityTimestamp(activity),
                Symbol: activity.Symbol,
                Description: activity.Description))
            .ToArray();

        return new BrokerageActivitySnapshotDto(
            ProviderId: GatewayId,
            AccountId: account.AccountId,
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
            CashTransactions: cashTransactions,
            Activities: activities.Select(BuildCanonicalActivityEvent).ToArray(),
            Cursor: activityResult.Cursor);
    }

    private async Task<AccountInfo> RequireRequestedAccountAsync(
        string externalAccountId,
        CancellationToken ct)
    {
        var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(account.AccountId)
            || !string.Equals(
                account.AccountId.Trim(),
                externalAccountId.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The requested Alpaca account does not match the account resolved by the configured credentials.");
        }

        return account;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        _connected = false;
        _reportChannel.Writer.TryComplete();
        return _tradeUpdates?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private void EnsureConnected()
    {
        if (!_connected)
            throw new InvalidOperationException("Alpaca brokerage gateway is not connected. Call ConnectAsync first.");
    }

    private void EnsureExecutionStreamHealthy()
    {
        if (_tradeUpdates is not null && !_tradeUpdates.IsHealthy)
            throw new InvalidOperationException($"Alpaca live submission is blocked because the execution stream is unhealthy: {_tradeUpdates.UnhealthyReason}");
    }

    // Reconciliation deliberately reads the broker snapshot after every socket reconnect so orders
    // created outside Meridian and updates missed during the disconnect become immutable reports.
    private async Task<IReadOnlyList<ExecutionReport>> ReconcileExecutionSnapshotsAsync(CancellationToken ct)
    {
        var orders = await GetOpenOrdersAsync(ct).ConfigureAwait(false);
        return orders.Select(order => new ExecutionReport
        {
            OrderId = order.OrderId,
            GatewayOrderId = order.OrderId,
            ClientOrderId = order.ClientOrderId,
            Symbol = order.Symbol,
            Side = order.Side,
            OrderQuantity = order.Quantity,
            FilledQuantity = order.FilledQuantity,
            OrderStatus = order.Status,
            ReportType = order.Status == OrderStatus.PartiallyFilled ? ExecutionReportType.PartialFill : ExecutionReportType.New,
            Timestamp = order.CreatedAt,
            Diagnostics = new ExecutionDiagnostics { Category = "alpaca-rest-reconciliation", RecommendedAction = "Reconciled after execution-stream reconnect." }
        }).ToArray();
    }

    private HttpClient CreateHttpClient()
    {
        var credentials = CurrentCredentials;
        if (!credentials.HasCredentials)
        {
            throw new InvalidOperationException(
                "Alpaca KeyId and SecretKey are required for brokerage. Configure credentials before calling Alpaca Trading API.");
        }

        var client = _httpClientFactory.CreateClient("AlpacaBrokerage");
        client.DefaultRequestHeaders.TryAddWithoutValidation("APCA-API-KEY-ID", credentials.KeyId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("APCA-API-SECRET-KEY", credentials.SecretKey);
        return client;
    }

    private HttpClient CreateBrokerApiHttpClient()
    {
        var credentials = CurrentCredentials;
        if (!credentials.HasCredentials)
        {
            throw new InvalidOperationException(
                "Alpaca Broker API KeyId and SecretKey are required for fixed-income brokerage orders.");
        }

        var client = _httpClientFactory.CreateClient("AlpacaBrokerage");
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{credentials.KeyId}:{credentials.SecretKey}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        return client;
    }

    private async Task<IReadOnlyList<AlpacaAccountActivityResponse>> GetAccountActivitiesAsync(
        DateTimeOffset? after,
        DateTimeOffset? untilExclusive,
        CancellationToken ct)
    {
        using var client = CreateHttpClient();
        var basePath = $"{BaseUrl}/v2/account/activities?direction=desc&page_size={AccountActivityPageSize}";
        if (after.HasValue)
        {
            basePath += $"&after={Uri.EscapeDataString(after.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}";
        }
        if (untilExclusive.HasValue)
        {
            basePath += $"&until={Uri.EscapeDataString(untilExclusive.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}";
        }

        var activities = new List<AlpacaAccountActivityResponse>();
        var observedPageTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var path = pageToken is null
                ? basePath
                : $"{basePath}&page_token={Uri.EscapeDataString(pageToken)}";
            using var response = await client.GetAsync(path, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var page = await response.Content.ReadFromJsonAsync(
                    AlpacaBrokerageSerializerContext.Default.AlpacaAccountActivityResponseArray,
                    ct)
                .ConfigureAwait(false) ?? [];
            activities.AddRange(page);
            if (page.Length < AccountActivityPageSize)
            {
                return activities;
            }

            var nextPageToken = page[^1].Id;
            if (string.IsNullOrWhiteSpace(nextPageToken))
            {
                throw new InvalidDataException(
                    "Alpaca activity pagination returned a full page without a terminal activity id.");
            }

            if (!observedPageTokens.Add(nextPageToken))
            {
                throw new InvalidDataException(
                    "Alpaca activity pagination repeated a page token before reaching a short page.");
            }

            pageToken = nextPageToken;
        }
    }

    private static IReadOnlyList<AlpacaAccountActivityResponse> EnforceHalfOpenActivityWindow(
        IReadOnlyList<AlpacaAccountActivityResponse> activities,
        DateTimeOffset? since,
        DateTimeOffset untilExclusive)
    {
        var retained = new List<AlpacaAccountActivityResponse>(activities.Count);
        foreach (var activity in activities)
        {
            if (!TryGetActivityTimestamp(activity, out var occurredAt))
            {
                throw new InvalidDataException(
                    "Alpaca activity response omitted the timestamp required to enforce an exact statement period.");
            }

            if ((!since.HasValue || occurredAt >= since.Value) && occurredAt < untilExclusive)
            {
                retained.Add(activity);
            }
        }

        return retained;
    }

    private static AlpacaOrderPayload BuildOrderPayload(OrderRequest request)
    {
        var payload = new AlpacaOrderPayload
        {
            Symbol = request.Legs is { Count: > 0 } ? null : request.Symbol,
            Qty = request.Quantity.ToString("G", CultureInfo.InvariantCulture),
            Side = request.Side == OrderSide.Buy ? "buy" : "sell",
            Type = MapOrderType(request.Type),
            TimeInForce = MapTimeInForce(request),
            LimitPrice = FormatDecimal(request.LimitPrice),
            StopPrice = FormatDecimal(request.StopPrice),
            ClientOrderId = request.ClientOrderId,
            ExtendedHours = ReadMetadataBool(request.Metadata, "extended_hours", "extendedHours", "alpaca:extended_hours"),
        };

        if (ReadMetadataString(request.Metadata, out var orderClass, "order_class", "orderClass", "alpaca:order_class"))
        {
            payload.OrderClass = NormalizeOrderClass(orderClass);
        }

        if (request.Legs is { Count: > 0 } legs)
        {
            payload.OrderClass = "mleg";
            payload.Legs = legs.Select(MapOrderLeg).ToArray();
        }

        if (ReadMetadataString(request.Metadata, out var assetClass, "asset_class", "assetClass", "alpaca:asset_class"))
        {
            payload.AssetClass = NormalizeAssetClass(assetClass);
        }
        else if (LooksLikeOptionSymbol(request.Symbol))
        {
            payload.AssetClass = "us_option";
        }

        var notional = ReadMetadataDecimal(request.Metadata, "notional", "alpaca:notional")
            ?? (ReadMetadataBool(request.Metadata, "notional", "alpaca:notional") == true ? request.Quantity : null);
        if (notional.HasValue)
        {
            payload.Notional = FormatDecimal(notional);
            payload.Qty = null;
        }

        if (request.PositionIntent.HasValue)
        {
            payload.PositionIntent = MapPositionIntent(request.PositionIntent.Value);
        }
        else if (ReadMetadataString(request.Metadata, out var positionIntent, "position_intent", "positionIntent", "alpaca:position_intent"))
        {
            payload.PositionIntent = NormalizePositionIntent(positionIntent);
        }

        var takeProfitLimit = ReadMetadataDecimal(request.Metadata, "take_profit.limit_price", "take_profit_limit_price", "alpaca:take_profit_limit_price");
        if (takeProfitLimit.HasValue)
        {
            payload.TakeProfit = new AlpacaTakeProfitPayload
            {
                LimitPrice = FormatDecimal(takeProfitLimit),
            };
        }

        var stopLossStop = ReadMetadataDecimal(request.Metadata, "stop_loss.stop_price", "stop_loss_stop_price", "alpaca:stop_loss_stop_price");
        var stopLossLimit = ReadMetadataDecimal(request.Metadata, "stop_loss.limit_price", "stop_loss_limit_price", "alpaca:stop_loss_limit_price");
        if (stopLossStop.HasValue || stopLossLimit.HasValue)
        {
            payload.StopLoss = new AlpacaStopLossPayload
            {
                StopPrice = FormatDecimal(stopLossStop),
                LimitPrice = FormatDecimal(stopLossLimit),
            };
        }

        payload.TrailPrice = FormatDecimal(request.TrailPrice)
            ?? FormatDecimal(ReadMetadataDecimal(request.Metadata, "trail_price", "trailPrice", "alpaca:trail_price"));
        payload.TrailPercent = FormatDecimal(request.TrailPercent)
            ?? FormatDecimal(ReadMetadataDecimal(request.Metadata, "trail_percent", "trailPercent", "alpaca:trail_percent"));

        return payload;
    }

    private static void ValidateOrderPayload(OrderRequest request, AlpacaOrderPayload payload)
    {
        if (request.Type is OrderType.TrailingStop)
        {
            var hasTrailPrice = !string.IsNullOrWhiteSpace(payload.TrailPrice);
            var hasTrailPercent = !string.IsNullOrWhiteSpace(payload.TrailPercent);
            if (hasTrailPrice == hasTrailPercent)
            {
                throw new NotSupportedException("Alpaca trailing stop orders require exactly one of TrailPrice or TrailPercent.");
            }

            if (!string.IsNullOrWhiteSpace(payload.OrderClass) && payload.OrderClass != "simple")
            {
                throw new NotSupportedException("Alpaca currently supports trailing stops only as simple orders.");
            }
        }

        if (IsFixedIncomeAssetClass(payload.AssetClass))
        {
            if (request.Type is not (OrderType.Market or OrderType.Limit))
            {
                throw new NotSupportedException("Alpaca fixed-income orders support only market and limit orders.");
            }

            if (payload.TimeInForce != "day")
            {
                throw new NotSupportedException("Alpaca fixed-income orders require day time in force.");
            }

            if (request.Quantity < 1000m || request.Quantity > 1_000_000m || request.Quantity % 1000m != 0m)
            {
                throw new NotSupportedException("Alpaca fixed-income order quantity must be full face-value denominations from 1000 through 1000000.");
            }

            if (!ReadMetadataString(request.Metadata, out _, "broker_account_id", "brokerAccountId", "account_id", "accountId", "alpaca:broker_account_id"))
            {
                throw new NotSupportedException("Alpaca fixed-income orders require Broker API account id metadata: broker_account_id.");
            }

            payload.Notional = null;
            payload.Qty = request.Quantity.ToString("G", CultureInfo.InvariantCulture);
            payload.ExtendedHours = null;
        }

        if (IsOptionsOrder(payload))
        {
            if (payload.OrderClass == "mleg")
            {
                if (request.Type is not (OrderType.Market or OrderType.Limit))
                {
                    throw new NotSupportedException("Alpaca multi-leg options orders support only market and limit orders.");
                }

                if (payload.Legs is null || payload.Legs.Length is < 2 or > 4)
                {
                    throw new NotSupportedException("Alpaca multi-leg options orders require two to four legs.");
                }

                ValidateMlegRatioQuantities(payload.Legs);
            }
            else if (request.Type is not (OrderType.Market or OrderType.Limit or OrderType.StopMarket or OrderType.StopLimit))
            {
                throw new NotSupportedException("Alpaca single-leg options orders support market, limit, stop, and stop-limit orders.");
            }

            if (payload.TimeInForce is not ("day" or "gtc"))
            {
                throw new NotSupportedException("Alpaca options orders require day or gtc time in force.");
            }

            if (payload.ExtendedHours == true)
            {
                throw new NotSupportedException("Alpaca options orders do not support extended-hours execution.");
            }

            if (payload.Notional is not null)
            {
                throw new NotSupportedException("Alpaca options orders do not support notional sizing.");
            }

            if (request.Quantity <= 0m || request.Quantity != decimal.Truncate(request.Quantity))
            {
                throw new NotSupportedException("Alpaca options order quantity must be a positive whole number.");
            }
        }

        if (payload.ExtendedHours == true)
        {
            if (request.Type is not OrderType.Limit)
            {
                throw new NotSupportedException("Alpaca extended-hours orders must be limit orders.");
            }

            if (request.TimeInForce is not (TimeInForce.Day or TimeInForce.GoodTilCancelled))
            {
                throw new NotSupportedException("Alpaca extended-hours orders require Day or GoodTilCancelled time in force.");
            }
        }

        if (string.IsNullOrWhiteSpace(payload.OrderClass) || payload.OrderClass == "simple")
        {
            return;
        }

        if (payload.ExtendedHours == true)
        {
            throw new NotSupportedException("Alpaca advanced order classes do not support extended-hours execution.");
        }

        if (payload.OrderClass is "bracket" or "oco")
        {
            if (payload.TakeProfit?.LimitPrice is null || payload.StopLoss?.StopPrice is null)
            {
                throw new NotSupportedException($"Alpaca {payload.OrderClass} orders require take_profit.limit_price and stop_loss.stop_price.");
            }
        }

        if (payload.OrderClass == "oco" && request.Type is not OrderType.Limit)
        {
            throw new NotSupportedException("Alpaca OCO orders must be submitted as limit orders.");
        }

        if (payload.OrderClass == "oto" && payload.TakeProfit is null && payload.StopLoss is null)
        {
            throw new NotSupportedException("Alpaca OTO orders require a take_profit or stop_loss leg.");
        }
    }

    private static string MapOrderType(OrderType type) => type switch
    {
        OrderType.Market => "market",
        OrderType.Limit => "limit",
        OrderType.StopMarket => "stop",
        OrderType.StopLimit => "stop_limit",
        OrderType.MarketOnOpen or OrderType.MarketOnClose => "market",
        OrderType.LimitOnOpen or OrderType.LimitOnClose => "limit",
        OrderType.TrailingStop => "trailing_stop",
        _ => throw new NotSupportedException($"Alpaca order mapping does not support {type}.")
    };

    private static string MapTimeInForce(OrderRequest request) => request.Type switch
    {
        OrderType.MarketOnOpen or OrderType.LimitOnOpen => "opg",
        OrderType.MarketOnClose or OrderType.LimitOnClose => "cls",
        _ => request.TimeInForce switch
        {
            TimeInForce.Day => "day",
            TimeInForce.GoodTilCancelled => "gtc",
            TimeInForce.ImmediateOrCancel => "ioc",
            TimeInForce.FillOrKill => "fok",
            _ => "day"
        }
    };

    private static string NormalizeOrderClass(string orderClass) => orderClass.Trim().ToLowerInvariant() switch
    {
        "simple" or "bracket" or "oco" or "oto" => orderClass.Trim().ToLowerInvariant(),
        "mleg" => "mleg",
        _ => throw new NotSupportedException($"Alpaca order_class '{orderClass}' is not supported.")
    };

    private static string NormalizeAssetClass(string assetClass) => assetClass.Trim().ToLowerInvariant() switch
    {
        "equity" or "us_equity" => "us_equity",
        "option" or "options" or "us_option" => "us_option",
        "treasury" or "us_treasury" or "fixed_income_treasury" => "treasury",
        "corporate" or "bond" or "corporate_bond" or "us_corporate" => "corporate",
        _ => assetClass.Trim().ToLowerInvariant()
    };

    private static AlpacaOrderLegPayload MapOrderLeg(OrderLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);
        if (string.IsNullOrWhiteSpace(leg.Symbol))
            throw new NotSupportedException("Alpaca option legs require a symbol.");

        if (leg.RatioQuantity <= 0m || leg.RatioQuantity != decimal.Truncate(leg.RatioQuantity))
            throw new NotSupportedException("Alpaca option leg ratio quantities must be positive whole numbers.");

        return new AlpacaOrderLegPayload
        {
            Symbol = leg.Symbol,
            RatioQty = FormatDecimal(leg.RatioQuantity),
            Side = leg.Side == OrderSide.Buy ? "buy" : "sell",
            PositionIntent = leg.PositionIntent.HasValue
                ? MapPositionIntent(leg.PositionIntent.Value)
                : null,
        };
    }

    private static string MapPositionIntent(PositionIntent positionIntent) => positionIntent switch
    {
        PositionIntent.BuyToOpen => "buy_to_open",
        PositionIntent.BuyToClose => "buy_to_close",
        PositionIntent.SellToOpen => "sell_to_open",
        PositionIntent.SellToClose => "sell_to_close",
        _ => throw new NotSupportedException($"Unsupported Alpaca position intent {positionIntent}.")
    };

    private static string NormalizePositionIntent(string positionIntent) => positionIntent.Trim().ToLowerInvariant() switch
    {
        "buy_to_open" or "buytoopen" => "buy_to_open",
        "buy_to_close" or "buytoclose" => "buy_to_close",
        "sell_to_open" or "selltoopen" => "sell_to_open",
        "sell_to_close" or "selltoclose" => "sell_to_close",
        _ => throw new NotSupportedException($"Alpaca position_intent '{positionIntent}' is not supported.")
    };

    private static bool LooksLikeOptionSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return false;

        var trimmed = symbol.Trim();
        return trimmed.Length >= 15 &&
               (trimmed.Contains("C", StringComparison.Ordinal) || trimmed.Contains("P", StringComparison.Ordinal)) &&
               trimmed.Any(char.IsDigit);
    }

    private static bool IsFixedIncomeAssetClass(string? assetClass) =>
        assetClass is "treasury" or "corporate";

    private static bool IsOptionsOrder(AlpacaOrderPayload payload) =>
        payload.OrderClass == "mleg" || payload.AssetClass == "us_option";

    private OrderEndpoint ResolveOrderEndpoint(OrderRequest request, AlpacaOrderPayload payload)
    {
        if (!IsFixedIncomeAssetClass(payload.AssetClass))
        {
            return new OrderEndpoint($"{BaseUrl}/v2/orders", RequiresBrokerApi: false);
        }

        if (!ReadMetadataString(request.Metadata, out var accountId, "broker_account_id", "brokerAccountId", "account_id", "accountId", "alpaca:broker_account_id"))
        {
            throw new NotSupportedException("Alpaca fixed-income orders require Broker API account id metadata: broker_account_id.");
        }

        var brokerBaseUrl = CurrentCredentials.UseSandbox ? BrokerApiSandboxBaseUrl : BrokerApiLiveBaseUrl;
        return new OrderEndpoint(
            $"{brokerBaseUrl}/v1/trading/accounts/{Uri.EscapeDataString(accountId)}/orders",
            RequiresBrokerApi: true);
    }

    private static void ValidateMlegRatioQuantities(IReadOnlyList<AlpacaOrderLegPayload> legs)
    {
        var values = legs
            .Select(static leg => decimal.TryParse(leg.RatioQty, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0m)
            .ToArray();

        if (values.Any(static value => value <= 0m || value != decimal.Truncate(value)))
            throw new NotSupportedException("Alpaca multi-leg option ratios must be positive whole numbers.");

        var gcd = values.Select(static value => (long)value).Aggregate(GreatestCommonDivisor);
        if (gcd > 1)
            throw new NotSupportedException("Alpaca multi-leg option ratios must be simplified so their greatest common divisor is 1.");
    }

    private static long GreatestCommonDivisor(long left, long right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
        {
            var temp = right;
            right = left % right;
            left = temp;
        }

        return left;
    }

    private sealed record OrderEndpoint(string Url, bool RequiresBrokerApi);

    private static string? FormatDecimal(decimal? value) =>
        value?.ToString("G", CultureInfo.InvariantCulture);

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

    private static IReadOnlyList<string> BuildAccountRestrictions(AccountInfo account)
    {
        var restrictions = new List<string>();
        if (account.TradingBlocked)
            restrictions.Add("Trading blocked");
        if (account.TransfersBlocked)
            restrictions.Add("Transfers blocked");
        if (account.AccountBlocked)
            restrictions.Add("Account blocked");
        if (!string.Equals(account.Status, "active", StringComparison.OrdinalIgnoreCase))
            restrictions.Add($"Account status: {account.Status}");
        return restrictions.AsReadOnly();
    }

    private static string BuildActivityId(AlpacaAccountActivityResponse activity)
    {
        if (!string.IsNullOrWhiteSpace(activity.Id))
            return activity.Id;

        return FormattableString.Invariant(
            $"alpaca:{activity.ActivityType ?? "unknown"}:{ParseActivityTimestamp(activity):O}:{activity.OrderId}:{activity.Symbol}:{activity.NetAmount}:{activity.Qty}:{activity.Price}:{activity.Description}");
    }

    private static BrokerageActivityEventDto BuildCanonicalActivityEvent(AlpacaAccountActivityResponse activity)
    {
        var providerCode = activity.ActivityType?.Trim().ToUpperInvariant() ?? "UNKNOWN";
        var (category, subtype) = MapActivityType(providerCode, activity.Description);
        var option = category == BrokerageActivityCategory.OptionLifecycle
            ? BuildOptionLifecycle(activity.Symbol, subtype)
            : null;

        return new BrokerageActivityEventDto(
            EventId: BuildActivityId(activity),
            ProviderCode: providerCode,
            Category: category,
            Subtype: subtype,
            EffectiveAt: ParseActivityTimestamp(activity),
            Currency: activity.Currency ?? "USD",
            NetAmount: ParseDecimal(activity.NetAmount),
            Symbol: activity.Symbol,
            Quantity: ParseSignedActivityQuantity(activity),
            Price: ParseNullableDecimal(activity.Price ?? activity.PerShareAmount),
            OrderId: activity.OrderId,
            RelatedEventId: activity.TradeId,
            Description: activity.Description,
            Option: option,
            Metadata: BuildActivityMetadata(activity, providerCode));
    }

    private static decimal? ParseSignedActivityQuantity(AlpacaAccountActivityResponse activity)
    {
        var quantity = ParseNullableDecimal(activity.Qty);
        if (!quantity.HasValue)
            return null;

        return string.Equals(activity.Side, "sell", StringComparison.OrdinalIgnoreCase)
            ? -Math.Abs(quantity.Value)
            : Math.Abs(quantity.Value);
    }

    private static IReadOnlyDictionary<string, string> BuildActivityMetadata(
        AlpacaAccountActivityResponse activity,
        string providerCode)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceAuthority"] = "ProviderReported",
            ["providerActivityCode"] = providerCode
        };
        if (!string.IsNullOrWhiteSpace(activity.Side))
            metadata["side"] = activity.Side;
        if (!string.IsNullOrWhiteSpace(activity.Commission))
            metadata["commission"] = activity.Commission;
        if (!string.IsNullOrWhiteSpace(activity.Exchange))
            metadata["exchange"] = activity.Exchange;
        return metadata;
    }

    private static (BrokerageActivityCategory Category, BrokerageActivitySubtype Subtype) MapActivityType(
        string providerCode,
        string? description)
    {
        return providerCode switch
        {
            "FILL" or "TRADE" => (BrokerageActivityCategory.Trade, BrokerageActivitySubtype.TradeFill),
            "COR" or "TRADE_CORRECTION" => (BrokerageActivityCategory.Trade, BrokerageActivitySubtype.TradeCorrection),
            "BUST" or "TRADE_BUST" => (BrokerageActivityCategory.Trade, BrokerageActivitySubtype.TradeBust),
            "CSD" => (BrokerageActivityCategory.Cash, BrokerageActivitySubtype.CashDeposit),
            "CSW" => (BrokerageActivityCategory.Cash, BrokerageActivitySubtype.CashWithdrawal),
            "FEE" or "DIVFEE" => (BrokerageActivityCategory.Fee, BrokerageActivitySubtype.Fee),
            "CFEE" => (BrokerageActivityCategory.Fee, BrokerageActivitySubtype.CryptoFee),
            "PTC" => (BrokerageActivityCategory.Fee, BrokerageActivitySubtype.PassThroughCharge),
            "PTR" => (BrokerageActivityCategory.Fee, BrokerageActivitySubtype.PassThroughRebate),
            "INT" => MapInterestActivity(description),
            "DIV" or "DIVFT" => (BrokerageActivityCategory.Dividend, BrokerageActivitySubtype.CashDividend),
            "DIVSTOCK" => (BrokerageActivityCategory.Dividend, BrokerageActivitySubtype.StockDividend),
            "DIVCGL" or "DIVCGS" => (BrokerageActivityCategory.Dividend, BrokerageActivitySubtype.CapitalGainDistribution),
            "DIVROC" => (BrokerageActivityCategory.Dividend, BrokerageActivitySubtype.ReturnOfCapital),
            "DIVNRA" or "DIVTW" or "DIVTXEX" => (BrokerageActivityCategory.Tax, BrokerageActivitySubtype.DividendWithholding),
            "ACATC" => (BrokerageActivityCategory.Transfer, BrokerageActivitySubtype.AcatsCash),
            "ACATS" => (BrokerageActivityCategory.Transfer, BrokerageActivitySubtype.AcatsSecurity),
            "TRANS" => (BrokerageActivityCategory.Transfer, BrokerageActivitySubtype.CashTransfer),
            "JNL" or "JNLC" => (BrokerageActivityCategory.Journal, BrokerageActivitySubtype.CashJournal),
            "JNLS" or "FOPT" => (BrokerageActivityCategory.Journal, BrokerageActivitySubtype.SecurityJournal),
            "OPASN" => (BrokerageActivityCategory.OptionLifecycle, BrokerageActivitySubtype.OptionAssignment),
            "OPXRC" => (BrokerageActivityCategory.OptionLifecycle, BrokerageActivitySubtype.OptionExercise),
            "OPEXP" => (BrokerageActivityCategory.OptionLifecycle, BrokerageActivitySubtype.OptionExpiration),
            "SSP" => (BrokerageActivityCategory.CorporateAction, BrokerageActivitySubtype.StockSplit),
            "SSO" or "SPIN" => (BrokerageActivityCategory.CorporateAction, BrokerageActivitySubtype.Spinoff),
            "MA" => (BrokerageActivityCategory.CorporateAction, BrokerageActivitySubtype.Merger),
            "NC" or "SC" => (BrokerageActivityCategory.CorporateAction, BrokerageActivitySubtype.SymbolChange),
            "REORG" => (BrokerageActivityCategory.CorporateAction, BrokerageActivitySubtype.Reorganization),
            "HTB" or "BORROW_FEE" => (BrokerageActivityCategory.Borrow, BrokerageActivitySubtype.BorrowFee),
            "BORROW_REBATE" => (BrokerageActivityCategory.Borrow, BrokerageActivitySubtype.BorrowRebate),
            "TAX" or "TAX_WITHHOLDING" => (BrokerageActivityCategory.Tax, BrokerageActivitySubtype.TaxWithholding),
            _ => (BrokerageActivityCategory.Other, BrokerageActivitySubtype.Other)
        };
    }

    private static (BrokerageActivityCategory Category, BrokerageActivitySubtype Subtype) MapInterestActivity(
        string? description)
    {
        if (description?.Contains("borrow", StringComparison.OrdinalIgnoreCase) == true ||
            description?.Contains("short", StringComparison.OrdinalIgnoreCase) == true)
        {
            return (BrokerageActivityCategory.Borrow, BrokerageActivitySubtype.BorrowFee);
        }

        if (description?.Contains("margin", StringComparison.OrdinalIgnoreCase) == true)
        {
            return (BrokerageActivityCategory.Interest, BrokerageActivitySubtype.MarginInterest);
        }

        return (BrokerageActivityCategory.Interest, BrokerageActivitySubtype.CreditInterest);
    }

    private static BrokerageOptionLifecycleSnapshotDto BuildOptionLifecycle(
        string? contractId,
        BrokerageActivitySubtype subtype)
    {
        var lifecycleAction = subtype switch
        {
            BrokerageActivitySubtype.OptionAssignment => "Assignment",
            BrokerageActivitySubtype.OptionExercise => "Exercise",
            BrokerageActivitySubtype.OptionExpiration => "Expiration",
            _ => "Other"
        };

        if (string.IsNullOrWhiteSpace(contractId) || contractId.Length <= 15)
        {
            return new BrokerageOptionLifecycleSnapshotDto(
                ContractId: contractId ?? string.Empty,
                UnderlyingSymbol: null,
                OptionType: null,
                StrikePrice: null,
                ExpirationDate: null,
                ContractMultiplier: 100m,
                LifecycleAction: lifecycleAction);
        }

        var suffix = contractId[^15..];
        var underlying = contractId[..^15].Trim();
        var expiration = DateOnly.TryParseExact(
            suffix[..6],
            "yyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedExpiration)
            ? parsedExpiration
            : (DateOnly?)null;
        var optionType = suffix[6] switch
        {
            'C' => "Call",
            'P' => "Put",
            _ => null
        };
        var strikePrice = decimal.TryParse(
            suffix[7..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsedStrike)
            ? parsedStrike / 1_000m
            : (decimal?)null;

        return new BrokerageOptionLifecycleSnapshotDto(
            ContractId: contractId,
            UnderlyingSymbol: string.IsNullOrWhiteSpace(underlying) ? null : underlying,
            OptionType: optionType,
            StrikePrice: strikePrice,
            ExpirationDate: expiration,
            ContractMultiplier: 100m,
            LifecycleAction: lifecycleAction);
    }

    private static DateTimeOffset ParseActivityTimestamp(AlpacaAccountActivityResponse activity)
    {
        return TryGetActivityTimestamp(activity, out var occurredAt)
            ? occurredAt
            : DateTimeOffset.UtcNow;
    }

    private static bool TryGetActivityTimestamp(
        AlpacaAccountActivityResponse activity,
        out DateTimeOffset occurredAt)
    {
        if (activity.TransactionTime.HasValue)
        {
            occurredAt = activity.TransactionTime.Value;
            return true;
        }

        if (activity.Date.HasValue)
        {
            occurredAt = new DateTimeOffset(
                activity.Date.Value.ToDateTime(TimeOnly.MinValue),
                TimeSpan.Zero);
            return true;
        }

        occurredAt = default;
        return false;
    }

    private static bool ReadMetadataString(
        IReadOnlyDictionary<string, string>? metadata,
        out string value,
        params string[] keys)
    {
        value = string.Empty;
        if (metadata is null)
            return false;

        foreach (var key in keys)
        {
            if (metadata.TryGetValue(key, out var directValue) &&
                !string.IsNullOrWhiteSpace(directValue))
            {
                value = directValue;
                return true;
            }

            foreach (var kvp in metadata)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    value = kvp.Value;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool? ReadMetadataBool(
        IReadOnlyDictionary<string, string>? metadata,
        params string[] keys)
    {
        if (!ReadMetadataString(metadata, out var value, keys))
            return null;

        var normalized = value.Trim().ToLowerInvariant();
        return bool.TryParse(normalized, out var parsed)
            ? parsed
            : normalized is "1" or "yes" or "y";
    }

    private static decimal? ReadMetadataDecimal(
        IReadOnlyDictionary<string, string>? metadata,
        params string[] keys)
    {
        if (!ReadMetadataString(metadata, out var value, keys))
            return null;

        return decimal.TryParse(
            value,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
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
        [JsonPropertyName("extended_hours")] public bool? ExtendedHours { get; set; }
        [JsonPropertyName("order_class")] public string? OrderClass { get; set; }
        [JsonPropertyName("legs")] public AlpacaOrderLegPayload[]? Legs { get; set; }
        [JsonPropertyName("take_profit")] public AlpacaTakeProfitPayload? TakeProfit { get; set; }
        [JsonPropertyName("stop_loss")] public AlpacaStopLossPayload? StopLoss { get; set; }
        [JsonPropertyName("trail_price")] public string? TrailPrice { get; set; }
        [JsonPropertyName("trail_percent")] public string? TrailPercent { get; set; }
        [JsonPropertyName("position_intent")] public string? PositionIntent { get; set; }
        /// <summary>Explicit asset class hint used for treasury/bond routing (e.g., "us_treasury").</summary>
        [JsonPropertyName("asset_class")] public string? AssetClass { get; set; }
    }

    internal sealed class AlpacaOrderModifyPayload
    {
        [JsonPropertyName("qty")] public string? Qty { get; set; }
        [JsonPropertyName("limit_price")] public string? LimitPrice { get; set; }
        [JsonPropertyName("stop_price")] public string? StopPrice { get; set; }
        [JsonPropertyName("trail")] public string? Trail { get; set; }
    }

    internal sealed class AlpacaTakeProfitPayload
    {
        [JsonPropertyName("limit_price")] public string? LimitPrice { get; set; }
    }

    internal sealed class AlpacaStopLossPayload
    {
        [JsonPropertyName("stop_price")] public string? StopPrice { get; set; }
        [JsonPropertyName("limit_price")] public string? LimitPrice { get; set; }
    }

    internal sealed class AlpacaOrderLegPayload
    {
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("ratio_qty")] public string? RatioQty { get; set; }
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("position_intent")] public string? PositionIntent { get; set; }
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
        [JsonPropertyName("multiplier")] public string? Multiplier { get; set; }
        [JsonPropertyName("regt_buying_power")] public string? RegTBuyingPower { get; set; }
        [JsonPropertyName("initial_margin")] public string? InitialMargin { get; set; }
        [JsonPropertyName("maintenance_margin")] public string? MaintenanceMargin { get; set; }
        [JsonPropertyName("last_maintenance_margin")] public string? LastMaintenanceMargin { get; set; }
        [JsonPropertyName("sma")] public string? SpecialMemorandumAccount { get; set; }
        [JsonPropertyName("long_market_value")] public string? LongMarketValue { get; set; }
        [JsonPropertyName("short_market_value")] public string? ShortMarketValue { get; set; }
        [JsonPropertyName("non_marginable_buying_power")] public string? NonMarginableBuyingPower { get; set; }
        [JsonPropertyName("trading_blocked")] public bool? TradingBlocked { get; set; }
        [JsonPropertyName("transfers_blocked")] public bool? TransfersBlocked { get; set; }
        [JsonPropertyName("account_blocked")] public bool? AccountBlocked { get; set; }
        [JsonPropertyName("shorting_enabled")] public bool? ShortingEnabled { get; set; }
        [JsonPropertyName("options_approved_level")] public int? OptionsApprovedLevel { get; set; }
        [JsonPropertyName("options_trading_level")] public int? OptionsTradingLevel { get; set; }
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
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
        [JsonPropertyName("transaction_time")] public DateTimeOffset? TransactionTime { get; set; }
        [JsonPropertyName("date")] public DateOnly? Date { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("qty")] public string? Qty { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("order_id")] public string? OrderId { get; set; }
        [JsonPropertyName("trade_id")] public string? TradeId { get; set; }
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
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaTakeProfitPayload))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaStopLossPayload))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaOrderLegPayload))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaOrderLegPayload[]))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaOrderResponse))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaOrderResponse[]))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaAccountResponse))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaPositionResponse[]))]
[JsonSerializable(typeof(AlpacaBrokerageGateway.AlpacaAccountActivityResponse[]))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class AlpacaBrokerageSerializerContext : JsonSerializerContext;
