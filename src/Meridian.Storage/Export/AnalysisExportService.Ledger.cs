using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Meridian.Ledger;

namespace Meridian.Storage.Export;

public sealed partial class AnalysisExportService
{
    /// <summary>Exports a journal, as-of trial balance, cash-flow statement, and supplied lot
    /// evidence as CSV, XLSX, Parquet or Arrow. A unique result directory prevents replacement
    /// of another export. A failed or cancelled export removes its incomplete artifacts.</summary>
    public async Task<ExportResult> ExportLedgerAsync(LedgerExportRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Ledger);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);
        if (request.AsOf < request.PeriodStart)
            throw new ArgumentException("As-of timestamp cannot precede the period start.", nameof(request));
        if (request.Format is not (ExportFormat.Csv or ExportFormat.Xlsx or ExportFormat.Parquet or ExportFormat.Arrow))
            throw new NotSupportedException("Accounting exports support CSV, XLSX, Parquet and Arrow.");
        ct.ThrowIfCancellationRequested();
        request = request with
        {
            FinancialAccountId = string.IsNullOrWhiteSpace(request.FinancialAccountId)
            ? null : request.FinancialAccountId.Trim()
        };

        // Freeze/revalidate the source once so every table describes the same journal generation.
        var source = new Meridian.Ledger.Ledger();
        foreach (var entry in request.Ledger.Journal.Where(entry => entry.Timestamp <= request.AsOf).ToArray())
            source.Post(entry);
        var tables = BuildLedgerExportTables(request with { Ledger = source });
        var output = Path.Combine(request.OutputDirectory, $"ledger-{Guid.NewGuid():N}");
        var result = ExportResult.CreateSuccess("ledger", output);
        result.Warnings = new[]
        {
            request.OpenLots is null ? "Open-lot P&L unavailable: no matching retained lot/valuation evidence was supplied." : null,
            request.RealizedLots is null ? "Realized-gains schedule unavailable: no retained disposal evidence was supplied." : null,
            source.Journal.Any(entry => entry.Metadata.ActivityType?.EndsWith("_cash_in_lieu", StringComparison.Ordinal) == true) ?
                "Corporate-action cash-in-lieu realizations are included in corporate-action-realizations; acquisition parcel dates are unavailable in the journal." : null
        }.Where(message => message is not null).Select(message => message!).ToArray();
        try
        {
            Directory.CreateDirectory(output);
            var files = new List<ExportedFile>();
            foreach (var (name, rows) in tables)
            {
                ct.ThrowIfCancellationRequested();
                var columns = LedgerExportColumns(name);
                var extension = request.Format.ToString().ToLowerInvariant();
                var path = Path.Combine(output, $"{name}.{extension}");
                switch (request.Format)
                {
                    case ExportFormat.Csv:
                        await WriteLedgerCsvAsync(path, columns, rows, ct);
                        break;
                    case ExportFormat.Xlsx:
                        if (rows.Count >= 1_048_576)
                            throw new InvalidOperationException("Accounting worksheet exceeds Excel's row limit; use a columnar format.");
                        await WriteLedgerWorkbookAsync(path, columns, rows, ct);
                        break;
                    case ExportFormat.Parquet:
                        if (rows.Count == 0)
                            await WriteEmptyLedgerParquetAsync(path, columns, ct);
                        else
                            await WriteParquetFileAsync(path, rows, ct);
                        break;
                    case ExportFormat.Arrow:
                        if (rows.Count == 0)
                            await WriteEmptyLedgerArrowAsync(path, columns, ct);
                        else
                            await WriteArrowFileAsync(path, rows, new TimestampSettings(), ct);
                        break;
                }
                files.Add(new ExportedFile
                {
                    Path = path,
                    RelativePath = Path.GetFileName(path),
                    Format = extension,
                    RecordCount = rows.Count,
                    SizeBytes = new FileInfo(path).Length,
                    ChecksumSha256 = await ComputeChecksumAsync(path, ct)
                });
            }
            await File.WriteAllTextAsync(Path.Combine(output, "README.txt"),
                "Meridian accounting export\nAmounts and quantities are invariant decimal text, preserving booked precision. " +
                "Parse using a decimal type for calculations. Journal and timestamped disposal range: (PeriodStart, AsOf]; " +
                "date-only disposals include both boundary calendar dates because their intraday timing is unknown. " +
                "trial balance and open lots: AsOf. Open/realized lot files appear only when evidence is supplied. " +
                "Realized gains are a supporting schedule, not a filed tax form.\n" +
                $"PeriodStart: {request.PeriodStart:O}\nAsOf: {request.AsOf:O}\n", ct);
            ct.ThrowIfCancellationRequested();
            result.Files = files.ToArray();
            result.FilesGenerated = files.Count;
            result.TotalRecords = files.Sum(file => file.RecordCount);
            result.TotalBytes = files.Sum(file => file.SizeBytes);
            result.CompletedAt = DateTime.UtcNow;
            return result;
        }
        catch
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
            throw;
        }
    }

    private static Dictionary<string, List<Dictionary<string, object?>>> BuildLedgerExportTables(LedgerExportRequest request)
    {
        bool InScope(string? account) => request.FinancialAccountId is null ||
            string.Equals(account, request.FinancialAccountId, StringComparison.OrdinalIgnoreCase);
        var journal = request.Ledger.GetJournalEntries(request.PeriodStart, request.AsOf)
            .Where(entry => entry.Timestamp > request.PeriodStart)
            .SelectMany(entry => entry.Lines.Where(line => InScope(line.Account.FinancialAccountId))
                .Select(line => Row(("JournalId", entry.JournalEntryId), ("LineId", line.EntryId),
                    ("Timestamp", entry.Timestamp), ("Description", entry.Description),
                    ("FinancialAccountId", line.Account.FinancialAccountId), ("Account", line.Account.Name),
                    ("AccountType", line.Account.AccountType), ("Symbol", line.Account.Symbol ?? entry.Metadata.Symbol),
                    ("Debit", line.Debit), ("Credit", line.Credit), ("ActivityType", entry.Metadata.ActivityType)))).ToList();
        var trialBalance = request.Ledger.TrialBalanceAsOf(request.AsOf, request.FinancialAccountId)
            .OrderBy(pair => pair.Key.Name, StringComparer.Ordinal).ThenBy(pair => pair.Key.FinancialAccountId, StringComparer.Ordinal)
            .Select(pair => Row(("AsOf", request.AsOf), ("Account", pair.Key.Name),
                ("AccountType", pair.Key.AccountType), ("FinancialAccountId", pair.Key.FinancialAccountId),
                ("Symbol", pair.Key.Symbol), ("NormalBalance", pair.Value))).ToList();
        var cashFlow = LedgerFinancialStatementBuilder.BuildForPeriod(request.Ledger, request.PeriodStart,
            request.AsOf, financialAccountId: request.FinancialAccountId).CashFlow!;
        if (!cashFlow.IsReconciled)
            throw new InvalidOperationException("Cash-flow statement does not reconcile.");
        var cashRows = cashFlow.Lines.Select(line => Row(("Category", line.Category),
            ("Description", line.Description), ("Amount", line.Amount))).ToList();
        cashRows.AddRange(new[] { ("Beginning cash", cashFlow.BeginningCash), ("Operating cash flow", cashFlow.OperatingCashFlow),
            ("Investing cash flow", cashFlow.InvestingCashFlow), ("Financing cash flow", cashFlow.FinancingCashFlow),
            ("Ending cash", cashFlow.EndingCash), ("Reconciliation variance", cashFlow.ReconciliationVariance) }
            .Select(total => Row(("Category", "Total"), ("Description", total.Item1), ("Amount", total.Item2))));
        var tables = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal)
        {
            ["journal"] = journal,
            ["trial-balance"] = trialBalance,
            ["cash-flow"] = cashRows,
            ["availability"] =
            [
                Row(("Report", "open-lot-pnl"), ("Status", request.OpenLots is null ? "Unavailable" : "Available"),
                    ("Reason", request.OpenLots is null ? "No matching retained lot/valuation evidence" : "Retained lot evidence supplied")),
                Row(("Report", "realized-gains"), ("Status", request.RealizedLots is null ? "Unavailable" : "Available"),
                    ("Reason", request.RealizedLots is null ? "No retained disposal evidence" : "Retained disposal evidence supplied"))
            ]
        };
        tables["corporate-action-realizations"] = request.Ledger.GetJournalEntries(request.PeriodStart, request.AsOf)
            .Where(entry => entry.Timestamp > request.PeriodStart &&
                (entry.Metadata.ActivityType?.EndsWith("_cash_in_lieu", StringComparison.Ordinal) == true ||
                 entry.Metadata.ActivityType?.Contains("returnofcapital", StringComparison.OrdinalIgnoreCase) == true))
            .Where(entry => entry.Lines.Any(line => InScope(line.Account.FinancialAccountId)))
            .Select(entry =>
            {
                var lines = entry.Lines.Where(line => InScope(line.Account.FinancialAccountId)).ToArray();
                var cash = lines.Where(line => line.Account.Name == "Cash").Sum(line => line.Debit - line.Credit);
                var realized = lines.Where(line => line.Account.Name is "Realized Gain" or "Realized Loss")
                    .Sum(line => line.Credit - line.Debit);
                var basis = lines.Where(line => line.Account.Name.StartsWith("Securities", StringComparison.Ordinal) ||
                    line.Account.Name.StartsWith("Short Securities Payable", StringComparison.Ordinal))
                    .Sum(line => line.Account.AccountType == LedgerAccountType.Liability ? line.Debit - line.Credit : line.Credit - line.Debit);
                return Row(("JournalId", entry.JournalEntryId), ("Timestamp", entry.Timestamp),
                    ("FinancialAccountId", entry.Metadata.FinancialAccountId), ("Symbol", entry.Metadata.Symbol),
                    ("ActivityType", entry.Metadata.ActivityType), ("CashImpact", cash), ("BasisRelieved", basis),
                    ("RealizedGainOrLoss", realized), ("ParcelEvidence", "Journal aggregate; acquisition parcel dates unavailable"));
            }).ToList();
        if (request.OpenLots is not null)
        {
            var lots = request.OpenLots.Where(lot => InScope(lot.FinancialAccountId)).ToArray();
            if (lots.GroupBy(lot => (lot.FinancialAccountId.ToUpperInvariant(), lot.LotId)).Any(group => group.Count() > 1))
                throw new ArgumentException("Open-lot evidence contains duplicate identities.");
            if (lots.Any(lot => lot.AsOf != request.AsOf || lot.Quantity <= 0m || lot.CostBasis < 0m ||
                lot.AcquiredDate > DateOnly.FromDateTime(request.AsOf.UtcDateTime)))
                throw new ArgumentException("Open-lot evidence must have positive quantity, nonnegative basis and match the as-of date.");
            tables["open-lot-pnl"] = lots.Select(lot => Row(("LotId", lot.LotId), ("Symbol", lot.Symbol),
                ("FinancialAccountId", lot.FinancialAccountId), ("AcquiredDate", lot.AcquiredDate),
                ("Quantity", lot.Quantity), ("CostBasis", lot.CostBasis), ("MarketValue", lot.MarketValue),
                ("UnrealizedGainOrLoss", lot.IsShort ? lot.CostBasis - lot.MarketValue : lot.MarketValue - lot.CostBasis),
                ("AsOf", lot.AsOf), ("IsShort", lot.IsShort))).ToList();
        }
        if (request.RealizedLots is not null)
        {
            var lots = request.RealizedLots.Where(lot => InScope(lot.FinancialAccountId) &&
                (lot.SoldAt is { } soldAt ? soldAt > request.PeriodStart && soldAt <= request.AsOf :
                    lot.SoldDate >= DateOnly.FromDateTime(request.PeriodStart.UtcDateTime) &&
                    lot.SoldDate <= DateOnly.FromDateTime(request.AsOf.UtcDateTime))).ToArray();
            if (lots.Any(lot => lot.Quantity <= 0m || lot.CostBasis < 0m || lot.SoldDate < lot.AcquiredDate ||
                (lot.Adjustment != 0m && string.IsNullOrWhiteSpace(lot.AdjustmentCode))))
                throw new ArgumentException("Disposal evidence contains invalid quantity, basis, dates or an unexplained adjustment.");
            if (lots.Where(lot => lot.DisposalId is not null).GroupBy(lot =>
                    (lot.FinancialAccountId.ToUpperInvariant(), lot.DisposalId)).Any(group => group.Count() > 1))
                throw new ArgumentException("Disposal evidence contains duplicate source disposal identities.");
            tables["realized-gains"] = lots.Select(lot => Row(("LotId", lot.LotId), ("Symbol", lot.Symbol),
                ("FinancialAccountId", lot.FinancialAccountId), ("AcquiredDate", lot.AcquiredDate),
                ("SoldDate", lot.SoldDate), ("Quantity", lot.Quantity), ("Proceeds", lot.Proceeds),
                ("CostBasis", lot.CostBasis), ("EconomicGainOrLoss", lot.Proceeds - lot.CostBasis),
                ("Adjustment", lot.Adjustment), ("AdjustmentCode", lot.AdjustmentCode),
                ("RecognizedGainOrLoss", lot.Proceeds - lot.CostBasis + lot.Adjustment), ("TaxCharacter", lot.TaxCharacter),
                ("SoldAt", lot.SoldAt), ("IsShort", lot.IsShort), ("DisposalId", lot.DisposalId))).ToList();
        }
        return tables;
    }

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells) =>
        cells.ToDictionary(cell => cell.Key, cell => (object?)(cell.Value switch
        {
            null => "",
            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            // Fixed notation also keeps tiny negative amounts recognizable as numbers by
            // the spreadsheet formula guard and by ordinary decimal importers.
            decimal amount => amount.ToString("0.############################", CultureInfo.InvariantCulture),
            _ => Convert.ToString(cell.Value, CultureInfo.InvariantCulture) ?? ""
        }), StringComparer.Ordinal);

    private static string[] LedgerExportColumns(string name) => name switch
    {
        "journal" => ["JournalId", "LineId", "Timestamp", "Description", "FinancialAccountId", "Account", "AccountType", "Symbol", "Debit", "Credit", "ActivityType"],
        "availability" => ["Report", "Status", "Reason"],
        "corporate-action-realizations" => ["JournalId", "Timestamp", "FinancialAccountId", "Symbol", "ActivityType", "CashImpact", "BasisRelieved", "RealizedGainOrLoss", "ParcelEvidence"],
        "trial-balance" => ["AsOf", "Account", "AccountType", "FinancialAccountId", "Symbol", "NormalBalance"],
        "cash-flow" => ["Category", "Description", "Amount"],
        "open-lot-pnl" => ["LotId", "Symbol", "FinancialAccountId", "AcquiredDate", "Quantity", "CostBasis", "MarketValue", "UnrealizedGainOrLoss", "AsOf", "IsShort"],
        "realized-gains" => ["LotId", "Symbol", "FinancialAccountId", "AcquiredDate", "SoldDate", "Quantity", "Proceeds", "CostBasis", "EconomicGainOrLoss", "Adjustment", "AdjustmentCode", "RecognizedGainOrLoss", "TaxCharacter", "SoldAt", "IsShort", "DisposalId"],
        _ => throw new ArgumentException("Unknown accounting export table.", nameof(name))
    };

    private static async Task WriteEmptyLedgerParquetAsync(string path, string[] columns, CancellationToken ct)
    {
        var fields = columns.Select(column => new Parquet.Schema.DataField<string>(column)).ToArray();
        using var stream = File.Create(path);
        using var writer = await Parquet.ParquetWriter.CreateAsync(new Parquet.Schema.ParquetSchema(fields), stream, cancellationToken: ct);
        using var group = writer.CreateRowGroup();
        foreach (var field in fields)
            await group.WriteColumnAsync(new Parquet.Data.DataColumn(field, Array.Empty<string>()), ct);
    }

    private static async Task WriteEmptyLedgerArrowAsync(string path, string[] columns, CancellationToken ct)
    {
        var schema = new Apache.Arrow.Schema(columns.Select(column => new Apache.Arrow.Field(column,
            Apache.Arrow.Types.StringType.Default, nullable: true)), null);
        var arrays = columns.Select(_ => (Apache.Arrow.IArrowArray)new Apache.Arrow.StringArray.Builder().Build()).ToArray();
        using var batch = new Apache.Arrow.RecordBatch(schema, arrays, 0);
        await using var stream = File.Create(path);
        using var writer = new Apache.Arrow.Ipc.ArrowFileWriter(stream, schema);
        await writer.WriteRecordBatchAsync(batch, ct);
        await writer.WriteEndAsync(ct);
    }

    private static async Task WriteLedgerCsvAsync(string path, string[] columns, List<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        static string Quote(object? value) => EscapeCsvValue(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        await writer.WriteLineAsync(string.Join(",", columns.Select(Quote)).AsMemory(), ct);
        foreach (var row in rows)
            await writer.WriteLineAsync(string.Join(",", row.Values.Select(Quote)).AsMemory(), ct);
    }

    private static async Task WriteLedgerWorkbookAsync(string path, string[] columns, List<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var data = new XElement(ns + "sheetData");
        IEnumerable<IEnumerable<object?>> values = new[] { columns.Cast<object?>() }
            .Concat(rows.Select(row => row.Values.AsEnumerable()));
        foreach (var row in values)
        {
            ct.ThrowIfCancellationRequested();
            data.Add(new XElement(ns + "row", row.Select(value => new XElement(ns + "c",
                new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? ""))))));
        }
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var parts = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = GetContentTypesXml(),
            ["_rels/.rels"] = GetRelsXml(),
            ["xl/workbook.xml"] = GetWorkbookXml(),
            ["xl/_rels/workbook.xml.rels"] = GetWorkbookRelsXml(),
            ["xl/styles.xml"] = GetStylesXml(),
            ["xl/sharedStrings.xml"] = GetSharedStringsXml([]),
            ["xl/worksheets/sheet1.xml"] = new XElement(ns + "worksheet", data).ToString(SaveOptions.DisableFormatting)
        };
        foreach (var (name, content) in parts)
        {
            await using var stream = archive.CreateEntry(name).Open();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(content.AsMemory(), ct);
        }
    }
}
