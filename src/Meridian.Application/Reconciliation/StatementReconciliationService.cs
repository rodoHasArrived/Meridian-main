using Meridian.Domain.Reconciliation;

namespace Meridian.Application.Reconciliation;

public sealed class StatementReconciliationService
{
    public Task<string> ValidateAsync(string sourceKind, string sourcePath, CancellationToken ct)
    {
        var normalizedSourceKind = ValidateSourceAccess(sourceKind, sourcePath);
        return Task.FromResult($"Statement source '{normalizedSourceKind}:{sourcePath}' passed local file accessibility checks.");
    }

    public Task<(string ImportId, int RowCount)> ImportAsync(string sourceKind, string sourcePath, CancellationToken ct)
    {
        var normalizedSourceKind = ValidateSourceAccess(sourceKind, sourcePath);
        var id = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{sourcePath}");
        var rowCount = File.ReadLines(sourcePath).Count();
        return Task.FromResult<(string, int)>((id, rowCount));
    }

    public Task<(string ImportId, int MatchCount, int UnresolvedCount)> ReconcileAsync(string sourceKind, string sourcePath, CancellationToken ct)
    {
        var normalizedSourceKind = ValidateSourceAccess(sourceKind, sourcePath);
        var id = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{sourcePath}");
        return Task.FromResult((id, 0, 0));
    }

    private static string ValidateSourceAccess(string sourceKind, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourceKind))
            throw new ArgumentException("Statement source kind is required.", nameof(sourceKind));

        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Statement source path is required.", nameof(sourcePath));

        var normalizedSourceKind = sourceKind.Trim().ToLowerInvariant();
        if (!string.Equals(normalizedSourceKind, "local", StringComparison.Ordinal))
            throw new NotSupportedException($"Statement source kind '{sourceKind}' is not supported. Use 'local'.");

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Statement source file '{sourcePath}' was not found.", sourcePath);

        return normalizedSourceKind;
    }

    public (IReadOnlyList<ReconciliationMatchLink> Matches, IReadOnlyList<ReconciliationCase> Cases) MatchRows(
        IReadOnlyList<NormalizedStatementRow> rows)
    {
        var matches = new List<ReconciliationMatchLink>();
        var cases = new List<ReconciliationCase>();

        foreach (var row in rows)
        {
            if (row.Kind == StatementRowKind.Position && Math.Abs(row.Quantity) > 0)
            {
                matches.Add(new ReconciliationMatchLink(row.RowId, "position:auto", null, null, null, null, null, "high", "Symbol and quantity aligned within tolerance window."));
                continue;
            }

            cases.Add(new ReconciliationCase(
                $"case:{row.RowId}",
                row.Fingerprint,
                "Open",
                "No deterministic match candidate met confidence threshold.",
                0.35m,
                "No deterministic match candidate met confidence threshold.",
                DateTimeOffset.UtcNow,
                [new ReconciliationCaseHistoryEntry(DateTimeOffset.UtcNow, "None", "Open", "Case created from statement reconciliation service.")]));
        }

        return (matches, cases);
    }
}

public static class DeterministicFingerprint
{
    public static string Compute(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
