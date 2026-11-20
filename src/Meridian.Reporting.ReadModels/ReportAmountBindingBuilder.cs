using System.Collections.Immutable;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>Projects account amounts and exact line identities from the retained canonical ledger population.</summary>
public static class ReportAmountBindingBuilder
{
    public static ImmutableArray<ReportLedgerAmountBindingDto> Build(ReportingLedgerPopulationSnapshot snapshot)
        => Build(snapshot, null);

    public static ImmutableArray<ReportLedgerAmountBindingDto> Build(
        ReportingLedgerPopulationSnapshot snapshot, LedgerFinancialReportPack? replayedPack)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var pack = replayedPack ?? snapshot.Replay();
        var result = ImmutableArray.CreateBuilder<ReportLedgerAmountBindingDto>();
        // Use Ledger's own report-line support and normal-balance calculations, including any
        // rolled-up chart rows, rather than reconstructing accounting rules in the UI layer.
        foreach (var row in pack.Statements.TrialBalanceRows.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            var support = pack.LineProvenance.Single(item => item.ArtifactName == "trial-balance.csv" && item.RowKey == row.Path);
            if (support.LedgerEntryIds.Count == 0)
                continue;
            var identity = JsonSerializer.Serialize(new
            {
                artifact = "trial-balance.csv",
                row.Path,
                row.Account.Name,
                row.Account.AccountType,
                row.Account.Symbol,
                financialAccountId = row.Account.FinancialAccountId?.ToUpperInvariant()
            });
            result.Add(new ReportLedgerAmountBindingDto(Sha256Digest.ComputeUtf8(identity),
                row.Path, row.AggregateBalance, snapshot.Request.BaseCurrency, snapshot.Scope,
                support.LedgerJournalEntryIds, support.LedgerEntryIds));
        }
        if (pack.StatementControl is { } statements)
        {
            AppendStatementControls(result, snapshot, statements);
            if (statements.Comparison is { } comparison && statements.Definition.Comparison is { } prior)
                AppendStatementControls(result, snapshot, comparison.Prior, prior);
        }
        return result.ToImmutable();
    }

    private static void AppendStatementControls(
        ImmutableArray<ReportLedgerAmountBindingDto>.Builder result,
        ReportingLedgerPopulationSnapshot snapshot, LedgerStatementControlPack statements,
        LedgerStatementComparisonDefinition? prior = null)
    {
        var accountMappings = statements.LineMappings.ToLookup(static mapping => mapping.AccountRowKey, StringComparer.Ordinal);
        var pnlMappings = statements.LineMappings.Where(static mapping => mapping.ProfitAndLossRowKey is not null)
            .ToLookup(static mapping => mapping.ProfitAndLossRowKey!, StringComparer.Ordinal);
        var capitalMappings = statements.LineMappings.Where(static mapping => mapping.CapitalAccountRowKey is not null)
            .ToLookup(static mapping => mapping.CapitalAccountRowKey!, StringComparer.Ordinal);
        var cashRows = statements.LineMappings.Where(static mapping => mapping.IsCash).ToArray();
        var cashActivityJournals = cashRows.Where(static mapping => !mapping.IsOpening)
            .Select(static mapping => mapping.JournalEntryId).ToHashSet();
        var cashActivity = statements.LineMappings.Where(mapping => cashActivityJournals.Contains(mapping.JournalEntryId)).ToArray();
        var pnlActivity = statements.LineMappings.Where(static mapping => mapping.ProfitAndLossRowKey is not null).ToArray();
        foreach (var row in statements.Rows)
        {
            var matches = (row.Statement switch
            {
                "Trial Balance" when row.Measure == "Opening" => accountMappings[row.RowKey].Where(static map => map.IsOpening),
                "Trial Balance" when row.Measure == "Period debit" => accountMappings[row.RowKey].Where(static map => !map.IsOpening && map.Debit != 0m),
                "Trial Balance" when row.Measure == "Period credit" => accountMappings[row.RowKey].Where(static map => !map.IsOpening && map.Credit != 0m),
                "Trial Balance" => accountMappings[row.RowKey],
                "Balance Sheet" when row.RowKey == "assets" => statements.LineMappings.Where(static map => map.AccountType == LedgerAccountType.Asset),
                "Balance Sheet" when row.RowKey == "liabilities" => statements.LineMappings.Where(static map => map.AccountType == LedgerAccountType.Liability),
                "Balance Sheet" when row.RowKey == "undistributed-income" => statements.LineMappings.Where(static map => map.AccountType is LedgerAccountType.Revenue or LedgerAccountType.Expense),
                "Balance Sheet" when row.RowKey == "equity" => statements.LineMappings.Where(static map => map.AccountType is LedgerAccountType.Equity or LedgerAccountType.Revenue or LedgerAccountType.Expense),
                "Balance Sheet" => accountMappings[row.RowKey],
                "Income Statement" when row.RowKey == "total-revenue" => pnlActivity.Where(static map => map.AccountType == LedgerAccountType.Revenue),
                "Income Statement" when row.RowKey == "total-expenses" => pnlActivity.Where(static map => map.AccountType == LedgerAccountType.Expense),
                "Income Statement" when row.RowKey == "net-income" => pnlActivity,
                "Income Statement" => accountMappings[row.RowKey].Where(static map => !map.IsOpening && !map.IsClosingTransfer),
                "Profit and Loss" => pnlMappings[row.RowKey],
                "Fiscal Close" when row.Measure == "Before closing transfers" => accountMappings[row.RowKey].Where(static map => !map.IsClosingTransfer),
                "Fiscal Close" when row.Measure == "Posted closing transfers" => accountMappings[row.RowKey].Where(static map => map.IsClosingTransfer),
                "Fiscal Close" => accountMappings[row.RowKey],
                "Cash Bridge" when row.RowKey == "opening-cash" => cashRows.Where(static map => map.IsOpening),
                "Cash Bridge" when row.RowKey == "ending-cash" => cashRows,
                "Cash Bridge" when row.RowKey == "exchange-rate-effects" => cashRows.Where(map => statements.Definition.CashFxRevaluationJournalIds.Contains(map.JournalEntryId)),
                "Cash Bridge" => cashActivity,
                "Partners' Capital" when row.RowKey == "ending-capital" => statements.LineMappings.Where(static map => map.AccountType is LedgerAccountType.Equity or LedgerAccountType.Revenue or LedgerAccountType.Expense),
                "Partners' Capital" when capitalMappings.Contains(row.RowKey) => RelatedCapitalInputs(row, capitalMappings[row.RowKey], statements.LineMappings),
                "Partners' Capital" => statements.LineMappings.Where(static map => map.AccountType is LedgerAccountType.Revenue or LedgerAccountType.Expense),
                "Agreement Controls" => statements.LineMappings,
                _ => []
            }).ToArray();
            if (matches.Length == 0)
                continue;

            var identity = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
            {
                artifact = LedgerStatementControlPack.ArtifactName,
                row.Statement,
                RowKey = prior is null ? row.RowKey : $"{prior.PeriodId:D}:{row.RowKey}",
                row.Measure
            }));
            var label = $"{row.Statement} / {row.Label} / {row.Measure}";
            if (prior is not null)
                label = FormattableString.Invariant($"Comparative {prior.StartDate:yyyy-MM-dd} to {prior.EndDate:yyyy-MM-dd} / {label}");
            result.Add(new(identity, label, row.Amount, statements.Currency,
                snapshot.Scope, matches.Select(static map => map.JournalEntryId).Distinct().ToArray(),
                matches.Select(static map => map.LedgerEntryId).ToArray()));
        }
    }

    private static IEnumerable<LedgerStatementControlLineMapping> RelatedCapitalInputs(
        LedgerStatementControlRow row, IEnumerable<LedgerStatementControlLineMapping> accountLines,
        IReadOnlyList<LedgerStatementControlLineMapping> population)
    {
        if (row.Measure == "Opening")
            return accountLines.Where(static map => map.IsOpening);
        if (row.Measure == "Closing")
            return accountLines;
        var journalIds = accountLines.Where(static map => !map.IsOpening).Select(static map => map.JournalEntryId).ToHashSet();
        return population.Where(map => journalIds.Contains(map.JournalEntryId));
    }

    public static bool MatchesRetained(ReportingLedgerPopulationSnapshot snapshot)
    {
        if (snapshot.ReportAmounts.IsDefault || snapshot.ReportAmounts.Any(amount => amount is null
            || amount.Scope is null || amount.JournalEntryIds is null || amount.LedgerEntryIds is null))
            return false;
        var expected = Build(snapshot);
        return expected.Length == snapshot.ReportAmounts.Length && expected.Zip(snapshot.ReportAmounts).All(pair =>
            pair.First.AmountId == pair.Second.AmountId && pair.First.Label == pair.Second.Label
            && pair.First.Amount == pair.Second.Amount && pair.First.Currency == pair.Second.Currency
            && pair.First.Scope == pair.Second.Scope && pair.Second.SubjectId is null
            && pair.Second.SourceSnapshotHash is null
            && pair.First.JournalEntryIds.SequenceEqual(pair.Second.JournalEntryIds)
            && pair.First.LedgerEntryIds.SequenceEqual(pair.Second.LedgerEntryIds));
    }
}
