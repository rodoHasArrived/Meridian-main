using Meridian.Contracts.FundStructure;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FundOperationsWorkspaceReadService
{
    private async Task<AccountReconciliationReadCut> ReadAccountReconciliationCutAsync(
        IReadOnlyList<AccountSummaryDto> accounts, CancellationToken ct)
    {
        var ids = accounts.Select(account => account.AccountId).Distinct().ToArray();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var before = await ReadRunsAsync().ConfigureAwait(false);
            var results = await _fundAccountService.GetReconciliationResultsForAccountsAsync(ids, ct: ct)
                .ConfigureAwait(false);
            var after = await ReadRunsAsync().ConfigureAwait(false);
            // Run history is immutable and append-only. Stores publish a run and its results
            // atomically, including supersession of older results. An unchanged run set on both
            // sides therefore identifies the same read cut at the batched result query. A new run
            // cannot disappear between these reads (no ABA); retry rather than join mismatched cuts.
            if (ids.All(id => before[id].SequenceEqual(after[id])))
            {
                var runIds = before.Values.SelectMany(runs => runs).Select(run => run.ReconciliationRunId).ToHashSet();
                if (results.Any(result => !runIds.Contains(result.ReconciliationRunId)))
                    throw new InvalidOperationException("Reconciliation results refer to unavailable run history.");
                return new(before, results.ToLookup(result => result.ReconciliationRunId));
            }
        }
        throw new InvalidOperationException("Reconciliation history changed during the workspace read. Retry to obtain consistent run and break evidence.");

        async Task<Dictionary<Guid, AccountReconciliationRunDto[]>> ReadRunsAsync()
        {
            var runs = new Dictionary<Guid, AccountReconciliationRunDto[]>();
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                runs[id] = (await _fundAccountService.GetReconciliationRunsAsync(id, ct).ConfigureAwait(false))
                    .OrderBy(run => run.ReconciliationRunId).ToArray();
            }
            return runs;
        }
    }

    private sealed record AccountReconciliationReadCut(
        IReadOnlyDictionary<Guid, AccountReconciliationRunDto[]> Runs,
        ILookup<Guid, AccountReconciliationResultDto> Results);
}
