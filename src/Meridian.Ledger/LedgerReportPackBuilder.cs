using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Ledger;

/// <summary>
/// Builds signed, export-ready ledger report packs from point-in-time financial statements.
/// </summary>
public static class LedgerReportPackBuilder
{
    public static LedgerFinancialReportPack Build(
        IReadOnlyLedger ledger,
        LedgerReportPackRequest request,
        ChartOfAccounts? chart = null,
        string? financialAccountId = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(request);

        var statements = LedgerFinancialStatementBuilder.BuildAsOf(
            ledger,
            request.AsOf,
            chart,
            financialAccountId);

        var artifacts = new List<LedgerReportPackArtifact>
        {
            CreateCsvArtifact("trial-balance.csv", statements.TrialBalanceRows),
            CreateCsvArtifact("income-statement.csv", statements.IncomeStatementRows),
            CreateCsvArtifact("balance-sheet.csv", statements.BalanceSheetRows),
        };
        var provenance = BuildLineProvenance(ledger, request, statements, financialAccountId);
        artifacts.Add(CreateLineProvenanceArtifact(provenance));

        artifacts.Add(CreateManifestArtifact(request, statements, artifacts));

        var payload = string.Join(
            "\n",
            artifacts
                .OrderBy(static artifact => artifact.Name, StringComparer.Ordinal)
                .Select(static artifact => $"{artifact.Name}:{artifact.ChecksumSha256}"));

        var signature = new LedgerReportPackSignature(
            "SHA256",
            ComputeSha256(payload),
            request.GeneratedBy,
            request.GeneratedAtUtc);

        return new LedgerFinancialReportPack(request, statements, artifacts, signature)
        {
            LineProvenance = provenance
        };
    }

    private static LedgerReportPackArtifact CreateCsvArtifact(string name, IReadOnlyList<LedgerChartBalance> rows)
    {
        var content = BuildCsv(rows);
        return new LedgerReportPackArtifact(name, "text/csv", content, ComputeSha256(content));
    }

    private static LedgerReportPackArtifact CreateManifestArtifact(
        LedgerReportPackRequest request,
        LedgerFinancialStatements statements,
        IReadOnlyList<LedgerReportPackArtifact> artifacts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ledger-report-pack-manifest-v1");
        builder.AppendLine(CultureInfo.InvariantCulture, $"report-id,{EscapeCsv(request.ReportId)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"fund-id,{EscapeCsv(request.FundId)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"period-id,{EscapeCsv(request.PeriodId)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"period-start,{request.PeriodStart:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"period-end,{request.PeriodEnd:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"as-of,{request.AsOf:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"base-currency,{EscapeCsv(request.BaseCurrency)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"generated-by,{EscapeCsv(request.GeneratedBy)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"generated-at-utc,{request.GeneratedAtUtc:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"locked-period,{(request.LockedPeriod is null ? "false" : "true")}");

        if (request.LockedPeriod is not null)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"locked-by,{EscapeCsv(request.LockedPeriod.LockedBy)}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"locked-at-utc,{request.LockedPeriod.LockedAtUtc:O}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"locked-reason,{EscapeCsv(request.LockedPeriod.Reason)}");
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"source-run-id,{EscapeCsv(request.SourceRunId ?? string.Empty)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"source-session-id,{EscapeCsv(request.SourceSessionId ?? string.Empty)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"reconciliation-evidence-count,{request.ReconciliationEvidenceLinks.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"approval-evidence-count,{request.ApprovalEvidenceLinks.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"total-assets,{FormatDecimal(statements.TotalAssets)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"total-liabilities,{FormatDecimal(statements.TotalLiabilities)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"total-equity,{FormatDecimal(statements.TotalEquity)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"total-revenue,{FormatDecimal(statements.TotalRevenue)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"total-expenses,{FormatDecimal(statements.TotalExpenses)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"net-income,{FormatDecimal(statements.NetIncome)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"accounting-equation-variance,{FormatDecimal(statements.AccountingEquationVariance)}");
        builder.AppendLine("artifact,content-type,sha256");

        foreach (var artifact in artifacts.OrderBy(static artifact => artifact.Name, StringComparer.Ordinal))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"{EscapeCsv(artifact.Name)},{EscapeCsv(artifact.ContentType)},{artifact.ChecksumSha256}");
        }

        var content = builder.ToString();
        return new LedgerReportPackArtifact("manifest.csv", "text/csv", content, ComputeSha256(content));
    }

    private static IReadOnlyList<LedgerReportLineProvenance> BuildLineProvenance(
        IReadOnlyLedger ledger,
        LedgerReportPackRequest request,
        LedgerFinancialStatements statements,
        string? financialAccountId)
    {
        var allRows = statements.TrialBalanceRows
            .Concat(statements.IncomeStatementRows)
            .Concat(statements.BalanceSheetRows)
            .DistinctBy(static row => row.Path, StringComparer.Ordinal)
            .ToArray();

        return BuildProvenanceForArtifact("trial-balance.csv", statements.TrialBalanceRows)
            .Concat(BuildProvenanceForArtifact("income-statement.csv", statements.IncomeStatementRows))
            .Concat(BuildProvenanceForArtifact("balance-sheet.csv", statements.BalanceSheetRows))
            .ToArray();

        IEnumerable<LedgerReportLineProvenance> BuildProvenanceForArtifact(string artifactName, IReadOnlyList<LedgerChartBalance> rows)
        {
            foreach (var row in rows.OrderBy(static item => item.Path, StringComparer.Ordinal))
            {
                var descendants = allRows
                    .Where(candidate => string.Equals(candidate.Path, row.Path, StringComparison.Ordinal)
                        || candidate.Path.StartsWith($"{row.Path}:", StringComparison.Ordinal))
                    .Select(static candidate => candidate.Account)
                    .Distinct()
                    .ToArray();
                var entries = descendants
                    .SelectMany(account => ledger.GetEntries(account, request.PeriodStart, request.AsOf))
                    .Where(entry => string.IsNullOrWhiteSpace(financialAccountId)
                        || string.Equals(entry.Account.FinancialAccountId, financialAccountId.Trim(), StringComparison.OrdinalIgnoreCase))
                    .DistinctBy(static entry => entry.EntryId)
                    .OrderBy(static entry => entry.Timestamp)
                    .ThenBy(static entry => entry.EntryId)
                    .ToArray();
                var journalEntryIds = entries
                    .Select(static entry => entry.JournalEntryId)
                    .Distinct()
                    .ToArray();
                var ledgerEntryIds = entries
                    .Select(static entry => entry.EntryId)
                    .Distinct()
                    .ToArray();
                var evidenceLinks = request.ReconciliationEvidenceLinks
                    .Concat(request.ApprovalEvidenceLinks)
                    .Concat(ledgerEntryIds.Select(static id => $"ledger-entry:{id:D}"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                yield return new LedgerReportLineProvenance(
                    artifactName,
                    row.Path,
                    "aggregate-balance",
                    row.AggregateBalance,
                    request.SourceRunId,
                    request.SourceSessionId,
                    journalEntryIds,
                    ledgerEntryIds,
                    evidenceLinks);
            }
        }
    }

    private static LedgerReportPackArtifact CreateLineProvenanceArtifact(IReadOnlyList<LedgerReportLineProvenance> provenance)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ArtifactName,RowKey,ValueName,Value,SourceRunId,SourceSessionId,LedgerJournalEntryIds,LedgerEntryIds,EvidenceLinks");
        foreach (var row in provenance.OrderBy(static row => row.ArtifactName, StringComparer.Ordinal).ThenBy(static row => row.RowKey, StringComparer.Ordinal))
        {
            builder.Append(EscapeCsv(row.ArtifactName));
            builder.Append(',');
            builder.Append(EscapeCsv(row.RowKey));
            builder.Append(',');
            builder.Append(EscapeCsv(row.ValueName));
            builder.Append(',');
            builder.Append(FormatDecimal(row.Value));
            builder.Append(',');
            builder.Append(EscapeCsv(row.SourceRunId ?? string.Empty));
            builder.Append(',');
            builder.Append(EscapeCsv(row.SourceSessionId ?? string.Empty));
            builder.Append(',');
            builder.Append(EscapeCsv(string.Join('|', row.LedgerJournalEntryIds.Select(static id => id.ToString("D")))));
            builder.Append(',');
            builder.Append(EscapeCsv(string.Join('|', row.LedgerEntryIds.Select(static id => id.ToString("D")))));
            builder.Append(',');
            builder.AppendLine(EscapeCsv(string.Join('|', row.EvidenceLinks)));
        }

        var content = builder.ToString();
        return new LedgerReportPackArtifact("line-provenance.csv", "text/csv", content, ComputeSha256(content));
    }

    private static string BuildCsv(IReadOnlyList<LedgerChartBalance> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Path,AccountName,AccountType,Symbol,FinancialAccountId,DirectBalance,AggregateBalance");

        foreach (var row in rows.OrderBy(static row => row.Path, StringComparer.Ordinal))
        {
            builder.Append(EscapeCsv(row.Path));
            builder.Append(',');
            builder.Append(EscapeCsv(row.Account.Name));
            builder.Append(',');
            builder.Append(row.Account.AccountType);
            builder.Append(',');
            builder.Append(EscapeCsv(row.Account.Symbol ?? string.Empty));
            builder.Append(',');
            builder.Append(EscapeCsv(row.Account.FinancialAccountId ?? string.Empty));
            builder.Append(',');
            builder.Append(FormatDecimal(row.DirectBalance));
            builder.Append(',');
            builder.AppendLine(FormatDecimal(row.AggregateBalance));
        }

        return builder.ToString();
    }

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string FormatDecimal(decimal value)
        => value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
