using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Command receipts and a cross-host lease for manual journals whose drafts live in PostgreSQL
/// (ledger migration 077). Every host sharing the draft schema takes the same session advisory lock
/// for the full command, so draft reads, validation, writes and audit are serialized across hosts as
/// the file store's exclusive lock does on one machine. Completed receipts are retained in place;
/// the database refuses to rewrite or remove them.
/// </summary>
public sealed class PostgresManualJournalMutationRecoveryStore : IManualJournalMutationRecoveryStore
{
    private readonly LedgerJournalStoreOptions _options;
    private readonly TimeProvider _clock;
    private readonly string _table;
    private readonly string _leaseKey;

    public PostgresManualJournalMutationRecoveryStore(LedgerJournalStoreOptions options, TimeProvider? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("A ledger connection string is required.", nameof(options));
        if (!System.Text.RegularExpressions.Regex.IsMatch(options.SchemaName, "^[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new ArgumentException("Invalid ledger schema name.", nameof(options));
        _clock = clock ?? TimeProvider.System;
        _table = $"\"{options.SchemaName}\".\"manual_journal_mutation_receipts\"";
        _leaseKey = $"manual-journal-mutation:{options.SchemaName}";
    }

    public async Task<IManualJournalMutationSession> OpenSessionAsync(CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using (var lease = connection.CreateCommand())
            {
                lease.CommandText = "select pg_advisory_lock(hashtextextended(@key, 0))";
                lease.Parameters.AddWithValue("key", _leaseKey);
                await lease.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            return new Session(this, connection);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class Session(PostgresManualJournalMutationRecoveryStore owner, NpgsqlConnection connection)
        : IManualJournalMutationSession
    {
        private readonly string _table = owner._table;

        public async Task<ManualJournalMutationIntent?> GetAsync(string commandKey, CancellationToken ct)
        {
            RequireKey(commandKey);
            await using var command = connection.CreateCommand();
            command.CommandText = $"select command_key, completed, digest, payload from {_table} where command_key = @key";
            command.Parameters.AddWithValue("key", commandKey);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
        }

        public async Task<IReadOnlyList<ManualJournalMutationIntent>> ListPendingAsync(CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"select command_key, completed, digest, payload from {_table} where not completed order by command_key";
            var result = new List<ManualJournalMutationIntent>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                result.Add(Read(reader));
            return result;
        }

        public async Task RetainAsync(ManualJournalMutationIntent intent, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(intent);
            RequireKey(intent.CommandKey);
            var pending = intent with { Completed = false, CompletedAtUtc = null };
            var json = Serialize(pending);
            await using var command = connection.CreateCommand();
            command.CommandText = $"insert into {_table} (command_key, completed, completed_at_utc, digest, payload) values (@key, false, null, @digest, @payload) on conflict (command_key) do nothing";
            command.Parameters.AddWithValue("key", intent.CommandKey);
            command.Parameters.AddWithValue("digest", Sha256Digest.ComputeUtf8(json));
            command.Parameters.AddWithValue("payload", json);
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("A manual journal command receipt already exists.");
        }

        public async Task CompleteAsync(ManualJournalMutationIntent intent, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(intent);
            RequireKey(intent.CommandKey);
            var existing = await GetAsync(intent.CommandKey, ct).ConfigureAwait(false);
            if (existing is { Completed: true })
                return;
            var completedAt = owner._clock.GetUtcNow();
            var json = Serialize(intent with { Completed = true, CompletedAtUtc = completedAt });
            await using var command = connection.CreateCommand();
            command.CommandText = existing is null
                ? $"insert into {_table} (command_key, completed, completed_at_utc, digest, payload) values (@key, true, @at, @digest, @payload)"
                : $"update {_table} set completed = true, completed_at_utc = @at, digest = @digest, payload = @payload where command_key = @key and not completed";
            command.Parameters.AddWithValue("key", intent.CommandKey);
            command.Parameters.AddWithValue("at", completedAt.ToUniversalTime());
            command.Parameters.AddWithValue("digest", Sha256Digest.ComputeUtf8(json));
            command.Parameters.AddWithValue("payload", json);
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The manual journal command receipt changed while this session held the lease.");
        }

        public async Task DiscardUnappliedAsync(ManualJournalMutationIntent intent, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(intent);
            RequireKey(intent.CommandKey);
            await using var command = connection.CreateCommand();
            command.CommandText = $"delete from {_table} where command_key = @key and not completed";
            command.Parameters.AddWithValue("key", intent.CommandKey);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    await using var unlock = connection.CreateCommand();
                    unlock.CommandText = "select pg_advisory_unlock(hashtextextended(@key, 0))";
                    unlock.Parameters.AddWithValue("key", owner._leaseKey);
                    await unlock.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                // Closing the session also releases any session-level advisory lock it still holds.
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static ManualJournalMutationIntent Read(NpgsqlDataReader reader)
        {
            var key = reader.GetString(0);
            var completed = reader.GetBoolean(1);
            var digest = reader.GetString(2);
            var json = reader.GetString(3);
            if (!string.Equals(digest, Sha256Digest.ComputeUtf8(json), StringComparison.Ordinal))
                throw new InvalidOperationException("Manual journal recovery receipt integrity check failed.");
            var intent = JsonSerializer.Deserialize(json, ManualJournalMutationJsonContext.Default.ManualJournalMutationIntent)
                ?? throw new InvalidOperationException("Manual journal recovery receipt is invalid.");
            if (!string.Equals(intent.CommandKey, key, StringComparison.Ordinal) || intent.Completed != completed)
                throw new InvalidOperationException("Manual journal recovery receipt identity or state disagrees with its row.");
            return intent;
        }

        private static string Serialize(ManualJournalMutationIntent intent)
            => JsonSerializer.Serialize(intent, ManualJournalMutationJsonContext.Default.ManualJournalMutationIntent);

        private static void RequireKey(string key)
        {
            if (key is null || key.Length != 64 || !key.All(char.IsAsciiHexDigit))
                throw new InvalidOperationException("Invalid manual journal command identity.");
        }
    }
}
