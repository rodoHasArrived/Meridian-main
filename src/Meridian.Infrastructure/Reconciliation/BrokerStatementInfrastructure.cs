using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Domain.Reconciliation;

namespace Meridian.Infrastructure.Reconciliation;

public interface ICanonicalStatementStore
{
    Task<bool> ImportExistsByChecksumAsync(string checksum, CancellationToken ct = default);
    Task<bool> ImportExistsByDuplicateKeyAsync(string duplicateKey, CancellationToken ct = default);
    Task SaveImportAsync(CanonicalStatementImport import, IReadOnlyList<CanonicalStatementRow> rows, CancellationToken ct = default);
    Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken ct = default);
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
        Directory.CreateDirectory(_folder);
        var payload = JsonSerializer.Serialize(new { import, rows });
        await File.WriteAllTextAsync(Path.Combine(_folder, $"{import.ImportId}.json"), payload, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_folder)) return Task.FromResult<IReadOnlyList<CanonicalStatementImport>>([]);
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
    public async Task<BrokerStatementValidationResult> ValidateAsync(BrokerStatementImportRequest request, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!File.Exists(request.SourcePath)) errors.Add("Source file not found.");
        if (!string.Equals(request.Broker, "samplebroker", StringComparison.OrdinalIgnoreCase)) errors.Add("Unsupported broker.");
        if (errors.Count > 0) return new BrokerStatementValidationResult(false, errors, 0);

        var lines = await File.ReadAllLinesAsync(request.SourcePath, ct).ConfigureAwait(false);
        if (lines.Length == 0 || !lines[0].Contains("account,symbol,quantity,price,cashAmount,activityType,tradeDate", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Invalid header for samplebroker schema.");
        }

        return new BrokerStatementValidationResult(errors.Count == 0, errors, Math.Max(0, lines.Length - 1));
    }

    public async Task<BrokerStatementImportResult> ImportAsync(BrokerStatementImportRequest request, CancellationToken ct = default)
    {
        var fileBytes = await File.ReadAllBytesAsync(request.SourcePath, ct).ConfigureAwait(false);
        var content = Encoding.UTF8.GetString(fileBytes);
        var sourceFileHash = string.IsNullOrWhiteSpace(request.SourceFileHash) ? HashBytes(fileBytes) : request.SourceFileHash.Trim().ToUpperInvariant();
        var duplicateKey = StatementDuplicateKey.Create(
            request.FundAccountId,
            request.StatementPeriodStart,
            request.StatementPeriodEnd,
            sourceFileHash);

        if (await store.ImportExistsByDuplicateKeyAsync(duplicateKey, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Statement already imported (fund account, statement period, and source file hash match).");

        var importId = duplicateKey;
        var normalizedRequest = request.WithSourceFileHash(sourceFileHash);
        var rows = ParseRows(content, normalizedRequest, importId).ToList();
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
        await store.SaveImportAsync(import, rows, ct).ConfigureAwait(false);
        return new BrokerStatementImportResult(import, rows);
    }

    private static IEnumerable<CanonicalStatementRow> ParseRows(string content, BrokerStatementImportRequest request, string importId)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1).ToArray();
        for (var i = 0; i < lines.Length; i++)
        {
            var p = lines[i].Split(',');
            yield return new CanonicalStatementRow(
                importId,
                i + 2,
                p[0], p[1], decimal.Parse(p[2]), decimal.Parse(p[3]), decimal.Parse(p[4]), p[5], DateOnly.Parse(p[6]), Hash(lines[i]));
        }
    }

    private static string Hash(string input)
        => HashBytes(Encoding.UTF8.GetBytes(input));

    private static string HashBytes(byte[] input)
    {
        var bytes = SHA256.HashData(input);
        return Convert.ToHexString(bytes);
    }
}

public sealed record StatementMatchingTolerance(decimal PositionQuantityTolerance, decimal CashAmountTolerance, decimal TransactionAmountTolerance)
{
    public static StatementMatchingTolerance Default => new(0.0001m, 0.01m, 0.01m);
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
        Directory.CreateDirectory(_folder);
        foreach (var record in records)
        {
            var path = Path.Combine(_folder, $"{record.BreakId}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record), ct).ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_folder)) return Task.FromResult<IReadOnlyList<ReconciliationBreakRecord>>([]);
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

            return new MatchOutcome(r.RawChecksum, ok ? "matched" : code, ok ? $"SRC-{r.SourceRowNumber}" : string.Empty, conf, rationale);
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
                Tolerance: x.row.ActivityType.Equals("cash", StringComparison.OrdinalIgnoreCase) ? t.CashAmountTolerance : t.TransactionAmountTolerance,
                ToleranceBreached: true,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                Status: "Open")).ToList();
    }

    private static (bool,string,string,decimal) MatchPosition(CanonicalStatementRow row, StatementMatchingTolerance tolerance)
    {
        if (string.IsNullOrWhiteSpace(row.Symbol)) return (false, "POS_SYMBOL_MISSING", "Position row missing symbol.", 0.1m);
        if (Math.Abs(row.Quantity) <= tolerance.PositionQuantityTolerance) return (false, "POS_QTY_TOLERANCE_BREACH", "Position quantity within tolerance floor and treated as unresolved.", 0.3m);
        return (true, "", "Position matched within configured tolerance.", 0.95m);
    }

    private static (bool,string,string,decimal) MatchCash(CanonicalStatementRow row, StatementMatchingTolerance tolerance)
    {
        if (Math.Abs(row.CashAmount) <= tolerance.CashAmountTolerance) return (true, "", "Cash movement within tolerance.", 0.9m);
        return (false, "CASH_TOLERANCE_BREACH", "Cash movement exceeds configured tolerance.", 0.25m);
    }

    private static (bool,string,string,decimal) MatchTransaction(CanonicalStatementRow row, StatementMatchingTolerance tolerance)
    {
        if (Math.Abs(row.Price * row.Quantity) <= tolerance.TransactionAmountTolerance) return (true, "", "Transaction amount within tolerance.", 0.85m);
        return (false, "TXN_TOLERANCE_BREACH", "Transaction amount exceeds configured tolerance.", 0.25m);
    }
}
