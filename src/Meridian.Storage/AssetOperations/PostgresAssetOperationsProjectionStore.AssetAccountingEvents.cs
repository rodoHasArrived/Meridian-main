using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Npgsql;

namespace Meridian.Storage.AssetOperations;

public sealed partial class PostgresAssetOperationsProjectionStore
{
    public async Task<AssetAccountingEventProjectionRecord?> GetAsync(
        Guid eventId,
        long eventVersion,
        long spineVersion,
        CancellationToken ct = default)
    {
        ValidateAssetAccountingReadIdentity(eventId, eventVersion, spineVersion);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadAssetAccountingEventAsync(
                connection,
                transaction: null,
                eventId,
                eventVersion,
                spineVersion,
                forUpdate: false,
                ct)
            .ConfigureAwait(false);
    }

    public async Task<AssetAccountingEventProjectionRecord?> GetLatestAsync(
        Guid eventId,
        long eventVersion,
        CancellationToken ct = default)
    {
        ValidateAssetAccountingStreamIdentity(eventId, eventVersion);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadLatestAssetAccountingEventAsync(
                connection,
                transaction: null,
                eventId,
                eventVersion,
                forUpdate: false,
                ct)
            .ConfigureAwait(false);
    }

    public async Task<AssetAccountingEventAppendResult> AppendAsync(
        AssetAccountingEventSpineDto projection,
        long expectedSpineVersion,
        long expectedBookPositionVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ct.ThrowIfCancellationRequested();
        projection = AssetAccountingEventProjectionRules.Clone(projection);
        await AssetAccountingEventProjectionRules.ValidateDurablePostedImpactAsync(
                projection,
                _assetAccountingJournalStore,
                ct)
            .ConfigureAwait(false);
        var fingerprint = AssetAccountingEventProjectionRules.Fingerprint(projection);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);

        await AcquireAssetAccountingEventLockAsync(
                connection,
                transaction,
                projection.EventId,
                projection.EventVersion,
                ct)
            .ConfigureAwait(false);

        var exactIdentity = await ReadAssetAccountingEventAsync(
                connection,
                transaction,
                projection.EventId,
                projection.EventVersion,
                projection.SpineVersion,
                forUpdate: true,
                ct)
            .ConfigureAwait(false);
        if (exactIdentity is not null)
        {
            if (!string.Equals(
                    exactIdentity.CanonicalFingerprint,
                    fingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Asset accounting event '{projection.EventId:D}' source version " +
                    $"'{projection.EventVersion}' spine version '{projection.SpineVersion}' " +
                    "already exists with a different canonical fingerprint.");
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new AssetAccountingEventAppendResult(
                exactIdentity.Projection,
                exactIdentity.CanonicalFingerprint,
                WasReplay: true);
        }

        AssetAccountingEventProjectionRules.ValidateAppend(
            projection,
            expectedSpineVersion,
            expectedBookPositionVersion);

        await ValidateAssetAccountingPositionCasAsync(
                connection,
                transaction,
                projection,
                expectedBookPositionVersion,
                ct)
            .ConfigureAwait(false);

        var latest = await ReadLatestAssetAccountingEventAsync(
                connection,
                transaction,
                projection.EventId,
                projection.EventVersion,
                forUpdate: true,
                ct)
            .ConfigureAwait(false);
        var currentSpineVersion = latest?.Projection.SpineVersion ?? 0;
        if (currentSpineVersion != expectedSpineVersion)
        {
            throw new InvalidOperationException(
                $"Asset accounting event '{projection.EventId:D}' expected spine version " +
                $"'{expectedSpineVersion}', but current version is '{currentSpineVersion}'.");
        }

        if (latest is not null)
        {
            AssetAccountingEventProjectionRules.ValidateStreamContinuity(
                latest.Projection,
                projection);
        }

        await InsertAssetAccountingEventAsync(
                connection,
                transaction,
                projection,
                expectedSpineVersion,
                fingerprint,
                ct)
            .ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return new AssetAccountingEventAppendResult(projection, fingerprint, WasReplay: false);
    }

    private async Task ValidateAssetAccountingPositionCasAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetAccountingEventSpineDto projection,
        long expectedBookPositionVersion,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select version, security_id, ledger_book_id
            from {Qualified("book_position_projections")}
            where position_id = @position_id
            for share;
            """;
        command.Parameters.AddWithValue("position_id", projection.Scope.BookPositionId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Book position '{projection.Scope.BookPositionId:D}' was not found.");
        }

        var version = reader.GetInt64(0);
        var securityId = reader.GetGuid(1);
        var ledgerBookId = reader.GetGuid(2);
        if (version != expectedBookPositionVersion ||
            securityId != projection.Scope.SecurityId ||
            ledgerBookId != projection.Scope.LedgerBookId)
        {
            throw new InvalidOperationException(
                "Asset accounting event append rejected a stale or mismatched book-position assertion.");
        }
    }

    private async Task InsertAssetAccountingEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetAccountingEventSpineDto projection,
        long priorSpineVersion,
        string fingerprint,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("asset_accounting_event_projections")} (
                event_id, event_version, spine_version, prior_spine_version, event_kind,
                security_id, expected_security_version, book_position_id, expected_book_position_version,
                ledger_book_id, period_id, effective_date, event_amount, currency,
                source_content_hash, projection_run_id, projection_model_key,
                projection_model_version, posted_journal_entry_id,
                tax_lot_mutation_batch_id, canonical_fingerprint, payload)
            values (
                @event_id, @event_version, @spine_version, @prior_spine_version, @event_kind,
                @security_id, @expected_security_version, @book_position_id, @expected_book_position_version,
                @ledger_book_id, @period_id, @effective_date, @event_amount, @currency,
                @source_content_hash, @projection_run_id, @projection_model_key,
                @projection_model_version, @posted_journal_entry_id,
                @tax_lot_mutation_batch_id, @canonical_fingerprint, @payload::jsonb);
            """;
        command.Parameters.AddWithValue("event_id", projection.EventId);
        command.Parameters.AddWithValue("event_version", projection.EventVersion);
        command.Parameters.AddWithValue("spine_version", projection.SpineVersion);
        command.Parameters.AddWithValue("prior_spine_version", priorSpineVersion);
        command.Parameters.AddWithValue("event_kind", projection.EventKind.ToString());
        command.Parameters.AddWithValue("security_id", projection.Scope.SecurityId);
        command.Parameters.AddWithValue(
            "expected_security_version",
            projection.Scope.ExpectedSecurityVersion);
        command.Parameters.AddWithValue("book_position_id", projection.Scope.BookPositionId);
        command.Parameters.AddWithValue(
            "expected_book_position_version",
            projection.Scope.ExpectedBookPositionVersion);
        command.Parameters.AddWithValue("ledger_book_id", projection.Scope.LedgerBookId);
        command.Parameters.AddWithValue("period_id", projection.Scope.PeriodId);
        command.Parameters.AddWithValue("effective_date", projection.EffectiveDate);
        command.Parameters.AddWithValue("event_amount", projection.EventAmount);
        command.Parameters.AddWithValue("currency", projection.Currency);
        command.Parameters.AddWithValue(
            "source_content_hash",
            projection.EconomicEvent.SourceContentHash!);
        command.Parameters.AddWithValue("projection_run_id", projection.ProjectionLineage.ProjectionRunId);
        command.Parameters.AddWithValue("projection_model_key", projection.ProjectionLineage.ModelKey);
        command.Parameters.AddWithValue("projection_model_version", projection.ProjectionLineage.ModelVersion);
        command.Parameters.AddWithValue(
            "posted_journal_entry_id",
            (object?)projection.PostedJournalImpact?.JournalEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "tax_lot_mutation_batch_id",
            (object?)projection.TaxLotMutationBatchId ?? DBNull.Value);
        command.Parameters.AddWithValue("canonical_fingerprint", fingerprint);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(projection, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<AssetAccountingEventProjectionRecord?> ReadLatestAssetAccountingEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid eventId,
        long eventVersion,
        bool forUpdate,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select canonical_fingerprint, payload::text
            from {Qualified("asset_accounting_event_projections")}
            where event_id = @event_id and event_version = @event_version
            order by spine_version desc
            limit 1
            {(forUpdate ? "for update" : string.Empty)};
            """;
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("event_version", eventVersion);
        return await ReadAssetAccountingEventRecordAsync(command, ct).ConfigureAwait(false);
    }

    private async Task<AssetAccountingEventProjectionRecord?> ReadAssetAccountingEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid eventId,
        long eventVersion,
        long spineVersion,
        bool forUpdate,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select canonical_fingerprint, payload::text
            from {Qualified("asset_accounting_event_projections")}
            where event_id = @event_id
              and event_version = @event_version
              and spine_version = @spine_version
            {(forUpdate ? "for update" : string.Empty)};
            """;
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("event_version", eventVersion);
        command.Parameters.AddWithValue("spine_version", spineVersion);
        return await ReadAssetAccountingEventRecordAsync(command, ct).ConfigureAwait(false);
    }

    private static async Task<AssetAccountingEventProjectionRecord?> ReadAssetAccountingEventRecordAsync(
        NpgsqlCommand command,
        CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var retainedFingerprint = reader.GetString(0);
        var projection = JsonSerializer.Deserialize<AssetAccountingEventSpineDto>(
                reader.GetString(1),
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The retained asset accounting event payload could not be deserialized.");
        var calculatedFingerprint = AssetAccountingEventProjectionRules.Fingerprint(projection);
        if (!string.Equals(retainedFingerprint, calculatedFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Asset accounting event '{projection.EventId:D}' failed its canonical fingerprint check.");
        }

        return new AssetAccountingEventProjectionRecord(projection, retainedFingerprint);
    }

    private static async Task AcquireAssetAccountingEventLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        long eventVersion,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(@lock_key);";
        command.Parameters.AddWithValue(
            "lock_key",
            ComputeAssetAccountingEventLockKey(eventId, eventVersion));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static long ComputeAssetAccountingEventLockKey(Guid eventId, long eventVersion)
    {
        var payload = Encoding.UTF8.GetBytes(
            $"asset-accounting-event|{eventId:D}|{eventVersion}");
        return BitConverter.ToInt64(SHA256.HashData(payload), 0);
    }

    private static void ValidateAssetAccountingReadIdentity(
        Guid eventId,
        long eventVersion,
        long spineVersion)
    {
        ValidateAssetAccountingStreamIdentity(eventId, eventVersion);

        if (spineVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spineVersion),
                "An asset accounting spine version must be positive.");
        }
    }

    private static void ValidateAssetAccountingStreamIdentity(Guid eventId, long eventVersion)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("An asset accounting event identity is required.", nameof(eventId));
        }

        if (eventVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventVersion),
                "An asset accounting source-event version must be positive.");
        }
    }
}
