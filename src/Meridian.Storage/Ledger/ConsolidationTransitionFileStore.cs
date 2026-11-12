using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Archival;
using Meridian.Storage.FundStructure;

namespace Meridian.Storage.Ledger;

/// <summary>Atomic single-process local/demo persistence; production uses PostgreSQL fences.</summary>
public sealed class ConsolidationTransitionFileStore : IConsolidationTransitionStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PersistenceGates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string _path;
    private readonly IFundScopeTenantAccessor _tenants;
    private readonly ILedgerJournalStore _journals;
    private readonly IFundProfileTenancyRegistry _owners;
    private readonly SemaphoreSlim _persistence;
    private readonly AsyncLocal<int> _writerDepth = new();

    public ConsolidationTransitionFileStore(string path, IFundScopeTenantAccessor tenants,
        ILedgerJournalStore journals, IFundProfileTenancyRegistry owners)
    {
        _path = Path.GetFullPath(path);
        _tenants = tenants;
        _journals = journals;
        _owners = owners;
        _persistence = PersistenceGates.GetOrAdd(_path, _ => new(1, 1));
    }

    private string Tenant() => ConsolidationTransitionRules.RequireTenant(_tenants.ResolveCallerTenant() ?? FundScopeTenantAuthority.CurrentTenantId);

    public Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    {
        _ = Tenant();
        return _writerDepth.Value > 0 ? Task.FromResult<IAsyncDisposable>(EmptyLedgerAuthorityLease.Instance)
            : EntityLifecycleLeaseGate.For("consolidation-transition:" + _path).EnterReadAsync(ct);
    }

    public async Task<IReadOnlyList<ConsolidationTransitionReceiptDto>> ListAsync(Guid organizationId, Guid ownershipRootId, CancellationToken ct = default)
    {
        var tenant = Tenant();
        var state = await ReadAsync(ct).ConfigureAwait(false);
        var rows = state.Receipts.Where(row => row.TenantId == tenant && row.Request.OrganizationId == organizationId
            && row.Request.OwnershipRootId == ownershipRootId).OrderBy(row => row.RootVersion).ToArray();
        foreach (var row in rows) await RequireBooksAsync(row.Request, tenant, row.CompanyId, ct).ConfigureAwait(false);
        return rows;
    }

    public async Task<ConsolidationTransitionReceiptDto?> GetAsync(Guid transitionId, CancellationToken ct = default)
    {
        var tenant = Tenant();
        var row = (await ReadAsync(ct).ConfigureAwait(false)).Receipts.SingleOrDefault(item => item.TransitionId == transitionId && item.TenantId == tenant);
        if (row is not null) await RequireBooksAsync(row.Request, tenant, row.CompanyId, ct).ConfigureAwait(false);
        return row;
    }

    public async Task<ConsolidationTransitionPreviewDto?> GetPreviewAsync(Guid previewId, CancellationToken ct = default)
    {
        var tenant = Tenant();
        var row = (await ReadAsync(ct).ConfigureAwait(false)).Previews.SingleOrDefault(item => item.PreviewId == previewId && item.TenantId == tenant);
        if (row is not null) await RequireBooksAsync(row.Request, tenant, row.CompanyId, ct).ConfigureAwait(false);
        return row;
    }

    public async Task<ConsolidationTransitionPreviewDto> RetainPreviewAsync(ConsolidationTransitionPreviewDto preview, CancellationToken ct = default)
    {
        ConsolidationTransitionRules.ValidatePreview(preview);
        var tenant = Tenant();
        if (tenant != preview.TenantId) throw new TenantScopeRejectedException("foreign transition preview");
        await _persistence.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RequireBooksAsync(preview.Request, tenant, preview.CompanyId, ct).ConfigureAwait(false);
            var state = await ReadAsync(ct).ConfigureAwait(false);
            var existing = state.Previews.SingleOrDefault(item => item.PreviewId == preview.PreviewId);
            if (existing is not null)
            {
                if (ConsolidationTransitionPostgresStore.SerializePreview(existing)
                    != ConsolidationTransitionPostgresStore.SerializePreview(preview with { PreparedAt = existing.PreparedAt }))
                    throw new InvalidOperationException("The transition preview identity was reused with different operands.");
                return existing;
            }
            await WriteAsync(state with { Previews = [.. state.Previews, preview] }, ct).ConfigureAwait(false);
            return ConsolidationTransitionPostgresStore.Clone(preview);
        }
        finally { _persistence.Release(); }
    }

    public Task<ConsolidationTransitionReceiptDto> AppendAsync(ConsolidationTransitionReceiptDto receipt, CancellationToken ct = default)
        => AppendValidatedAsync(receipt, _ => Task.CompletedTask, ct);

    public Task<ConsolidationTransitionReceiptDto> AppendValidatedAsync(ConsolidationTransitionReceiptDto receipt,
        Func<CancellationToken, Task> revalidate, CancellationToken ct = default)
        => AppendValidatedWithAuthorityAsync(receipt,
            _ => Task.FromResult<IAsyncDisposable>(EmptyLedgerAuthorityLease.Instance), revalidate, ct);

    public async Task<ConsolidationTransitionReceiptDto> AppendValidatedWithAuthorityAsync(ConsolidationTransitionReceiptDto receipt,
        Func<CancellationToken, Task<IAsyncDisposable>> acquireAuthority,
        Func<CancellationToken, Task> revalidate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(acquireAuthority);
        ArgumentNullException.ThrowIfNull(revalidate);
        var tenant = Tenant();
        if (tenant != receipt.TenantId) throw new TenantScopeRejectedException("foreign reporting transition");
        await using var domainAuthority = await acquireAuthority(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Transition domain authority did not retain its validation lease.");
        await using var writer = await EntityLifecycleLeaseGate.For("consolidation-transition:" + _path).EnterWriteAsync(ct).ConfigureAwait(false);
        await _persistence.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RequireBooksAsync(receipt.Request, tenant, receipt.CompanyId, ct).ConfigureAwait(false);
            var state = await ReadAsync(ct).ConfigureAwait(false);
            var existing = state.Receipts.SingleOrDefault(item => item.TransitionId == receipt.TransitionId);
            if (existing is not null)
            {
                if (ConsolidationTransitionPostgresStore.SerializeReceipt(existing) != ConsolidationTransitionPostgresStore.SerializeReceipt(receipt))
                    throw new InvalidOperationException("The transition identity was already approved with different operands.");
                return existing;
            }
            var preview = state.Previews.SingleOrDefault(item => item.PreviewId == receipt.PreviewId && item.TenantId == tenant)
                ?? throw new InvalidOperationException("The exact retained transition preview is required.");
            ConsolidationTransitionRules.ValidateReceipt(receipt, preview);
            ConsolidationTransitionRules.ValidateAppend(receipt, state.Receipts.Where(item => item.TenantId == tenant
                && item.Request.OrganizationId == receipt.Request.OrganizationId && item.Request.OwnershipRootId == receipt.Request.OwnershipRootId).ToArray());
            _writerDepth.Value++;
            try { await revalidate(ct).ConfigureAwait(false); }
            finally { _writerDepth.Value--; }
            await WriteAsync(state with { Receipts = [.. state.Receipts, receipt] }, ct).ConfigureAwait(false);
            return ConsolidationTransitionPostgresStore.Clone(receipt);
        }
        finally { _persistence.Release(); }
    }

    private async Task RequireBooksAsync(ConsolidationTransitionRequestDto request, string tenant, string? company, CancellationToken ct)
    {
        ConsolidationTransitionRules.ValidateRequest(request);
        foreach (var id in new[] { request.PreviousEliminationBookId, request.NewEliminationBookId }.Order())
        {
            var book = await _journals.GetLedgerBookAsync(id, ct).ConfigureAwait(false)
                ?? throw new TenantScopeRejectedException("transition elimination book is unavailable");
            var owner = await _owners.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
            if (owner is null || !owner.IsHeldBy(tenant) || !string.Equals(owner.CompanyId, company, StringComparison.OrdinalIgnoreCase)
                || book.FundStructureNodeKind != FundStructureNodeKindDto.Fund || book.FundStructureNodeId != request.OwnershipRootId
                || book.AccountingBasis != AccountingBasisKindDto.Primary)
                throw new TenantScopeRejectedException("foreign or differently mapped transition elimination book");
        }
    }

    private async Task<ConsolidationTransitionFileState> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new([], []);
        var state = JsonSerializer.Deserialize(await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false),
            ConsolidationTransitionFileJsonContext.Default.ConsolidationTransitionFileState)
            ?? throw new InvalidDataException("Retained reporting transition state is empty.");
        if (state.Previews.Select(item => item.PreviewId).Distinct().Count() != state.Previews.Count
            || state.Receipts.Select(item => item.TransitionId).Distinct().Count() != state.Receipts.Count)
            throw new InvalidDataException("Retained transition identities are duplicated.");
        foreach (var preview in state.Previews) ConsolidationTransitionRules.ValidatePreview(preview);
        foreach (var group in state.Receipts.GroupBy(item => (item.TenantId, item.Request.OrganizationId, item.Request.OwnershipRootId)))
        {
            var prior = new List<ConsolidationTransitionReceiptDto>();
            foreach (var receipt in group.OrderBy(item => item.RootVersion))
            {
                var preview = state.Previews.SingleOrDefault(item => item.PreviewId == receipt.PreviewId)
                    ?? throw new InvalidDataException("Retained approval has no immutable preview.");
                ConsolidationTransitionRules.ValidateReceipt(receipt, preview);
                ConsolidationTransitionRules.ValidateAppend(receipt, prior);
                prior.Add(receipt);
            }
        }
        return state;
    }

    private Task WriteAsync(ConsolidationTransitionFileState state, CancellationToken ct)
        => AtomicFileWriter.WriteAsync(_path, JsonSerializer.Serialize(state, ConsolidationTransitionFileJsonContext.Default.ConsolidationTransitionFileState), ct);
}

internal sealed record ConsolidationTransitionFileState(IReadOnlyList<ConsolidationTransitionPreviewDto> Previews,
    IReadOnlyList<ConsolidationTransitionReceiptDto> Receipts);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConsolidationTransitionFileState))]
internal sealed partial class ConsolidationTransitionFileJsonContext : JsonSerializerContext;
