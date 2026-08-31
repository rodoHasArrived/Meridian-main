using Meridian.Contracts.Banking;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Banking;

/// <summary>
/// PostgreSQL implementation of <see cref="IBankingStore"/> using raw Npgsql.
/// </summary>
public sealed class PostgresBankingStore : IBankingStore
{
    private const string PendingPaymentColumns =
        "pending_payment_id, entity_id, amount, effective_date, external_ref, notes, " +
        "status, reviewed_by, review_notes, initiated_at, reviewed_at, currency, " +
        "currency_remediated_by, currency_remediation_reason, currency_remediated_at";

    private const string BankTransactionColumns =
        "bank_transaction_id, entity_id, transaction_type, effective_date, transaction_date, " +
        "settlement_date, amount, currency, external_ref, recorded_at, is_voided, recorded_by, " +
        "pending_payment_id, evidence_id, canonical_input_hash";

    private readonly BankingStoreOptions _options;

    public PostgresBankingStore(BankingStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    // ── Pending payments ─────────────────────────────────────────────────────

    public async Task UpsertPendingPaymentAsync(PendingPaymentDto payment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (payment.Status != PaymentApprovalStatus.Pending
            || payment.ReviewedBy is not null
            || payment.ReviewNotes is not null
            || payment.ReviewedAt is not null
            || payment.CurrencyRemediatedBy is not null
            || payment.CurrencyRemediationReason is not null
            || payment.CurrencyRemediatedAt is not null)
        {
            throw new InvalidOperationException(
                "Pending-payment creation accepts only an undecided intent. "
                + $"Use {nameof(TryTransitionPendingPaymentAsync)} for an approval or rejection.");
        }
        if (!IsCurrentNormalizedCurrency(payment.Currency))
            throw new InvalidOperationException("Pending-payment creation requires a recognized normalized currency.");

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_options.Schema}.pending_payments
                (pending_payment_id, entity_id, amount, effective_date, external_ref, notes,
                 status, reviewed_by, review_notes, initiated_at, reviewed_at, currency,
                 currency_remediated_by, currency_remediation_reason, currency_remediated_at)
            VALUES
                (@id, @eid, @amount, @eff, @xref, @notes,
                 @status, @reviewed_by, @review_notes, @initiated_at, @reviewed_at, @currency,
                 @currency_remediated_by, @currency_remediation_reason, @currency_remediated_at)
            ON CONFLICT (pending_payment_id) DO NOTHING
            RETURNING pending_payment_id;
            """;

        AddPendingPaymentParameters(cmd, payment);
        var inserted = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (inserted is not null)
            return;

        await using var retainedCommand = connection.CreateCommand();
        retainedCommand.CommandText = $"""
            SELECT {PendingPaymentColumns}
            FROM {_options.Schema}.pending_payments
            WHERE pending_payment_id = @id;
            """;
        retainedCommand.Parameters.AddWithValue("id", payment.PendingPaymentId);
        await using var reader = await retainedCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var retained = await reader.ReadAsync(ct).ConfigureAwait(false)
            ? ReadPendingPayment(reader)
            : null;
        if (retained is not null && IsSamePendingIntent(retained, payment))
            return;

        throw new InvalidOperationException(
            $"Pending payment '{payment.PendingPaymentId}' is immutable once retained; "
            + "the attempted insert conflicts with the authoritative intent or review decision.");
    }

    private static bool IsSamePendingIntent(PendingPaymentDto retained, PendingPaymentDto candidate)
        => retained.PendingPaymentId == candidate.PendingPaymentId
           && retained.EntityId == candidate.EntityId
           && retained.Amount == candidate.Amount
           && retained.EffectiveDate == candidate.EffectiveDate
           && string.Equals(retained.ExternalRef, candidate.ExternalRef, StringComparison.Ordinal)
           && string.Equals(retained.Notes, candidate.Notes, StringComparison.Ordinal)
           && retained.Status == candidate.Status
           && string.Equals(retained.ReviewedBy, candidate.ReviewedBy, StringComparison.Ordinal)
           && string.Equals(retained.ReviewNotes, candidate.ReviewNotes, StringComparison.Ordinal)
           && ToPostgresMicroseconds(retained.InitiatedAt) == ToPostgresMicroseconds(candidate.InitiatedAt)
           && (retained.ReviewedAt is null) == (candidate.ReviewedAt is null)
           && (retained.ReviewedAt is null
               || ToPostgresMicroseconds(retained.ReviewedAt.Value)
                   == ToPostgresMicroseconds(candidate.ReviewedAt!.Value))
           && string.Equals(retained.Currency, candidate.Currency, StringComparison.Ordinal)
           && string.Equals(
               retained.CurrencyRemediatedBy,
               candidate.CurrencyRemediatedBy,
               StringComparison.Ordinal)
           && string.Equals(
               retained.CurrencyRemediationReason,
               candidate.CurrencyRemediationReason,
               StringComparison.Ordinal)
           && (retained.CurrencyRemediatedAt is null) == (candidate.CurrencyRemediatedAt is null)
           && (retained.CurrencyRemediatedAt is null
               || ToPostgresMicroseconds(retained.CurrencyRemediatedAt.Value)
                   == ToPostgresMicroseconds(candidate.CurrencyRemediatedAt!.Value));

    private static long ToPostgresMicroseconds(DateTimeOffset value)
        => value.UtcTicks / TimeSpan.TicksPerMicrosecond;

    public async Task<PendingPaymentDto?> GetPendingPaymentAsync(
        Guid pendingPaymentId,
        CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT {PendingPaymentColumns}
            FROM {_options.Schema}.pending_payments
            WHERE pending_payment_id = @id;
            """;
        cmd.Parameters.AddWithValue("id", pendingPaymentId);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? ReadPendingPayment(reader)
            : null;
    }

    public async Task<IReadOnlyList<PendingPaymentDto>> GetAllPendingPaymentsAsync(
        CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT {PendingPaymentColumns}
            FROM {_options.Schema}.pending_payments
            ORDER BY initiated_at DESC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<PendingPaymentDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(ReadPendingPayment(reader));
        return results;
    }

    public async Task<PendingPaymentDto?> TryRemediatePendingPaymentCurrencyAsync(
        Guid pendingPaymentId,
        string currency,
        string remediatedBy,
        string remediationReason,
        DateTimeOffset remediatedAt,
        CancellationToken ct = default)
    {
        if (!IsCurrentNormalizedCurrency(currency))
            throw new ArgumentException("A recognized normalized currency is required.", nameof(currency));
        ArgumentException.ThrowIfNullOrWhiteSpace(remediatedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(remediationReason);

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_options.Schema}.pending_payments
            SET currency = @currency,
                currency_remediated_by = @remediated_by,
                currency_remediation_reason = @remediation_reason,
                currency_remediated_at = @remediated_at
            WHERE pending_payment_id = @id
              AND status = @pending_status
              AND currency IS NULL
            RETURNING {PendingPaymentColumns};
            """;
        cmd.Parameters.AddWithValue("currency", currency);
        cmd.Parameters.AddWithValue("remediated_by", remediatedBy);
        cmd.Parameters.AddWithValue("remediation_reason", remediationReason);
        cmd.Parameters.AddWithValue("remediated_at", remediatedAt);
        cmd.Parameters.AddWithValue("id", pendingPaymentId);
        cmd.Parameters.AddWithValue("pending_status", (short)PaymentApprovalStatus.Pending);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? ReadPendingPayment(reader)
            : null;
    }

    public async Task<PendingPaymentDto?> TryTransitionPendingPaymentAsync(
        Guid pendingPaymentId,
        PaymentApprovalStatus targetStatus,
        string? reviewedBy,
        string? reviewNotes,
        DateTimeOffset reviewedAt,
        CancellationToken ct = default)
    {
        if (targetStatus is not PaymentApprovalStatus.Approved and not PaymentApprovalStatus.Rejected)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetStatus),
                targetStatus,
                "Only Approved and Rejected are valid review transitions.");
        }

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_options.Schema}.pending_payments
            SET status = @target_status,
                reviewed_by = @reviewed_by,
                review_notes = @review_notes,
                reviewed_at = @reviewed_at
            WHERE pending_payment_id = @id
              AND status = @pending_status
              AND (@target_status <> @approved_status OR currency = ANY(@recognized_currencies))
            RETURNING {PendingPaymentColumns};
            """;
        cmd.Parameters.AddWithValue("target_status", (short)targetStatus);
        cmd.Parameters.AddWithValue("reviewed_by", (object?)reviewedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("review_notes", (object?)reviewNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("reviewed_at", reviewedAt);
        cmd.Parameters.AddWithValue("id", pendingPaymentId);
        cmd.Parameters.AddWithValue("pending_status", (short)PaymentApprovalStatus.Pending);
        cmd.Parameters.AddWithValue("approved_status", (short)PaymentApprovalStatus.Approved);
        cmd.Parameters.AddWithValue("recognized_currencies", CurrencyCodeCatalog.CurrentTransactionCodes.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? ReadPendingPayment(reader)
            : null;
    }

    // ── Bank transactions ────────────────────────────────────────────────────

    public async Task InsertBankTransactionAsync(
        BankTransactionDto transaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (!IsRecognizedNormalizedCurrency(transaction.Currency))
            throw new InvalidOperationException("Bank transactions require a recognized normalized currency.");
        if (transaction.PendingPaymentId is not null ||
            transaction.EvidenceId is not null ||
            transaction.CanonicalInputHash is not null)
        {
            throw new InvalidOperationException(
                $"Payment-bound bank evidence must be written through {nameof(RecordPaymentBankEvidenceAsync)}.");
        }

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_options.Schema}.bank_transactions
                ({BankTransactionColumns})
            VALUES
                (@id, @eid, @type, @eff, @tx_date, @settle, @amount, @currency,
                 @xref, @recorded_at, @is_voided, @recorded_by,
                 NULL, NULL, NULL)
            ON CONFLICT (bank_transaction_id) DO NOTHING;
            """;
        AddBankTransactionParameters(cmd, transaction);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<PaymentBankEvidenceWriteResult> RecordPaymentBankEvidenceAsync(
        BankTransactionDto transaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.PendingPaymentId is not Guid pendingPaymentId ||
            string.IsNullOrWhiteSpace(transaction.EvidenceId) ||
            string.IsNullOrWhiteSpace(transaction.CanonicalInputHash))
        {
            throw new ArgumentException(
                "Payment bank evidence requires PendingPaymentId, EvidenceId, and CanonicalInputHash.",
                nameof(transaction));
        }

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var dbTransaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        PendingPaymentDto? payment;
        await using (var paymentCommand = connection.CreateCommand())
        {
            paymentCommand.Transaction = dbTransaction;
            paymentCommand.CommandText = $"""
                SELECT {PendingPaymentColumns}
                FROM {_options.Schema}.pending_payments
                WHERE pending_payment_id = @id
                FOR SHARE;
                """;
            paymentCommand.Parameters.AddWithValue("id", pendingPaymentId);

            await using var reader = await paymentCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
            payment = await reader.ReadAsync(ct).ConfigureAwait(false)
                ? ReadPendingPayment(reader)
                : null;
        }

        if (payment is null)
        {
            await dbTransaction.CommitAsync(ct).ConfigureAwait(false);
            return new PaymentBankEvidenceWriteResult(PaymentBankEvidenceWriteStatus.PaymentNotFound);
        }

        if (payment.Status != PaymentApprovalStatus.Approved)
        {
            await dbTransaction.CommitAsync(ct).ConfigureAwait(false);
            return new PaymentBankEvidenceWriteResult(
                PaymentBankEvidenceWriteStatus.PaymentNotApproved,
                CurrentPaymentStatus: payment.Status);
        }

        if (!IsRecognizedNormalizedCurrency(payment.Currency))
        {
            await dbTransaction.CommitAsync(ct).ConfigureAwait(false);
            return new PaymentBankEvidenceWriteResult(
                PaymentBankEvidenceWriteStatus.PaymentCurrencyUnresolved,
                CurrentPaymentStatus: payment.Status);
        }

        if (transaction.EntityId != payment.EntityId ||
            transaction.Amount != payment.Amount ||
            transaction.EffectiveDate != payment.EffectiveDate ||
            !string.Equals(transaction.Currency, payment.Currency, StringComparison.Ordinal))
        {
            await dbTransaction.CommitAsync(ct).ConfigureAwait(false);
            return new PaymentBankEvidenceWriteResult(
                PaymentBankEvidenceWriteStatus.PaymentBindingConflict,
                CurrentPaymentStatus: payment.Status);
        }

        BankTransactionDto? inserted;
        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = dbTransaction;
            insertCommand.CommandText = $"""
                INSERT INTO {_options.Schema}.bank_transactions
                    ({BankTransactionColumns})
                VALUES
                    (@id, @eid, @type, @eff, @tx_date, @settle, @amount, @currency,
                     @xref, @recorded_at, @is_voided, @recorded_by,
                     @pending_payment_id, @evidence_id, @canonical_input_hash)
                ON CONFLICT (pending_payment_id, evidence_id) DO NOTHING
                RETURNING {BankTransactionColumns};
                """;
            AddBankTransactionParameters(insertCommand, transaction);
            insertCommand.Parameters.AddWithValue("pending_payment_id", pendingPaymentId);
            insertCommand.Parameters.AddWithValue("evidence_id", transaction.EvidenceId);
            insertCommand.Parameters.AddWithValue("canonical_input_hash", transaction.CanonicalInputHash);

            await using var reader = await insertCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
            inserted = await reader.ReadAsync(ct).ConfigureAwait(false)
                ? ReadBankTransaction(reader)
                : null;
        }

        if (inserted is not null)
        {
            await dbTransaction.CommitAsync(ct).ConfigureAwait(false);
            return new PaymentBankEvidenceWriteResult(
                PaymentBankEvidenceWriteStatus.Inserted,
                inserted,
                payment.Status);
        }

        BankTransactionDto? retained;
        await using (var retainedCommand = connection.CreateCommand())
        {
            retainedCommand.Transaction = dbTransaction;
            retainedCommand.CommandText = $"""
                SELECT {BankTransactionColumns}
                FROM {_options.Schema}.bank_transactions
                WHERE pending_payment_id = @pending_payment_id
                  AND evidence_id = @evidence_id;
                """;
            retainedCommand.Parameters.AddWithValue("pending_payment_id", pendingPaymentId);
            retainedCommand.Parameters.AddWithValue("evidence_id", transaction.EvidenceId);

            await using var reader = await retainedCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
            retained = await reader.ReadAsync(ct).ConfigureAwait(false)
                ? ReadBankTransaction(reader)
                : null;
        }

        if (retained is null)
        {
            throw new InvalidOperationException(
                "The payment evidence uniqueness conflict did not retain a readable transaction.");
        }

        var status = string.Equals(
            retained.CanonicalInputHash,
            transaction.CanonicalInputHash,
            StringComparison.Ordinal)
            ? PaymentBankEvidenceWriteStatus.Replay
            : PaymentBankEvidenceWriteStatus.IdempotencyConflict;
        await dbTransaction.CommitAsync(ct).ConfigureAwait(false);
        return new PaymentBankEvidenceWriteResult(status, retained, payment.Status);
    }

    public async Task<IReadOnlyList<BankTransactionDto>> GetBankTransactionsAsync(
        Guid? entityId = null,
        CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        if (entityId.HasValue)
        {
            cmd.CommandText = $"""
                SELECT {BankTransactionColumns}
                FROM {_options.Schema}.bank_transactions
                WHERE entity_id = @eid
                ORDER BY effective_date DESC;
                """;
            cmd.Parameters.AddWithValue("eid", entityId.Value);
        }
        else
        {
            cmd.CommandText = $"""
                SELECT {BankTransactionColumns}
                FROM {_options.Schema}.bank_transactions
                ORDER BY effective_date DESC;
                """;
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<BankTransactionDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(ReadBankTransaction(reader));
        return results;
    }

    // ── Utility ──────────────────────────────────────────────────────────────

    public async Task<bool> IsEmptyAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT NOT EXISTS (SELECT 1 FROM {_options.Schema}.pending_payments LIMIT 1)
               AND NOT EXISTS (SELECT 1 FROM {_options.Schema}.bank_transactions LIMIT 1);
            """;

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is true;
    }

    // ── Mapping helpers ──────────────────────────────────────────────────────

    private static void AddPendingPaymentParameters(NpgsqlCommand cmd, PendingPaymentDto payment)
    {
        cmd.Parameters.AddWithValue("id", payment.PendingPaymentId);
        cmd.Parameters.AddWithValue("eid", payment.EntityId);
        cmd.Parameters.AddWithValue("amount", payment.Amount);
        cmd.Parameters.AddWithValue("eff", payment.EffectiveDate);
        cmd.Parameters.AddWithValue("xref", (object?)payment.ExternalRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("notes", (object?)payment.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", (short)payment.Status);
        cmd.Parameters.AddWithValue("reviewed_by", (object?)payment.ReviewedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("review_notes", (object?)payment.ReviewNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("initiated_at", payment.InitiatedAt);
        cmd.Parameters.AddWithValue("reviewed_at", (object?)payment.ReviewedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("currency", (object?)payment.Currency ?? DBNull.Value);
        cmd.Parameters.AddWithValue("currency_remediated_by", (object?)payment.CurrencyRemediatedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("currency_remediation_reason", (object?)payment.CurrencyRemediationReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("currency_remediated_at", (object?)payment.CurrencyRemediatedAt ?? DBNull.Value);
    }

    private static void AddBankTransactionParameters(NpgsqlCommand cmd, BankTransactionDto transaction)
    {
        cmd.Parameters.AddWithValue("id", transaction.BankTransactionId);
        cmd.Parameters.AddWithValue("eid", transaction.EntityId);
        cmd.Parameters.AddWithValue("type", transaction.TransactionType);
        cmd.Parameters.AddWithValue("eff", transaction.EffectiveDate);
        cmd.Parameters.AddWithValue("tx_date", transaction.TransactionDate);
        cmd.Parameters.AddWithValue("settle", transaction.SettlementDate);
        cmd.Parameters.AddWithValue("amount", transaction.Amount);
        cmd.Parameters.AddWithValue("currency", transaction.Currency);
        cmd.Parameters.AddWithValue("xref", (object?)transaction.ExternalRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("recorded_at", transaction.RecordedAt);
        cmd.Parameters.AddWithValue("is_voided", transaction.IsVoided);
        cmd.Parameters.AddWithValue("recorded_by", (object?)transaction.RecordedBy ?? DBNull.Value);
    }

    private static PendingPaymentDto ReadPendingPayment(NpgsqlDataReader reader)
        => new(
            PendingPaymentId: reader.GetGuid(0),
            EntityId: reader.GetGuid(1),
            Amount: reader.GetDecimal(2),
            EffectiveDate: reader.GetFieldValue<DateOnly>(3),
            ExternalRef: reader.IsDBNull(4) ? null : reader.GetString(4),
            Notes: reader.IsDBNull(5) ? null : reader.GetString(5),
            Status: (PaymentApprovalStatus)reader.GetInt16(6),
            ReviewedBy: reader.IsDBNull(7) ? null : reader.GetString(7),
            ReviewNotes: reader.IsDBNull(8) ? null : reader.GetString(8),
            InitiatedAt: reader.GetFieldValue<DateTimeOffset>(9),
            ReviewedAt: reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            Currency: reader.IsDBNull(11) ? null : reader.GetString(11),
            CurrencyRemediatedBy: reader.IsDBNull(12) ? null : reader.GetString(12),
            CurrencyRemediationReason: reader.IsDBNull(13) ? null : reader.GetString(13),
            CurrencyRemediatedAt: reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14));

    private static BankTransactionDto ReadBankTransaction(NpgsqlDataReader reader)
        => new(
            BankTransactionId: reader.GetGuid(0),
            EntityId: reader.GetGuid(1),
            TransactionType: reader.GetString(2),
            EffectiveDate: reader.GetFieldValue<DateOnly>(3),
            TransactionDate: reader.GetFieldValue<DateOnly>(4),
            SettlementDate: reader.GetFieldValue<DateOnly>(5),
            Amount: reader.GetDecimal(6),
            Currency: reader.GetString(7),
            ExternalRef: reader.IsDBNull(8) ? null : reader.GetString(8),
            RecordedAt: reader.GetFieldValue<DateTimeOffset>(9),
            IsVoided: reader.GetBoolean(10),
            RecordedBy: reader.IsDBNull(11) ? null : reader.GetString(11),
            PendingPaymentId: reader.IsDBNull(12) ? null : reader.GetGuid(12),
            EvidenceId: reader.IsDBNull(13) ? null : reader.GetString(13),
            CanonicalInputHash: reader.IsDBNull(14) ? null : reader.GetString(14));

    private static bool IsRecognizedNormalizedCurrency(string? currency)
        => CurrencyCodeCatalog.TryNormalizeRecognized(currency, out var normalized)
           && string.Equals(currency, normalized, StringComparison.Ordinal);

    private static bool IsCurrentNormalizedCurrency(string? currency)
        => CurrencyCodeCatalog.TryNormalizeCurrent(currency, out var normalized)
           && string.Equals(currency, normalized, StringComparison.Ordinal);

}
