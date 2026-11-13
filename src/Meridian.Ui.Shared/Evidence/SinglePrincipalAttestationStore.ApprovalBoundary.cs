using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class SinglePrincipalAttestationStore : IPreparerAttestationReferenceClassifier
{
    /// <summary>
    /// Classifies exact identities only for denying their use as ordinary approval. This trusted
    /// server seam is not an operator read endpoint and never returns receipt contents or authority.
    /// Retained assertions remain preparer assertions after withdrawal or revocation.
    /// </summary>
    public async Task<bool> IsPreparerReceiptReferenceAsync(
        string tenantId,
        string companyId,
        string? reference,
        CancellationToken ct = default)
    {
        if (PreparerAttestationApprovalBoundary.IsReservedReference(reference))
            return true;
        if (!Guid.TryParse(reference?.Trim(), out var identity))
            return false;

        var scope = NormalizeScope(new SinglePrincipalAttestationScopeDto(tenantId, companyId));
        await using var lease = await AcquireAsync(scope, ct).ConfigureAwait(false);
        var state = await ReadAsync(scope, ct).ConfigureAwait(false);
        // CommandId/AssertionId are caller-selected idempotency identities, not authority
        // identities. Matching them would let a preparer relabel an unrelated real approval
        // merely by choosing the same GUID for a new assertion command.
        return state.Receipts.Any(receipt => receipt.ReceiptId == identity);
    }
}
