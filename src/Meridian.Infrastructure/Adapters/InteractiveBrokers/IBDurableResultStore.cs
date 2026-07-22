using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.ProviderSdk;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>A persisted, request-correlated IB result.  Tenant is part of the durable key.</summary>
public sealed record IBDurableResult(string TenantId, ProviderDataRequestReadModel Request, IBDataLineage? Lineage);

/// <summary>Durable query seam; callers must always supply the tenant they are authorized to read.</summary>
public interface IBDurableResultStore
{
    void Upsert(string tenantId, ProviderDataRequestReadModel request, IBDataLineage? lineage);
    IReadOnlyList<IBDurableResult> Get(string tenantId, string? capability = null, string? accountId = null, string? modelAccountId = null);
}

/// <summary>
/// Small atomic JSON result store used until an installation supplies a database projection. It is
/// deliberately keyed by tenant + request id, so a process restart never turns an in-memory IB
/// callback cache into a cross-tenant read surface.
/// </summary>
public sealed class JsonIBDurableResultStore : IBDurableResultStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, IBDurableResult>? _results;

    public JsonIBDurableResultStore(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

    public void Upsert(string tenantId, ProviderDataRequestReadModel request, IBDataLineage? lineage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var results = Load();
            results[$"{tenantId}:{request.RequestId}"] = new IBDurableResult(tenantId, request, lineage);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(results.Values));
            File.Move(temporary, _path, overwrite: true);
        }
    }

    public IReadOnlyList<IBDurableResult> Get(string tenantId, string? capability = null, string? accountId = null, string? modelAccountId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        lock (_gate)
            return Load().Values.Where(x => x.TenantId == tenantId)
                .Where(x => capability is null || string.Equals(x.Request.Capability, capability, StringComparison.OrdinalIgnoreCase))
                .Where(x => accountId is null || string.Equals(x.Request.AccountId, accountId, StringComparison.Ordinal))
                .Where(x => modelAccountId is null || string.Equals(x.Request.ModelAccountId, modelAccountId, StringComparison.Ordinal))
                .OrderByDescending(x => x.Request.UpdatedAt).ToArray();
    }

    private Dictionary<string, IBDurableResult> Load()
    {
        if (_results is not null) return _results;
        if (!File.Exists(_path)) return _results = new();
        var values = JsonSerializer.Deserialize<List<IBDurableResult>>(File.ReadAllText(_path)) ?? [];
        return _results = values.ToDictionary(x => $"{x.TenantId}:{x.Request.RequestId}", StringComparer.Ordinal);
    }
}

/// <summary>Materializes callback updates without treating a submitted request as live entitlement.</summary>
public sealed class IBDataResultMaterializer
{
    private readonly IBDurableResultStore _store;
    private readonly Func<string> _tenantResolver;
    public IBDataResultMaterializer(IBDurableResultStore store, Func<string>? tenantResolver = null)
    { _store = store; _tenantResolver = tenantResolver ?? (() => "system"); }
    public void Materialize(ProviderDataRequestReadModel request, IBDataLineage? lineage)
        => _store.Upsert(_tenantResolver(), request, lineage);
}
