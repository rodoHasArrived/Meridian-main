using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Meridian.Application.Pipeline;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using OrderSide = Meridian.Execution.Sdk.OrderSide;
using OrderType = Meridian.Execution.Sdk.OrderType;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;
using TimeInForce = Meridian.Execution.Sdk.TimeInForce;

namespace Meridian.Infrastructure.Adapters.Templates;

/// <summary>
/// Template brokerage gateway scaffold. Copy this file and fill in the TODOs
/// to implement a new brokerage provider.
///
/// Steps to implement a new brokerage:
/// 1. Copy this file to a new folder under Adapters/{BrokerName}/
/// 2. Rename the class to {BrokerName}BrokerageGateway
/// 3. Add [DataSource] and [ImplementsAdr] attributes
/// 4. Implement all TODO methods with broker-specific API calls
/// 5. Add JSON DTOs and a JsonSerializerContext for ADR-014 compliance
/// 6. Register in DI: services.AddBrokerageGateway&lt;{BrokerName}BrokerageGateway&gt;()
/// 7. Add configuration options record if needed
/// 8. Add tests under tests/Meridian.Tests/Brokerage/
/// </summary>
// TODO: Add attributes:
// [DataSource("your-broker", "Your Broker Name", DataSourceType.Realtime, DataSourceCategory.Broker,
//     Priority = 15, Description = "Your broker order execution gateway")]
// [ImplementsAdr("ADR-001", "Your broker brokerage provider implementation")]
// [ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
// [ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
// [ImplementsAdr("ADR-010", "Uses IHttpClientFactory for HTTP connections")]
public sealed class TemplateBrokerageGateway : IBrokerageGateway
{
    // TODO: Add your broker's HTTP client factory, config options, etc.
    private readonly ILogger<TemplateBrokerageGateway> _logger;
    private readonly Channel<ExecutionReport> _reportChannel;
    private volatile bool _connected;
    private bool _disposed;

    public TemplateBrokerageGateway(
        // TODO: Add IHttpClientFactory, options, etc.
        ILogger<TemplateBrokerageGateway> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reportChannel = EventPipelinePolicy.Default.CreateChannel<ExecutionReport>(
            singleReader: false, singleWriter: false);
    }

    // TODO: Change to your broker's ID (lowercase, no spaces)
    public string GatewayId => "template";

    public bool IsConnected => _connected;

    // TODO: Change to your broker's display name
    public string BrokerDisplayName => "Template Broker";

    // TODO: Configure capabilities for your broker
    public BrokerageCapabilities BrokerageCapabilities { get; } =
        BrokerageCapabilities.UsEquity(
            modification: true,
            partialFills: true,
            shortSelling: false,
            fractional: false,
            extendedHours: false);

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connected) return;

        // TODO: Authenticate with your broker's API
        // - Validate credentials
        // - Establish WebSocket connection if needed
        // - Fetch initial account state
        await Task.CompletedTask.ConfigureAwait(false);

        _connected = true;
        _logger.LogInformation("Template broker connected");
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        // TODO: Close WebSocket/API connections
        _connected = false;
        return Task.CompletedTask;
    }

    public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        // TODO: Translate OrderRequest to your broker's order format and submit
        // Example:
        //   var brokerOrder = MapToBrokerOrder(request);
        //   var response = await _httpClient.PostAsJsonAsync("/orders", brokerOrder, ct);
        //   var brokerResponse = await response.Content.ReadFromJsonAsync<BrokerOrderResponse>(ct);

        var orderId = request.ClientOrderId ?? Guid.NewGuid().ToString("N");

        var report = new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.New,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = OrderStatus.Accepted,
            OrderQuantity = request.Quantity,
            GatewayOrderId = orderId,
            Timestamp = DateTimeOffset.UtcNow,
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    public async Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        EnsureConnected();

        // TODO: Call your broker's cancel API

        var report = new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Cancelled,
            Symbol = string.Empty,
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Cancelled,
            Timestamp = DateTimeOffset.UtcNow,
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    public async Task<ExecutionReport> ModifyOrderAsync(
        string orderId, OrderModification modification, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        EnsureConnected();

        // TODO: Call your broker's modify/amend API
        // Some brokers require cancel-replace instead of in-place modification

        var report = new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Modified,
            Symbol = string.Empty,
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Accepted,
            Timestamp = DateTimeOffset.UtcNow,
        };
        await _reportChannel.Writer.WriteAsync(report, ct).ConfigureAwait(false);
        return report;
    }

    public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var report in _reportChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return report;
        }
    }

    public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        // TODO: Call your broker's account endpoint
        return Task.FromResult(new AccountInfo
        {
            AccountId = "template-account",
            Equity = 0m,
            Cash = 0m,
            BuyingPower = 0m,
        });
    }

    public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        // TODO: Call your broker's positions endpoint
        return Task.FromResult<IReadOnlyList<BrokerPosition>>(Array.Empty<BrokerPosition>());
    }

    public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        // TODO: Call your broker's open orders endpoint
        return Task.FromResult<IReadOnlyList<BrokerOrder>>(Array.Empty<BrokerOrder>());
    }

    public async Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            if (!_connected) return BrokerHealthStatus.Unhealthy("Not connected");
            var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
            return BrokerHealthStatus.Healthy($"Account {account.AccountId}");
        }
        catch (Exception ex)
        {
            return BrokerHealthStatus.Unhealthy(ex.Message);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _connected = false;
        _reportChannel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private void EnsureConnected()
    {
        if (!_connected)
            throw new InvalidOperationException("Template broker is not connected. Call ConnectAsync first.");
    }
}
