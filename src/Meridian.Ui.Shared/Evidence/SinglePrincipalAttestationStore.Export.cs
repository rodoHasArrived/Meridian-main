using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

public sealed record PreparerAttestationProofFile(byte[] Content, string ContentHashSha256,
    DateTimeOffset RecordedAt, string FileName);

public sealed partial class SinglePrincipalAttestationStore
{
    /// <summary>
    /// Reproduces historical assertion evidence without granting current authority. Current
    /// read permission is still required; withdrawal and revocation do not rewrite these bytes.
    /// </summary>
    public async Task<PreparerAttestationProofFile> GetReceiptExportAsync(
        SinglePrincipalAttestationScopeDto scope,
        SinglePrincipalAttestationAuthority authority,
        Guid receiptId,
        CancellationToken ct = default)
    {
        RequireRead(authority);
        var normalized = NormalizeScope(scope);
        await using var lease = await AcquireAsync(normalized, ct).ConfigureAwait(false);
        var state = await ReadAsync(normalized, ct).ConfigureAwait(false);
        var receipt = state.Receipts.SingleOrDefault(item => item.ReceiptId == receiptId)
            ?? throw Denied("receipt_not_found", "The receipt was not found in this scope.");
        var receiptResult = state.Commands.Single(item => item.CommandId == receipt.CommandId).Result;
        var declarationResults = state.Commands.Select(item => item.Result).Where(result =>
            result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("declarationId", out var declarationId)
            && declarationId.TryGetGuid(out var id) && id == receipt.DeclarationId
            && result.TryGetProperty("revision", out var revision)
            && revision.TryGetInt64(out var version) && version == receipt.DeclarationRevision
            && result.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True).ToArray();
        if (declarationResults.Length != 1)
            throw Denied("state_unavailable", "The assertion's original declaration evidence is unavailable.");

        // Format 1 intentionally uses only retained historical operands. Do not add current
        // declaration state, current time, revocation or reevaluated support to this payload.
        var proof = new PreparerAttestationProofExportDto(1, "PreparerAttestationEvidence",
            receipt.Disclosure, receipt.RecordedAt, PreparerAttestationApprovalBoundary.ReceiptReference(receiptId),
            receiptResult.Clone(), declarationResults[0].Clone(),
            "Non-posting preparer assertion only. This assertion is not independent review, ordinary approval, posting authority or Verified Coverage credit. Current applicability must be checked separately.");
        var content = JsonSerializer.SerializeToUtf8Bytes(proof, JsonOptions);
        return new(content, Sha256Digest.Compute(content), receipt.RecordedAt,
            $"preparer-assertion-{receipt.ReceiptId:D}.json");
    }
}
