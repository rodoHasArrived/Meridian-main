using System.Text.Json;
using Meridian.Storage.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>Explicit maintenance orchestration; never registered as a startup backfill.</summary>
public sealed class FundStructureTenantBackfillRunner(IFundStructureTenantBackfillStore store)
{
    public async Task<FundStructureTenantBackfillPlan> PreviewAsync(CancellationToken ct = default)
    {
        await using var session = await store.OpenSessionAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var plan = FundStructureTenantBackfillPlanner.Create(session.Snapshot);
        ct.ThrowIfCancellationRequested();
        return plan;
    }

    public async Task<FundStructureTenantBackfillReceipt> ApplyAsync(
        Guid runId, string reviewedPlanHash, string operatorId, string reviewReference,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("A retained run identity is required.", nameof(runId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedPlanHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewReference);
        ct.ThrowIfCancellationRequested();

        var committed = await store.FindReceiptAsync(runId, ct).ConfigureAwait(false);
        if (committed is not null)
            return ValidateReceipt(committed, reviewedPlanHash, operatorId, reviewReference);

        await using var session = await store.OpenSessionAsync(ct).ConfigureAwait(false);
        // A concurrent caller may have committed while this caller waited for the graph locks.
        var retained = await session.FindReceiptAsync(runId, ct).ConfigureAwait(false);
        if (retained is not null)
        {
            return ValidateReceipt(retained, reviewedPlanHash, operatorId, reviewReference);
        }

        // The caller supplies only the reviewed fingerprint, never proposed tenant values. Rebuild
        // the complete plan from locked authoritative rows, rather than trusting a local JSON edit.
        var current = FundStructureTenantBackfillPlanner.Create(session.Snapshot);
        ct.ThrowIfCancellationRequested();
        if (!string.Equals(current.PlanHash, reviewedPlanHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The reviewed plan is stale or belongs to another code/schema/source identity; preview again.");
        if (current.BlockingReasons.Count != 0)
            throw new InvalidOperationException("The plan contains unresolved structural or retained-review blockers.");

        return await session.CommitAsync(runId, current.PlanHash, operatorId, reviewReference,
            JsonSerializer.SerializeToElement(current), current.Stamps, current.Exceptions, ct).ConfigureAwait(false);
    }
    private static FundStructureTenantBackfillReceipt ValidateReceipt(
        FundStructureTenantBackfillReceipt retained, string planHash, string operatorId, string reviewReference)
    {
        if (retained.PlanHash != planHash || retained.OperatorId != operatorId || retained.ReviewReference != reviewReference)
            throw new InvalidOperationException("The run identity is already bound to different review evidence.");
        return retained;
    }
}
