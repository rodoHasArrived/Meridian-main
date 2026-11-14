using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Export;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FundOperationsWorkspaceReadService
{

    private async Task<FundNavAttributionSummaryDto> BuildNavSummaryAsync(
        string fundProfileId,
        string currency,
        FundLedgerBook fundLedgerBook,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        var result = await _navAttributionService
            .AttributeAsync(
                new NavAttributionRequest(
                    FundId: fundProfileId,
                    AsOf: asOf,
                    FundLedger: fundLedgerBook,
                    Currency: currency),
                ct)
            .ConfigureAwait(false);

        var assetClassExposure = result.Consolidated.ByAssetClass
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new FundNavAssetClassExposureDto(
                AssetClass: pair.Key,
                NetBalance: pair.Value))
            .ToArray();

        return new FundNavAttributionSummaryDto(
            Currency: result.Currency,
            TotalNav: result.Consolidated.TotalNav,
            ComponentCount: result.Consolidated.Components.Count,
            EntityCount: result.ByEntity.Count,
            SleeveCount: result.BySleeve.Count,
            VehicleCount: result.ByVehicle.Count,
            AssetClassExposure: assetClassExposure);
    }

    private async Task<FundLedgerSummary> BuildLedgerSummaryAsync(
        string fundProfileId,
        string displayName,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf,
        FundLedgerBook fundLedgerBook,
        CancellationToken ct)
    {
        var journal = BuildJournal(fundProfileId, fundLedgerBook, scopeKind, scopeId, asOf);
        var trialBalance = await BuildTrialBalanceAsync(fundProfileId, fundLedgerBook, scopeKind, scopeId, asOf, ct).ConfigureAwait(false);
        var entityCount = fundLedgerBook.EntitySnapshotsAsOf(asOf).Count;
        var sleeveCount = fundLedgerBook.SleeveSnapshotsAsOf(asOf).Count;
        var vehicleCount = fundLedgerBook.VehicleSnapshotsAsOf(asOf).Count;

        return new FundLedgerSummary(
            FundProfileId: fundProfileId,
            FundDisplayName: displayName,
            ScopeKind: scopeKind,
            ScopeId: scopeId,
            AsOf: asOf,
            JournalEntryCount: journal.Count,
            LedgerEntryCount: trialBalance.Sum(static line => line.EntryCount),
            AssetBalance: SumBalance(trialBalance, LedgerAccountType.Asset),
            LiabilityBalance: SumBalance(trialBalance, LedgerAccountType.Liability),
            EquityBalance: SumBalance(trialBalance, LedgerAccountType.Equity),
            RevenueBalance: SumBalance(trialBalance, LedgerAccountType.Revenue),
            ExpenseBalance: SumBalance(trialBalance, LedgerAccountType.Expense),
            TrialBalance: trialBalance,
            Journal: journal,
            EntityCount: entityCount,
            SleeveCount: sleeveCount,
            VehicleCount: vehicleCount);
    }

    public static FundLedgerReconciliationSnapshot ProjectReconciliationSnapshot(FundLedgerSnapshot snapshot)
    {
        return new FundLedgerReconciliationSnapshot(
            FundProfileId: snapshot.FundId,
            AsOf: snapshot.AsOf,
            Consolidated: ProjectDimensionSnapshot(snapshot.FundId, FundLedgerScope.Consolidated, null, snapshot.Consolidated),
            Entities: snapshot.Entities.ToDictionary(
                pair => pair.Key,
                pair => ProjectDimensionSnapshot(snapshot.FundId, FundLedgerScope.Entity, pair.Key, pair.Value),
                StringComparer.OrdinalIgnoreCase),
            Sleeves: snapshot.Sleeves.ToDictionary(
                pair => pair.Key,
                pair => ProjectDimensionSnapshot(snapshot.FundId, FundLedgerScope.Sleeve, pair.Key, pair.Value),
                StringComparer.OrdinalIgnoreCase),
            Vehicles: snapshot.Vehicles.ToDictionary(
                pair => pair.Key,
                pair => ProjectDimensionSnapshot(snapshot.FundId, FundLedgerScope.Vehicle, pair.Key, pair.Value),
                StringComparer.OrdinalIgnoreCase));
    }

    private static FundLedgerDimensionSnapshot ProjectDimensionSnapshot(
        string fundProfileId,
        FundLedgerScope scopeKind,
        string? scopeId,
        LedgerSnapshot snapshot) =>
        new(
            Timestamp: snapshot.Timestamp,
            JournalEntryCount: snapshot.JournalEntryCount,
            LedgerEntryCount: snapshot.LedgerEntryCount,
            Balances: snapshot.Balances
                .OrderBy(static pair => pair.Key.AccountType)
                .ThenBy(static pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new FundLedgerSnapshotBalanceLine(
                    AccountName: pair.Key.Name,
                    AccountType: pair.Key.AccountType.ToString(),
                    Symbol: pair.Key.Symbol,
                    FinancialAccountId: pair.Key.FinancialAccountId,
                    Balance: pair.Value,
                    Dimensions: BuildFundLedgerDimensions(fundProfileId, scopeKind, scopeId, pair.Key.FinancialAccountId)))
                .ToArray());

    private async Task<FundLedgerBook> BuildLedgerBookAsync(
        string fundProfileId,
        IReadOnlyList<StrategyRunEntry> runs,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        var fundLedgerBook = new FundLedgerBook(fundProfileId);

        if (_ledgerJournalStore is not null)
        {
            var durableProjection = await _ledgerJournalStore
                .HydrateFundLedgerAsOfAsync(
                    fundProfileId,
                    asOf,
                    AccountingBasisKindDto.Primary,
                    ct)
                .ConfigureAwait(false);
            foreach (var journalEntry in durableProjection.Journal)
            {
                fundLedgerBook.FundLedger.Post(journalEntry);
            }

            return fundLedgerBook;
        }

        foreach (var run in runs)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The retained run ledger does not match the requested fund scope.");

            var ledger = run.Metrics?.Ledger;
            if (ledger?.JournalEntryCount is not > 0 &&
                run.OutputMetadata.TryGetValue(StrategyRunLedgerReference.HashMetadataKey, out var archiveHash))
            {
                // Summary reads intentionally omit archived journals. Hydrate only the runs
                // already admitted by the fund scope and selected-run filter, and never use
                // research archives as a fallback for a configured durable accounting book.
                var detail = await _strategyRepository.GetRunByIdAsync(run.RunId, ct).ConfigureAwait(false);
                var detailLedger = detail?.Metrics?.Ledger;
                if (detail is null || !string.Equals(detail.RunId, run.RunId, StringComparison.Ordinal) ||
                    !string.Equals(detail.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase) ||
                    !detail.OutputMetadata.TryGetValue(StrategyRunLedgerReference.HashMetadataKey, out var detailHash) ||
                    !string.Equals(detailHash, archiveHash, StringComparison.Ordinal) ||
                    detailLedger is null || detailLedger.JournalEntryCount != run.RetainedJournalEntryCount)
                    throw new InvalidDataException("The retained run ledger is unavailable or no longer matches its scoped archive reference.");

                ledger = detailLedger;
            }

            foreach (var journalEntry in ledger?.Journal ?? [])
            {
                fundLedgerBook.FundLedger.Post(journalEntry);
            }
        }

        return fundLedgerBook;
    }

    private async Task<IReadOnlyList<FundTrialBalanceLine>> BuildTrialBalanceAsync(
        string fundProfileId,
        FundLedgerBook book,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        IReadOnlyDictionary<LedgerAccount, decimal> balances = scopeKind switch
        {
            FundLedgerScope.Consolidated => book.ConsolidatedSnapshotAsOf(asOf).Balances,
            FundLedgerScope.Entity => book.EntityLedger(scopeId ?? string.Empty).SnapshotAsOf(asOf).Balances,
            FundLedgerScope.Sleeve => book.SleeveLedger(scopeId ?? string.Empty).SnapshotAsOf(asOf).Balances,
            FundLedgerScope.Vehicle => book.VehicleLedger(scopeId ?? string.Empty).SnapshotAsOf(asOf).Balances,
            _ => book.ConsolidatedSnapshotAsOf(asOf).Balances
        };

        var entryCounts = BuildEntryCounts(book, scopeKind, scopeId, asOf);
        var securityLookup = await ResolveSecurityReferencesAsync(
            balances.Keys
                .Select(static account => account.Symbol)
                .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))!
                .Select(static symbol => symbol!),
            ct).ConfigureAwait(false);

        return balances
            .OrderBy(static pair => pair.Key.AccountType)
            .ThenBy(static pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new FundTrialBalanceLine(
                AccountName: pair.Key.Name,
                AccountType: pair.Key.AccountType.ToString(),
                Symbol: pair.Key.Symbol,
                FinancialAccountId: pair.Key.FinancialAccountId,
                Balance: pair.Value,
                EntryCount: entryCounts.TryGetValue(pair.Key, out var count) ? count : 0,
                Security: pair.Key.Symbol is not null ? securityLookup.GetValueOrDefault(pair.Key.Symbol) : null,
                Dimensions: BuildFundLedgerDimensions(fundProfileId, scopeKind, scopeId, pair.Key.FinancialAccountId)))
            .ToArray();
    }

    private static IReadOnlyList<FundJournalLine> BuildJournal(
        string fundProfileId,
        FundLedgerBook book,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf)
    {
        IEnumerable<JournalEntry> source = scopeKind switch
        {
            FundLedgerScope.Consolidated => book.ConsolidatedJournalEntries(),
            FundLedgerScope.Entity => book.EntityLedger(scopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Sleeve => book.SleeveLedger(scopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Vehicle => book.VehicleLedger(scopeId ?? string.Empty).GetJournalEntries(),
            _ => book.ConsolidatedJournalEntries()
        };

        return source
            .Where(entry => entry.Timestamp <= asOf)
            .OrderByDescending(static entry => entry.Timestamp)
            .ThenByDescending(static entry => entry.JournalEntryId)
            .Select(entry =>
            {
                var financialAccountIds = entry.Lines
                    .Select(static line => line.Account.FinancialAccountId)
                    .Where(static accountId => !string.IsNullOrWhiteSpace(accountId))
                    .Select(static accountId => accountId!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static accountId => accountId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new FundJournalLine(
                    JournalEntryId: entry.JournalEntryId,
                    Timestamp: entry.Timestamp,
                    Description: entry.Description,
                    TotalDebits: entry.Lines.Sum(static line => line.Debit),
                    TotalCredits: entry.Lines.Sum(static line => line.Credit),
                    LineCount: entry.Lines.Count,
                    FinancialAccountIds: financialAccountIds,
                    Dimensions: BuildFundLedgerDimensions(
                        fundProfileId,
                        scopeKind,
                        scopeId,
                        financialAccountIds.Length == 1 ? financialAccountIds[0] : null));
            })
            .ToArray();
    }

    private static LedgerDimensionSetDto BuildFundLedgerDimensions(
        string fundProfileId,
        FundLedgerScope scopeKind,
        string? scopeId,
        string? financialAccountId)
    {
        var normalizedScopeId = TrimOrNull(scopeId);
        return new LedgerDimensionSetDto(
            FundId: TrimOrNull(fundProfileId),
            EntityId: scopeKind == FundLedgerScope.Entity ? normalizedScopeId : null,
            SleeveId: scopeKind == FundLedgerScope.Sleeve ? normalizedScopeId : null,
            AccountId: TrimOrNull(financialAccountId));
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<LedgerAccount, int> BuildEntryCounts(
        FundLedgerBook book,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf)
    {
        IEnumerable<JournalEntry> source = scopeKind switch
        {
            FundLedgerScope.Consolidated => book.ConsolidatedJournalEntries(),
            FundLedgerScope.Entity => book.EntityLedger(scopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Sleeve => book.SleeveLedger(scopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Vehicle => book.VehicleLedger(scopeId ?? string.Empty).GetJournalEntries(),
            _ => book.ConsolidatedJournalEntries()
        };

        return source
            .Where(entry => entry.Timestamp <= asOf)
            .SelectMany(static entry => entry.Lines)
            .GroupBy(static line => line.Account)
            .ToDictionary(static group => group.Key, static group => group.Count());
    }

    private static decimal SumBalance(IEnumerable<FundTrialBalanceLine> lines, LedgerAccountType accountType)
        => lines
            .Where(line => string.Equals(line.AccountType, accountType.ToString(), StringComparison.Ordinal))
            .Sum(static line => line.Balance);

    private async Task<Dictionary<string, WorkstationSecurityReference?>> ResolveSecurityReferencesAsync(
        IEnumerable<string> symbols,
        CancellationToken ct)
    {
        var lookup = new Dictionary<string, WorkstationSecurityReference?>(StringComparer.OrdinalIgnoreCase);
        if (_securityReferenceLookup is null)
        {
            return lookup;
        }

        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            lookup[symbol] = await _securityReferenceLookup
                .GetBySymbolAsync(symbol, ct)
                .ConfigureAwait(false);
        }

        return lookup;
    }
}
