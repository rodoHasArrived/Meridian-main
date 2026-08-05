using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

internal static class ReconciliationRunContinuity
{
    public static IReadOnlyList<ReconciliationRunDetail> UpsertAndNormalize(
        IEnumerable<ReconciliationRunDetail> retainedRuns,
        ReconciliationRunDetail incoming)
    {
        ArgumentNullException.ThrowIfNull(retainedRuns);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentException.ThrowIfNullOrWhiteSpace(incoming.Summary.ReconciliationRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(incoming.Summary.RunId);

        // Repositories retain rows newest-first. Preserve that commit order as the tie-breaker when
        // two processes observe the same clock tick; random reconciliation ids must not decide
        // whether a break is currently open or matched.
        var retained = retainedRuns
            .Where(static run => !string.IsNullOrWhiteSpace(run.Summary.ReconciliationRunId))
            .ToArray();
        var runsById = new Dictionary<string, RankedRun>(StringComparer.Ordinal);
        for (var index = 0; index < retained.Length; index++)
        {
            var run = retained[index];
            runsById.TryAdd(
                run.Summary.ReconciliationRunId,
                new RankedRun(run, retained.Length - index));
        }

        runsById[incoming.Summary.ReconciliationRunId] = new RankedRun(
            incoming,
            retained.Length + 1);

        foreach (var normalized in NormalizeHistory(
                     runsById.Values
                         .Where(item => string.Equals(
                             item.Detail.Summary.RunId,
                             incoming.Summary.RunId,
                             StringComparison.Ordinal))
                         .OrderBy(static item => item.Detail.Summary.CreatedAt)
                         .ThenBy(static item => item.EqualTimestampOrder)
                         .ThenBy(static item => item.Detail.Summary.ReconciliationRunId, StringComparer.Ordinal)
                         .Select(static item => item.Detail)))
        {
            var ranked = runsById[normalized.Summary.ReconciliationRunId];
            runsById[normalized.Summary.ReconciliationRunId] = ranked with { Detail = normalized };
        }

        return runsById.Values
            .OrderByDescending(static item => item.Detail.Summary.CreatedAt)
            .ThenByDescending(static item => item.EqualTimestampOrder)
            .ThenByDescending(static item => item.Detail.Summary.ReconciliationRunId, StringComparer.Ordinal)
            .Select(static item => item.Detail)
            .ToArray();
    }

    private static IReadOnlyList<ReconciliationRunDetail> NormalizeHistory(
        IEnumerable<ReconciliationRunDetail> history)
    {
        var active = new Dictionary<string, ActiveObservation>(StringComparer.Ordinal);
        var normalizedRuns = new List<ReconciliationRunDetail>();

        foreach (var run in history)
        {
            var matches = run.Matches
                .Select(match => NormalizeMatch(match, run.Summary))
                .ToArray();
            foreach (var match in matches)
            {
                CloseCompatibleObservations(active, Descriptor(match));
            }

            var breaks = new List<ReconciliationBreakDto>(run.Breaks.Count);
            foreach (var breakItem in run.Breaks.Select(item => NormalizeBreak(item, run.Summary)))
            {
                var descriptor = Descriptor(breakItem);
                var compatible = FindCompatibleObservations(active, descriptor);
                var firstObservedAt = compatible
                    .Select(static item => item.FirstObservedAt)
                    .Append(breakItem.FirstObservedAt ?? run.Summary.CreatedAt)
                    .Min();

                foreach (var observation in compatible)
                {
                    active.Remove(observation.Identity);
                }

                var normalizedBreak = breakItem with { FirstObservedAt = firstObservedAt };
                breaks.Add(normalizedBreak);

                if (IsUnresolvedStatus(normalizedBreak.Status))
                {
                    active[descriptor.Identity] = new ActiveObservation(
                        descriptor.Identity,
                        descriptor.CheckKey,
                        descriptor.BankEntityId,
                        descriptor.SourceScope,
                        descriptor.CorrelationKeys,
                        firstObservedAt);
                }
            }

            normalizedRuns.Add(run with
            {
                Matches = matches,
                Breaks = breaks.ToArray()
            });
        }

        return normalizedRuns;
    }

    private static ReconciliationMatchDto NormalizeMatch(
        ReconciliationMatchDto match,
        ReconciliationRunSummary summary)
    {
        var correlationKeys = EnsureRunCorrelation(match.CorrelationKeys, summary.RunId);
        var bankEntityId = ResolveBankEntity(match.CheckId, match.BankEntityId, summary.BankEntityId);
        var identity = ReconciliationLogicalBreakIdentity.Create(
            match.CheckId,
            bankEntityId,
            match.SourceScope,
            correlationKeys);

        return match with
        {
            LogicalBreakIdentity = identity,
            BankEntityId = bankEntityId,
            SourceScope = NormalizeOptional(match.SourceScope),
            CorrelationKeys = correlationKeys
        };
    }

    private static ReconciliationBreakDto NormalizeBreak(
        ReconciliationBreakDto breakItem,
        ReconciliationRunSummary summary)
    {
        var correlationKeys = EnsureRunCorrelation(breakItem.CorrelationKeys, summary.RunId);
        var bankEntityId = ResolveBankEntity(breakItem.CheckId, breakItem.BankEntityId, summary.BankEntityId);
        var identity = ReconciliationLogicalBreakIdentity.Create(
            breakItem.CheckId,
            bankEntityId,
            breakItem.SourceScope,
            correlationKeys);

        return breakItem with
        {
            LogicalBreakIdentity = identity,
            BankEntityId = bankEntityId,
            SourceScope = NormalizeOptional(breakItem.SourceScope),
            CorrelationKeys = correlationKeys
        };
    }

    private static OperationsContinuityCorrelationKeysDto EnsureRunCorrelation(
        OperationsContinuityCorrelationKeysDto? correlationKeys,
        string runId)
    {
        if (correlationKeys is null)
        {
            return new OperationsContinuityCorrelationKeysDto(RunId: runId);
        }

        return correlationKeys with { RunId = runId };
    }

    private static Guid? ResolveBankEntity(string checkId, Guid? itemBankEntityId, Guid? runBankEntityId) =>
        IsBankScopedCheck(checkId) ? runBankEntityId ?? itemBankEntityId : itemBankEntityId;

    private static bool IsBankScopedCheck(string checkId) =>
        checkId.StartsWith("bank-", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static bool IsUnresolvedStatus(ReconciliationBreakStatus status) =>
        status is not ReconciliationBreakStatus.Matched and not ReconciliationBreakStatus.Resolved;

    private static ObservationDescriptor Descriptor(ReconciliationMatchDto match) => new(
        match.LogicalBreakIdentity!,
        NormalizeCheckKey(match.CheckId),
        match.BankEntityId,
        match.SourceScope,
        match.CorrelationKeys);

    private static ObservationDescriptor Descriptor(ReconciliationBreakDto breakItem) => new(
        breakItem.LogicalBreakIdentity!,
        NormalizeCheckKey(breakItem.CheckId),
        breakItem.BankEntityId,
        breakItem.SourceScope,
        breakItem.CorrelationKeys);

    private static string NormalizeCheckKey(string checkId) => checkId.Trim().ToLowerInvariant();

    private static IReadOnlyList<ActiveObservation> FindCompatibleObservations(
        IReadOnlyDictionary<string, ActiveObservation> active,
        ObservationDescriptor descriptor)
    {
        if (active.TryGetValue(descriptor.Identity, out var exact))
        {
            return [exact];
        }

        var compatible = active.Values
            .Where(item =>
                string.Equals(item.CheckKey, descriptor.CheckKey, StringComparison.Ordinal)
                && ScopesAreCompatible(item, descriptor))
            .ToArray();
        // Legacy rows can omit scope. Carry or close them only when the mapping is unambiguous;
        // an unscoped row must never merge incidents from multiple bank entities or cases.
        return compatible.Length == 1 ? compatible : [];
    }

    private static void CloseCompatibleObservations(
        Dictionary<string, ActiveObservation> active,
        ObservationDescriptor descriptor)
    {
        foreach (var observation in FindCompatibleObservations(active, descriptor))
        {
            active.Remove(observation.Identity);
        }
    }

    private static bool ScopesAreCompatible(ActiveObservation active, ObservationDescriptor candidate) =>
        Compatible(active.BankEntityId, candidate.BankEntityId)
        && Compatible(active.SourceScope, candidate.SourceScope)
        && Compatible(active.CorrelationKeys?.RunId, candidate.CorrelationKeys?.RunId)
        && Compatible(active.CorrelationKeys?.FundAccountId, candidate.CorrelationKeys?.FundAccountId)
        && Compatible(active.CorrelationKeys?.PortfolioSnapshotId, candidate.CorrelationKeys?.PortfolioSnapshotId)
        && Compatible(active.CorrelationKeys?.LedgerBatchId, candidate.CorrelationKeys?.LedgerBatchId)
        && Compatible(active.CorrelationKeys?.LedgerPostingGroupId, candidate.CorrelationKeys?.LedgerPostingGroupId)
        && Compatible(active.CorrelationKeys?.ReconciliationCaseId, candidate.CorrelationKeys?.ReconciliationCaseId);

    private static bool Compatible(Guid? left, Guid? right) =>
        !left.HasValue || !right.HasValue || left.Value == right.Value;

    private static bool Compatible(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left)
        || string.IsNullOrWhiteSpace(right)
        || string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record ObservationDescriptor(
        string Identity,
        string CheckKey,
        Guid? BankEntityId,
        string? SourceScope,
        OperationsContinuityCorrelationKeysDto? CorrelationKeys);

    private sealed record ActiveObservation(
        string Identity,
        string CheckKey,
        Guid? BankEntityId,
        string? SourceScope,
        OperationsContinuityCorrelationKeysDto? CorrelationKeys,
        DateTimeOffset FirstObservedAt);

    private sealed record RankedRun(
        ReconciliationRunDetail Detail,
        int EqualTimestampOrder);
}
