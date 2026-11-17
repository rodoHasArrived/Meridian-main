using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meridian.Storage.Ledger;
using Npgsql;
using static Meridian.FinancialOperations.FundAdministration.RecurringJournalDocumentRules;

namespace Meridian.FinancialOperations.FundAdministration;

/// <summary>
/// Retains recurring journal definitions, activations and occurrence claims in the ledger database
/// (<c>V_ledger_073__recurring_journal_state</c>), so runners on different processes and hosts share one
/// lease instead of one filesystem root. The retained document, its validation and its session
/// semantics are identical to <see cref="FileRecurringJournalStore"/>.
/// </summary>
/// <remarks>
/// A session holds a PostgreSQL session-level advisory lock on a dedicated connection for its whole
/// planning, claim, intake and completion cycle. Every commit also advances a row revision guarded by
/// compare-and-swap, so a session whose connection (and therefore lease) was lost cannot overwrite a
/// newer commit: it faults instead. Missing or invalid state never becomes empty state; the row is
/// created only by explicit provisioning and the table rejects row deletes.
/// </remarks>
public sealed class PostgresRecurringJournalStore : IRecurringJournalStore, IRecurringJournalStoreProvisioning
{
    private const string StoreId = "default";
    private readonly LedgerJournalStoreOptions _options;
    private readonly string _table;
    private readonly string _lockKey;

    public PostgresRecurringJournalStore(LedgerJournalStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        if (!Regex.IsMatch(options.SchemaName, "^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException("Invalid ledger schema name.", nameof(options));
        _table = $"\"{options.SchemaName}\".\"recurring_journal_state\"";
        _lockKey = $"meridian-recurring-journals:{options.SchemaName}";
    }

    /// <summary>
    /// Explicit provisioning only; never called by the runner. Creates the empty retained document
    /// when none exists and otherwise verifies the existing one without replacing it.
    /// </summary>
    public Task InitializeAsync(CancellationToken ct = default) => InitializeAsync(legacy: null, ct);

    /// <summary>
    /// Explicit provisioning that carries a host's earlier file-retained state forward. When no row
    /// exists yet and <paramref name="legacy"/> was provisioned, its verified document becomes the
    /// initial state, so retained claims, outcomes and draft identities are not reissued. Unverifiable
    /// legacy state fails closed. Once a row exists it is authoritative and the file is not read.
    /// </summary>
    public async Task InitializeAsync(FileRecurringJournalStore? legacy, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await AcquireAsync(connection, ct).ConfigureAwait(false);
        try
        {
            if (!await ExistsAsync(connection, ct).ConfigureAwait(false))
            {
                var initial = (legacy is null ? null : await legacy.ReadForImportAsync(ct).ConfigureAwait(false)) ?? Empty();
                var (hash, envelope) = SerializeEnvelope(initial);
                await using var insert = connection.CreateCommand();
                insert.CommandText = $"""
                    insert into {_table} (store_id, revision, content_hash, envelope_json, initialized_at, updated_at)
                    values (@store, 1, @hash, @envelope, now(), now())
                    on conflict (store_id) do nothing;
                    """;
                insert.Parameters.AddWithValue("store", StoreId);
                insert.Parameters.AddWithValue("hash", hash);
                insert.Parameters.AddWithValue("envelope", Encoding.UTF8.GetString(envelope));
                await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            _ = await ReadAsync(connection, ct).ConfigureAwait(false);
        }
        finally
        {
            await ReleaseAsync(connection).ConfigureAwait(false);
        }
    }

    public async Task<IRecurringJournalSession> OpenSessionAsync(CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await AcquireAsync(connection, ct).ConfigureAwait(false);
            try
            {
                var (revision, document) = await ReadAsync(connection, ct).ConfigureAwait(false);
                return new RecurringJournalDocumentSession(new Lease(this, connection, revision), document);
            }
            catch
            {
                await ReleaseAsync(connection).ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task AcquireAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        // Wait as long as the caller allows, like the file lease: a runner holding the lease may be
        // mid-intake, and a command timeout would turn ordinary contention into a failed cycle.
        command.CommandTimeout = 0;
        command.CommandText = "select pg_advisory_lock(hashtextextended(@key, 0));";
        command.Parameters.AddWithValue("key", _lockKey);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task ReleaseAsync(NpgsqlConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            return;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "select pg_advisory_unlock(hashtextextended(@key, 0));";
            command.Parameters.AddWithValue("key", _lockKey);
            await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            // Closing the connection below releases a session-level advisory lock regardless.
        }
    }

    private async Task<bool> ExistsAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"select exists (select 1 from {_table} where store_id = @store);";
        command.Parameters.AddWithValue("store", StoreId);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;
    }

    private async Task<(long Revision, RecurringJournalDocument Document)> ReadAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"select revision, content_hash, envelope_json from {_table} where store_id = @store;";
        command.Parameters.AddWithValue("store", StoreId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException("The durable recurring journal store is unavailable or has not been provisioned.");
        var revision = reader.GetInt64(0);
        var columnHash = reader.GetString(1);
        var json = reader.GetString(2);
        try
        {
            var document = ReadEnvelope(Encoding.UTF8.GetBytes(json));
            // The indexed hash column must agree with the envelope it summarizes.
            if (!string.Equals(columnHash, SerializeEnvelope(document).ContentHash, StringComparison.Ordinal))
                throw new InvalidOperationException("The durable recurring journal state failed integrity or schema validation.");
            return (revision, document);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException or InvalidOperationException)
        {
            throw new InvalidOperationException("The durable recurring journal state is unavailable or invalid; restore the retained store before retrying.", ex);
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class Lease(PostgresRecurringJournalStore owner, NpgsqlConnection connection, long revision) : IRecurringJournalDocumentLease
    {
        private long _revision = revision;

        public async Task WriteAsync(RecurringJournalDocument document, CancellationToken ct)
        {
            var (hash, envelope) = SerializeEnvelope(document);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                update {owner._table}
                set revision = revision + 1, content_hash = @hash, envelope_json = @envelope, updated_at = now()
                where store_id = @store and revision = @expected;
                """;
            command.Parameters.AddWithValue("store", StoreId);
            command.Parameters.AddWithValue("hash", hash);
            command.Parameters.AddWithValue("envelope", Encoding.UTF8.GetString(envelope));
            command.Parameters.AddWithValue("expected", _revision);
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The durable recurring journal state changed outside this session's lease. Reopen the store before retrying.");
            _revision++;
        }

        public async ValueTask DisposeAsync()
        {
            await owner.ReleaseAsync(connection).ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
