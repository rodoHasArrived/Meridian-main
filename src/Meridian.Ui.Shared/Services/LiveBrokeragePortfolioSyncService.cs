using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>Account-scoped execution evidence; never substitutes for the accounting import projection.</summary>
public sealed record LiveBrokeragePortfolioStatus(
    Guid FundAccountId,
    string ProviderId,
    string? ExternalAccountId,
    bool IsConnected,
    bool IsFresh,
    bool IsComplete,
    bool IsConsistent,
    bool IsReconciled,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessfulSyncAt,
    BrokeragePortfolioSnapshotDto? Snapshot,
    BrokerageExecutionReconciliationReport? Reconciliation,
    IReadOnlyList<string> BlockingReasons)
{
    public bool IsReady => IsConnected && IsFresh && IsComplete && IsConsistent && IsReconciled
        && BlockingReasons.Count == 0;
}

/// <summary>
/// Publishes a live account only after complete portfolio evidence and OMS reconciliation agree.
/// Restart, disconnect, stale evidence, and subsequent order/fill changes require a fresh sync.
/// No cached snapshot is restored as trading authority after a restart.
/// </summary>
public sealed class LiveBrokeragePortfolioSyncService
{
    private readonly Func<IBrokerageGateway?> _gatewayAccessor;
    private readonly Func<IOrderManager?> _orderManagerAccessor;
    private readonly BrokerageExecutionReconciliationService _reconciliation;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maxAge;
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly ConcurrentDictionary<Guid, AccountBinding> _bindings = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    public LiveBrokeragePortfolioSyncService(
        Func<IBrokerageGateway?> gatewayAccessor,
        Func<IOrderManager?> orderManagerAccessor,
        BrokerageExecutionReconciliationService reconciliation,
        TimeProvider? timeProvider = null,
        TimeSpan? maxAge = null)
    {
        _gatewayAccessor = gatewayAccessor;
        _orderManagerAccessor = orderManagerAccessor;
        _reconciliation = reconciliation;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxAge = maxAge ?? TimeSpan.FromSeconds(30);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_maxAge, TimeSpan.Zero);
    }

    public bool IsActive => _gatewayAccessor() is not null;

    public TimeSpan MaximumAge => _maxAge;

    public void InvalidateAccountBinding(Guid fundAccountId, string providerId, string externalAccountId)
    {
        _bindings.AddOrUpdate(fundAccountId,
            _ => new(providerId, externalAccountId, 1L),
            (_, old) => new(providerId, externalAccountId, old.Generation + 1L));
        if (_entries.TryGetValue(fundAccountId, out var entry))
            _entries[fundAccountId] = entry with
            {
                Status = entry.Status with
                {
                    IsComplete = false,
                    IsConsistent = false,
                    IsReconciled = false,
                    BlockingReasons = ["Account binding changed; synchronize the linked broker account again."]
                }
            };
    }

    public IReadOnlyList<LiveBrokeragePortfolioStatus> GetStatuses() =>
        _entries.Keys.Select(GetStatus).ToArray();

    public BrokeragePortfolioSnapshotDto? GetRiskSnapshot(Guid fundAccountId) =>
        _entries.TryGetValue(fundAccountId, out var entry) ? entry.RiskSnapshot ?? entry.Status.Snapshot : null;

    /// <summary>
    /// Keeps the last reconciled unfilled reservation until holdings are refreshed. Retiring it
    /// from a new fill while retaining old broker holdings would briefly lose that fill's exposure.
    /// </summary>
    public IReadOnlyList<OrderState> GetExposureReservingOrders(Guid fundAccountId)
    {
        var oms = _orderManagerAccessor();
        if (oms is null)
            return [];
        var current = ReadOrders(oms).Where(o => o.FundAccountId == fundAccountId)
            .GroupBy(o => o.OrderId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var live = oms.GetExposureReservingOrders().Where(o => o.FundAccountId == fundAccountId).ToArray();
        if (!_entries.TryGetValue(fundAccountId, out var entry))
            return live;
        var captured = (entry.CapturedOrders ?? []).GroupBy(o => o.OrderId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<OrderState>();
        foreach (var order in entry.CapturedExposureOrders ?? [])
        {
            reserved.Add(order.OrderId);
            current.TryGetValue(order.OrderId, out var now);
            result.Add(order with
            {
                Quantity = Math.Max(order.Quantity, now?.Quantity ?? order.Quantity),
                FilledQuantity = Math.Min(order.FilledQuantity, now?.FilledQuantity ?? order.FilledQuantity),
                LimitPrice = Math.Max(order.LimitPrice ?? 0m, now?.LimitPrice ?? 0m) is > 0m and var limit ? limit : null
            });
        }
        foreach (var order in live.Where(o => !reserved.Contains(o.OrderId)))
        {
            reserved.Add(order.OrderId);
            result.Add(order with { FilledQuantity = 0m });
        }
        foreach (var order in current.Values.Where(o => !reserved.Contains(o.OrderId)))
        {
            var priorFilled = captured.GetValueOrDefault(order.OrderId)?.FilledQuantity ?? 0m;
            var newlyFilled = order.FilledQuantity - priorFilled;
            if (newlyFilled > 0m)
                result.Add(order with { Quantity = newlyFilled, FilledQuantity = 0m, LimitPrice = order.AverageFillPrice ?? order.LimitPrice });
        }
        return result;
    }

    public LiveBrokeragePortfolioStatus GetStatus(Guid fundAccountId)
    {
        var gateway = _gatewayAccessor();
        if (!_entries.TryGetValue(fundAccountId, out var entry))
        {
            return new(fundAccountId, gateway?.GatewayId ?? "alpaca", null,
                gateway?.IsConnected == true, false, false, false, false,
                null, null, null, null, ["Synchronize and reconcile this account before placing orders."]);
        }

        var status = entry.Status;
        var reasons = status.BlockingReasons.ToList();
        var connected = gateway?.IsConnected == true && ReferenceEquals(entry.Gateway, gateway)
            && (gateway is not IBrokerageConnectionState connection ||
                connection.IsExecutionStreamHealthy && connection.ConnectionGeneration == entry.ConnectionGeneration);
        var fresh = status.Snapshot is { } snapshot && IsFresh(snapshot);
        var unchanged = _orderManagerAccessor() is { } oms
            && string.Equals(entry.OrderFingerprint, Fingerprint(oms, fundAccountId), StringComparison.Ordinal);
        var bindingUnchanged = (_bindings.GetValueOrDefault(fundAccountId)?.Generation ?? 0L) == entry.BindingGeneration;
        if (!connected)
            reasons.Add("Broker connection is unavailable or changed; synchronize after reconnecting.");
        if (!fresh)
            reasons.Add("Portfolio evidence is missing, stale, or has an invalid timestamp.");
        if (!unchanged)
            reasons.Add("Retained order or fill state changed after synchronization; reconcile again.");
        if (!bindingUnchanged)
            reasons.Add("Account binding changed after synchronization; reconcile the current link.");
        if (_orderManagerAccessor() is OrderManagementSystem concrete && concrete.GetRecoveryOrders(fundAccountId).Count > 0)
            reasons.Add("Retained orders still require authoritative broker recovery.");

        return status with
        {
            IsConnected = connected,
            IsFresh = fresh,
            IsReconciled = status.IsReconciled && unchanged && bindingUnchanged,
            BlockingReasons = reasons.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    public async Task<LiveBrokeragePortfolioStatus> SynchronizeAsync(
        Guid fundAccountId,
        string externalAccountId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);
        if (fundAccountId == Guid.Empty)
            throw new ArgumentException("An explicit account scope is required.", nameof(fundAccountId));
        await _syncGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var gateway = _gatewayAccessor();
            var oms = _orderManagerAccessor();
            var previous = GetStatus(fundAccountId);
            var binding = _bindings.GetValueOrDefault(fundAccountId);
            var bindingGeneration = binding?.Generation ?? 0L;
            _entries.TryGetValue(fundAccountId, out var previousEntry);
            var pending = previous with
            {
                ExternalAccountId = externalAccountId,
                LastAttemptAt = _timeProvider.GetUtcNow(),
                IsComplete = false,
                IsReconciled = false,
                BlockingReasons = ["Portfolio synchronization is in progress."]
            };
            var pendingEntry = previousEntry is null
                ? new Entry(pending, gateway, string.Empty)
                : previousEntry with { Status = pending };
            _entries[fundAccountId] = pendingEntry;
            try
            {
                if (!string.Equals(gateway?.GatewayId, "alpaca", StringComparison.OrdinalIgnoreCase)
                    || gateway is not IBrokeragePortfolioSync portfolioSync)
                    throw new InvalidOperationException("The selected gateway must support Alpaca portfolio synchronization.");
                if (binding is not null && (!string.Equals(binding.ProviderId, gateway.GatewayId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(binding.ExternalAccountId, externalAccountId, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The requested account no longer matches the retained account binding.");
                if (!gateway.IsConnected)
                    await gateway.ConnectAsync(ct).ConfigureAwait(false);
                if (!gateway.IsConnected)
                    throw new InvalidOperationException("Alpaca did not establish a broker connection.");
                var generation = (gateway as IBrokerageConnectionState)?.ConnectionGeneration ?? 0L;
                if (gateway is IBrokerageConnectionState bound &&
                    (bound.ScopeIdentity is not { } scope || !scope.EndsWith($":{externalAccountId}", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The connected brokerage scope does not match the linked account.");
                if (oms is null)
                    throw new InvalidOperationException("The retained order manager is unavailable.");
                if (_entries.Any(pair => pair.Key != fundAccountId && pair.Value.Status.LastSuccessfulSyncAt.HasValue &&
                    string.Equals(pair.Value.Status.Snapshot?.Account.AccountId, externalAccountId, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("This broker account is already bound to another local account.");

                var beforeOrders = Fingerprint(oms, fundAccountId);
                var before = await portfolioSync.GetPortfolioSnapshotAsync(externalAccountId, ct).ConfigureAwait(false);
                if (!string.Equals(before.Account.AccountId, externalAccountId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(before.Account.ProviderId, gateway.GatewayId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Broker evidence does not prove the linked account identity.");
                if (oms is OrderManagementSystem recovering)
                {
                    await _reconciliation.RecoverOrdersAsync(gateway, recovering, fundAccountId, ct).ConfigureAwait(false);
                    beforeOrders = Fingerprint(oms, fundAccountId);
                    before = await portfolioSync.GetPortfolioSnapshotAsync(externalAccountId, ct).ConfigureAwait(false);
                }
                var initialReport = await _reconciliation.ReconcileOpenOrdersAsync(gateway, oms, fundAccountId, ct).ConfigureAwait(false);
                var snapshot = await portfolioSync.GetPortfolioSnapshotAsync(externalAccountId, ct).ConfigureAwait(false);
                // Holdings and cash do not reflect an unfilled order that appears while the
                // portfolio is being read. Bracket that read with the same reconciliation
                // service and require the entire observed broker order book to remain stable.
                var report = await _reconciliation.ReconcileOpenOrdersAsync(gateway, oms, fundAccountId, ct).ConfigureAwait(false);
                var brokerOrdersUnchanged = initialReport.BrokerOrderSnapshotFingerprint is { } initialBrokerOrders
                    && string.Equals(initialBrokerOrders, report.BrokerOrderSnapshotFingerprint, StringComparison.Ordinal);
                var capturedOrders = ReadOrders(oms).Where(o => o.FundAccountId == fundAccountId).ToArray();
                var capturedExposure = oms.GetExposureReservingOrders().Where(o => o.FundAccountId == fundAccountId).ToArray();
                var afterOrders = Fingerprint(oms, fundAccountId);
                var issues = Validate(snapshot, externalAccountId);
                var beforeIssues = Validate(before, externalAccountId);
                if (beforeIssues.Count > 0)
                    issues.Add("The initial portfolio observation was incomplete or inconsistent.");
                if (!string.Equals(EconomicFingerprint(before), EconomicFingerprint(snapshot), StringComparison.Ordinal))
                    issues.Add("Broker holdings or cash changed during synchronization; retry reconciliation.");
                if (!string.Equals(beforeOrders, afterOrders, StringComparison.Ordinal))
                    issues.Add("Retained orders or fills changed during synchronization; retry reconciliation.");
                if (oms is OrderManagementSystem concrete && concrete.GetRecoveryOrders(fundAccountId).Count > 0)
                    issues.Add("Retained orders still require authoritative broker recovery.");
                if (!initialReport.IsClean || !report.IsClean)
                    issues.Add("Broker order reconciliation has unresolved discrepancies or unhealthy connectivity.");
                if (!brokerOrdersUnchanged)
                    issues.Add("Broker open orders changed during portfolio synchronization; retry reconciliation.");
                if ((_bindings.GetValueOrDefault(fundAccountId)?.Generation ?? 0L) != bindingGeneration)
                    issues.Add("Account binding changed during synchronization; reconcile the current link.");
                if (!ReferenceEquals(gateway, _gatewayAccessor()) || !gateway.IsConnected
                    || gateway is IBrokerageConnectionState connection &&
                        (!connection.IsExecutionStreamHealthy || connection.ConnectionGeneration != generation))
                    issues.Add("Broker disconnected or changed during synchronization.");

                var consistent = issues.Count == 0;
                var status = new LiveBrokeragePortfolioStatus(
                    fundAccountId, gateway.GatewayId, externalAccountId, gateway.IsConnected,
                    IsFresh(snapshot), snapshot.IsComplete, consistent, initialReport.IsClean && report.IsClean && brokerOrdersUnchanged,
                    pending.LastAttemptAt, consistent ? _timeProvider.GetUtcNow() : previous.LastSuccessfulSyncAt,
                    snapshot, report, issues.ToArray());
                _entries[fundAccountId] = consistent
                    ? new(status, gateway, afterOrders, generation,
                        capturedOrders, capturedExposure, snapshot, bindingGeneration)
                    : pendingEntry with { Status = status };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _entries[fundAccountId] = pendingEntry with { Status = pending with { BlockingReasons = ["Portfolio synchronization was interrupted; reconcile again."] } };
                throw;
            }
            catch (Exception)
            {
                // Retain the last observation for display, but revoke its authority. Provider
                // exceptions can contain account credentials or transport details: do not expose them.
                _entries[fundAccountId] = pendingEntry with
                {
                    Status = pending with
                    {
                        BlockingReasons = ["Portfolio synchronization failed. Verify the account binding and broker connection, then reconcile again."]
                    }
                };
            }

            return GetStatus(fundAccountId);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private List<string> Validate(BrokeragePortfolioSnapshotDto snapshot, string externalAccountId)
    {
        var issues = new List<string>();
        if (!snapshot.IsComplete)
            issues.AddRange(snapshot.CompletenessIssues is { Count: > 0 } details ? details : ["Provider did not certify complete portfolio evidence."]);
        if (!string.Equals(snapshot.Account.ProviderId, "alpaca", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(snapshot.Account.AccountId, externalAccountId, StringComparison.OrdinalIgnoreCase)
            || snapshot.AccountSnapshot is not { } account
            || !string.Equals(account.AccountId, externalAccountId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(account.ProviderId, "alpaca", StringComparison.OrdinalIgnoreCase))
            issues.Add("Broker portfolio identity does not match the linked account.");
        if (!IsFresh(snapshot))
            issues.Add("Broker portfolio timestamp is stale or invalid.");
        if (snapshot.AccountSnapshot is not { TradingBlocked: false, AccountBlocked: false } accountState
            || !string.Equals(accountState.Status, "active", StringComparison.OrdinalIgnoreCase))
            issues.Add("The brokerage account is not active for trading.");
        if (snapshot.Balance.Currency != "USD" || snapshot.Account.Currency != "USD"
            || snapshot.AccountSnapshot?.Currency != "USD" || snapshot.Positions.Any(p => p.Currency != "USD"))
            issues.Add("Complete USD currency evidence is required for the supported Alpaca account lane.");
        if (snapshot.AccountSnapshot is { } balances &&
            (balances.Cash != snapshot.Balance.Cash || balances.Equity != snapshot.Balance.Equity
                || balances.BuyingPower != snapshot.Balance.BuyingPower))
            issues.Add("Account and balance evidence disagree.");
        if (snapshot.Positions.GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
            issues.Add("Broker positions contain duplicate symbols.");
        foreach (var position in snapshot.Positions)
        {
            var multiplier = position.AssetClass == "us_option" ? 100m : 1m;
            if (string.IsNullOrWhiteSpace(position.Symbol) || position.Quantity == 0m || position.MarketPrice <= 0m
                || position.AssetClass is not ("us_equity" or "us_option")
                || Math.Abs(position.MarketValue - position.Quantity * position.MarketPrice * multiplier) > 0.01m)
                issues.Add("A broker holding has unsupported or inconsistent quantity, valuation, or asset evidence.");
        }
        if (Math.Abs(snapshot.Balance.Cash + snapshot.Positions.Sum(p => p.MarketValue) - snapshot.Balance.Equity) > 0.01m)
            issues.Add("Broker cash plus holdings does not reconcile to account equity.");
        return issues;
    }

    private bool IsFresh(BrokeragePortfolioSnapshotDto snapshot)
    {
        var now = _timeProvider.GetUtcNow();
        bool Current(DateTimeOffset timestamp) => timestamp >= now - _maxAge && timestamp <= now + TimeSpan.FromSeconds(5);
        return Current(snapshot.RetrievedAt) && Current(snapshot.Account.RetrievedAt)
            && snapshot.AccountSnapshot is { } account && Current(account.AsOf);
    }

    private static string EconomicFingerprint(BrokeragePortfolioSnapshotDto snapshot) => JsonSerializer.Serialize(new
    {
        snapshot.Account.AccountId,
        snapshot.Balance.Cash,
        Positions = snapshot.Positions.OrderBy(p => p.Symbol, StringComparer.Ordinal).Select(p => new { p.Symbol, p.Quantity })
    });

    private static string Fingerprint(IOrderManager oms, Guid fundAccountId)
    {
        var retained = ReadOrders(oms).Where(o => o.FundAccountId == fundAccountId || o.FundAccountId is null);
        // Serialize immutable retained states, including prices, fill quantity, and status; a
        // late fill is authority-changing even when no open order remains.
        return JsonSerializer.Serialize(new
        {
            Orders = retained.OrderBy(o => o.OrderId, StringComparer.Ordinal),
            Reservations = oms.GetExposureReservingOrders()
                .Where(o => o.FundAccountId == fundAccountId || o.FundAccountId is null)
                .OrderBy(o => o.OrderId, StringComparer.Ordinal).ThenBy(o => o.FilledQuantity)
        });
    }

    private static IReadOnlyList<OrderState> ReadOrders(IOrderManager oms) => oms is OrderManagementSystem concrete
        ? concrete.GetAllRetainedOrderStates()
        : oms.GetOpenOrders().Concat(oms.GetCompletedOrders(int.MaxValue))
            .GroupBy(o => o.OrderId, StringComparer.Ordinal).Select(g => g.First()).ToArray();

    private sealed record Entry(LiveBrokeragePortfolioStatus Status, IBrokerageGateway? Gateway, string OrderFingerprint,
        long ConnectionGeneration = 0L, IReadOnlyList<OrderState>? CapturedOrders = null,
        IReadOnlyList<OrderState>? CapturedExposureOrders = null, BrokeragePortfolioSnapshotDto? RiskSnapshot = null,
        long BindingGeneration = 0L);

    private sealed record AccountBinding(string ProviderId, string ExternalAccountId, long Generation);
}
