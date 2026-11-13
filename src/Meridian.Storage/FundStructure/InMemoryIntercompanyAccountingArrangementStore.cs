using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

/// <summary>Synthetic/local store with the same CAS and idempotency semantics as durable storage.</summary>
public sealed class InMemoryIntercompanyAccountingArrangementStore : IIntercompanyAccountingArrangementStore
{
    private readonly Dictionary<(string Tenant, Guid Id), string> _histories = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _authority = new(1, 1);

    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    {
        await _authority.WaitAsync(ct).ConfigureAwait(false);
        return new IntercompanyAccountingArrangementAuthorityLease(() => { _authority.Release(); return ValueTask.CompletedTask; });
    }

    public Task<IntercompanyAccountingArrangementHistoryDto?> GetAsync(string tenantId, Guid agreementId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_histories.TryGetValue((IntercompanyAccountingArrangementStoreRules.Tenant(tenantId), agreementId), out var json)
            ? IntercompanyAccountingArrangementStoreRules.Deserialize(json) : null);
    }

    public Task<IReadOnlyList<IntercompanyAccountingArrangementDto>> ListAsync(string tenantId, Guid? organizationId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var tenant = IntercompanyAccountingArrangementStoreRules.Tenant(tenantId);
        lock (_gate)
            return Task.FromResult<IReadOnlyList<IntercompanyAccountingArrangementDto>>(_histories.Where(x => x.Key.Tenant == tenant)
            .Select(x => IntercompanyAccountingArrangementStoreRules.Deserialize(x.Value).Current)
            .Where(x => organizationId is null || x.OrganizationId == organizationId).OrderBy(x => x.ArrangementReference).ThenBy(x => x.ArrangementId).ToArray());
    }

    public Task<IntercompanyAccountingArrangementDto> AppendAsync(string tenantId, long expectedVersion, IntercompanyAccountingArrangementEventDto item, CancellationToken ct = default)
        => MutateAsync(tenantId, item.Snapshot.ArrangementId, item.RequestId, item.RequestDigest, (old, _) =>
        {
            if ((old?.Current.Version ?? 0) != expectedVersion)
                throw new InvalidOperationException("The agreement version changed.");
            return Task.FromResult(item);
        }, ct);

    public async Task<IntercompanyAccountingArrangementDto> MutateAsync(string tenantId, Guid agreementId, Guid requestId, string requestDigest,
        Func<IntercompanyAccountingArrangementHistoryDto?, CancellationToken, Task<IntercompanyAccountingArrangementEventDto>> build, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var authority = await AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false);
        var key = (IntercompanyAccountingArrangementStoreRules.Tenant(tenantId), agreementId);
        IntercompanyAccountingArrangementHistoryDto? old;
        lock (_gate)
            old = _histories.TryGetValue(key, out var retained) ? IntercompanyAccountingArrangementStoreRules.Deserialize(retained) : null;
        if (IntercompanyAccountingArrangementStoreRules.Retry(old, requestId, requestDigest) is { } retry)
            return retry;
        var item = await build(old, ct).ConfigureAwait(false);
        if (item.Snapshot.ArrangementId != agreementId || item.RequestId != requestId || item.RequestDigest != requestDigest)
            throw new InvalidOperationException("The agreement mutation changed its locked identity.");
        lock (_gate)
        {
            var result = IntercompanyAccountingArrangementStoreRules.Append(old, old?.Current.Version ?? 0, item);
            if (result.Changed)
                _histories[key] = IntercompanyAccountingArrangementStoreRules.Serialize(result.History);
            return result.Result;
        }
    }
}
