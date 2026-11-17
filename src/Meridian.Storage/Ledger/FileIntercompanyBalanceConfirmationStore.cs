using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Durable single-process local counterpart of the append-only PostgreSQL store: every version is retained, one
/// confirmation exists per tenant, company, profile and date, and a stale expected version is refused.
/// </summary>
public sealed class FileIntercompanyBalanceConfirmationStore(string path, IFundScopeTenantAccessor tenants) : IIntercompanyBalanceConfirmationStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = Gates.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));

    public async Task<IntercompanyBalanceConfirmationDto?> GetAsync(Guid confirmationId, string? companyId, CancellationToken ct = default)
    {
        var tenant = RequireTenant();
        return Latest((await ReadAsync(ct).ConfigureAwait(false)).Where(item => item.ConfirmationId == confirmationId), tenant, companyId);
    }

    public async Task<IntercompanyBalanceConfirmationDto?> FindAsync(Guid accountingArrangementId, DateOnly asOf, string? companyId, CancellationToken ct = default)
    {
        var tenant = RequireTenant();
        return Latest((await ReadAsync(ct).ConfigureAwait(false))
            .Where(item => item.AccountingArrangementId == accountingArrangementId && item.AsOf == asOf), tenant, companyId);
    }

    public async Task<IntercompanyBalanceConfirmationDto> AppendAsync(IntercompanyBalanceConfirmationDto next, long expectedVersion, CancellationToken ct = default)
    {
        if (!string.Equals(next.TenantId, RequireTenant(), StringComparison.Ordinal))
            throw new TenantScopeRejectedException("balance confirmation for another tenant");
        if (next.Version != expectedVersion + 1)
            throw new InvalidOperationException("A confirmation version must directly follow the expected version.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadAsync(ct).ConfigureAwait(false);
            var scope = all.Where(item => item.TenantId == next.TenantId && item.CompanyId == next.CompanyId
                && item.AccountingArrangementId == next.AccountingArrangementId && item.AsOf == next.AsOf).ToArray();
            if (scope.FirstOrDefault(item => item.Version == next.Version) is { } retained)
            {
                // An exact replay of a retained version is idempotent; anything else lost a race.
                if (Serialize([retained]) == Serialize([next]))
                    return retained;
                throw new InvalidOperationException("The confirmation changed since it was read. Reload it and act on the current version.");
            }
            var current = scope.MaxBy(item => item.Version);
            if ((current?.Version ?? 0) != expectedVersion || (current is not null && current.ConfirmationId != next.ConfirmationId))
                throw new InvalidOperationException("The confirmation changed since it was read. Reload it and act on the current version.");
            await AtomicFileWriter.WriteAsync(path, Serialize([.. all, next]), ct).ConfigureAwait(false);
            return next;
        }
        finally { _gate.Release(); }
    }

    private static IntercompanyBalanceConfirmationDto? Latest(IEnumerable<IntercompanyBalanceConfirmationDto> items, string tenant, string? company)
        => items.Where(item => item.TenantId == tenant && item.CompanyId == company).MaxBy(item => item.Version);

    private string RequireTenant()
    {
        var tenant = tenants.ResolveCallerTenant() ?? FundScopeTenantAuthority.CurrentTenantId;
        if (!TenantReadPredicate.IsResolvedTenant(tenant))
            throw new TenantScopeRejectedException("balance confirmation without a concrete tenant");
        return tenant!.Trim();
    }

    private async Task<IReadOnlyList<IntercompanyBalanceConfirmationDto>> ReadAsync(CancellationToken ct)
        => File.Exists(path) ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false),
            IntercompanyBalanceConfirmationJsonContext.Default.IReadOnlyListIntercompanyBalanceConfirmationDto)
            ?? throw new InvalidOperationException("Invalid retained balance confirmation history.") : [];

    private static string Serialize(IReadOnlyList<IntercompanyBalanceConfirmationDto> values)
        => JsonSerializer.Serialize(values, IntercompanyBalanceConfirmationJsonContext.Default.IReadOnlyListIntercompanyBalanceConfirmationDto);
}
