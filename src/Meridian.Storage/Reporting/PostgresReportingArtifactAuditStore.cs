using System.Data;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Reporting;

/// <summary>
/// PostgreSQL-backed append-only reporting artifact audit chain. A locked chain-head row assigns
/// each sequence and predecessor hash atomically so concurrent writers cannot fork the chain.
/// </summary>
public sealed class PostgresReportingArtifactAuditStore : IReportingArtifactAuditStore
{
    private const int MaximumIdentifierLength = 256;

    private readonly ReportingArtifactStoreOptions _options;
    private readonly string _headTable;
    private readonly string _auditTable;

    public PostgresReportingArtifactAuditStore(ReportingArtifactStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ValidateDatabaseIdentifier(_options.Schema, nameof(options.Schema));
        _headTable = $"\"{_options.Schema}\".\"reporting_artifact_audit_chain_head\"";
        _auditTable = $"\"{_options.Schema}\".\"reporting_artifact_audit\"";
    }

    public async ValueTask<ReportingArtifactAuditReceipt> AppendAsync(
        ReportingArtifactAuditEvent auditEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        ValidateAuditEvent(auditEvent);
        var eventPayload = JsonSerializer.Serialize(
            auditEvent,
            ReportingArtifactCatalogJsonContext.Default.ReportingArtifactAuditEvent);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        var head = await LockAndReadHeadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var existing = await ReadExistingEventAsync(
                connection,
                transaction,
                auditEvent.EventId,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            VerifyExistingEvent(existing, eventPayload);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return existing.Receipt;
        }

        await VerifyChainHeadAsync(connection, transaction, head, cancellationToken).ConfigureAwait(false);
        var hash = ComputeEntryHash(head.NextSequence, head.LastHash, eventPayload);
        var receipt = new ReportingArtifactAuditReceipt(
            auditEvent.EventId,
            head.NextSequence,
            head.LastHash,
            hash);

        await InsertAuditAsync(
                connection,
                transaction,
                auditEvent,
                eventPayload,
                receipt,
                cancellationToken)
            .ConfigureAwait(false);
        await AdvanceHeadAsync(connection, transaction, receipt, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return receipt;
    }

    private async Task<AuditChainHead> LockAndReadHeadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select next_sequence,
                   last_hash
            from {_headTable}
            where chain_id = 1
            for update;
            """;
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ReportingArtifactAuditIntegrityException(
                "Reporting artifact audit chain head is missing; append failed closed.");
        }

        return new AuditChainHead(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<RetainedAuditEvent?> ReadExistingEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string eventId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select sequence,
                   previous_hash,
                   entry_hash,
                   event_payload
            from {_auditTable}
            where event_id = @event_id;
            """;
        command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new RetainedAuditEvent(
            new ReportingArtifactAuditReceipt(
                eventId,
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2)),
            reader.GetString(3));
    }

    private async Task VerifyChainHeadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuditChainHead head,
        CancellationToken cancellationToken)
    {
        if (head.NextSequence <= 0)
        {
            throw new ReportingArtifactAuditIntegrityException(
                "Reporting artifact audit chain head contains an invalid next sequence.");
        }

        if (head.NextSequence == 1)
        {
            if (head.LastHash is not null)
            {
                throw new ReportingArtifactAuditIntegrityException(
                    "Empty reporting artifact audit chain unexpectedly carries a predecessor hash.");
            }

            await using var emptyCommand = connection.CreateCommand();
            emptyCommand.Transaction = transaction;
            emptyCommand.CommandText = $"select exists(select 1 from {_auditTable});";
            if ((bool)(await emptyCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false))
            {
                throw new ReportingArtifactAuditIntegrityException(
                    "Reporting artifact audit rows exist while the chain head is empty.");
            }

            return;
        }

        if (head.LastHash is null)
        {
            throw new ReportingArtifactAuditIntegrityException(
                "Non-empty reporting artifact audit chain is missing its predecessor hash.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select previous_hash,
                   entry_hash,
                   event_payload
            from {_auditTable}
            where sequence = @sequence;
            """;
        command.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, head.NextSequence - 1);
        await using var reader = await command
            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ReportingArtifactAuditIntegrityException(
                "Reporting artifact audit chain head points to a missing final event.");
        }

        var previousHash = reader.IsDBNull(0) ? null : reader.GetString(0);
        var retainedHash = reader.GetString(1);
        var payload = reader.GetString(2);
        var computedHash = ComputeEntryHash(head.NextSequence - 1, previousHash, payload);
        if (!string.Equals(retainedHash, head.LastHash, StringComparison.Ordinal)
            || !string.Equals(retainedHash, computedHash, StringComparison.Ordinal))
        {
            throw new ReportingArtifactAuditIntegrityException(
                "Reporting artifact audit chain head or final event failed hash verification.");
        }
    }

    private async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingArtifactAuditEvent auditEvent,
        string eventPayload,
        ReportingArtifactAuditReceipt receipt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {_auditTable} (
                sequence,
                event_id,
                occurred_at_utc,
                action,
                actor_tenant_id,
                target_tenant_id,
                package_id,
                artifact_id,
                previous_hash,
                entry_hash,
                event_payload)
            values (
                @sequence,
                @event_id,
                @occurred_at_utc,
                @action,
                @actor_tenant_id,
                @target_tenant_id,
                @package_id,
                @artifact_id,
                @previous_hash,
                @entry_hash,
                @event_payload);
            """;
        command.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, receipt.Sequence);
        command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, auditEvent.EventId);
        command.Parameters.AddWithValue("occurred_at_utc", NpgsqlDbType.TimestampTz, auditEvent.OccurredAtUtc);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, auditEvent.Action.ToString());
        command.Parameters.AddWithValue("actor_tenant_id", NpgsqlDbType.Text, auditEvent.ActorTenantId);
        command.Parameters.AddWithValue("target_tenant_id", NpgsqlDbType.Text, auditEvent.TargetTenantId);
        command.Parameters.AddWithValue("package_id", NpgsqlDbType.Text, auditEvent.PackageId);
        command.Parameters.AddWithValue("artifact_id", NpgsqlDbType.Text, auditEvent.ArtifactId);
        command.Parameters.AddWithValue(
            "previous_hash",
            NpgsqlDbType.Text,
            (object?)receipt.PreviousHash ?? DBNull.Value);
        command.Parameters.AddWithValue("entry_hash", NpgsqlDbType.Text, receipt.Hash);
        command.Parameters.AddWithValue("event_payload", NpgsqlDbType.Text, eventPayload);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task AdvanceHeadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReportingArtifactAuditReceipt receipt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            update {_headTable}
            set next_sequence = @next_sequence,
                last_hash = @last_hash
            where chain_id = 1;
            """;
        command.Parameters.AddWithValue("next_sequence", NpgsqlDbType.Bigint, receipt.Sequence + 1);
        command.Parameters.AddWithValue("last_hash", NpgsqlDbType.Text, receipt.Hash);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new ReportingArtifactAuditIntegrityException(
                "Reporting artifact audit chain head could not be advanced atomically.");
        }
    }

    private static void VerifyExistingEvent(RetainedAuditEvent existing, string expectedPayload)
    {
        var computedHash = ComputeEntryHash(
            existing.Receipt.Sequence,
            existing.Receipt.PreviousHash,
            existing.EventPayload);
        if (!string.Equals(existing.Receipt.Hash, computedHash, StringComparison.Ordinal))
        {
            throw new ReportingArtifactAuditIntegrityException(
                $"Existing audit event '{existing.Receipt.EventId}' failed hash verification.");
        }

        if (!string.Equals(existing.EventPayload, expectedPayload, StringComparison.Ordinal))
        {
            throw new ReportingArtifactAuditIntegrityException(
                $"Audit event id '{existing.Receipt.EventId}' was retried with non-identical metadata.");
        }
    }

    internal static string ComputeEntryHash(long sequence, string? previousHash, string eventPayload)
    {
        var material = string.Concat(
            sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "\n",
            previousHash ?? string.Empty,
            "\n",
            eventPayload);
        return Sha256Digest.ComputeUtf8(material);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static void ValidateAuditEvent(ReportingArtifactAuditEvent auditEvent)
    {
        ValidateRequiredIdentifier(auditEvent.EventId, nameof(auditEvent.EventId));
        ValidateRequiredIdentifier(auditEvent.ActorId, nameof(auditEvent.ActorId));
        ValidateRequiredIdentifier(auditEvent.ActorTenantId, nameof(auditEvent.ActorTenantId));
        ValidateRequiredIdentifier(auditEvent.TargetTenantId, nameof(auditEvent.TargetTenantId));
        ValidateRequiredIdentifier(auditEvent.PackageId, nameof(auditEvent.PackageId));
        ValidateRequiredIdentifier(auditEvent.ArtifactId, nameof(auditEvent.ArtifactId));
        ValidateRequiredIdentifier(auditEvent.CorrelationId, nameof(auditEvent.CorrelationId));
        if (!Enum.IsDefined(auditEvent.Action))
        {
            throw new ArgumentOutOfRangeException(nameof(auditEvent), "Reporting artifact audit action is invalid.");
        }

        if (auditEvent.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Reporting artifact audit timestamps must be expressed in UTC.", nameof(auditEvent));
        }

        if (auditEvent.ContentHashSha256 is not null)
        {
            ValidateHash(auditEvent.ContentHashSha256, nameof(auditEvent.ContentHashSha256));
        }
    }

    private static void ValidateRequiredIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumIdentifierLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Reporting audit identifiers must be trimmed and at most {MaximumIdentifierLength} characters.",
                parameterName);
        }
    }

    private static void ValidateHash(string value, string parameterName)
    {
        if (value.Length != 64
            || !value.All(Uri.IsHexDigit)
            || !string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Reporting audit hashes must contain exactly 64 lowercase hexadecimal characters.",
                parameterName);
        }
    }

    private static void ValidateDatabaseIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !(char.IsAsciiLetter(value[0]) || value[0] == '_')
            || !value.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException(
                $"PostgreSQL identifier '{value}' is not supported. Use letters, digits, and underscores, and start with a letter or underscore.",
                parameterName);
        }
    }

    private sealed record AuditChainHead(long NextSequence, string? LastHash);

    private sealed record RetainedAuditEvent(
        ReportingArtifactAuditReceipt Receipt,
        string EventPayload);
}
