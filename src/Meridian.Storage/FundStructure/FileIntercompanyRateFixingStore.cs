using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Archival;

namespace Meridian.Storage.FundStructure;

/// <summary>Local durable fixing history: every version of every fixing, published atomically per tenant.</summary>
public sealed class FileIntercompanyRateFixingStore(string rootDirectory, IFundScopeTenantAccessor tenants) : IIntercompanyRateFixingStore
{
    private readonly string _root = Path.GetFullPath(rootDirectory);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string PathFor(string tenant) => Path.Combine(_root, Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(tenant)).ToLowerInvariant() + ".json");

    public async Task<IntercompanyRateFixingDto?> GetAsync(string tenantId, Guid fixingId, CancellationToken ct = default)
    {
        var tenant = Scope(tenantId);
        return Current(await ReadAsync(tenant, ct).ConfigureAwait(false)).FirstOrDefault(item => item.FixingId == fixingId);
    }

    public async Task<IReadOnlyList<IntercompanyRateFixingDto>> ListAsync(string tenantId, string indexReference, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        var tenant = Scope(tenantId);
        return Current(await ReadAsync(tenant, ct).ConfigureAwait(false))
            .Where(item => item.IndexReference == indexReference && item.FixingDate >= from && item.FixingDate <= to)
            .OrderBy(item => item.FixingDate).ToArray();
    }

    public async Task<IntercompanyRateFixingDto> AppendAsync(IntercompanyRateFixingDto next, long expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        var tenant = Scope(next.TenantId);
        IntercompanyRateFixingStoreRules.RequireTenant(tenant, next);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root);
            await using var writer = new FileStream(Path.Combine(_root, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var versions = (await ReadAsync(tenant, ct).ConfigureAwait(false)).ToList();
            var current = Current(versions).FirstOrDefault(item => item.FixingId == next.FixingId);
            if (IntercompanyRateFixingStoreRules.IsIdenticalRetry(current, next, expectedVersion))
                return current!;
            versions.Add(next);
            await AtomicFileWriter.WriteAsync(PathFor(tenant),
                JsonSerializer.Serialize(versions.ToArray(), IntercompanyRateFixingJsonContext.Default.IntercompanyRateFixingDtoArray), ct).ConfigureAwait(false);
            return next;
        }
        finally { _gate.Release(); }
    }

    private string Scope(string tenantId)
    {
        var tenant = IntercompanyRateFixingStoreRules.Tenant(tenantId);
        if (tenants.ResolveCallerTenant() is { Length: > 0 } active && !string.Equals(IntercompanyRateFixingStoreRules.Tenant(active), tenant, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The caller is scoped to a different tenant.");
        return tenant;
    }

    private async Task<IReadOnlyList<IntercompanyRateFixingDto>> ReadAsync(string tenant, CancellationToken ct)
    {
        var path = PathFor(tenant);
        if (!File.Exists(path))
            return [];
        var versions = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false),
            IntercompanyRateFixingJsonContext.Default.IntercompanyRateFixingDtoArray) ?? [];
        foreach (var item in versions)
            IntercompanyRateFixingStoreRules.RequireTenant(tenant, item);
        return versions;
    }

    private static IEnumerable<IntercompanyRateFixingDto> Current(IEnumerable<IntercompanyRateFixingDto> versions)
        => versions.GroupBy(item => item.FixingId).Select(group => group.MaxBy(item => item.Version)!);
}
