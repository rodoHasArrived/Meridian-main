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
        var selected = await ReadRunLedgerAsync(run, scope, ct, asOf, capturePostingChart: false).ConfigureAwait(false);
        var source = selected.Ledger;
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

    private async Task<RunLedgerSelection> ReadRunLedgerAsync(StrategyRunEntry run,
        StrategyRunReadScope? scope, CancellationToken ct, DateTimeOffset? asOf = null, bool capturePostingChart = true)
    {
        if (run.RunType != RunType.Backtest && scope is not null && _postedLedgerSource is not null)
        {
            var posted = await _postedLedgerSource.ReadAsync(run, scope, ct).ConfigureAwait(false);
            if (posted is not null)
                return new(posted, capturePostingChart ? _postedLedgerSource.GetPostingChart(posted, asOf) : null,
                    _postedLedgerSource.GetBalanceComparisonUnavailableReason(posted));
        }
        var retained = run.Metrics?.Ledger;
        var missingAuthoritativeSource = run.RunType != RunType.Backtest && scope is not null;
        // The private live context ledger starts empty and does not attest an empty accounting book.
        return new(run.RunType != RunType.Backtest && retained?.Journal.Count == 0 ? null : retained,
            (retained as Meridian.Ledger.Ledger)?.Policy?.Chart,
            missingAuthoritativeSource ? "No authoritative posted accounting source established trusted run ownership; retained metrics journals are audit-only evidence." : null,
            HasAuthoritativeRunAttribution: !missingAuthoritativeSource);
    }

    private async Task<LedgerSummary?> BuildRunLedgerSummaryAsync(StrategyRunEntry run,
        StrategyRunReadScope? scope, CancellationToken ct, RunLedgerSelection? selected = null)
    {
        selected ??= await ReadRunLedgerAsync(run, scope, ct).ConfigureAwait(false);
        var summary = selected.Ledger is null ? null : await _ledgerReadService.BuildSummaryAsync(run, ct,
            selected.Ledger, selected.PostingChart).ConfigureAwait(false);
        return ApplyLedgerSelectionCoverage(summary, selected);
    }

    private static LedgerSummary? ApplyLedgerSelectionCoverage(LedgerSummary? summary, RunLedgerSelection selected)
        => summary is null ? null : summary with
        {
            BalanceComparisonUnavailableReason = selected.BalanceComparisonUnavailableReason,
            AccountingPositions = selected.HasAuthoritativeRunAttribution ? summary.AccountingPositions
                : summary.AccountingPositions.Select(position => position with
                {
                    Quantity = null,
                    MonetaryBasis = null,
                    CoverageFailure = selected.BalanceComparisonUnavailableReason
                }).ToArray()
        };

    private sealed record RunLedgerSelection(IReadOnlyLedger? Ledger, IChartOfAccounts? PostingChart,
        string? BalanceComparisonUnavailableReason, bool HasAuthoritativeRunAttribution = true);
}
