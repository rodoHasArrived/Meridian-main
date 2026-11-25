using System.Collections.Concurrent;
using Meridian.Execution.OrderManagement;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;

namespace Meridian.Strategies.Services;

/// <summary>
/// Supplies the OMS with retained strategy-run authority: the run record and the approved live
/// promotion that targets it. Read-only; it never creates or repairs runs or promotions.
/// </summary>
/// <remarks>
/// The OMS asks at preflight and again at dispatch, inside its pre-trade gate, so every live
/// strategy order waits on these reads. The run record is read every time, because whether a run
/// is still active changes. An approved live promotion found for a run is remembered: promotion
/// decisions are append-only (<see cref="IPromotionRecordStore"/> loads, reserves and appends;
/// it never rewrites or removes), so an approval found for a run cannot later be withdrawn from
/// the store. A run with no approval yet is looked up again on the next order. The remembered
/// record is the one found first; a later approval for the same run would differ only in its
/// promotion id and audit reference.
/// </remarks>
public sealed class StrategyRunAuthoritySource : IStrategyRunAuthoritySource
{
    private readonly IStrategyRepository _runs;
    private readonly IPromotionRecordStore _promotions;
    private readonly ConcurrentDictionary<string, StrategyPromotionRecord> _approvedLivePromotions = new(StringComparer.Ordinal);

    public StrategyRunAuthoritySource(IStrategyRepository runs, IPromotionRecordStore promotions)
    {
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _promotions = promotions ?? throw new ArgumentNullException(nameof(promotions));
    }

    /// <inheritdoc />
    public async Task<StrategyRunAuthoritySnapshot?> GetAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var run = await _runs.GetRunByIdAsync(runId, ct).ConfigureAwait(false);
        if (run is null)
            return null;

        var promotion = await FindApprovedLivePromotionAsync(run.RunId, ct).ConfigureAwait(false);

        return new StrategyRunAuthoritySnapshot(
            RunId: run.RunId,
            FundAccountId: run.FundAccountId,
            // A run that has ended, or reached any terminal status, no longer routes orders.
            IsActive: run.EndedAt is null && run.TerminalStatus is null,
            PromotionId: promotion?.PromotionId,
            PromotionTargetRunId: promotion?.TargetRunId,
            PromotionAuditReference: promotion?.AuditReference,
            RunStrategyArtifactId: run.StrategyArtifactId,
            PromotedStrategyArtifactId: promotion?.StrategyArtifactId);
    }

    /// <summary>
    /// The latest approved live promotion whose target is this run. Rejected decisions and
    /// promotions to other modes never confer live authority.
    /// </summary>
    private async Task<StrategyPromotionRecord?> FindApprovedLivePromotionAsync(string runId, CancellationToken ct)
    {
        if (_approvedLivePromotions.TryGetValue(runId, out var known))
            return known;

        StrategyPromotionRecord? promotion = null;
        foreach (var record in await _promotions.LoadAllAsync(ct).ConfigureAwait(false))
        {
            if (record.TargetRunType == RunType.Live
                && string.Equals(record.Decision, PromotionDecisionKinds.Approved, StringComparison.Ordinal)
                && string.Equals(record.TargetRunId, runId, StringComparison.Ordinal))
            {
                promotion = record;
            }
        }

        if (promotion is not null)
            _approvedLivePromotions.TryAdd(runId, promotion);
        return promotion;
    }
}
