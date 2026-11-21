using Meridian.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

public sealed partial class StrategyRunReadService
{
    /// <summary>
    /// Rebuilds an isolated read snapshot from the retained run journal after applying the same
    /// tenant/company visibility policy as every other run drill-in. An absent ledger stays absent.
    /// </summary>
    public async Task<IReadOnlyLedger?> GetLedgerSnapshotAsync(
        string runId,
        StrategyRunReadScope scope,
        DateTimeOffset? asOf = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var run = await GetRunForScopeAsync(runId, scope, ct).ConfigureAwait(false);
        if (run is null)
            return null;
        var source = await ReadRunLedgerAsync(run, scope, ct).ConfigureAwait(false);
        if (source is null)
            return null;

        var snapshot = new Meridian.Ledger.Ledger();
        foreach (var entry in source.Journal.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            if (asOf is null || entry.Timestamp <= asOf.Value)
                snapshot.Post(entry);
        }
        return snapshot;
    }

    private async Task<IReadOnlyLedger?> ReadRunLedgerAsync(StrategyRunEntry run,
        StrategyRunReadScope? scope, CancellationToken ct)
    {
        if (run.RunType != RunType.Backtest && scope is not null && _postedLedgerSource is not null)
        {
            var posted = await _postedLedgerSource.ReadAsync(run, scope, ct).ConfigureAwait(false);
            if (posted is not null)
                return posted;
        }
        return run.Metrics?.Ledger;
    }

    private async Task<LedgerSummary?> BuildRunLedgerSummaryAsync(StrategyRunEntry run,
        StrategyRunReadScope? scope, CancellationToken ct)
        => await _ledgerReadService.BuildSummaryAsync(run, ct,
            await ReadRunLedgerAsync(run, scope, ct).ConfigureAwait(false)).ConfigureAwait(false);
}
