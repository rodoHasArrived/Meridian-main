using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

/// <summary>Scoped immutable history, version-fenced writes and request-identity conflict detection.</summary>
public interface IIntercompanyAgreementStore
{
    /// <summary>Holds agreement authority unchanged through a ledger commit; unsupported stores fail closed.</summary>
    Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
        => Task.FromException<IAsyncDisposable>(new NotSupportedException("This agreement store cannot fence posting authority."));
    Task<IntercompanyAgreementHistoryDto?> GetAsync(string tenantId, Guid agreementId, CancellationToken ct = default);
    Task<IReadOnlyList<IntercompanyAgreementDto>> ListAsync(string tenantId, Guid? organizationId = null, CancellationToken ct = default);
    /// <summary>Appends version ExpectedVersion+1, or returns the original matching request result on retry.</summary>
    Task<IntercompanyAgreementDto> AppendAsync(string tenantId, long expectedVersion, IntercompanyAgreementEventDto item, CancellationToken ct = default);
    /// <summary>Builds the exact event after locking current and peer agreement authority, then commits without releasing that lease.</summary>
    Task<IntercompanyAgreementDto> MutateAsync(string tenantId, Guid agreementId, Guid requestId, string requestDigest,
        Func<IntercompanyAgreementHistoryDto?, CancellationToken, Task<IntercompanyAgreementEventDto>> build,
        CancellationToken ct = default)
        => Task.FromException<IntercompanyAgreementDto>(new NotSupportedException("This agreement store cannot atomically validate a mutation."));
}
