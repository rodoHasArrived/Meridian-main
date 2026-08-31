using System.Data;
using Meridian.Ledger;
using Npgsql;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Surveys and repairs the currency detail that historical journal legs are missing.
/// <para>
/// V_ledger_026 added transaction currency, both transaction-side amounts, and the FX rate to
/// <c>journal_legs</c> as nullable columns, and legs written before it have them null. So do legs
/// written after it but before #2800, because the shared validator's dimension rebuild dropped a
/// line's currency on the way to the store. The append path is fixed; nothing repairs what is
/// already retained, and nothing reports it either — a currency-blind leg reads back as a leg that
/// simply has no currency detail.
/// </para>
/// <para>
/// The repair only ever writes the identity translation — transaction currency equal to the
/// functional currency, transaction amounts equal to the functional amounts, FX rate 1 — and never
/// touches a leg's debit or credit. The original rate on a foreign leg is not recoverable from
/// anything retained, so this deliberately refuses to invent one: see
/// <see cref="LedgerCurrencyBackfillDisposition"/> for what each refusal means.
/// </para>
/// </summary>
public sealed class PostgresLedgerCurrencyBackfill
{
    private readonly LedgerJournalStoreOptions _options;

    public PostgresLedgerCurrencyBackfill(LedgerJournalStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Reports every retained leg still missing its currency detail, grouped by ledger book and by
    /// what can be done about it. This is the surface the gap shows up on: currency-blind legs are
    /// otherwise indistinguishable from legs that were never meant to carry currency.
    /// </summary>
    public async Task<LedgerCurrencyBackfillSurvey> SurveyAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select ledger_book_id,
                   base_currency,
                   disposition,
                   (count(*))::int as currency_blind_legs,
                   (count(*) filter (where period_status <> 'Open'))::int as closed_period_legs
            from {Qualified("journal_leg_currency_backfill_status")}
            group by ledger_book_id, base_currency, disposition
            order by disposition, ledger_book_id;
            """;

        var scopes = new List<LedgerCurrencyBackfillScope>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            scopes.Add(new LedgerCurrencyBackfillScope(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                ParseDisposition(reader.GetString(2)),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }

        return new LedgerCurrencyBackfillSurvey(scopes);
    }

    /// <summary>
    /// Repairs every leg the retained evidence already determines, and returns how many were
    /// completed. V_ledger_029 runs the same repair once at migration time; this re-runs it, which
    /// matters because a book that had no currency evidence then accumulates it with every posting
    /// appended since — the same legs become repairable without anyone having to assert anything.
    /// </summary>
    public async Task<int> RepairEvidencedLegsAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);

        var repaired = await RepairAsync(
                connection,
                transaction,
                LedgerCurrencyBackfillDisposition.Repairable,
                ledgerBookId: null,
                ct)
            .ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return repaired;
    }

    /// <summary>
    /// Completes the currency-blind legs of one ledger book on an operator's assertion that the
    /// book transacted only in <paramref name="currency"/>, and records that assertion as the
    /// authority for the change.
    /// <para>
    /// This is narrow on purpose. It completes an evidence gap; it never overrules evidence. A book
    /// whose legs show foreign-currency denomination, or whose functional currency disagrees with
    /// its base currency, is refused — no affirmation makes an unrecoverable FX rate recoverable.
    /// So is a book whose blind legs the data already determines, which needs
    /// <see cref="RepairEvidencedLegsAsync"/> and no assertion at all.
    /// </para>
    /// </summary>
    /// <param name="currency">
    /// The currency the operator is asserting, which must match the book's own base currency.
    /// Naming it is the check: an operator who believes the book is denominated in something else
    /// is describing a different problem, and stamping either code would be wrong.
    /// </param>
    /// <exception cref="LedgerValidationException">
    /// Thrown when the book is unknown, when the asserted currency is not the book's base currency,
    /// or when the book's currency-blind legs are not waiting on an affirmation.
    /// </exception>
    public async Task<LedgerCurrencyAffirmationResult> AffirmSingleCurrencyBookAsync(
        Guid ledgerBookId,
        string currency,
        string actor,
        string rationale,
        CancellationToken ct = default)
    {
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        var asserted = NormalizeCurrency(currency, nameof(currency));
        var affirmedBy = RequireText(actor);
        var reason = RequireText(rationale);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);

        var baseCurrency = await LoadBaseCurrencyAsync(connection, transaction, ledgerBookId, ct)
            .ConfigureAwait(false)
            ?? throw new LedgerValidationException($"Ledger book '{ledgerBookId}' was not found.");

        if (!string.Equals(baseCurrency, asserted, StringComparison.Ordinal))
        {
            throw new LedgerValidationException(
                $"Ledger book '{ledgerBookId}' is denominated in '{baseCurrency}', but the affirmation asserts '{asserted}'.");
        }

        await RequireAwaitingAffirmationAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false);

        var repaired = await RepairAsync(
                connection,
                transaction,
                LedgerCurrencyBackfillDisposition.UnaffirmedSingleCurrency,
                ledgerBookId,
                ct)
            .ConfigureAwait(false);

        var affirmationId = Guid.NewGuid();
        var affirmedAt = DateTimeOffset.UtcNow;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("journal_leg_currency_affirmations")} (
                    affirmation_id,
                    ledger_book_id,
                    affirmed_currency,
                    actor,
                    rationale,
                    affirmed_at,
                    legs_repaired)
                values (
                    @affirmation_id,
                    @ledger_book_id,
                    @affirmed_currency,
                    @actor,
                    @rationale,
                    @affirmed_at,
                    @legs_repaired);
                """;
            command.Parameters.AddWithValue("affirmation_id", affirmationId);
            command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
            command.Parameters.AddWithValue("affirmed_currency", asserted);
            command.Parameters.AddWithValue("actor", affirmedBy);
            command.Parameters.AddWithValue("rationale", reason);
            command.Parameters.AddWithValue("affirmed_at", affirmedAt.UtcDateTime);
            command.Parameters.AddWithValue("legs_repaired", repaired);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return new LedgerCurrencyAffirmationResult(
            affirmationId,
            ledgerBookId,
            asserted,
            affirmedBy,
            repaired,
            affirmedAt);
    }

    /// <summary>
    /// Stamps the identity translation onto the currency-blind legs carrying one disposition,
    /// optionally within a single ledger book. The <c>transaction_currency is null</c> guard makes
    /// this idempotent, and debit/credit are read into the transaction-side columns, never written.
    /// <para>
    /// V_ledger_030 made <c>journal_legs</c> immutable at the database; this repair is the one
    /// governed mutation its trigger admits, and only from a transaction that has declared itself.
    /// The declaration is transaction-scoped (<c>set_config(..., is_local => true)</c>), so it
    /// expires with the transaction and never leaks to other work on the connection.
    /// </para>
    /// </summary>
    private async Task<int> RepairAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerCurrencyBackfillDisposition disposition,
        Guid? ledgerBookId,
        CancellationToken ct)
    {
        await using (var declare = connection.CreateCommand())
        {
            declare.Transaction = transaction;
            declare.CommandText =
                "select set_config('meridian.ledger_currency_repair', 'on', true);";
            await declare.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var bookFilter = ledgerBookId.HasValue
            ? "\n              and s.ledger_book_id = @ledger_book_id"
            : string.Empty;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            update {Qualified("journal_legs")} l
            set transaction_currency = s.base_currency,
                functional_currency = s.base_currency,
                transaction_debit = l.debit,
                transaction_credit = l.credit,
                fx_rate_to_functional = 1
            from {Qualified("journal_leg_currency_backfill_status")} s
            where s.entry_id = l.entry_id
              and s.disposition = @disposition{bookFilter}
              and l.transaction_currency is null;
            """;
        command.Parameters.AddWithValue("disposition", disposition.ToString());
        if (ledgerBookId.HasValue)
        {
            command.Parameters.AddWithValue("ledger_book_id", ledgerBookId.Value);
        }

        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<string?> LoadBaseCurrencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ledgerBookId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select upper(trim(base_currency))
            from {Qualified("ledger_books")}
            where ledger_book_id = @ledger_book_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is string text ? text : null;
    }

    /// <summary>
    /// Fails closed unless every currency-blind leg in the book is waiting on exactly this
    /// affirmation, naming what it found instead so the operator can see why.
    /// </summary>
    private async Task RequireAwaitingAffirmationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ledgerBookId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select distinct disposition
            from {Qualified("journal_leg_currency_backfill_status")}
            where ledger_book_id = @ledger_book_id
            order by disposition;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);

        var dispositions = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                dispositions.Add(reader.GetString(0));
            }
        }

        if (dispositions.Count == 0)
        {
            throw new LedgerValidationException(
                $"Ledger book '{ledgerBookId}' has no currency-blind legs to affirm.");
        }

        var awaiting = LedgerCurrencyBackfillDisposition.UnaffirmedSingleCurrency.ToString();
        var unexpected = dispositions
            .Where(value => !string.Equals(value, awaiting, StringComparison.Ordinal))
            .ToArray();
        if (unexpected.Length > 0)
        {
            throw new LedgerValidationException(
                $"Ledger book '{ledgerBookId}' currency-blind legs are not awaiting an affirmation " +
                $"({string.Join(", ", unexpected)}).");
        }
    }

    private static LedgerCurrencyBackfillDisposition ParseDisposition(string value)
        => Enum.TryParse<LedgerCurrencyBackfillDisposition>(value, ignoreCase: false, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Unsupported ledger currency backfill disposition '{value}'.");

    /// <summary>
    /// Mirrors the normalization <see cref="LedgerEntryCurrency"/> applies, so an affirmation can
    /// only ever assert a code the ledger's own currency type would accept.
    /// </summary>
    private static string NormalizeCurrency(string? currency, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new LedgerValidationException($"{parameterName} must not be null or whitespace.");
        }

        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(static c => c is >= 'A' and <= 'Z'))
        {
            throw new LedgerValidationException(
                $"{parameterName} must be a three-letter ISO-style currency code.");
        }

        return normalized;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("LedgerJournalStoreOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private string Qualified(string tableName)
        => $"{ValidateIdentifier(_options.SchemaName)}.{ValidateIdentifier(tableName)}";

    private static string ValidateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("A PostgreSQL identifier is required.");
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                throw new InvalidOperationException($"Identifier '{value}' contains an invalid character.");
            }
        }

        return value;
    }
}
