using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

/// <summary>Immutable approved FX revisions, serialized against posting's shared authority lease.</summary>
public sealed class ConsolidationFxPostgresStore : IConsolidationFxStore
{
    private const long AuthorityLock = 714293004001L;
    private readonly LedgerJournalStoreOptions _options;
    private readonly string _schema;

    public ConsolidationFxPostgresStore(LedgerJournalStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _schema = new NpgsqlCommandBuilder().QuoteIdentifier(options.SchemaName);
    }

    public async Task RetainRateSetAsync(ConsolidationFxRateSetDto value, CancellationToken ct = default)
    {
        ConsolidationFxCurrencyRules.Validate(value);
        value = value with { ContentHash = ConsolidationFxValidation.Hash(value) };
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockAsync(connection, transaction, shared: false, ct).ConfigureAwait(false);
        await using var head = connection.CreateCommand();
        head.Transaction = transaction;
        head.CommandText = $"select revision, content_hash from {_schema}.consolidation_fx_rate_sets where tenant_id=@tenant and company_id=@company and rate_set_id=@id order by revision desc";
        Scope(head, value.TenantId, value.CompanyId);
        head.Parameters.AddWithValue("id", value.RateSetId);
        var history = new List<(int Revision, string Hash)>();
        await using (var reader = await head.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                history.Add((reader.GetInt32(0), reader.GetString(1)));
        if (history.Any(x => x.Revision == value.Revision))
        {
            if (!Sha256Digest.FixedEquals(history.Single(x => x.Revision == value.Revision).Hash, value.ContentHash))
                throw new InvalidOperationException("An approved rate-set revision cannot be replaced.");
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return;
        }
        ConsolidationFxInMemoryStore.RequirePredecessor(value.Revision, value.PreviousHash,
            history.Count == 0 ? null : history[0].Revision, history.Count == 0 ? null : history[0].Hash);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"insert into {_schema}.consolidation_fx_rate_sets (tenant_id,company_id,rate_set_id,revision,content_hash,document) values (@tenant,@company,@id,@revision,@hash,@document::jsonb)";
        Scope(insert, value.TenantId, value.CompanyId);
        insert.Parameters.AddWithValue("id", value.RateSetId);
        insert.Parameters.AddWithValue("revision", value.Revision);
        insert.Parameters.AddWithValue("hash", value.ContentHash!);
        insert.Parameters.AddWithValue("document", JsonSerializer.Serialize(value, ConsolidationFxJsonContext.Default.ConsolidationFxRateSetDto));
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<ConsolidationFxRateSetDto?> GetRateSetAsync(string tenantId, string companyId, Guid rateSetId, int revision, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select document::text, content_hash from {_schema}.consolidation_fx_rate_sets where tenant_id=@tenant and company_id=@company and rate_set_id=@id and revision=@revision";
        Scope(command, tenantId, companyId);
        command.Parameters.AddWithValue("id", rateSetId);
        command.Parameters.AddWithValue("revision", revision);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        var result = ReadRateSet(reader);
        if (result.TenantId != tenantId || result.CompanyId != companyId || result.RateSetId != rateSetId || result.Revision != revision)
            throw new InvalidDataException("Retained FX rate-set scope does not match its indexed identity.");
        return result;
    }

    public async Task<IReadOnlyList<ConsolidationFxRateSetDto>> ListRateSetsAsync(string tenantId, string companyId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select document::text,content_hash,rate_set_id,revision from {_schema}.consolidation_fx_rate_sets where tenant_id=@tenant and company_id=@company order by rate_set_id,revision";
        Scope(command, tenantId, companyId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<ConsolidationFxRateSetDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var value = ReadRateSet(reader);
            if (value.TenantId != tenantId || value.CompanyId != companyId || value.RateSetId != reader.GetGuid(2) || value.Revision != reader.GetInt32(3))
                throw new InvalidDataException("Retained FX rate-set scope does not match its indexed identity.");
            results.Add(value);
        }
        return results;
    }

    public async Task RetainConfigurationAsync(ConsolidationFxBookConfigurationDto value, CancellationToken ct = default)
    {
        ConsolidationFxCurrencyRules.Validate(value);
        value = value with { ContentHash = ConsolidationFxValidation.Hash(value) };
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockAsync(connection, transaction, shared: false, ct).ConfigureAwait(false);
        await using var head = connection.CreateCommand();
        head.Transaction = transaction;
        head.CommandText = $"select revision,content_hash,tenant_id,company_id from {_schema}.consolidation_fx_book_configurations where ledger_book_id=@book order by revision desc";
        head.Parameters.AddWithValue("book", value.LedgerBookId);
        var history = new List<(int Revision, string Hash)>();
        await using (var reader = await head.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (reader.GetString(2) != value.TenantId || reader.GetString(3) != value.CompanyId)
                    throw new InvalidOperationException("A book's FX configuration cannot change owner scope.");
                history.Add((reader.GetInt32(0), reader.GetString(1)));
            }
        if (history.Any(x => x.Revision == value.Revision))
        {
            if (!Sha256Digest.FixedEquals(history.Single(x => x.Revision == value.Revision).Hash, value.ContentHash))
                throw new InvalidOperationException("An approved FX configuration revision cannot be replaced.");
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return;
        }
        ConsolidationFxInMemoryStore.RequirePredecessor(value.Revision, value.PreviousHash,
            history.Count == 0 ? null : history[0].Revision, history.Count == 0 ? null : history[0].Hash);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            insert into {_schema}.consolidation_fx_book_configurations
                (ledger_book_id,revision,tenant_id,company_id,effective_from,effective_to,rate_set_id,rate_set_revision,content_hash,document)
            values (@book,@revision,@tenant,@company,@from,@to,@rates,@rates_revision,@hash,@document::jsonb)
            """;
        Scope(insert, value.TenantId, value.CompanyId);
        insert.Parameters.AddWithValue("book", value.LedgerBookId);
        insert.Parameters.AddWithValue("revision", value.Revision);
        insert.Parameters.AddWithValue("from", value.EffectiveFrom);
        insert.Parameters.AddWithValue("to", NpgsqlTypes.NpgsqlDbType.Date, (object?)value.EffectiveTo ?? DBNull.Value);
        insert.Parameters.AddWithValue("rates", value.RateSetId);
        insert.Parameters.AddWithValue("rates_revision", value.RateSetRevision);
        insert.Parameters.AddWithValue("hash", value.ContentHash!);
        insert.Parameters.AddWithValue("document", JsonSerializer.Serialize(value, ConsolidationFxJsonContext.Default.ConsolidationFxBookConfigurationDto));
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<ConsolidationFxBookConfigurationDto?> GetConfigurationAsync(Guid ledgerBookId, DateOnly asOf, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select document::text,content_hash,revision,tenant_id,company_id from {_schema}.consolidation_fx_book_configurations where ledger_book_id=@book and effective_from<=@asof order by revision desc limit 1";
        command.Parameters.AddWithValue("book", ledgerBookId);
        command.Parameters.AddWithValue("asof", asOf);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        var value = JsonSerializer.Deserialize(reader.GetString(0), ConsolidationFxJsonContext.Default.ConsolidationFxBookConfigurationDto)
            ?? throw new InvalidDataException("The retained FX configuration is empty.");
        ConsolidationFxCurrencyRules.Validate(value);
        if (value.ContentHash is null || !Sha256Digest.FixedEquals(value.ContentHash, reader.GetString(1)) ||
            value.LedgerBookId != ledgerBookId || value.Revision != reader.GetInt32(2) || value.TenantId != reader.GetString(3) ||
            value.CompanyId != reader.GetString(4) || value.EffectiveFrom > asOf)
            throw new InvalidDataException("Retained FX configuration authority is inconsistent.");
        return value.EffectiveTo is { } end && asOf >= end ? null : value;
    }

    public async Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct).ConfigureAwait(false);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await LockAsync(connection, transaction, shared: true, ct).ConfigureAwait(false);
            return new AuthorityLease(connection, transaction);
        }
        catch
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ConsolidationFxBookConfigurationDto?> GetConfigurationRevisionAsync(Guid ledgerBookId, int revision, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select document::text,content_hash,tenant_id,company_id from {_schema}.consolidation_fx_book_configurations where ledger_book_id=@book and revision=@revision";
        command.Parameters.AddWithValue("book", ledgerBookId);
        command.Parameters.AddWithValue("revision", revision);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        var value = JsonSerializer.Deserialize(reader.GetString(0), ConsolidationFxJsonContext.Default.ConsolidationFxBookConfigurationDto)
            ?? throw new InvalidDataException("The retained FX configuration is empty.");
        ConsolidationFxCurrencyRules.Validate(value);
        if (value.ContentHash is null || !Sha256Digest.FixedEquals(value.ContentHash, reader.GetString(1)) ||
            value.LedgerBookId != ledgerBookId || value.Revision != revision || value.TenantId != reader.GetString(2) || value.CompanyId != reader.GetString(3))
            throw new InvalidDataException("Retained FX configuration authority is inconsistent.");
        return value;
    }

    private static ConsolidationFxRateSetDto ReadRateSet(NpgsqlDataReader reader)
    {
        var value = JsonSerializer.Deserialize(reader.GetString(0), ConsolidationFxJsonContext.Default.ConsolidationFxRateSetDto)
            ?? throw new InvalidDataException("The retained FX rate set is empty.");
        ConsolidationFxCurrencyRules.Validate(value);
        if (value.ContentHash is null || !Sha256Digest.FixedEquals(value.ContentHash, reader.GetString(1)))
            throw new InvalidDataException("Retained FX rate-set authority is inconsistent.");
        return value;
    }
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        try
        { await connection.OpenAsync(ct).ConfigureAwait(false); return connection; }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private static void Scope(NpgsqlCommand command, string tenant, string company)
    {
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("company", company);
    }
    private static async Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, bool shared, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = shared ? "select pg_advisory_xact_lock_shared(@key)" : "select pg_advisory_xact_lock(@key)";
        command.Parameters.AddWithValue("key", AuthorityLock);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
    private sealed class AuthorityLease(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            { await transaction.DisposeAsync().ConfigureAwait(false); }
            finally { await connection.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
