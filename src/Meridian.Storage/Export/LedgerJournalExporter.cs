using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace Meridian.Storage.Export;

/// <summary>
/// Exports one row per immutable journal line, retaining the full audit envelopes. Decimal amounts
/// are invariant decimal text in both formats so Parquet readers cannot truncate monetary precision.
/// Mapping changes only the external account column and never the source journal.
/// </summary>
public static class LedgerJournalExporter
{
    private static readonly string[] Columns =
    [
        "journal_entry_id", "entry_id", "timestamp", "description", "account_name", "external_account",
        "account_type", "symbol", "financial_account_id", "debit", "credit", "strategy_id", "order_id",
        "fill_id", "activity_type", "effective_date", "idempotency_key", "metadata_json", "dimensions_json",
        "currency_json", "posting_metadata_json"
    ];

    public static byte[] ExportCsv(
        IReadOnlyLedger ledger,
        IReadOnlyDictionary<string, string>? accountMapping = null,
        LedgerQuery? query = null,
        IReadOnlyDictionary<Guid, string>? postingMetadata = null)
    {
        var rows = BuildRows(ledger, accountMapping, query, postingMetadata);
        var result = new StringBuilder();
        result.AppendLine(string.Join(',', Columns));
        foreach (var row in rows)
            result.AppendLine(string.Join(',', row.Select(SpreadsheetFormulaGuard.EscapeCsvCell)));
        return Encoding.UTF8.GetBytes(result.ToString());
    }

    public static async Task<byte[]> ExportParquetAsync(
        IReadOnlyLedger ledger,
        IReadOnlyDictionary<string, string>? accountMapping = null,
        LedgerQuery? query = null,
        CancellationToken ct = default,
        IReadOnlyDictionary<Guid, string>? postingMetadata = null)
    {
        ct.ThrowIfCancellationRequested();
        var rows = BuildRows(ledger, accountMapping, query, postingMetadata);
        var fields = Columns.Select(static name => new DataField<string>(name)).ToArray();
        using var output = new MemoryStream();
        using (var writer = await ParquetWriter.CreateAsync(new ParquetSchema(fields), output, cancellationToken: ct).ConfigureAwait(false))
        {
            if (rows.Count > 0)
            {
                using var group = writer.CreateRowGroup();
                for (var column = 0; column < fields.Length; column++)
                {
                    ct.ThrowIfCancellationRequested();
                    var values = rows.Select(row => row[column]).ToArray();
                    await group.WriteColumnAsync(new DataColumn(fields[column], values), ct).ConfigureAwait(false);
                }
            }
        }
        return output.ToArray();
    }

    /// <summary>Preserves governed posting metadata (book, period, source event, rules and approval).</summary>
    public static byte[] ExportCsv(IReadOnlyList<LedgerJournalEntryRecord> records, IReadOnlyDictionary<string, string>? accountMapping = null, Guid? ledgerBookId = null)
    {
        var (ledger, metadata) = BuildPostedSnapshot(records, ledgerBookId);
        return ExportCsv(ledger, accountMapping, postingMetadata: metadata);
    }

    public static Task<byte[]> ExportParquetAsync(IReadOnlyList<LedgerJournalEntryRecord> records,
        IReadOnlyDictionary<string, string>? accountMapping = null, CancellationToken ct = default, Guid? ledgerBookId = null)
    {
        var (ledger, metadata) = BuildPostedSnapshot(records, ledgerBookId);
        return ExportParquetAsync(ledger, accountMapping, ct: ct, postingMetadata: metadata);
    }

    private static (IReadOnlyLedger Ledger, IReadOnlyDictionary<Guid, string> Metadata) BuildPostedSnapshot(IReadOnlyList<LedgerJournalEntryRecord> records, Guid? ledgerBookId)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ledger = new Meridian.Ledger.Ledger();
        var metadata = new Dictionary<Guid, string>();
        foreach (var record in records)
        {
            ledger.Post(record.Entry);
            metadata.Add(record.Entry.JournalEntryId, JsonSerializer.Serialize(
                LedgerJournalPostingExportMetadata.FromRecord(record, ledgerBookId),
                LedgerJournalExportJsonContext.Default.LedgerJournalPostingExportMetadata));
        }
        return (ledger, metadata);
    }

    private static List<string[]> BuildRows(
        IReadOnlyLedger ledger,
        IReadOnlyDictionary<string, string>? accountMapping,
        LedgerQuery? query,
        IReadOnlyDictionary<Guid, string>? postingMetadata)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var rows = new List<string[]>();
        foreach (var journal in ledger.GetJournalEntries(query ?? new LedgerQuery()))
        {
            var metadata = journal.Metadata;
            var metadataJson = JsonSerializer.Serialize(metadata, LedgerJournalExportJsonContext.Default.JournalEntryMetadata);
            foreach (var line in journal.Lines)
            {
                var externalAccount = line.Account.Name;
                if (accountMapping is not null &&
                    (!accountMapping.TryGetValue(line.Account.Name, out externalAccount) || string.IsNullOrWhiteSpace(externalAccount)))
                    throw new ArgumentException($"No external account mapping was supplied for '{line.Account.Name}'.", nameof(accountMapping));
                rows.Add(
                [
                    journal.JournalEntryId.ToString("D"), line.EntryId.ToString("D"),
                    journal.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    journal.Description, line.Account.Name, externalAccount!, line.Account.AccountType.ToString(),
                    line.Account.Symbol ?? metadata.Symbol ?? string.Empty,
                    line.Account.FinancialAccountId ?? metadata.FinancialAccountId ?? string.Empty,
                    line.Debit.ToString(CultureInfo.InvariantCulture), line.Credit.ToString(CultureInfo.InvariantCulture),
                    metadata.StrategyId ?? string.Empty, metadata.OrderId?.ToString("D") ?? string.Empty,
                    metadata.FillId?.ToString("D") ?? string.Empty, metadata.ActivityType ?? string.Empty,
                    metadata.EffectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                    metadata.IdempotencyKey ?? string.Empty, metadataJson,
                    JsonSerializer.Serialize(line.Dimensions, LedgerJournalExportJsonContext.Default.LedgerLineDimensionSet),
                    JsonSerializer.Serialize(line.Currency, LedgerJournalExportJsonContext.Default.LedgerEntryCurrency),
                    postingMetadata?.GetValueOrDefault(journal.JournalEntryId) ?? "null"
                ]);
            }
        }
        return rows;
    }
}
