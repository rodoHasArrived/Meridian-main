using Meridian.Contracts.Banking;
using Npgsql;

namespace Meridian.Storage.Banking;

/// <summary>
/// PostgreSQL implementation of <see cref="IBankingStore"/> using raw Npgsql.
/// </summary>
public sealed class PostgresBankingStore : IBankingStore
{
    private readonly BankingStoreOptions _options;

    public PostgresBankingStore(BankingStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    // ── Pending payments ─────────────────────────────────────────────────────

    public async Task UpsertPendingPaymentAsync(PendingPaymentDto payment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payment);
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_options.Schema}.pending_payments
                (pending_payment_id, entity_id, amount, effective_date, external_ref, notes,
                 status, reviewed_by, review_notes, initiated_at, reviewed_at)
            VALUES
                (@id, @eid, @amount, @eff, @xref, @notes,
                 @status, @reviewed_by, @review_notes, @initiated_at, @reviewed_at)
            ON CONFLICT (pending_payment_id) DO UPDATE SET
                entity_id       = EXCLUDED.entity_id,
                amount          = EXCLUDED.amount,
                effective_date  = EXCLUDED.effective_date,
                external_ref    = EXCLUDED.external_ref,
                notes           = EXCLUDED.notes,
                status          = EXCLUDED.status,
                reviewed_by     = EXCLUDED.reviewed_by,
                review_notes    = EXCLUDED.review_notes,
                initiated_at    = EXCLUDED.initiated_at,
                reviewed_at     = EXCLUDED.reviewed_at;
            """;

        cmd.Parameters.AddWithValue("id",          payment.PendingPaymentId);
        cmd.Parameters.AddWithValue("eid",         payment.EntityId);
        cmd.Parameters.AddWithValue("amount",      payment.Amount);
        cmd.Parameters.AddWithValue("eff",         payment.EffectiveDate);
        cmd.Parameters.AddWithValue("xref",        (object?)payment.ExternalRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("notes",       (object?)payment.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status",      (short)payment.Status);
        cmd.Parameters.AddWithValue("reviewed_by", (object?)payment.ReviewedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("review_notes",(object?)payment.ReviewNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("initiated_at",payment.InitiatedAt);
        cmd.Parameters.AddWithValue("reviewed_at", (object?)payment.ReviewedAt ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<PendingPaymentDto?> GetPendingPaymentAsync(Guid pendingPaymentId, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT pending_payment_id, entity_id, amount, effective_date, external_ref, notes,
                   status, reviewed_by, review_notes, initiated_at, reviewed_at
            FROM {_options.Schema}.pending_payments
            WHERE pending_payment_id = @id;
            """;
        cmd.Parameters.AddWithValue("id", pendingPaymentId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadPendingPayment(reader);
    }

    public async Task<IReadOnlyList<PendingPaymentDto>> GetAllPendingPaymentsAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT pending_payment_id, entity_id, amount, effective_date, external_ref, notes,
                   status, reviewed_by, review_notes, initiated_at, reviewed_at
            FROM {_options.Schema}.pending_payments
            ORDER BY initiated_at DESC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<PendingPaymentDto>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadPendingPayment(reader));
        return results;
    }

    // ── Bank transactions ────────────────────────────────────────────────────

    public async Task InsertBankTransactionAsync(BankTransactionDto transaction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_options.Schema}.bank_transactions
                (bank_transaction_id, entity_id, transaction_type, effective_date,
                 transaction_date, settlement_date, amount, currency, external_ref,
                 recorded_at, is_voided)
            VALUES
                (@id, @eid, @type, @eff, @tx_date, @settle, @amount, @currency,
                 @xref, @recorded_at, @is_voided)
            ON CONFLICT (bank_transaction_id) DO NOTHING;
            """;

        cmd.Parameters.AddWithValue("id",          transaction.BankTransactionId);
        cmd.Parameters.AddWithValue("eid",         transaction.EntityId);
        cmd.Parameters.AddWithValue("type",        transaction.TransactionType);
        cmd.Parameters.AddWithValue("eff",         transaction.EffectiveDate);
        cmd.Parameters.AddWithValue("tx_date",     transaction.TransactionDate);
        cmd.Parameters.AddWithValue("settle",      transaction.SettlementDate);
        cmd.Parameters.AddWithValue("amount",      transaction.Amount);
        cmd.Parameters.AddWithValue("currency",    transaction.Currency);
        cmd.Parameters.AddWithValue("xref",        (object?)transaction.ExternalRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("recorded_at", transaction.RecordedAt);
        cmd.Parameters.AddWithValue("is_voided",   transaction.IsVoided);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<BankTransactionDto>> GetBankTransactionsAsync(
        Guid? entityId = null,
        CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        if (entityId.HasValue)
        {
            cmd.CommandText = $"""
                SELECT bank_transaction_id, entity_id, transaction_type, effective_date,
                       transaction_date, settlement_date, amount, currency, external_ref,
                       recorded_at, is_voided
                FROM {_options.Schema}.bank_transactions
                WHERE entity_id = @eid
                ORDER BY effective_date DESC;
                """;
            cmd.Parameters.AddWithValue("eid", entityId.Value);
        }
        else
        {
            cmd.CommandText = $"""
                SELECT bank_transaction_id, entity_id, transaction_type, effective_date,
                       transaction_date, settlement_date, amount, currency, external_ref,
                       recorded_at, is_voided
                FROM {_options.Schema}.bank_transactions
                ORDER BY effective_date DESC;
                """;
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<BankTransactionDto>();
        while (await reader.ReadAsync(ct))
            results.Add(ReadBankTransaction(reader));
        return results;
    }

    // ── Utility ──────────────────────────────────────────────────────────────

    public async Task<bool> IsEmptyAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT NOT EXISTS (SELECT 1 FROM {_options.Schema}.pending_payments LIMIT 1)
               AND NOT EXISTS (SELECT 1 FROM {_options.Schema}.bank_transactions LIMIT 1);
            """;

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }

    // ── Mapping helpers ──────────────────────────────────────────────────────

    private static PendingPaymentDto ReadPendingPayment(NpgsqlDataReader r)
        => new(
            PendingPaymentId: r.GetGuid(0),
            EntityId:         r.GetGuid(1),
            Amount:           r.GetDecimal(2),
            EffectiveDate:    r.GetFieldValue<DateOnly>(3),
            ExternalRef:      r.IsDBNull(4) ? null : r.GetString(4),
            Notes:            r.IsDBNull(5) ? null : r.GetString(5),
            Status:           (PaymentApprovalStatus)r.GetInt16(6),
            ReviewedBy:       r.IsDBNull(7) ? null : r.GetString(7),
            ReviewNotes:      r.IsDBNull(8) ? null : r.GetString(8),
            InitiatedAt:      r.GetFieldValue<DateTimeOffset>(9),
            ReviewedAt:       r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10));

    private static BankTransactionDto ReadBankTransaction(NpgsqlDataReader r)
        => new(
            BankTransactionId: r.GetGuid(0),
            EntityId:          r.GetGuid(1),
            TransactionType:   r.GetString(2),
            EffectiveDate:     r.GetFieldValue<DateOnly>(3),
            TransactionDate:   r.GetFieldValue<DateOnly>(4),
            SettlementDate:    r.GetFieldValue<DateOnly>(5),
            Amount:            r.GetDecimal(6),
            Currency:          r.GetString(7),
            ExternalRef:       r.IsDBNull(8) ? null : r.GetString(8),
            RecordedAt:        r.GetFieldValue<DateTimeOffset>(9),
            IsVoided:          r.GetBoolean(10));
}
