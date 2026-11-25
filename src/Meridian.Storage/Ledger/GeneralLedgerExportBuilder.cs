using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;
using Meridian.Storage.Export;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace Meridian.Storage.Ledger;

/// <summary>A frozen journal-line dataset. Amount cells are invariant decimal text, never binary floating point.</summary>
public sealed record GeneralLedgerExportDataset(
    string SnapshotSha256,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string DebitTotal,
    string CreditTotal,
    string Variance,
    int LegacyCurrencyLineCount,
    IReadOnlyList<string> EvidenceReferences);

/// <summary>One atomic, internally consistent download of all general-ledger.v1 formats and their agreement manifest.</summary>
public sealed record GeneralLedgerExportPackage(byte[] Bytes, string SnapshotSha256, int RowCount);

/// <summary>
/// Serializes one authoritative snapshot to CSV, text-cell XLSX and typed-decimal Parquet. This is
/// a read-only, neutral interchange schema; it does not authorize posting or claim vendor import compatibility.
/// </summary>
public static class GeneralLedgerExportBuilder
{
    public const string SchemaVersion = "general-ledger.v1";
    public const int MaximumRows = 100_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] Headers =
    [
        "SchemaVersion", "LedgerBookId", "PeriodId", "GlobalSequence", "JournalEntryId", "EntryId",
        "OccurredAtUtc", "EffectiveDate", "AccountName", "AccountType", "FinancialAccountId", "Symbol",
        "FunctionalCurrency", "Debit", "Credit", "TransactionCurrency", "TransactionDebit", "TransactionCredit",
        "FxRateToFunctional", "CurrencyEvidence", "Description", "AggregateId", "SourceEventId", "SourceJournalEntryId",
        "CommandId", "CorrelationId", "PostingKind", "AccountingBasis", "AccountingPolicyId", "AccountingPolicyVersion",
        "IdempotencyKey", "DimensionsJson", "EvidenceReferencesJson", "JournalMetadataJson"
    ];
    private static readonly HashSet<string> DecimalColumns =
        ["Debit", "Credit", "TransactionDebit", "TransactionCredit", "FxRateToFunctional"];

    public static GeneralLedgerExportDataset BuildDataset(LedgerBookRecord book, LedgerReportingSnapshot snapshot,
        GeneralLedgerUnrealizedInputs? unrealizedInputs = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(snapshot);
        var period = snapshot.Period;
        if (period is null || period.LedgerBookId != book.LedgerBookId)
            throw new InvalidOperationException("The captured accounting period must belong to the exact export book.");
        if (snapshot.Journals.Any(record => record.PeriodId != period.PeriodId))
            throw new InvalidOperationException("The captured journals include a different accounting period.");
        if (snapshot.Journals.Select(record => record.Entry.JournalEntryId).Distinct().Count() != snapshot.Journals.Count)
            throw new InvalidOperationException("The snapshot contains duplicate journal identities.");
        var journalIds = snapshot.Journals.Select(record => record.Entry.JournalEntryId).ToHashSet();
        if (snapshot.TaxLotDisposalHistory.Any(disposal => !journalIds.Contains(disposal.JournalEntryId)))
            throw new InvalidOperationException("The captured disposal history belongs to a different journal population.");
        if (snapshot.Journals.Sum(record => (long)record.Entry.Lines.Count) > MaximumRows)
            throw new InvalidOperationException($"The export exceeds {MaximumRows} journal lines; select a smaller accounting period.");

        var rows = new List<IReadOnlyList<string>>();
        var evidence = new SortedSet<string>(StringComparer.Ordinal);
        decimal debits = 0m, credits = 0m;
        var legacyLines = 0;
        foreach (var record in snapshot.Journals.OrderBy(record => record.GlobalSequence).ThenBy(record => record.Entry.JournalEntryId))
        {
            var journal = record.Entry;
            if (!journal.IsBalanced)
                throw new InvalidOperationException($"Journal '{journal.JournalEntryId}' is unbalanced.");
            evidence.Add($"ledger://books/{book.LedgerBookId:D}/periods/{period.PeriodId:D}/journals/{journal.JournalEntryId:D}");
            foreach (var reference in journal.Metadata.EvidenceReferences)
                evidence.Add(reference.Uri);
            foreach (var line in journal.Lines.OrderBy(line => line.EntryId))
            {
                var currency = line.Currency;
                if (currency is not null && !string.Equals(currency.FunctionalCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A journal line's functional currency differs from its ledger book.");
                if (currency is null)
                    legacyLines++;
                debits += line.Debit;
                credits += line.Credit;
                var dimensions = line.Dimensions is null ? null : line.Dimensions with
                {
                    ExternalGlDimensions = new SortedDictionary<string, string>(line.Dimensions.ExternalGlDimensions
                        .ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal)
                };
                var metadata = journal.Metadata with
                {
                    Tags = journal.Metadata.Tags is null ? null : new SortedDictionary<string, string>(
                        journal.Metadata.Tags.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal)
                };
                rows.Add(Array.AsReadOnly(new[]
                {
                    SchemaVersion, book.LedgerBookId.ToString("D"), period.PeriodId.ToString("D"),
                    record.GlobalSequence.ToString(CultureInfo.InvariantCulture), journal.JournalEntryId.ToString("D"),
                    line.EntryId.ToString("D"), line.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    journal.Metadata.EffectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                    line.Account.Name, line.Account.AccountType.ToString(), line.Account.FinancialAccountId ?? "",
                    line.Account.Symbol ?? "", book.BaseCurrency, Amount(line.Debit), Amount(line.Credit),
                    currency?.TransactionCurrency ?? "", NullableAmount(currency?.TransactionDebit),
                    NullableAmount(currency?.TransactionCredit), NullableAmount(currency?.FxRateToFunctional),
                    currency is null ? "book-base-currency-only" : "explicit-line-currency",
                    line.Description, record.AggregateId.ToString("D"), record.SourceEventId?.ToString("D") ?? "",
                    record.SourceJournalEntryId?.ToString("D") ?? "", record.CommandId?.ToString("D") ?? "",
                    record.CorrelationId?.ToString("D") ?? "", record.PostingKind.ToString(), record.AccountingBasis.ToString(),
                    record.AccountingPolicyId, record.AccountingPolicyVersion, journal.Metadata.IdempotencyKey ?? "",
                    JsonSerializer.Serialize(dimensions), JsonSerializer.Serialize(journal.Metadata.EvidenceReferences),
                    JsonSerializer.Serialize(metadata)
                }));
            }
        }

        var frozenRows = rows.AsReadOnly();
        var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            SchemaVersion,
            Book = book,
            Period = period,
            Rows = frozenRows,
            Disposals = snapshot.TaxLotDisposalHistory.OrderBy(disposal => disposal.MutationBatchId).ToArray(),
            OpenPositions = snapshot.OpenPositions,
            UnrealizedInputs = unrealizedInputs
        }));
        return new(hash, Array.AsReadOnly(Headers), frozenRows, Amount(debits), Amount(credits), Amount(debits - credits),
            legacyLines, evidence.ToArray());
    }

    public static async Task<GeneralLedgerExportPackage> BuildAsync(LedgerBookRecord book,
        LedgerReportingSnapshot snapshot, CancellationToken ct = default, GeneralLedgerUnrealizedInputs? unrealizedInputs = null)
    {
        ct.ThrowIfCancellationRequested();
        var dataset = BuildDataset(book, snapshot, unrealizedInputs);
        var symbolPnl = GeneralLedgerSymbolPnlBuilder.Build(book, snapshot, unrealizedInputs);
        var artifacts = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [$"{SchemaVersion}.csv"] = Encoding.UTF8.GetBytes(BuildCsv(dataset)),
            [$"{SchemaVersion}.xlsx"] = BuiltInLedgerReportBinaryRenderer.Instance.RenderWorkbook(
                [new LedgerReportTable("General Ledger", dataset.Columns, dataset.Rows)]),
            [$"{SchemaVersion}.parquet"] = await BuildParquetAsync(dataset, ct).ConfigureAwait(false)
        };
        artifacts["symbol-pnl.v1.json"] = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = GeneralLedgerSymbolPnlBuilder.SchemaVersion,
            dataset.SnapshotSha256,
            unrealizedAvailability = symbolPnl.Count > 0 && symbolPnl.All(row => row.UnrealizedAvailability == "Available") ? "Available" : "Unavailable",
            unrealizedInputs,
            openPositionEvidence = snapshot.OpenPositions,
            rows = symbolPnl
        }, JsonOptions);
        var pnlCsv = new StringBuilder("SchemaVersion,Symbol,FinancialAccountId,FunctionalCurrency,RealizedAvailability,Proceeds,CostBasis,BookRealizedGainLoss,EconomicRealizedGainLoss,RealizedReason,UnrealizedAvailability,UnrealizedGainLoss,UnrealizedReason,DisposalJournalIds\r\n");
        foreach (var row in symbolPnl)
            pnlCsv.Append(string.Join(',', new[] { GeneralLedgerSymbolPnlBuilder.SchemaVersion, row.Symbol, row.FinancialAccountId,
                row.FunctionalCurrency, row.RealizedAvailability, row.Proceeds, row.CostBasis, row.BookRealizedGainLoss,
                row.EconomicRealizedGainLoss, row.RealizedReason, row.UnrealizedAvailability, row.UnrealizedGainLoss,
                row.UnrealizedReason, string.Join('|', row.DisposalJournalIds) }.Select(SpreadsheetFormulaGuard.EscapeCsvCell))).Append("\r\n");
        artifacts["symbol-pnl.v1.csv"] = Encoding.UTF8.GetBytes(pnlCsv.ToString());
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = SchemaVersion,
            ledgerBookId = book.LedgerBookId,
            periodId = snapshot.Period!.PeriodId,
            periodVersion = snapshot.Period.Version,
            periodStatus = snapshot.Period.Status,
            accountingBasis = book.AccountingBasis.ToString(),
            functionalCurrency = book.BaseCurrency,
            snapshotSha256 = dataset.SnapshotSha256,
            rowCount = dataset.Rows.Count,
            debitTotal = dataset.DebitTotal,
            creditTotal = dataset.CreditTotal,
            variance = dataset.Variance,
            isExactlyBalanced = decimal.Parse(dataset.Variance, CultureInfo.InvariantCulture) == 0m,
            legacyCurrencyLineCount = dataset.LegacyCurrencyLineCount,
            symbolPnlSchemaVersion = GeneralLedgerSymbolPnlBuilder.SchemaVersion,
            unrealizedAvailability = symbolPnl.Count > 0 && symbolPnl.All(row => row.UnrealizedAvailability == "Available") ? "Available" : "Unavailable",
            unrealizedBlockers = symbolPnl.Where(row => row.UnrealizedAvailability != "Available").Select(row => row.UnrealizedReason).Distinct().ToArray(),
            decimalEncoding = "Invariant decimal text in CSV/XLSX; exact DECIMAL in Parquet. XLSX cells are text to preserve every digit.",
            csvTextEncoding = "Formula-looking text is apostrophe-prefixed; XLSX and Parquet retain the original text.",
            certification = "Read-only journal export; does not certify close, valuation or tax completeness.",
            evidenceReferences = dataset.EvidenceReferences,
            artifacts = artifacts.Select(pair => new { name = pair.Key, sha256 = Sha256Digest.Compute(pair.Value), sizeBytes = pair.Value.Length })
        }, JsonOptions);
        artifacts.Add("manifest.json", manifest);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in artifacts)
            {
                ct.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                await using var stream = entry.Open();
                await stream.WriteAsync(content, ct).ConfigureAwait(false);
            }
        }
        return new(output.ToArray(), dataset.SnapshotSha256, dataset.Rows.Count);
    }

    private static string BuildCsv(GeneralLedgerExportDataset dataset)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', dataset.Columns)).Append("\r\n");
        foreach (var row in dataset.Rows)
            builder.Append(string.Join(',', row.Select(SpreadsheetFormulaGuard.EscapeCsvCell))).Append("\r\n");
        return builder.ToString();
    }

    private static async Task<byte[]> BuildParquetAsync(GeneralLedgerExportDataset dataset, CancellationToken ct)
    {
        var data = new List<(DataField Field, Array Values)>();
        for (var index = 0; index < dataset.Columns.Count; index++)
        {
            var values = dataset.Rows.Select(row => row[index]).ToArray();
            if (DecimalColumns.Contains(dataset.Columns[index]))
            {
                var amounts = values.Select(value => value.Length == 0 ? (decimal?)null : decimal.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                var scale = amounts.Where(value => value.HasValue).Select(value => (decimal.GetBits(value!.Value)[3] >> 16) & 0xff).DefaultIfEmpty(0).Max();
                // Fail rather than truncate a mixed-scale column that exceeds the Parquet decimal contract.
                var integerDigits = amounts.Where(value => value.HasValue).Select(value =>
                    decimal.Truncate(decimal.Abs(value!.Value)).ToString("0", CultureInfo.InvariantCulture).Length).DefaultIfEmpty(1).Max();
                if (integerDigits + scale > 38)
                    throw new InvalidOperationException("The exact decimal values exceed Parquet's supported export precision.");
                data.Add((new DecimalDataField(dataset.Columns[index], 38, scale, true, isNullable: true), amounts));
            }
            else
                data.Add((new DataField<string>(dataset.Columns[index]), values));
        }
        // Parquet attaches repetition/definition metadata while constructing the schema; a
        // DataColumn may only be materialized after its field has that schema ownership.
        var schema = new ParquetSchema(data.Select(column => column.Field));
        var columns = data.Select(column => new DataColumn(column.Field, column.Values)).ToArray();
        using var output = new MemoryStream();
        using (var writer = await ParquetWriter.CreateAsync(schema, output,
                   cancellationToken: ct).ConfigureAwait(false))
        {
            writer.CustomMetadata = new Dictionary<string, string> { ["schemaVersion"] = SchemaVersion, ["snapshotSha256"] = dataset.SnapshotSha256 };
            using var group = writer.CreateRowGroup();
            foreach (var column in columns)
                await group.WriteColumnAsync(column, ct).ConfigureAwait(false);
        }
        return output.ToArray();
    }

    private static string Amount(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string NullableAmount(decimal? value) => value.HasValue ? Amount(value.Value) : "";
}
