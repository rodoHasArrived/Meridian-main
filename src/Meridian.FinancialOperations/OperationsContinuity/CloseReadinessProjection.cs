using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

/// <summary>One fail-closed decision over the required close contributors.</summary>
internal sealed class CloseReadinessProjection(CloseReadinessScopeDto scope, DateTimeOffset evaluatedAt)
{
    private readonly List<CloseReadinessContributionDto> _contributions = [];
    private readonly List<CloseReadinessBlockerDto> _blockers = [];
    private bool _complete = true;

    public bool HasCompleteScope => !string.IsNullOrWhiteSpace(scope.FundProfileId)
        && scope.LedgerBookId is { } book && book != Guid.Empty
        && scope.FundAccountId is { } account && account != Guid.Empty
        && !string.IsNullOrWhiteSpace(scope.EntityId)
        && !string.IsNullOrWhiteSpace(scope.PeriodId);

    public void Contribute(string id, string owner, string status, DateTimeOffset? timestamp,
        IReadOnlyList<string> records, string? reason = null)
    {
        if ((status is "Ready" or "Blocked") && (timestamp is null || timestamp > evaluatedAt.AddMinutes(1)
            || timestamp < evaluatedAt.AddMinutes(-5)))
        {
            status = "Stale";
            reason = "The contributor has no current evaluation for the selected close scope.";
        }

        _contributions.Add(new(id, owner, status, timestamp, records));
        if (status != "Ready")
        {
            _complete &= status == "Blocked";
            _blockers.Add(new($"close.{id}.{status.ToLowerInvariant()}", id, status,
                Math.Max(1, records.Count), "Critical", owner,
                reason ?? $"{owner} has unresolved close requirements.", records));
        }
    }

    public CloseReadinessProjectionDto Build(IReadOnlyList<FinancialOperationsQueueRowDto> rows)
    {
        if (!HasCompleteScope)
        {
            _complete = false;
            _blockers.Add(new("close.scope.required", "scope", "ScopeRequired", 1, "Critical",
                "Controller", "Select a fund profile, ledger book, fund account, entity, and period before evaluating close readiness.", []));
        }

        foreach (var row in rows)
        {
            _blockers.Add(new(row.QueueId, row.SourceKind, row.BlockerType, 1,
                row.SeverityLabel, row.OwnerLabel, $"{row.Detail} {row.ActionLabel}", [row.QueueId]));
        }

        var ready = _complete && _blockers.Count == 0;
        return new(scope, evaluatedAt, ready ? "Ready" : "Blocked", _complete, ready,
            _contributions.ToArray(), _blockers.ToArray());
    }
}
