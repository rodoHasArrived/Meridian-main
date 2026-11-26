using System.Text.Json;
using System.Collections.Concurrent;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Ledger;

/// <summary>Durable single-process local counterpart of the append-only PostgreSQL store.</summary>
public sealed class FileLegacyIntercompanyAttributionStore(string path, IFundScopeTenantAccessor tenants,
    LegacyIntercompanyAttributionValidator validator) : ILegacyIntercompanyAttributionStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = Gates.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));
    public async Task<IReadOnlyList<LegacyIntercompanyAttributionDto>> ListAsync(Guid ledgerBookId, CancellationToken ct = default)
        => LegacyIntercompanyAttributionRules.Resolve(await HistoryAsync(ledgerBookId, ct).ConfigureAwait(false));

    public async Task<IReadOnlyList<LegacyIntercompanyAttributionDto>> HistoryAsync(Guid ledgerBookId, CancellationToken ct = default)
    {
        var tenant = RequireTenant();
        // Atomic replacement makes concurrent readers see one complete retained history.
        return (await ReadAsync(ct).ConfigureAwait(false)).Where(x => x.LedgerBookId == ledgerBookId && x.TenantId == tenant).ToArray();
    }

    public async Task<LegacyIntercompanyAttributionDto> AppendAsync(LegacyIntercompanyAttributionDto attribution,
        Guid? expectedPreviousAttributionId, CancellationToken ct = default)
    {
        if (attribution.TenantId != RequireTenant())
            throw new TenantScopeRejectedException("foreign legacy attribution");
        await using var authority = await validator.AcquireAuthorityLeaseAsync(attribution, ct).ConfigureAwait(false);
        await validator.ValidateAsync(attribution, ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadAsync(ct).ConfigureAwait(false);
            var old = all.SingleOrDefault(x => x.AttributionId == attribution.AttributionId);
            if (old is not null)
            {
                if (Serialize([old]) != Serialize([attribution]) || old.PreviousAttributionId != expectedPreviousAttributionId)
                    throw new InvalidOperationException("The attribution id was reused with different reviewed facts.");
                return old;
            }
            LegacyIntercompanyAttributionRules.ValidateAppend(attribution, expectedPreviousAttributionId,
                all.Where(x => x.LedgerBookId == attribution.LedgerBookId && x.TenantId == attribution.TenantId).ToArray());
            await AtomicFileWriter.WriteAsync(path, Serialize([.. all, attribution]), ct).ConfigureAwait(false);
            return attribution;
        }
        finally { _gate.Release(); }
    }
    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    { _ = RequireTenant(); await _gate.WaitAsync(ct).ConfigureAwait(false); return new Lease(_gate); }
    private string RequireTenant()
    {
        var tenant = tenants.ResolveCallerTenant() ?? FundScopeTenantAuthority.CurrentTenantId;
        if (!TenantReadPredicate.IsResolvedTenant(tenant))
            throw new TenantScopeRejectedException("legacy attribution without a concrete tenant");
        return tenant!.Trim();
    }
    private async Task<IReadOnlyList<LegacyIntercompanyAttributionDto>> ReadAsync(CancellationToken ct)
        => File.Exists(path) ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false),
            LegacyIntercompanyAttributionJsonContext.Default.IReadOnlyListLegacyIntercompanyAttributionDto)
            ?? throw new InvalidOperationException("Invalid retained legacy attribution history.") : [];
    private static string Serialize(IReadOnlyList<LegacyIntercompanyAttributionDto> values)
        => JsonSerializer.Serialize(values, LegacyIntercompanyAttributionJsonContext.Default.IReadOnlyListLegacyIntercompanyAttributionDto);
    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    { public ValueTask DisposeAsync() { gate.Release(); return ValueTask.CompletedTask; } }
}
