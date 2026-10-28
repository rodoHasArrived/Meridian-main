using System.Collections.Concurrent;
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

    // Executions withheld for a stale recovered id, numbered in arrival order under the session gate.
    // A withheld status changes the cached report the route handoff rechecks; an execution leaves
    // none, so recovery compares these numbers instead: one newer than its snapshot request is a
    // fill the snapshot may not describe.
    private readonly ConcurrentDictionary<int, long> _withheldExecutions = new();
    private long _withheldExecutionSequence;

    private void NoteWithheldExecution(int gatewayOrderId)
        => _withheldExecutions[gatewayOrderId] = Interlocked.Increment(ref _withheldExecutionSequence);

    // Guards the open-orders request/end pairing: openOrderEnd carries no request id, so ends still
    // owed to abandoned requests are counted and drained instead of completing a later request.
    private readonly object _openOrdersFence = new();
    private int _abandonedOpenOrdersRequests;

    /// <summary>Test seam: runs after a stale recovered route is detected, before the session gate.</summary>
    internal Action? StaleRouteCleanupStarting { get; set; }

    /// <summary>Test seam: runs inside the session gate immediately before recovered routes install.</summary>
    internal Action? RecoveryRoutesInstalling { get; set; }

    /// <summary>Test seam: runs after an order-status callback resolves its route, before it reports.</summary>
    internal Action? OrderStatusRouteResolved { get; set; }

    private static readonly IReadOnlySet<string> NoVerifiedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Accounts IB reported for this login via managedAccounts. Cleared when the transport is lost or
    // explicitly reconnected, never on an automatic restore: IB reports them during the reconnect
    // handshake, which can complete before ConnectionRestored is raised, and may not repeat them.
    private volatile IReadOnlySet<string> _verifiedAccounts = NoVerifiedAccounts;
    // A trusted host may explicitly pin legacy recovery to one broker account. Never choose the
    // first managed account: an orderRef is unique only inside its actual external account.
    private readonly string? _recoveryAccountId;

    /// <inheritdoc />
    public long ConnectionGeneration => Volatile.Read(ref _connectionEpoch);

    /// <inheritdoc />
    public bool IsExecutionStreamHealthy => IsConnected;

    /// <summary>
    /// The trading environment and the broker-reported accounts of this login. Null until IB has
    /// reported them for the current session, so retained orders are never bound to a guess.
    /// </summary>
    public string? ScopeIdentity
    {
        get
        {
            var accounts = _verifiedAccounts;
            return IsConnected && TryGetVerifiedEnvironment(accounts, out var environment)
                ? $"{environment}:{string.Join(',', accounts.Order(StringComparer.OrdinalIgnoreCase))}"
                : null;
        }
    }

    private bool TryGetVerifiedEnvironment(IReadOnlySet<string> accounts, out string environment)
    {
        environment = string.Empty;
        if (accounts.Count == 0)
            return false;
        foreach (var account in accounts)
        {
            // Bounded individual-account support: IB documents DU paper accounts; its introducing
            // broker documents U live accounts. Advisor, mixed and unknown account forms refuse.
            var prefix = account.StartsWith("DU", StringComparison.Ordinal) ? "DU"
                : account.StartsWith('U') ? "U" : string.Empty;
            if (prefix.Length == 0 || account.Length == prefix.Length
                || account.Skip(prefix.Length).Any(character => character is < '0' or > '9'))
                return false;
            var observed = prefix == "DU" ? "paper" : "live";
            if (environment.Length > 0 && environment != observed)
                return false;
            environment = observed;
        }
        return environment == (_options.UsePaperTrading ? "paper" : "live");
    }

    // The first report of a session (after a loss forgot the accounts) verifies the scope. A later
    // report naming a different set is another login's scope without a transport edge: routes
    // recovered under the earlier set must not stay current, so it ends the session too.
    private void OnManagedAccountsReceived(object? sender, IReadOnlyList<string> accounts)
    {
        var verified = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account))
            .Select(account => account.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_sessionGate)
        {
            var changed = _verifiedAccounts.Count > 0 && !_verifiedAccounts.SetEquals(verified);
            _verifiedAccounts = verified;
            if (changed)
            {
                Interlocked.Increment(ref _connectionEpoch);
                ForgetOwedOpenOrdersEnds();
            }
        }
    }

    // The client can drop and re-establish the transport on its own. The loss ends the session
    // that any in-flight recovery evidence belongs to and forgets the accounts. The client marks
    // the transport connected (and may re-report the accounts) before it raises ConnectionRestored,
    // so a recovery can complete in that gap: its session is the one the loss already began, and
    // the restored event must not end it again or that fresh route would be refused as stale. Only
    // a restoration not preceded by an observed loss ends a session of its own.
    private void OnConnectionLost(object? sender, EventArgs e) => EndSession(forgetAccounts: true, lost: true);

    private void OnConnectionRestored(object? sender, EventArgs e)
    {
        lock (_sessionGate)
        {
            if (_sessionEndedByLoss)
            {
                _sessionEndedByLoss = false;
                return;
            }
        }
        EndSession(forgetAccounts: false, lost: false);
    }

    private bool _sessionEndedByLoss;

    private void EndSession(bool forgetAccounts, bool lost)
    {
        lock (_sessionGate)
        {
            if (forgetAccounts)
                _verifiedAccounts = NoVerifiedAccounts;
            _sessionEndedByLoss = lost;
            Interlocked.Increment(ref _connectionEpoch);
            ForgetOwedOpenOrdersEnds();
        }
    }

    // A previous session's owed ends never arrive in the new one.
    private void ForgetOwedOpenOrdersEnds()
    {
        lock (_openOrdersFence)
            _abandonedOpenOrdersRequests = 0;
    }

    /// <inheritdoc />
    public Task<ExecutionReport?> GetOrderForRecoveryAsync(string clientOrderId, CancellationToken ct = default)
        => RecoverFromOpenOrdersAsync(clientOrderId, null, null, null, ct);

    /// <summary>Recovery with the retained order's identity but no generation guard.</summary>
    public Task<ExecutionReport?> GetOrderForRecoveryAsync(string clientOrderId, OrderState retainedOrder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(retainedOrder);
        return RecoverFromOpenOrdersAsync(clientOrderId, retainedOrder, null, null, ct);
    }

    /// <inheritdoc />
    public Task<ExecutionReport?> GetOrderForRecoveryAsync(
        string clientOrderId, OrderState retainedOrder, long expectedConnectionGeneration, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(retainedOrder);
        return RecoverFromOpenOrdersAsync(clientOrderId, retainedOrder, null, expectedConnectionGeneration, ct);
    }

    /// <inheritdoc />
    public Task<ExecutionReport?> GetAmendmentForRecoveryAsync(
        string clientOrderId, OrderState retainedOrder, PendingOrderAmendment amendment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(retainedOrder);
        ArgumentNullException.ThrowIfNull(amendment);
        return RecoverFromOpenOrdersAsync(clientOrderId, retainedOrder, amendment, null, ct);
    }

    /// <inheritdoc />
    public Task<ExecutionReport?> GetAmendmentForRecoveryAsync(
        string clientOrderId, OrderState retainedOrder, PendingOrderAmendment amendment, long expectedConnectionGeneration,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(retainedOrder);
        ArgumentNullException.ThrowIfNull(amendment);
        return RecoverFromOpenOrdersAsync(clientOrderId, retainedOrder, amendment, expectedConnectionGeneration, ct);
    }

    /// <summary>
    /// Recovers a retained order from IB's authoritative open-order snapshot, matched by the
    /// orderRef Meridian submits as the client order id. IB amends in place, so the working order
    /// keeps its id and its echoed terms select the amendment outcome. A completed order is absent
    /// from the snapshot; absence proves nothing, so it and any ambiguous match stay unresolved.
    /// </summary>
    private async Task<ExecutionReport?> RecoverFromOpenOrdersAsync(
        string clientOrderId, OrderState? retainedOrder, PendingOrderAmendment? amendment, long? expectedEpoch, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientOrderId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureConnected();
        var epoch = Volatile.Read(ref _connectionEpoch);
        // The caller bound its recovery scope under a generation. A later session is another
        // login's; refusing before the lookup means nothing of it is read, let alone installed.
        if (expectedEpoch is { } expected && expected != epoch)
            throw new InvalidOperationException(
                "The IB connection changed after the recovery scope was bound; no evidence from the current session was applied.");
        // The broker-verified accounts for this session. An orderRef is only unique within an
        // account, so a snapshot from another login or environment must never match.
        var verifiedAccounts = _verifiedAccounts;
        var retainedAccount = retainedOrder?.BrokerAccountId;
        if (retainedAccount is not null && _recoveryAccountId is not null
            && !string.Equals(retainedAccount, _recoveryAccountId, StringComparison.Ordinal))
            return RefuseAuthority("Retained broker account disagrees with the host-pinned recovery account.");
        var expectedAccount = retainedAccount ?? _recoveryAccountId;
        if (string.IsNullOrWhiteSpace(expectedAccount))
            return RefuseAuthority("IB recovery requires a retained broker acknowledgement account or an explicitly pinned account; legacy ownership is unknown.");
        if (!TryGetVerifiedEnvironment(verifiedAccounts, out _))
            return RefuseAuthority("IB recovery requires broker-reported U/DU individual accounts matching the configured live/paper environment; unknown, advisor and mixed environments are unresolved.");
        if (!verifiedAccounts.Contains(expectedAccount))
            return RefuseAuthority("The retained or pinned broker account is not verified in the current IB login.");
        // An execution withheld from here on may be a fill the snapshot this recovery reads omits.
        var executionsBefore = Interlocked.Read(ref _withheldExecutionSequence);

        var open = await GetOpenOrdersAsync(ct).ConfigureAwait(false);
        var matches = open.Where(order => string.Equals(order.ClientOrderId, clientOrderId, StringComparison.Ordinal)
            && int.TryParse(order.OrderId, out var id)
            && _openOrderAccounts.TryGetValue(id, out var account)
            && string.Equals(expectedAccount, account, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1 || !int.TryParse(matches[0].OrderId, out var gatewayOrderId))
            return Current(null);
        var order = matches[0];
        if (verifiedAccounts.Count == 0
            || !_openOrderAccounts.TryGetValue(gatewayOrderId, out var orderAccount)
            || !string.Equals(expectedAccount, orderAccount, StringComparison.Ordinal))
            return Current(null);
        if (amendment?.OriginalBrokerOrderId is { Length: > 0 } brokerId
            && !string.Equals(brokerId, order.OrderId, StringComparison.Ordinal))
            return Current(null);
        // An orderRef match alone is not the retained order: a coincidence of reference, quantity
        // and prices on a different instrument or side must not install a cancellation route for it.
        if (retainedOrder is not null
            && (!string.Equals(retainedOrder.Symbol, order.Symbol, StringComparison.OrdinalIgnoreCase)
                || retainedOrder.Side != order.Side || retainedOrder.Type != order.Type))
            return Current(null);
        // Rebuilding a derivative context from the snapshot would lose contract detail that a
        // later amendment or cancellation needs; leave those for manual reconciliation.
        // An unreported security type is not evidence of a stock either.
        if (!_openOrderSecurityTypes.TryGetValue(gatewayOrderId, out var securityType)
            || !string.Equals(securityType, "STK", StringComparison.OrdinalIgnoreCase))
            return Current(null);

        // Only a report from this session can describe this session's order (see CurrentSessionReport).
        var latest = CurrentSessionReport(gatewayOrderId, TrackedContext(gatewayOrderId));
        // The row was copied at openOrderEnd; a fill or terminal status arriving after it is cached
        // but not in the row. Combining the newer quantity with the row's status would recover a
        // completed order as working, so evidence that advanced past the row stays unresolved
        // until the next snapshot describes the order consistently.
        if (latest is not null && (latest.FilledQuantity != order.FilledQuantity
            || IsTerminalOrderStatus(latest.OrderStatus) != IsTerminalOrderStatus(order.Status)))
            return Current(null);
        var filled = Math.Max(order.FilledQuantity, latest?.FilledQuantity ?? 0m);
        var fillPrice = latest?.FillPrice;
        // A status callback can carry only the last fill price; relabelling it as the cumulative
        // average would misstate every booked fill, so it stays unresolved. The average must also
        // cover exactly the recovered quantity: an average over fewer shares than the snapshot
        // reports filled would price the remainder with it.
        if (filled < 0m || filled > order.Quantity
            || filled > 0m && (latest is null || fillPrice is not > 0m || !latest.FillPriceIsCumulativeAverage
                || latest.FilledQuantity != filled))
            return Current(null);

        var reportType = order.Status == OrderStatus.PartiallyFilled ? ExecutionReportType.PartialFill : ExecutionReportType.New;
        if (amendment is not null)
        {
            // The snapshot carries no trailing term, so no amendment that requested one (trail-only
            // or combined with other terms) can be confirmed or refused from it.
            if (amendment.ProposedTrail is not null)
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
            BrokerAccountId = orderAccount,
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
            // A withheld callback for this id caches its report under this gate, so one that arrived
            // after the consistency check shows here: the validated evidence is no longer current.
            if (!ReferenceEquals(CurrentSessionReport(gatewayOrderId, TrackedContext(gatewayOrderId)), latest)
                || _withheldExecutions.TryGetValue(gatewayOrderId, out var withheld) && withheld > executionsBefore)
                return null;
            // A route recovered in an earlier session is refreshed; a context this process submitted
            // is never replaced by snapshot terms.
            var recovered = context with { RecoveryEpoch = epoch, BrokerAccountId = orderAccount };
            _staleRecoveredIds.TryRemove(gatewayOrderId, out _);
            var installed = _submittedOrders.AddOrUpdate(gatewayOrderId, recovered, (_, existing) =>
            {
                if (!existing.RecoveredFromSnapshot)
                    return existing;
                // The id now names a different client order: the replaced route's alias must not
                // resolve to it, or cancelling the old order would cancel the new one.
                if (!string.Equals(existing.MeridianOrderId, recovered.MeridianOrderId, StringComparison.Ordinal))
                    ((ICollection<KeyValuePair<string, int>>)_gatewayOrderIdsByExternalId)
                        .Remove(new KeyValuePair<string, int>(existing.MeridianOrderId, gatewayOrderId));
                return recovered;
            });
            if (ReferenceEquals(installed, recovered))
            {
                _gatewayOrderIdsByExternalId[clientOrderId] = gatewayOrderId;
                _gatewayOrderIdsByExternalId[gatewayOrderId.ToString()] = gatewayOrderId;
            }
        }
        return report;

        ExecutionReport? RefuseAuthority(string reason)
        {
            _logger.LogWarning("IB restart recovery refused: {RecoveryRefusal}", reason);
            return Current(null);
        }

        ExecutionReport? Current(ExecutionReport? result) => Volatile.Read(ref _connectionEpoch) == epoch
            ? result
            : throw new InvalidOperationException(
                "The IB connection changed during order recovery; evidence from the previous session was discarded.");
    }

    private static bool MatchesTerms(BrokerOrder order, OrderState terms)
        => order.Quantity == terms.Quantity && order.LimitPrice == terms.LimitPrice && order.StopPrice == terms.StopPrice;
}
