using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>Durable authority for commitment revisions, frozen call plans and atomic posting receipts.</summary>
public interface IPrivateCapitalStore
{
    Task<IReadOnlyList<PrivateCapitalCommitmentDto>> ListCommitmentsAsync(PrivateCapitalScopeDto scope, CancellationToken ct = default);
    Task<PrivateCapitalCommitmentDto?> GetCommitmentAsync(PrivateCapitalScopeDto scope, string commitmentId, int? revision = null, CancellationToken ct = default);
    Task<PrivateCapitalCommitmentDto> SaveCommitmentDraftAsync(PrivateCapitalCommitmentDto draft, int expectedRevision, CancellationToken ct = default);
    Task<PrivateCapitalCommitmentDto> ApproveCommitmentAsync(PrivateCapitalScopeDto scope, string commitmentId, int revision, string actor, DateTimeOffset approvedAtUtc, CancellationToken ct = default);
    Task<IReadOnlyList<PrivateCapitalCallDto>> ListCallsAsync(PrivateCapitalScopeDto scope, CancellationToken ct = default);
    Task<PrivateCapitalCallDto?> GetCallAsync(PrivateCapitalScopeDto scope, string callId, CancellationToken ct = default);
    Task<PrivateCapitalCallDto> SaveCallAsync(PrivateCapitalCallDto call, CancellationToken ct = default);
    Task<IReadOnlyList<PrivateCapitalPostingReceiptDto>> ListPostingReceiptsAsync(PrivateCapitalScopeDto scope, CancellationToken ct = default);
    Task<bool> HasUnreconciledLegacyCapitalAsync(PrivateCapitalScopeDto scope, string investorId, CancellationToken ct = default);
}
