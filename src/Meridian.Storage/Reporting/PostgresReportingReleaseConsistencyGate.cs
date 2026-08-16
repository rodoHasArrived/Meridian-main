using System.Buffers.Binary;
using System.Text;
using Meridian.Reporting;
using Npgsql;
using NpgsqlTypes;
using Meridian.Contracts.Integrity;

namespace Meridian.Storage.Reporting;

/// <summary>
/// PostgreSQL transaction-scoped advisory lock shared by final reporting release and governed
/// ledger-period reopen. The lock is independent of a reporting schema migration and remains
/// effective across all hosts that use the same reporting authority database.
/// </summary>
public sealed class PostgresReportingReleaseConsistencyGate : IReportingReleaseConsistencyGate
{
    private const string LockNamespace = "meridian:reporting-release-period:";
    private readonly ReportingArtifactStoreOptions _options;

    public PostgresReportingReleaseConsistencyGate(ReportingArtifactStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string accountingPeriodId,
        CancellationToken cancellationToken = default)
    {
        var normalizedPeriodId = NormalizePeriodId(accountingPeriodId);
        var lockKey = ComputeLockKey(normalizedPeriodId);
        var connection = new NpgsqlConnection(_options.ConnectionString);
        NpgsqlTransaction? transaction = null;
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "select pg_advisory_xact_lock(@lock_key);";
            command.Parameters.AddWithValue("lock_key", NpgsqlDbType.Bigint, lockKey);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new Lease(connection, transaction);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }

            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static long ComputeLockKey(string accountingPeriodId)
    {
        var normalizedPeriodId = NormalizePeriodId(accountingPeriodId);
        var payload = Encoding.UTF8.GetBytes(LockNamespace + normalizedPeriodId);
        var digest = Sha256Digest.ComputeBytes(payload);
        return BinaryPrimitives.ReadInt64BigEndian(digest);
    }

    private static string NormalizePeriodId(string accountingPeriodId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountingPeriodId);
        if (!Guid.TryParse(accountingPeriodId.Trim(), out var periodId)
            || periodId == Guid.Empty)
        {
            throw new ArgumentException(
                "Reporting release consistency requires a non-empty canonical accounting-period id.",
                nameof(accountingPeriodId));
        }

        return periodId.ToString("D");
    }

    private sealed class Lease(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
