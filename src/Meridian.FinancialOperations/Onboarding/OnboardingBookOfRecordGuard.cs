using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.Onboarding;

/// <summary>
/// Resolves book-of-record status from retained onboarding workspaces. A book with no shadow designation is
/// authoritative. A book in shadow mode, or one whose shadow trial was withdrawn without promotion, is not.
/// </summary>
public sealed class OnboardingBookOfRecordGuard(IOnboardingWorkspaceStore store) : IBookOfRecordGuard
{
    public async Task<BookOfRecordStatusDto> GetStatusAsync(
        string tenantId, string? companyId, Guid ledgerBookId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || ledgerBookId == Guid.Empty)
            return new(ledgerBookId, false, null, null, "A resolved tenant and ledger book are required to establish book-of-record status.");
        var designated = (await store.ListByLedgerBookAsync(tenantId, companyId, ledgerBookId, ct).ConfigureAwait(false))
            .Where(workspace => workspace.ShadowDesignation is not null)
            .OrderBy(workspace => workspace.ShadowDesignation!.DesignatedAtUtc)
            .ToArray();
        if (designated.Length == 0)
            return new(ledgerBookId, true, null, null, "The ledger book is not in shadow mode.");

        var active = designated.LastOrDefault(workspace => workspace.ShadowDesignation!.State == OnboardingShadowStates.Shadow);
        if (active is not null)
        {
            var periods = active.Readiness.Periods ?? [];
            return new(ledgerBookId, false, OnboardingShadowStates.Shadow, active.WorkspaceId,
                $"The ledger book is a shadow book in parallel run: {periods.Count(period => period.IsClean)} of " +
                $"{active.Criteria.RequiredDates.Count} required period(s) reconcile cleanly with the incumbent and " +
                $"{active.Readiness.UnresolvedDifferenceCount} difference(s) remain unexplained. Promote it with a " +
                "parallel-run certificate before it can act as the book of record.",
                periods.Count(period => period.IsClean), active.Criteria.RequiredDates.Count, active.Readiness.UnresolvedDifferenceCount);
        }

        var promoted = designated.LastOrDefault(workspace => workspace.ShadowDesignation!.State == OnboardingShadowStates.Promoted);
        if (promoted is not null)
            return new(ledgerBookId, true, OnboardingShadowStates.Promoted, promoted.WorkspaceId,
                $"The shadow book was promoted by decision {promoted.Promotion?.DecisionId}.");

        var withdrawn = designated[^1];
        return new(ledgerBookId, false, OnboardingShadowStates.Withdrawn, withdrawn.WorkspaceId,
            "The shadow trial for this ledger book was withdrawn without promotion; it cannot act as the book of record.");
    }
}
