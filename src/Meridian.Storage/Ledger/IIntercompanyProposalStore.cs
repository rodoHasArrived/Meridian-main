using Meridian.Contracts.Ledger;

namespace Meridian.Storage.Ledger;

public interface IIntercompanyProposalStore
{
    Task<IReadOnlyList<IntercompanyProposalDto>> ListAsync(string tenantId, Guid sourceBookId, CancellationToken ct = default);
    Task<IntercompanyProposalDto?> GetAsync(string tenantId, Guid proposalId, CancellationToken ct = default);
    /// <summary>Trusted background-only claim; tenant identity comes from the retained source row.</summary>
    Task<IReadOnlyList<IntercompanyProposalDto>> ClaimAsync(string workerId, int limit, TimeSpan lease, CancellationToken ct = default);
    Task CompleteAsync(string tenantId, Guid proposalId, string claimToken, Guid targetBookId,
        Guid targetJournalId, CancellationToken ct = default);
    Task FailAsync(string tenantId, Guid proposalId, string claimToken, string error, CancellationToken ct = default);
    Task LinkAsync(string tenantId, Guid proposalId, LinkIntercompanyCounterpartRequest request,
        string actor, CancellationToken ct = default);
    Task LinkRejectedDraftAsync(string tenantId, Guid proposalId, LinkIntercompanyCounterpartRequest request,
        string actor, RejectedIntercompanyDraftEvidenceDto rejectedDraft, CancellationToken ct = default)
        => Task.FromException(new NotSupportedException("This proposal store cannot retain rejected-draft replacement evidence."));
    Task RetryAsync(string tenantId, Guid proposalId, RetryIntercompanyProposalRequest request,
        string actor, CancellationToken ct = default);
}
