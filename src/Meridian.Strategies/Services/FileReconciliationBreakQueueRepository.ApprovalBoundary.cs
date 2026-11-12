using System.Text.Json;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public sealed partial class FileReconciliationBreakQueueRepository
{
    private async Task<ReconciliationBreakQueueTransitionResult?> ValidatePreparerApprovalReferenceAsync(
        ReconciliationBreakQueueItem item,
        ReconciliationCaseworkCommand command,
        CancellationToken ct)
    {
        // Only denial classification is added here. An unrecognized opaque legacy reference
        // still does not authenticate independent review; all ordinary controls remain in force.
        var inputs = new[] { command.ApprovalReference, command.ApprovalActor };
        if (inputs.Any(PreparerAttestationApprovalBoundary.IsReservedReference))
            return PreparerApprovalRefusal(item);

        if (_preparerAttestationReferences is null
            || string.IsNullOrWhiteSpace(item.TenantId) || string.IsNullOrWhiteSpace(item.CompanyId))
            return null;
        try
        {
            foreach (var input in inputs)
            {
                if (await _preparerAttestationReferences.IsPreparerReceiptReferenceAsync(
                        item.TenantId, item.CompanyId, input, ct).ConfigureAwait(false))
                    return PreparerApprovalRefusal(item);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or JsonException or TimeoutException)
        {
            return Invalid(item,
                "Approval reference classification is unavailable; no new disposition can rely on this reference.",
                ReconciliationBreakQueueTransitionErrorCode.MissingApproval, ["approvalReference"]);
        }
        return null;
    }

    private static ReconciliationBreakQueueTransitionResult PreparerApprovalRefusal(ReconciliationBreakQueueItem item) =>
        Invalid(item,
            "A preparer attestation cannot supply an ordinary reconciliation approval or independent approval actor.",
            ReconciliationBreakQueueTransitionErrorCode.MissingApproval, ["approvalActor", "approvalReference"]);
}
