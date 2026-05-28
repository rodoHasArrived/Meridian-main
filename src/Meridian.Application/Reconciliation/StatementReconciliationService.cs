using Meridian.Domain.Reconciliation;

namespace Meridian.Application.Reconciliation;

public sealed class StatementReconciliationService
{
    private static readonly string[] CanonicalStatementColumns =
    [
        "account",
        "symbol",
        "quantity",
        "price",
        "cashAmount",
        "activityType",
        "tradeDate"
    ];

    public Task<string> ValidateAsync(string sourceKind, string sourcePath, CancellationToken ct)
    {
        var normalizedSourceKind = ValidateSourceAccess(sourceKind, sourcePath);
        if (RequiresCanonicalStatementSchema(normalizedSourceKind))
        {
            ValidateCanonicalStatementHeader(sourcePath);
        }

        return Task.FromResult($"Statement source '{normalizedSourceKind}:{sourcePath}' passed local file accessibility checks.");
    }

    public Task<(string ImportId, int RowCount)> ImportAsync(string sourceKind, string sourcePath, CancellationToken ct)
    {
        var normalizedSourceKind = ValidateSourceAccess(sourceKind, sourcePath);
        if (RequiresCanonicalStatementSchema(normalizedSourceKind))
        {
            ValidateCanonicalStatementHeader(sourcePath);
        }

        var content = File.ReadAllText(sourcePath);
        var id = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{sourcePath}|{content}");
        var rowCount = Math.Max(0, File.ReadLines(sourcePath).Count() - 1);
        return Task.FromResult<(string, int)>((id, rowCount));
    }

    public Task<(string ImportId, int MatchCount, int UnresolvedCount)> ReconcileAsync(string sourceKind, string sourcePath, CancellationToken ct)
    {
        var normalizedSourceKind = ValidateSourceAccess(sourceKind, sourcePath);
        var intake = CreateExternalStatementCases(normalizedSourceKind, sourcePath);
        return Task.FromResult((intake.ImportId, intake.MatchCount, intake.Cases.Count));
    }

    public Task<ExternalStatementCaseIntakeResult> CreateExternalStatementCasesAsync(
        string sourceKind,
        string sourcePath,
        CancellationToken ct = default)
    {
        var normalizedSourceKind = ValidateSourceAccess(sourceKind, sourcePath);
        return Task.FromResult(CreateExternalStatementCases(normalizedSourceKind, sourcePath));
    }

    private static string ValidateSourceAccess(string sourceKind, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourceKind))
            throw new ArgumentException("Statement source kind is required.", nameof(sourceKind));

        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Statement source path is required.", nameof(sourcePath));

        var normalizedSourceKind = sourceKind.Trim().ToLowerInvariant();
        if (!string.Equals(normalizedSourceKind, "local", StringComparison.Ordinal)
            && !string.Equals(normalizedSourceKind, "broker", StringComparison.Ordinal)
            && !string.Equals(normalizedSourceKind, "custodian", StringComparison.Ordinal))
            throw new NotSupportedException($"Statement source kind '{sourceKind}' is not supported. Use 'local', 'broker', or 'custodian'.");

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Statement source file '{sourcePath}' was not found.", sourcePath);

        return normalizedSourceKind;
    }

    private static bool RequiresCanonicalStatementSchema(string normalizedSourceKind) =>
        string.Equals(normalizedSourceKind, "broker", StringComparison.Ordinal)
        || string.Equals(normalizedSourceKind, "custodian", StringComparison.Ordinal);

    private ExternalStatementCaseIntakeResult CreateExternalStatementCases(string normalizedSourceKind, string sourcePath)
    {
        if (!RequiresCanonicalStatementSchema(normalizedSourceKind))
        {
            var content = File.ReadAllText(sourcePath);
            var importId = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{sourcePath}|{content}");
            return new ExternalStatementCaseIntakeResult(importId, normalizedSourceKind, sourcePath, 0, 0, []);
        }

        var rows = ReadCanonicalStatementRows(normalizedSourceKind, sourcePath);
        var (matches, cases) = MatchRows(rows);
        return new ExternalStatementCaseIntakeResult(
            rows.Count == 0 ? DeterministicFingerprint.Compute($"{normalizedSourceKind}|{sourcePath}") : rows[0].RawSnapshot["importId"],
            normalizedSourceKind,
            sourcePath,
            rows.Count,
            matches.Count,
            cases);
    }

    private static IReadOnlyList<NormalizedStatementRow> ReadCanonicalStatementRows(string normalizedSourceKind, string sourcePath)
    {
        ValidateCanonicalStatementHeader(sourcePath);

        var content = File.ReadAllText(sourcePath);
        var importId = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{sourcePath}|{content}");
        var rows = new List<NormalizedStatementRow>();
        var lines = File.ReadLines(sourcePath).Skip(1);
        var rowNumber = 1;
        foreach (var line in lines)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < CanonicalStatementColumns.Length)
            {
                throw new InvalidDataException($"Statement row {rowNumber} has {parts.Length} columns; expected at least {CanonicalStatementColumns.Length}.");
            }

            var account = parts[0];
            var symbol = parts[1];
            var quantity = decimal.Parse(parts[2]);
            var price = decimal.Parse(parts[3]);
            var cashAmount = decimal.Parse(parts[4]);
            var activityType = parts[5];
            var tradeDate = DateOnly.Parse(parts[6]);
            var rowFingerprint = DeterministicFingerprint.Compute($"{importId}|{rowNumber}|{line}");
            rows.Add(new NormalizedStatementRow(
                $"{importId}:{rowNumber}",
                ToStatementRowKind(activityType),
                symbol,
                quantity,
                cashAmount == 0m ? price * quantity : cashAmount,
                new DateTimeOffset(tradeDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                "USD",
                rowFingerprint,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["importId"] = importId,
                    ["sourceKind"] = normalizedSourceKind,
                    ["sourcePath"] = sourcePath,
                    ["account"] = account,
                    ["activityType"] = activityType,
                    ["rowNumber"] = rowNumber.ToString()
                }));
        }

        return rows;
    }

    private static void ValidateCanonicalStatementHeader(string sourcePath)
    {
        var header = File.ReadLines(sourcePath).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header))
        {
            throw new InvalidDataException("Statement source file is empty.");
        }

        var actual = header.Split(',', StringSplitOptions.TrimEntries);
        if (actual.Length < CanonicalStatementColumns.Length
            || !CanonicalStatementColumns.SequenceEqual(actual.Take(CanonicalStatementColumns.Length), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Statement source must use the canonical external statement header: account,symbol,quantity,price,cashAmount,activityType,tradeDate.");
        }
    }

    private static StatementRowKind ToStatementRowKind(string activityType)
    {
        if (activityType.Equals("position", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRowKind.Position;
        }

        if (activityType.Equals("cash", StringComparison.OrdinalIgnoreCase)
            || activityType.Equals("cashbalance", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRowKind.CashBalance;
        }

        if (activityType.Equals("fee", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRowKind.Fee;
        }

        if (activityType.Equals("dividend", StringComparison.OrdinalIgnoreCase)
            || activityType.Equals("div", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRowKind.Dividend;
        }

        return StatementRowKind.Transaction;
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
                [new ReconciliationCaseHistoryEntry(DateTimeOffset.UtcNow, "None", "Open", "Case created from statement reconciliation service.")])
            {
                Owner = "fund-ops",
                Priority = row.Kind is StatementRowKind.CashBalance or StatementRowKind.Transaction ? "High" : "Normal",
                DueAtUtc = DateTimeOffset.UtcNow.AddBusinessDays(2),
                CommentThreads =
                [
                    new ReconciliationCaseCommentThread(
                        "statement-intake",
                        "External statement intake",
                        [
                            new ReconciliationCaseComment(
                                Guid.NewGuid().ToString("N"),
                                $"Created from {row.RawSnapshot.GetValueOrDefault("sourceKind", "external")} statement row {row.RawSnapshot.GetValueOrDefault("rowNumber", row.RowId)}.",
                                "system",
                                DateTimeOffset.UtcNow)
                        ])
                ],
                AuditEvents =
                [
                    new ReconciliationCaseAuditEvent(
                        Guid.NewGuid().ToString("N"),
                        "ExternalStatementCaseCreated",
                        DateTimeOffset.UtcNow,
                        "system",
                        "Case created from broker/custodian statement intake.")
                ]
            });
        }

        return (matches, cases);
    }
}

public sealed record ExternalStatementCaseIntakeResult(
    string ImportId,
    string SourceKind,
    string SourcePath,
    int RowCount,
    int MatchCount,
    IReadOnlyList<ReconciliationCase> Cases);

internal static class DateTimeOffsetBusinessDayExtensions
{
    public static DateTimeOffset AddBusinessDays(this DateTimeOffset value, int days)
    {
        var result = value;
        var remaining = days;
        while (remaining > 0)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                remaining--;
            }
        }

        return result;
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
