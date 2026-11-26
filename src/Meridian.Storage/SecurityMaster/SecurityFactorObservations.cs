using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>One recorded pool-factor observation (review C3).</summary>
public sealed record SecurityFactorObservationRecord(
    Guid ObservationId,
    Guid SecurityId,
    DateOnly EffectiveDate,
    decimal Factor,
    string Source,
    string? EvidenceRef,
    DateTimeOffset RecordedAt,
    string RecordedBy);

/// <summary>Append-only pool-factor observations (migration 075).</summary>
public interface ISecurityFactorObservationStore
{
    Task<SecurityFactorObservationRecord> RecordAsync(
        Guid securityId, DateOnly effectiveDate, decimal factor, string source, string? evidenceRef, string actor, CancellationToken ct = default);

    /// <summary>Every observation of the security, oldest recording first.</summary>
    Task<IReadOnlyList<SecurityFactorObservationRecord>> ListAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>The effective observation per date: the latest recorded one.</summary>
    async Task<IReadOnlyList<SecurityFactorObservationRecord>> ListEffectiveAsync(Guid securityId, CancellationToken ct = default)
        => (await ListAsync(securityId, ct).ConfigureAwait(false))
            .GroupBy(static observation => observation.EffectiveDate)
            .Select(static group => group.OrderBy(static o => o.RecordedAt).ThenBy(static o => o.ObservationId).Last())
            .OrderBy(static observation => observation.EffectiveDate)
            .ToArray();
}

/// <summary>Process-local factor observations.</summary>
public sealed class InMemorySecurityFactorObservationStore : ISecurityFactorObservationStore
{
    private readonly ConcurrentQueue<SecurityFactorObservationRecord> _rows = new();

    public Task<SecurityFactorObservationRecord> RecordAsync(
        Guid securityId, DateOnly effectiveDate, decimal factor, string source, string? evidenceRef, string actor, CancellationToken ct = default)
    {
        SecurityFactorObservationRules.Validate(factor, source);
        var record = new SecurityFactorObservationRecord(Guid.NewGuid(), securityId, effectiveDate, factor, source.Trim(), evidenceRef, DateTimeOffset.UtcNow, actor);
        _rows.Enqueue(record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<SecurityFactorObservationRecord>> ListAsync(Guid securityId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SecurityFactorObservationRecord>>(_rows.Where(row => row.SecurityId == securityId).ToArray());
}

/// <summary>PostgreSQL factor observations (migration 075).</summary>
public sealed class PostgresSecurityFactorObservationStore : ISecurityFactorObservationStore
{
    private readonly SecurityMasterOptions _options;

    public PostgresSecurityFactorObservationStore(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private string Table => $"{_options.Schema}.security_factor_observations";

    public async Task<SecurityFactorObservationRecord> RecordAsync(
        Guid securityId, DateOnly effectiveDate, decimal factor, string source, string? evidenceRef, string actor, CancellationToken ct = default)
    {
        SecurityFactorObservationRules.Validate(factor, source);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"""
            insert into {Table} (observation_id, security_id, effective_date, factor, source, evidence_ref, recorded_by)
            values (@id, @security, @date, @factor, @source, @evidence, @by)
            returning observation_id, security_id, effective_date, factor, source, evidence_ref, recorded_at, recorded_by;
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("security", securityId);
        command.Parameters.AddWithValue("date", effectiveDate);
        command.Parameters.AddWithValue("factor", factor);
        command.Parameters.AddWithValue("source", source.Trim());
        command.Parameters.Add(new NpgsqlParameter("evidence", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)evidenceRef ?? DBNull.Value });
        command.Parameters.AddWithValue("by", actor);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await reader.ReadAsync(ct).ConfigureAwait(false);
        return Map(reader);
    }

    public async Task<IReadOnlyList<SecurityFactorObservationRecord>> ListAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"""
            select observation_id, security_id, effective_date, factor, source, evidence_ref, recorded_at, recorded_by
            from {Table} where security_id = @security order by recorded_at, observation_id;
            """, connection);
        command.Parameters.AddWithValue("security", securityId);
        var rows = new List<SecurityFactorObservationRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(Map(reader));
        return rows;
    }

    private static SecurityFactorObservationRecord Map(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<DateOnly>(2), reader.GetDecimal(3), reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5), new DateTimeOffset(reader.GetDateTime(6), TimeSpan.Zero), reader.GetString(7));

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}

internal static class SecurityFactorObservationRules
{
    public static void Validate(decimal factor, string source)
    {
        if (factor is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "A pool factor lies in [0, 1].");
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
    }
}
