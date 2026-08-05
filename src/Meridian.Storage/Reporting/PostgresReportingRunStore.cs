using System.Collections.Immutable;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Reporting;

/// <summary>
/// PostgreSQL-backed operational snapshot store for reporting runs. Run identities are isolated by
/// tenant, exact retries are no-ops, and every retained JSON payload is re-hashed before use.
/// </summary>
public sealed class PostgresReportingRunStore : IReportingRunStore
{
    private const int MaximumIdentityLength = 256;

    private readonly ReportingArtifactStoreOptions _options;
    private readonly string _runTable;
    private readonly string _runClaimTable;

    public PostgresReportingRunStore(ReportingArtifactStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ReportingDistributionStoreGuard.ValidateIdentifier(_options.Schema, nameof(options.Schema));
        _runTable = $"\"{_options.Schema}\".\"reporting_run_snapshots\"";
        _runClaimTable = $"\"{_options.Schema}\".\"reporting_run_create_claims\"";
    }

    public IReadOnlyList<ReportingRunSnapshot> ListRuns(int limit = 25)
    {
        var boundedLimit = Math.Clamp(limit, 1, 200);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select tenant_id,
                   run_id,
                   manifest_payload::text,
                   audit_payload::text,
                   updated_at_utc,
                   certified_dataset_hash_sha256,
                   manifest_hash_sha256,
                   audit_hash_sha256,
                   state_hash_sha256
            from {_runTable}
            order by updated_at_utc desc, tenant_id, run_id_key
            limit @limit;
            """;
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, boundedLimit);
        return ReadSnapshots(command);
    }

    public IReadOnlyList<ReportingRunSnapshot> ListRuns(string tenantId, int limit = 25)
    {
        var normalizedTenantId = ReportingOperationalStoreJson.NormalizeRequired(
            tenantId,
            nameof(tenantId),
            MaximumIdentityLength,
            requireCanonical: true);
        var boundedLimit = Math.Clamp(limit, 1, 200);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select tenant_id,
                   run_id,
                   manifest_payload::text,
                   audit_payload::text,
                   updated_at_utc,
                   certified_dataset_hash_sha256,
                   manifest_hash_sha256,
                   audit_hash_sha256,
                   state_hash_sha256
            from {_runTable}
            where tenant_id = @tenant_id
            order by updated_at_utc desc, run_id_key
            limit @limit;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, normalizedTenantId);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, boundedLimit);
        return ReadSnapshots(command);
    }

    public IReadOnlyList<ReportingRunSnapshot> ListRuns(
        string tenantId,
        string? companyId,
        int limit = 25) =>
        ListRuns(tenantId, companyId, offset: 0, limit: limit);

    public IReadOnlyList<ReportingRunSnapshot> ListRuns(
        string tenantId,
        string? companyId,
        int offset,
        int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (string.IsNullOrWhiteSpace(companyId))
        {
            var normalizedTenantOnly = ReportingOperationalStoreJson.NormalizeRequired(
                tenantId,
                nameof(tenantId),
                MaximumIdentityLength,
                requireCanonical: true);
            return ReadRunPage(
                normalizedTenantOnly,
                companyId: null,
                offset: offset,
                limit: limit);
        }

        var normalizedTenantId = ReportingOperationalStoreJson.NormalizeRequired(
            tenantId,
            nameof(tenantId),
            MaximumIdentityLength,
            requireCanonical: true);
        var normalizedCompanyId = ReportingOperationalStoreJson.NormalizeRequired(
            companyId,
            nameof(companyId),
            MaximumIdentityLength,
            requireCanonical: true);
        return ReadRunPage(normalizedTenantId, normalizedCompanyId, offset, limit);
    }

    private IReadOnlyList<ReportingRunSnapshot> ReadRunPage(
        string tenantId,
        string? companyId,
        int offset,
        int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, 200);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select tenant_id,
                   run_id,
                   manifest_payload::text,
                   audit_payload::text,
                   updated_at_utc,
                   certified_dataset_hash_sha256,
                   manifest_hash_sha256,
                   audit_hash_sha256,
                   state_hash_sha256
            from {_runTable}
            where tenant_id = @tenant_id
              and (@company_id is null
                   or manifest_payload #>> @company_path = @company_id)
            order by updated_at_utc desc, run_id_key
            offset @offset
            limit @limit;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        var companyParameter = command.Parameters.Add("company_id", NpgsqlDbType.Text);
        companyParameter.Value = companyId is null ? DBNull.Value : companyId;
        command.Parameters.AddWithValue(
            "company_path",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            new[] { "operationalScope", "companyId" });
        command.Parameters.AddWithValue("offset", NpgsqlDbType.Integer, offset);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, boundedLimit);
        return ReadSnapshots(command);
    }

    public ReportingOutputManifest? GetManifest(string runId)
    {
        var normalizedRunId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            runId,
            nameof(runId),
            MaximumIdentityLength);
        var snapshots = ReadByRunId(tenantId: null, normalizedRunId, maximumRows: 2);
        return snapshots.Count == 1 ? snapshots[0].Manifest : null;
    }

    public ReportingOutputManifest? GetManifest(string tenantId, string runId)
    {
        var normalizedTenantId = ReportingOperationalStoreJson.NormalizeRequired(
            tenantId,
            nameof(tenantId),
            MaximumIdentityLength,
            requireCanonical: true);
        var normalizedRunId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            runId,
            nameof(runId),
            MaximumIdentityLength);
        return ReadByRunId(normalizedTenantId, normalizedRunId, maximumRows: 1)
            .SingleOrDefault()
            ?.Manifest;
    }

    public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId)
    {
        var normalizedRunId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            runId,
            nameof(runId),
            MaximumIdentityLength);
        var snapshots = ReadByRunId(tenantId: null, normalizedRunId, maximumRows: 2);
        return snapshots.Count == 1 ? snapshots[0].AuditTrail : [];
    }

    public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string tenantId, string runId)
    {
        var normalizedTenantId = ReportingOperationalStoreJson.NormalizeRequired(
            tenantId,
            nameof(tenantId),
            MaximumIdentityLength,
            requireCanonical: true);
        var normalizedRunId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            runId,
            nameof(runId),
            MaximumIdentityLength);
        return ReadByRunId(normalizedTenantId, normalizedRunId, maximumRows: 1)
            .SingleOrDefault()
            ?.AuditTrail
            ?? [];
    }

    public string? GetRevision(string runId)
    {
        var normalizedRunId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            runId,
            nameof(runId),
            MaximumIdentityLength);
        var snapshots = ReadByRunId(tenantId: null, normalizedRunId, maximumRows: 2);
        return snapshots.Count == 1
            ? ReportingRunStoreRevision.Compute(
                snapshots[0].Manifest,
                snapshots[0].AuditTrail)
            : null;
    }

    public string? GetRevision(string tenantId, string runId)
    {
        var normalizedTenantId = ReportingOperationalStoreJson.NormalizeRequired(
            tenantId,
            nameof(tenantId),
            MaximumIdentityLength,
            requireCanonical: true);
        var normalizedRunId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            runId,
            nameof(runId),
            MaximumIdentityLength);
        var snapshot = ReadByRunId(normalizedTenantId, normalizedRunId, maximumRows: 1)
            .SingleOrDefault();
        return snapshot is null
            ? null
            : ReportingRunStoreRevision.Compute(snapshot.Manifest, snapshot.AuditTrail);
    }

    public async Task<ReportingRunCreateClaimResult> TryClaimCreateAsync(
        string tenantId,
        string runId,
        string leaseOwner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        var normalizedTenantId = ReportingOperationalStoreJson.NormalizeRequired(
            tenantId,
            nameof(tenantId),
            MaximumIdentityLength,
            requireCanonical: true);
        var normalizedRunId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            runId,
            nameof(runId),
            MaximumIdentityLength);
        var normalizedOwner = ReportingOperationalStoreJson.NormalizeRequired(
            leaseOwner,
            nameof(leaseOwner),
            MaximumIdentityLength,
            requireCanonical: true);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);
        await AcquireIdentityLockAsync(
                connection,
                transaction,
                normalizedTenantId,
                normalizedRunId,
                ct)
            .ConfigureAwait(false);

        await using (var existingCommand = connection.CreateCommand())
        {
            existingCommand.Transaction = transaction;
            existingCommand.CommandText =
                $"""
                select exists (
                    select 1
                    from {_runTable}
                    where tenant_id = @tenant_id
                      and run_id_key = @run_id_key);
                """;
            existingCommand.Parameters.AddWithValue(
                "tenant_id",
                NpgsqlDbType.Text,
                normalizedTenantId);
            existingCommand.Parameters.AddWithValue(
                "run_id_key",
                NpgsqlDbType.Text,
                normalizedRunId.ToLowerInvariant());
            if ((bool)(await existingCommand.ExecuteScalarAsync(ct).ConfigureAwait(false)
                       ?? false))
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return new ReportingRunCreateClaimResult(
                    ReportingRunCreateClaimStatus.AlreadyExists);
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {_runClaimTable} as retained_claim (
                tenant_id,
                run_id,
                run_id_key,
                lease_owner,
                claimed_at_utc,
                lease_expires_at_utc,
                lease_version)
            values (
                @tenant_id,
                @run_id,
                @run_id_key,
                @lease_owner,
                clock_timestamp(),
                clock_timestamp() + @lease_duration,
                1)
            on conflict (tenant_id, run_id_key)
            do update
               set lease_owner = excluded.lease_owner,
                   claimed_at_utc = clock_timestamp(),
                   lease_expires_at_utc = clock_timestamp() + @lease_duration,
                   lease_version = retained_claim.lease_version + 1
             where retained_claim.lease_expires_at_utc <= clock_timestamp()
                or retained_claim.lease_owner = excluded.lease_owner
            returning lease_expires_at_utc, lease_version;
            """;
        command.Parameters.AddWithValue(
            "tenant_id",
            NpgsqlDbType.Text,
            normalizedTenantId);
        command.Parameters.AddWithValue(
            "run_id",
            NpgsqlDbType.Text,
            normalizedRunId);
        command.Parameters.AddWithValue(
            "run_id_key",
            NpgsqlDbType.Text,
            normalizedRunId.ToLowerInvariant());
        command.Parameters.AddWithValue(
            "lease_owner",
            NpgsqlDbType.Text,
            normalizedOwner);
        command.Parameters.AddWithValue(
            "lease_duration",
            NpgsqlDbType.Interval,
            leaseDuration);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            await reader.DisposeAsync().ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new ReportingRunCreateClaimResult(
                ReportingRunCreateClaimStatus.LeasedByAnotherOwner);
        }

        var expiresAtUtc = ReportingDistributionStoreGuard.ReadUtcTimestamp(reader, 0);
        var leaseVersion = reader.GetInt64(1);
        await reader.DisposeAsync().ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new ReportingRunCreateClaimResult(
            ReportingRunCreateClaimStatus.Acquired,
            expiresAtUtc,
            leaseVersion);
    }

    public async Task<bool> RenewCreateClaimAsync(
        string tenantId,
        string runId,
        string leaseOwner,
        long leaseVersion,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        var normalizedTenantId = ReportingOperationalStoreJson.NormalizeRequired(
            tenantId,
            nameof(tenantId),
            MaximumIdentityLength,
            requireCanonical: true);
        var normalizedRunId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            runId,
            nameof(runId),
            MaximumIdentityLength);
        var normalizedOwner = ReportingOperationalStoreJson.NormalizeRequired(
            leaseOwner,
            nameof(leaseOwner),
            MaximumIdentityLength,
            requireCanonical: true);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseVersion);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            update {_runClaimTable}
               set lease_expires_at_utc = clock_timestamp() + @lease_duration
             where tenant_id = @tenant_id
               and run_id_key = @run_id_key
               and lease_owner = @lease_owner
               and lease_version = @lease_version
               and lease_expires_at_utc > clock_timestamp();
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, normalizedTenantId);
        command.Parameters.AddWithValue(
            "run_id_key",
            NpgsqlDbType.Text,
            normalizedRunId.ToLowerInvariant());
        command.Parameters.AddWithValue("lease_owner", NpgsqlDbType.Text, normalizedOwner);
        command.Parameters.AddWithValue("lease_version", NpgsqlDbType.Bigint, leaseVersion);
        command.Parameters.AddWithValue(
            "lease_duration",
            NpgsqlDbType.Interval,
            leaseDuration);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task ReleaseCreateClaimAsync(
        string tenantId,
        string runId,
        string leaseOwner,
        long leaseVersion,
        CancellationToken ct = default)
    {
        var normalizedTenantId = ReportingOperationalStoreJson.NormalizeRequired(
            tenantId,
            nameof(tenantId),
            MaximumIdentityLength,
            requireCanonical: true);
        var normalizedRunId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            runId,
            nameof(runId),
            MaximumIdentityLength);
        var normalizedOwner = ReportingOperationalStoreJson.NormalizeRequired(
            leaseOwner,
            nameof(leaseOwner),
            MaximumIdentityLength,
            requireCanonical: true);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseVersion);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            delete from {_runClaimTable}
            where tenant_id = @tenant_id
              and run_id_key = @run_id_key
              and lease_owner = @lease_owner
              and lease_version = @lease_version;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, normalizedTenantId);
        command.Parameters.AddWithValue(
            "run_id_key",
            NpgsqlDbType.Text,
            normalizedRunId.ToLowerInvariant());
        command.Parameters.AddWithValue("lease_owner", NpgsqlDbType.Text, normalizedOwner);
        command.Parameters.AddWithValue("lease_version", NpgsqlDbType.Bigint, leaseVersion);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public Task SaveAsync(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        CancellationToken ct = default) =>
        SaveAsync(manifest, auditTrail, expectedRevision: null, ct: ct);

    public Task SaveAsync(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        string? expectedRevision,
        CancellationToken ct = default) =>
        SaveCoreAsync(
            manifest,
            auditTrail,
            expectedRevision,
            leaseOwner: null,
            leaseVersion: 0,
            ct);

    public Task SaveClaimedCreateAsync(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        string leaseOwner,
        long leaseVersion,
        CancellationToken ct = default) =>
        SaveCoreAsync(
            manifest,
            auditTrail,
            expectedRevision: null,
            leaseOwner,
            leaseVersion,
            ct);

    private async Task SaveCoreAsync(
        ReportingOutputManifest manifest,
        IReadOnlyList<ReportingRunAuditEntry> auditTrail,
        string? expectedRevision,
        string? leaseOwner,
        long leaseVersion,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ct.ThrowIfCancellationRequested();

        manifest = NormalizeManifestArrays(manifest);
        var identity = ValidateManifest(manifest);
        var retainedAudit = auditTrail.ToArray();
        ValidateAudit(retainedAudit, identity.RunId);

        var manifestJson = ReportingOperationalStoreJson.SerializeCanonical(manifest, nameof(manifest));
        var auditJson = ReportingOperationalStoreJson.SerializeCanonical(retainedAudit, nameof(auditTrail));
        var manifestHash = ReportingOperationalStoreJson.ComputeSha256(manifestJson);
        var auditHash = ReportingOperationalStoreJson.ComputeSha256(auditJson);
        var certifiedDatasetHash =
            ReportingCertifiedManifestValidation.ComputeCertifiedRowsHash(
                manifest.CertifiedDatasetRows);
        var stateHash = ComputeStateHash(
            identity.TenantId,
            identity.RunIdKey,
            manifestHash,
            auditHash,
            certifiedDatasetHash);
        var candidateRevision = ReportingRunStoreRevision.Compute(manifest, retainedAudit);
        var updatedAtUtc = DateTimeOffset.UtcNow;

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);
        await AcquireIdentityLockAsync(
                connection,
                transaction,
                identity.TenantId,
                identity.RunId,
                ct)
            .ConfigureAwait(false);
        var current = await ReadCurrentStateAsync(
                connection,
                transaction,
                identity,
                ct)
            .ConfigureAwait(false);
        await EnsureCreateClaimAsync(
                connection,
                transaction,
                identity,
                current is null,
                leaseOwner,
                leaseVersion,
                ct)
            .ConfigureAwait(false);
        if (current is null)
        {
            if (expectedRevision is not null)
            {
                throw ReportingRunConcurrencyException.ForMissing(
                    identity.TenantId,
                    identity.RunId,
                    expectedRevision);
            }

            var inserted = await InsertAsync(
                    connection,
                    transaction,
                    identity,
                    manifestJson,
                    auditJson,
                    updatedAtUtc,
                    certifiedDatasetHash,
                    manifestHash,
                    auditHash,
                    stateHash,
                    ct)
                .ConfigureAwait(false);
            if (!inserted)
            {
                var concurrent = await ReadCurrentStateAsync(
                        connection,
                        transaction,
                        identity,
                        ct)
                    .ConfigureAwait(false);
                throw ReportingRunConcurrencyException.ForConflict(
                    identity.TenantId,
                    identity.RunId,
                    expectedRevision: null,
                    concurrent?.Revision ?? "<concurrent-create>");
            }

            await DeleteCreateClaimAsync(
                    connection,
                    transaction,
                    identity,
                    leaseOwner,
                    leaseVersion,
                    ct)
                .ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return;
        }

        if (expectedRevision is null)
        {
            if (ReportingRunStoreRevision.Matches(current.Revision, candidateRevision))
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return;
            }

            throw ReportingRunConcurrencyException.ForConflict(
                identity.TenantId,
                identity.RunId,
                expectedRevision: null,
                current.Revision);
        }
        if (!ReportingRunStoreRevision.Matches(current.Revision, expectedRevision))
        {
            throw ReportingRunConcurrencyException.ForConflict(
                identity.TenantId,
                identity.RunId,
                expectedRevision,
                current.Revision);
        }
        if (ReportingRunStoreRevision.Matches(current.Revision, candidateRevision))
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return;
        }

        var updated = await UpdateAsync(
                connection,
                transaction,
                identity,
                manifestJson,
                auditJson,
                updatedAtUtc,
                certifiedDatasetHash,
                manifestHash,
                auditHash,
                stateHash,
                current.StateHashSha256,
                ct)
            .ConfigureAwait(false);
        if (!updated)
        {
            var concurrent = await ReadCurrentStateAsync(
                    connection,
                    transaction,
                    identity,
                    ct)
                .ConfigureAwait(false);
            throw ReportingRunConcurrencyException.ForConflict(
                identity.TenantId,
                identity.RunId,
                expectedRevision,
                concurrent?.Revision ?? "<missing>");
        }

        await DeleteCreateClaimAsync(
                connection,
                transaction,
                identity,
                leaseOwner,
                leaseVersion,
                ct)
            .ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task AcquireIdentityLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        string runId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "select pg_advisory_xact_lock(hashtextextended(@identity, 0));";
        command.Parameters.AddWithValue(
            "identity",
            NpgsqlDbType.Text,
            $"{_options.Schema}:{tenantId}:{runId.ToLowerInvariant()}");
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureCreateClaimAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingRunIdentity identity,
        bool isCreate,
        string? leaseOwner,
        long leaseVersion,
        CancellationToken ct)
    {
        if (leaseOwner is null && !isCreate)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (leaseOwner is null)
        {
            command.CommandText =
                $"""
                select exists (
                    select 1
                    from {_runClaimTable}
                    where tenant_id = @tenant_id
                      and run_id_key = @run_id_key
                      and lease_expires_at_utc > clock_timestamp());
                """;
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(leaseVersion);
            command.CommandText =
                $"""
                select exists (
                    select 1
                    from {_runClaimTable}
                    where tenant_id = @tenant_id
                      and run_id_key = @run_id_key
                      and lease_owner = @lease_owner
                      and lease_version = @lease_version
                      and lease_expires_at_utc > clock_timestamp());
                """;
            command.Parameters.AddWithValue(
                "lease_owner",
                NpgsqlDbType.Text,
                ReportingOperationalStoreJson.NormalizeRequired(
                    leaseOwner,
                    nameof(leaseOwner),
                    MaximumIdentityLength,
                    requireCanonical: true));
            command.Parameters.AddWithValue(
                "lease_version",
                NpgsqlDbType.Bigint,
                leaseVersion);
        }

        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, identity.TenantId);
        command.Parameters.AddWithValue(
            "run_id_key",
            NpgsqlDbType.Text,
            identity.RunIdKey);
        var exists = (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false)
                            ?? false);
        if (leaseOwner is null ? exists : !exists)
        {
            throw new ReportingRunCreateClaimException(
                identity.TenantId,
                identity.RunId,
                leaseOwner is null
                    ? "The reporting run identity has an active durable create owner."
                    : "The reporting run create lease is missing, expired, or was superseded by another owner.");
        }
    }

    private async Task DeleteCreateClaimAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingRunIdentity identity,
        string? leaseOwner,
        long leaseVersion,
        CancellationToken ct)
    {
        if (leaseOwner is null)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            delete from {_runClaimTable}
            where tenant_id = @tenant_id
              and run_id_key = @run_id_key
              and lease_owner = @lease_owner
              and lease_version = @lease_version;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, identity.TenantId);
        command.Parameters.AddWithValue("run_id_key", NpgsqlDbType.Text, identity.RunIdKey);
        command.Parameters.AddWithValue("lease_owner", NpgsqlDbType.Text, leaseOwner);
        command.Parameters.AddWithValue("lease_version", NpgsqlDbType.Bigint, leaseVersion);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
        {
            throw new ReportingRunCreateClaimException(
                identity.TenantId,
                identity.RunId,
                "The reporting run create lease could not be completed by its fenced owner.");
        }
    }

    private async Task<StoredRunState?> ReadCurrentStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingRunIdentity identity,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select tenant_id,
                   run_id,
                   manifest_payload::text,
                   audit_payload::text,
                   updated_at_utc,
                   certified_dataset_hash_sha256,
                   manifest_hash_sha256,
                   audit_hash_sha256,
                   state_hash_sha256
            from {_runTable}
            where tenant_id = @tenant_id
              and run_id_key = @run_id_key
            for update;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, identity.TenantId);
        command.Parameters.AddWithValue("run_id_key", NpgsqlDbType.Text, identity.RunIdKey);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var stateHashSha256 = reader.GetString(8);
        var snapshot = ReadSnapshot(reader);
        return new StoredRunState(
            ReportingRunStoreRevision.Compute(snapshot.Manifest, snapshot.AuditTrail),
            stateHashSha256);
    }

    private async Task<bool> InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingRunIdentity identity,
        string manifestJson,
        string auditJson,
        DateTimeOffset updatedAtUtc,
        string certifiedDatasetHash,
        string manifestHash,
        string auditHash,
        string stateHash,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {_runTable} (
                tenant_id,
                run_id,
                run_id_key,
                manifest_payload,
                audit_payload,
                updated_at_utc,
                certified_dataset_hash_sha256,
                manifest_hash_sha256,
                audit_hash_sha256,
                state_hash_sha256)
            values (
                @tenant_id,
                @run_id,
                @run_id_key,
                @manifest_payload,
                @audit_payload,
                @updated_at_utc,
                @certified_dataset_hash_sha256,
                @manifest_hash_sha256,
                @audit_hash_sha256,
                @state_hash_sha256)
            on conflict (tenant_id, run_id_key) do nothing;
            """;
        AddRunParameters(
            command,
            identity,
            manifestJson,
            auditJson,
            updatedAtUtc,
            certifiedDatasetHash,
            manifestHash,
            auditHash,
            stateHash);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    private async Task<bool> UpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingRunIdentity identity,
        string manifestJson,
        string auditJson,
        DateTimeOffset updatedAtUtc,
        string certifiedDatasetHash,
        string manifestHash,
        string auditHash,
        string stateHash,
        string retainedStateHash,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            update {_runTable}
            set run_id = @run_id,
                manifest_payload = @manifest_payload,
                audit_payload = @audit_payload,
                updated_at_utc = @updated_at_utc,
                certified_dataset_hash_sha256 = @certified_dataset_hash_sha256,
                manifest_hash_sha256 = @manifest_hash_sha256,
                audit_hash_sha256 = @audit_hash_sha256,
                state_hash_sha256 = @state_hash_sha256
            where tenant_id = @tenant_id
              and run_id_key = @run_id_key
              and state_hash_sha256 = @retained_state_hash_sha256;
            """;
        AddRunParameters(
            command,
            identity,
            manifestJson,
            auditJson,
            updatedAtUtc,
            certifiedDatasetHash,
            manifestHash,
            auditHash,
            stateHash);
        command.Parameters.AddWithValue(
            "retained_state_hash_sha256",
            NpgsqlDbType.Text,
            retainedStateHash);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    private static void AddRunParameters(
        NpgsqlCommand command,
        ReportingRunIdentity identity,
        string manifestJson,
        string auditJson,
        DateTimeOffset updatedAtUtc,
        string certifiedDatasetHash,
        string manifestHash,
        string auditHash,
        string stateHash)
    {
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, identity.TenantId);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, identity.RunId);
        command.Parameters.AddWithValue("run_id_key", NpgsqlDbType.Text, identity.RunIdKey);
        command.Parameters.AddWithValue("manifest_payload", NpgsqlDbType.Jsonb, manifestJson);
        command.Parameters.AddWithValue("audit_payload", NpgsqlDbType.Jsonb, auditJson);
        command.Parameters.AddWithValue("updated_at_utc", NpgsqlDbType.TimestampTz, updatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "certified_dataset_hash_sha256",
            NpgsqlDbType.Text,
            certifiedDatasetHash);
        command.Parameters.AddWithValue("manifest_hash_sha256", NpgsqlDbType.Text, manifestHash);
        command.Parameters.AddWithValue("audit_hash_sha256", NpgsqlDbType.Text, auditHash);
        command.Parameters.AddWithValue("state_hash_sha256", NpgsqlDbType.Text, stateHash);
    }

    private IReadOnlyList<ReportingRunSnapshot> ReadByRunId(
        string? tenantId,
        string runId,
        int maximumRows)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select tenant_id,
                   run_id,
                   manifest_payload::text,
                   audit_payload::text,
                   updated_at_utc,
                   certified_dataset_hash_sha256,
                   manifest_hash_sha256,
                   audit_hash_sha256,
                   state_hash_sha256
            from {_runTable}
            where run_id_key = @run_id_key
              and (@tenant_id is null or tenant_id = @tenant_id)
            order by tenant_id
            limit @maximum_rows;
            """;
        command.Parameters.AddWithValue(
            "run_id_key",
            NpgsqlDbType.Text,
            runId.ToLowerInvariant());
        var tenantParameter = command.Parameters.Add("tenant_id", NpgsqlDbType.Text);
        tenantParameter.Value = tenantId is null ? DBNull.Value : tenantId;
        command.Parameters.AddWithValue("maximum_rows", NpgsqlDbType.Integer, maximumRows);
        return ReadSnapshots(command);
    }

    private static IReadOnlyList<ReportingRunSnapshot> ReadSnapshots(NpgsqlCommand command)
    {
        var snapshots = new List<ReportingRunSnapshot>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            snapshots.Add(ReadSnapshot(reader));
        }

        return snapshots;
    }

    private static ReportingRunSnapshot ReadSnapshot(NpgsqlDataReader reader)
    {
        var retainedTenantId = reader.GetString(0);
        var retainedRunId = reader.GetString(1);
        var entityId = $"{retainedTenantId}/{retainedRunId}";

        try
        {
            var manifest = NormalizeManifestArrays(
                ReportingOperationalStoreJson.DeserializeRetained<ReportingOutputManifest>(
                    reader.GetString(2),
                    "run snapshot",
                    entityId));
            var audit = ReportingOperationalStoreJson.DeserializeRetained<ReportingRunAuditEntry[]>(
                reader.GetString(3),
                "run snapshot",
                entityId);
            var updatedAtUtc = ReportingDistributionStoreGuard.ReadUtcTimestamp(reader, 4);
            var retainedDatasetHash = reader.GetString(5);
            var retainedManifestHash = reader.GetString(6);
            var retainedAuditHash = reader.GetString(7);
            var retainedStateHash = reader.GetString(8);

            var identity = ValidateManifest(manifest);
            ValidateAudit(audit, identity.RunId);
            if (!string.Equals(identity.TenantId, retainedTenantId, StringComparison.Ordinal)
                || !string.Equals(identity.RunId, retainedRunId, StringComparison.Ordinal)
                || updatedAtUtc.Offset != TimeSpan.Zero)
            {
                throw new InvalidDataException("the indexed tenant/run identity does not match the retained payload");
            }

            var manifestHash = ReportingOperationalStoreJson.ComputeSha256(
                ReportingOperationalStoreJson.SerializeCanonical(manifest, nameof(manifest)));
            var auditHash = ReportingOperationalStoreJson.ComputeSha256(
                ReportingOperationalStoreJson.SerializeCanonical(audit, nameof(audit)));
            var certifiedDatasetHash =
                ReportingCertifiedManifestValidation.ComputeCertifiedRowsHash(
                    manifest.CertifiedDatasetRows);
            var stateHash = ComputeStateHash(
                identity.TenantId,
                identity.RunIdKey,
                manifestHash,
                auditHash,
                certifiedDatasetHash);
            if (!ReportingOperationalStoreJson.FixedHashEquals(retainedManifestHash, manifestHash)
                || !ReportingOperationalStoreJson.FixedHashEquals(retainedAuditHash, auditHash)
                || !ReportingOperationalStoreJson.FixedHashEquals(
                    retainedDatasetHash,
                    certifiedDatasetHash)
                || !ReportingOperationalStoreJson.FixedHashEquals(retainedStateHash, stateHash))
            {
                throw new InvalidDataException("one or more canonical JSON integrity digests do not match");
            }

            return new ReportingRunSnapshot(
                manifest,
                audit,
                updatedAtUtc,
                certifiedDatasetHash,
                manifestHash);
        }
        catch (ReportingOperationalStateCorruptionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException
            or InvalidDataException
            or InvalidOperationException
            or JsonException
            or NotSupportedException)
        {
            throw new ReportingOperationalStateCorruptionException(
                "run snapshot",
                entityId,
                ex.Message,
                ex);
        }
    }

    private static ReportingRunIdentity ValidateManifest(ReportingOutputManifest manifest)
    {
        var runId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            manifest.RunId,
            nameof(manifest.RunId),
            MaximumIdentityLength);
        _ = ReportingOperationalStoreJson.NormalizeRequired(
            manifest.TemplateId,
            nameof(manifest.TemplateId),
            MaximumIdentityLength,
            requireCanonical: true);
        if (manifest.AsOfDate == default
            || !Enum.IsDefined(manifest.Status)
            || !Enum.IsDefined(manifest.Trigger)
            || manifest.AttemptCount < 0
            || manifest.Sections.IsDefault
            || manifest.Artifacts.IsDefault
            || manifest.ReportWriterGrids.IsDefault
            || manifest.RenderedReportWriterGrids.IsDefault
            || manifest.ReportWriterGridDiffs.IsDefault
            || manifest.CertifiedDatasetRows.IsDefault)
        {
            throw new ArgumentException("Reporting run manifest state is incomplete.", nameof(manifest));
        }

        ReportingCertifiedManifestValidation.Validate(manifest);
        var scope = manifest.OperationalScope
            ?? throw new ArgumentException(
                "PostgreSQL reporting run persistence requires an immutable tenant scope.",
                nameof(manifest));
        var tenantId = ReportingOperationalStoreJson.NormalizeRequired(
            scope.TenantId,
            nameof(scope.TenantId),
            MaximumIdentityLength,
            requireCanonical: true);
        _ = ReportingOperationalStoreJson.NormalizeRequired(
            scope.OrganizationId,
            nameof(scope.OrganizationId),
            MaximumIdentityLength,
            requireCanonical: true);
        _ = ReportingOperationalStoreJson.NormalizeRequired(
            scope.BookId,
            nameof(scope.BookId),
            MaximumIdentityLength,
            requireCanonical: true);
        _ = ReportingOperationalStoreJson.NormalizeRequired(
            scope.PeriodId,
            nameof(scope.PeriodId),
            MaximumIdentityLength,
            requireCanonical: true);
        return new ReportingRunIdentity(tenantId, runId, runId.ToLowerInvariant());
    }

    private static void ValidateAudit(
        IReadOnlyList<ReportingRunAuditEntry> audit,
        string runId)
    {
        ArgumentNullException.ThrowIfNull(audit);
        foreach (var entry in audit)
        {
            if (entry is null)
            {
                throw new ArgumentException("Reporting run audit cannot contain null entries.", nameof(audit));
            }

            var entryRunId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
                entry.RunId,
                nameof(entry.RunId),
                MaximumIdentityLength);
            if (!string.Equals(entryRunId, runId, StringComparison.OrdinalIgnoreCase)
                || entry.TimestampUtc == default
                || entry.TimestampUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "Reporting run audit identity and timestamps must match the retained run.",
                    nameof(audit));
            }

            _ = ReportingOperationalStoreJson.NormalizeRequired(
                entry.Action,
                nameof(entry.Action),
                512,
                requireCanonical: true);
            _ = ReportingOperationalStoreJson.NormalizeRequired(
                entry.Actor,
                nameof(entry.Actor),
                512,
                requireCanonical: true);
            ArgumentNullException.ThrowIfNull(entry.Notes);
        }
    }

    private static string ComputeStateHash(
        string tenantId,
        string runIdKey,
        string manifestHash,
        string auditHash,
        string certifiedDatasetHash) =>
        ReportingOperationalStoreJson.ComputeSha256(
            ReportingOperationalStoreJson.SerializeCanonical(
                new
                {
                    tenantId,
                    runIdKey,
                    manifestHash,
                    auditHash,
                    certifiedDatasetHash
                },
                "reporting run state"));

    private static ReportingOutputManifest NormalizeManifestArrays(
        ReportingOutputManifest manifest) =>
        manifest with
        {
            Sections = OrEmpty(manifest.Sections),
            Artifacts = OrEmpty(manifest.Artifacts),
            ReportWriterGrids = OrEmpty(manifest.ReportWriterGrids),
            RenderedReportWriterGrids = OrEmpty(manifest.RenderedReportWriterGrids),
            ReportWriterGridDiffs = OrEmpty(manifest.ReportWriterGridDiffs),
            CertifiedDatasetRows = OrEmpty(manifest.CertifiedDatasetRows)
        };

    private static ImmutableArray<T> OrEmpty<T>(ImmutableArray<T> value) =>
        value.IsDefault ? ImmutableArray<T>.Empty : value;

    private NpgsqlConnection OpenConnection()
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        connection.Open();
        return connection;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private readonly record struct ReportingRunIdentity(
        string TenantId,
        string RunId,
        string RunIdKey);

    private sealed record StoredRunState(
        string Revision,
        string StateHashSha256);
}

/// <summary>
/// Raised when retained operational reporting state cannot be proven to match its indexed scope
/// and canonical JSON digests.
/// </summary>
public sealed class ReportingOperationalStateCorruptionException : IOException
{
    public ReportingOperationalStateCorruptionException(
        string entityType,
        string entityId,
        string detail)
        : base($"Retained reporting {entityType} '{entityId}' is corrupt: {detail}.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    public ReportingOperationalStateCorruptionException(
        string entityType,
        string entityId,
        string detail,
        Exception innerException)
        : base($"Retained reporting {entityType} '{entityId}' is corrupt: {detail}.", innerException)
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    public string EntityType { get; }

    public string EntityId { get; }
}

internal static class ReportingOperationalStoreJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string NormalizeRequired(
        string value,
        string parameterName,
        int maximumLength,
        bool requireCanonical)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"{parameterName} cannot exceed {maximumLength} characters.",
                parameterName);
        }

        if (requireCanonical && !string.Equals(value, normalized, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{parameterName} must not contain surrounding whitespace.",
                parameterName);
        }

        return normalized;
    }

    internal static string NormalizeMachineIdentity(
        string value,
        string parameterName,
        int maximumLength)
    {
        var normalized = NormalizeRequired(
            value,
            parameterName,
            maximumLength,
            requireCanonical: true);
        if (normalized.Any(static character => !char.IsAscii(character) || char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw new ArgumentException(
                $"{parameterName} must be a canonical ASCII identifier without whitespace.",
                parameterName);
        }

        return normalized;
    }

    internal static string SerializeCanonical<T>(T value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            using var document = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteCanonical(writer, document.RootElement);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (Exception ex) when (ex is JsonException
            or NotSupportedException
            or InvalidOperationException)
        {
            throw new ArgumentException(
                $"Reporting {parameterName} cannot be serialized deterministically.",
                parameterName,
                ex);
        }
    }

    internal static T DeserializeRetained<T>(
        string json,
        string entityType,
        string entityId)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                   ?? throw new JsonException("JSON value was null");
        }
        catch (Exception ex) when (ex is JsonException
            or NotSupportedException
            or InvalidOperationException)
        {
            throw new ReportingOperationalStateCorruptionException(
                entityType,
                entityId,
                ex.Message,
                ex);
        }
    }

    internal static string ComputeSha256(string value) =>
        ComputeSha256(Encoding.UTF8.GetBytes(value));

    internal static string ComputeSha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    internal static bool FixedHashEquals(string? retained, string computed)
    {
        if (!IsSha256(retained) || !IsSha256(computed))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(retained!),
            Convert.FromHexString(computed));
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element
                             .EnumerateObject()
                             .OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException($"Unsupported JSON value kind '{element.ValueKind}'.");
        }
    }
}
