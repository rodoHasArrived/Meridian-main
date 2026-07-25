using Meridian.Infrastructure.Adapters.InteractiveBrokers;

namespace Meridian.Ui.Shared.Services;

/// <summary>Shared, tenant-scoped query service for typed, durable IB callback results.</summary>
public sealed class IBResultQueryService
{
    private readonly IBDurableResultStore _store;
    public IBResultQueryService(IBDurableResultStore store) => _store = store;
    public IReadOnlyList<IBDurableResult> Get(
        string tenantId,
        string companyId,
        string? family,
        string? accountId,
        string? modelAccountId)
        => _store.Get(tenantId, companyId, family, accountId, modelAccountId);
}
