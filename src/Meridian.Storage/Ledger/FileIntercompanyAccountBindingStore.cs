using System.Text.Json;
using System.Collections.Concurrent;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Ledger;

/// <summary>Single-process local/demo authority; PostgreSQL supplies transactional production fencing.</summary>
public sealed class FileIntercompanyAccountBindingStore(string path, IFundScopeTenantAccessor tenants,
    IAccountingConfigurationStore configuration, ILedgerJournalStore journals, IFundProfileTenancyRegistry ownership)
    : IIntercompanyAccountBindingStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = Gates.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));

    public async Task<IReadOnlyList<IntercompanyAccountBindingDto>> ListAsync(Guid ledgerBookId,
        DateOnly asOf, CancellationToken ct = default)
        => IntercompanyAccountBindingRules.Resolve(await HistoryAsync(ledgerBookId, ct).ConfigureAwait(false), asOf);

    public async Task<IReadOnlyList<IntercompanyAccountBindingDto>> HistoryAsync(Guid ledgerBookId, CancellationToken ct = default)
    {
        var (_, owner) = await RequireOwnerAsync(ledgerBookId, ct).ConfigureAwait(false);
        return (await ReadAsync(ct).ConfigureAwait(false)).Where(x => x.LedgerBookId == ledgerBookId &&
            Same(x.TenantId, owner.TenantId) && Same(Company(x.CompanyId), Company(owner.CompanyId))).ToArray();
    }

    public async Task<IntercompanyAccountBindingDto?> GetRevisionAsync(Guid revisionId, CancellationToken ct = default)
    {
        var tenant = RequireTenant();
        var revision = (await ReadAsync(ct).ConfigureAwait(false)).SingleOrDefault(x => x.RevisionId == revisionId && Same(x.TenantId, tenant));
        if (revision is null) return null;
        var (_, owner) = await RequireOwnerAsync(revision.LedgerBookId, ct).ConfigureAwait(false);
        if (!Same(Company(owner.CompanyId), Company(revision.CompanyId))) throw new TenantScopeRejectedException("foreign account binding company");
        return revision;
    }

    public async Task<IntercompanyAccountBindingDto> AppendAsync(IntercompanyAccountBindingDto revision,
        Guid? expectedPreviousRevisionId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (book, owner) = await RequireOwnerAsync(revision.LedgerBookId, ct).ConfigureAwait(false);
            if (!Same(revision.TenantId, owner.TenantId) || !Same(Company(revision.CompanyId), Company(owner.CompanyId)))
                throw new TenantScopeRejectedException("account binding differing from its authoritative book owner");
            var all = await ReadAsync(ct).ConfigureAwait(false);
            var retained = all.SingleOrDefault(x => x.RevisionId == revision.RevisionId);
            if (retained is not null)
            {
                if (Serialize([retained]) != Serialize([revision]) || retained.PreviousRevisionId != expectedPreviousRevisionId)
                    throw new InvalidOperationException("The account binding revision id was reused with different content.");
                return retained;
            }
            var history = all.Where(x => x.LedgerBookId == revision.LedgerBookId && Same(x.TenantId, owner.TenantId) &&
                Same(Company(x.CompanyId), Company(owner.CompanyId))).ToArray();
            IntercompanyAccountBindingRules.ValidateAppend(revision, expectedPreviousRevisionId, history);
            var workspace = await configuration.GetAsync(book.FundProfileId, book.LedgerBookId, ct,
                owner.TenantId, Company(owner.CompanyId)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Configure the book's scoped chart before retaining account bindings.");
            var node = workspace.ChartOfAccounts.SingleOrDefault(x => x.NodeId == revision.ChartAccountNodeId);
            if (node is null || node.Path != revision.AccountPath || node.AccountName != revision.AccountName ||
                node.AccountType != revision.AccountType || node.IntercompanyRole != revision.Role ||
                node.IsArchived != revision.IsArchived || node.Symbol is not null || node.FinancialAccountId is not null)
                throw new InvalidOperationException("The binding must match the exact current, unscoped chart node and configured role.");
            await AtomicFileWriter.WriteAsync(path, Serialize([.. all, revision]), ct).ConfigureAwait(false);
            return Clone(revision);
        }
        finally { _gate.Release(); }
    }

    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    { _ = RequireTenant(); await _gate.WaitAsync(ct).ConfigureAwait(false); return new Lease(_gate); }

    private async Task<(LedgerBookRecord Book, FundProfileOwnership Owner)> RequireOwnerAsync(Guid bookId, CancellationToken ct)
    {
        var tenant = RequireTenant();
        var book = await journals.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false)
            ?? throw new TenantScopeRejectedException("account bindings for an unavailable ledger book");
        var owner = await ownership.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
        if (owner is null || !owner.IsHeldBy(tenant)) throw new TenantScopeRejectedException("account bindings for an unowned or foreign book");
        return (book, owner);
    }
    private string RequireTenant()
    {
        var tenant = tenants.ResolveCallerTenant() ?? FundScopeTenantAuthority.CurrentTenantId;
        if (!TenantReadPredicate.IsResolvedTenant(tenant)) throw new TenantScopeRejectedException("account bindings without a concrete owning tenant");
        return tenant!.Trim();
    }
    private async Task<IReadOnlyList<IntercompanyAccountBindingDto>> ReadAsync(CancellationToken ct)
        => File.Exists(path) ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false),
            IntercompanyAccountContractsJsonContext.Default.IReadOnlyListIntercompanyAccountBindingDto)
            ?? throw new InvalidOperationException("Invalid retained account-binding history.") : [];
    private static string Serialize(IReadOnlyList<IntercompanyAccountBindingDto> rows)
        => JsonSerializer.Serialize(rows, IntercompanyAccountContractsJsonContext.Default.IReadOnlyListIntercompanyAccountBindingDto);
    private static IntercompanyAccountBindingDto Clone(IntercompanyAccountBindingDto row)
        => JsonSerializer.Deserialize(JsonSerializer.Serialize(row, IntercompanyAccountContractsJsonContext.Default.IntercompanyAccountBindingDto),
            IntercompanyAccountContractsJsonContext.Default.IntercompanyAccountBindingDto)!;
    private static string? Company(string? company) => string.Equals(company, "all", StringComparison.OrdinalIgnoreCase) ? null : company;
    private static bool Same(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    { public ValueTask DisposeAsync() { gate.Release(); return ValueTask.CompletedTask; } }
}
