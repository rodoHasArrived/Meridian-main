using System.Data;
using System.Text.Json;
using Meridian.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Reporting;

/// <summary>
/// PostgreSQL outbox for reporting delivery. Jobs are claimed with row locks and skip-locked
/// leasing; state, retry metadata, and append-only receipts are committed atomically.
/// </summary>
public sealed class PostgresReportingDeliveryStore : IReportingDeliveryStore
{
    private readonly ReportingArtifactStoreOptions _options;
    private readonly string _jobTable;
    private readonly string _receiptTable;
    private readonly string _grantTable;

    public PostgresReportingDeliveryStore(ReportingArtifactStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ReportingDistributionStoreGuard.ValidateIdentifier(_options.Schema, nameof(options.Schema));
        _jobTable = $"\"{_options.Schema}\".\"reporting_delivery_jobs\"";
        _receiptTable = $"\"{_options.Schema}\".\"reporting_delivery_receipts\"";
        _grantTable = $"\"{_options.Schema}\".\"reporting_access_grants\"";
    }

    public Task<ReportingDeliveryJobRecord?> GetAsync(
        string jobId,
        CancellationToken ct = default)
    {
        var normalizedJobId = ReportingDistributionStoreGuard.NormalizeRequired(
            jobId,
            nameof(jobId),
            256);
        return ReadJobAsync("job_id", normalizedJobId, ct);
    }

    public Task<ReportingDeliveryJobRecord?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken ct = default)
    {
        ReportingDistributionStoreGuard.ValidateSha256(idempotencyKey, nameof(idempotencyKey));
        return ReadJobAsync("idempotency_key", idempotencyKey, ct);
    }

    public Task<ReportingDeliveryJobRecord?> GetByAccessGrantIdAsync(
        string accessGrantId,
        CancellationToken ct = default)
    {
        var normalizedAccessGrantId = ReportingDistributionStoreGuard.NormalizeRequired(
            accessGrantId,
            nameof(accessGrantId),
            256);
        return ReadJobAsync("access_grant_id", normalizedAccessGrantId, ct);
    }

    public async Task<IReadOnlyList<ReportingDeliveryJobRecord>> ListByPackageAsync(
        string tenantId,
        string packageId,
        CancellationToken ct = default)
    {
        var normalizedTenantId = ReportingDistributionStoreGuard.NormalizeRequired(
            tenantId,
            nameof(tenantId),
            256);
        var normalizedPackageId = ReportingDistributionStoreGuard.NormalizeRequired(
            packageId,
            nameof(packageId),
            256);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select {JobSelectList()}
            from {_jobTable}
            where tenant_id = @tenant_id
              and package_id = @package_id
            order by created_at_utc desc, job_id;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, normalizedTenantId);
        command.Parameters.AddWithValue("package_id", NpgsqlDbType.Text, normalizedPackageId);
        var baseJobs = new List<ReportingDeliveryJobRecord>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                baseJobs.Add(ReadBaseJob(reader));
            }
        }

        var jobs = new List<ReportingDeliveryJobRecord>(baseJobs.Count);
        foreach (var baseJob in baseJobs)
        {
            var receipts = await ReadReceiptsAsync(connection, transaction, baseJob, ct).ConfigureAwait(false);
            var complete = baseJob with { Receipts = receipts };
            ValidateRetainedJob(complete);
            jobs.Add(complete);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return jobs;
    }

    public async Task<IReadOnlyList<ReportingDeliveryGrantRevocationCandidate>>
        ListPendingAccessGrantRevocationsAsync(
            int take,
            CancellationToken ct = default)
    {
        if (take is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select job.job_id, job.tenant_id, job.access_grant_id
            from {_jobTable} as job
            join {_grantTable} as grant
              on grant.grant_id = job.access_grant_id
             and grant.tenant_id = job.tenant_id
            where grant.revoked_at_utc is null
              and exists (
                  select 1
                  from {_receiptTable} as receipt
                  where receipt.job_id = job.job_id
                    and receipt.tenant_id = job.tenant_id
                    and (receipt.kind in (@bounced_kind, @rejected_kind)
                        or (job.state = @failed_state
                            and receipt.kind = @failed_kind
                            and coalesce(receipt.detail, '') not like 'RELAY_OUTCOME_UNKNOWN:%'
                            and coalesce(receipt.detail, '') not like 'TRANSPORT_CANCELLED:%')))
            order by job.updated_at_utc, job.job_id
            limit @take;
            """;
        command.Parameters.AddWithValue(
            "failed_state",
            NpgsqlDbType.Integer,
            (int)ReportingDeliveryState.Failed);
        command.Parameters.AddWithValue(
            "bounced_kind",
            NpgsqlDbType.Integer,
            (int)ReportingDeliveryReceiptKind.Bounced);
        command.Parameters.AddWithValue(
            "rejected_kind",
            NpgsqlDbType.Integer,
            (int)ReportingDeliveryReceiptKind.Rejected);
        command.Parameters.AddWithValue(
            "failed_kind",
            NpgsqlDbType.Integer,
            (int)ReportingDeliveryReceiptKind.Failed);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, take);
        var candidates = new List<ReportingDeliveryGrantRevocationCandidate>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            candidates.Add(new ReportingDeliveryGrantRevocationCandidate(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return candidates;
    }

    public async Task<bool> TryCreateAsync(
        ReportingDeliveryJobRecord job,
        CancellationToken ct = default)
    {
        ValidateJob(job, expectedVersion: 0, requireExactVersion: true);
        if (job.Receipts.Count != 0)
        {
            throw new ArgumentException("New reporting delivery jobs cannot contain pre-existing receipts.", nameof(job));
        }

        var releaseJson = SerializeRetained(job.ReleaseAuthorization, "release authorization", job.JobId);
        var payloadJson = SerializeRetained(job.Payload, "delivery payload", job.JobId);
        EnsureNoBearerToken(releaseJson, payloadJson, job);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {_jobTable} (
                job_id,
                tenant_id,
                package_id,
                distribution_id,
                transport_id,
                release_authorization,
                requested_by,
                idempotency_key,
                payload,
                state,
                attempt_count,
                max_attempts,
                created_at_utc,
                updated_at_utc,
                next_attempt_at_utc,
                lease_owner,
                lease_expires_at_utc,
                last_error_code,
                last_error,
                provider_message_id,
                access_grant_id,
                version)
            values (
                @job_id,
                @tenant_id,
                @package_id,
                @distribution_id,
                @transport_id,
                @release_authorization,
                @requested_by,
                @idempotency_key,
                @payload,
                @state,
                @attempt_count,
                @max_attempts,
                @created_at_utc,
                @updated_at_utc,
                @next_attempt_at_utc,
                @lease_owner,
                @lease_expires_at_utc,
                @last_error_code,
                @last_error,
                @provider_message_id,
                @access_grant_id,
                @version)
            on conflict do nothing
            returning 1;
            """;
        AddJobParameters(command, job, releaseJson, payloadJson);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    public async Task<IReadOnlyList<ReportingDeliveryJobRecord>> ClaimDueAsync(
        DateTimeOffset nowUtc,
        string leaseOwner,
        TimeSpan leaseDuration,
        int take,
        CancellationToken ct = default)
    {
        ReportingDistributionStoreGuard.RequireUtc(nowUtc, nameof(nowUtc));
        var normalizedLeaseOwner = ReportingDistributionStoreGuard.NormalizeRequired(
            leaseOwner,
            nameof(leaseOwner),
            256);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Reporting delivery lease duration must be positive.");
        }

        if (take is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(take), "Reporting delivery claim size must be between 1 and 1000.");
        }

        DateTimeOffset leaseExpiresAtUtc;
        try
        {
            leaseExpiresAtUtc = nowUtc.Add(leaseDuration);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, ex.Message);
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            with due as (
                select job_id
                from {_jobTable}
                where ((state in (@queued_state, @retry_state)
                        and next_attempt_at_utc <= @now_utc)
                       or (state = @dispatching_state
                           and lease_expires_at_utc <= @now_utc))
                order by coalesce(next_attempt_at_utc, lease_expires_at_utc, created_at_utc),
                         created_at_utc,
                         job_id
                for update skip locked
                limit @take
            )
            update {_jobTable} as job
            set state = @dispatching_state,
                updated_at_utc = @now_utc,
                next_attempt_at_utc = null,
                lease_owner = @lease_owner,
                lease_expires_at_utc = @lease_expires_at_utc,
                version = job.version + 1
            from due
            where job.job_id = due.job_id
            returning {JobSelectList("job")};
            """;
        command.Parameters.AddWithValue("queued_state", NpgsqlDbType.Integer, (int)ReportingDeliveryState.Queued);
        command.Parameters.AddWithValue("retry_state", NpgsqlDbType.Integer, (int)ReportingDeliveryState.RetryScheduled);
        command.Parameters.AddWithValue("dispatching_state", NpgsqlDbType.Integer, (int)ReportingDeliveryState.Dispatching);
        command.Parameters.AddWithValue("now_utc", NpgsqlDbType.TimestampTz, nowUtc.UtcDateTime);
        command.Parameters.AddWithValue("lease_owner", NpgsqlDbType.Text, normalizedLeaseOwner);
        command.Parameters.AddWithValue("lease_expires_at_utc", NpgsqlDbType.TimestampTz, leaseExpiresAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, take);

        var baseRows = new List<ReportingDeliveryJobRecord>(take);
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                baseRows.Add(ReadBaseJob(reader));
            }
        }

        var claimed = new List<ReportingDeliveryJobRecord>(baseRows.Count);
        foreach (var row in baseRows)
        {
            var receipts = await ReadReceiptsAsync(connection, transaction, row, ct).ConfigureAwait(false);
            var complete = row with { Receipts = receipts };
            ValidateRetainedJob(complete);
            claimed.Add(complete);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return claimed;
    }

    public async Task<bool> TryUpdateAsync(
        string jobId,
        long expectedVersion,
        ReportingDeliveryJobRecord updatedJob,
        CancellationToken ct = default)
    {
        if (expectedVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        var normalizedJobId = ReportingDistributionStoreGuard.NormalizeRequired(
            jobId,
            nameof(jobId),
            256);
        ValidateJob(updatedJob, checked(expectedVersion + 1), requireExactVersion: true);
        if (!string.Equals(normalizedJobId, updatedJob.JobId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Updated reporting delivery job id does not match the requested job.", nameof(updatedJob));
        }

        var releaseJson = SerializeRetained(updatedJob.ReleaseAuthorization, "release authorization", updatedJob.JobId);
        var payloadJson = SerializeRetained(updatedJob.Payload, "delivery payload", updatedJob.JobId);
        EnsureNoBearerToken(releaseJson, payloadJson, updatedJob);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);
        var current = await ReadForUpdateAsync(connection, transaction, normalizedJobId, ct).ConfigureAwait(false);
        if (current is null || current.Version != expectedVersion)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        ValidateTransition(current, updatedJob);
        var appendedReceipts = ValidateReceiptAppend(current, updatedJob);

        foreach (var receipt in appendedReceipts)
        {
            await InsertReceiptAsync(connection, transaction, updatedJob, receipt, ct).ConfigureAwait(false);
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText =
            $"""
            update {_jobTable}
            set state = @state,
                attempt_count = @attempt_count,
                updated_at_utc = @updated_at_utc,
                next_attempt_at_utc = @next_attempt_at_utc,
                lease_owner = @lease_owner,
                lease_expires_at_utc = @lease_expires_at_utc,
                last_error_code = @last_error_code,
                last_error = @last_error,
                provider_message_id = @provider_message_id,
                access_grant_id = @access_grant_id,
                version = @next_version
            where job_id = @job_id
              and version = @expected_version;
            """;
        AddMutableJobParameters(update, updatedJob, expectedVersion);
        if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
        {
            throw new ReportingDistributionStateCorruptionException(
                "delivery job",
                normalizedJobId,
                "its locked version disappeared before the atomic outbox update");
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async Task<ReportingDeliveryJobRecord?> ReadJobAsync(
        string keyColumn,
        string keyValue,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select {JobSelectList()}
            from {_jobTable}
            where {keyColumn} = @key_value;
            """;
        command.Parameters.AddWithValue("key_value", NpgsqlDbType.Text, keyValue);

        ReportingDeliveryJobRecord? baseJob;
        await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false))
        {
            baseJob = await reader.ReadAsync(ct).ConfigureAwait(false)
                ? ReadBaseJob(reader)
                : null;
        }

        if (baseJob is null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return null;
        }

        var receipts = await ReadReceiptsAsync(connection, transaction, baseJob, ct).ConfigureAwait(false);
        var complete = baseJob with { Receipts = receipts };
        ValidateRetainedJob(complete);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return complete;
    }

    private async Task<ReportingDeliveryJobRecord?> ReadForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string jobId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select {JobSelectList()}
            from {_jobTable}
            where job_id = @job_id
            for update;
            """;
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Text, jobId);

        ReportingDeliveryJobRecord? baseJob;
        await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false))
        {
            baseJob = await reader.ReadAsync(ct).ConfigureAwait(false)
                ? ReadBaseJob(reader)
                : null;
        }

        if (baseJob is null)
        {
            return null;
        }

        var receipts = await ReadReceiptsAsync(connection, transaction, baseJob, ct).ConfigureAwait(false);
        var complete = baseJob with { Receipts = receipts };
        ValidateRetainedJob(complete);
        return complete;
    }

    private async Task<IReadOnlyList<ReportingDeliveryReceipt>> ReadReceiptsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingDeliveryJobRecord job,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select receipt_id,
                   kind,
                   occurred_at_utc,
                   transport_id,
                   provider_reference,
                   evidence_reference,
                   detail,
                   tenant_id
            from {_receiptTable}
            where job_id = @job_id
            order by receipt_sequence;
            """;
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Text, job.JobId);
        var receipts = new List<ReportingDeliveryReceipt>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var receiptId = reader.GetString(0);
            try
            {
                var retainedTenant = reader.GetString(7);
                if (!string.Equals(retainedTenant, job.TenantId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("receipt tenant does not match its delivery job");
                }

                var receipt = new ReportingDeliveryReceipt(
                    receiptId,
                    ReadEnum<ReportingDeliveryReceiptKind>(reader.GetInt32(1), "receipt kind", receiptId),
                    ReportingDistributionStoreGuard.ReadUtcTimestamp(reader, 2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6));
                ValidateReceipt(receipt, job.TransportId);
                receipts.Add(receipt);
            }
            catch (ReportingDistributionStateCorruptionException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException or ArgumentException)
            {
                throw new ReportingDistributionStateCorruptionException(
                    "delivery receipt",
                    receiptId,
                    ex.Message,
                    ex);
            }
        }

        if (receipts.Select(static receipt => receipt.ReceiptId).Distinct(StringComparer.Ordinal).Count() != receipts.Count)
        {
            throw new ReportingDistributionStateCorruptionException(
                "delivery job",
                job.JobId,
                "duplicate receipt identifiers were retained");
        }

        return receipts;
    }

    private async Task InsertReceiptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingDeliveryJobRecord job,
        ReportingDeliveryReceipt receipt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {_receiptTable} (
                job_id,
                tenant_id,
                receipt_id,
                kind,
                occurred_at_utc,
                transport_id,
                provider_reference,
                evidence_reference,
                detail)
            values (
                @job_id,
                @tenant_id,
                @receipt_id,
                @kind,
                @occurred_at_utc,
                @transport_id,
                @provider_reference,
                @evidence_reference,
                @detail);
            """;
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Text, job.JobId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, job.TenantId);
        command.Parameters.AddWithValue("receipt_id", NpgsqlDbType.Text, receipt.ReceiptId);
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Integer, (int)receipt.Kind);
        command.Parameters.AddWithValue("occurred_at_utc", NpgsqlDbType.TimestampTz, receipt.OccurredAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("transport_id", NpgsqlDbType.Text, receipt.TransportId);
        AddNullableText(command, "provider_reference", receipt.ProviderReference);
        AddNullableText(command, "evidence_reference", receipt.EvidenceReference);
        AddNullableText(command, "detail", receipt.Detail);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static ReportingDeliveryJobRecord ReadBaseJob(NpgsqlDataReader reader)
    {
        var jobId = reader.GetString(0);
        try
        {
            var release = DeserializeRetained<ReportingDeliveryReleaseAuthorization>(
                reader.GetString(5),
                "release authorization",
                jobId);
            var payload = DeserializeRetained<ReportingDeliveryPayload>(
                reader.GetString(8),
                "delivery payload",
                jobId);
            var job = new ReportingDeliveryJobRecord(
                jobId,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                release,
                reader.GetString(6),
                reader.GetString(7),
                payload,
                ReadEnum<ReportingDeliveryState>(reader.GetInt32(9), "delivery state", jobId),
                reader.GetInt32(10),
                reader.GetInt32(11),
                ReportingDistributionStoreGuard.ReadUtcTimestamp(reader, 12),
                ReportingDistributionStoreGuard.ReadUtcTimestamp(reader, 13),
                ReportingDistributionStoreGuard.ReadNullableUtcTimestamp(reader, 14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                ReportingDistributionStoreGuard.ReadNullableUtcTimestamp(reader, 16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                Receipts: [],
                reader.GetInt64(21));
            ValidateJob(job, expectedVersion: null, requireExactVersion: false);
            EnsureNoBearerToken(reader.GetString(5), reader.GetString(8), job);
            return job;
        }
        catch (ReportingDistributionStateCorruptionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException or ArgumentException or OverflowException)
        {
            throw new ReportingDistributionStateCorruptionException(
                "delivery job",
                jobId,
                ex.Message,
                ex);
        }
    }

    private static string JobSelectList(string? alias = null)
    {
        var prefix = string.IsNullOrWhiteSpace(alias) ? string.Empty : $"{alias}.";
        return $"""
               {prefix}job_id,
               {prefix}tenant_id,
               {prefix}package_id,
               {prefix}distribution_id,
               {prefix}transport_id,
               {prefix}release_authorization::text,
               {prefix}requested_by,
               {prefix}idempotency_key,
               {prefix}payload::text,
               {prefix}state,
               {prefix}attempt_count,
               {prefix}max_attempts,
               {prefix}created_at_utc,
               {prefix}updated_at_utc,
               {prefix}next_attempt_at_utc,
               {prefix}lease_owner,
               {prefix}lease_expires_at_utc,
               {prefix}last_error_code,
               {prefix}last_error,
               {prefix}provider_message_id,
               {prefix}access_grant_id,
               {prefix}version
               """;
    }

    private static void ValidateRetainedJob(ReportingDeliveryJobRecord job)
    {
        try
        {
            ValidateJob(job, expectedVersion: null, requireExactVersion: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new ReportingDistributionStateCorruptionException(
                "delivery job",
                job.JobId,
                ex.Message,
                ex);
        }
    }

    private static void ValidateJob(
        ReportingDeliveryJobRecord job,
        long? expectedVersion,
        bool requireExactVersion)
    {
        ArgumentNullException.ThrowIfNull(job);
        ReportingDistributionStoreGuard.NormalizeRequired(job.JobId, nameof(job.JobId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(job.TenantId, nameof(job.TenantId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(job.PackageId, nameof(job.PackageId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(job.DistributionId, nameof(job.DistributionId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(job.TransportId, nameof(job.TransportId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(job.RequestedBy, nameof(job.RequestedBy), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.ValidateSha256(job.IdempotencyKey, nameof(job.IdempotencyKey));
        if (!Enum.IsDefined(job.State))
        {
            throw new ArgumentException("Reporting delivery state is invalid.", nameof(job));
        }

        if (job.MaxAttempts is < 1 or > 100 || job.AttemptCount < 0 || job.AttemptCount > job.MaxAttempts)
        {
            throw new ArgumentException("Reporting delivery attempt counters are invalid.", nameof(job));
        }

        ReportingDistributionStoreGuard.RequireUtc(job.CreatedAtUtc, nameof(job.CreatedAtUtc));
        ReportingDistributionStoreGuard.RequireUtc(job.UpdatedAtUtc, nameof(job.UpdatedAtUtc));
        if (job.UpdatedAtUtc < job.CreatedAtUtc)
        {
            throw new ArgumentException("Reporting delivery update time predates creation.", nameof(job));
        }

        ValidateOptionalUtc(job.NextAttemptAtUtc, nameof(job.NextAttemptAtUtc));
        ValidateOptionalUtc(job.LeaseExpiresAtUtc, nameof(job.LeaseExpiresAtUtc));
        if (job.State == ReportingDeliveryState.Dispatching)
        {
            ReportingDistributionStoreGuard.NormalizeRequired(job.LeaseOwner!, nameof(job.LeaseOwner), 256, requireCanonical: true);
            if (job.LeaseExpiresAtUtc is null)
            {
                throw new ArgumentException("Dispatching reporting deliveries require a lease expiration.", nameof(job));
            }
        }
        else if (job.LeaseOwner is not null || job.LeaseExpiresAtUtc is not null)
        {
            throw new ArgumentException("Non-dispatching reporting deliveries cannot retain a dispatch lease.", nameof(job));
        }

        if (job.State is ReportingDeliveryState.Queued or ReportingDeliveryState.RetryScheduled)
        {
            if (job.NextAttemptAtUtc is null)
            {
                throw new ArgumentException("Queued and retry-scheduled deliveries require a next-attempt time.", nameof(job));
            }
        }
        else if (job.NextAttemptAtUtc is not null)
        {
            throw new ArgumentException("Only queued or retry-scheduled deliveries may retain a next-attempt time.", nameof(job));
        }

        ValidateRelease(job.ReleaseAuthorization, job.TenantId, job.PackageId);
        ValidatePayload(job.Payload);
        ValidateOptionalIdentifier(
            job.ProviderMessageId,
            nameof(job.ProviderMessageId),
            ReportingDistributionValueLimits.ProviderMessageIdLength);
        ValidateOptionalIdentifier(job.AccessGrantId, nameof(job.AccessGrantId), 256);
        ReportingDistributionStoreGuard.ValidateStringSet(
            job.Receipts.Select(static receipt => receipt.ReceiptId).ToArray(),
            nameof(job.Receipts),
            256);
        foreach (var receipt in job.Receipts)
        {
            ValidateReceipt(receipt, job.TransportId);
        }

        if (job.Version < 0 || (requireExactVersion && job.Version != expectedVersion))
        {
            throw new ArgumentException("Reporting delivery version is invalid.", nameof(job));
        }
    }

    private static void ValidateRelease(
        ReportingDeliveryReleaseAuthorization release,
        string tenantId,
        string packageId)
    {
        ArgumentNullException.ThrowIfNull(release);
        ReportingDistributionStoreGuard.NormalizeRequired(release.ReceiptId, nameof(release.ReceiptId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(release.TenantId, nameof(release.TenantId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(release.PackageId, nameof(release.PackageId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(release.RunId, nameof(release.RunId), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(release.ReleaseVersion, nameof(release.ReleaseVersion), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.ValidateSha256(release.ArtifactManifestHashSha256, nameof(release.ArtifactManifestHashSha256));
        ReportingDistributionStoreGuard.NormalizeRequired(release.ReleasedBy, nameof(release.ReleasedBy), 256, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(release.AuthorizationProof, nameof(release.AuthorizationProof), 4096, requireCanonical: true);
        ReportingDistributionStoreGuard.RequireUtc(release.ReleasedAtUtc, nameof(release.ReleasedAtUtc));
        if (release.State != ReportingReleaseState.Released
            || !string.Equals(release.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(release.PackageId, packageId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Delivery release authorization is not a matching Released tenant package.", nameof(release));
        }

        if (release.Artifacts is null || release.Artifacts.Count == 0)
        {
            throw new ArgumentException("Delivery release authorization requires immutable artifacts.", nameof(release));
        }

        var artifactIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in release.Artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
            var artifactId = ReportingDistributionStoreGuard.NormalizeRequired(artifact.ArtifactId, nameof(artifact.ArtifactId), 512, requireCanonical: true);
            if (!artifactIds.Add(artifactId))
            {
                throw new ArgumentException("Delivery release authorization contains duplicate artifacts.", nameof(release));
            }

            var retainedUri = ReportingDistributionStoreGuard.NormalizeRequired(artifact.RetainedUri, nameof(artifact.RetainedUri), 4096, requireCanonical: true);
            RejectTokenBearingText(retainedUri, nameof(artifact.RetainedUri));
            ReportingDistributionStoreGuard.ValidateSha256(artifact.ContentHashSha256, nameof(artifact.ContentHashSha256));
            if (artifact.ByteSize <= 0)
            {
                throw new ArgumentException("Released artifact sizes must be positive.", nameof(release));
            }
        }

        ReportingDistributionStoreGuard.ValidateStringSet(release.EvidenceReferences, nameof(release.EvidenceReferences), 4096);
        if (release.EvidenceReferences.Count == 0)
        {
            throw new ArgumentException("Delivery release authorization requires evidence references.", nameof(release));
        }
    }

    private static void ValidatePayload(ReportingDeliveryPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ReportingDistributionStoreGuard.NormalizeRequired(payload.Recipient, nameof(payload.Recipient), 512, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(payload.RecipientRole, nameof(payload.RecipientRole), 512, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(payload.Destination, nameof(payload.Destination), 2048, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(payload.Subject, nameof(payload.Subject), 2048, requireCanonical: true);
        ReportingDistributionStoreGuard.NormalizeRequired(payload.Body, nameof(payload.Body), 65536);
        if (!Enum.IsDefined(payload.RecipientKind))
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "Reporting delivery recipient kind is invalid.");
        }
        RejectTokenBearingText(payload.PortalUri, nameof(payload.PortalUri));
        if (payload.ExternalAccess is { } access)
        {
            ReportingDistributionStoreGuard.NormalizeRequired(access.Audience, nameof(access.Audience), 512, requireCanonical: true);
            if (!Enum.IsDefined(access.AudienceKind)
                || access.AudienceKind != payload.RecipientKind)
            {
                throw new ArgumentException(
                    "Reporting delivery recipient and external-access principal kinds must match.",
                    nameof(payload));
            }
            RejectTokenBearingText(access.AccessBaseUri, nameof(access.AccessBaseUri));
            if (access.Lifetime <= TimeSpan.Zero || access.MaxUses <= 0)
            {
                throw new ArgumentException("Reporting delivery external access policy is invalid.", nameof(payload));
            }

            ReportingDistributionStoreGuard.ValidateStringSet(access.ArtifactIds ?? [], nameof(access.ArtifactIds), 512);
        }
    }

    private static void ValidateReceipt(ReportingDeliveryReceipt receipt, string transportId)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ReportingDistributionStoreGuard.NormalizeRequired(receipt.ReceiptId, nameof(receipt.ReceiptId), 256, requireCanonical: true);
        if (!Enum.IsDefined(receipt.Kind))
        {
            throw new ArgumentException("Reporting delivery receipt kind is invalid.", nameof(receipt));
        }

        ReportingDistributionStoreGuard.RequireUtc(receipt.OccurredAtUtc, nameof(receipt.OccurredAtUtc));
        var normalizedTransport = ReportingDistributionStoreGuard.NormalizeRequired(
            receipt.TransportId,
            nameof(receipt.TransportId),
            256,
            requireCanonical: true);
        if (!string.Equals(normalizedTransport, transportId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Reporting delivery receipt transport does not match its job.", nameof(receipt));
        }

        ValidateOptionalIdentifier(
            receipt.ProviderReference,
            nameof(receipt.ProviderReference),
            ReportingDistributionValueLimits.ProviderMessageIdLength);
    }

    private static void ValidateTransition(
        ReportingDeliveryJobRecord current,
        ReportingDeliveryJobRecord updated)
    {
        if (!string.Equals(current.JobId, updated.JobId, StringComparison.Ordinal)
            || !string.Equals(current.TenantId, updated.TenantId, StringComparison.Ordinal)
            || !string.Equals(current.PackageId, updated.PackageId, StringComparison.Ordinal)
            || !string.Equals(current.DistributionId, updated.DistributionId, StringComparison.Ordinal)
            || !string.Equals(current.TransportId, updated.TransportId, StringComparison.Ordinal)
            || !string.Equals(current.RequestedBy, updated.RequestedBy, StringComparison.Ordinal)
            || !string.Equals(current.IdempotencyKey, updated.IdempotencyKey, StringComparison.Ordinal)
            || current.MaxAttempts != updated.MaxAttempts
            || current.CreatedAtUtc != updated.CreatedAtUtc
            || !JsonEquals(current.ReleaseAuthorization, updated.ReleaseAuthorization)
            || !JsonEquals(current.Payload, updated.Payload))
        {
            throw new InvalidOperationException("Reporting delivery authority, payload, and idempotency scope are immutable.");
        }

        if (updated.UpdatedAtUtc < current.UpdatedAtUtc)
        {
            throw new InvalidOperationException("Reporting delivery update time cannot move backwards.");
        }

        if (updated.AttemptCount < current.AttemptCount || updated.AttemptCount > current.AttemptCount + 1)
        {
            throw new InvalidOperationException("Reporting delivery attempt count can only advance by one atomically.");
        }

        if (updated.State == ReportingDeliveryState.Dispatching
            && current.State != ReportingDeliveryState.Dispatching)
        {
            throw new InvalidOperationException("Reporting delivery leases can only be acquired by ClaimDueAsync.");
        }

        if (current.State == ReportingDeliveryState.Dispatching
            && updated.State == ReportingDeliveryState.Dispatching
            && (current.AccessGrantId is not null
                || updated.AccessGrantId is null
                || current.AttemptCount != updated.AttemptCount
                || current.ProviderMessageId != updated.ProviderMessageId
                || current.LastErrorCode != updated.LastErrorCode
                || current.LastError != updated.LastError
                || current.NextAttemptAtUtc != updated.NextAttemptAtUtc
                || current.LeaseOwner != updated.LeaseOwner
                || current.LeaseExpiresAtUtc != updated.LeaseExpiresAtUtc
                || !JsonEquals(current.Receipts, updated.Receipts)))
        {
            throw new InvalidOperationException(
                "An active delivery lease may only bind its first deterministic access grant before provider dispatch.");
        }

        if (current.AccessGrantId is not null
            && !string.Equals(current.AccessGrantId, updated.AccessGrantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A retained reporting access grant reference cannot be replaced.");
        }

        if (current.ProviderMessageId is not null
            && !string.Equals(current.ProviderMessageId, updated.ProviderMessageId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A retained reporting provider message id cannot be replaced.");
        }

        if (current.State is ReportingDeliveryState.RetryScheduled or ReportingDeliveryState.Blocked
            && updated.State != current.State
            && updated.State != ReportingDeliveryState.Dispatching
            && !IsProviderOutcomeReceiptResolution(current, updated))
        {
            throw new InvalidOperationException(
                "Only an exact authenticated provider receipt may resolve a retained unknown provider outcome.");
        }

        if (!IsAllowedStateTransition(current.State, updated.State))
        {
            throw new InvalidOperationException(
                $"Reporting delivery state cannot move from {current.State} to {updated.State}.");
        }
    }

    private static bool IsAllowedStateTransition(
        ReportingDeliveryState current,
        ReportingDeliveryState updated) =>
        current switch
        {
            ReportingDeliveryState.Queued => updated == ReportingDeliveryState.Dispatching,
            ReportingDeliveryState.Dispatching => updated is
                ReportingDeliveryState.Dispatching or
                ReportingDeliveryState.RetryScheduled or
                ReportingDeliveryState.Sent or
                ReportingDeliveryState.Delivered or
                ReportingDeliveryState.Blocked or
                ReportingDeliveryState.Failed,
            ReportingDeliveryState.RetryScheduled => updated is
                ReportingDeliveryState.Dispatching or
                ReportingDeliveryState.Sent or
                ReportingDeliveryState.Delivered or
                ReportingDeliveryState.Failed,
            ReportingDeliveryState.Sent => updated is
                ReportingDeliveryState.Sent or
                ReportingDeliveryState.Delivered or
                ReportingDeliveryState.Failed,
            ReportingDeliveryState.Delivered => updated == ReportingDeliveryState.Delivered,
            ReportingDeliveryState.Blocked => updated is
                ReportingDeliveryState.Blocked or
                ReportingDeliveryState.Sent or
                ReportingDeliveryState.Delivered or
                ReportingDeliveryState.Failed,
            ReportingDeliveryState.Failed => updated == ReportingDeliveryState.Failed,
            _ => false
        };

    private static bool IsProviderOutcomeReceiptResolution(
        ReportingDeliveryJobRecord current,
        ReportingDeliveryJobRecord updated)
    {
        if (current.AccessGrantId is null
            || current.ProviderMessageId is not null
            || updated.ProviderMessageId is null
            || current.AttemptCount != updated.AttemptCount
            || updated.NextAttemptAtUtc is not null
            || updated.LeaseOwner is not null
            || updated.LeaseExpiresAtUtc is not null
            || !string.Equals(current.AccessGrantId, updated.AccessGrantId, StringComparison.Ordinal)
            || current.LastErrorCode is not ("RELAY_OUTCOME_UNKNOWN" or "TRANSPORT_CANCELLED")
            || updated.Receipts.Count != current.Receipts.Count + 1)
        {
            return false;
        }

        var receipt = updated.Receipts[^1];
        return string.Equals(
                   receipt.ProviderReference,
                   updated.ProviderMessageId,
                   StringComparison.Ordinal)
               && receipt.Kind switch
               {
                   ReportingDeliveryReceiptKind.Delivered or
                   ReportingDeliveryReceiptKind.Accessed or
                   ReportingDeliveryReceiptKind.Downloaded =>
                       updated.State == ReportingDeliveryState.Delivered,
                   ReportingDeliveryReceiptKind.Bounced or
                   ReportingDeliveryReceiptKind.Rejected or
                   ReportingDeliveryReceiptKind.Failed =>
                       updated.State == ReportingDeliveryState.Failed,
                   _ => updated.State == ReportingDeliveryState.Sent
               };
    }

    private static IReadOnlyList<ReportingDeliveryReceipt> ValidateReceiptAppend(
        ReportingDeliveryJobRecord current,
        ReportingDeliveryJobRecord updated)
    {
        if (updated.Receipts.Count < current.Receipts.Count)
        {
            throw new InvalidOperationException("Reporting delivery receipts are append-only.");
        }

        for (var index = 0; index < current.Receipts.Count; index++)
        {
            if (current.Receipts[index] != updated.Receipts[index])
            {
                throw new InvalidOperationException("Retained reporting delivery receipts cannot be reordered or modified.");
            }
        }

        return updated.Receipts.Skip(current.Receipts.Count).ToArray();
    }

    private static void AddJobParameters(
        NpgsqlCommand command,
        ReportingDeliveryJobRecord job,
        string releaseJson,
        string payloadJson)
    {
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Text, job.JobId);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, job.TenantId);
        command.Parameters.AddWithValue("package_id", NpgsqlDbType.Text, job.PackageId);
        command.Parameters.AddWithValue("distribution_id", NpgsqlDbType.Text, job.DistributionId);
        command.Parameters.AddWithValue("transport_id", NpgsqlDbType.Text, job.TransportId);
        command.Parameters.AddWithValue("release_authorization", NpgsqlDbType.Jsonb, releaseJson);
        command.Parameters.AddWithValue("requested_by", NpgsqlDbType.Text, job.RequestedBy);
        command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, job.IdempotencyKey);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, payloadJson);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Integer, (int)job.State);
        command.Parameters.AddWithValue("attempt_count", NpgsqlDbType.Integer, job.AttemptCount);
        command.Parameters.AddWithValue("max_attempts", NpgsqlDbType.Integer, job.MaxAttempts);
        command.Parameters.AddWithValue("created_at_utc", NpgsqlDbType.TimestampTz, job.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("updated_at_utc", NpgsqlDbType.TimestampTz, job.UpdatedAtUtc.UtcDateTime);
        AddNullableTimestamp(command, "next_attempt_at_utc", job.NextAttemptAtUtc);
        AddNullableText(command, "lease_owner", job.LeaseOwner);
        AddNullableTimestamp(command, "lease_expires_at_utc", job.LeaseExpiresAtUtc);
        AddNullableText(command, "last_error_code", job.LastErrorCode);
        AddNullableText(command, "last_error", job.LastError);
        AddNullableText(command, "provider_message_id", job.ProviderMessageId);
        AddNullableText(command, "access_grant_id", job.AccessGrantId);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, job.Version);
    }

    private static void AddMutableJobParameters(
        NpgsqlCommand command,
        ReportingDeliveryJobRecord job,
        long expectedVersion)
    {
        command.Parameters.AddWithValue("job_id", NpgsqlDbType.Text, job.JobId);
        command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, expectedVersion);
        command.Parameters.AddWithValue("next_version", NpgsqlDbType.Bigint, job.Version);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Integer, (int)job.State);
        command.Parameters.AddWithValue("attempt_count", NpgsqlDbType.Integer, job.AttemptCount);
        command.Parameters.AddWithValue("updated_at_utc", NpgsqlDbType.TimestampTz, job.UpdatedAtUtc.UtcDateTime);
        AddNullableTimestamp(command, "next_attempt_at_utc", job.NextAttemptAtUtc);
        AddNullableText(command, "lease_owner", job.LeaseOwner);
        AddNullableTimestamp(command, "lease_expires_at_utc", job.LeaseExpiresAtUtc);
        AddNullableText(command, "last_error_code", job.LastErrorCode);
        AddNullableText(command, "last_error", job.LastError);
        AddNullableText(command, "provider_message_id", job.ProviderMessageId);
        AddNullableText(command, "access_grant_id", job.AccessGrantId);
    }

    private static T DeserializeRetained<T>(string json, string entityType, string entityId)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, ReportingDistributionStoreGuard.JsonOptions)
                   ?? throw new JsonException("JSON value was null");
        }
        catch (JsonException ex)
        {
            throw new ReportingDistributionStateCorruptionException(entityType, entityId, ex.Message, ex);
        }
    }

    private static string SerializeRetained<T>(T value, string entityType, string entityId)
    {
        try
        {
            return JsonSerializer.Serialize(value, ReportingDistributionStoreGuard.JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new ArgumentException($"Reporting {entityType} for '{entityId}' cannot be serialized.", entityType, ex);
        }
    }

    private static bool JsonEquals<T>(T left, T right) =>
        string.Equals(
            JsonSerializer.Serialize(left, ReportingDistributionStoreGuard.JsonOptions),
            JsonSerializer.Serialize(right, ReportingDistributionStoreGuard.JsonOptions),
            StringComparison.Ordinal);

    private static TEnum ReadEnum<TEnum>(int value, string entityType, string entityId)
        where TEnum : struct, Enum
    {
        var parsed = (TEnum)Enum.ToObject(typeof(TEnum), value);
        if (!Enum.IsDefined(parsed))
        {
            throw new ReportingDistributionStateCorruptionException(entityType, entityId, $"enum value {value} is not defined");
        }

        return parsed;
    }

    private static void EnsureNoBearerToken(
        string releaseJson,
        string payloadJson,
        ReportingDeliveryJobRecord job)
    {
        if (ContainsTokenMarker(releaseJson) || ContainsTokenMarker(payloadJson))
        {
            throw new ArgumentException("Reporting delivery persistence rejects token-bearing retained content.", nameof(job));
        }
    }

    private static void RejectTokenBearingText(string value, string parameterName)
    {
        ReportingDistributionStoreGuard.NormalizeRequired(value, parameterName, 4096, requireCanonical: true);
        if (ContainsTokenMarker(value))
        {
            throw new ArgumentException("Reporting delivery retained URIs cannot contain bearer tokens.", parameterName);
        }
    }

    private static void ValidateOptionalIdentifier(
        string? value,
        string parameterName,
        int maximumLength)
    {
        if (value is null)
        {
            return;
        }

        ReportingDistributionStoreGuard.NormalizeRequired(
            value,
            parameterName,
            maximumLength,
            requireCanonical: true);
        if (ContainsTokenMarker(value)
            || value.Contains("bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Reporting delivery identifiers cannot contain credential-shaped material.",
                parameterName);
        }
    }

    private static bool ContainsTokenMarker(string value) =>
        value.Contains("#token=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("?token=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("&token=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("\"token\"", StringComparison.OrdinalIgnoreCase);

    private static void ValidateOptionalUtc(DateTimeOffset? value, string parameterName)
    {
        if (value is not null)
        {
            ReportingDistributionStoreGuard.RequireUtc(value.Value, parameterName);
        }
    }

    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.TimestampTz);
        parameter.Value = value is null ? DBNull.Value : value.Value.UtcDateTime;
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
