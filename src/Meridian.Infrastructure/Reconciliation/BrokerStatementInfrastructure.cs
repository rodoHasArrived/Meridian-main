using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Meridian.Domain.Reconciliation;
using Meridian.Storage.Archival;

namespace Meridian.Infrastructure.Reconciliation;

public interface ICanonicalStatementStore
{
    Task<bool> ImportExistsByChecksumAsync(string checksum, CancellationToken ct = default);
    Task<bool> ImportExistsByDuplicateKeyAsync(string duplicateKey, CancellationToken ct = default);
    Task SaveImportAsync(CanonicalStatementImport import, IReadOnlyList<CanonicalStatementRow> rows, CancellationToken ct = default);
    Task<bool> TrySaveImportAsync(CanonicalStatementImport import, IReadOnlyList<CanonicalStatementRow> rows, CancellationToken ct = default);
    Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken ct = default);
}

/// <summary>
/// Maps record identifiers onto safe file names for the per-record JSON stores. The identifiers
/// are system-generated today, but they flow through DTOs, so path separators, traversal
/// sequences, and characters invalid in file names are neutralized before touching the disk.
/// </summary>
internal static class ReconciliationRecordFileName
{
    // Longer sanitized names fall back to a hash so the result stays within
    // path-component limits on every supported file system.
    private const int MaxNameLength = 128;

    private static readonly char[] InvalidChars =
        [.. Path.GetInvalidFileNameChars(), '/', '\\'];

    public static string For(string? recordId)
    {
        if (string.IsNullOrEmpty(recordId))
        {
            return Hash(string.Empty);
        }

        var name = string.Join('_', recordId.Split(InvalidChars))
            .Replace("..", "_", StringComparison.Ordinal)
            .Trim('.', ' ');

        return string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength
            ? Hash(recordId)
            : name;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class JsonCanonicalStatementStore(string dataRoot) : ICanonicalStatementStore
{
    private readonly string _folder = Path.Combine(dataRoot, "reconciliation", "statement-imports");

    public Task<bool> ImportExistsByChecksumAsync(string checksum, CancellationToken ct = default)
        => Task.FromResult(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder, "*.json").Any(path => File.ReadAllText(path).Contains(checksum, StringComparison.Ordinal)));

    public Task<bool> ImportExistsByDuplicateKeyAsync(string duplicateKey, CancellationToken ct = default)
    {
        if (!Directory.Exists(_folder))
            return Task.FromResult(false);

        var exists = Directory.EnumerateFiles(_folder, "*.json")
            .Select(path => JsonSerializer.Deserialize<ImportEnvelope>(File.ReadAllText(path))?.import)
            .Any(import => string.Equals(import?.DuplicateKey, duplicateKey, StringComparison.Ordinal));

        return Task.FromResult(exists);
    }

    public async Task SaveImportAsync(CanonicalStatementImport import, IReadOnlyList<CanonicalStatementRow> rows, CancellationToken ct = default)
    {
        if (!await TrySaveImportAsync(import, rows, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Statement already imported (atomic duplicate-key claim already exists).");
        }
    }

    public async Task<bool> TrySaveImportAsync(
        CanonicalStatementImport import,
        IReadOnlyList<CanonicalStatementRow> rows,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { import, rows });
        var fileName = ReconciliationRecordFileName.For(import.ImportId);
        var targetPath = Path.Combine(_folder, $"{fileName}.json");
        Directory.CreateDirectory(_folder);
        var temporaryPath = Path.Combine(_folder, $".{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, payload, Encoding.UTF8, ct).ConfigureAwait(false);
            // Same-volume rename with overwrite disabled is the atomic uniqueness boundary across
            // concurrent processes. Exactly one importer can claim this duplicate key.
            File.Move(temporaryPath, targetPath, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(targetPath))
        {
            return false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_folder))
            return Task.FromResult<IReadOnlyList<CanonicalStatementImport>>([]);
        var imports = Directory.EnumerateFiles(_folder, "*.json")
            .Select(path => JsonSerializer.Deserialize<ImportEnvelope>(File.ReadAllText(path))?.import)
            .Where(x => x is not null)
            .Cast<CanonicalStatementImport>()
            .OrderByDescending(x => x.ImportedAtUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<CanonicalStatementImport>>(imports);
    }

    private sealed record ImportEnvelope(CanonicalStatementImport import);
}

public sealed class CsvBrokerStatementService(ICanonicalStatementStore store) : IBrokerStatementService
{
    private const long MaximumStatementBytes = 32L * 1024 * 1024;
    private const int MaximumRows = 100_000;
    private const int MaximumLineCharacters = 64 * 1024;
    private static readonly string[] ExpectedColumns =
        ["account", "symbol", "quantity", "price", "cashAmount", "activityType", "tradeDate"];
    private static readonly string[] OptionalCanonicalColumns =
        ["settlementDate", "currency", "feesCommission", "externalTransactionId"];

    public async Task<BrokerStatementValidationResult> ValidateAsync(BrokerStatementImportRequest request, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!File.Exists(request.SourcePath))
            errors.Add("Source file not found.");
        if (!IsSupportedStatementSource(request.Broker))
            errors.Add("Unsupported broker or custodian statement source.");
        if (errors.Count > 0)
            return new BrokerStatementValidationResult(false, errors, 0);

        try
        {
            var rows = await ParseFileAsync(request.SourcePath, request, importId: "validation", ct).ConfigureAwait(false);
            return new BrokerStatementValidationResult(true, errors, rows.Count);
        }
        catch (InvalidDataException ex)
        {
            errors.Add(ex.Message);
            return new BrokerStatementValidationResult(false, errors, 0);
        }
    }

    public async Task<BrokerStatementImportResult> ImportAsync(BrokerStatementImportRequest request, CancellationToken ct = default)
    {
        EnsureBoundedFile(request.SourcePath);
        var sourceFileHash = string.IsNullOrWhiteSpace(request.SourceFileHash)
            ? await HashFileAsync(request.SourcePath, ct).ConfigureAwait(false)
            : request.SourceFileHash.Trim().ToUpperInvariant();
        var duplicateKey = StatementDuplicateKey.Create(
            request.FundAccountId,
            request.StatementPeriodStart,
            request.StatementPeriodEnd,
            sourceFileHash);

        if (await store.ImportExistsByDuplicateKeyAsync(duplicateKey, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Statement already imported (fund account, statement period, and source file hash match).");

        var importId = duplicateKey;
        var normalizedRequest = request.WithSourceFileHash(sourceFileHash);
        var rows = await ParseFileAsync(request.SourcePath, normalizedRequest, importId, ct).ConfigureAwait(false);
        var import = new CanonicalStatementImport(
            importId,
            normalizedRequest.Broker,
            normalizedRequest.StatementPeriodEnd,
            DateTimeOffset.UtcNow,
            normalizedRequest.SourcePath,
            sourceFileHash,
            rows.Count,
            rows.Count)
        {
            SourceInstitution = normalizedRequest.SourceInstitution,
            FundAccountId = normalizedRequest.FundAccountId,
            ExternalAccountId = normalizedRequest.ExternalAccountId,
            StatementPeriodStart = normalizedRequest.StatementPeriodStart,
            StatementPeriodEnd = normalizedRequest.StatementPeriodEnd,
            OriginalFileName = normalizedRequest.OriginalFileName,
            MappingProfileId = normalizedRequest.MappingProfileId,
            ToleranceProfileId = normalizedRequest.ToleranceProfileId,
            ImportedBy = normalizedRequest.ImportedBy,
            SourceFileHash = sourceFileHash,
            DuplicateKey = duplicateKey
        };
        if (!await store.TrySaveImportAsync(import, rows, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Statement already imported (fund account, statement period, and source file hash match).");
        }

        return new BrokerStatementImportResult(import, rows);
    }

    private static async Task<IReadOnlyList<CanonicalStatementRow>> ParseFileAsync(
        string path,
        BrokerStatementImportRequest request,
        string importId,
        CancellationToken ct)
    {
        EnsureBoundedFile(path);
        var rows = new List<CanonicalStatementRow>();
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var headerLine = await reader.ReadLineAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Statement CSV is empty.");
        var header = ParseCsvLine(headerLine, rowNumber: 1);
        var optionalColumnCount = header.Count - ExpectedColumns.Length;
        if (optionalColumnCount < 0 ||
            optionalColumnCount > OptionalCanonicalColumns.Length ||
            !header.Take(ExpectedColumns.Length).SequenceEqual(ExpectedColumns, StringComparer.OrdinalIgnoreCase) ||
            !header.Skip(ExpectedColumns.Length).SequenceEqual(
                OptionalCanonicalColumns.Take(optionalColumnCount),
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Invalid statement CSV header. Expected the canonical prefix {string.Join(',', ExpectedColumns)} " +
                $"followed only by the optional columns {string.Join(',', OptionalCanonicalColumns)} in order.");
        }

        var sourceRowNumber = 1;
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            sourceRowNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (rows.Count >= MaximumRows)
            {
                throw new InvalidDataException($"Statement CSV exceeds the {MaximumRows}-row limit.");
            }

            var fields = ParseCsvLine(line, sourceRowNumber);
            if (fields.Count != header.Count)
            {
                throw new InvalidDataException(
                    $"Statement CSV row {sourceRowNumber} has {fields.Count} columns; expected {header.Count} from the validated header.");
            }

            if (!decimal.TryParse(fields[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) ||
                !decimal.TryParse(fields[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var price) ||
                !decimal.TryParse(fields[4], NumberStyles.Number, CultureInfo.InvariantCulture, out var cashAmount) ||
                !DateOnly.TryParse(fields[6], CultureInfo.InvariantCulture, DateTimeStyles.None, out var tradeDate))
            {
                throw new InvalidDataException($"Statement CSV row {sourceRowNumber} contains an invalid numeric or date value.");
            }

            rows.Add(new CanonicalStatementRow(
                importId,
                sourceRowNumber,
                fields[0],
                fields[1],
                quantity,
                price,
                cashAmount,
                fields[5],
                tradeDate,
                Hash(line)));
        }

        return rows;
    }

    private static IReadOnlyList<string> ParseCsvLine(string line, int rowNumber)
    {
        if (line.Length > MaximumLineCharacters || line.Contains("\0", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Statement CSV row {rowNumber} exceeds the line limit or contains a null byte.");
        }

        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (current == ',' && !quoted)
            {
                fields.Add(field.ToString().Trim());
                field.Clear();
            }
            else
            {
                field.Append(current);
            }
        }

        if (quoted)
        {
            throw new InvalidDataException($"Statement CSV row {rowNumber} contains an unterminated quoted field.");
        }

        fields.Add(field.ToString().Trim());
        return fields;
    }

    private static void EnsureBoundedFile(string path)
    {
        var length = new FileInfo(path).Length;
        if (length > MaximumStatementBytes)
        {
            throw new InvalidDataException($"Statement file exceeds the {MaximumStatementBytes}-byte limit.");
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string Hash(string input)
        => HashBytes(Encoding.UTF8.GetBytes(input));

    private static string HashBytes(byte[] input) => Convert.ToHexString(SHA256.HashData(input));

    private static bool IsSupportedStatementSource(string source) =>
        string.Equals(source, "samplebroker", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, "broker", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, "custodian", StringComparison.OrdinalIgnoreCase);
}

public sealed record StatementMatchingTolerance(decimal PositionQuantityTolerance, decimal CashAmountTolerance, decimal TransactionAmountTolerance)
{
    public static StatementMatchingTolerance Default => new(0.0001m, 0.01m, 0.01m);

    public string ToleranceProfileId { get; init; } = "statement-default";
    public int ToleranceProfileVersion { get; init; } = 1;
    public string PositionToleranceRuleId { get; init; } = "position-default-v1";
    public string CashToleranceRuleId { get; init; } = "cash-default-absolute-v1";
    public string TransactionToleranceRuleId { get; init; } = "transaction-default-v1";
    public decimal? BasisPointCashTolerance { get; init; }
    public decimal MarketValueTolerance { get; init; }
    public TimeSpan SettlementDateTolerance { get; init; }
    public decimal PriceTolerance { get; init; }
}

public interface IReconciliationBreakStore
{
    Task WriteAsync(IReadOnlyList<ReconciliationBreakRecord> records, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenAsync(CancellationToken ct = default);
}

public sealed class JsonReconciliationBreakStore(string dataRoot) : IReconciliationBreakStore
{
    private readonly string _folder = Path.Combine(dataRoot, "reconciliation", "statement-breaks");

    public async Task WriteAsync(IReadOnlyList<ReconciliationBreakRecord> records, CancellationToken ct = default)
    {
        foreach (var record in records)
        {
            var path = Path.Combine(_folder, $"{ReconciliationRecordFileName.For(record.BreakId)}.json");
            await AtomicFileWriter.WriteAsync(path, JsonSerializer.Serialize(record), ct).ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_folder))
            return Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>([]);
        var items = Directory.EnumerateFiles(_folder, "*.json")
            .Select(path => JsonSerializer.Deserialize<ReconciliationBreakRecord>(File.ReadAllText(path)))
            .Where(x => x is not null && x.Status == "Open")
            .Cast<ReconciliationBreakRecord>()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>(items);
    }
}

public sealed class StatementMatchingService
{
    public IReadOnlyList<MatchOutcome> MatchRows(IReadOnlyList<CanonicalStatementRow> rows, StatementMatchingTolerance? tolerance = null)
    {
        var t = tolerance ?? StatementMatchingTolerance.Default;
        return rows.Select(r =>
        {
            var (ok, code, rationale, conf) = r.ActivityType.ToLowerInvariant() switch
            {
                "position" => MatchPosition(r, t),
                "cash" => MatchCash(r, t),
                _ => MatchTransaction(r, t)
            };

            var ruleId = ResolveRuleId(r, t);
            var explanation = ok && !string.IsNullOrWhiteSpace(ruleId)
                ? $"{rationale} Tolerance rule {ruleId} allowed this match."
                : rationale;

            return new MatchOutcome(r.RawChecksum, ok ? "matched" : code, ok ? $"SRC-{r.SourceRowNumber}" : string.Empty, conf, explanation)
            {
                ToleranceProfileId = t.ToleranceProfileId,
                ToleranceProfileVersion = t.ToleranceProfileVersion,
                ToleranceRuleId = ok ? ruleId : null
            };
        }).ToList();
    }

    public IReadOnlyList<ReconciliationBreakRecord> BuildBreakRecords(string runId, string importId, IReadOnlyList<CanonicalStatementRow> rows, IReadOnlyList<MatchOutcome> outcomes, StatementMatchingTolerance? tolerance = null)
    {
        var t = tolerance ?? StatementMatchingTolerance.Default;
        return rows.Zip(outcomes, (row, outcome) => (row, outcome))
            .Where(x => x.outcome.OutcomeType != "matched")
            .Select(x => new ReconciliationBreakRecord(
                BreakId: Guid.NewGuid().ToString("N"),
                RunId: runId,
                ImportId: importId,
                SourceReference: $"{importId}:{x.row.SourceRowNumber}",
                BreakCode: x.outcome.OutcomeType,
                Category: x.row.ActivityType,
                Delta: Math.Abs(x.row.Quantity) + Math.Abs(x.row.CashAmount),
                Tolerance: ResolveToleranceAmount(x.row, t),
                ToleranceBreached: true,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                Status: "Open")).ToList();
    }

    private static string ResolveRuleId(CanonicalStatementRow row, StatementMatchingTolerance tolerance)
    {
        if (row.ActivityType.Equals("position", StringComparison.OrdinalIgnoreCase))
        {
            return tolerance.PositionToleranceRuleId;
        }

        return row.ActivityType.Equals("cash", StringComparison.OrdinalIgnoreCase)
            ? tolerance.CashToleranceRuleId
            : tolerance.TransactionToleranceRuleId;
    }

    private static decimal ResolveToleranceAmount(CanonicalStatementRow row, StatementMatchingTolerance tolerance)
    {
        if (row.ActivityType.Equals("cash", StringComparison.OrdinalIgnoreCase))
        {
            var basisPointTolerance = tolerance.BasisPointCashTolerance is { } basisPoints
                ? Math.Abs(row.CashAmount) * Math.Abs(basisPoints) / 10_000m
                : 0m;
            return Math.Max(tolerance.CashAmountTolerance, basisPointTolerance);
        }

        if (row.ActivityType.Equals("position", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(tolerance.PositionQuantityTolerance, Math.Max(tolerance.MarketValueTolerance, tolerance.PriceTolerance));
        }

        return Math.Max(tolerance.TransactionAmountTolerance, tolerance.PriceTolerance);
    }

    private static (bool, string, string, decimal) MatchPosition(CanonicalStatementRow row, StatementMatchingTolerance tolerance)
    {
        if (string.IsNullOrWhiteSpace(row.Symbol))
            return (false, "POS_SYMBOL_MISSING", "Position row missing symbol.", 0.1m);
        if (Math.Abs(row.Quantity) <= tolerance.PositionQuantityTolerance)
            return (false, "POS_QTY_TOLERANCE_BREACH", "Position quantity within tolerance floor and treated as unresolved.", 0.3m);
        return (true, "", $"Position matched within configured tolerance rule {tolerance.PositionToleranceRuleId}.", 0.95m);
    }

    private static (bool, string, string, decimal) MatchCash(CanonicalStatementRow row, StatementMatchingTolerance tolerance)
    {
        var basisPointTolerance = tolerance.BasisPointCashTolerance is { } basisPoints
            ? Math.Abs(row.CashAmount) * Math.Abs(basisPoints) / 10_000m
            : 0m;
        var allowedTolerance = Math.Max(tolerance.CashAmountTolerance, basisPointTolerance);
        if (Math.Abs(row.CashAmount) <= allowedTolerance)
            return (true, "", $"Cash movement within tolerance rule {tolerance.CashToleranceRuleId}.", 0.9m);
        return (false, "CASH_TOLERANCE_BREACH", "Cash movement exceeds configured tolerance.", 0.25m);
    }

    private static (bool, string, string, decimal) MatchTransaction(CanonicalStatementRow row, StatementMatchingTolerance tolerance)
    {
        if (Math.Abs(row.Price * row.Quantity) <= tolerance.TransactionAmountTolerance)
            return (true, "", $"Transaction amount within tolerance rule {tolerance.TransactionToleranceRuleId}.", 0.85m);
        return (false, "TXN_TOLERANCE_BREACH", "Transaction amount exceeds configured tolerance.", 0.25m);
    }
}
