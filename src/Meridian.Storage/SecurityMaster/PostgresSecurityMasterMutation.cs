using System.Data;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>Participants in the same database-backed workbench mutation boundary.</summary>
public interface ISecurityMasterMutationParticipant
{
    SecurityMasterOptions MutationOptions { get; }
}

/// <summary>
/// A transaction shared only by the overlay and governed revision stores. The durable generation
/// fences readers on other instances, including the first edit (there need not be an overlay yet).
/// No external workflow, event publication or downstream handler is enlisted in this transaction.
/// </summary>
public static class PostgresSecurityMasterMutation
{
    private static readonly AsyncLocal<Session?> Current = new();

    public static async Task<T> ExecuteAsync<T>(
        SecurityMasterOptions options, Guid securityId, Func<Task<T>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Current.Value is { } nested)
        {
            EnsureScope(nested, options, securityId);
            return await operation().ConfigureAwait(false);
        }

        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        var table = $"\"{options.Schema.Replace("\"", "\"\"")}\".security_workbench_generations";

        // Read before taking the write lock. A competing committed command invalidates this
        // generation; do not silently retry a command whose validation/review assumptions changed.
        await using var read = new NpgsqlCommand($"select generation from {table} where security_id = @id;", connection, transaction);
        read.Parameters.AddWithValue("id", securityId);
        var expected = (long?)await read.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L;

        await using var seed = new NpgsqlCommand($"insert into {table} (security_id, generation) values (@id, 0) on conflict (security_id) do nothing;", connection, transaction);
        seed.Parameters.AddWithValue("id", securityId);
        await seed.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await using var fence = new NpgsqlCommand($"update {table} set generation = generation + 1 where security_id = @id and generation = @expected;", connection, transaction);
        fence.Parameters.AddWithValue("id", securityId);
        fence.Parameters.AddWithValue("expected", expected);
        if (await fence.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
        {
            throw new SecurityMasterMutationConflictException(securityId);
        }

        var session = new Session(options, securityId, connection, transaction);
        Current.Value = session;
        try
        {
            var result = await operation().ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        finally
        {
            session.Active = false;
            Current.Value = null;
            session.ConnectionGate.Dispose();
        }
    }

    public static async Task<ConnectionLease> OpenConnectionAsync(SecurityMasterOptions options, CancellationToken ct)
    {
        if (Current.Value is { } session)
        {
            EnsureScope(session, options, session.SecurityId);
            // Passport composition can fan out read requests. Npgsql permits one active command
            // per connection, so serialize participants without opening a second transaction.
            await session.ConnectionGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureScope(session, options, session.SecurityId);
                return new ConnectionLease(session.Connection, ownsConnection: false, session.Transaction, session.ConnectionGate);
            }
            catch
            {
                session.ConnectionGate.Release();
                throw;
            }
        }

        var connection = new NpgsqlConnection(options.ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return new ConnectionLease(connection, ownsConnection: true);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static NpgsqlTransaction RequireTransaction(SecurityMasterOptions options, Guid securityId)
    {
        var session = Current.Value ?? throw new InvalidOperationException("A Security Master mutation transaction is required.");
        EnsureScope(session, options, securityId);
        return session.Transaction;
    }

    public static bool SameDatabase(SecurityMasterOptions left, SecurityMasterOptions right)
        => string.Equals(left.ConnectionString, right.ConnectionString, StringComparison.Ordinal)
            && string.Equals(left.Schema, right.Schema, StringComparison.Ordinal);

    private static void EnsureScope(Session session, SecurityMasterOptions options, Guid securityId)
    {
        if (!session.Active || session.SecurityId != securityId || !SameDatabase(session.Options, options))
        {
            throw new InvalidOperationException("Security Master mutation participants must share the active security, database and schema.");
        }
    }

    private sealed record Session(SecurityMasterOptions Options, Guid SecurityId, NpgsqlConnection Connection, NpgsqlTransaction Transaction)
    {
        public bool Active { get; set; } = true;
        public SemaphoreSlim ConnectionGate { get; } = new(1, 1);
    }

    public sealed class ConnectionLease(NpgsqlConnection connection, bool ownsConnection,
        NpgsqlTransaction? transaction = null, SemaphoreSlim? gate = null) : IAsyncDisposable
    {
        public NpgsqlConnection Connection { get; } = connection;
        public NpgsqlTransaction? Transaction { get; } = transaction;
        public ValueTask DisposeAsync()
        {
            gate?.Release();
            return ownsConnection ? Connection.DisposeAsync() : ValueTask.CompletedTask;
        }
    }
}

public sealed class SecurityMasterMutationConflictException(Guid securityId)
    : InvalidOperationException($"Security Master edits for '{securityId:D}' changed on another instance. Reload and retry.")
{
    public Guid SecurityId { get; } = securityId;
}
