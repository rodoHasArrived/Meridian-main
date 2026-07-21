using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

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
        string? financialAccountId = null,
        IReadOnlyList<LedgerTaxLotReliefProjection>? taxLotReliefProjections = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(request);

        var statements = LedgerFinancialStatementBuilder.BuildForPeriod(
            ledger,
            request.PeriodStart,
            request.AsOf,
            chart,
            financialAccountId,
            request.LineDimensions);

        var artifacts = new List<LedgerReportPackArtifact>
        {
            CreateCsvArtifact("trial-balance.csv", statements.TrialBalanceRows),
            CreateCsvArtifact("income-statement.csv", statements.IncomeStatementRows),
            CreateCsvArtifact("balance-sheet.csv", statements.BalanceSheetRows),
            CreateCashFlowArtifact("cash-flow-statement.csv", statements.CashFlow),
            CreatePartnersCapitalArtifact("partners-capital-statement.csv", statements.PartnersCapital),
            CreateFinancialStatementsJsonArtifact(request, statements),
            CreateTaxLotRealizedGainsArtifact(taxLotReliefProjections ?? []),
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

    private static LedgerReportPackArtifact CreateTaxLotRealizedGainsArtifact(
        IReadOnlyList<LedgerTaxLotReliefProjection> projections)
    {
        var builder = new StringBuilder();
        builder.AppendLine("SaleDate,AccountName,Symbol,FinancialAccountId,ReliefMethod,LotId,AcquiredDate,QuantityRelieved,UnitCost,Proceeds,CostBasis,RealizedGainOrLoss,DisallowedWashSaleLoss,RecognizedGainOrLoss");

        foreach (var projection in projections
            .OrderBy(static projection => projection.Input.SaleDate)
            .ThenBy(static projection => projection.Input.Account.Name, StringComparer.Ordinal)
            .ThenBy(static projection => projection.Input.Account.Symbol ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static projection => projection.Input.FinancialAccountId ?? string.Empty, StringComparer.Ordinal))
        {
            var orderedSelections = projection.Selections
                .OrderBy(static selection => selection.Lot.AcquiredDate)
                .ThenBy(static selection => selection.Lot.LotId, StringComparer.Ordinal)
                .ToList();

            // A wash sale defers part of the loss; the ledger recognized only the allowed portion.
            // Spread the disallowed amount across the relieved lots by quantity so each row's
            // recognized gain/loss nets to what was actually booked (residual on the final row),
            // instead of the export overstating the current-period realized loss.
            var disallowedTotal = projection.WashSale?.DisallowedLoss ?? 0m;
            var totalQuantity = orderedSelections.Sum(static selection => selection.QuantityRelieved);
            var allocatedDisallowed = 0m;

            for (var index = 0; index < orderedSelections.Count; index++)
            {
                var selection = orderedSelections[index];
                var proceeds = RoundCurrency(selection.QuantityRelieved * projection.Input.SalePrice);
                var realizedGainOrLoss = proceeds - selection.CostBasis;

                decimal disallowed;
                if (disallowedTotal == 0m)
                {
                    disallowed = 0m;
                }
                else if (index == orderedSelections.Count - 1)
                {
                    disallowed = disallowedTotal - allocatedDisallowed;
                }
                else
                {
                    // Cap each row at the remaining unallocated amount so accumulated rounding on
                    // earlier rows can never push the final row's residual negative (matches the
                    // projector's DistributeBasisIncreases).
                    var remaining = disallowedTotal - allocatedDisallowed;
                    disallowed = totalQuantity == 0m
                        ? 0m
                        : Math.Min(remaining, RoundCurrency(disallowedTotal * (selection.QuantityRelieved / totalQuantity)));
                    allocatedDisallowed += disallowed;
                }

                // Disallowed loss is a positive amount that reduces the recognized loss (a realized
                // loss is negative, so adding the deferred portion moves it toward zero).
                var recognizedGainOrLoss = realizedGainOrLoss + disallowed;

                builder.Append(projection.Input.SaleDate.ToString("O", CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(EscapeCsv(projection.Input.Account.Name));
                builder.Append(',');
                builder.Append(EscapeCsv(projection.Input.Account.Symbol ?? string.Empty));
                builder.Append(',');
                builder.Append(EscapeCsv(projection.Input.FinancialAccountId ?? string.Empty));
                builder.Append(',');
                builder.Append(projection.Input.ReliefMethod);
                builder.Append(',');
                builder.Append(EscapeCsv(selection.Lot.LotId));
                builder.Append(',');
                builder.Append(selection.Lot.AcquiredDate.ToString("O", CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(FormatDecimal(selection.QuantityRelieved));
                builder.Append(',');
                builder.Append(FormatDecimal(selection.UnitCost));
                builder.Append(',');
                builder.Append(FormatDecimal(proceeds));
                builder.Append(',');
                builder.Append(FormatDecimal(selection.CostBasis));
                builder.Append(',');
                builder.Append(FormatDecimal(realizedGainOrLoss));
                builder.Append(',');
                builder.Append(FormatDecimal(disallowed));
                builder.Append(',');
                builder.AppendLine(FormatDecimal(recognizedGainOrLoss));
            }
        }

        var content = builder.ToString();
        return new LedgerReportPackArtifact("tax-lot-realized-gains.csv", "text/csv", content, ComputeSha256(content));
    }

    private static LedgerReportPackArtifact CreateCsvArtifact(string name, IReadOnlyList<LedgerChartBalance> rows)
    {
        var content = BuildCsv(rows);
        return new LedgerReportPackArtifact(name, "text/csv", content, ComputeSha256(content));
    }

    private static LedgerReportPackArtifact CreateCashFlowArtifact(string name, LedgerCashFlowStatement? cashFlow)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Category,Description,CounterpartyType,Amount");
        if (cashFlow is not null)
        {
            foreach (var line in cashFlow.Lines
                .OrderBy(static line => line.Category)
                .ThenBy(static line => line.Description, StringComparer.Ordinal))
            {
                builder.Append(line.Category);
                builder.Append(',');
                builder.Append(EscapeCsv(line.Description));
                builder.Append(',');
                builder.Append(line.CounterpartyType);
                builder.Append(',');
                builder.AppendLine(FormatDecimal(line.Amount));
            }

            AppendCashFlowTotal(builder, "Operating cash flow", cashFlow.OperatingCashFlow);
            AppendCashFlowTotal(builder, "Investing cash flow", cashFlow.InvestingCashFlow);
            AppendCashFlowTotal(builder, "Financing cash flow", cashFlow.FinancingCashFlow);
            AppendCashFlowTotal(builder, "Net change in cash", cashFlow.NetCashFlow);
            AppendCashFlowTotal(builder, "Beginning cash", cashFlow.BeginningCash);
            AppendCashFlowTotal(builder, "Ending cash", cashFlow.EndingCash);
        }

        var content = builder.ToString();
        return new LedgerReportPackArtifact(name, "text/csv", content, ComputeSha256(content));
    }

    private static void AppendCashFlowTotal(StringBuilder builder, string label, decimal amount)
    {
        builder.Append("Total,");
        builder.Append(EscapeCsv(label));
        builder.Append(',');
        builder.Append(',');
        builder.AppendLine(FormatDecimal(amount));
    }

    private static LedgerReportPackArtifact CreatePartnersCapitalArtifact(string name, LedgerPartnersCapitalStatement? partnersCapital)
    {
        var builder = new StringBuilder();
        builder.AppendLine("AccountName,InvestorId,BeginningCapital,Contributions,Distributions,AllocatedResult,OtherMovements,EndingCapital");
        if (partnersCapital is not null)
        {
            foreach (var account in partnersCapital.Accounts
                .OrderBy(static account => account.AccountName, StringComparer.Ordinal)
                .ThenBy(static account => account.InvestorId ?? string.Empty, StringComparer.Ordinal))
            {
                builder.Append(EscapeCsv(account.AccountName));
                builder.Append(',');
                builder.Append(EscapeCsv(account.InvestorId ?? string.Empty));
                builder.Append(',');
                builder.Append(FormatDecimal(account.BeginningCapital));
                builder.Append(',');
                builder.Append(FormatDecimal(account.Contributions));
                builder.Append(',');
                builder.Append(FormatDecimal(account.Distributions));
                builder.Append(',');
                builder.Append(FormatDecimal(account.AllocatedResult));
                builder.Append(',');
                builder.Append(FormatDecimal(account.OtherMovements));
                builder.Append(',');
                builder.AppendLine(FormatDecimal(account.EndingCapital));
            }

            builder.Append("Total,,");
            builder.Append(FormatDecimal(partnersCapital.BeginningCapital));
            builder.Append(',');
            builder.Append(FormatDecimal(partnersCapital.Contributions));
            builder.Append(',');
            builder.Append(FormatDecimal(partnersCapital.Distributions));
            builder.Append(',');
            builder.Append(FormatDecimal(partnersCapital.AllocatedResult));
            builder.Append(',');
            builder.Append(FormatDecimal(partnersCapital.OtherMovements));
            builder.Append(',');
            builder.AppendLine(FormatDecimal(partnersCapital.EndingCapital));
        }

        var content = builder.ToString();
        return new LedgerReportPackArtifact(name, "text/csv", content, ComputeSha256(content));
    }

    private static LedgerReportPackArtifact CreateFinancialStatementsJsonArtifact(
        LedgerReportPackRequest request,
        LedgerFinancialStatements statements)
    {
        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"schema\": \"ledger-financial-statements-v1\",");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"reportId\": {JsonString(request.ReportId)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"fundId\": {JsonString(request.FundId)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"periodId\": {JsonString(request.PeriodId)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"periodStart\": {JsonString(request.PeriodStart.ToString("O", CultureInfo.InvariantCulture))},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"periodEnd\": {JsonString(request.PeriodEnd.ToString("O", CultureInfo.InvariantCulture))},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"asOf\": {JsonString(request.AsOf.ToString("O", CultureInfo.InvariantCulture))},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"baseCurrency\": {JsonString(request.BaseCurrency)},");
        AppendDimensionScope(builder, request.LineDimensions);
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"lockedPeriod\": {(request.LockedPeriod is null ? "false" : "true")},");
        builder.AppendLine("  \"totals\": {");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    \"assets\": {FormatDecimal(statements.TotalAssets)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    \"liabilities\": {FormatDecimal(statements.TotalLiabilities)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    \"equity\": {FormatDecimal(statements.TotalEquity)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    \"revenue\": {FormatDecimal(statements.TotalRevenue)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    \"expenses\": {FormatDecimal(statements.TotalExpenses)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    \"netIncome\": {FormatDecimal(statements.NetIncome)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    \"endingEquity\": {FormatDecimal(statements.EndingEquity)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    \"accountingEquationVariance\": {FormatDecimal(statements.AccountingEquationVariance)}");
        builder.AppendLine("  },");
        AppendRows(builder, "trialBalanceRows", statements.TrialBalanceRows, trailingComma: true);
        AppendRows(builder, "incomeStatementRows", statements.IncomeStatementRows, trailingComma: true);
        AppendRows(builder, "balanceSheetRows", statements.BalanceSheetRows, trailingComma: false);
        builder.AppendLine("}");

        var content = builder.ToString();
        return new LedgerReportPackArtifact("financial-statements.json", "application/json", content, ComputeSha256(content));
    }

    private static void AppendRows(
        StringBuilder builder,
        string propertyName,
        IReadOnlyList<LedgerChartBalance> rows,
        bool trailingComma)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"{propertyName}\": [");
        var orderedRows = rows.OrderBy(static row => row.Path, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < orderedRows.Length; i++)
        {
            var row = orderedRows[i];
            builder.AppendLine("    {");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      \"path\": {JsonString(row.Path)},");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      \"parentPath\": {JsonString(row.ParentPath ?? string.Empty)},");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      \"accountName\": {JsonString(row.Account.Name)},");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      \"accountType\": {JsonString(row.Account.AccountType.ToString())},");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      \"symbol\": {JsonString(row.Account.Symbol ?? string.Empty)},");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      \"financialAccountId\": {JsonString(row.Account.FinancialAccountId ?? string.Empty)},");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      \"directBalance\": {FormatDecimal(row.DirectBalance)},");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      \"aggregateBalance\": {FormatDecimal(row.AggregateBalance)}");
            builder.Append("    }");
            builder.AppendLine(i == orderedRows.Length - 1 ? string.Empty : ",");
        }

        builder.Append("  ]");
        builder.AppendLine(trailingComma ? "," : string.Empty);
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
        builder.AppendLine(CultureInfo.InvariantCulture, $"dimension-scope,{EscapeCsv(FormatDimensionScope(request.LineDimensions))}");

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
                    .Where(entry => MatchesLineDimensions(entry.Dimensions, request.LineDimensions))
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

    private static void AppendDimensionScope(StringBuilder builder, LedgerLineDimensionSet? dimensions)
    {
        dimensions = LedgerLineDimensionSetNormalizer.Canonicalize(dimensions);
        if (dimensions is null)
        {
            builder.AppendLine("  \"dimensionScope\": {},");
            return;
        }

        builder.AppendLine("  \"dimensionScope\": {");
        var fields = BuildDimensionFields(dimensions).ToArray();
        for (var i = 0; i < fields.Length; i++)
        {
            var (name, value) = fields[i];
            builder.Append(CultureInfo.InvariantCulture, $"    {JsonString(name)}: {JsonString(value)}");
            builder.AppendLine(i == fields.Length - 1 ? string.Empty : ",");
        }

        builder.AppendLine("  },");
    }

    private static string FormatDimensionScope(LedgerLineDimensionSet? dimensions)
    {
        dimensions = LedgerLineDimensionSetNormalizer.Canonicalize(dimensions);
        if (dimensions is null)
            return string.Empty;

        return string.Join(
            ';',
            BuildDimensionFields(dimensions)
                .OrderBy(static field => field.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static field => $"{field.Name}={field.Value}"));
    }

    private static IEnumerable<(string Name, string Value)> BuildDimensionFields(LedgerLineDimensionSet dimensions)
        => LedgerLineDimensionSetFields.Enumerate(dimensions)
            .Select(static field => (field.Name, field.Value));

    private static bool MatchesLineDimensions(LedgerLineDimensionSet? actual, LedgerLineDimensionSet? expected)
        => LedgerLineDimensionSetNormalizer.Matches(actual, expected);

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string FormatDecimal(decimal value)
        => value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string JsonString(string value)
        => JsonSerializer.Serialize(value);

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
