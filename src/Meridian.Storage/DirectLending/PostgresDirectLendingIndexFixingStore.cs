using System.Data;
using Meridian.Contracts.DirectLending;
using Npgsql;

namespace Meridian.Storage.DirectLending;

/// <summary>
/// PostgreSQL fixing store over <c>loan_index_fixing</c> (direct-lending migration 011).
/// Revisions are assigned under a transaction-scoped advisory lock per (index, date), so two
/// concurrent corrections cannot claim the same revision.
/// </summary>
public sealed class PostgresDirectLendingIndexFixingStore : IDirectLendingIndexFixingStore
{
    private readonly DirectLendingOptions _options;

    public PostgresDirectLendingIndexFixingStore(DirectLendingOptions options)
    {
        _options = options;
    }

    public async Task<DirectLendingIndexFixingDto> RecordAsync(RecordIndexFixingRequest request, decimal rateFraction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var indexName = InMemoryDirectLendingIndexFixingStore.NormalizeIndexName(request.IndexName);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);

        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = "select pg_advisory_xact_lock(hashtextextended(@lock_key, 0));";
            lockCommand.Parameters.AddWithValue("lock_key", $"direct-lending-fixing:{indexName}:{request.ObservationDate:yyyy-MM-dd}");
            await lockCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        int revision;
        await using (var revisionCommand = connection.CreateCommand())
        {
            revisionCommand.Transaction = transaction;
            revisionCommand.CommandText =
                $"""
                select coalesce(max(revision), 0) + 1
                from {Qualified("loan_index_fixing")}
                where index_name = @index_name and observation_date = @observation_date;
                """;
            revisionCommand.Parameters.AddWithValue("index_name", indexName);
            revisionCommand.Parameters.AddWithValue("observation_date", request.ObservationDate);
            revision = Convert.ToInt32(await revisionCommand.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        var fixing = new DirectLendingIndexFixingDto(
            Guid.NewGuid(),
            indexName,
            request.ObservationDate,
            revision,
            request.Rate,
            rateFraction,
            request.SourceRef,
            // PostgreSQL timestamptz keeps microseconds; truncate so the returned DTO equals a re-read.
            new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / 10 * 10, TimeSpan.Zero),
            request.RecordedBy);

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("loan_index_fixing")} (
                    fixing_id, index_name, observation_date, revision, declared_value, declared_unit,
                    rate_fraction, source_ref, recorded_at, recorded_by)
                values (
                    @fixing_id, @index_name, @observation_date, @revision, @declared_value, @declared_unit,
                    @rate_fraction, @source_ref, @recorded_at, @recorded_by);
                """;
            insert.Parameters.AddWithValue("fixing_id", fixing.FixingId);
            insert.Parameters.AddWithValue("index_name", fixing.IndexName);
            insert.Parameters.AddWithValue("observation_date", fixing.ObservationDate);
            insert.Parameters.AddWithValue("revision", fixing.Revision);
            insert.Parameters.AddWithValue("declared_value", fixing.DeclaredRate.Value);
            insert.Parameters.AddWithValue("declared_unit", fixing.DeclaredRate.Unit.ToString());
            insert.Parameters.AddWithValue("rate_fraction", fixing.RateFraction);
            insert.Parameters.AddWithValue("source_ref", (object?)fixing.SourceRef ?? DBNull.Value);
            insert.Parameters.AddWithValue("recorded_at", fixing.RecordedAt.UtcDateTime);
            insert.Parameters.AddWithValue("recorded_by", (object?)fixing.RecordedBy ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return fixing;
    }

    public async Task<DirectLendingIndexFixingDto?> GetEffectiveAsync(string indexName, DateOnly observationDate, CancellationToken ct = default)
    {
        var results = await QueryAsync(
            InMemoryDirectLendingIndexFixingStore.NormalizeIndexName(indexName),
            observationDate,
            observationDate,
            ct).ConfigureAwait(false);
        return results.Count == 0 ? null : results[0];
    }

    public Task<IReadOnlyList<DirectLendingIndexFixingDto>> ListAsync(string? indexName = null, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
        => QueryAsync(
            string.IsNullOrWhiteSpace(indexName) ? null : InMemoryDirectLendingIndexFixingStore.NormalizeIndexName(indexName),
            from,
            to,
            ct);

    private async Task<IReadOnlyList<DirectLendingIndexFixingDto>> QueryAsync(string? indexName, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select distinct on (index_name, observation_date)
                   fixing_id, index_name, observation_date, revision, declared_value, declared_unit,
                   rate_fraction, source_ref, recorded_at, recorded_by
            from {Qualified("loan_index_fixing")}
            where (@index_name::text is null or index_name = @index_name::text)
              and (@from_date::date is null or observation_date >= @from_date::date)
              and (@to_date::date is null or observation_date <= @to_date::date)
            order by index_name, observation_date, revision desc;
            """;
        command.Parameters.Add(new NpgsqlParameter("index_name", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)indexName ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("from_date", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)from ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("to_date", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)to ?? DBNull.Value });

        var results = new List<DirectLendingIndexFixingDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new DirectLendingIndexFixingDto(
                reader.GetGuid(0),
                reader.GetString(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.GetInt32(3),
                new DeclaredRate(reader.GetDecimal(4), Enum.Parse<DirectLendingRateUnit>(reader.GetString(5), ignoreCase: true)),
                reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return results;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("DirectLendingOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";
}
