using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>One Security Master exception case (review D2).</summary>
public sealed record SecurityMasterExceptionCase(
    Guid CaseId,
    string CaseKey,
    string Category,
    string RuleCode,
    Guid? SecurityId,
    string Severity,
    string Title,
    string Detail,
    string Status,
    string? Assignee,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset SlaDueAt,
    DateTimeOffset? ClosedAt,
    string? ClosedReason)
{
    public bool IsBreached(DateTimeOffset asOf) => Status == SecurityMasterExceptionCaseStatus.Open && SlaDueAt < asOf;
}

/// <summary>Case statuses.</summary>
public static class SecurityMasterExceptionCaseStatus
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}

/// <summary>A source condition the sync wants represented by an open case.</summary>
public sealed record SecurityMasterExceptionObservation(
    string CaseKey,
    string Category,
    string RuleCode,
    Guid? SecurityId,
    string Severity,
    string Title,
    string Detail,
    DateTimeOffset SlaDueAt);

/// <summary>Result of reconciling observed conditions with open cases.</summary>
public sealed record SecurityMasterExceptionSyncResult(int Opened, int Refreshed, int Closed);

/// <summary>Deployment-global Security Master exception cases (migration 074).</summary>
public interface ISecurityMasterExceptionCaseStore
{
    /// <summary>
    /// Makes the open cases of <paramref name="categories"/> match <paramref name="observations"/>:
    /// opens a case for each new key, refreshes existing ones, and closes open cases whose key was not
    /// observed. Idempotent for the same input.
    /// </summary>
    Task<SecurityMasterExceptionSyncResult> SyncAsync(
        IReadOnlyCollection<string> categories,
        IReadOnlyList<SecurityMasterExceptionObservation> observations,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<IReadOnlyList<SecurityMasterExceptionCase>> ListAsync(string? status, CancellationToken ct = default);

    Task<bool> AssignAsync(Guid caseId, string assignee, CancellationToken ct = default);
}

/// <summary>Process-local exception cases.</summary>
public sealed class InMemorySecurityMasterExceptionCaseStore : ISecurityMasterExceptionCaseStore
{
    private readonly ConcurrentDictionary<Guid, SecurityMasterExceptionCase> _cases = new();
    private readonly object _gate = new();

    public Task<SecurityMasterExceptionSyncResult> SyncAsync(
        IReadOnlyCollection<string> categories, IReadOnlyList<SecurityMasterExceptionObservation> observations, DateTimeOffset now, CancellationToken ct = default)
    {
        lock (_gate)
        {
            int opened = 0, refreshed = 0, closed = 0;
            var observedKeys = observations.Select(static o => o.CaseKey).ToHashSet(StringComparer.Ordinal);
            var open = _cases.Values.Where(static c => c.Status == SecurityMasterExceptionCaseStatus.Open).ToDictionary(static c => c.CaseKey, StringComparer.Ordinal);
            foreach (var observation in observations.DistinctBy(static o => o.CaseKey))
            {
                if (open.TryGetValue(observation.CaseKey, out var existing))
                {
                    _cases[existing.CaseId] = existing with { LastSeenAt = now, Detail = observation.Detail, Severity = observation.Severity };
                    refreshed++;
                }
                else
                {
                    var created = new SecurityMasterExceptionCase(Guid.NewGuid(), observation.CaseKey, observation.Category, observation.RuleCode,
                        observation.SecurityId, observation.Severity, observation.Title, observation.Detail, SecurityMasterExceptionCaseStatus.Open,
                        null, now, now, observation.SlaDueAt, null, null);
                    _cases[created.CaseId] = created;
                    opened++;
                }
            }

            foreach (var stale in open.Values.Where(c => categories.Contains(c.Category) && !observedKeys.Contains(c.CaseKey)))
            {
                _cases[stale.CaseId] = stale with { Status = SecurityMasterExceptionCaseStatus.Closed, ClosedAt = now, ClosedReason = "Source condition no longer observed." };
                closed++;
            }

            return Task.FromResult(new SecurityMasterExceptionSyncResult(opened, refreshed, closed));
        }
    }

    public Task<IReadOnlyList<SecurityMasterExceptionCase>> ListAsync(string? status, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SecurityMasterExceptionCase>>(_cases.Values
            .Where(c => status is null || string.Equals(c.Status, status, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static c => c.SlaDueAt).ThenBy(static c => c.CaseKey, StringComparer.Ordinal).ToArray());

    public Task<bool> AssignAsync(Guid caseId, string assignee, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_cases.TryGetValue(caseId, out var existing) || existing.Status != SecurityMasterExceptionCaseStatus.Open)
                return Task.FromResult(false);
            _cases[caseId] = existing with { Assignee = assignee };
            return Task.FromResult(true);
        }
    }
}

/// <summary>PostgreSQL exception cases (migration 074).</summary>
public sealed class PostgresSecurityMasterExceptionCaseStore : ISecurityMasterExceptionCaseStore
{
    private const string Columns =
        "case_id, case_key, category, rule_code, security_id, severity, title, detail, status, assignee, opened_at, last_seen_at, sla_due_at, closed_at, closed_reason";

    private readonly SecurityMasterOptions _options;

    public PostgresSecurityMasterExceptionCaseStore(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private string Table => $"{_options.Schema}.security_master_exception_cases";

    public async Task<SecurityMasterExceptionSyncResult> SyncAsync(
        IReadOnlyCollection<string> categories, IReadOnlyList<SecurityMasterExceptionObservation> observations, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var lockCommand = new NpgsqlCommand($"select pg_advisory_xact_lock(hashtext('{_options.Schema}.security_master_exception_cases'));", connection, transaction))
            await lockCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        int opened = 0, refreshed = 0;
        foreach (var observation in observations.DistinctBy(static o => o.CaseKey))
        {
            await using var refresh = new NpgsqlCommand($"""
                update {Table} set last_seen_at = @now, detail = @detail, severity = @severity
                where case_key = @key and status = 'Open';
                """, connection, transaction);
            refresh.Parameters.AddWithValue("now", now.UtcDateTime);
            refresh.Parameters.AddWithValue("detail", observation.Detail);
            refresh.Parameters.AddWithValue("severity", observation.Severity);
            refresh.Parameters.AddWithValue("key", observation.CaseKey);
            if (await refresh.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0)
            {
                refreshed++;
                continue;
            }

            await using var insert = new NpgsqlCommand($"""
                insert into {Table} (case_id, case_key, category, rule_code, security_id, severity, title, detail, status, opened_at, last_seen_at, sla_due_at)
                values (@id, @key, @category, @rule, @security, @severity, @title, @detail, 'Open', @now, @now, @due);
                """, connection, transaction);
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("key", observation.CaseKey);
            insert.Parameters.AddWithValue("category", observation.Category);
            insert.Parameters.AddWithValue("rule", observation.RuleCode);
            insert.Parameters.Add(new NpgsqlParameter("security", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)observation.SecurityId ?? DBNull.Value });
            insert.Parameters.AddWithValue("severity", observation.Severity);
            insert.Parameters.AddWithValue("title", observation.Title);
            insert.Parameters.AddWithValue("detail", observation.Detail);
            insert.Parameters.AddWithValue("now", now.UtcDateTime);
            insert.Parameters.AddWithValue("due", observation.SlaDueAt.UtcDateTime);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            opened++;
        }

        await using var close = new NpgsqlCommand($"""
            update {Table}
            set status = 'Closed', closed_at = @now, closed_reason = 'Source condition no longer observed.'
            where status = 'Open' and category = any(@categories) and not (case_key = any(@keys));
            """, connection, transaction);
        close.Parameters.AddWithValue("now", now.UtcDateTime);
        close.Parameters.Add(new NpgsqlParameter("categories", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text) { Value = categories.ToArray() });
        close.Parameters.Add(new NpgsqlParameter("keys", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text) { Value = observations.Select(static o => o.CaseKey).Distinct().ToArray() });
        var closed = await close.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new SecurityMasterExceptionSyncResult(opened, refreshed, closed);
    }

    public async Task<IReadOnlyList<SecurityMasterExceptionCase>> ListAsync(string? status, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"""
            select {Columns} from {Table}
            where (@status is null or status = @status)
            order by sla_due_at, case_key;
            """, connection);
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)status ?? DBNull.Value });
        var rows = new List<SecurityMasterExceptionCase>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new SecurityMasterExceptionCase(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9),
                new DateTimeOffset(reader.GetDateTime(10), TimeSpan.Zero), new DateTimeOffset(reader.GetDateTime(11), TimeSpan.Zero),
                new DateTimeOffset(reader.GetDateTime(12), TimeSpan.Zero),
                reader.IsDBNull(13) ? null : new DateTimeOffset(reader.GetDateTime(13), TimeSpan.Zero),
                reader.IsDBNull(14) ? null : reader.GetString(14)));
        }

        return rows;
    }

    public async Task<bool> AssignAsync(Guid caseId, string assignee, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"update {Table} set assignee = @assignee where case_id = @id and status = 'Open';", connection);
        command.Parameters.AddWithValue("assignee", assignee);
        command.Parameters.AddWithValue("id", caseId);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
