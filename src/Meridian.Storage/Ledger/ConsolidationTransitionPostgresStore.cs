using System.Data;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Ledger;
using Meridian.Storage.FundStructure;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Ledger;

/// <summary>Immutable reporting routes fenced against source appends and mapping changes.</summary>
public sealed class ConsolidationTransitionPostgresStore : IConsolidationTransitionStore
{
    internal const long AuthorityLock = 714293004002L;
    private readonly LedgerJournalStoreOptions _options;
    private readonly IFundScopeTenantAccessor _tenants;
    private readonly string _schema;
    private readonly AsyncLocal<int> _writerDepth = new();

    public ConsolidationTransitionPostgresStore(LedgerJournalStoreOptions options, IFundScopeTenantAccessor tenants)
    {
        _options = options;
        _tenants = tenants;
        _schema = new NpgsqlCommandBuilder().QuoteIdentifier(options.SchemaName);
    }

    private string Q(string table) => $"{_schema}.{table}";
    private string Tenant() => ConsolidationTransitionRules.RequireTenant(_tenants.ResolveCallerTenant() ?? FundScopeTenantAuthority.CurrentTenantId);
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        try { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    {
        _ = Tenant();
        if (_writerDepth.Value > 0) return EmptyLedgerAuthorityLease.Instance;
        var connection = await OpenAsync(ct).ConfigureAwait(false);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
            await FenceAsync(connection, transaction, shared: true, ct).ConfigureAwait(false);
            return new Lease(connection, transaction);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task FenceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, bool shared, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // The consumer already retains book/configuration authority. Reacquiring the same
        // table SHARE lock on another connection can deadlock behind a queued book writer.
        // Route readers need only the shared route fence; activation owns mapping fences.
        command.CommandText = shared ? "select pg_advisory_xact_lock_shared(@authority);"
            : "select pg_advisory_xact_lock(@authority);";
        command.Parameters.AddWithValue("authority", AuthorityLock);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConsolidationTransitionReceiptDto>> ListAsync(Guid organizationId, Guid ownershipRootId, CancellationToken ct = default)
    {
        var tenant = Tenant();
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var results = await ReadReceiptsAsync(connection, null, tenant, organizationId, ownershipRootId, null, ct).ConfigureAwait(false);
        foreach (var result in results) await RequireBooksAsync(connection, null, tenant, result.CompanyId, result.Request, ct).ConfigureAwait(false);
        return results;
    }

    public async Task<ConsolidationTransitionReceiptDto?> GetAsync(Guid transitionId, CancellationToken ct = default)
    {
        var tenant = Tenant();
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var result = (await ReadReceiptsAsync(connection, null, tenant, null, null, transitionId, ct).ConfigureAwait(false)).SingleOrDefault();
        if (result is not null) await RequireBooksAsync(connection, null, tenant, result.CompanyId, result.Request, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<ConsolidationTransitionPreviewDto?> GetPreviewAsync(Guid previewId, CancellationToken ct = default)
    {
        var tenant = Tenant();
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        var result = await ReadPreviewAsync(connection, null, tenant, previewId, ct).ConfigureAwait(false);
        if (result is not null) await RequireBooksAsync(connection, null, tenant, result.CompanyId, result.Request, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<ConsolidationTransitionPreviewDto> RetainPreviewAsync(ConsolidationTransitionPreviewDto preview, CancellationToken ct = default)
    {
        ConsolidationTransitionRules.ValidatePreview(preview);
        var tenant = Tenant();
        RequireOwner(tenant, preview.TenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await using (var guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            // Publishing a preview does not change an active route and must remain possible
            // while its calculator holds the shared route-authority fence.
            // A preview is retained evidence, not an activated route. A subsequent activation
            // rechecks ownership and all operands under the authoritative mapping/append fences.
            // Reacquiring mapping locks here would invert the calculator's policy-first lease.
            guard.CommandText = "select pg_advisory_xact_lock(hashtextextended(@key,0));";
            guard.Parameters.AddWithValue("key", $"consolidation-transition-preview:{preview.PreviewId:D}");
            await guard.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await RequireBooksAsync(connection, transaction, tenant, preview.CompanyId, preview.Request, ct).ConfigureAwait(false);
        var existing = await ReadPreviewAsync(connection, transaction, tenant, preview.PreviewId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (SerializePreview(existing) != SerializePreview(preview with { PreparedAt = existing.PreparedAt }))
                throw new InvalidOperationException("The preview identity was already retained with different reviewed operands.");
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return existing;
        }
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            insert into {Q("consolidation_transition_previews")}
                (preview_id,transition_id,organization_id,ownership_root_id,tenant_id,company_id,fingerprint,prepared_at,document)
            values (@id,@transition,@organization,@root,@tenant,@company,@fingerprint,@prepared,@document::jsonb);
            """;
        Scope(insert, tenant, preview.CompanyId, preview.Request);
        insert.Parameters.AddWithValue("id", preview.PreviewId);
        insert.Parameters.AddWithValue("transition", preview.Request.TransitionId);
        insert.Parameters.AddWithValue("fingerprint", preview.Fingerprint);
        insert.Parameters.AddWithValue("prepared", preview.PreparedAt.ToUniversalTime());
        insert.Parameters.AddWithValue("document", SerializePreview(preview));
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return Clone(preview);
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
        RequireOwner(tenant, receipt.TenantId);
        ConsolidationTransitionRules.ValidateRequest(receipt.Request);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await using (var mappings = connection.CreateCommand())
        {
            mappings.Transaction = transaction;
            mappings.CommandText = $"lock table {Q("ledger_books")}, {Q("fund_profile_tenancy")} in share mode;";
            await mappings.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        // Journal append already follows book -> policy/domain -> route -> audit. Acquiring
        // domain authority before this mapping fence can deadlock behind a queued book writer.
        await using var domainAuthority = await acquireAuthority(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Transition domain authority did not retain its validation lease.");
        await FenceAsync(connection, transaction, shared: false, ct).ConfigureAwait(false);
        await using (var guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            guard.CommandText = "select pg_advisory_xact_lock(hashtextextended(@root,0));";
            guard.Parameters.AddWithValue("root", $"consolidation-transition-root:{tenant}:{receipt.Request.OrganizationId:D}:{receipt.Request.OwnershipRootId:D}");
            await guard.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var appendBarrier = connection.CreateCommand())
        {
            appendBarrier.Transaction = transaction;
            appendBarrier.CommandText = $"select chain_id from {Q("ledger_event_audit_head")} where chain_id=1 for update;";
            if (await appendBarrier.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
                throw new InvalidOperationException("The authoritative ledger append barrier is unavailable.");
        }
        await RequireBooksAsync(connection, transaction, tenant, receipt.CompanyId, receipt.Request, ct).ConfigureAwait(false);
        var retained = (await ReadReceiptsAsync(connection, transaction, tenant, null, null, receipt.TransitionId, ct).ConfigureAwait(false)).SingleOrDefault();
        if (retained is not null)
        {
            if (SerializeReceipt(retained) != SerializeReceipt(receipt))
                throw new InvalidOperationException("The transition identity was already approved with different reviewed operands.");
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return retained;
        }
        var preview = await ReadPreviewAsync(connection, transaction, tenant, receipt.PreviewId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The exact retained transition preview is required before approval.");
        ConsolidationTransitionRules.ValidateReceipt(receipt, preview);
        var history = await ReadReceiptsAsync(connection, transaction, tenant, receipt.Request.OrganizationId, receipt.Request.OwnershipRootId, null, ct).ConfigureAwait(false);
        ConsolidationTransitionRules.ValidateAppend(receipt, history);
        _writerDepth.Value++;
        try { await revalidate(ct).ConfigureAwait(false); }
        finally { _writerDepth.Value--; }
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            insert into {Q("consolidation_transition_receipts")}
                (transition_id,preview_id,organization_id,ownership_root_id,root_version,previous_transition_id,
                 previous_book_id,new_book_id,cutover_date,tenant_id,company_id,content_hash,document)
            values (@id,@preview,@organization,@root,@version,@previous,@old_book,@new_book,@cutover,@tenant,@company,@hash,@document::jsonb);
            """;
        Scope(insert, tenant, receipt.CompanyId, receipt.Request);
        insert.Parameters.AddWithValue("id", receipt.TransitionId);
        insert.Parameters.AddWithValue("preview", receipt.PreviewId);
        insert.Parameters.AddWithValue("version", receipt.RootVersion);
        insert.Parameters.AddWithValue("previous", NpgsqlDbType.Uuid, (object?)receipt.PreviousTransitionId ?? DBNull.Value);
        insert.Parameters.AddWithValue("old_book", receipt.Request.PreviousEliminationBookId);
        insert.Parameters.AddWithValue("new_book", receipt.Request.NewEliminationBookId);
        insert.Parameters.AddWithValue("cutover", receipt.Request.CutoverDate);
        insert.Parameters.AddWithValue("hash", receipt.ContentHash);
        insert.Parameters.AddWithValue("document", SerializeReceipt(receipt));
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return Clone(receipt);
    }

    private async Task RequireBooksAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string tenant,
        string? company, ConsolidationTransitionRequestDto request, CancellationToken ct)
    {
        foreach (var id in new[] { request.PreviousEliminationBookId, request.NewEliminationBookId }.Order())
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                select b.fund_structure_node_id,b.fund_structure_node_kind,b.accounting_basis,nullif(lower(trim(t.company_id)),'all')
                from {Q("ledger_books")} b join {Q("fund_profile_tenancy")} t on lower(trim(b.fund_profile_id))=t.fund_profile_id
                where b.ledger_book_id=@id and lower(trim(b.tenant_id))=@tenant and lower(trim(t.tenant_id))=@tenant;
                """;
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("tenant", tenant);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false) || reader.GetGuid(0) != request.OwnershipRootId
                || reader.GetString(1) != FundStructureNodeKindDto.Fund.ToString() || reader.GetString(2) != AccountingBasisKindDto.Primary.ToString()
                || !string.Equals(reader.IsDBNull(3) ? null : reader.GetString(3), company, StringComparison.OrdinalIgnoreCase))
                throw new TenantScopeRejectedException("reporting transitions for an unavailable, foreign or differently mapped elimination book");
        }
    }

    private async Task<ConsolidationTransitionPreviewDto?> ReadPreviewAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string tenant, Guid id, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select document::text from {Q("consolidation_transition_previews")} where preview_id=@id and tenant_id=@tenant;";
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("tenant", tenant);
        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (json is null) return null;
        var value = JsonSerializer.Deserialize(json, ConsolidationTransitionJsonContext.Default.ConsolidationTransitionPreviewDto)
            ?? throw new InvalidDataException("The retained transition preview is empty.");
        if (value.TenantId != tenant || value.PreviewId != id) throw new InvalidDataException("Transition preview storage scope is inconsistent.");
        ConsolidationTransitionRules.ValidatePreview(value);
        return value;
    }

    private async Task<IReadOnlyList<ConsolidationTransitionReceiptDto>> ReadReceiptsAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, string tenant, Guid? organization, Guid? root, Guid? id, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"select document::text from {Q("consolidation_transition_receipts")} where tenant_id=@tenant"
            + (organization.HasValue ? " and organization_id=@organization and ownership_root_id=@root" : " and transition_id=@id") + " order by root_version;";
        command.Parameters.AddWithValue("tenant", tenant);
        if (organization.HasValue) { command.Parameters.AddWithValue("organization", organization.Value); command.Parameters.AddWithValue("root", root!.Value); }
        else command.Parameters.AddWithValue("id", id!.Value);
        var results = new List<ConsolidationTransitionReceiptDto>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var value = JsonSerializer.Deserialize(reader.GetString(0), ConsolidationTransitionJsonContext.Default.ConsolidationTransitionReceiptDto)
                    ?? throw new InvalidDataException("The retained reporting transition is empty.");
                if (value.TenantId != tenant || organization.HasValue && (value.Request.OrganizationId != organization || value.Request.OwnershipRootId != root)
                    || id.HasValue && value.TransitionId != id || value.ContentHash != ConsolidationTransitionRules.ReceiptHash(value))
                    throw new InvalidDataException("Transition receipt storage identity or content hash is inconsistent.");
                results.Add(value);
            }
        }
        if (id.HasValue && results.SingleOrDefault() is { } target)
        {
            var complete = await ReadReceiptsAsync(connection, transaction, tenant, target.Request.OrganizationId,
                target.Request.OwnershipRootId, null, ct).ConfigureAwait(false);
            return complete.Where(item => item.TransitionId == id.Value).ToArray();
        }
        var prior = new List<ConsolidationTransitionReceiptDto>();
        foreach (var receipt in results)
        {
            var preview = await ReadPreviewAsync(connection, transaction, tenant, receipt.PreviewId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("A retained transition receipt has no immutable preview.");
            ConsolidationTransitionRules.ValidateReceipt(receipt, preview);
            ConsolidationTransitionRules.ValidateAppend(receipt, prior);
            prior.Add(receipt);
        }
        return results;
    }

    private static void RequireOwner(string tenant, string owner)
    { if (tenant != owner) throw new TenantScopeRejectedException("foreign reporting transition authority"); }
    private static void Scope(NpgsqlCommand command, string tenant, string? company, ConsolidationTransitionRequestDto request)
    {
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("company", NpgsqlDbType.Text, (object?)company ?? DBNull.Value);
        command.Parameters.AddWithValue("organization", request.OrganizationId);
        command.Parameters.AddWithValue("root", request.OwnershipRootId);
    }
    internal static string SerializePreview(ConsolidationTransitionPreviewDto value) => JsonSerializer.Serialize(value, ConsolidationTransitionJsonContext.Default.ConsolidationTransitionPreviewDto);
    internal static string SerializeReceipt(ConsolidationTransitionReceiptDto value) => JsonSerializer.Serialize(value, ConsolidationTransitionJsonContext.Default.ConsolidationTransitionReceiptDto);
    internal static ConsolidationTransitionPreviewDto Clone(ConsolidationTransitionPreviewDto value) => JsonSerializer.Deserialize(SerializePreview(value), ConsolidationTransitionJsonContext.Default.ConsolidationTransitionPreviewDto)!;
    internal static ConsolidationTransitionReceiptDto Clone(ConsolidationTransitionReceiptDto value) => JsonSerializer.Deserialize(SerializeReceipt(value), ConsolidationTransitionJsonContext.Default.ConsolidationTransitionReceiptDto)!;
    private sealed class Lease(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    { public async ValueTask DisposeAsync() { try { await transaction.DisposeAsync().ConfigureAwait(false); } finally { await connection.DisposeAsync().ConfigureAwait(false); } } }
}
