using System.Data;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Reporting;

/// <summary>
/// PostgreSQL-backed authoritative schedule snapshot. Composite tenant/company/schedule identity
/// prevents cross-scope replacement, and every row is integrity-checked before it is returned.
/// </summary>
public sealed class PostgresReportingScheduleStore : IReportingScheduleStore
{
    private const int MaximumIdentityLength = 256;

    private readonly ReportingArtifactStoreOptions _options;
    private readonly string _scheduleTable;
    private readonly object _legacySnapshotGate = new();
    private IReadOnlyDictionary<ReportingScheduleStorageKey, LegacySnapshotEntry>
        _legacySnapshotBaseline =
            new Dictionary<ReportingScheduleStorageKey, LegacySnapshotEntry>();
    private IReadOnlySet<ReportingScheduleScopeKey> _legacySnapshotScopes =
        new HashSet<ReportingScheduleScopeKey>();
    private bool _hasLegacySnapshotBaseline;
    private bool _legacySnapshotCoversAllScopes;

    public PostgresReportingScheduleStore(ReportingArtifactStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ReportingDistributionStoreGuard.ValidateIdentifier(_options.Schema, nameof(options.Schema));
        _scheduleTable = $"\"{_options.Schema}\".\"reporting_schedule_snapshots\"";
    }

    public bool IsDurableAuthority => true;

    public IReadOnlyList<ReportingScheduleRecordDto> Load()
    {
        lock (_legacySnapshotGate)
        {
            using var connection = OpenConnection();
            var retained = ReadAllSchedules(connection, transaction: null);
            _legacySnapshotBaseline = retained.ToDictionary(
                static state => ReportingScheduleStorageKey.From(state.Identity),
                static state => new LegacySnapshotEntry(
                    state.Schedule,
                    state.PayloadHashSha256));
            _legacySnapshotScopes = retained
                .Select(static state => ReportingScheduleStorageKey.From(state.Identity))
                .Select(static key => ReportingScheduleScopeKey.From(key))
                .ToHashSet();
            _hasLegacySnapshotBaseline = true;
            _legacySnapshotCoversAllScopes = true;

            return retained.Select(static state => state.Schedule).ToArray();
        }
    }

    public void Save(IReadOnlyList<ReportingScheduleRecordDto> schedules)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        var entries = PrepareEntries(schedules);
        lock (_legacySnapshotGate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            LockForLegacySnapshotReplacement(connection, transaction);
            var current = ReadAllSchedules(connection, transaction)
                .ToDictionary(
                    static state => ReportingScheduleStorageKey.From(state.Identity));
            var desired = entries.ToDictionary(
                static entry => ReportingScheduleStorageKey.From(entry.Identity));
            var desiredScopes = desired.Keys
                .Select(static key => ReportingScheduleScopeKey.From(key))
                .ToHashSet();
            var baselineCoversAllScopes = _hasLegacySnapshotBaseline
                ? _legacySnapshotCoversAllScopes
                : desiredScopes.Count == 0;
            var baselineScopes = _hasLegacySnapshotBaseline
                ? _legacySnapshotScopes
                : desiredScopes.Count == 0
                    ? current.Keys
                        .Select(static key => ReportingScheduleScopeKey.From(key))
                        .ToHashSet()
                    : desiredScopes;
            var replacementScopes = baselineScopes
                .Concat(desiredScopes)
                .ToHashSet();
            var baselineForSave = _hasLegacySnapshotBaseline
                ? _legacySnapshotBaseline
                : current
                    .Where(pair => baselineCoversAllScopes
                        || desiredScopes.Contains(
                            ReportingScheduleScopeKey.From(pair.Key)))
                    .ToDictionary(
                        static pair => pair.Key,
                        static pair => new LegacySnapshotEntry(
                            pair.Value.Schedule,
                            pair.Value.PayloadHashSha256));

            foreach (var retained in current)
            {
                if (baselineForSave.ContainsKey(retained.Key)
                    || desired.ContainsKey(retained.Key)
                    || (!baselineCoversAllScopes
                        && !replacementScopes.Contains(
                            ReportingScheduleScopeKey.From(retained.Key))))
                {
                    continue;
                }

                throw ReportingScheduleConcurrencyException.ForConflict(
                    retained.Value.Schedule,
                    expectedUpdatedAtUtc: null);
            }

            foreach (var baseline in baselineForSave)
            {
                if (!replacementScopes.Contains(
                        ReportingScheduleScopeKey.From(baseline.Key))
                    || desired.ContainsKey(baseline.Key)
                    || !current.TryGetValue(baseline.Key, out var retained))
                {
                    continue;
                }
                if (!Sha256Digest.FixedEquals(
                        retained.PayloadHashSha256,
                        baseline.Value.PayloadHashSha256))
                {
                    throw ReportingScheduleConcurrencyException.ForConflict(
                        retained.Schedule,
                        baseline.Value.Schedule.UpdatedAtUtc);
                }

                DeleteEntry(
                    connection,
                    transaction,
                    retained.Identity,
                    retained.PayloadHashSha256,
                    retained.Schedule,
                    baseline.Value.Schedule.UpdatedAtUtc);
            }

            foreach (var entry in entries)
            {
                var key = ReportingScheduleStorageKey.From(entry.Identity);
                current.TryGetValue(key, out var retained);
                if (baselineForSave.TryGetValue(key, out var baseline))
                {
                    if (retained is null)
                    {
                        throw ReportingScheduleConcurrencyException.ForMissing(
                            entry.Schedule,
                            baseline.Schedule.UpdatedAtUtc);
                    }
                    if (Sha256Digest.FixedEquals(
                            retained.PayloadHashSha256,
                            entry.PayloadHashSha256))
                    {
                        continue;
                    }
                    if (!Sha256Digest.FixedEquals(
                            retained.PayloadHashSha256,
                            baseline.PayloadHashSha256))
                    {
                        throw ReportingScheduleConcurrencyException.ForConflict(
                            retained.Schedule,
                            baseline.Schedule.UpdatedAtUtc);
                    }
                    if (entry.Schedule.UpdatedAtUtc <= retained.Schedule.UpdatedAtUtc)
                    {
                        throw new ArgumentException(
                            "A changed reporting schedule must advance UpdatedAtUtc beyond the retained revision.",
                            nameof(schedules));
                    }

                    Update(
                        connection,
                        transaction,
                        entry,
                        retained.PayloadHashSha256);
                    continue;
                }

                if (retained is null)
                {
                    Insert(connection, transaction, entry);
                    continue;
                }
                if (!Sha256Digest.FixedEquals(
                        retained.PayloadHashSha256,
                        entry.PayloadHashSha256))
                {
                    throw ReportingScheduleConcurrencyException.ForConflict(
                        retained.Schedule,
                        expectedUpdatedAtUtc: null);
                }
            }

            transaction.Commit();
            _legacySnapshotBaseline = desired.ToDictionary(
                static pair => pair.Key,
                static pair => new LegacySnapshotEntry(
                    pair.Value.Schedule,
                    pair.Value.PayloadHashSha256));
            _legacySnapshotScopes = replacementScopes;
            _hasLegacySnapshotBaseline = true;
            _legacySnapshotCoversAllScopes = baselineCoversAllScopes;
        }
    }

    public void Upsert(ReportingScheduleRecordDto schedule) =>
        Upsert(schedule, expectedUpdatedAtUtc: null);

    public void Upsert(
        ReportingScheduleRecordDto schedule,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var entry = PrepareEntries([schedule]).Single();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        PersistEntry(connection, transaction, entry, expectedUpdatedAtUtc);
        transaction.Commit();
    }

    public bool Delete(
        string tenantId,
        string companyId,
        string scheduleId,
        DateTimeOffset expectedUpdatedAtUtc)
    {
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
        var normalizedScheduleId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            scheduleId,
            nameof(scheduleId),
            MaximumIdentityLength);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var identity = new ReportingScheduleIdentity(
            normalizedTenantId,
            normalizedCompanyId,
            normalizedScheduleId,
            normalizedScheduleId.ToLowerInvariant());
        var current = ReadCurrentSchedule(connection, transaction, identity);
        if (current is null)
        {
            transaction.Commit();
            return false;
        }
        if (current.Schedule.UpdatedAtUtc != expectedUpdatedAtUtc)
        {
            throw ReportingScheduleConcurrencyException.ForConflict(
                current.Schedule,
                expectedUpdatedAtUtc);
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            delete from {_scheduleTable}
            where tenant_id = @tenant_id
              and company_id = @company_id
              and schedule_id_key = @schedule_id_key
              and payload_hash_sha256 = @payload_hash_sha256;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, normalizedTenantId);
        command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, normalizedCompanyId);
        command.Parameters.AddWithValue(
            "schedule_id_key",
            NpgsqlDbType.Text,
            normalizedScheduleId.ToLowerInvariant());
        command.Parameters.AddWithValue(
            "payload_hash_sha256",
            NpgsqlDbType.Text,
            current.PayloadHashSha256);
        var deleted = command.ExecuteNonQuery() > 0;
        if (!deleted)
        {
            throw ReportingScheduleConcurrencyException.ForConflict(
                current.Schedule,
                expectedUpdatedAtUtc);
        }

        transaction.Commit();
        return true;
    }

    public ReportingScheduleExecutionLease? TryClaimExecution(
        ReportingScheduleRecordDto schedule,
        string leaseOwner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var entry = PrepareEntries([schedule]).Single();
        var normalizedOwner = ReportingOperationalStoreJson.NormalizeRequired(
            leaseOwner,
            nameof(leaseOwner),
            MaximumIdentityLength,
            requireCanonical: true);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            update {_scheduleTable}
               set lease_owner = @lease_owner,
                   lease_expires_at_utc = clock_timestamp() + @lease_duration,
                   lease_version = lease_version + 1
             where tenant_id = @tenant_id
               and company_id = @company_id
               and schedule_id_key = @schedule_id_key
               and payload_hash_sha256 = @payload_hash_sha256
               and (lease_owner is null or lease_expires_at_utc <= clock_timestamp())
            returning lease_expires_at_utc, lease_version;
            """;
        AddIdentityParameters(command, entry.Identity);
        command.Parameters.AddWithValue(
            "payload_hash_sha256",
            NpgsqlDbType.Text,
            entry.PayloadHashSha256);
        command.Parameters.AddWithValue(
            "lease_owner",
            NpgsqlDbType.Text,
            normalizedOwner);
        command.Parameters.AddWithValue(
            "lease_duration",
            NpgsqlDbType.Interval,
            leaseDuration);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ReportingScheduleExecutionLease(
                normalizedOwner,
                ReportingDistributionStoreGuard.ReadUtcTimestamp(reader, 0),
                reader.GetInt64(1))
            : null;
    }

    public ReportingScheduleExecutionLease? RenewExecutionLease(
        ReportingScheduleRecordDto schedule,
        ReportingScheduleExecutionLease lease,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(lease);
        var entry = PrepareEntries([schedule]).Single();
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            update {_scheduleTable}
               set lease_expires_at_utc = clock_timestamp() + @lease_duration
             where tenant_id = @tenant_id
               and company_id = @company_id
               and schedule_id_key = @schedule_id_key
               and payload_hash_sha256 = @payload_hash_sha256
               and lease_owner = @lease_owner
               and lease_version = @lease_version
               and lease_expires_at_utc > clock_timestamp()
            returning lease_expires_at_utc;
            """;
        AddIdentityParameters(command, entry.Identity);
        command.Parameters.AddWithValue(
            "payload_hash_sha256",
            NpgsqlDbType.Text,
            entry.PayloadHashSha256);
        command.Parameters.AddWithValue(
            "lease_owner",
            NpgsqlDbType.Text,
            lease.LeaseOwner);
        command.Parameters.AddWithValue(
            "lease_version",
            NpgsqlDbType.Bigint,
            lease.LeaseVersion);
        command.Parameters.AddWithValue(
            "lease_duration",
            NpgsqlDbType.Interval,
            leaseDuration);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? lease with
            {
                LeaseExpiresAtUtc =
                    ReportingDistributionStoreGuard.ReadUtcTimestamp(reader, 0)
            }
            : null;
    }

    public void ReleaseExecutionLease(
        string tenantId,
        string companyId,
        string scheduleId,
        ReportingScheduleExecutionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var normalizedScheduleId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            scheduleId,
            nameof(scheduleId),
            MaximumIdentityLength);
        var identity = new ReportingScheduleIdentity(
            ReportingOperationalStoreJson.NormalizeRequired(
                tenantId,
                nameof(tenantId),
                MaximumIdentityLength,
                requireCanonical: true),
            ReportingOperationalStoreJson.NormalizeRequired(
                companyId,
                nameof(companyId),
                MaximumIdentityLength,
                requireCanonical: true),
            normalizedScheduleId,
            normalizedScheduleId.ToLowerInvariant());
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            update {_scheduleTable}
               set lease_owner = null,
                   lease_expires_at_utc = null
             where tenant_id = @tenant_id
               and company_id = @company_id
               and schedule_id_key = @schedule_id_key
               and lease_owner = @lease_owner
               and lease_version = @lease_version;
            """;
        AddIdentityParameters(command, identity);
        command.Parameters.AddWithValue(
            "lease_owner",
            NpgsqlDbType.Text,
            lease.LeaseOwner);
        command.Parameters.AddWithValue(
            "lease_version",
            NpgsqlDbType.Bigint,
            lease.LeaseVersion);
        command.ExecuteNonQuery();
    }

    public void UpsertClaimedExecution(
        ReportingScheduleRecordDto schedule,
        DateTimeOffset expectedUpdatedAtUtc,
        ReportingScheduleExecutionLease lease)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(lease);
        var entry = PrepareEntries([schedule]).Single();
        var normalizedOwner = ReportingOperationalStoreJson.NormalizeRequired(
            lease.LeaseOwner,
            nameof(lease.LeaseOwner),
            MaximumIdentityLength,
            requireCanonical: true);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lease.LeaseVersion);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var current = ReadCurrentSchedule(connection, transaction, entry.Identity);
        if (current is null)
        {
            throw ReportingScheduleConcurrencyException.ForMissing(
                schedule,
                expectedUpdatedAtUtc);
        }
        if (current.Schedule.UpdatedAtUtc != expectedUpdatedAtUtc)
        {
            throw ReportingScheduleConcurrencyException.ForConflict(
                current.Schedule,
                expectedUpdatedAtUtc);
        }
        if (!HasActiveExecutionLease(
                connection,
                transaction,
                entry.Identity,
                normalizedOwner,
                lease.LeaseVersion))
        {
            throw ExecutionLeaseException(entry.Identity);
        }
        if (Sha256Digest.FixedEquals(
                current.PayloadHashSha256,
                entry.PayloadHashSha256))
        {
            transaction.Commit();
            return;
        }
        if (entry.Schedule.UpdatedAtUtc <= current.Schedule.UpdatedAtUtc)
        {
            throw new ArgumentException(
                "A changed reporting schedule must advance UpdatedAtUtc beyond the retained revision.",
                nameof(schedule));
        }

        UpdateClaimedExecution(
            connection,
            transaction,
            entry,
            current.PayloadHashSha256,
            normalizedOwner,
            lease.LeaseVersion);
        transaction.Commit();
    }

    private static IReadOnlyList<StoredScheduleEntry> PrepareEntries(
        IReadOnlyList<ReportingScheduleRecordDto> schedules)
    {
        var identities = new HashSet<ReportingScheduleStorageKey>();
        var entries = new List<StoredScheduleEntry>(schedules.Count);
        foreach (var schedule in schedules)
        {
            ArgumentNullException.ThrowIfNull(schedule);
            var identity = ValidateSchedule(schedule);
            if (!identities.Add(ReportingScheduleStorageKey.From(identity)))
            {
                throw new ArgumentException(
                    $"Reporting schedules contain duplicate scoped identity '{identity.TenantId}/{identity.CompanyId}/{identity.ScheduleId}'.",
                    nameof(schedules));
            }

            var payload = ReportingOperationalStoreJson.SerializeCanonical(
                schedule,
                nameof(schedules));
            entries.Add(new StoredScheduleEntry(
                identity,
                schedule,
                payload,
                ReportingOperationalStoreJson.ComputeSha256(payload)));
        }

        return entries
            .OrderBy(static entry => entry.Identity.TenantId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Identity.CompanyId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Identity.ScheduleIdKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static ReportingScheduleRecordDto ReadSchedule(NpgsqlDataReader reader)
    {
        var retainedTenantId = reader.GetString(0);
        var retainedCompanyId = reader.GetString(1);
        var retainedScheduleId = reader.GetString(2);
        var entityId = $"{retainedTenantId}/{retainedCompanyId}/{retainedScheduleId}";

        try
        {
            var schedule = ReportingOperationalStoreJson.DeserializeRetained<ReportingScheduleRecordDto>(
                reader.GetString(3),
                "schedule snapshot",
                entityId);
            var retainedHash = reader.GetString(4);
            var identity = ValidateSchedule(schedule);
            if (!string.Equals(identity.TenantId, retainedTenantId, StringComparison.Ordinal)
                || !string.Equals(identity.CompanyId, retainedCompanyId, StringComparison.Ordinal)
                || !string.Equals(identity.ScheduleId, retainedScheduleId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "the indexed tenant/company/schedule identity does not match the retained payload");
            }

            var computedHash = ReportingOperationalStoreJson.ComputeSha256(
                ReportingOperationalStoreJson.SerializeCanonical(
                    schedule,
                    nameof(schedule)));
            if (!Sha256Digest.FixedEquals(retainedHash, computedHash))
            {
                throw new InvalidDataException(
                    "the canonical schedule JSON integrity digest does not match");
            }

            return schedule;
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
                "schedule snapshot",
                entityId,
                ex.Message,
                ex);
        }
    }

    private static ReportingScheduleIdentity ValidateSchedule(
        ReportingScheduleRecordDto schedule)
    {
        var tenantId = ReportingOperationalStoreJson.NormalizeRequired(
            schedule.TenantId
            ?? throw new ArgumentException(
                "PostgreSQL reporting schedule persistence requires tenant scope.",
                nameof(schedule)),
            nameof(schedule.TenantId),
            MaximumIdentityLength,
            requireCanonical: true);
        var companyId = ReportingOperationalStoreJson.NormalizeRequired(
            schedule.CompanyId
            ?? throw new ArgumentException(
                "PostgreSQL reporting schedule persistence requires company scope.",
                nameof(schedule)),
            nameof(schedule.CompanyId),
            MaximumIdentityLength,
            requireCanonical: true);
        var scheduleId = ReportingOperationalStoreJson.NormalizeMachineIdentity(
            schedule.ScheduleId,
            nameof(schedule.ScheduleId),
            MaximumIdentityLength);
        _ = ReportingOperationalStoreJson.NormalizeRequired(
            schedule.TemplateId,
            nameof(schedule.TemplateId),
            MaximumIdentityLength,
            requireCanonical: true);
        _ = ReportingOperationalStoreJson.NormalizeRequired(
            schedule.CronExpression,
            nameof(schedule.CronExpression),
            1024,
            requireCanonical: true);
        _ = ReportingOperationalStoreJson.NormalizeRequired(
            schedule.RequestedBy,
            nameof(schedule.RequestedBy),
            512,
            requireCanonical: true);

        if (schedule.NextAsOfDate == default
            || schedule.DueAtUtc == default
            || schedule.DueAtUtc.Offset != TimeSpan.Zero
            || schedule.CreatedAtUtc == default
            || schedule.CreatedAtUtc.Offset != TimeSpan.Zero
            || schedule.UpdatedAtUtc == default
            || schedule.UpdatedAtUtc.Offset != TimeSpan.Zero
            || schedule.UpdatedAtUtc < schedule.CreatedAtUtc
            || schedule.LastRunAtUtc is { Offset: var lastRunOffset }
            && lastRunOffset != TimeSpan.Zero
            || schedule.MaxRetries < 0
            || schedule.RunCount < 0
            || !Enum.IsDefined(schedule.State))
        {
            throw new ArgumentException(
                "Reporting schedule dates, counters, or lifecycle state are invalid.",
                nameof(schedule));
        }

        return new ReportingScheduleIdentity(
            tenantId,
            companyId,
            scheduleId,
            scheduleId.ToLowerInvariant());
    }

    private void PersistEntry(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StoredScheduleEntry entry,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        var current = ReadCurrentSchedule(
            connection,
            transaction,
            entry.Identity);
        if (current is null)
        {
            if (expectedUpdatedAtUtc is not null)
            {
                throw ReportingScheduleConcurrencyException.ForMissing(
                    entry.Schedule,
                    expectedUpdatedAtUtc.Value);
            }

            Insert(connection, transaction, entry);
            return;
        }

        if (expectedUpdatedAtUtc is null)
        {
            if (Sha256Digest.FixedEquals(
                    current.PayloadHashSha256,
                    entry.PayloadHashSha256))
            {
                return;
            }

            throw ReportingScheduleConcurrencyException.ForConflict(
                current.Schedule,
                expectedUpdatedAtUtc: null);
        }
        if (current.Schedule.UpdatedAtUtc != expectedUpdatedAtUtc.Value)
        {
            throw ReportingScheduleConcurrencyException.ForConflict(
                current.Schedule,
                expectedUpdatedAtUtc.Value);
        }
        if (Sha256Digest.FixedEquals(
                current.PayloadHashSha256,
                entry.PayloadHashSha256))
        {
            return;
        }
        if (entry.Schedule.UpdatedAtUtc <= current.Schedule.UpdatedAtUtc)
        {
            throw new ArgumentException(
                "A changed reporting schedule must advance UpdatedAtUtc beyond the retained revision.",
                nameof(entry));
        }

        Update(connection, transaction, entry, current.PayloadHashSha256);
    }

    private StoredScheduleState? ReadCurrentSchedule(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingScheduleIdentity identity)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select tenant_id,
                   company_id,
                   schedule_id,
                   schedule_payload::text,
                   payload_hash_sha256
            from {_scheduleTable}
            where tenant_id = @tenant_id
              and company_id = @company_id
              and schedule_id_key = @schedule_id_key
            for update;
            """;
        AddIdentityParameters(command, identity);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new StoredScheduleState(
                identity,
                ReadSchedule(reader),
                reader.GetString(4))
            : null;
    }

    private IReadOnlyList<StoredScheduleState> ReadAllSchedules(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select tenant_id,
                   company_id,
                   schedule_id,
                   schedule_payload::text,
                   payload_hash_sha256
            from {_scheduleTable}
            order by tenant_id, company_id, schedule_id_key;
            """;
        var retained = new List<StoredScheduleState>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var schedule = ReadSchedule(reader);
            retained.Add(new StoredScheduleState(
                ValidateSchedule(schedule),
                schedule,
                reader.GetString(4)));
        }

        return retained;
    }

    private void LockForLegacySnapshotReplacement(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"lock table {_scheduleTable} in exclusive mode;";
        command.ExecuteNonQuery();
    }

    private void DeleteEntry(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingScheduleIdentity identity,
        string retainedPayloadHashSha256,
        ReportingScheduleRecordDto schedule,
        DateTimeOffset expectedUpdatedAtUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            delete from {_scheduleTable}
            where tenant_id = @tenant_id
              and company_id = @company_id
              and schedule_id_key = @schedule_id_key
              and payload_hash_sha256 = @payload_hash_sha256;
            """;
        AddIdentityParameters(command, identity);
        command.Parameters.AddWithValue(
            "payload_hash_sha256",
            NpgsqlDbType.Text,
            retainedPayloadHashSha256);
        if (command.ExecuteNonQuery() != 1)
        {
            throw ReportingScheduleConcurrencyException.ForMissing(
                schedule,
                expectedUpdatedAtUtc);
        }
    }

    private void Insert(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StoredScheduleEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {_scheduleTable} as retained (
                tenant_id,
                company_id,
                schedule_id,
                schedule_id_key,
                schedule_payload,
                payload_hash_sha256,
                due_at_utc,
                stored_at_utc)
            values (
                @tenant_id,
                @company_id,
                @schedule_id,
                @schedule_id_key,
                @schedule_payload,
                @payload_hash_sha256,
                @due_at_utc,
                @stored_at_utc)
            on conflict (tenant_id, company_id, schedule_id_key) do nothing;
            """;
        AddIdentityParameters(command, entry.Identity);
        command.Parameters.AddWithValue(
            "schedule_id",
            NpgsqlDbType.Text,
            entry.Identity.ScheduleId);
        command.Parameters.AddWithValue(
            "schedule_payload",
            NpgsqlDbType.Jsonb,
            entry.Payload);
        command.Parameters.AddWithValue(
            "payload_hash_sha256",
            NpgsqlDbType.Text,
            entry.PayloadHashSha256);
        command.Parameters.AddWithValue(
            "due_at_utc",
            NpgsqlDbType.TimestampTz,
            entry.Schedule.DueAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "stored_at_utc",
            NpgsqlDbType.TimestampTz,
            DateTime.UtcNow);
        if (command.ExecuteNonQuery() != 1)
        {
            var concurrent = ReadCurrentSchedule(
                connection,
                transaction,
                entry.Identity);
            if (concurrent is not null
                && Sha256Digest.FixedEquals(
                    concurrent.PayloadHashSha256,
                    entry.PayloadHashSha256))
            {
                return;
            }

            throw ReportingScheduleConcurrencyException.ForConflict(
                concurrent?.Schedule ?? entry.Schedule,
                expectedUpdatedAtUtc: null);
        }
    }

    private void Update(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StoredScheduleEntry entry,
        string retainedPayloadHashSha256)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            update {_scheduleTable}
            set schedule_id = @schedule_id,
                schedule_payload = @schedule_payload,
                payload_hash_sha256 = @payload_hash_sha256,
                due_at_utc = @due_at_utc,
                stored_at_utc = @stored_at_utc
            where tenant_id = @tenant_id
              and company_id = @company_id
              and schedule_id_key = @schedule_id_key
              and payload_hash_sha256 = @retained_payload_hash_sha256;
            """;
        AddIdentityParameters(command, entry.Identity);
        command.Parameters.AddWithValue(
            "schedule_id",
            NpgsqlDbType.Text,
            entry.Identity.ScheduleId);
        command.Parameters.AddWithValue(
            "schedule_payload",
            NpgsqlDbType.Jsonb,
            entry.Payload);
        command.Parameters.AddWithValue(
            "payload_hash_sha256",
            NpgsqlDbType.Text,
            entry.PayloadHashSha256);
        command.Parameters.AddWithValue(
            "due_at_utc",
            NpgsqlDbType.TimestampTz,
            entry.Schedule.DueAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "stored_at_utc",
            NpgsqlDbType.TimestampTz,
            DateTime.UtcNow);
        command.Parameters.AddWithValue(
            "retained_payload_hash_sha256",
            NpgsqlDbType.Text,
            retainedPayloadHashSha256);
        if (command.ExecuteNonQuery() != 1)
        {
            throw ReportingScheduleConcurrencyException.ForConflict(
                entry.Schedule,
                expectedUpdatedAtUtc: entry.Schedule.UpdatedAtUtc);
        }
    }

    private bool HasActiveExecutionLease(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingScheduleIdentity identity,
        string leaseOwner,
        long leaseVersion)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select exists (
                select 1
                from {_scheduleTable}
                where tenant_id = @tenant_id
                  and company_id = @company_id
                  and schedule_id_key = @schedule_id_key
                  and lease_owner = @lease_owner
                  and lease_version = @lease_version
                  and lease_expires_at_utc > clock_timestamp());
            """;
        AddIdentityParameters(command, identity);
        command.Parameters.AddWithValue(
            "lease_owner",
            NpgsqlDbType.Text,
            leaseOwner);
        command.Parameters.AddWithValue(
            "lease_version",
            NpgsqlDbType.Bigint,
            leaseVersion);
        return (bool)(command.ExecuteScalar() ?? false);
    }

    private void UpdateClaimedExecution(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StoredScheduleEntry entry,
        string retainedPayloadHashSha256,
        string leaseOwner,
        long leaseVersion)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            update {_scheduleTable}
               set schedule_id = @schedule_id,
                   schedule_payload = @schedule_payload,
                   payload_hash_sha256 = @payload_hash_sha256,
                   due_at_utc = @due_at_utc,
                   stored_at_utc = @stored_at_utc
             where tenant_id = @tenant_id
               and company_id = @company_id
               and schedule_id_key = @schedule_id_key
               and payload_hash_sha256 = @retained_payload_hash_sha256
               and lease_owner = @lease_owner
               and lease_version = @lease_version
               and lease_expires_at_utc > clock_timestamp();
            """;
        AddIdentityParameters(command, entry.Identity);
        command.Parameters.AddWithValue(
            "schedule_id",
            NpgsqlDbType.Text,
            entry.Identity.ScheduleId);
        command.Parameters.AddWithValue(
            "schedule_payload",
            NpgsqlDbType.Jsonb,
            entry.Payload);
        command.Parameters.AddWithValue(
            "payload_hash_sha256",
            NpgsqlDbType.Text,
            entry.PayloadHashSha256);
        command.Parameters.AddWithValue(
            "due_at_utc",
            NpgsqlDbType.TimestampTz,
            entry.Schedule.DueAtUtc.UtcDateTime);
        command.Parameters.AddWithValue(
            "stored_at_utc",
            NpgsqlDbType.TimestampTz,
            DateTime.UtcNow);
        command.Parameters.AddWithValue(
            "retained_payload_hash_sha256",
            NpgsqlDbType.Text,
            retainedPayloadHashSha256);
        command.Parameters.AddWithValue(
            "lease_owner",
            NpgsqlDbType.Text,
            leaseOwner);
        command.Parameters.AddWithValue(
            "lease_version",
            NpgsqlDbType.Bigint,
            leaseVersion);
        if (command.ExecuteNonQuery() != 1)
        {
            throw ExecutionLeaseException(entry.Identity);
        }
    }

    private static ReportingScheduleExecutionLeaseException ExecutionLeaseException(
        ReportingScheduleIdentity identity) =>
        new(
            identity.TenantId,
            identity.CompanyId,
            identity.ScheduleId,
            "The reporting schedule execution lease is missing, expired, or was superseded by another owner.");

    private static void AddIdentityParameters(
        NpgsqlCommand command,
        ReportingScheduleIdentity identity)
    {
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, identity.TenantId);
        command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, identity.CompanyId);
        command.Parameters.AddWithValue(
            "schedule_id_key",
            NpgsqlDbType.Text,
            identity.ScheduleIdKey);
    }

    private NpgsqlConnection OpenConnection()
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        connection.Open();
        return connection;
    }

    private readonly record struct ReportingScheduleIdentity(
        string TenantId,
        string CompanyId,
        string ScheduleId,
        string ScheduleIdKey);

    private readonly record struct ReportingScheduleStorageKey(
        string TenantId,
        string CompanyId,
        string ScheduleIdKey)
    {
        internal static ReportingScheduleStorageKey From(
            ReportingScheduleIdentity identity) =>
            new(
                identity.TenantId,
                identity.CompanyId,
                identity.ScheduleIdKey);
    }

    private readonly record struct ReportingScheduleScopeKey(
        string TenantId,
        string CompanyId)
    {
        internal static ReportingScheduleScopeKey From(
            ReportingScheduleStorageKey key) =>
            new(key.TenantId, key.CompanyId);
    }

    private sealed record StoredScheduleEntry(
        ReportingScheduleIdentity Identity,
        ReportingScheduleRecordDto Schedule,
        string Payload,
        string PayloadHashSha256);

    private sealed record StoredScheduleState(
        ReportingScheduleIdentity Identity,
        ReportingScheduleRecordDto Schedule,
        string PayloadHashSha256);

    private sealed record LegacySnapshotEntry(
        ReportingScheduleRecordDto Schedule,
        string PayloadHashSha256);
}
