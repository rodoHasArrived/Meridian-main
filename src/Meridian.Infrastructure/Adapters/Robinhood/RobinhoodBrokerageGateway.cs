// ✅ ADR-001: IBrokerageGateway contract for brokerage order execution
// ✅ ADR-004: CancellationToken on all async methods
// ✅ ADR-005: Attribute-based provider discovery via [DataSource]
// ✅ ADR-010: HTTP client via IHttpClientFactory
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using OrderSide = Meridian.Execution.Sdk.OrderSide;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;
using OrderType = Meridian.Execution.Sdk.OrderType;
using TimeInForce = Meridian.Execution.Sdk.TimeInForce;

namespace Meridian.Infrastructure.Adapters.Robinhood;

/// <summary>
/// Robinhood brokerage gateway for live order execution via the unofficial Robinhood API.
/// Uses REST for order management and a bounded channel for execution report streaming.
///
/// <para>
/// <b>Important:</b> Robinhood does not publish an official public API.
/// This gateway targets the same endpoints used by the Robinhood mobile application.
/// Set <c>ROBINHOOD_ACCESS_TOKEN</c> to your personal access token.
/// </para>
///
/// <para>
/// Order types supported: market, limit, stop, stop-limit.
/// Order modification is implemented as cancel + resubmit (Robinhood does not support
/// in-place order modification via the unofficial API).
/// </para>
/// </summary>
[DataSource("robinhood-brokerage", "Robinhood Brokerage", DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 35, Description = "Robinhood order execution via unofficial API (requires personal access token)")]
[ImplementsAdr("ADR-001", "Robinhood brokerage provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory for HTTP connections")]
public sealed class RobinhoodBrokerageGateway : IBrokerageGateway
{
    private const string BaseUrl = "https://api.robinhood.com";
    private const string OptionsPositionsUrl = BaseUrl + "/options/positions/?nonzero=true";
    private const string EnvAccessToken = "ROBINHOOD_ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RobinhoodBrokerageGateway> _logger;
    private readonly string? _accessToken;
    private readonly Channel<ExecutionReport> _reportChannel;
    private volatile bool _connected;
    private bool _disposed;

    public RobinhoodBrokerageGateway(
        IHttpClientFactory httpClientFactory,
        ILogger<RobinhoodBrokerageGateway> logger,
        string? accessToken = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _accessToken = accessToken ?? Environment.GetEnvironmentVariable(EnvAccessToken);

        if (string.IsNullOrWhiteSpace(_accessToken))
            _logger.LogWarning(
                "ROBINHOOD_ACCESS_TOKEN is not set; Robinhood brokerage gateway will be unavailable.");

        _reportChannel = EventPipelinePolicy.Default.CreateChannel<ExecutionReport>(
            singleReader: false, singleWriter: false);
    }

    // ── IBrokerageGateway / IExecutionGateway ─────────────────────────────

    /// <inheritdoc />
    public string GatewayId => "robinhood";

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public string BrokerDisplayName => "Robinhood (unofficial)";

    /// <inheritdoc />
    public BrokerageCapabilities BrokerageCapabilities { get; } =
        BrokerageCapabilities.UsEquity(
            modification: false,  // Cancel + resubmit only
            partialFills: true,
            shortSelling: false,
            fractional: true,
            extendedHours: false);

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connected)
            return;

        if (string.IsNullOrWhiteSpace(_accessToken))
            throw new InvalidOperationException(
                "ROBINHOOD_ACCESS_TOKEN is required. Set the environment variable before calling ConnectAsync.");

        // Validate token by fetching accounts.
        var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
        _connected = true;
        _logger.LogInformation(
            "Robinhood brokerage connected: account {AccountId}, equity {Equity:C}",
            account.AccountId, account.Equity);
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        _logger.LogInformation("Robinhood brokerage disconnected");
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
            "Robinhood submitting order: {Side} {Quantity} {Symbol} @ {Type}",
            request.Side, request.Quantity, request.Symbol, request.Type);

        var accountUrl = await GetAccountUrlAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accountUrl))
        {
            var rejectReport = BuildRejectedReport(request, "Could not resolve Robinhood account URL.");
            await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
            return rejectReport;
        }

        if (IsOptionOrderRequest(request))
        {
            if (!TryGetOptionOrderMetadata(request, out var optionInstrumentUrl, out var positionEffect))
            {
                var rejectReport = BuildRejectedReport(
                    request,
                    "Robinhood option orders require option_instrument_url and position_effect metadata.");
                await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
                return rejectReport;
            }

            return await SubmitOptionOrderAsync(
                request,
                accountUrl,
                optionInstrumentUrl,
                positionEffect,
                ct).ConfigureAwait(false);
        }

        // Look up the instrument URL required by Robinhood's order API.
        var instrumentUrl = await GetInstrumentUrlAsync(request.Symbol, ct).ConfigureAwait(false);
        if (instrumentUrl is null)
        {
            var rejectReport = BuildRejectedReport(request, $"Could not resolve instrument URL for {request.Symbol}");
            await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
            return rejectReport;
        }

        var payload = new RobinhoodOrderPayload
        {
            Account = accountUrl,
            Instrument = instrumentUrl,
            Symbol = request.Symbol.ToUpperInvariant(),
            Side = request.Side == OrderSide.Buy ? "buy" : "sell",
            Type = MapOrderType(request.Type),
            TimeInForce = MapTimeInForce(request.TimeInForce),
            Trigger = request.Type is OrderType.StopMarket or OrderType.StopLimit ? "stop" : "immediate",
            Quantity = request.Quantity.ToString("G", CultureInfo.InvariantCulture),
            Price = request.LimitPrice?.ToString("G", CultureInfo.InvariantCulture),
            StopPrice = request.StopPrice?.ToString("G", CultureInfo.InvariantCulture),
            RefId = request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
        };

        using var client = CreateHttpClient();
        var response = await client.PostAsJsonAsync(
            $"{BaseUrl}/orders/", payload,
            RobinhoodBrokerageSerializerContext.Default.RobinhoodOrderPayload, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogError("Robinhood order rejected: {StatusCode} {Body}", response.StatusCode, errorBody);
            var rejectReport = BuildRejectedReport(request, $"API error {response.StatusCode}: {errorBody}");
            await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
            return rejectReport;
        }

        var order = await response.Content.ReadFromJsonAsync(
            RobinhoodBrokerageSerializerContext.Default.RobinhoodOrderResponse, ct).ConfigureAwait(false);

        var report = new ExecutionReport
        {
            OrderId = order?.Id ?? request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
            ClientOrderId = request.ClientOrderId,
            ReportType = ExecutionReportType.New,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = MapRobinhoodStatus(order?.State),
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

        // Fetch current order so the report carries symbol + side.
        var existing = await TryGetOrderAsync(client, orderId, ct).ConfigureAwait(false);
        var existingOption = existing is null
            ? await TryGetOptionOrderAsync(client, orderId, ct).ConfigureAwait(false)
            : null;
        var cancelUrl = existingOption is null
            ? $"{BaseUrl}/orders/{orderId}/cancel/"
            : $"{BaseUrl}/options/orders/{orderId}/cancel/";

        var cancelResp = await client.PostAsync(
            cancelUrl, content: null, ct).ConfigureAwait(false);

        string? body = null;

        if (!cancelResp.IsSuccessStatusCode)
        {
            body = await cancelResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("Robinhood cancel failed for {OrderId}: {Body}", orderId, body);
        }

        var report = new ExecutionReport
        {
            OrderId = orderId,
            ClientOrderId = existing?.RefId ?? existingOption?.RefId,
            ReportType = cancelResp.IsSuccessStatusCode ? ExecutionReportType.Cancelled : ExecutionReportType.Rejected,
            Symbol = existing?.Symbol ?? existingOption?.ChainSymbol ?? string.Empty,
            Side = existing is not null ? ParseOrderSide(existing.Side) : ParseOptionOrderSide(existingOption),
            OrderStatus = cancelResp.IsSuccessStatusCode ? OrderStatus.Cancelled : OrderStatus.Rejected,
            RejectReason = cancelResp.IsSuccessStatusCode
                ? null
                : string.IsNullOrWhiteSpace(body)
                    ? "Cancel request failed"
                    : $"Cancel request failed: {body}",
            GatewayOrderId = orderId,
            Timestamp = DateTimeOffset.UtcNow,
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Robinhood does not support in-place order modification via the unofficial API.
    /// This method cancels the existing order and submits a new one with the updated parameters.
    /// </remarks>
    public async Task<ExecutionReport> ModifyOrderAsync(
        string orderId, OrderModification modification, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(modification);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        _logger.LogInformation(
            "Robinhood does not support in-place modification; cancelling {OrderId} and resubmitting", orderId);

        // Fetch original order details.
        using var client = CreateHttpClient();
        var original = await TryGetOrderAsync(client, orderId, ct).ConfigureAwait(false);
        var originalOption = original is null
            ? await TryGetOptionOrderAsync(client, orderId, ct).ConfigureAwait(false)
            : null;

        if (original is null && originalOption is null)
        {
            var report = new ExecutionReport
            {
                OrderId = orderId,
                ReportType = ExecutionReportType.Rejected,
                Symbol = string.Empty,
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Rejected,
                RejectReason = $"Cannot modify order {orderId}: original order details not found",
                Timestamp = DateTimeOffset.UtcNow,
            };
            await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
            return report;
        }

        // Cancel the original order.
        var cancelReport = await CancelOrderAsync(orderId, ct).ConfigureAwait(false);
        if (cancelReport.OrderStatus is not OrderStatus.Cancelled)
        {
            _logger.LogWarning(
                "Robinhood modify aborted for {OrderId} because cancel returned {Status}",
                orderId,
                cancelReport.OrderStatus);
            return cancelReport;
        }

        // Resubmit with updated fields.
        var newRequest = original is not null
            ? new OrderRequest
            {
                Symbol = original.Symbol ?? string.Empty,
                Side = ParseOrderSide(original.Side),
                Type = ParseOrderType(original.Type),
                TimeInForce = ParseTimeInForce(original.TimeInForce),
                Quantity = modification.NewQuantity ?? ParseDecimal(original.Quantity),
                LimitPrice = modification.NewLimitPrice
                    ?? (string.IsNullOrEmpty(original.Price) ? null : ParseDecimal(original.Price)),
                StopPrice = modification.NewStopPrice
                    ?? (string.IsNullOrEmpty(original.StopPrice) ? null : ParseDecimal(original.StopPrice)),
            }
            : BuildOptionModifyRequest(orderId, originalOption!, modification);

        return await SubmitOrderAsync(newRequest, ct).ConfigureAwait(false);
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
        var response = await client.GetAsync($"{BaseUrl}/accounts/", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(
            RobinhoodBrokerageSerializerContext.Default.RobinhoodAccountListResponse, ct).ConfigureAwait(false);

        var acct = result?.Results?.FirstOrDefault();
        return new AccountInfo
        {
            AccountId = acct?.AccountNumber ?? "unknown",
            Equity = ParseDecimal(acct?.Equity),
            Cash = ParseDecimal(acct?.Cash),
            BuyingPower = ParseDecimal(acct?.BuyingPower),
            Currency = "USD",
            Status = acct?.Deactivated == true ? "deactivated" : "active",
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default)
    {
        using var client = CreateHttpClient();
        var optionsFetch = client.GetAsync(OptionsPositionsUrl, ct);
        var response = await client.GetAsync($"{BaseUrl}/positions/?nonzero=true", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(
            RobinhoodBrokerageSerializerContext.Default.RobinhoodPositionListResponse, ct).ConfigureAwait(false);

        if (result?.Results is null)
            return Array.Empty<BrokerPosition>();

        var positions = new List<BrokerPosition>();
        foreach (var p in result.Results)
        {
            // Resolve the instrument URL to get the symbol.
            var symbol = await ResolveSymbolFromInstrumentAsync(p.Instrument, client, ct).ConfigureAwait(false);
            positions.Add(new BrokerPosition
            {
                PositionId = symbol ?? p.Instrument,
                Symbol = symbol ?? p.Instrument ?? string.Empty,
                UnderlyingSymbol = symbol ?? p.Instrument ?? string.Empty,
                Description = symbol ?? p.Instrument ?? string.Empty,
                Quantity = ParseDecimal(p.Quantity),
                AverageEntryPrice = ParseDecimal(p.AveragePrice),
                MarketPrice = 0m, // Robinhood positions endpoint does not include current price
                MarketValue = 0m,
                UnrealizedPnl = 0m,
                AssetClass = "equity",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["asset_class"] = "equity"
                }
            });
        }

        // Harvest the options response — already in-flight from the concurrent fetch above.
        try
        {
            var optResponse = await optionsFetch.ConfigureAwait(false);
            if (optResponse.IsSuccessStatusCode)
            {
                var optResult = await optResponse.Content.ReadFromJsonAsync(
                    RobinhoodBrokerageSerializerContext.Default.RobinhoodOptionPositionListResponse, ct)
                    .ConfigureAwait(false);

                if (optResult?.Results is not null)
                {
                    var optionDetails = await Task.WhenAll(optResult.Results.Select(async p => new
                    {
                        Position = p,
                        Detail = await ResolveOptionInstrumentAsync(p.Option, client, ct).ConfigureAwait(false)
                    })).ConfigureAwait(false);

                    foreach (var item in optionDetails)
                    {
                        var p = item.Position;
                        var detail = item.Detail;
                        var qty = ParseDecimal(p.Quantity);
                        if (qty == 0m)
                            continue;

                        var underlyingSymbol = detail?.ChainSymbol ?? p.ChainSymbol ?? string.Empty;
                        var expiration = TryParseDateOnly(detail?.ExpirationDate);
                        var strike = TryParseDecimal(detail?.StrikePrice);
                        var right = NormalizeOptionRight(detail?.Type ?? p.Type);
                        positions.Add(new BrokerPosition
                        {
                            PositionId = p.Option ?? BuildFallbackOptionPositionId(underlyingSymbol, right, expiration, strike),
                            Symbol = underlyingSymbol,
                            UnderlyingSymbol = underlyingSymbol,
                            Description = BuildOptionDescription(underlyingSymbol, strike, right, expiration),
                            Quantity = qty,
                            AverageEntryPrice = ParseDecimal(p.AveragePrice),
                            MarketPrice = 0m,
                            MarketValue = 0m,
                            UnrealizedPnl = 0m,
                            AssetClass = "option",
                            Expiration = expiration,
                            Strike = strike,
                            Right = right,
                            Metadata = BuildOptionPositionMetadata(p.Option, underlyingSymbol, right, expiration, strike)
                        });
                    }
                }
            }
            else
            {
                _logger.LogWarning(
                    "Robinhood options positions request returned {StatusCode}", optResponse.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // Use the URL constant to keep exception-path logging allocation-free.
            _logger.LogWarning(ex, "Failed to fetch Robinhood options positions from {Url}", OptionsPositionsUrl);
        }

        return positions.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        using var client = CreateHttpClient();
        var response = await client.GetAsync(
            $"{BaseUrl}/orders/?state=queued,unconfirmed,confirmed,partially_filled", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(
            RobinhoodBrokerageSerializerContext.Default.RobinhoodOrderListResponse, ct).ConfigureAwait(false);

        if (result?.Results is null)
            return Array.Empty<BrokerOrder>();

        return result.Results.Select(o => new BrokerOrder
        {
            OrderId = o.Id ?? string.Empty,
            ClientOrderId = o.RefId,
            Symbol = o.Symbol ?? string.Empty,
            Side = o.Side == "sell" ? OrderSide.Sell : OrderSide.Buy,
            Type = ParseOrderType(o.Type),
            Quantity = ParseDecimal(o.Quantity),
            FilledQuantity = 0m, // Robinhood executed_notional is a currency value, not filled shares/contracts.
            LimitPrice = string.IsNullOrEmpty(o.Price) ? null : ParseDecimal(o.Price),
            StopPrice = string.IsNullOrEmpty(o.StopPrice) ? null : ParseDecimal(o.StopPrice),
            Status = MapRobinhoodStatus(o.State),
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
            _logger.LogError(ex, "Robinhood health check failed");
            return BrokerHealthStatus.Unhealthy(ex.Message);
        }
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

    // ── Private helpers ───────────────────────────────────────────────────

    private void EnsureConnected()
    {
        if (!_connected)
            throw new InvalidOperationException(
                "Robinhood brokerage gateway is not connected. Call ConnectAsync first.");
    }

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientNames.RobinhoodBrokerage);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }

    private async Task<string?> GetInstrumentUrlAsync(string symbol, CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient();
            var response = await client.GetAsync(
                $"{BaseUrl}/instruments/?symbol={Uri.EscapeDataString(symbol)}", ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadFromJsonAsync(
                RobinhoodBrokerageSerializerContext.Default.RobinhoodInstrumentListResponse, ct).ConfigureAwait(false);

            return result?.Results?.FirstOrDefault()?.Url;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve instrument URL for {Symbol}", symbol);
            return null;
        }
    }

    private async Task<string?> GetAccountUrlAsync(CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient();
            var response = await client.GetAsync($"{BaseUrl}/accounts/", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadFromJsonAsync(
                RobinhoodBrokerageSerializerContext.Default.RobinhoodAccountListResponse, ct).ConfigureAwait(false);

            return result?.Results?.FirstOrDefault()?.Url;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve Robinhood account URL");
            return null;
        }
    }

    private async Task<RobinhoodOrderResponse?> TryGetOrderAsync(
        HttpClient client,
        string orderId,
        CancellationToken ct)
    {
        try
        {
            var response = await client.GetAsync($"{BaseUrl}/orders/{orderId}/", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync(
                RobinhoodBrokerageSerializerContext.Default.RobinhoodOrderResponse,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Robinhood failed to fetch equity order {OrderId}", orderId);
            return null;
        }
    }

    private async Task<RobinhoodOptionOrderResponse?> TryGetOptionOrderAsync(
        HttpClient client,
        string orderId,
        CancellationToken ct)
    {
        try
        {
            var response = await client.GetAsync($"{BaseUrl}/options/orders/{orderId}/", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync(
                RobinhoodBrokerageSerializerContext.Default.RobinhoodOptionOrderResponse,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Robinhood failed to fetch option order {OrderId}", orderId);
            return null;
        }
    }

    private async Task<ExecutionReport> SubmitOptionOrderAsync(
        OrderRequest request,
        string accountUrl,
        string optionInstrumentUrl,
        string positionEffect,
        CancellationToken ct)
    {
        var payload = new RobinhoodOptionOrderPayload
        {
            Account = accountUrl,
            Direction = request.Side == OrderSide.Buy ? "debit" : "credit",
            Quantity = request.Quantity.ToString("G", CultureInfo.InvariantCulture),
            Type = MapOrderType(request.Type),
            TimeInForce = MapTimeInForce(request.TimeInForce),
            Price = request.LimitPrice?.ToString("G", CultureInfo.InvariantCulture),
            RefId = request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
            Legs =
            [
                new RobinhoodOptionLeg
                {
                    Option = optionInstrumentUrl,
                    Side = request.Side == OrderSide.Buy ? "buy" : "sell",
                    PositionEffect = positionEffect,
                    RatioQuantity = 1,
                }
            ]
        };

        using var client = CreateHttpClient();
        var response = await client.PostAsJsonAsync(
            $"{BaseUrl}/options/orders/",
            payload,
            RobinhoodBrokerageSerializerContext.Default.RobinhoodOptionOrderPayload,
            ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogError("Robinhood option order rejected: {StatusCode} {Body}", response.StatusCode, errorBody);
            var rejectReport = BuildRejectedReport(request, $"API error {response.StatusCode}: {errorBody}");
            await _reportChannel.Writer.WriteAsync(rejectReport, ct).ConfigureAwait(false);
            return rejectReport;
        }

        var order = await response.Content.ReadFromJsonAsync(
            RobinhoodBrokerageSerializerContext.Default.RobinhoodOptionOrderResponse,
            ct).ConfigureAwait(false);

        var report = new ExecutionReport
        {
            OrderId = order?.Id ?? request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
            ClientOrderId = request.ClientOrderId,
            ReportType = ExecutionReportType.New,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = MapRobinhoodStatus(order?.State),
            OrderQuantity = request.Quantity,
            GatewayOrderId = order?.Id,
            Timestamp = order?.CreatedAt ?? DateTimeOffset.UtcNow,
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    private static async Task<string?> ResolveSymbolFromInstrumentAsync(
        string? instrumentUrl, HttpClient client, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrumentUrl))
            return null;
        try
        {
            var resp = await client.GetAsync(instrumentUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            var instrument = await resp.Content.ReadFromJsonAsync(
                RobinhoodBrokerageSerializerContext.Default.RobinhoodInstrumentResponse, ct).ConfigureAwait(false);
            return instrument?.Symbol;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<RobinhoodOptionInstrumentResponse?> ResolveOptionInstrumentAsync(
        string? optionInstrumentUrl,
        HttpClient client,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(optionInstrumentUrl))
            return null;

        try
        {
            var response = await client.GetAsync(optionInstrumentUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync(
                RobinhoodBrokerageSerializerContext.Default.RobinhoodOptionInstrumentResponse,
                ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> BuildOptionPositionMetadata(
        string? optionInstrumentUrl,
        string underlyingSymbol,
        string? right,
        DateOnly? expiration,
        decimal? strike)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["asset_class"] = "option"
        };

        if (!string.IsNullOrWhiteSpace(optionInstrumentUrl))
            metadata["option_instrument_url"] = optionInstrumentUrl;
        if (!string.IsNullOrWhiteSpace(underlyingSymbol))
            metadata["underlying_symbol"] = underlyingSymbol;
        if (!string.IsNullOrWhiteSpace(right))
            metadata["right"] = right;
        if (expiration.HasValue)
            metadata["expiration"] = expiration.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (strike.HasValue)
            metadata["strike"] = strike.Value.ToString("G", CultureInfo.InvariantCulture);

        return metadata;
    }

    private static string BuildOptionDescription(
        string underlyingSymbol,
        decimal? strike,
        string? right,
        DateOnly? expiration)
    {
        var parts = new List<string>(4);

        if (!string.IsNullOrWhiteSpace(underlyingSymbol))
            parts.Add(underlyingSymbol);
        if (strike.HasValue)
            parts.Add(strike.Value.ToString("G", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(right))
            parts.Add(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(right.ToLowerInvariant()));
        if (expiration.HasValue)
            parts.Add(expiration.Value.ToString("ddMMMyy", CultureInfo.InvariantCulture));

        return parts.Count == 0 ? "Option Position" : string.Join(" ", parts);
    }

    private static string BuildFallbackOptionPositionId(
        string underlyingSymbol,
        string? right,
        DateOnly? expiration,
        decimal? strike)
    {
        var expirationText = expiration?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown-expiry";
        var strikeText = strike?.ToString("G", CultureInfo.InvariantCulture) ?? "unknown-strike";
        var rightText = string.IsNullOrWhiteSpace(right) ? "unknown-right" : right;
        return $"{underlyingSymbol}:{rightText}:{expirationText}:{strikeText}";
    }

    private static string? NormalizeOptionRight(string? value) => value?.ToLowerInvariant() switch
    {
        "call" or "c" => "call",
        "put" or "p" => "put",
        _ => value
    };

    private static DateOnly? TryParseDateOnly(string? value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static decimal? TryParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool IsOptionOrderRequest(OrderRequest request)
    {
        if (request.Metadata is null)
            return false;

        return request.Metadata.TryGetValue("option_instrument_url", out var optionInstrumentUrl)
                   && !string.IsNullOrWhiteSpace(optionInstrumentUrl)
               || request.Metadata.TryGetValue("asset_class", out var assetClass)
                   && string.Equals(assetClass, "option", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetOptionOrderMetadata(
        OrderRequest request,
        out string optionInstrumentUrl,
        out string positionEffect)
    {
        optionInstrumentUrl = string.Empty;
        positionEffect = string.Empty;

        if (request.Metadata is null)
            return false;

        request.Metadata.TryGetValue("option_instrument_url", out var optionInstrumentUrlValue);
        request.Metadata.TryGetValue("position_effect", out var positionEffectValue);
        optionInstrumentUrl = optionInstrumentUrlValue ?? string.Empty;
        positionEffect = positionEffectValue ?? string.Empty;

        return !string.IsNullOrWhiteSpace(optionInstrumentUrl)
               && !string.IsNullOrWhiteSpace(positionEffect);
    }

    private static OrderRequest BuildOptionModifyRequest(
        string orderId,
        RobinhoodOptionOrderResponse originalOption,
        OrderModification modification)
    {
        var leg = originalOption.Legs?.FirstOrDefault();
        if (leg is null || string.IsNullOrWhiteSpace(leg.Option) || string.IsNullOrWhiteSpace(leg.PositionEffect))
        {
            throw new InvalidOperationException(
                $"Cannot modify Robinhood option order {orderId}: option leg metadata is missing.");
        }

        return new OrderRequest
        {
            Symbol = originalOption.ChainSymbol ?? string.Empty,
            Side = ParseOptionOrderSide(originalOption),
            Type = ParseOrderType(originalOption.Type),
            TimeInForce = ParseTimeInForce(originalOption.TimeInForce),
            Quantity = modification.NewQuantity ?? ParseDecimal(originalOption.Quantity),
            LimitPrice = modification.NewLimitPrice
                ?? (string.IsNullOrEmpty(originalOption.Price) ? null : ParseDecimal(originalOption.Price)),
            StopPrice = modification.NewStopPrice,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["asset_class"] = "option",
                ["option_instrument_url"] = leg.Option,
                ["position_effect"] = leg.PositionEffect
            }
        };
    }

    private static ExecutionReport BuildRejectedReport(OrderRequest request, string reason) =>
        new()
        {
            OrderId = request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
            ClientOrderId = request.ClientOrderId,
            ReportType = ExecutionReportType.Rejected,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = OrderStatus.Rejected,
            RejectReason = reason,
            Timestamp = DateTimeOffset.UtcNow,
        };

    private static OrderSide ParseOrderSide(string? side) =>
        string.Equals(side, "sell", StringComparison.OrdinalIgnoreCase)
            ? OrderSide.Sell
            : OrderSide.Buy;

    private static OrderSide ParseOptionOrderSide(RobinhoodOptionOrderResponse? optionOrder)
    {
        var legSide = optionOrder?.Legs?.FirstOrDefault()?.Side;
        return !string.IsNullOrWhiteSpace(legSide)
            ? ParseOrderSide(legSide)
            : string.Equals(optionOrder?.Direction, "credit", StringComparison.OrdinalIgnoreCase)
                ? OrderSide.Sell
                : OrderSide.Buy;
    }

    private static string MapOrderType(OrderType type) => type switch
    {
        OrderType.Market => "market",
        OrderType.Limit => "limit",
        OrderType.StopMarket => "stop",
        OrderType.StopLimit => "stop_limit",
        OrderType.MarketOnOpen or OrderType.MarketOnClose or OrderType.LimitOnOpen or OrderType.LimitOnClose
            => throw new NotSupportedException(
                $"Robinhood order mapping does not preserve the {type} session timing qualifier."),
        _ => throw new NotSupportedException($"Robinhood order mapping does not support {type}.")
    };

    private static void RejectSessionScopedOrderType(OrderType type)
    {
        if (type is OrderType.MarketOnOpen or OrderType.MarketOnClose or OrderType.LimitOnOpen or OrderType.LimitOnClose)
        {
            throw new NotSupportedException(
                $"Robinhood gateway does not currently preserve the {type} session timing qualifier.");
        }
    }

    private static string MapTimeInForce(TimeInForce tif) => tif switch
    {
        TimeInForce.Day => "gfd",         // Good-for-day
        TimeInForce.GoodTilCancelled => "gtc",
        TimeInForce.ImmediateOrCancel => "ioc",
        TimeInForce.FillOrKill => "fok",
        _ => "gfd"
    };

    private static OrderType ParseOrderType(string? type) => type switch
    {
        "market" => OrderType.Market,
        "limit" => OrderType.Limit,
        "stop" => OrderType.StopMarket,
        "stop_limit" => OrderType.StopLimit,
        _ => OrderType.Market
    };

    private static TimeInForce ParseTimeInForce(string? tif) => tif switch
    {
        "gfd" or "day" => TimeInForce.Day,
        "gtc" => TimeInForce.GoodTilCancelled,
        "ioc" => TimeInForce.ImmediateOrCancel,
        "fok" => TimeInForce.FillOrKill,
        _ => TimeInForce.Day
    };

    private static OrderStatus MapRobinhoodStatus(string? state) => state switch
    {
        "queued" => OrderStatus.PendingNew,
        "unconfirmed" => OrderStatus.PendingNew,
        "confirmed" => OrderStatus.Accepted,
        "partially_filled" => OrderStatus.PartiallyFilled,
        "filled" => OrderStatus.Filled,
        "cancelled" => OrderStatus.Cancelled,
        "failed" => OrderStatus.Rejected,
        "rejected" => OrderStatus.Rejected,
        _ => OrderStatus.PendingNew
    };

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var result) ? result : 0m;

    // ── JSON DTOs (ADR-014: source generators) ────────────────────────────

    internal sealed class RobinhoodOrderPayload
    {
        [JsonPropertyName("account")] public string? Account { get; set; }
        [JsonPropertyName("instrument")] public string? Instrument { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("time_in_force")] public string? TimeInForce { get; set; }
        [JsonPropertyName("trigger")] public string? Trigger { get; set; }
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
        [JsonPropertyName("stop_price")] public string? StopPrice { get; set; }
        [JsonPropertyName("ref_id")] public string? RefId { get; set; }
    }

    internal sealed class RobinhoodOrderResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("ref_id")] public string? RefId { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("time_in_force")] public string? TimeInForce { get; set; }
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
        [JsonPropertyName("stop_price")] public string? StopPrice { get; set; }
        [JsonPropertyName("executed_notional")] public string? ExecutedNotional { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    }

    internal sealed class RobinhoodOrderListResponse
    {
        [JsonPropertyName("results")] public RobinhoodOrderResponse[]? Results { get; set; }
    }

    internal sealed class RobinhoodAccountResponse
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("account_number")] public string? AccountNumber { get; set; }
        [JsonPropertyName("equity")] public string? Equity { get; set; }
        [JsonPropertyName("cash")] public string? Cash { get; set; }
        [JsonPropertyName("buying_power")] public string? BuyingPower { get; set; }
        [JsonPropertyName("deactivated")] public bool? Deactivated { get; set; }
    }

    internal sealed class RobinhoodAccountListResponse
    {
        [JsonPropertyName("results")] public RobinhoodAccountResponse[]? Results { get; set; }
    }

    internal sealed class RobinhoodPositionResponse
    {
        [JsonPropertyName("instrument")] public string? Instrument { get; set; }
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("average_buy_price")] public string? AveragePrice { get; set; }
    }

    internal sealed class RobinhoodPositionListResponse
    {
        [JsonPropertyName("results")] public RobinhoodPositionResponse[]? Results { get; set; }
    }

    internal sealed class RobinhoodInstrumentResponse
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
    }

    internal sealed class RobinhoodInstrumentListResponse
    {
        [JsonPropertyName("results")] public RobinhoodInstrumentResponse[]? Results { get; set; }
    }
    // ── Options DTOs ──────────────────────────────────────────────────────

    /// <summary>A single leg within a Robinhood options order.</summary>
    internal sealed class RobinhoodOptionLeg
    {
        [JsonPropertyName("option")] public string? Option { get; set; }
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("ratio_quantity")] public int RatioQuantity { get; set; } = 1;
        [JsonPropertyName("position_effect")] public string? PositionEffect { get; set; }
    }

    /// <summary>Payload for <c>POST /options/orders/</c>.</summary>
    internal sealed class RobinhoodOptionOrderPayload
    {
        [JsonPropertyName("account")] public string? Account { get; set; }
        [JsonPropertyName("direction")] public string? Direction { get; set; }
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("time_in_force")] public string? TimeInForce { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
        [JsonPropertyName("ref_id")] public string? RefId { get; set; }
        [JsonPropertyName("legs")] public RobinhoodOptionLeg[]? Legs { get; set; }
    }

    /// <summary>Response from <c>POST /options/orders/</c> or a single entry in <c>GET /options/orders/</c>.</summary>
    internal sealed class RobinhoodOptionOrderResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("ref_id")] public string? RefId { get; set; }
        [JsonPropertyName("chain_symbol")] public string? ChainSymbol { get; set; }
        [JsonPropertyName("direction")] public string? Direction { get; set; }
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("time_in_force")] public string? TimeInForce { get; set; }
        [JsonPropertyName("price")] public string? Price { get; set; }
        [JsonPropertyName("legs")] public RobinhoodOptionLeg[]? Legs { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    }

    /// <summary>Paginated list response from <c>GET /options/orders/</c>.</summary>
    internal sealed class RobinhoodOptionOrderListResponse
    {
        [JsonPropertyName("results")] public RobinhoodOptionOrderResponse[]? Results { get; set; }
    }

    /// <summary>A single entry from <c>GET /options/positions/</c>.</summary>
    internal sealed class RobinhoodOptionPositionResponse
    {
        /// <summary>URL of the options instrument.</summary>
        [JsonPropertyName("option")] public string? Option { get; set; }
        /// <summary>Underlying ticker symbol (e.g. "AAPL").</summary>
        [JsonPropertyName("chain_symbol")] public string? ChainSymbol { get; set; }
        /// <summary>Number of contracts held (string decimal).</summary>
        [JsonPropertyName("quantity")] public string? Quantity { get; set; }
        /// <summary>Average cost per contract (string decimal).</summary>
        [JsonPropertyName("average_price")] public string? AveragePrice { get; set; }
        /// <summary>"call" or "put".</summary>
        [JsonPropertyName("type")] public string? Type { get; set; }
    }

    /// <summary>Paginated list response from <c>GET /options/positions/</c>.</summary>
    internal sealed class RobinhoodOptionPositionListResponse
    {
        [JsonPropertyName("results")] public RobinhoodOptionPositionResponse[]? Results { get; set; }
    }

    internal sealed class RobinhoodOptionInstrumentResponse
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("chain_symbol")] public string? ChainSymbol { get; set; }
        [JsonPropertyName("expiration_date")] public string? ExpirationDate { get; set; }
        [JsonPropertyName("strike_price")] public string? StrikePrice { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
    }
}

/// <summary>
/// Source-generated JSON serializer context for Robinhood brokerage DTOs (ADR-014).
/// </summary>
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOrderPayload))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOrderResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOrderListResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodAccountResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodAccountListResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodPositionResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodPositionListResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodInstrumentResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodInstrumentListResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionLeg))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionOrderPayload))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionOrderResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionOrderListResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionPositionResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionPositionListResponse))]
[JsonSerializable(typeof(RobinhoodBrokerageGateway.RobinhoodOptionInstrumentResponse))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
internal sealed partial class RobinhoodBrokerageSerializerContext : JsonSerializerContext;
