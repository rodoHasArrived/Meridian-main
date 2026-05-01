using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Reconciliation;
using Meridian.Domain.Reconciliation;

namespace Meridian.Infrastructure.Reconciliation;

public interface ICanonicalStatementStore
{
    Task<bool> ImportExistsByChecksumAsync(string checksum, CancellationToken ct = default);
    Task SaveImportAsync(CanonicalStatementImport import, IReadOnlyList<CanonicalStatementRow> rows, CancellationToken ct = default);
    Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken ct = default);
}

public sealed class JsonCanonicalStatementStore(string dataRoot) : ICanonicalStatementStore
{
    private readonly string _folder = Path.Combine(dataRoot, "reconciliation", "statement-imports");

    public Task<bool> ImportExistsByChecksumAsync(string checksum, CancellationToken ct = default)
        => Task.FromResult(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder, "*.json").Any(path => File.ReadAllText(path).Contains(checksum, StringComparison.Ordinal)));

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
        var content = await File.ReadAllTextAsync(request.SourcePath, ct).ConfigureAwait(false);
        var checksum = Hash(content);
        if (await store.ImportExistsByChecksumAsync(checksum, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Statement already imported (checksum match).");

        var importId = Guid.NewGuid().ToString("N");
        var rows = ParseRows(content, request, importId).ToList();
        var import = new CanonicalStatementImport(
            importId,
            request.Broker,
            request.StatementDate,
            DateTimeOffset.UtcNow,
            request.SourcePath,
            checksum,
            rows.Count,
            rows.Count);
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
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}

public sealed class StatementMatchingService
{
    public IReadOnlyList<MatchOutcome> MatchRows(IReadOnlyList<CanonicalStatementRow> rows)
        => rows.Select(r =>
        {
            var known = r.Symbol.Length <= 5 && r.Quantity != 0;
            return new MatchOutcome(r.RawChecksum, known ? "linked-position" : "unmatched", known ? $"POS-{r.Symbol}" : string.Empty, known ? 0.95m : 0.35m, known ? "Matched by symbol + non-zero quantity." : "No confident linkage found.");
        }).ToList();
}
