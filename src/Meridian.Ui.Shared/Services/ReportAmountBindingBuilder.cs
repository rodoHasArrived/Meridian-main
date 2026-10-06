using System.Collections.Immutable;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>Projects account amounts and exact line identities from the retained canonical ledger population.</summary>
public static class ReportAmountBindingBuilder
{
    public static ImmutableArray<ReportLedgerAmountBindingDto> Build(ReportingLedgerPopulationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var pack = snapshot.Replay();
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
        return result.ToImmutable();
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
