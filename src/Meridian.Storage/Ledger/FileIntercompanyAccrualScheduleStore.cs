using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Ledger;

/// <summary>Local durable schedule history: every version of every schedule, published atomically in one file.</summary>
public sealed class FileIntercompanyAccrualScheduleStore(string path, IFundScopeTenantAccessor tenants) : IIntercompanyAccrualScheduleStore
{
    private readonly string _path = Path.GetFullPath(path);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IntercompanyAccrualScheduleDto?> GetAsync(string tenantId, Guid scheduleId, CancellationToken ct = default)
    {
        var tenant = Scope(tenantId);
        return Current(await ReadAsync(ct).ConfigureAwait(false)).FirstOrDefault(item => item.ScheduleId == scheduleId
            && IntercompanyAccrualScheduleStoreRules.Tenant(item.TenantId) == tenant);
    }

    public async Task<IReadOnlyList<IntercompanyAccrualScheduleDto>> ListAsync(string tenantId, Guid? ledgerBookId = null, CancellationToken ct = default)
    {
        var tenant = Scope(tenantId);
        return Current(await ReadAsync(ct).ConfigureAwait(false))
            .Where(item => IntercompanyAccrualScheduleStoreRules.Tenant(item.TenantId) == tenant && (ledgerBookId is null || item.LedgerBookId == ledgerBookId))
            .OrderBy(item => item.ScheduleId).ToArray();
    }

    public async Task<IReadOnlyList<IntercompanyAccrualScheduleDto>> ListActiveAsync(CancellationToken ct = default)
        => Current(await ReadAsync(ct).ConfigureAwait(false)).Where(item => item.Status == IntercompanyAccrualScheduleStatusDto.Active)
            .OrderBy(item => item.ScheduleId).ToArray();

    public async Task<IntercompanyAccrualScheduleDto> AppendAsync(IntercompanyAccrualScheduleDto next, long expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        var tenant = Scope(next.TenantId);
        IntercompanyAccrualScheduleStoreRules.RequireTenant(tenant, next);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using var writer = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var versions = (await ReadAsync(ct).ConfigureAwait(false)).ToList();
            var current = Current(versions).FirstOrDefault(item => item.ScheduleId == next.ScheduleId);
            if (current is not null)
                IntercompanyAccrualScheduleStoreRules.RequireTenant(tenant, current);
            if (IntercompanyAccrualScheduleStoreRules.IsIdenticalRetry(current, next, expectedVersion))
                return current!;
            versions.Add(next);
            await AtomicFileWriter.WriteAsync(_path,
                JsonSerializer.Serialize(versions.ToArray(), IntercompanyAccrualScheduleJsonContext.Default.IntercompanyAccrualScheduleDtoArray), ct).ConfigureAwait(false);
            return next;
        }
        finally { _gate.Release(); }
    }

    private string Scope(string tenantId)
    {
        var tenant = IntercompanyAccrualScheduleStoreRules.Tenant(tenantId);
        if (tenants.ResolveCallerTenant() is { Length: > 0 } active && IntercompanyAccrualScheduleStoreRules.Tenant(active) != tenant)
            throw new UnauthorizedAccessException("The caller is scoped to a different tenant.");
        return tenant;
    }

    private async Task<IReadOnlyList<IntercompanyAccrualScheduleDto>> ReadAsync(CancellationToken ct)
        => File.Exists(_path)
            ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false),
                IntercompanyAccrualScheduleJsonContext.Default.IntercompanyAccrualScheduleDtoArray) ?? []
            : [];

    private static IEnumerable<IntercompanyAccrualScheduleDto> Current(IEnumerable<IntercompanyAccrualScheduleDto> versions)
        => versions.GroupBy(item => item.ScheduleId).Select(group => group.MaxBy(item => item.Version)!);
}
