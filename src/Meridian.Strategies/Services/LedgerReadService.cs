using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

/// <summary>
/// Builds workstation-facing ledger read models from recorded run results.
/// </summary>
public sealed class LedgerReadService
{
    public const string LedgerSeam = "ledger";
    private readonly ISecurityReferenceLookup? _securityReferenceLookup;

    public LedgerReadService()
    {
    }

    public LedgerReadService(ISecurityReferenceLookup securityReferenceLookup)
    {
        _securityReferenceLookup = securityReferenceLookup ?? throw new ArgumentNullException(nameof(securityReferenceLookup));
    }

    public LedgerSummary? BuildSummary(StrategyRunEntry entry)
        => BuildBaseSummary(entry);

    public async Task<LedgerSummary?> BuildSummaryAsync(StrategyRunEntry entry, CancellationToken ct = default)
    {
        var summary = BuildBaseSummary(entry);
        if (summary is null || _securityReferenceLookup is null || summary.TrialBalance.Count == 0)
        {
            return summary;
        }

        var requests = BuildLookupRequests(entry, summary.TrialBalance);
        var lookup = await ResolveSecuritiesAsync(requests, ct).ConfigureAwait(false);

        var trialBalance = summary.TrialBalance
            .Select(line => line with
            {
                Security = line.Symbol is not null
                    ? lookup.GetValueOrDefault(ComposeLineKey(line.Symbol, line.FinancialAccountId))
                    : null
            })
            .ToArray();

        var resolvedCount = lookup.Values.Count(static value => value is not null);
        var missingCount = lookup.Count - resolvedCount;

        return summary with
        {
            TrialBalance = trialBalance,
            SecurityResolvedCount = resolvedCount,
            SecurityMissingCount = missingCount
        };
    }

    public IReadOnlyList<StrategyRunContinuityWarning> BuildContinuityWarnings(string runId, LedgerSummary? summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (summary is not null)
        {
            return Array.Empty<StrategyRunContinuityWarning>();
        }

        return
        [
            new StrategyRunContinuityWarning(
                Code: "missing-ledger",
                Severity: StrategyRunContinuityWarningSeverity.Warning,
                Message: "Run does not have a shared ledger summary yet.",
                SourceSeam: LedgerSeam)
        ];
    }

    private static LedgerSummary? BuildBaseSummary(StrategyRunEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var ledger = entry.Metrics?.Ledger;
        if (ledger is null)
        {
            return null;
        }

        var scope = StrategyRunScopeMetadataResolver.Resolve(entry);

        var journal = ledger.Journal
            .OrderByDescending(static item => item.Timestamp)
            .Select(item => new LedgerJournalLine(
                JournalEntryId: item.JournalEntryId,
                Timestamp: item.Timestamp,
                Description: item.Description,
                TotalDebits: item.Lines.Sum(static line => line.Debit),
                TotalCredits: item.Lines.Sum(static line => line.Credit),
                LineCount: item.Lines.Count,
                AccountScopeId: scope.AccountId,
                AccountScopeDisplayName: scope.AccountDisplayName,
                EntityScopeId: scope.EntityId,
                EntityScopeDisplayName: scope.EntityDisplayName,
                SleeveScopeId: scope.SleeveId,
                SleeveScopeDisplayName: scope.SleeveDisplayName,
                VehicleScopeId: scope.VehicleId,
                VehicleScopeDisplayName: scope.VehicleDisplayName))
            .ToArray();

        var accountSummaries = ledger.SummarizeAccounts()
            .OrderBy(static summary => summary.Account.AccountType)
            .ThenBy(static summary => summary.Account.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var trialBalance = accountSummaries
            .Select(summary => new LedgerTrialBalanceLine(
                AccountName: summary.Account.Name,
                AccountType: summary.Account.AccountType.ToString(),
                Symbol: summary.Account.Symbol,
                FinancialAccountId: summary.Account.FinancialAccountId,
                Balance: summary.Balance,
                EntryCount: summary.EntryCount,
                AccountScopeId: string.IsNullOrWhiteSpace(scope.AccountId) ? summary.Account.FinancialAccountId : scope.AccountId,
                AccountScopeDisplayName: scope.AccountDisplayName,
                EntityScopeId: scope.EntityId,
                EntityScopeDisplayName: scope.EntityDisplayName,
                SleeveScopeId: scope.SleeveId,
                SleeveScopeDisplayName: scope.SleeveDisplayName,
                VehicleScopeId: scope.VehicleId,
                VehicleScopeDisplayName: scope.VehicleDisplayName))
            .ToArray();

        return new LedgerSummary(
            LedgerReference: entry.LedgerReference ?? entry.RunId,
            RunId: entry.RunId,
            AsOf: entry.EndedAt ?? entry.StartedAt,
            JournalEntryCount: ledger.Journal.Count,
            LedgerEntryCount: accountSummaries.Sum(static summary => summary.EntryCount),
            AssetBalance: SumBalance(accountSummaries, LedgerAccountType.Asset),
            LiabilityBalance: SumBalance(accountSummaries, LedgerAccountType.Liability),
            EquityBalance: SumBalance(accountSummaries, LedgerAccountType.Equity),
            RevenueBalance: SumBalance(accountSummaries, LedgerAccountType.Revenue),
            ExpenseBalance: SumBalance(accountSummaries, LedgerAccountType.Expense),
            TrialBalance: trialBalance,
            Journal: journal,
            AccountScopeId: scope.AccountId,
            AccountScopeDisplayName: scope.AccountDisplayName,
            EntityScopeId: scope.EntityId,
            EntityScopeDisplayName: scope.EntityDisplayName,
            SleeveScopeId: scope.SleeveId,
            SleeveScopeDisplayName: scope.SleeveDisplayName,
            VehicleScopeId: scope.VehicleId,
            VehicleScopeDisplayName: scope.VehicleDisplayName);
    }

    private static decimal SumBalance(
        IEnumerable<LedgerAccountSummary> summaries,
        LedgerAccountType accountType)
        => summaries
            .Where(summary => summary.Account.AccountType == accountType)
            .Sum(static summary => summary.Balance);

    private static IReadOnlyDictionary<string, SecurityReferenceLookupRequest> BuildLookupRequests(
        StrategyRunEntry entry,
        IReadOnlyList<LedgerTrialBalanceLine> trialBalance)
    {
        var metadataByKey = entry.Metrics?.Ledger?.Journal
            .Where(static item => !string.IsNullOrWhiteSpace(item.Metadata.Symbol))
            .GroupBy(
                static item => ComposeLineKey(item.Metadata.Symbol!, item.Metadata.FinancialAccountId),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group =>
                {
                    var securityIds = group
                        .Select(static item => item.Metadata.SecurityId)
                        .Where(static id => id.HasValue)
                        .Select(static id => id!.Value)
                        .Distinct()
                        .ToArray();
                    var venues = group
                        .Select(static item => item.Metadata.Institution)
                        .Where(static venue => !string.IsNullOrWhiteSpace(venue))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    return (
                        SecurityId: securityIds.Length == 1 ? securityIds[0] : (Guid?)null,
                        Venue: venues.Length == 1 ? venues[0] : null);
                },
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, (Guid? SecurityId, string? Venue)>(StringComparer.OrdinalIgnoreCase);

        return trialBalance
            .Where(static line => !string.IsNullOrWhiteSpace(line.Symbol))
            .GroupBy(static line => ComposeLineKey(line.Symbol!, line.FinancialAccountId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group =>
                {
                    var representative = group.First();
                    var key = group.Key;
                    var meta = metadataByKey.GetValueOrDefault(key);
                    return new SecurityReferenceLookupRequest(
                        SecurityId: meta.SecurityId,
                        IdentifierKind: SecurityIdentifierKind.Ticker.ToString(),
                        IdentifierValue: representative.Symbol,
                        Symbol: representative.Symbol,
                        Venue: meta.Venue ?? representative.FinancialAccountId,
                        Source: meta.SecurityId is null ? "ledger-trial-balance" : "ledger-journal-metadata");
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, WorkstationSecurityReference?>> ResolveSecuritiesAsync(
        IReadOnlyDictionary<string, SecurityReferenceLookupRequest> requests,
        CancellationToken ct)
    {
        var lookup = new Dictionary<string, WorkstationSecurityReference?>(StringComparer.OrdinalIgnoreCase);
        if (_securityReferenceLookup is null)
        {
            return lookup;
        }

        foreach (var (key, request) in requests)
        {
            var resolved = await _securityReferenceLookup.GetByCanonicalAsync(request, ct).ConfigureAwait(false)
                ?? (request.Symbol is null
                    ? null
                    : await _securityReferenceLookup.GetBySymbolAsync(request.Symbol, ct).ConfigureAwait(false));

            lookup[key] = resolved is null
                ? null
                : resolved with
                {
                    LookupSource = request.Source,
                    LookupPath = resolved.LookupPath ?? (request.SecurityId is null ? "symbol" : "security-id")
                };
        }

        return lookup;
    }

    private static string ComposeLineKey(string symbol, string? financialAccountId)
        => string.IsNullOrWhiteSpace(financialAccountId)
            ? symbol.Trim()
            : $"{symbol.Trim()}::{financialAccountId.Trim()}";
}
