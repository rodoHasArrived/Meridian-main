namespace Meridian.Contracts.CashFlows;

public sealed record CashFlowDateDelta(DateOnly Date, string StreamId, decimal LeftNetCash, decimal RightNetCash)
{
    public decimal Delta => RightNetCash - LeftNetCash;
}

/// <summary>Side is "LeftOnly" or "RightOnly"; a blocker present in both runs is not a delta.</summary>
public sealed record CashFlowBlockerDelta(string Side, string Code, string Message, string? StreamId);

/// <summary>
/// Pure read comparing two retained runs of the same subject: per-date, per-stream net cash deltas, the
/// symmetric difference of blockers, and whether each retained packet still replays to its own output
/// hash. Nothing is recalculated against current terms; both sides are the retained packets.
/// </summary>
public sealed record ContractualCashFlowRunComparison(
    string SubjectKind,
    Guid SubjectId,
    Guid LeftRunId,
    Guid RightRunId,
    bool LeftReplayVerified,
    bool RightReplayVerified,
    bool SameRequest,
    string LeftOutputHash,
    string RightOutputHash,
    IReadOnlyList<CashFlowDateDelta> CashDeltas,
    IReadOnlyList<CashFlowBlockerDelta> BlockerDeltas)
{
    public decimal TotalDelta => CashDeltas.Sum(delta => delta.Delta);
}

public static class ContractualCashFlowRunComparer
{
    public static ContractualCashFlowRunComparison Compare(ContractualCashFlowRun left, ContractualCashFlowRun right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.SubjectKind != right.SubjectKind || left.SubjectId != right.SubjectId)
            throw new ArgumentException("Only retained runs of the same subject can be compared.");
        if (left.RunId == right.RunId)
            throw new ArgumentException("Choose two different retained runs to compare.");
        if (!string.Equals(left.Contract.Currency, right.Contract.Currency, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only retained runs in the same contract currency can be compared; amounts are never converted.");

        var leftCash = NetCashByDateAndStream(left.Result.Events);
        var rightCash = NetCashByDateAndStream(right.Result.Events);
        var deltas = leftCash.Keys.Union(rightCash.Keys)
            .OrderBy(key => key.Date).ThenBy(key => key.StreamId, StringComparer.Ordinal)
            .Select(key => new CashFlowDateDelta(key.Date, key.StreamId, leftCash.GetValueOrDefault(key), rightCash.GetValueOrDefault(key)))
            .Where(delta => delta.Delta != 0m)
            .ToArray();

        var leftBlockers = left.Result.Blockers.Select(Key).ToHashSet();
        var rightBlockers = right.Result.Blockers.Select(Key).ToHashSet();
        var blockerDeltas = left.Result.Blockers.Where(b => !rightBlockers.Contains(Key(b))).Select(b => new CashFlowBlockerDelta("LeftOnly", b.Code, b.Message, b.StreamId))
            .Concat(right.Result.Blockers.Where(b => !leftBlockers.Contains(Key(b))).Select(b => new CashFlowBlockerDelta("RightOnly", b.Code, b.Message, b.StreamId)))
            .ToArray();

        return new(left.SubjectKind, left.SubjectId, left.RunId, right.RunId,
            ContractualCashFlowEngine.Verify(left), ContractualCashFlowEngine.Verify(right),
            ContractualCashFlowEngine.Hash(left.Request) == ContractualCashFlowEngine.Hash(right.Request),
            left.Result.Evidence.OutputHash, right.Result.Evidence.OutputHash, deltas, blockerDeltas);
    }

    private static Dictionary<(DateOnly Date, string StreamId), decimal> NetCashByDateAndStream(IReadOnlyList<ContractualCashFlowEvent> events)
        => events.GroupBy(e => (e.Date, e.StreamId)).ToDictionary(g => g.Key, g => g.Sum(e => e.NetCash));

    private static (string, string, string?) Key(CashFlowCalculationBlocker blocker) => (blocker.Code, blocker.Message, blocker.StreamId);
}
