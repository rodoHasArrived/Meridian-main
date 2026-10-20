using Meridian.Execution.Sdk;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

public sealed partial class IBBrokerageGateway
{
    // Incremented on every connect, disconnect, connection loss and automatic reconnect; recovery
    // evidence read across a change is from another session and is refused rather than applied to
    // the retained order.
    private int _connectionEpoch;

    // Guards every epoch change and the install of recovered routes, so a route is installed only
    // for evidence from the session that is still current and never needs rolling back.
    private readonly object _sessionGate = new();

    /// <summary>Test seam: runs inside the session gate immediately before recovered routes install.</summary>
    internal Action? RecoveryRoutesInstalling { get; set; }

    private static readonly IReadOnlySet<string> NoVerifiedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Accounts IB reported for this login via managedAccounts. Cleared when the transport is lost or
    // explicitly reconnected, never on an automatic restore: IB reports them during the reconnect
    // handshake, which can complete before ConnectionRestored is raised, and may not repeat them.
    private volatile IReadOnlySet<string> _verifiedAccounts = NoVerifiedAccounts;

    /// <inheritdoc />
    public long ConnectionGeneration => Volatile.Read(ref _connectionEpoch);

    /// <inheritdoc />
    public bool IsExecutionStreamHealthy => IsConnected;

    /// <summary>
    /// The trading environment and the broker-reported accounts of this login. Null until IB has
    /// reported them for the current session, so retained orders are never bound to a guess.
    /// </summary>
    public string? ScopeIdentity => IsConnected && _verifiedAccounts is { Count: > 0 } accounts
        ? $"{(ExecutionMode == Meridian.Execution.Sdk.ExecutionMode.Live ? "live" : "paper")}:{string.Join(',', accounts.Order(StringComparer.OrdinalIgnoreCase))}"
        : null;

    private void OnManagedAccountsReceived(object? sender, IReadOnlyList<string> accounts)
        => _verifiedAccounts = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account))
            .Select(account => account.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // The client can drop and re-establish the transport on its own. Either edge ends the session
    // that any in-flight recovery evidence belongs to; only the loss forgets the accounts.
    private void OnConnectionLost(object? sender, EventArgs e) => EndSession(forgetAccounts: true);

    private void OnConnectionRestored(object? sender, EventArgs e) => EndSession(forgetAccounts: false);

    private void EndSession(bool forgetAccounts)
    {
        lock (_sessionGate)
        {
            if (forgetAccounts)
                _verifiedAccounts = NoVerifiedAccounts;
            Interlocked.Increment(ref _connectionEpoch);
        }
    }

    /// <inheritdoc />
    public Task<ExecutionReport?> GetOrderForRecoveryAsync(string clientOrderId, CancellationToken ct = default)
        => RecoverFromOpenOrdersAsync(clientOrderId, null, ct);

    /// <inheritdoc />
    public Task<ExecutionReport?> GetAmendmentForRecoveryAsync(
        string clientOrderId, OrderState retainedOrder, PendingOrderAmendment amendment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(retainedOrder);
        ArgumentNullException.ThrowIfNull(amendment);
        return RecoverFromOpenOrdersAsync(clientOrderId, amendment, ct);
    }

    /// <summary>
    /// Recovers a retained order from IB's authoritative open-order snapshot, matched by the
    /// orderRef Meridian submits as the client order id. IB amends in place, so the working order
    /// keeps its id and its echoed terms select the amendment outcome. A completed order is absent
    /// from the snapshot; absence proves nothing, so it and any ambiguous match stay unresolved.
    /// </summary>
    private async Task<ExecutionReport?> RecoverFromOpenOrdersAsync(
        string clientOrderId, PendingOrderAmendment? amendment, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientOrderId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();
        var epoch = Volatile.Read(ref _connectionEpoch);
        // The broker-verified accounts for this session. An orderRef is only unique within an
        // account, so a snapshot from another login or environment must never match.
        var verifiedAccounts = _verifiedAccounts;

        var open = await GetOpenOrdersAsync(ct).ConfigureAwait(false);
        var matches = open.Where(order => string.Equals(order.ClientOrderId, clientOrderId, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1 || !int.TryParse(matches[0].OrderId, out var gatewayOrderId))
            return Current(null);
        var order = matches[0];
        if (verifiedAccounts.Count == 0
            || !_openOrderAccounts.TryGetValue(gatewayOrderId, out var orderAccount)
            || !verifiedAccounts.Contains(orderAccount))
            return Current(null);
        if (amendment?.OriginalBrokerOrderId is { Length: > 0 } brokerId
            && !string.Equals(brokerId, order.OrderId, StringComparison.Ordinal))
            return Current(null);
        // Rebuilding a derivative context from the snapshot would lose contract detail that a
        // later amendment or cancellation needs; leave those for manual reconciliation.
        if (!_openOrderSecurityTypes.TryGetValue(gatewayOrderId, out var securityType)
            || securityType.Length > 0 && !string.Equals(securityType, "STK", StringComparison.OrdinalIgnoreCase))
            return Current(null);

        _latestOrderReports.TryGetValue(gatewayOrderId, out var latest);
        var filled = Math.Max(order.FilledQuantity, latest?.FilledQuantity ?? 0m);
        var fillPrice = latest?.FillPrice;
        // A status callback can carry only the last fill price; relabelling it as the cumulative
        // average would misstate every booked fill, so it stays unresolved.
        if (filled < 0m || filled > order.Quantity
            || filled > 0m && (fillPrice is not > 0m || latest?.FillPriceIsCumulativeAverage != true))
            return Current(null);

        var reportType = order.Status == OrderStatus.PartiallyFilled ? ExecutionReportType.PartialFill : ExecutionReportType.New;
        if (amendment is not null)
        {
            // The snapshot carries no trailing term, so a trail-only amendment echoes the same
            // quantity and prices either way and cannot be told accepted from refused.
            if (amendment.ProposedTrail is not null && MatchesTerms(order, amendment.Original) && MatchesTerms(order, amendment.Proposed))
                return Current(null);
            if (MatchesTerms(order, amendment.Proposed))
                reportType = ExecutionReportType.Modified;
            else if (MatchesTerms(order, amendment.Original))
                reportType = ExecutionReportType.ModificationRejected;
            else
                return Current(null);
        }

        // Restore enough context to route cancellation. Time-in-force is not in the snapshot, so
        // the context is marked recovered and amendment is refused rather than guessed.
        var context = new SubmittedOrderContext(gatewayOrderId, clientOrderId, clientOrderId, order.Symbol, order.Side,
            order.Type, order.Quantity, order.LimitPrice, order.StopPrice, TimeInForce.Day, null, null, null, null,
            order.CreatedAt, RecoveredFromSnapshot: true);
        var report = BuildExecutionReport(gatewayOrderId, reportType, order.Status, context, order.Symbol, order.Side,
            order.Quantity, filled, fillPrice, order.Status.ToString(), null, null, DateTimeOffset.UtcNow,
            fillPriceIsCumulativeAverage: true) with
        {
            OrderId = clientOrderId,
            HasOrderTerms = true,
            OrderLimitPrice = order.LimitPrice,
            OrderStopPrice = order.StopPrice
        };

        // Routes are installed only for evidence from this session. The epoch check and the install
        // share the session gate with every epoch change, so the session cannot change in between:
        // a stale snapshot never installs, and nothing installed is ever rolled back, which would
        // otherwise race a recovery from the new session for the same order.
        lock (_sessionGate)
        {
            RecoveryRoutesInstalling?.Invoke();
            Current(report);
            _submittedOrders.TryAdd(gatewayOrderId, context);
            _gatewayOrderIdsByExternalId[clientOrderId] = gatewayOrderId;
            _gatewayOrderIdsByExternalId[gatewayOrderId.ToString()] = gatewayOrderId;
        }
        return report;

        ExecutionReport? Current(ExecutionReport? result) => Volatile.Read(ref _connectionEpoch) == epoch
            ? result
            : throw new InvalidOperationException(
                "The IB connection changed during order recovery; evidence from the previous session was discarded.");
    }

    private static bool MatchesTerms(BrokerOrder order, OrderState terms)
        => order.Quantity == terms.Quantity && order.LimitPrice == terms.LimitPrice && order.StopPrice == terms.StopPrice;
}
