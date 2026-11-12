using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

/// <summary>Synthetic/local store with the same CAS and idempotency semantics as durable storage.</summary>
public sealed class InMemoryIntercompanyAgreementStore : IIntercompanyAgreementStore
{
    private readonly Dictionary<(string Tenant, Guid Id), string> _histories = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _authority = new(1, 1);

    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    {
        await _authority.WaitAsync(ct).ConfigureAwait(false);
        return new IntercompanyAuthorityLease(() => { _authority.Release(); return ValueTask.CompletedTask; });
    }

    public Task<IntercompanyAgreementHistoryDto?> GetAsync(string tenantId, Guid agreementId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_histories.TryGetValue((IntercompanyAgreementStoreRules.Tenant(tenantId), agreementId), out var json)
            ? IntercompanyAgreementStoreRules.Deserialize(json) : null);
    }

    public Task<IReadOnlyList<IntercompanyAgreementDto>> ListAsync(string tenantId, Guid? organizationId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var tenant = IntercompanyAgreementStoreRules.Tenant(tenantId);
        lock (_gate) return Task.FromResult<IReadOnlyList<IntercompanyAgreementDto>>(_histories.Where(x => x.Key.Tenant == tenant)
            .Select(x => IntercompanyAgreementStoreRules.Deserialize(x.Value).Current)
            .Where(x => organizationId is null || x.OrganizationId == organizationId).OrderBy(x => x.FacilityReference).ThenBy(x => x.AgreementId).ToArray());
    }

    public Task<IntercompanyAgreementDto> AppendAsync(string tenantId, long expectedVersion, IntercompanyAgreementEventDto item, CancellationToken ct = default)
        => MutateAsync(tenantId, item.Snapshot.AgreementId, item.RequestId, item.RequestDigest, (old, _) =>
        {
            if ((old?.Current.Version ?? 0) != expectedVersion) throw new InvalidOperationException("The agreement version changed.");
            return Task.FromResult(item);
        }, ct);

    public async Task<IntercompanyAgreementDto> MutateAsync(string tenantId, Guid agreementId, Guid requestId, string requestDigest,
        Func<IntercompanyAgreementHistoryDto?, CancellationToken, Task<IntercompanyAgreementEventDto>> build, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var authority = await AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
        var key = (IntercompanyAgreementStoreRules.Tenant(tenantId), agreementId);
        IntercompanyAgreementHistoryDto? old;
        lock (_gate) old = _histories.TryGetValue(key, out var retained) ? IntercompanyAgreementStoreRules.Deserialize(retained) : null;
        if (IntercompanyAgreementStoreRules.Retry(old, requestId, requestDigest) is { } retry) return retry;
        var item = await build(old, ct).ConfigureAwait(false);
        if (item.Snapshot.AgreementId != agreementId || item.RequestId != requestId || item.RequestDigest != requestDigest)
            throw new InvalidOperationException("The agreement mutation changed its locked identity.");
        lock (_gate)
        {
            var result = IntercompanyAgreementStoreRules.Append(old, old?.Current.Version ?? 0, item);
            if (result.Changed) _histories[key] = IntercompanyAgreementStoreRules.Serialize(result.History);
            return result.Result;
        }
    }
}
