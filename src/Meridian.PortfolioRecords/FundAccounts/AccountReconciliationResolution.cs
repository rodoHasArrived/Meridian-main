using Meridian.Contracts.FundStructure;

namespace Meridian.PortfolioRecords.FundAccounts;

internal static class AccountReconciliationResolution
{
    internal static string CheckKey(string label) => label.StartsWith("PositionCount (", StringComparison.OrdinalIgnoreCase)
        ? "PositionCount" : label;

    // A verified observation replaces only the same check in the same account/date scope.
    // Missing evidence never clears a retained disagreement.
    internal static void Supersede(
        IReadOnlyList<AccountReconciliationRunDto> runs,
        List<AccountReconciliationResultDto> retained,
        AccountReconciliationRunDto run,
        List<AccountReconciliationResultDto> incoming)
    {
        var scope = runs.Where(r => r.AccountId == run.AccountId && r.AsOfDate == run.AsOfDate)
            .ToDictionary(r => r.ReconciliationRunId);
        for (var resultIndex = 0; resultIndex < incoming.Count; resultIndex++)
        {
            var result = incoming[resultIndex];
            if (AccountReconciliationChecks.IsUnverified(result))
                continue;
            var newerCandidates = retained.Where(r => scope.TryGetValue(r.ReconciliationRunId, out var owner)
                    && owner.RequestedAt > run.RequestedAt
                    && !AccountReconciliationChecks.IsUnverified(r)
                    && string.Equals(r.Category, result.Category, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(CheckKey(r.CheckLabel), CheckKey(result.CheckLabel), StringComparison.OrdinalIgnoreCase))
                .GroupBy(r => scope[r.ReconciliationRunId].RequestedAt)
                .OrderByDescending(group => group.Key).FirstOrDefault();
            var newer = newerCandidates?.Count() == 1 ? newerCandidates.Single() : null;
            if (newer is not null)
            {
                incoming[resultIndex] = result with
                {
                    SupersededByResultId = newer.ResultId,
                    ResolvedAt = result.IsOpenBreak && newer.IsMatch
                        ? scope[newer.ReconciliationRunId].CompletedAt ?? scope[newer.ReconciliationRunId].RequestedAt : null
                };
                continue;
            }
            for (var i = 0; i < retained.Count; i++)
            {
                var previous = retained[i];
                if (previous.SupersededByResultId is not null
                    || !scope.TryGetValue(previous.ReconciliationRunId, out var previousRun)
                    || previousRun.RequestedAt >= run.RequestedAt
                    || !string.Equals(previous.Category, result.Category, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(CheckKey(previous.CheckLabel), CheckKey(result.CheckLabel), StringComparison.OrdinalIgnoreCase))
                    continue;
                retained[i] = previous with
                {
                    SupersededByResultId = result.ResultId,
                    ResolvedAt = previous.IsOpenBreak && result.IsMatch ? run.CompletedAt ?? run.RequestedAt : null
                };
            }
        }
    }
}
