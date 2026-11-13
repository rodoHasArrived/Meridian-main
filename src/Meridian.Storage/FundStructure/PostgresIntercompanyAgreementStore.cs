using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Contracts.FundStructure;
using Npgsql;

namespace Meridian.Storage.FundStructure;

/// <summary>
/// Tenant-partitioned agreement commands. The mutable head only points to immutable command
/// snapshots; approved revisions, audit decisions and receipts are independently append-only.
/// </summary>
public sealed partial class PostgresIntercompanyAgreementStore : IIntercompanyAgreementStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FundStructureStoreOptions _options;

    public PostgresIntercompanyAgreementStore(FundStructureStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Regex.IsMatch(options.Schema, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException("A valid PostgreSQL schema identifier is required.", nameof(options));
        // Feature migrations use unquoted identifiers, which PostgreSQL folds to lower case.
        _options = new FundStructureStoreOptions
        {
            ConnectionString = options.ConnectionString,
            Schema = options.Schema.ToLowerInvariant()
        };
    }

    public async Task<IntercompanyAgreementStoreState?> GetAsync(
        string tenantId, Guid agreementId, CancellationToken ct = default)
    {
        var tenant = NormalizeTenant(tenantId);
        RequireAgreementId(agreementId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        return await ReadStateAsync(connection, null, tenant, agreementId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IntercompanyAgreementStoreState>> ListAsync(
        string tenantId, CancellationToken ct = default)
    {
        var tenant = NormalizeTenant(tenantId);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select s.payload::text from {Q("intercompany_agreement")} h
            join {Q("intercompany_agreement_snapshot")} s
              using (tenant_id, agreement_id, aggregate_version)
            where h.tenant_id = @tenant order by h.agreement_id;
            """;
        command.Parameters.AddWithValue("tenant", tenant);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<IntercompanyAgreementStoreState>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(Deserialize(reader.GetString(0)));
        return result;
    }

    public Task<T> ExecuteAsync<T>(string tenantId, Guid agreementId,
        Func<IIntercompanyAgreementStoreTransaction, CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
        => ExecuteCoreAsync(tenantId, agreementId, null, operation, ct);

    public Task<T> ExecuteAsync<T>(string tenantId, Guid agreementId, LegalEntityReadAuthorityProof structureAuthority,
        Func<IIntercompanyAgreementStoreTransaction, CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(structureAuthority);
        return ExecuteCoreAsync(tenantId, agreementId, structureAuthority, operation, ct);
    }

    private async Task<T> ExecuteCoreAsync<T>(string tenantId, Guid agreementId, LegalEntityReadAuthorityProof? structureAuthority,
        Func<IIntercompanyAgreementStoreTransaction, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        var tenant = NormalizeTenant(tenantId);
        RequireAgreementId(agreementId);
        ArgumentNullException.ThrowIfNull(operation);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        // Same order as the posting lease: structure authority first, then agreement IDs. The
        // advisory lock also protects an absent head during simultaneous first-draft creation.
        if (structureAuthority is null)
            await LockEntitiesAsync(connection, transaction, ct).ConfigureAwait(false);
        else
            ValidateStructureProof(connection, structureAuthority);
        await LockAgreementAsync(connection, transaction, tenant, agreementId, shared: false, ct).ConfigureAwait(false);
        var original = await ReadStateAsync(connection, transaction, tenant, agreementId, ct).ConfigureAwait(false);
        var unit = new CommandTransaction(this, connection, transaction, tenant, Clone(original));
        var result = await operation(unit, ct).ConfigureAwait(false);
        if (unit.PendingState is { } next)
        {
            ValidateAppend(original, next, agreementId);
            try
            { await PersistAsync(connection, transaction, tenant, original, next, ct).ConfigureAwait(false); }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new IntercompanyAgreementConflictException("The command identity or approved version is already retained.");
            }
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    public Task<IIntercompanyAgreementPostingLease> AcquirePostingLeaseAsync(
        string tenantId, IReadOnlyList<Guid> agreementIds, CancellationToken ct = default)
        => AcquirePostingLeaseCoreAsync(tenantId, agreementIds, null, ct);

    public Task<IIntercompanyAgreementPostingLease> AcquirePostingLeaseAsync(string tenantId, IReadOnlyList<Guid> agreementIds,
        LegalEntityReadAuthorityProof structureAuthority, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(structureAuthority);
        return AcquirePostingLeaseCoreAsync(tenantId, agreementIds, structureAuthority, ct);
    }

    private async Task<IIntercompanyAgreementPostingLease> AcquirePostingLeaseCoreAsync(
        string tenantId, IReadOnlyList<Guid> agreementIds, LegalEntityReadAuthorityProof? structureAuthority, CancellationToken ct)
    {
        var tenant = NormalizeTenant(tenantId);
        ArgumentNullException.ThrowIfNull(agreementIds);
        var ids = agreementIds.Distinct().OrderBy(x => x).ToArray();
        foreach (var id in ids)
            RequireAgreementId(id);
        var connection = await OpenAsync(ct).ConfigureAwait(false);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
            if (structureAuthority is null)
                await LockEntitiesAsync(connection, transaction, ct).ConfigureAwait(false);
            else
                ValidateStructureProof(connection, structureAuthority);
            var states = new List<IntercompanyAgreementStoreState>(ids.Length);
            foreach (var id in ids)
            {
                await LockAgreementAsync(connection, transaction, tenant, id, shared: true, ct).ConfigureAwait(false);
                var state = await ReadStateAsync(connection, transaction, tenant, id, ct).ConfigureAwait(false);
                if (state is not null)
                    states.Add(state);
            }
            return new PostingLease(this, connection, transaction, tenant, states);
        }
        catch
        {
            try
            {
                if (transaction is not null)
                    await transaction.DisposeAsync().ConfigureAwait(false);
            }
            finally { await connection.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }

    private void ValidateStructureProof(NpgsqlConnection connection, LegalEntityReadAuthorityProof proof)
    {
        if (!proof.IsActive || proof.Port != connection.Port
            || !string.Equals(proof.Host, connection.Host, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(proof.Database, connection.Database, StringComparison.Ordinal)
            || !string.Equals(proof.Schema, _options.Schema, StringComparison.Ordinal))
            throw new IntercompanyAgreementValidationException("The retained legal-entity authority is expired or belongs to another backend or schema.");
    }

    private string Q(string table) => $"\"{_options.Schema}\".\"{table}\"";

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task LockEntitiesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"lock table {Q("legal_entity")} in share mode;";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task LockAgreementAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string tenant, Guid agreementId, bool shared, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var function = shared ? "pg_advisory_xact_lock_shared" : "pg_advisory_xact_lock";
        var rowMode = shared ? "share" : "update";
        command.CommandText = $"""
            select {function}(hashtextextended(@scope, 0));
            select aggregate_version from {Q("intercompany_agreement")}
            where tenant_id = @tenant and agreement_id = @id for {rowMode};
            """;
        command.Parameters.AddWithValue("scope", $"meridian.agreement.{_options.Schema}.{tenant}.{agreementId:D}");
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("id", agreementId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<IntercompanyAgreementStoreState?> ReadStateAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, string tenant, Guid agreementId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select s.payload::text from {Q("intercompany_agreement")} h
            join {Q("intercompany_agreement_snapshot")} s
              using (tenant_id, agreement_id, aggregate_version)
            where h.tenant_id = @tenant and h.agreement_id = @id;
            """;
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("id", agreementId);
        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return json is null ? null : Deserialize(json);
    }

    private async Task<LegalEntitySummaryDto?> ReadEntityAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string tenant, Guid entityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select * from {Q("legal_entity")}
            where entity_id = @id and lower(btrim(tenant_id)) = @tenant;
            """;
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("id", entityId);
        await using var r = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return new LegalEntitySummaryDto(
            r.GetGuid(r.GetOrdinal("entity_id")), Enum.Parse<LegalEntityTypeDto>(r.GetString(r.GetOrdinal("entity_type"))),
            r.GetString(r.GetOrdinal("code")), r.GetString(r.GetOrdinal("name")), r.GetString(r.GetOrdinal("jurisdiction")),
            r.GetString(r.GetOrdinal("base_currency")), r.GetBoolean(r.GetOrdinal("is_active")),
            r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_from")),
            r.IsDBNull(r.GetOrdinal("effective_to")) ? null : r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("effective_to")),
            r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
            Enum.Parse<LegalEntityFormDto>(r.GetString(r.GetOrdinal("legal_form"))),
            Enum.Parse<LegalEntityLifecycleStatusDto>(r.GetString(r.GetOrdinal("lifecycle_status"))),
            r.IsDBNull(r.GetOrdinal("registration_number")) ? null : r.GetString(r.GetOrdinal("registration_number")),
            JsonSerializer.Deserialize<List<BeneficialOwnerSummaryDto>>(r.GetString(r.GetOrdinal("beneficial_owners")), JsonOptions),
            JsonSerializer.Deserialize<List<LegalEntityLifecycleEventDto>>(r.GetString(r.GetOrdinal("lifecycle_events")), JsonOptions));
    }

    private static string NormalizeTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.Equals(tenantId.Trim(), "all", StringComparison.OrdinalIgnoreCase))
            throw new IntercompanyAgreementForbiddenException("Authenticated tenant scope is required.");
        return tenantId.Trim().ToLowerInvariant();
    }

    private static void RequireAgreementId(Guid agreementId)
    {
        if (agreementId == Guid.Empty)
            throw new IntercompanyAgreementValidationException("Agreement identity is required.");
    }

    private static IntercompanyAgreementStoreState Deserialize(string json) =>
        JsonSerializer.Deserialize<IntercompanyAgreementStoreState>(json, JsonOptions)
        ?? throw new InvalidOperationException("Retained agreement snapshot is invalid.");

    private static IntercompanyAgreementStoreState? Clone(IntercompanyAgreementStoreState? state) =>
        state is null ? null : Deserialize(JsonSerializer.Serialize(state, JsonOptions));

    private sealed class CommandTransaction(PostgresIntercompanyAgreementStore owner, NpgsqlConnection connection,
        NpgsqlTransaction transaction, string tenant, IntercompanyAgreementStoreState? state) : IIntercompanyAgreementStoreTransaction
    {
        public IntercompanyAgreementStoreState? State { get; } = state;
        public IntercompanyAgreementStoreState? PendingState { get; private set; }
        public Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid entityId, CancellationToken ct = default) =>
            owner.ReadEntityAsync(connection, transaction, tenant, entityId, ct);

        public void Save(IntercompanyAgreementStoreState next)
        {
            ArgumentNullException.ThrowIfNull(next);
            if (PendingState is not null)
                throw new InvalidOperationException("An agreement transaction may save one command only.");
            PendingState = Clone(next);
        }
    }

    private sealed class PostingLease(PostgresIntercompanyAgreementStore owner, NpgsqlConnection connection,
        NpgsqlTransaction transaction, string tenant, IReadOnlyList<IntercompanyAgreementStoreState> states)
        : IIntercompanyAgreementPostingLease
    {
        private int _disposed;
        public IReadOnlyList<IntercompanyAgreementStoreState> Agreements { get; } = states;

        public Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid entityId, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return owner.ReadEntityAsync(connection, transaction, tenant, entityId, ct);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try
            { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { await connection.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
