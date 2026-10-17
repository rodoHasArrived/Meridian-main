using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.AccountingClose;

public sealed partial class AccountingClosePreparationService
{
    private async Task<PreparedClosePlanResultDto?> TryRecoverConfiguredClaimAsync(PreparationDocument state,
        CreationEntry claim, string tenantId, string companyId, CancellationToken ct)
    {
        if (!claim.StartAttempted)
            return null;
        var workflow = await workflows.GetAsync(claim.WorkflowId, ct).ConfigureAwait(false);
        if (workflow is null)
            return null;
        var plan = await closePlans.GetPeriodPlanScopedAsync(claim.WorkflowId, tenantId, companyId, ct).ConfigureAwait(false);
        var lineage = plan?.Configuration?.Preparation;
        if (lineage is null)
            return null;
        var evidence = ConfigurationEvidence(claim);
        if (lineage.TemplateId != claim.TemplateId || lineage.TemplateVersion != claim.TemplateVersion
            || lineage.SourceWorkflowId != claim.SourceWorkflowId || lineage.TargetPeriodId != claim.TargetPeriodId
            || plan!.LedgerBookId != claim.TargetBookId || plan.WorkflowId != claim.WorkflowId
            || !plan.Configuration!.EvidenceLinks.Contains(evidence, StringComparer.Ordinal))
            throw new ClosePreparationRecoveryRequiredException("The target configuration no longer matches its retained creation claim. Preserve this request and resolve the conflicting history before retrying.");
        // Configuration is the successful creation boundary. Subsequent source changes or target
        // execution must not erase that success when the final claim update was interrupted.
        claim = claim with { Completed = true, History = lineage.History };
        await SaveAsync(ReplaceClaim(state, claim), ct).ConfigureAwait(false);
        return Result(claim, plan, true);
    }

    private async Task RejectStaleAsync(PreparationDocument state, CreationEntry? claim, string reason, CancellationToken ct)
    {
        if (claim is { StartAttempted: true })
            throw new ClosePreparationRecoveryRequiredException($"{reason} This preparation already attempted workflow creation. Preserve the original request key and resolve its retained workflow before retrying; a new plan cannot replace it.");
        if (claim is not null)
        {
            // StartAttempted is flushed before invoking workflow creation. A false value proves
            // no call could have started; only this case can safely release the target reservation.
            claim = claim with
            {
                Abandoned = true,
                History = [.. claim.History, new("CreationAbandoned", DateTimeOffset.UtcNow, claim.CreatedBy,
                    "Released an unattempted preparation after preview inputs changed.")]
            };
            await SaveAsync(ReplaceClaim(state, claim), ct).ConfigureAwait(false);
        }
        throw new ClosePreparationPreviewStaleException($"{reason} Generate a fresh preview.");
    }

    private static PreparationDocument ReplaceClaim(PreparationDocument state, CreationEntry claim)
        => state with { Creations = state.Creations.Select(item => item.WorkflowId == claim.WorkflowId ? claim : item).ToArray() };

    private static string ConfigurationEvidence(CreationEntry claim)
        => $"close-plan-preparation:{claim.WorkflowId:D}:book:{claim.TargetBookId:D}:preview:{claim.PreviewId:D}";
}
