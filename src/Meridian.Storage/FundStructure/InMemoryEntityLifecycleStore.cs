using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

/// <summary>Immutable local history with shared reader/exclusive writer leases.</summary>
public sealed class InMemoryEntityLifecycleStore : IEntityLifecycleStore
{
    private readonly string _identity = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, List<EntityLifecycleRevisionDto>> _history = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private string Key(string tenantId, Guid entityId) => $"{_identity}:{NormalizeTenant(tenantId)}:{entityId:D}";
    internal static string NormalizeTenant(string tenantId)
        => !string.IsNullOrWhiteSpace(tenantId) && !tenantId.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
            ? tenantId.Trim().ToLowerInvariant() : throw new InvalidOperationException("An explicit tenant is required for lifecycle history.");
    private static EntityLifecycleRevisionDto Copy(EntityLifecycleRevisionDto value) => value with { Timeline = value.Timeline.ToArray() };

    public Task<EntityLifecycleRevisionDto?> GetLatestAsync(string tenantId, Guid entityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_history.TryGetValue(Key(tenantId, entityId), out var rows) ? Copy(rows[^1]) : null);
    }

    public Task<EntityLifecycleRevisionDto?> GetRevisionAsync(string tenantId, Guid entityId, long revision, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_history.TryGetValue(Key(tenantId, entityId), out var rows)
            ? rows.Where(row => row.Revision == revision).Select(Copy).SingleOrDefault() : null);
    }

    public Task<IReadOnlyList<EntityLifecycleRevisionDto>> GetHistoryAsync(string tenantId, Guid entityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<EntityLifecycleRevisionDto>>(_history.TryGetValue(Key(tenantId, entityId), out var rows)
            ? rows.Select(Copy).ToArray() : []);
    }

    public async Task<EntityLifecycleRevisionDto> AppendAsync(EntityLifecycleRevisionDto revision, long expectedRevision, CancellationToken ct = default)
    {
        var key = Key(revision.TenantId, revision.EntityId);
        await using var graphLease = await EntityLifecycleLeaseGate.For(_identity + ":successor-graph").EnterWriteAsync(ct).ConfigureAwait(false);
        await using var lease = await EntityLifecycleLeaseGate.For(key).EnterWriteAsync(ct).ConfigureAwait(false);
        await EntityLifecycleStoreRules.ValidateSuccessorsAsync(revision, id => GetLatestAsync(revision.TenantId, id, ct)).ConfigureAwait(false);
        lock (_gate)
        {
            _history.TryGetValue(key, out var rows);
            EntityLifecycleRevisionIntegrity.ValidateRevision(revision, rows?.LastOrDefault(), expectedRevision);
            rows ??= [];
            rows.Add(Copy(revision));
            _history[key] = rows;
            return Copy(revision);
        }
    }

    public Task<IAsyncDisposable> AcquireReadLeaseAsync(string tenantId, IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
        => EntityLifecycleLeaseGate.AcquireManyAsync(entityIds.Select(id => Key(tenantId, id)), ct);
}
