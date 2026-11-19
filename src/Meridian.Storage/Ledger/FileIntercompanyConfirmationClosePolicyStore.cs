using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Ledger;

/// <summary>Local durable close-policy history: every version of every policy, published atomically in one file.</summary>
public sealed class FileIntercompanyConfirmationClosePolicyStore(string path, IFundScopeTenantAccessor tenants) : IIntercompanyConfirmationClosePolicyStore
{
    private readonly string _path = Path.GetFullPath(path);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IntercompanyConfirmationClosePolicyDto?> GetAsync(string tenantId, Guid policyId, CancellationToken ct = default)
    {
        var tenant = Scope(tenantId);
        return Current(await ReadAsync(ct).ConfigureAwait(false)).FirstOrDefault(item => item.PolicyId == policyId
            && IntercompanyConfirmationClosePolicyStoreRules.Tenant(item.TenantId) == tenant);
    }

    public async Task<IReadOnlyList<IntercompanyConfirmationClosePolicyDto>> ListAsync(string tenantId, Guid? ledgerBookId = null, CancellationToken ct = default)
    {
        var tenant = Scope(tenantId);
        return Current(await ReadAsync(ct).ConfigureAwait(false))
            .Where(item => IntercompanyConfirmationClosePolicyStoreRules.Tenant(item.TenantId) == tenant && (ledgerBookId is null || item.LedgerBookId == ledgerBookId))
            .OrderBy(item => item.PolicyId).ToArray();
    }

    public async Task<IntercompanyConfirmationClosePolicyDto> AppendAsync(IntercompanyConfirmationClosePolicyDto next, long expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        var tenant = Scope(next.TenantId);
        IntercompanyConfirmationClosePolicyStoreRules.RequireTenant(tenant, next);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using var writer = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var versions = (await ReadAsync(ct).ConfigureAwait(false)).ToList();
            var current = Current(versions).FirstOrDefault(item => item.PolicyId == next.PolicyId);
            if (current is not null)
                IntercompanyConfirmationClosePolicyStoreRules.RequireTenant(tenant, current);
            if (IntercompanyConfirmationClosePolicyStoreRules.IsIdenticalRetry(current, next, expectedVersion))
                return current!;
            versions.Add(next);
            await AtomicFileWriter.WriteAsync(_path,
                JsonSerializer.Serialize(versions.ToArray(), IntercompanyConfirmationClosePolicyJsonContext.Default.IntercompanyConfirmationClosePolicyDtoArray), ct).ConfigureAwait(false);
            return next;
        }
        finally { _gate.Release(); }
    }

    private string Scope(string tenantId)
    {
        var tenant = IntercompanyConfirmationClosePolicyStoreRules.Tenant(tenantId);
        if (tenants.ResolveCallerTenant() is { Length: > 0 } active && IntercompanyConfirmationClosePolicyStoreRules.Tenant(active) != tenant)
            throw new UnauthorizedAccessException("The caller is scoped to a different tenant.");
        return tenant;
    }

    private async Task<IReadOnlyList<IntercompanyConfirmationClosePolicyDto>> ReadAsync(CancellationToken ct)
        => File.Exists(_path)
            ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false),
                IntercompanyConfirmationClosePolicyJsonContext.Default.IntercompanyConfirmationClosePolicyDtoArray) ?? []
            : [];

    private static IEnumerable<IntercompanyConfirmationClosePolicyDto> Current(IEnumerable<IntercompanyConfirmationClosePolicyDto> versions)
        => versions.GroupBy(item => item.PolicyId).Select(group => group.MaxBy(item => item.Version)!);
}
