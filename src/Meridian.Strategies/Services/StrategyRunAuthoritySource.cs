using Meridian.Execution.OrderManagement;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;

namespace Meridian.Strategies.Services;

/// <summary>
/// Supplies the OMS with retained strategy-run authority: the run record and the approved live
/// promotion that targets it. Read-only; it never creates or repairs runs or promotions.
/// </summary>
public sealed class StrategyRunAuthoritySource : IStrategyRunAuthoritySource
{
    private readonly IStrategyRepository _runs;
    private readonly IPromotionRecordStore _promotions;

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

        // The latest approved live promotion whose target is this run. Rejected decisions and
        // promotions to other modes never confer live authority.
        StrategyPromotionRecord? promotion = null;
        foreach (var record in await _promotions.LoadAllAsync(ct).ConfigureAwait(false))
        {
            if (record.TargetRunType == RunType.Live
                && string.Equals(record.Decision, PromotionDecisionKinds.Approved, StringComparison.Ordinal)
                && string.Equals(record.TargetRunId, run.RunId, StringComparison.Ordinal))
            {
                promotion = record;
            }
        }

        return new StrategyRunAuthoritySnapshot(
            RunId: run.RunId,
            FundAccountId: run.FundAccountId,
            // A run that has ended, or reached any terminal status, no longer routes orders.
            IsActive: run.EndedAt is null && run.TerminalStatus is null,
            PromotionId: promotion?.PromotionId,
            PromotionTargetRunId: promotion?.TargetRunId,
            PromotionAuditReference: promotion?.AuditReference,
            // Strategy-artifact identity is not retained on runs or promotions yet
            // (strategy-artifact-identity blueprint); the resolver skips the comparison when null.
            RunStrategyArtifactId: null,
            PromotedStrategyArtifactId: null);
    }
}
