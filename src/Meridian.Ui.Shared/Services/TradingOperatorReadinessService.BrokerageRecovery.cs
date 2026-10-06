using Meridian.Contracts.Workstation;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class TradingOperatorReadinessService
{
    private async Task<TradingBrokerageRecoveryDto> BuildBrokerageRecoveryAsync(
        Guid? fundAccountId,
        StrategyRunReadScope? scope,
        WorkstationBrokerageSyncStatusDto? link,
        TradingExecutionReconciliationReadinessDto? reconciliation,
        CancellationToken ct)
    {
        var service = Resolve<LiveBrokeragePortfolioSyncService>();
        var state = fundAccountId.HasValue ? service?.GetStatus(fundAccountId.Value) : null;
        var blockers = state?.BlockingReasons.ToList() ?? [];
        if (!fundAccountId.HasValue)
            blockers.Add("Select an account to inspect broker synchronization and recovery evidence.");
        if (service?.IsActive != true)
            blockers.Add("An active Alpaca execution gateway is required for broker portfolio recovery.");
        if (state is null)
            blockers.Add("No broker portfolio synchronization is available for this account.");
        if (link?.IsLinked != true || !string.Equals(link.ProviderId, "alpaca", StringComparison.OrdinalIgnoreCase))
            blockers.Add("The selected account needs a retained Alpaca account link.");
        else if (state?.ExternalAccountId is { } externalAccountId
            && !string.Equals(externalAccountId, link.ExternalAccountId, StringComparison.OrdinalIgnoreCase))
            blockers.Add("The retained account link changed after synchronization; reconcile the new account.");
        if (reconciliation?.Status != TradingAcceptanceGateStatusDto.Ready)
            blockers.Add(reconciliation?.Detail ?? "Broker order reconciliation evidence is unavailable.");

        var ready = state?.IsReady == true && blockers.Count == 0;
        var reasons = blockers.Distinct(StringComparer.Ordinal).ToArray();
        var snapshot = state?.Snapshot;
        var expiresAt = snapshot?.AccountSnapshot is { } accountSnapshot
            ? new[] { snapshot.RetrievedAt, snapshot.Account.RetrievedAt, accountSnapshot.AsOf }.Min()
                + (service?.MaximumAge ?? TimeSpan.Zero)
            : (DateTimeOffset?)null;
        var portfolio = state is null ? null : new TradingBrokerageRecoveryPortfolioDto(
            snapshot?.Balance.Cash,
            snapshot?.Balance.BuyingPower,
            snapshot?.Balance.Equity,
            snapshot?.Balance.Currency,
            snapshot?.Positions.Count ?? 0,
            snapshot?.RetrievedAt,
            state.LastAttemptAt,
            state.LastSuccessfulSyncAt,
            state.IsComplete,
            state.IsFresh,
            state.IsConsistent && state.IsReconciled,
            reasons,
            expiresAt);

        return new TradingBrokerageRecoveryDto(
            fundAccountId,
            state?.ProviderId ?? link?.ProviderId,
            state?.ExternalAccountId ?? link?.ExternalAccountId,
            ready ? "Ready" : "Blocked",
            ready
                ? "Account holdings, cash and buying power are current and broker orders reconcile with retained local orders."
                : string.Join(" ", reasons),
            portfolio,
            reasons,
            await ResolveAffectedBrokerageRunsAsync(fundAccountId, scope, ready, ct).ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<TradingBrokerageAffectedRunDto>> ResolveAffectedBrokerageRunsAsync(
        Guid? fundAccountId,
        StrategyRunReadScope? scope,
        bool ready,
        CancellationToken ct)
    {
        var runs = Resolve<StrategyRunReadService>();
        if (runs is null || !fundAccountId.HasValue)
            return [];

        var visible = scope is null
            ? await runs.GetRunsAsync(ct: ct).ConfigureAwait(false)
            : await runs.GetRunsAsync(strategyId: null, runType: null, scope, ct).ConfigureAwait(false);
        var retainedRunIds = Resolve<IOrderManager>() is OrderManagementSystem oms
            ? oms.GetRetainedBrokerageOrders(fundAccountId)
                .Where(order => !string.IsNullOrWhiteSpace(order.RunId))
                .Select(order => order.RunId!).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var affected = new List<TradingBrokerageAffectedRunDto>();
        foreach (var run in visible.Where(run => run.Mode is StrategyRunMode.Live or StrategyRunMode.Paper))
        {
            var detail = scope is null
                ? await runs.GetRunDetailAsync(run.RunId, ct).ConfigureAwait(false)
                : await runs.GetRunDetailAsync(run.RunId, scope, ct).ConfigureAwait(false);
            if (detail is null)
                continue;

            var accountId = detail.Portfolio?.AccountScopeId;
            foreach (var key in new[] { "accountScopeId", "accountId", "fundAccountId" })
            {
                if (string.IsNullOrWhiteSpace(accountId) && detail.Parameters.TryGetValue(key, out var parameter))
                    accountId = parameter;
            }
            if ((!Guid.TryParse(accountId, out var account) || account != fundAccountId.Value)
                && !retainedRunIds.Contains(run.RunId))
                continue;

            affected.Add(new TradingBrokerageAffectedRunDto(run.RunId, run.StrategyId, run.Status.ToString(),
                ready ? "Account recovery evidence is current; other strategy readiness controls still apply."
                    : "New broker orders remain blocked until this account is synchronized and reconciled."));
        }
        return affected;
    }
}
