using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Auth;
using Meridian.Storage.Archival;
using Npgsql;

namespace Meridian.Application.Auth;

public sealed class ScopedAccessStoreOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string Schema { get; init; } = "identity_access";
}

public interface IScopedAccessAssignmentStore
{
    Task<IReadOnlyList<UserAccessAssignmentDto>> ListAsync(CancellationToken ct = default);

    Task<UserAccessAssignmentDto?> GetAsync(Guid assignmentId, CancellationToken ct = default);

    Task<UserAccessAssignmentDto> CreateAsync(UserAccessAssignmentDto assignment, CancellationToken ct = default);

    Task<UserAccessAssignmentDto> ReplaceAsync(
        UserAccessAssignmentDto assignment,
        long expectedVersion,
        CancellationToken ct = default);
}

public sealed class FileScopedAccessAssignmentStore : IScopedAccessAssignmentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly object _gate = new();

    public FileScopedAccessAssignmentStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public Task<IReadOnlyList<UserAccessAssignmentDto>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<UserAccessAssignmentDto>>(ReadSnapshot().Assignments);
    }

    public Task<UserAccessAssignmentDto?> GetAsync(Guid assignmentId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var assignment = ReadSnapshot().Assignments.FirstOrDefault(candidate => candidate.AssignmentId == assignmentId);
        return Task.FromResult(assignment);
    }

    public Task<UserAccessAssignmentDto> CreateAsync(UserAccessAssignmentDto assignment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var snapshot = ReadSnapshotLocked();
            if (snapshot.Assignments.Any(candidate => candidate.AssignmentId == assignment.AssignmentId))
            {
                throw new InvalidOperationException($"Access assignment '{assignment.AssignmentId}' already exists.");
            }

            var next = snapshot.Assignments
                .Append(assignment)
                .OrderBy(candidate => candidate.PrincipalId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.ScopeKind)
                .ThenBy(candidate => candidate.ScopeId)
                .ThenBy(candidate => candidate.AssignmentId)
                .ToArray();

            SaveSnapshotLocked(new ScopedAccessAssignmentSnapshot(next), ct);
            return Task.FromResult(assignment);
        }
    }

    public Task<UserAccessAssignmentDto> ReplaceAsync(
        UserAccessAssignmentDto assignment,
        long expectedVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var snapshot = ReadSnapshotLocked();
            var assignments = snapshot.Assignments.ToList();
            var index = assignments.FindIndex(candidate => candidate.AssignmentId == assignment.AssignmentId);
            if (index < 0)
            {
                throw new KeyNotFoundException($"Access assignment '{assignment.AssignmentId}' was not found.");
            }

            if (assignments[index].Version != expectedVersion)
            {
                throw new InvalidOperationException(
                    $"Access assignment '{assignment.AssignmentId}' version conflict. Expected {expectedVersion}, found {assignments[index].Version}.");
            }

            assignments[index] = assignment;
            SaveSnapshotLocked(new ScopedAccessAssignmentSnapshot(assignments), ct);
            return Task.FromResult(assignment);
        }
    }

    private ScopedAccessAssignmentSnapshot ReadSnapshot()
    {
        lock (_gate)
        {
            return ReadSnapshotLocked();
        }
    }

    private ScopedAccessAssignmentSnapshot ReadSnapshotLocked()
    {
        if (!File.Exists(_path))
        {
            return new ScopedAccessAssignmentSnapshot([]);
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<ScopedAccessAssignmentSnapshot>(json, JsonOptions)
                ?? new ScopedAccessAssignmentSnapshot([]);
        }
        catch (JsonException)
        {
            return new ScopedAccessAssignmentSnapshot([]);
        }
    }

    private void SaveSnapshotLocked(ScopedAccessAssignmentSnapshot snapshot, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        AtomicFileWriter.WriteAsync(_path, json, ct).GetAwaiter().GetResult();
    }

    private sealed record ScopedAccessAssignmentSnapshot(IReadOnlyList<UserAccessAssignmentDto> Assignments);
}

public sealed class PostgresScopedAccessAssignmentStore : IScopedAccessAssignmentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ScopedAccessStoreOptions _options;

    public PostgresScopedAccessAssignmentStore(ScopedAccessStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task EnsureMigratedAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var schema = connection.CreateCommand();
        schema.CommandText = $"CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(_options.Schema)}";
        await schema.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await using var table = connection.CreateCommand();
        table.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {Q("user_access_assignment")} (
                assignment_id uuid PRIMARY KEY,
                principal_id text NOT NULL,
                principal_kind text NOT NULL,
                scope_kind text NOT NULL,
                scope_id uuid NULL,
                role text NOT NULL,
                role_profile_name text NULL,
                permission_names jsonb NOT NULL,
                permission_mask bigint NOT NULL,
                effective_from timestamptz NOT NULL,
                effective_to timestamptz NULL,
                granted_by text NOT NULL,
                rationale text NOT NULL,
                correlation_id text NOT NULL,
                version bigint NOT NULL,
                created_at_utc timestamptz NOT NULL,
                updated_at_utc timestamptz NOT NULL,
                revoked_by text NULL,
                revoked_at_utc timestamptz NULL,
                revocation_reason text NULL,
                last_audit_id text NULL
            );
            CREATE INDEX IF NOT EXISTS ix_user_access_assignment_principal
                ON {Q("user_access_assignment")} (principal_id, scope_kind, scope_id);
            """;
        await table.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UserAccessAssignmentDto>> ListAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {Q("user_access_assignment")} ORDER BY principal_id, scope_kind, scope_id, effective_from";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<UserAccessAssignmentDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(Read(reader));
        }

        return result;
    }

    public async Task<UserAccessAssignmentDto?> GetAsync(Guid assignmentId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {Q("user_access_assignment")} WHERE assignment_id = @assignment_id";
        command.Parameters.AddWithValue("assignment_id", assignmentId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<UserAccessAssignmentDto> CreateAsync(UserAccessAssignmentDto assignment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = BuildUpsertCommand(connection, assignment);
        command.CommandText += " ON CONFLICT (assignment_id) DO NOTHING";
        var rows = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (rows == 0)
        {
            throw new InvalidOperationException($"Access assignment '{assignment.AssignmentId}' already exists.");
        }

        return assignment;
    }

    public async Task<UserAccessAssignmentDto> ReplaceAsync(
        UserAccessAssignmentDto assignment,
        long expectedVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = BuildUpdateCommand(connection, assignment, expectedVersion);
        var rows = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (rows > 0)
        {
            return assignment;
        }

        var current = await GetAsync(assignment.AssignmentId, ct).ConfigureAwait(false);
        if (current is null)
        {
            throw new KeyNotFoundException($"Access assignment '{assignment.AssignmentId}' was not found.");
        }

        throw new InvalidOperationException(
            $"Access assignment '{assignment.AssignmentId}' version conflict. Expected {expectedVersion}, found {current.Version}.");
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("ScopedAccessStoreOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private NpgsqlCommand BuildUpsertCommand(NpgsqlConnection connection, UserAccessAssignmentDto assignment)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {Q("user_access_assignment")} (
                assignment_id, principal_id, principal_kind, scope_kind, scope_id, role,
                role_profile_name, permission_names, permission_mask, effective_from, effective_to,
                granted_by, rationale, correlation_id, version, created_at_utc, updated_at_utc,
                revoked_by, revoked_at_utc, revocation_reason, last_audit_id)
            VALUES (
                @assignment_id, @principal_id, @principal_kind, @scope_kind, @scope_id, @role,
                @role_profile_name, @permission_names::jsonb, @permission_mask, @effective_from, @effective_to,
                @granted_by, @rationale, @correlation_id, @version, @created_at_utc, @updated_at_utc,
                @revoked_by, @revoked_at_utc, @revocation_reason, @last_audit_id)
            """;
        AddParameters(command, assignment);
        return command;
    }

    private NpgsqlCommand BuildUpdateCommand(NpgsqlConnection connection, UserAccessAssignmentDto assignment, long expectedVersion)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {Q("user_access_assignment")}
            SET principal_id = @principal_id,
                principal_kind = @principal_kind,
                scope_kind = @scope_kind,
                scope_id = @scope_id,
                role = @role,
                role_profile_name = @role_profile_name,
                permission_names = @permission_names::jsonb,
                permission_mask = @permission_mask,
                effective_from = @effective_from,
                effective_to = @effective_to,
                granted_by = @granted_by,
                rationale = @rationale,
                correlation_id = @correlation_id,
                version = @version,
                created_at_utc = @created_at_utc,
                updated_at_utc = @updated_at_utc,
                revoked_by = @revoked_by,
                revoked_at_utc = @revoked_at_utc,
                revocation_reason = @revocation_reason,
                last_audit_id = @last_audit_id
            WHERE assignment_id = @assignment_id AND version = @expected_version
            """;
        AddParameters(command, assignment);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return command;
    }

    private static void AddParameters(NpgsqlCommand command, UserAccessAssignmentDto assignment)
    {
        command.Parameters.AddWithValue("assignment_id", assignment.AssignmentId);
        command.Parameters.AddWithValue("principal_id", assignment.PrincipalId);
        command.Parameters.AddWithValue("principal_kind", assignment.PrincipalKind.ToString());
        command.Parameters.AddWithValue("scope_kind", assignment.ScopeKind.ToString());
        command.Parameters.AddWithValue("scope_id", assignment.ScopeId.HasValue ? assignment.ScopeId.Value : DBNull.Value);
        command.Parameters.AddWithValue("role", assignment.Role);
        command.Parameters.AddWithValue("role_profile_name", (object?)assignment.RoleProfileName ?? DBNull.Value);
        command.Parameters.AddWithValue("permission_names", JsonSerializer.Serialize(assignment.PermissionNames, JsonOptions));
        command.Parameters.AddWithValue("permission_mask", assignment.PermissionMask);
        command.Parameters.AddWithValue("effective_from", assignment.EffectiveFrom);
        command.Parameters.AddWithValue("effective_to", assignment.EffectiveTo.HasValue ? assignment.EffectiveTo.Value : DBNull.Value);
        command.Parameters.AddWithValue("granted_by", assignment.GrantedBy);
        command.Parameters.AddWithValue("rationale", assignment.Rationale);
        command.Parameters.AddWithValue("correlation_id", assignment.CorrelationId);
        command.Parameters.AddWithValue("version", assignment.Version);
        command.Parameters.AddWithValue("created_at_utc", assignment.CreatedAtUtc);
        command.Parameters.AddWithValue("updated_at_utc", assignment.UpdatedAtUtc);
        command.Parameters.AddWithValue("revoked_by", (object?)assignment.RevokedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("revoked_at_utc", assignment.RevokedAtUtc.HasValue ? assignment.RevokedAtUtc.Value : DBNull.Value);
        command.Parameters.AddWithValue("revocation_reason", (object?)assignment.RevocationReason ?? DBNull.Value);
        command.Parameters.AddWithValue("last_audit_id", (object?)assignment.LastAuditId ?? DBNull.Value);
    }

    private static UserAccessAssignmentDto Read(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(reader.GetOrdinal("assignment_id")),
            reader.GetString(reader.GetOrdinal("principal_id")),
            Enum.Parse<AccessPrincipalKindDto>(reader.GetString(reader.GetOrdinal("principal_kind"))),
            Enum.Parse<AccessScopeKindDto>(reader.GetString(reader.GetOrdinal("scope_kind"))),
            reader.IsDBNull(reader.GetOrdinal("scope_id")) ? null : reader.GetGuid(reader.GetOrdinal("scope_id")),
            reader.GetString(reader.GetOrdinal("role")),
            reader.IsDBNull(reader.GetOrdinal("role_profile_name")) ? null : reader.GetString(reader.GetOrdinal("role_profile_name")),
            JsonSerializer.Deserialize<IReadOnlyList<string>>(reader.GetString(reader.GetOrdinal("permission_names")), JsonOptions) ?? [],
            reader.GetInt64(reader.GetOrdinal("permission_mask")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("effective_from")),
            reader.IsDBNull(reader.GetOrdinal("effective_to")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("effective_to")),
            reader.GetString(reader.GetOrdinal("granted_by")),
            reader.GetString(reader.GetOrdinal("rationale")),
            reader.GetString(reader.GetOrdinal("correlation_id")),
            reader.GetInt64(reader.GetOrdinal("version")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at_utc")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at_utc")),
            reader.IsDBNull(reader.GetOrdinal("revoked_by")) ? null : reader.GetString(reader.GetOrdinal("revoked_by")),
            reader.IsDBNull(reader.GetOrdinal("revoked_at_utc")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("revoked_at_utc")),
            reader.IsDBNull(reader.GetOrdinal("revocation_reason")) ? null : reader.GetString(reader.GetOrdinal("revocation_reason")),
            reader.IsDBNull(reader.GetOrdinal("last_audit_id")) ? null : reader.GetString(reader.GetOrdinal("last_audit_id")));

    private string Q(string table) => $"{QuoteIdentifier(_options.Schema)}.{QuoteIdentifier(table)}";

    private static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
