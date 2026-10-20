using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Meridian.Core.Pipeline;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.Logging;
using IBOptions = Meridian.Core.Config.IBOptions;
using OrderSide = Meridian.Execution.Sdk.OrderSide;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;
using OrderType = Meridian.Execution.Sdk.OrderType;
using TimeInForce = Meridian.Execution.Sdk.TimeInForce;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Interactive Brokers brokerage gateway for live or paper order execution via the TWS/Gateway API.
/// Uses the native IB socket path when the official vendor SDK is available.
/// </summary>
[DataSource("ibkr", "Interactive Brokers Brokerage", DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 5, Description = "Interactive Brokers TWS/Gateway order execution")]
[ImplementsAdr("ADR-001", "IB brokerage provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed partial class IBBrokerageGateway :
    IBrokerageGateway,
    IExecutionGatewayModeProvider,
    IBrokerageOrderRecoveryGateway,
    IBrokerageOrderAmendmentRecoveryGateway,
    IBrokerageConnectionState,
    IBrokerageAccountCatalog,
    IBrokeragePortfolioSync,
    IBrokerageActivitySync
{
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromSeconds(10);

    private readonly IBOptions _options;
    private readonly ILogger<IBBrokerageGateway> _logger;
    private readonly IIBBrokerageClient _client;
    private readonly Channel<ExecutionReport> _reportChannel;
    private readonly ConcurrentDictionary<int, SubmittedOrderContext> _submittedOrders = new();
    private readonly ConcurrentDictionary<string, int> _gatewayOrderIdsByExternalId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, BrokerOrder> _openOrders = new();
    private readonly ConcurrentDictionary<int, ExecutionReport> _latestOrderReports = new();
    private readonly ConcurrentDictionary<int, string> _openOrderAccounts = new();
    private readonly ConcurrentDictionary<int, string> _openOrderSecurityTypes = new();
    private readonly ConcurrentDictionary<string, IBExecutionUpdate> _executions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, PendingOrderOperation> _pendingOrderOperations = new();
    private readonly ConcurrentDictionary<int, PendingOrderOperation> _unconfirmedAmendments = new();
    private readonly ConcurrentDictionary<int, AccountSummaryCollector> _accountSummaryRequests = new();
    private readonly SemaphoreSlim _openOrdersGate = new(1, 1);
    private readonly SemaphoreSlim _positionsGate = new(1, 1);

    private volatile bool _connected;
    private bool _disposed;
    private int _nextOrderId;
    private TaskCompletionSource<int>? _nextValidIdWaiter;
    private TaskCompletionSource<IReadOnlyList<BrokerOrder>>? _openOrdersSnapshotWaiter;
    private ConcurrentDictionary<int, BrokerOrder>? _openOrdersSnapshotBuffer;
    private TaskCompletionSource<IReadOnlyList<AccountScopedBrokerPosition>>? _positionsSnapshotWaiter;
    private ConcurrentDictionary<AccountPositionKey, AccountScopedBrokerPosition>? _positionsSnapshotBuffer;

    public IBBrokerageGateway(
        IBOptions options,
        ILogger<IBBrokerageGateway> logger,
        IIBBrokerageClient? client = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = client ?? CreateBrokerageClient(options);
        _reportChannel = EventPipelinePolicy.CompletionQueue.CreateChannel<ExecutionReport>();

        _client.NextValidIdReceived += OnNextValidIdReceived;
        _client.OrderStatusReceived += OnOrderStatusReceived;
        _client.OpenOrderReceived += OnOpenOrderReceived;
        _client.OpenOrdersCompleted += OnOpenOrdersCompleted;
        _client.ExecutionDetailsReceived += OnExecutionDetailsReceived;
        _client.PositionReceived += OnPositionReceived;
        _client.PositionsCompleted += OnPositionsCompleted;
        _client.AccountSummaryReceived += OnAccountSummaryReceived;
        _client.AccountSummaryCompleted += OnAccountSummaryCompleted;
        _client.ErrorOccurred += OnErrorOccurred;
        _client.ManagedAccountsReceived += OnManagedAccountsReceived;
        _client.ConnectionLost += OnConnectionLost;
        _client.ConnectionRestored += OnConnectionRestored;
    }

    /// <inheritdoc />
    public string GatewayId => "ibkr";

    /// <inheritdoc />
    public bool IsConnected => _connected && _client.IsConnected;

    /// <inheritdoc />
    public string BrokerDisplayName => "Interactive Brokers";

    /// <summary>
    /// Exposes the actual runtime posture to the OMS. A guidance build is explicitly simulation,
    /// which prevents a configuration-only promotion from treating it as a live broker.
    /// </summary>
    public Meridian.Execution.Sdk.ExecutionMode ExecutionMode
    {
        get
        {
#if IBAPI_VENDOR
            return _options.UsePaperTrading
                ? Meridian.Execution.Sdk.ExecutionMode.Paper
                : Meridian.Execution.Sdk.ExecutionMode.Live;
#else
            return Meridian.Execution.Sdk.ExecutionMode.Simulation;
#endif
        }
    }

    /// <inheritdoc />
    public string ProviderId => "ibkr";

    /// <inheritdoc />
    public string ProviderDisplayName => BrokerDisplayName;

    /// <inheritdoc />
    public BrokerageCapabilities BrokerageCapabilities { get; } = new()
    {
        SupportedOrderTypes = new HashSet<OrderType>
        {
            OrderType.Market,
            OrderType.Limit,
            OrderType.StopMarket,
            OrderType.StopLimit
        },
        SupportedTimeInForce = new HashSet<TimeInForce>
        {
            TimeInForce.Day,
            TimeInForce.GoodTilCancelled,
            TimeInForce.ImmediateOrCancel,
            TimeInForce.FillOrKill
        },
        SupportsOrderModification = true,
        SupportsPartialFills = true,
        SupportsShortSelling = true,
        SupportsFractionalShares = false,
        SupportsExtendedHours = true,
        SupportedAssetClasses = ["equity", "option", "futures", "forex", "bond"],
        SupportedMarkets = ["US", "EU", "APAC"],
        Extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["supportsFixedIncome"] = "true",
        },
    };

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsConnected)
            return;

        _logger.LogInformation(
            "IB brokerage connecting to {Host}:{Port} (clientId={ClientId}, paper={Paper})",
            _options.Host, _options.Port, _options.ClientId, _options.UsePaperTrading);

        _verifiedAccounts = NoVerifiedAccounts;
        await _client.ConnectAsync(ct).ConfigureAwait(false);
        await PrimeNextOrderIdAsync(ct).ConfigureAwait(false);

        lock (_sessionGate)
            Interlocked.Increment(ref _connectionEpoch);
        _connected = true;

        _logger.LogInformation(
            "IB brokerage connected to {Host}:{Port} (nextValidId={NextOrderId})",
            _client.Host, _client.Port, _nextOrderId);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (!_connected && !_client.IsConnected)
            return;

        await _client.DisconnectAsync(ct).ConfigureAwait(false);
        EndSession(forgetAccounts: true);
        _connected = false;

        _logger.LogInformation("IB brokerage disconnected");
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();
        RejectSessionScopedOrderType(request.Type);

        var gatewayOrderId = ReserveGatewayOrderId();
        var meridianOrderId = request.ClientOrderId ?? $"IB-{gatewayOrderId}";
        var context = SubmittedOrderContext.FromRequest(gatewayOrderId, meridianOrderId, request);
        var pendingOperation = RegisterPendingOrderOperation(gatewayOrderId, PendingOrderOperationKind.Submit);

        _submittedOrders[gatewayOrderId] = context;
        _gatewayOrderIdsByExternalId[meridianOrderId] = gatewayOrderId;
        _gatewayOrderIdsByExternalId[gatewayOrderId.ToString()] = gatewayOrderId;

        try
        {
            await _client.PlaceOrderAsync(gatewayOrderId, request, ct).ConfigureAwait(false);
            return await AwaitPendingOrderOperationAsync(
                gatewayOrderId,
                pendingOperation,
                $"submit order {meridianOrderId}",
                ct).ConfigureAwait(false);
        }
        catch
        {
            _pendingOrderOperations.TryRemove(gatewayOrderId, out _);
            _submittedOrders.TryRemove(gatewayOrderId, out _);
            _gatewayOrderIdsByExternalId.TryRemove(meridianOrderId, out _);
            _gatewayOrderIdsByExternalId.TryRemove(gatewayOrderId.ToString(), out _);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();
        ct.ThrowIfCancellationRequested();

        var (gatewayOrderId, context) = ResolveOrder(orderId);
        var pendingOperation = RegisterPendingOrderOperation(gatewayOrderId, PendingOrderOperationKind.Cancel);

        try
        {
            await _client.CancelOrderAsync(gatewayOrderId, ct).ConfigureAwait(false);
            return await AwaitPendingOrderOperationAsync(
                gatewayOrderId,
                pendingOperation,
                $"cancel order {context.MeridianOrderId}",
                ct).ConfigureAwait(false);
        }
        finally
        {
            ReleaseOrderOperationWaiter(gatewayOrderId, pendingOperation);
        }
    }

    /// <inheritdoc />
    public async Task<ExecutionReport> ModifyOrderAsync(
        string orderId,
        OrderModification modification,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(modification);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        // Send boundary: resolution, request shaping and claiming the operation slot (which a
        // concurrent cancel or amendment may own) are process-local, so a failure or cancellation
        // before PlaceOrderAsync proves the amendment never reached IB. The slot is claimed last,
        // immediately before the send, so nothing can leave it registered without dispatch.
        var sent = false;
        var gatewayOrderId = 0;
        PendingOrderOperation? pendingOperation = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            (gatewayOrderId, var context) = ResolveOrder(orderId);
            // The open-order snapshot omits time-in-force and contract detail; resubmitting those
            // guessed terms could silently change the working order, so recovered orders cancel only.
            if (context.RecoveredFromSnapshot)
                throw new InvalidOperationException(
                    $"Order '{orderId}' was recovered from IB's open-order snapshot and cannot be amended; cancel and resubmit it.");
            var updatedRequest = context.ToModifiedRequest(modification);
            var updatedContext = context with
            {
                Quantity = updatedRequest.Quantity,
                LimitPrice = updatedRequest.LimitPrice,
                StopPrice = updatedRequest.StopPrice
            };
            pendingOperation = RegisterPendingOrderOperation(
                gatewayOrderId, PendingOrderOperationKind.Modify, updatedContext);
            sent = true;
            await _client.PlaceOrderAsync(gatewayOrderId, updatedRequest, ct).ConfigureAwait(false);
            return await AwaitPendingOrderOperationAsync(
                gatewayOrderId,
                pendingOperation,
                $"modify order {context.MeridianOrderId}",
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!sent && ex is not BrokerageRequestNotSentException)
        {
            throw new BrokerageRequestNotSentException(ex.Message, ex);
        }
        finally
        {
            if (pendingOperation is not null)
                ReleaseOrderOperationWaiter(gatewayOrderId, pendingOperation);
        }
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
        var accounts = await GetAccountSummariesAsync(ct).ConfigureAwait(false);
        return accounts.Count switch
        {
            1 => accounts[0],
            0 => throw new InvalidOperationException("IB returned no account summaries."),
            _ => throw new InvalidOperationException(
                "IB returned multiple accounts for an unscoped account-info request. Use the account catalog and account-scoped portfolio sync.")
        };
    }

    private async Task<IReadOnlyList<AccountInfo>> GetAccountSummariesAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();
        ct.ThrowIfCancellationRequested();

        var collector = new AccountSummaryCollector(_options.UsePaperTrading);
        var requestId = _client.ReserveAccountSummaryRequestId();
        _accountSummaryRequests[requestId] = collector;

        try
        {
            ct.ThrowIfCancellationRequested();
            _client.RequestAccountSummary(requestId);
            return await collector.Completion.Task.WaitAsync(CallbackTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _accountSummaryRequests.TryRemove(requestId, out _);
            _client.CancelAccountSummary(requestId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default)
        => (await GetAccountPositionsAsync(ct).ConfigureAwait(false))
            .Select(static position => position.Position)
            .ToArray();

    private async Task<IReadOnlyList<AccountScopedBrokerPosition>> GetAccountPositionsAsync(
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        await _positionsGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _positionsSnapshotBuffer = new ConcurrentDictionary<AccountPositionKey, AccountScopedBrokerPosition>();
            _positionsSnapshotWaiter = new TaskCompletionSource<IReadOnlyList<AccountScopedBrokerPosition>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _client.RequestPositions();
            return await _positionsSnapshotWaiter.Task.WaitAsync(CallbackTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _client.CancelPositions();
            _positionsSnapshotWaiter = null;
            _positionsSnapshotBuffer = null;
            _positionsGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();

        await _openOrdersGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _openOrdersSnapshotBuffer = new ConcurrentDictionary<int, BrokerOrder>();
            _openOrdersSnapshotWaiter = new TaskCompletionSource<IReadOnlyList<BrokerOrder>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _client.RequestOpenOrders();
            return await _openOrdersSnapshotWaiter.Task.WaitAsync(CallbackTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _openOrdersSnapshotWaiter = null;
            _openOrdersSnapshotBuffer = null;
            _openOrdersGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerageExternalAccountDto>> GetAccountsAsync(CancellationToken ct = default)
    {
        var accounts = await GetAccountSummariesAsync(ct).ConfigureAwait(false);
        return accounts
            .Select(account => new BrokerageExternalAccountDto(
                ProviderId,
                account.AccountId,
                string.IsNullOrWhiteSpace(account.AccountId) ? BrokerDisplayName : $"{BrokerDisplayName} {account.AccountId}",
                account.Status,
                account.Currency,
                account.RetrievedAt,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["gatewayId"] = GatewayId,
                    ["executionMode"] = ExecutionMode.ToString()
                }))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(
        string externalAccountId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);

        var accountsTask = GetAccountSummariesAsync(ct);
        var positionsTask = GetAccountPositionsAsync(ct);
        await Task.WhenAll(accountsTask, positionsTask).ConfigureAwait(false);

        var accounts = await accountsTask.ConfigureAwait(false);
        var account = accounts.FirstOrDefault(candidate =>
            string.Equals(candidate.AccountId, externalAccountId, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            throw new InvalidOperationException(
                $"IB account '{externalAccountId}' was not present in the account-summary response.");
        }

        var positions = (await positionsTask.ConfigureAwait(false))
            .Where(position =>
                string.Equals(position.AccountId, externalAccountId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return new BrokeragePortfolioSnapshotDto(
            new BrokerageExternalAccountDto(
                ProviderId,
                account.AccountId,
                string.IsNullOrWhiteSpace(account.AccountId) ? BrokerDisplayName : $"{BrokerDisplayName} {account.AccountId}",
                account.Status,
                account.Currency,
                account.RetrievedAt,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["gatewayId"] = GatewayId,
                    ["executionMode"] = ExecutionMode.ToString()
                }),
            new BrokerageBalanceSnapshotDto(account.Cash, account.Equity, account.BuyingPower, account.Currency),
            positions.Select(position => new BrokeragePositionSnapshotDto(
                position.Position.Symbol,
                position.Position.Quantity,
                position.Position.AverageEntryPrice,
                position.Position.MarketPrice,
                position.Position.MarketValue,
                position.Position.UnrealizedPnl,
                position.Position.AssetClass,
                position.Position.Description,
                position.Position.PositionId,
                position.Currency ?? account.Currency,
                position.Position.Metadata)).ToArray(),
            DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    /// <remarks>
    /// TWS publishes executions during the connected session. This intentionally returns only
    /// those source-identified callbacks; Flex imports remain the controlled backstop for cash,
    /// dividends, interest, fees, FX conversions, and prior-session activity.
    /// </remarks>
    public async Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
        string externalAccountId,
        DateTimeOffset? since = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);

        var orders = await GetOpenOrdersAsync(ct).ConfigureAwait(false);
        var fills = _executions.Values
            .Where(execution => string.Equals(execution.Account, externalAccountId, StringComparison.OrdinalIgnoreCase))
            .Where(execution => !since.HasValue || execution.ExecutedAt >= since.Value)
            .OrderBy(execution => execution.ExecutedAt)
            .Select(execution => new BrokerageFillSnapshotDto(
                execution.ExecutionId,
                execution.OrderId.ToString(),
                execution.Symbol,
                IBCanonicalPayloadMapper.ParseSide(execution.Side, execution.Metadata),
                execution.Shares,
                (decimal)execution.Price,
                execution.ExecutedAt,
                execution.Exchange))
            .ToArray();

        return new BrokerageActivitySnapshotDto(
            ProviderId,
            externalAccountId,
            DateTimeOffset.UtcNow,
            orders
                .Where(order =>
                    int.TryParse(order.OrderId, out var orderId) &&
                    _openOrderAccounts.TryGetValue(orderId, out var accountId) &&
                    string.Equals(accountId, externalAccountId, StringComparison.OrdinalIgnoreCase))
                .Select(order => new BrokerageOrderSnapshotDto(
                order.OrderId, order.ClientOrderId, order.Symbol, order.Side, order.Type, order.Status,
                order.Quantity, order.FilledQuantity, order.LimitPrice, order.StopPrice, order.CreatedAt)).ToArray(),
            fills,
            Array.Empty<BrokerageCashTransactionDto>());
    }

    /// <inheritdoc />
    public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        if (_client is UnsupportedIBBrokerageClient unsupportedClient)
        {
            return Task.FromResult(BrokerHealthStatus.Unhealthy(unsupportedClient.GuidanceMessage));
        }

        if (!IsConnected)
        {
            return Task.FromResult(BrokerHealthStatus.Unhealthy(
                $"IB brokerage is not connected to {_options.Host}:{_options.Port}."));
        }

        return Task.FromResult(BrokerHealthStatus.Healthy(
            $"Connected to {_client.Host}:{_client.Port} (client {_client.ClientId}, {(_options.UsePaperTrading ? "paper" : "live")})"));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        _client.NextValidIdReceived -= OnNextValidIdReceived;
        _client.OrderStatusReceived -= OnOrderStatusReceived;
        _client.OpenOrderReceived -= OnOpenOrderReceived;
        _client.OpenOrdersCompleted -= OnOpenOrdersCompleted;
        _client.ExecutionDetailsReceived -= OnExecutionDetailsReceived;
        _client.PositionReceived -= OnPositionReceived;
        _client.PositionsCompleted -= OnPositionsCompleted;
        _client.AccountSummaryReceived -= OnAccountSummaryReceived;
        _client.AccountSummaryCompleted -= OnAccountSummaryCompleted;
        _client.ErrorOccurred -= OnErrorOccurred;
        _client.ManagedAccountsReceived -= OnManagedAccountsReceived;
        _client.ConnectionLost -= OnConnectionLost;
        _client.ConnectionRestored -= OnConnectionRestored;

        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "IB brokerage disposal disconnect cleanup skipped");
        }

        _openOrdersGate.Dispose();
        _positionsGate.Dispose();
        _reportChannel.Writer.TryComplete();
        _client.Dispose();
    }

    private static IIBBrokerageClient CreateBrokerageClient(IBOptions options)
    {
#if IBAPI
        var publisher = new NullMarketEventPublisher();
        var router = new IBCallbackRouter(
            new MarketDepthCollector(publisher, requireExplicitSubscription: false),
            new TradeDataCollector(publisher, null));

        return new EnhancedIBConnectionManager(
            router,
            options.Host,
            options.Port,
            options.ClientId);
#else
        return new UnsupportedIBBrokerageClient(options);
#endif
    }

    private async Task PrimeNextOrderIdAsync(CancellationToken ct)
    {
        var waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousWaiter = Interlocked.Exchange(ref _nextValidIdWaiter, waiter);
        previousWaiter?.TrySetCanceled();

        _client.RequestNextValidId();

        try
        {
            _nextOrderId = await waiter.Task.WaitAsync(CallbackTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"IB did not send nextValidId within {CallbackTimeout.TotalSeconds:0} seconds after connect.",
                ex);
        }
        finally
        {
            Interlocked.CompareExchange(ref _nextValidIdWaiter, null, waiter);
        }
    }

    private int ReserveGatewayOrderId()
    {
        var reserved = Interlocked.Increment(ref _nextOrderId);
        return reserved - 1;
    }

    private PendingOrderOperation RegisterPendingOrderOperation(
        int gatewayOrderId,
        PendingOrderOperationKind kind,
        SubmittedOrderContext? proposedContext = null)
    {
        var operation = new PendingOrderOperation(
            kind,
            new TaskCompletionSource<ExecutionReport>(TaskCreationOptions.RunContinuationsAsynchronously),
            proposedContext,
            _latestOrderReports.TryGetValue(gatewayOrderId, out var latest) ? latest.OrderStatus : OrderStatus.Accepted);

        if (!_pendingOrderOperations.TryAdd(gatewayOrderId, operation))
            throw new InvalidOperationException($"IB order {gatewayOrderId} already has a pending operation.");
        if (kind == PendingOrderOperationKind.Modify && !_unconfirmedAmendments.TryAdd(gatewayOrderId, operation))
        {
            RemovePendingOrderOperation(gatewayOrderId, operation);
            throw new InvalidOperationException($"IB order {gatewayOrderId} already has an unconfirmed amendment.");
        }
        return operation;
    }

    private async Task<ExecutionReport> AwaitPendingOrderOperationAsync(
        int gatewayOrderId,
        PendingOrderOperation operation,
        string description,
        CancellationToken ct)
    {
        try
        {
            return await operation.Completion.Task.WaitAsync(CallbackTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"IB did not acknowledge the request to {description} within {CallbackTimeout.TotalSeconds:0} seconds.",
                ex);
        }
        finally
        {
            ReleaseOrderOperationWaiter(gatewayOrderId, operation);
        }
    }

    private void ReleaseOrderOperationWaiter(int gatewayOrderId, PendingOrderOperation operation)
    {
        // Stop waiting without withdrawing a dispatched amendment. The separate amendment
        // record remains available to late callbacks while cancellation can still proceed.
        RemovePendingOrderOperation(gatewayOrderId, operation);
    }

    private void RemovePendingOrderOperation(int gatewayOrderId, PendingOrderOperation operation)
        => ((ICollection<KeyValuePair<int, PendingOrderOperation>>)_pendingOrderOperations)
            .Remove(new KeyValuePair<int, PendingOrderOperation>(gatewayOrderId, operation));

    private (int GatewayOrderId, SubmittedOrderContext Context) ResolveOrder(string orderId)
    {
        if (_gatewayOrderIdsByExternalId.TryGetValue(orderId, out var gatewayOrderId) &&
            _submittedOrders.TryGetValue(gatewayOrderId, out var context))
        {
            return (gatewayOrderId, context);
        }

        if (int.TryParse(orderId, out gatewayOrderId) &&
            _submittedOrders.TryGetValue(gatewayOrderId, out context))
        {
            return (gatewayOrderId, context);
        }

        throw new InvalidOperationException(
            $"Order '{orderId}' is unknown to the IB brokerage gateway. Submit the order before modifying or cancelling it.");
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException(
                "IB brokerage gateway is not connected. Call ConnectAsync first.");
        }
    }

    private static void RejectSessionScopedOrderType(OrderType type)
    {
        if (type is OrderType.MarketOnOpen or OrderType.MarketOnClose or OrderType.LimitOnOpen or OrderType.LimitOnClose)
        {
            throw new NotSupportedException(
                $"Interactive Brokers gateway does not currently preserve the {type} session timing qualifier.");
        }
    }

    private void OnNextValidIdReceived(object? sender, int nextValidId)
    {
        _nextOrderId = nextValidId;
        Interlocked.Exchange(ref _nextValidIdWaiter, null)?.TrySetResult(nextValidId);
    }

    private void OnAccountSummaryReceived(object? sender, IBAccountSummaryUpdate update)
    {
        if (_accountSummaryRequests.TryGetValue(update.RequestId, out var collector))
        {
            collector.Apply(update);
        }
    }

    private void OnAccountSummaryCompleted(object? sender, int requestId)
    {
        if (_accountSummaryRequests.TryGetValue(requestId, out var collector))
        {
            collector.TryComplete();
        }
    }

    private void OnPositionReceived(object? sender, IBPositionUpdate update)
    {
        var buffer = _positionsSnapshotBuffer;
        if (buffer is null || string.IsNullOrWhiteSpace(update.Symbol))
            return;

        if (string.IsNullOrWhiteSpace(update.Account))
        {
            _logger.LogWarning(
                "IB position callback for {Symbol} did not include an account identifier and was ignored",
                update.Symbol);
            return;
        }

        var accountId = update.Account.Trim();
        var symbol = update.Symbol.Trim();
        var averageCost = (decimal)update.AverageCost;
        var marketValue = Math.Abs(update.Quantity) * averageCost;
        var accruedInterest = TryGetDecimal(update.Metadata, "accrued_interest");

        var contractIdentity = BuildPositionContractIdentity(update);
        var key = new AccountPositionKey(accountId.ToUpperInvariant(), contractIdentity);
        buffer[key] = new AccountScopedBrokerPosition(
            accountId,
            string.IsNullOrWhiteSpace(update.Currency) ? null : update.Currency.Trim(),
            new BrokerPosition
            {
                PositionId = $"{key.AccountId}:{contractIdentity}",
                Symbol = symbol,
                Quantity = update.Quantity,
                AverageEntryPrice = averageCost,
                MarketPrice = averageCost,
                MarketValue = marketValue,
                UnrealizedPnl = 0m,
                AssetClass = IBCanonicalPayloadMapper.MapAssetClass(update.SecurityType, update.Metadata),
                Metadata = update.Metadata,
                AccruedInterest = accruedInterest
            });
    }

    private void OnPositionsCompleted(object? sender, EventArgs e)
    {
        _positionsSnapshotWaiter?.TrySetResult(
            _positionsSnapshotBuffer?.Values
                .OrderBy(p => p.AccountId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Position.Symbol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Position.PositionId, StringComparer.Ordinal)
                .ToArray()
            ?? Array.Empty<AccountScopedBrokerPosition>());
    }

    private void OnOpenOrderReceived(object? sender, IBOpenOrderUpdate update)
    {
        var gatewayOrderId = update.OrderId;
        if (string.IsNullOrWhiteSpace(update.Account))
            _openOrderAccounts.TryRemove(gatewayOrderId, out _);
        else
            _openOrderAccounts[gatewayOrderId] = update.Account.Trim();
        _openOrderSecurityTypes[gatewayOrderId] = update.SecurityType ?? string.Empty;

        var context = _submittedOrders.TryGetValue(gatewayOrderId, out var tracked) ? tracked : null;
        _pendingOrderOperations.TryGetValue(gatewayOrderId, out var pending);
        _unconfirmedAmendments.TryGetValue(gatewayOrderId, out var amendment);
        _latestOrderReports.TryGetValue(gatewayOrderId, out var latest);
        var status = IBCanonicalPayloadMapper.MapOrderStatus(update.Status, update.FilledQuantity,
            Math.Max(update.Quantity - update.FilledQuantity, 0m), update.RejectReason, update.Metadata);

        // A refused amendment describes the operation, not rejection of the working order.
        // Keep the last confirmed request until IB echoes the amended terms.
        var refusedOperation = pending?.Kind == PendingOrderOperationKind.Cancel ? pending : amendment;
        if (refusedOperation is not null && status == OrderStatus.Rejected)
        {
            RejectPendingOperation(gatewayOrderId, refusedOperation, context!, update.RejectReason ?? update.Status,
                update.FilledQuantity);
            return;
        }

        if (latest?.OrderStatus == OrderStatus.Filled)
            status = OrderStatus.Filled;
        else if (latest is not null && IsTerminalOrderStatus(latest.OrderStatus) && !IsTerminalOrderStatus(status))
            status = latest.OrderStatus;
        else if (latest?.OrderStatus == OrderStatus.PendingCancel && pending?.Kind == PendingOrderOperationKind.Cancel &&
            !IsTerminalOrderStatus(status))
            status = OrderStatus.PendingCancel;
        else if (status == OrderStatus.Accepted && Math.Max(update.FilledQuantity, latest?.FilledQuantity ?? 0m) > 0m)
            status = OrderStatus.PartiallyFilled;

        var amendmentConfirmed = amendment is not null &&
            (status is OrderStatus.Accepted or OrderStatus.PartiallyFilled or OrderStatus.PendingCancel or OrderStatus.Filled or OrderStatus.Cancelled ||
                string.Equals(update.Status, "PreSubmitted", StringComparison.OrdinalIgnoreCase)) &&
            amendment.ProposedContext is { } proposed && MatchesAmendment(update, proposed);
        if (amendmentConfirmed)
        {
            context = amendment!.ProposedContext;
            _submittedOrders[gatewayOrderId] = context!;
            if (Math.Max(update.FilledQuantity, latest?.FilledQuantity ?? 0m) >= context!.Quantity)
                status = OrderStatus.Filled;
        }

        var brokerOrder = new BrokerOrder
        {
            OrderId = gatewayOrderId.ToString(),
            ClientOrderId = context?.ClientOrderId ?? update.ClientOrderId,
            Symbol = context?.Symbol ?? update.Symbol,
            Side = context?.Side ?? IBCanonicalPayloadMapper.ParseSide(update.Action, update.Metadata),
            Type = context?.Type ?? IBCanonicalPayloadMapper.ParseOrderType(update.OrderType, update.Metadata),
            Quantity = update.Quantity,
            FilledQuantity = Math.Max(update.FilledQuantity, latest?.FilledQuantity ?? 0m),
            LimitPrice = update.LimitPrice is > 0 ? (decimal)update.LimitPrice.Value : null,
            StopPrice = update.StopPrice is > 0 ? (decimal)update.StopPrice.Value : null,
            Status = status,
            CreatedAt = context?.CreatedAt ?? update.ReceivedAt
        };

        if (IsTerminalOrderStatus(status))
            _openOrders.TryRemove(gatewayOrderId, out _);
        else
            _openOrders[gatewayOrderId] = brokerOrder;
        if (_openOrdersSnapshotBuffer is { } snapshot)
        {
            if (IsTerminalOrderStatus(status))
                snapshot.TryRemove(gatewayOrderId, out _);
            else
                snapshot[gatewayOrderId] = brokerOrder;
        }

        if (pending is null && amendment is null)
            return;
        if (!amendmentConfirmed && pending?.Kind == PendingOrderOperationKind.Cancel)
        {
            // openOrder can also deliver the terminal state while cancellation is pending.
            if (IsTerminalOrderStatus(status))
                OnOrderStatusReceived(sender, new IBOrderStatusUpdate(gatewayOrderId, update.Status,
                    brokerOrder.FilledQuantity, Math.Max(update.Quantity - brokerOrder.FilledQuantity, 0m),
                    0d, 0d, 0L, _client.ClientId, null, update.RejectReason, update.ReceivedAt));
            return;
        }
        if (amendment is not null && !amendmentConfirmed)
        {
            if (IsTerminalOrderStatus(status))
                OnOrderStatusReceived(sender, new IBOrderStatusUpdate(gatewayOrderId, update.Status,
                    brokerOrder.FilledQuantity, Math.Max(update.Quantity - brokerOrder.FilledQuantity, 0m),
                    0d, 0d, 0L, _client.ClientId, null, update.RejectReason, update.ReceivedAt));
            return;
        }

        var report = BuildExecutionReport(
            gatewayOrderId,
            status == OrderStatus.Rejected ? ExecutionReportType.Rejected :
                amendmentConfirmed ? ExecutionReportType.Modified : ExecutionReportType.New,
            status, context, brokerOrder.Symbol, brokerOrder.Side, brokerOrder.Quantity,
            brokerOrder.FilledQuantity, latest?.FillPrice, update.Status, update.RejectReason,
            update.Commission is > 0 ? (decimal)update.Commission.Value : null, update.ReceivedAt,
            latest?.FillPriceIsCumulativeAverage ?? false) with
        {
            HasOrderTerms = amendmentConfirmed,
            OrderLimitPrice = amendmentConfirmed ? context?.LimitPrice : null,
            OrderStopPrice = amendmentConfirmed ? context?.StopPrice : null
        };
        PublishReport(report);
        if (amendmentConfirmed)
        {
            CompleteAmendment(gatewayOrderId, amendment!, report);
            if (pending?.Kind == PendingOrderOperationKind.Cancel && IsTerminalOrderStatus(status))
                OnOrderStatusReceived(sender, new IBOrderStatusUpdate(gatewayOrderId, status.ToString(),
                    brokerOrder.FilledQuantity, Math.Max(update.Quantity - brokerOrder.FilledQuantity, 0m),
                    0d, 0d, 0L, _client.ClientId, null, null, update.ReceivedAt));
        }
        else
        {
            CompletePendingOrderOperation(gatewayOrderId, report);
        }
    }

    private static bool MatchesAmendment(IBOpenOrderUpdate update, SubmittedOrderContext proposed)
        => update.Quantity == proposed.Quantity &&
            (update.LimitPrice is > 0 ? (decimal)update.LimitPrice.Value : null) == proposed.LimitPrice &&
            (update.StopPrice is > 0 ? (decimal)update.StopPrice.Value : null) == proposed.StopPrice;

    private static bool IsTerminalOrderStatus(OrderStatus status)
        => status is OrderStatus.Cancelled or OrderStatus.Filled or OrderStatus.Rejected or OrderStatus.Expired;

    private void OnOpenOrdersCompleted(object? sender, EventArgs e)
    {
        _openOrdersSnapshotWaiter?.TrySetResult(
            _openOrdersSnapshotBuffer?.Values
                .OrderBy(o => o.Symbol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(o => o.OrderId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            ?? Array.Empty<BrokerOrder>());
    }

    private void OnOrderStatusReceived(object? sender, IBOrderStatusUpdate update)
    {
        var gatewayOrderId = update.OrderId;
        var context = _submittedOrders.TryGetValue(gatewayOrderId, out var tracked) ? tracked : null;
        _latestOrderReports.TryGetValue(gatewayOrderId, out var latest);
        _pendingOrderOperations.TryGetValue(gatewayOrderId, out var pending);
        _unconfirmedAmendments.TryGetValue(gatewayOrderId, out var amendment);
        var filled = Math.Max(update.Filled, latest?.FilledQuantity ?? 0m);
        var orderStatus = IBCanonicalPayloadMapper.MapOrderStatus(update.Status, filled, update.Remaining, update.RejectReason);
        var refusedOperation = pending?.Kind == PendingOrderOperationKind.Cancel ? pending : amendment;
        if (refusedOperation is not null &&
            orderStatus == OrderStatus.Rejected && context is not null)
        {
            RejectPendingOperation(gatewayOrderId, refusedOperation, context, update.RejectReason ?? update.Status,
                filled, update.AverageFillPrice > 0 ? (decimal)update.AverageFillPrice : null);
            return;
        }
        if (orderStatus == OrderStatus.Accepted && filled > 0m)
            orderStatus = OrderStatus.PartiallyFilled;
        // Late working callbacks must not revive an already completed order.
        if (latest?.OrderStatus == OrderStatus.Filled)
            orderStatus = OrderStatus.Filled;
        else if (latest is not null && IsTerminalOrderStatus(latest.OrderStatus) && !IsTerminalOrderStatus(orderStatus))
            orderStatus = latest.OrderStatus;
        else if (latest?.OrderStatus == OrderStatus.PendingCancel && pending?.Kind == PendingOrderOperationKind.Cancel &&
            !IsTerminalOrderStatus(orderStatus))
            orderStatus = OrderStatus.PendingCancel;

        if (_openOrders.TryGetValue(gatewayOrderId, out var openOrder))
        {
            var refreshed = openOrder with { FilledQuantity = filled, Status = orderStatus };
            if (IsTerminalOrderStatus(orderStatus))
                _openOrders.TryRemove(gatewayOrderId, out _);
            else
                _openOrders[gatewayOrderId] = refreshed;
            if (_openOrdersSnapshotBuffer is { } snapshot)
            {
                if (IsTerminalOrderStatus(orderStatus))
                    snapshot.TryRemove(gatewayOrderId, out _);
                else
                    snapshot[gatewayOrderId] = refreshed;
            }
        }

        var reportType = orderStatus switch
        {
            OrderStatus.Cancelled => ExecutionReportType.Cancelled,
            OrderStatus.Rejected => ExecutionReportType.Rejected,
            OrderStatus.Filled => ExecutionReportType.Fill,
            OrderStatus.PartiallyFilled => ExecutionReportType.PartialFill,
            _ when filled > (latest?.FilledQuantity ?? 0m) => ExecutionReportType.PartialFill,
            _ => ExecutionReportType.New
        };
        var report = BuildExecutionReport(
            gatewayOrderId, reportType, orderStatus, context,
            context?.Symbol ?? openOrder?.Symbol ?? gatewayOrderId.ToString(),
            context?.Side ?? openOrder?.Side ?? OrderSide.Buy,
            amendment?.ProposedContext is { } proposed
                ? Math.Max(context?.Quantity ?? 0m, proposed.Quantity)
                : context?.Quantity ?? openOrder?.Quantity ?? filled + update.Remaining,
            filled,
            update.Filled < (latest?.FilledQuantity ?? 0m) ? latest?.FillPrice :
                update.AverageFillPrice > 0 ? (decimal)update.AverageFillPrice : update.LastFillPrice > 0 ? (decimal)update.LastFillPrice : latest?.FillPrice,
            update.Status, update.RejectReason, null, update.ReceivedAt,
            update.Filled < (latest?.FilledQuantity ?? 0m) ? latest!.FillPriceIsCumulativeAverage :
                update.AverageFillPrice > 0 || (update.LastFillPrice <= 0 && latest?.FillPriceIsCumulativeAverage == true));
        PublishReport(report);

        // PendingCancel/Submitted and partial fills are progress, not confirmation of a cancel.
        // A status-only callback cannot confirm amended quantity or price either.
        if (IsTerminalOrderStatus(orderStatus) && pending?.Kind != PendingOrderOperationKind.Modify)
        {
            CompletePendingOrderOperation(gatewayOrderId, report);
        }
        else if (pending?.Kind == PendingOrderOperationKind.Submit && orderStatus != OrderStatus.PendingCancel)
        {
            CompletePendingOrderOperation(gatewayOrderId, report);
        }
    }

    private void OnExecutionDetailsReceived(object? sender, IBExecutionUpdate update)
    {
        if (!string.IsNullOrWhiteSpace(update.ExecutionId))
            _executions[update.ExecutionId] = update;

        var gatewayOrderId = update.OrderId;
        var context = _submittedOrders.TryGetValue(gatewayOrderId, out var tracked) ? tracked : null;
        _pendingOrderOperations.TryGetValue(gatewayOrderId, out var pending);
        _unconfirmedAmendments.TryGetValue(gatewayOrderId, out var amendment);
        _latestOrderReports.TryGetValue(gatewayOrderId, out var latest);
        var staleFill = update.CumulativeQuantity < (latest?.FilledQuantity ?? 0m);
        var cumulativeQuantity = Math.Max(update.CumulativeQuantity, latest?.FilledQuantity ?? 0m);
        var orderQuantity = amendment?.ProposedContext is { } proposed
            ? Math.Max(context?.Quantity ?? 0m, proposed.Quantity)
            : context?.Quantity ?? update.CumulativeQuantity;
        var reportType = cumulativeQuantity >= orderQuantity
            ? ExecutionReportType.Fill
            : ExecutionReportType.PartialFill;
        var orderStatus = reportType == ExecutionReportType.Fill || latest?.OrderStatus == OrderStatus.Filled
            ? OrderStatus.Filled
            : latest?.OrderStatus is OrderStatus.Cancelled or OrderStatus.PendingCancel
                ? latest.OrderStatus
                : OrderStatus.PartiallyFilled;
        if (orderStatus == OrderStatus.Filled)
            reportType = ExecutionReportType.Fill;

        var report = BuildExecutionReport(
            gatewayOrderId,
            reportType,
            orderStatus,
            context,
            context?.Symbol ?? update.Symbol,
            context?.Side ?? IBCanonicalPayloadMapper.ParseSide(update.Side),
            orderQuantity,
            cumulativeQuantity,
            staleFill ? latest?.FillPrice : update.AveragePrice > 0 ? (decimal)update.AveragePrice : (decimal)update.Price,
            null,
            null,
            null,
            update.ExecutedAt,
            staleFill ? latest!.FillPriceIsCumulativeAverage : update.AveragePrice > 0);

        if (_openOrders.TryGetValue(gatewayOrderId, out var openOrder))
        {
            if (orderStatus == OrderStatus.Filled)
            {
                _openOrders.TryRemove(gatewayOrderId, out _);
            }
            else
            {
                _openOrders[gatewayOrderId] = openOrder with
                {
                    FilledQuantity = cumulativeQuantity,
                    Status = orderStatus
                };
            }
        }

        PublishReport(report);
        if (orderStatus == OrderStatus.Filled && pending?.Kind != PendingOrderOperationKind.Modify)
        {
            CompletePendingOrderOperation(gatewayOrderId, report);
        }
    }

    private void OnErrorOccurred(object? sender, IBApiError error)
    {
        // IB sends informational/warning messages through the same error callback. These
        // neither refuse a request nor provide terminal order-state evidence.
        if (error.ErrorCode is 399 or 10167 or 10197 || error.ErrorCode is >= 2100 and < 2200 and not 2102)
        {
            _logger.LogInformation("IB message {ErrorCode}: {Message}", error.ErrorCode, error.ErrorMessage);
            return;
        }

        if (_accountSummaryRequests.TryRemove(error.RequestId, out var collector))
        {
            collector.Completion.TrySetException(new IBApiException(error.ErrorCode, error.ErrorMessage));
            return;
        }

        if (error.RequestId < 0 || !_submittedOrders.TryGetValue(error.RequestId, out var context))
            return;

        if (error.ErrorCode == 202)
        {
            _latestOrderReports.TryGetValue(error.RequestId, out var latest);
            OnOrderStatusReceived(sender, new IBOrderStatusUpdate(error.RequestId, "Cancelled",
                latest?.FilledQuantity ?? 0m, 0m, (double)(latest?.FillPrice ?? 0m), 0d,
                0L, _client.ClientId, null, null, DateTimeOffset.UtcNow));
            return;
        }

        _pendingOrderOperations.TryGetValue(error.RequestId, out var pending);
        _unconfirmedAmendments.TryGetValue(error.RequestId, out var amendment);
        // A cancel may follow an amendment whose caller stopped waiting. IB identifies
        // these refusals by error code even though both use the same stable order ID.
        var refusedOperation = error.ErrorCode is 161 or 10147 or 10148
            ? pending?.Kind == PendingOrderOperationKind.Cancel ? pending : null
            : error.ErrorCode is 104 or 105 or 106 or 107 or 110 or 201 or 2102
                ? amendment ?? (pending?.Kind == PendingOrderOperationKind.Cancel ? pending : null)
                : pending?.Kind is PendingOrderOperationKind.Cancel or PendingOrderOperationKind.Modify ? pending : amendment;
        if (refusedOperation is not null)
        {
            RejectPendingOperation(error.RequestId, refusedOperation, context, error.ErrorMessage);
            return;
        }

        // An error associated with an old order ID is not proof that a working order
        // was rejected. Code 201 explicitly reports order rejection; other errors can
        // reject a pending submission, but cannot terminalize an established order.
        if (pending?.Kind != PendingOrderOperationKind.Submit && error.ErrorCode != 201)
        {
            _logger.LogWarning("IB order {OrderId} message {ErrorCode}: {Message}",
                error.RequestId, error.ErrorCode, error.ErrorMessage);
            return;
        }

        _latestOrderReports.TryGetValue(error.RequestId, out var previous);
        var report = BuildExecutionReport(
            error.RequestId,
            ExecutionReportType.Rejected,
            OrderStatus.Rejected,
            context,
            context.Symbol,
            context.Side,
            context.Quantity,
            previous?.FilledQuantity ?? 0m,
            previous?.FillPrice,
            null,
            error.ErrorMessage,
            null,
            DateTimeOffset.UtcNow);

        PublishReport(report);
        _openOrders.TryRemove(error.RequestId, out _);
        CompletePendingOrderOperation(error.RequestId, report);
    }

    private void RejectPendingOperation(
        int gatewayOrderId,
        PendingOrderOperation pending,
        SubmittedOrderContext context,
        string reason,
        decimal? filledQuantity = null,
        decimal? averageFillPrice = null)
    {
        _latestOrderReports.TryGetValue(gatewayOrderId, out var latest);
        var filled = Math.Max(latest?.FilledQuantity ?? 0m, filledQuantity ?? 0m);
        var orderQuantity = pending.Kind == PendingOrderOperationKind.Cancel &&
            _unconfirmedAmendments.TryGetValue(gatewayOrderId, out var amendment) && amendment.ProposedContext is { } proposed
                ? Math.Max(context.Quantity, proposed.Quantity)
                : context.Quantity;
        var status = latest?.OrderStatus ?? pending.PreviousStatus;
        if (status == OrderStatus.PendingCancel && pending.Kind == PendingOrderOperationKind.Cancel)
            status = pending.PreviousStatus == OrderStatus.PendingCancel ? OrderStatus.Accepted : pending.PreviousStatus;
        if (!IsTerminalOrderStatus(status) && status != OrderStatus.PendingCancel)
            status = filled > 0m ? OrderStatus.PartiallyFilled : status;
        if (orderQuantity > 0m && filled >= orderQuantity)
            status = OrderStatus.Filled;
        var report = BuildExecutionReport(
            gatewayOrderId,
            pending.Kind == PendingOrderOperationKind.Cancel
                ? ExecutionReportType.CancelRejected : ExecutionReportType.ModificationRejected,
            status, context, context.Symbol, context.Side, orderQuantity, filled,
            averageFillPrice ?? latest?.FillPrice, null, reason, null, DateTimeOffset.UtcNow,
            averageFillPrice.HasValue || latest?.FillPriceIsCumulativeAverage == true);
        if (IsTerminalOrderStatus(status))
        {
            _openOrders.TryRemove(gatewayOrderId, out _);
            _openOrdersSnapshotBuffer?.TryRemove(gatewayOrderId, out _);
        }
        else if (_openOrders.TryGetValue(gatewayOrderId, out var openOrder))
        {
            var refreshed = openOrder with { Status = status, FilledQuantity = filled };
            _openOrders[gatewayOrderId] = refreshed;
            if (_openOrdersSnapshotBuffer is { } snapshot)
                snapshot[gatewayOrderId] = refreshed;
        }
        PublishReport(report);
        if (pending.Kind == PendingOrderOperationKind.Modify)
            CompleteAmendment(gatewayOrderId, pending, report);
        else
        {
            RemovePendingOrderOperation(gatewayOrderId, pending);
            pending.Completion.TrySetResult(report);
        }
    }

    private void PublishReport(ExecutionReport report)
    {
        if (int.TryParse(report.GatewayOrderId, out var gatewayOrderId))
            _latestOrderReports[gatewayOrderId] = report;
        if (!_reportChannel.Writer.TryWrite(report))
        {
            _ = _reportChannel.Writer.WriteAsync(report);
        }
    }

    private void CompletePendingOrderOperation(int gatewayOrderId, ExecutionReport report)
    {
        if (_pendingOrderOperations.TryRemove(gatewayOrderId, out var pending))
        {
            pending.Completion.TrySetResult(report);
        }
    }

    private void CompleteAmendment(int gatewayOrderId, PendingOrderOperation amendment, ExecutionReport report)
    {
        ((ICollection<KeyValuePair<int, PendingOrderOperation>>)_unconfirmedAmendments)
            .Remove(new KeyValuePair<int, PendingOrderOperation>(gatewayOrderId, amendment));
        RemovePendingOrderOperation(gatewayOrderId, amendment);
        amendment.Completion.TrySetResult(report);
    }

    private static ExecutionReport BuildExecutionReport(
        int gatewayOrderId,
        ExecutionReportType reportType,
        OrderStatus orderStatus,
        SubmittedOrderContext? context,
        string symbol,
        OrderSide side,
        decimal quantity,
        decimal filledQuantity,
        decimal? fillPrice,
        string? brokerStatus,
        string? rejectReason,
        decimal? commission,
        DateTimeOffset timestamp,
        bool fillPriceIsCumulativeAverage = false)
    {
        var optionContract = context?.OptionContract ?? TryParseOptionContract(context?.Metadata);
        var diagnostics = BuildDiagnostics(brokerStatus, rejectReason);
        return new ExecutionReport
        {
            OrderId = context?.MeridianOrderId ?? gatewayOrderId.ToString(),
            ClientOrderId = context?.ClientOrderId,
            ReportType = reportType,
            Symbol = symbol,
            Side = side,
            OrderStatus = orderStatus,
            OrderQuantity = quantity,
            FilledQuantity = filledQuantity,
            FillPrice = fillPrice,
            FillPriceIsCumulativeAverage = fillPriceIsCumulativeAverage,
            Commission = commission,
            RejectReason = rejectReason,
            GatewayOrderId = gatewayOrderId.ToString(),
            Timestamp = timestamp,
            Legs = context?.Legs,
            OptionContract = optionContract,
            Diagnostics = diagnostics
        };
    }

    private static decimal? TryGetDecimal(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var raw))
            return null;

        return decimal.TryParse(raw, out var parsed)
            ? parsed
            : null;
    }

    private static string BuildPositionContractIdentity(IBPositionUpdate update)
    {
        var metadata = update.Metadata;
        var conId = GetMetadata(metadata, "con_id") ?? GetMetadata(metadata, "conId");
        if (long.TryParse(conId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var contractId) && contractId > 0)
            return $"con_id:{contractId.ToString(CultureInfo.InvariantCulture)}";

        // Qualified socket callbacks normally contain conId. The full descriptor is a
        // deterministic fallback for clients without it: a symbol alone cannot identify
        // strikes, expiries, currencies, or separate instruments sharing an underlying.
        var strike = GetMetadata(metadata, "strike");
        if (decimal.TryParse(strike, NumberStyles.Number, CultureInfo.InvariantCulture, out var strikeValue))
            strike = strikeValue.ToString("G29", CultureInfo.InvariantCulture);
        var right = GetMetadata(metadata, "right")?.Trim().ToUpperInvariant();
        right = right is "CALL" ? "C" : right is "PUT" ? "P" : right;
        var components = new[]
        {
            update.SecurityType ?? GetMetadata(metadata, "sec_type"), update.Symbol,
            GetMetadata(metadata, "local_symbol"), GetMetadata(metadata, "trading_class"),
            GetMetadata(metadata, "last_trade_date_or_contract_month"), strike, right,
            GetMetadata(metadata, "multiplier"), update.Currency ?? GetMetadata(metadata, "currency"),
            GetMetadata(metadata, "primary_exchange") ?? update.Exchange ?? GetMetadata(metadata, "exchange")
        };
        return "contract:" + string.Join(":", components.Select(static value =>
            Uri.EscapeDataString(value?.Trim().ToUpperInvariant() ?? string.Empty)));
    }

    private static OptionContractIdentity? TryParseOptionContract(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
            return null;

        var secType = GetMetadata(metadata, "sec_type");
        if (!string.Equals(secType, "OPT", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(secType, "FOP", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var right = GetMetadata(metadata, "right");
        right = string.Equals(right, "CALL", StringComparison.OrdinalIgnoreCase) ? "C" :
            string.Equals(right, "PUT", StringComparison.OrdinalIgnoreCase) ? "P" : right?.ToUpperInvariant();

        return new OptionContractIdentity
        {
            UnderlyingSymbol = GetMetadata(metadata, "underlying_symbol"),
            LocalSymbol = GetMetadata(metadata, "local_symbol"),
            TradingClass = GetMetadata(metadata, "trading_class"),
            Multiplier = GetMetadata(metadata, "multiplier"),
            ContractMonth = GetMetadata(metadata, "last_trade_date_or_contract_month"),
            ExpirationDate = DateOnly.TryParseExact(GetMetadata(metadata, "last_trade_date_or_contract_month")?.Substring(0, Math.Min(8, GetMetadata(metadata, "last_trade_date_or_contract_month")?.Length ?? 0)), "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var expiry) ? expiry : null,
            StrikePrice = TryGetDecimal(metadata, "strike"),
            Right = right
        };
    }

    private static ExecutionDiagnostics? BuildDiagnostics(string? brokerStatus, string? rejectReason)
    {
        if (string.IsNullOrWhiteSpace(brokerStatus) && string.IsNullOrWhiteSpace(rejectReason))
            return null;

        var reason = rejectReason ?? string.Empty;
        var category =
            reason.Contains("insufficient", StringComparison.OrdinalIgnoreCase) ? "risk.margin" :
            reason.Contains("outside", StringComparison.OrdinalIgnoreCase) || reason.Contains("rth", StringComparison.OrdinalIgnoreCase) ? "session.restriction" :
            reason.Contains("permission", StringComparison.OrdinalIgnoreCase) || reason.Contains("not subscribed", StringComparison.OrdinalIgnoreCase) ? "entitlement" :
            reason.Contains("price", StringComparison.OrdinalIgnoreCase) || reason.Contains("tick", StringComparison.OrdinalIgnoreCase) ? "validation.price" :
            reason.Contains("size", StringComparison.OrdinalIgnoreCase) || reason.Contains("quantity", StringComparison.OrdinalIgnoreCase) ? "validation.size" :
            "broker.unknown";

        return new ExecutionDiagnostics
        {
            BrokerStatus = brokerStatus,
            BrokerRejectReason = rejectReason,
            Category = category,
            Severity = string.IsNullOrWhiteSpace(rejectReason) ? "info" : "error",
            RecommendedAction = category switch
            {
                "risk.margin" => "Reduce order size or free buying power before retry.",
                "session.restriction" => "Route during regular trading hours or enable extended-hours routing.",
                "entitlement" => "Check market data/trading permissions for the account.",
                "validation.price" => "Review limit/stop values against venue tick size and collars.",
                "validation.size" => "Review quantity constraints and lot-size requirements.",
                _ => "Review broker diagnostics and escalate if repeated."
            }
        };
    }

    private static string? GetMetadata(IReadOnlyDictionary<string, string>? metadata, string key)
        => metadata is not null && metadata.TryGetValue(key, out var value) ? value : null;

    private sealed record SubmittedOrderContext(
        int GatewayOrderId,
        string MeridianOrderId,
        string? ClientOrderId,
        string Symbol,
        OrderSide Side,
        OrderType Type,
        decimal Quantity,
        decimal? LimitPrice,
        decimal? StopPrice,
        TimeInForce TimeInForce,
        string? StrategyId,
        OptionContractIdentity? OptionContract,
        IReadOnlyList<OrderLeg>? Legs,
        IReadOnlyDictionary<string, string>? Metadata,
        DateTimeOffset CreatedAt,
        bool RecoveredFromSnapshot = false)
    {
        public static SubmittedOrderContext FromRequest(int gatewayOrderId, string meridianOrderId, OrderRequest request)
            => new(
                gatewayOrderId,
                meridianOrderId,
                request.ClientOrderId,
                request.Symbol,
                request.Side,
                request.Type,
                request.Quantity,
                request.LimitPrice,
                request.StopPrice,
                request.TimeInForce,
                request.StrategyId,
                request.OptionContract,
                request.Legs,
                request.Metadata,
                DateTimeOffset.UtcNow);

        public OrderRequest ToModifiedRequest(OrderModification modification)
            => new()
            {
                Symbol = Symbol,
                Side = Side,
                Type = Type,
                Quantity = modification.NewQuantity ?? Quantity,
                LimitPrice = modification.NewLimitPrice ?? LimitPrice,
                StopPrice = modification.NewStopPrice ?? StopPrice,
                TimeInForce = TimeInForce,
                ClientOrderId = ClientOrderId,
                StrategyId = StrategyId,
                OptionContract = OptionContract,
                Legs = Legs,
                Metadata = Metadata
            };
    }

    private sealed record PendingOrderOperation(
        PendingOrderOperationKind Kind,
        TaskCompletionSource<ExecutionReport> Completion,
        SubmittedOrderContext? ProposedContext,
        OrderStatus PreviousStatus);

    private enum PendingOrderOperationKind
    {
        Submit,
        Modify,
        Cancel
    }

    private readonly record struct AccountPositionKey(string AccountId, string ContractIdentity);

    private sealed record AccountScopedBrokerPosition(
        string AccountId,
        string? Currency,
        BrokerPosition Position);

    private sealed class AccountSummaryCollector
    {
        private readonly ConcurrentDictionary<string, AccountSummaryState> _accounts =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly bool _paper;

        public AccountSummaryCollector(bool paper)
        {
            _paper = paper;
            Completion = new TaskCompletionSource<IReadOnlyList<AccountInfo>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public TaskCompletionSource<IReadOnlyList<AccountInfo>> Completion { get; }

        public void Apply(IBAccountSummaryUpdate update)
        {
            if (string.IsNullOrWhiteSpace(update.Account))
                return;

            var accountId = update.Account.Trim();
            _accounts
                .GetOrAdd(accountId, id => new AccountSummaryState(id, _paper))
                .Apply(update);
        }

        public void TryComplete()
        {
            Completion.TrySetResult(
                _accounts.Values
                    .Select(static account => account.ToAccountInfo())
                    .OrderBy(static account => account.AccountId, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    private sealed class AccountSummaryState
    {
        private readonly object _sync = new();
        private readonly string _accountId;
        private readonly bool _paper;
        private decimal? _equity;
        private decimal? _cash;
        private decimal? _buyingPower;
        private string? _currency;
        private DateTimeOffset _retrievedAt;

        public AccountSummaryState(string accountId, bool paper)
        {
            _accountId = accountId;
            _paper = paper;
        }

        public void Apply(IBAccountSummaryUpdate update)
        {
            lock (_sync)
            {
                if (update.ReceivedAt > _retrievedAt)
                    _retrievedAt = update.ReceivedAt;

                if (string.Equals(update.Tag, "Currency", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(update.Value))
                        _currency = update.Value.Trim();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(update.Currency))
                    _currency = update.Currency.Trim();

                if (!decimal.TryParse(
                        update.Value,
                        NumberStyles.Number | NumberStyles.AllowExponent,
                        CultureInfo.InvariantCulture,
                        out var numericValue))
                {
                    return;
                }

                if (string.Equals(update.Tag, "NetLiquidation", StringComparison.OrdinalIgnoreCase))
                    _equity = numericValue;
                else if (string.Equals(update.Tag, "TotalCashValue", StringComparison.OrdinalIgnoreCase))
                    _cash = numericValue;
                else if (string.Equals(update.Tag, "BuyingPower", StringComparison.OrdinalIgnoreCase))
                    _buyingPower = numericValue;
            }
        }

        public AccountInfo ToAccountInfo()
        {
            lock (_sync)
            {
                return new AccountInfo
                {
                    AccountId = _accountId,
                    Equity = _equity ?? 0m,
                    Cash = _cash ?? 0m,
                    BuyingPower = _buyingPower ?? 0m,
                    Currency = _currency ?? "USD",
                    Status = _paper ? "paper" : "active",
                    RetrievedAt = _retrievedAt == default ? DateTimeOffset.UtcNow : _retrievedAt
                };
            }
        }
    }

    private sealed class UnsupportedIBBrokerageClient : IIBBrokerageClient
    {
        public UnsupportedIBBrokerageClient(IBOptions options)
        {
            Host = options.Host;
            Port = options.Port;
            ClientId = options.ClientId;
            GuidanceMessage = IBBuildGuidance.BuildRealProviderMessage("IBBrokerageGateway")
                + " Brokerage order routing is guidance-only until the official vendor runtime is enabled.";
        }

        public string Host { get; }
        public int Port { get; }
        public int ClientId { get; }
        public bool IsConnected => false;
        public string GuidanceMessage { get; }

        // Required by IIBBrokerageClient contract so unsupported-runtime guidance mode remains substitutable.
#pragma warning disable CS0067 // Event is never used
        public event EventHandler<int>? NextValidIdReceived;
        public event EventHandler<IBOrderStatusUpdate>? OrderStatusReceived;
        public event EventHandler<IBOpenOrderUpdate>? OpenOrderReceived;
        public event EventHandler? OpenOrdersCompleted;
        public event EventHandler<IBExecutionUpdate>? ExecutionDetailsReceived;
        public event EventHandler<IBPositionUpdate>? PositionReceived;
        public event EventHandler? PositionsCompleted;
        public event EventHandler<IBAccountSummaryUpdate>? AccountSummaryReceived;
        public event EventHandler<int>? AccountSummaryCompleted;
        public event EventHandler<IBApiError>? ErrorOccurred;
        public event EventHandler<IReadOnlyList<string>>? ManagedAccountsReceived;
        public event EventHandler? ConnectionLost;
        public event EventHandler? ConnectionRestored;
#pragma warning restore CS0067

        public Task ConnectAsync(CancellationToken ct = default)
            => Task.FromException(CreateException());

        public Task DisconnectAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public void RequestNextValidId() => throw CreateException();

        public Task PlaceOrderAsync(int orderId, OrderRequest request, CancellationToken ct = default)
            => Task.FromException(CreateException());

        public Task CancelOrderAsync(int orderId, CancellationToken ct = default)
            => Task.FromException(CreateException());

        public int ReserveAccountSummaryRequestId() => throw CreateException();

        public void RequestAccountSummary(int requestId) => throw CreateException();

        public void CancelAccountSummary(int requestId) { }

        public void RequestPositions() => throw CreateException();

        public void CancelPositions() { }

        public void RequestOpenOrders() => throw CreateException();

        public void Dispose()
        {
        }

        private NotSupportedException CreateException() => new(GuidanceMessage);
    }

    private sealed class NullMarketEventPublisher : IMarketEventPublisher
    {
        public bool TryPublish(in MarketEvent evt) => true;
    }
}
