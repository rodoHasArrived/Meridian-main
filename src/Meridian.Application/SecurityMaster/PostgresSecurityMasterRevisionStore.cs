using System.Data.Common;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Npgsql;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Durable, Postgres-backed revision-lifecycle store. This is the authority a publish consults to
/// confirm a revision was actually approved through the governed gate, so the guarantee must survive
/// process recycles: an arbitrary revision id can never trigger downstream publish side effects, even
/// across restarts or multiple instances. State transitions are compare-and-set at the database so
/// concurrent or out-of-order lifecycle calls are rejected rather than silently advancing the revision.
/// </summary>
public sealed class PostgresSecurityMasterRevisionStore : ISecurityMasterRevisionStore
{
    private const string RevisionsTable = "security_master_revisions";

    private const string RevisionColumns =
        "revision_id, security_id, state, actor, created_at, updated_at, workflow_id, " +
        "field_path, field_effective_from, field_justification, fund_profile_id";

    private readonly SecurityMasterOptions _options;

    public PostgresSecurityMasterRevisionStore(SecurityMasterOptions options)
    {
        _options = options;
    }

    public Task<SecurityMasterRevisionRecord> CreateDraftAsync(Guid securityId, string actor, CancellationToken ct = default)
        => CreateDraftCoreAsync(securityId, actor, fieldPath: null, fieldEffectiveFrom: null, fieldJustification: null, fundProfileId: null, ct);

    public Task<SecurityMasterRevisionRecord> CreateDraftAsync(
        Guid securityId,
        string actor,
        string fieldPath,
        DateTimeOffset fieldEffectiveFrom,
        string fieldJustification,
        string? fundProfileId = null,
        CancellationToken ct = default)
        => CreateDraftCoreAsync(securityId, actor, fieldPath, fieldEffectiveFrom, fieldJustification, fundProfileId, ct);

    private async Task<SecurityMasterRevisionRecord> CreateDraftCoreAsync(
        Guid securityId,
        string actor,
        string? fieldPath,
        DateTimeOffset? fieldEffectiveFrom,
        string? fieldJustification,
        string? fundProfileId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new SecurityMasterRevisionRecord(
            RevisionId: Guid.NewGuid(),
            SecurityId: securityId,
            State: SecurityMasterRevisionStateDto.Draft,
            Actor: actor,
            CreatedAt: now,
            UpdatedAt: now,
            WorkflowId: null,
            FieldPath: fieldPath,
            FieldEffectiveFrom: fieldEffectiveFrom,
            FieldJustification: fieldJustification,
            FundProfileId: string.IsNullOrWhiteSpace(fundProfileId) ? null : fundProfileId.Trim());

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified(RevisionsTable)} (
                revision_id, security_id, state, actor, created_at, updated_at, workflow_id,
                field_path, field_effective_from, field_justification, fund_profile_id)
            values (
                @revision_id, @security_id, @state, @actor, @created_at, @updated_at, @workflow_id,
                @field_path, @field_effective_from, @field_justification, @fund_profile_id);
            """;
        command.Parameters.AddWithValue("revision_id", record.RevisionId);
        command.Parameters.AddWithValue("security_id", record.SecurityId);
        command.Parameters.AddWithValue("state", record.State.ToString());
        command.Parameters.AddWithValue("actor", record.Actor);
        command.Parameters.AddWithValue("created_at", record.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", record.UpdatedAt.UtcDateTime);
        command.Parameters.AddWithValue("workflow_id", (object?)record.WorkflowId ?? DBNull.Value);
        command.Parameters.AddWithValue("field_path", (object?)record.FieldPath ?? DBNull.Value);
        command.Parameters.AddWithValue("field_effective_from", (object?)record.FieldEffectiveFrom?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("field_justification", (object?)record.FieldJustification ?? DBNull.Value);
        command.Parameters.AddWithValue("fund_profile_id", (object?)record.FundProfileId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return record;
    }

    public async Task<SecurityMasterRevisionRecord?> GetAsync(Guid revisionId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select {RevisionColumns}
            from {Qualified(RevisionsTable)}
            where revision_id = @revision_id;
            """;
        command.Parameters.AddWithValue("revision_id", revisionId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapRevision(reader) : null;
    }

    public async Task<IReadOnlyList<SecurityMasterRevisionRecord>> ListBySecurityAsync(
        Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select {RevisionColumns}
            from {Qualified(RevisionsTable)}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var revisions = new List<SecurityMasterRevisionRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            revisions.Add(MapRevision(reader));
        }

        return revisions;
    }

    public async Task<SecurityMasterRevisionRecord> TransitionAsync(
        Guid revisionId,
        SecurityMasterRevisionStateDto expected,
        SecurityMasterRevisionStateDto next,
        string actor,
        Guid? workflowIdForSubmit = null,
        CancellationToken ct = default)
    {
        // Bind the submitting workflow only on the Draft→Submitted transition; other transitions
        // preserve the existing binding so approval can be restricted to the same lane.
        var bindWorkflow = next == SecurityMasterRevisionStateDto.Submitted && workflowIdForSubmit.HasValue;

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        // Compare-and-set at the database: the row transitions only when its current state still
        // matches the expected state, so a concurrent or out-of-order transition matches zero rows.
        command.CommandText =
            $"""
            update {Qualified(RevisionsTable)}
            set state = @next,
                actor = @actor,
                updated_at = @updated_at,
                workflow_id = case when @bind_workflow then @workflow_id::uuid else workflow_id end
            where revision_id = @revision_id and state = @expected
            returning {RevisionColumns};
            """;
        command.Parameters.AddWithValue("next", next.ToString());
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("updated_at", DateTimeOffset.UtcNow.UtcDateTime);
        command.Parameters.AddWithValue("bind_workflow", bindWorkflow);
        command.Parameters.AddWithValue("workflow_id", (object?)workflowIdForSubmit ?? DBNull.Value);
        command.Parameters.AddWithValue("revision_id", revisionId);
        command.Parameters.AddWithValue("expected", expected.ToString());

        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return MapRevision(reader);
            }
        }

        // Zero rows updated: report whether the revision is missing or simply in a different state.
        var current = await GetAsync(revisionId, ct).ConfigureAwait(false);
        throw current is null
            ? new SecurityMasterRevisionStateException(revisionId, $"no such revision (expected state {expected}).")
            : new SecurityMasterRevisionStateException(revisionId, $"expected state {expected} but the revision is {current.State}.");
    }

    private static SecurityMasterRevisionRecord MapRevision(DbDataReader reader) => new(
        RevisionId: reader.GetGuid(0),
        SecurityId: reader.GetGuid(1),
        State: SecurityMasterEnumReads.ParseOrFallback(reader.GetString(2), SecurityMasterRevisionStateDto.Unknown),
        Actor: reader.GetString(3),
        CreatedAt: new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
        UpdatedAt: new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero),
        WorkflowId: reader.IsDBNull(6) ? null : reader.GetGuid(6),
        FieldPath: reader.IsDBNull(7) ? null : reader.GetString(7),
        FieldEffectiveFrom: reader.IsDBNull(8) ? null : new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
        FieldJustification: reader.IsDBNull(9) ? null : reader.GetString(9),
        FundProfileId: reader.IsDBNull(10) ? null : reader.GetString(10));

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";
}
