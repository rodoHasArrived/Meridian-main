using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

public sealed record IntercompanyAgreementStoreState(
    Guid AgreementId, long AggregateVersion, IReadOnlyList<IntercompanyAgreementProposalDto> Proposals,
    IReadOnlyList<IntercompanyAgreementRevisionDto> Revisions, IReadOnlyList<IntercompanyAgreementAuditDto> History,
    IReadOnlyList<IntercompanyAgreementCommandReceiptDto> Receipts);

/// <summary>Callback runs under exclusive agreement authority and stable legal-entity authority until commit.</summary>
public interface IIntercompanyAgreementStoreTransaction
{
    IntercompanyAgreementStoreState? State { get; }
    Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid entityId, CancellationToken ct = default);
    void Save(IntercompanyAgreementStoreState state);
}

public interface IIntercompanyAgreementPostingLease : IAsyncDisposable
{
    IReadOnlyList<IntercompanyAgreementStoreState> Agreements { get; }
    Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid entityId, CancellationToken ct = default);
}

public interface IIntercompanyAgreementStore
{
    Task<T> ExecuteAsync<T>(string tenantId, Guid agreementId,
        Func<IIntercompanyAgreementStoreTransaction, CancellationToken, Task<T>> operation, CancellationToken ct = default);
    Task<T> ExecuteAsync<T>(string tenantId, Guid agreementId, LegalEntityReadAuthorityProof structureAuthority,
        Func<IIntercompanyAgreementStoreTransaction, CancellationToken, Task<T>> operation, CancellationToken ct = default)
        => ExecuteAsync(tenantId, agreementId, operation, ct);
    Task<IntercompanyAgreementStoreState?> GetAsync(string tenantId, Guid agreementId, CancellationToken ct = default);
    Task<IReadOnlyList<IntercompanyAgreementStoreState>> ListAsync(string tenantId, CancellationToken ct = default);
    Task<IIntercompanyAgreementPostingLease> AcquirePostingLeaseAsync(string tenantId, IReadOnlyList<Guid> agreementIds, CancellationToken ct = default);
    Task<IIntercompanyAgreementPostingLease> AcquirePostingLeaseAsync(string tenantId, IReadOnlyList<Guid> agreementIds,
        LegalEntityReadAuthorityProof structureAuthority, CancellationToken ct = default)
        => AcquirePostingLeaseAsync(tenantId, agreementIds, ct);
}
