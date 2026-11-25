using Meridian.Contracts.Integrity;
using Meridian.Execution;
using Meridian.Execution.Services;
using Meridian.Strategies.Promotions;

namespace Meridian.Strategies.Live.Designer;

/// <summary>The review authority for a bounded Designer paper launch.</summary>
public interface IDesignerPaperReviewAuthority
{
    Task<StrategyPromotionRecord?> FindAsync(StrategyRunEntry source, string targetRunId, CancellationToken ct);
}

/// <summary>Resolves an ordinary promotion decision from the configured durable review store.</summary>
public sealed class RetainedDesignerPaperReviewAuthority(IPromotionRecordStore records) : IDesignerPaperReviewAuthority
{
    public async Task<StrategyPromotionRecord?> FindAsync(StrategyRunEntry source, string targetRunId, CancellationToken ct)
    {
        if (!DesignerStrategyVersion.HasRetainedProvenance(source) ||
            !string.Equals(source.DataProvenanceToken, "real", StringComparison.OrdinalIgnoreCase))
            return null;
        var decisions = await records.LoadAllAsync(ct).ConfigureAwait(false);
        var first = decisions.FirstOrDefault(decision => decision.SourceRunId == source.RunId &&
            decision.SourceRunType == RunType.Backtest && decision.TargetRunType == RunType.Paper);
        return first?.TargetRunId == targetRunId ? first : null;
    }
}

public sealed class DesignerPaperExecution(
    IStrategyRepository runs,
    IDesignerPaperReviewAuthority reviews,
    PaperSessionPersistenceService sessions)
{
    internal async Task<(PaperRunOrderContext? Context, string? Blocker)> PrepareAsync(
        StrategyRunEntry run, OrderManagementSystem oms, IReadOnlyList<string> universe, CancellationToken ct)
    {
        if (run.RunType != RunType.Paper || run.ParentRunId is null ||
            run.FundAccountId is not { } accountId || accountId == Guid.Empty ||
            !StrategyRunRepositoryVisibility.TryGetRetainedScope(run, out var scope) || !oms.SupportsScopedPaperIntents ||
            oms.ScopedPaperBook?.CanAccess(scope.TenantId, scope.CompanyId, accountId) != true)
            return (null, "The Designer version requires a scoped paper run on the in-process OMS gateway.");

        var target = await runs.GetRunByIdAsync(run.RunId, ct).ConfigureAwait(false);
        var source = await runs.GetRunByIdAsync(run.ParentRunId, ct).ConfigureAwait(false);
        var version = DesignerStrategyVersion.Read(run);
        if (target is null || source is null || source.RunType != RunType.Backtest ||
            target.RunType != RunType.Paper || target.FundAccountId != accountId ||
            target.ParentRunId != source.RunId || target.EndedAt.HasValue ||
            !Sha256Digest.FixedEquals(run.InputHashSha256, target.InputHashSha256) ||
            !string.Equals(run.StrategyArtifactId, target.StrategyArtifactId, StringComparison.Ordinal) ||
            !string.Equals(run.StrategyArtifactId, source.StrategyArtifactId, StringComparison.Ordinal) ||
            !Sha256Digest.FixedEquals(version, DesignerStrategyVersion.Read(source)) ||
            source.StrategyId != run.StrategyId || source.FundAccountId != accountId ||
            !DesignerStrategyVersion.HasRetainedProvenance(source) ||
            !DesignerStrategyVersion.HasRetainedProvenance(target) ||
            source.DataProvenanceToken != target.DataProvenanceToken ||
            source.EndedAt is null || source.Metrics is null ||
            source.LastLifecycleEvent != StrategyRunLifecycleEventType.Completed ||
            !StrategyRunRepositoryVisibility.TryGetRetainedScope(source, out var sourceScope) || sourceScope != scope)
            return (null, "The paper run does not match its retained Designer backtest and account.");

        var review = await reviews.FindAsync(source, run.RunId, ct).ConfigureAwait(false);
        if (review is null || review.Decision != PromotionDecisionKinds.Approved ||
            review.SourceRunId != source.RunId || review.TargetRunId != run.RunId ||
            review.StrategyId != run.StrategyId || review.SourceRunType != RunType.Backtest ||
            review.TargetRunType != RunType.Paper ||
            !string.Equals(review.StrategyArtifactId, run.StrategyArtifactId, StringComparison.Ordinal) ||
            !Sha256Digest.FixedEquals(review.StrategyVersionSha256, version) ||
            !Sha256Digest.FixedEquals(review.SourceInputHashSha256, source.InputHashSha256) ||
            string.IsNullOrWhiteSpace(review.ApprovedBy) || string.IsNullOrWhiteSpace(review.ApprovalReason) ||
            string.IsNullOrWhiteSpace(review.AuditReference) || review.AuditReference != target.AuditReference ||
            PromotionApprovalChecklist.GetMissingRequiredItems(RunType.Paper, review.ApprovalChecklist).Length != 0)
            return (null, "The paper run has no durable review of the exact strategy version and inputs.");

        // This bounded launch has no cross-restart strategy-state replay. A retained order is
        // enough to require reconciliation even when its acknowledgment or fill never persisted.
        if (await oms.HasRetainedPaperIntentForRunAsync(run.RunId, ct).ConfigureAwait(false))
            return (null, "The paper session has retained orders. Reconcile them before restarting this strategy.");

        PaperSessionSummaryDto session;
        try
        {
            session = await sessions.EnsureStrategyRunSessionAsync(run.RunId, target.StartedAt,
                new CreatePaperSessionDto(run.StrategyId, run.StrategyName, source.Metrics.Request.InitialCash, universe)
                {
                    FinancialAccountId = accountId.ToString("D")
                }, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException or IOException)
        {
            return (null, "The reviewed account's durable USD paper session is unavailable. Check the account and reconcile any retained session before retrying.");
        }
        if (!session.IsActive)
            return (null, "The retained paper session has already closed.");

        var authority = new StrategyOrderAuthority(run.StrategyId, run.RunId, version!, review.AuditReference, session.SessionId)
        {
            StrategyArtifactId = run.StrategyArtifactId
        };
        return (new PaperRunOrderContext(scope.TenantId, scope.CompanyId, accountId, authority, sessions), null);
    }
}

internal sealed record PaperRunOrderContext(
    string TenantId, string CompanyId, Guid AccountId, StrategyOrderAuthority Authority,
    PaperSessionPersistenceService Sessions);
