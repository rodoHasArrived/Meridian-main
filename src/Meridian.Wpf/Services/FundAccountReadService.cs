using Meridian.Application.FundAccounts;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Services;

/// <summary>
/// Operator-facing fund account projections built above <see cref="IFundAccountService"/>.
/// </summary>
public sealed class FundAccountReadService
{
    private readonly IFundAccountService _fundAccountService;

    public FundAccountReadService(IFundAccountService fundAccountService)
    {
        _fundAccountService = fundAccountService ?? throw new ArgumentNullException(nameof(fundAccountService));
    }

    public async Task<IReadOnlyList<FundAccountSummary>> GetAccountsAsync(
        string fundProfileId,
        CancellationToken ct = default)
    {
        var fundId = FundProfileKeyTranslator.ToFundId(fundProfileId);
        var grouped = await _fundAccountService.GetFundAccountsAsync(fundId, ct).ConfigureAwait(false);
        var accounts = grouped.CustodianAccounts
            .Concat(grouped.BankAccounts)
            .Concat(grouped.BrokerageAccounts)
            .Concat(grouped.OtherAccounts)
            .ToArray();

        var summaries = new List<FundAccountSummary>(accounts.Length);
        foreach (var account in accounts)
        {
            var latestSnapshot = await _fundAccountService
                .GetLatestBalanceSnapshotAsync(account.AccountId, ct)
                .ConfigureAwait(false);
            var reconciliationRuns = await _fundAccountService
                .GetReconciliationRunsAsync(account.AccountId, ct)
                .ConfigureAwait(false);
            var openBreaks = reconciliationRuns
                .Where(run => !string.Equals(run.Status, "Matched", StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(run.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
                .Sum(run => run.TotalBreaks);

            summaries.Add(new FundAccountSummary(
                AccountId: account.AccountId,
                AccountType: account.AccountType,
                AccountCode: account.AccountCode,
                DisplayName: account.DisplayName,
                BaseCurrency: account.BaseCurrency,
                Institution: account.Institution,
                IsActive: account.IsActive,
                CashBalance: latestSnapshot?.CashBalance ?? 0m,
                SecuritiesMarketValue: latestSnapshot?.SecuritiesMarketValue ?? 0m,
                NetAssetValue: (latestSnapshot?.CashBalance ?? 0m) + (latestSnapshot?.SecuritiesMarketValue ?? 0m),
                LastSnapshotDate: latestSnapshot?.AsOfDate,
                ReconciliationRuns: reconciliationRuns.Count,
                OpenBreaks: openBreaks,
                PortfolioId: account.PortfolioId,
                LedgerReference: account.LedgerReference,
                BankName: account.BankDetails?.BankName,
                AccountNumberMasked: MaskAccountNumber(account.BankDetails?.AccountNumber),
                EntityId: account.EntityId,
                SleeveId: account.SleeveId,
                VehicleId: account.VehicleId,
                StrategyId: account.StrategyId,
                RunId: account.RunId,
                StructureLabel: BuildStructureLabel(account),
                WorkflowLabel: BuildWorkflowLabel(account)));
        }

        return summaries
            .OrderBy(summary => summary.AccountType.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<BankAccountSnapshot>> GetBankSnapshotsAsync(
        string fundProfileId,
        CancellationToken ct = default)
    {
        var fundId = FundProfileKeyTranslator.ToFundId(fundProfileId);
        var grouped = await _fundAccountService.GetFundAccountsAsync(fundId, ct).ConfigureAwait(false);
        var bankingAccounts = grouped.BankAccounts
            .Concat(grouped.BrokerageAccounts.Where(account => account.BankDetails is not null))
            .ToArray();

        var snapshots = new List<BankAccountSnapshot>(bankingAccounts.Length);
        foreach (var account in bankingAccounts)
        {
            var latestSnapshot = await _fundAccountService
                .GetLatestBalanceSnapshotAsync(account.AccountId, ct)
                .ConfigureAwait(false);
            var bankLines = await _fundAccountService
                .GetBankStatementLinesAsync(account.AccountId, ct: ct)
                .ConfigureAwait(false);
            var latestLine = bankLines
                .OrderByDescending(line => line.StatementDate)
                .ThenByDescending(line => line.ValueDate)
                .FirstOrDefault();

            snapshots.Add(new BankAccountSnapshot(
                AccountId: account.AccountId,
                DisplayName: account.DisplayName,
                AccountCode: account.AccountCode,
                BankName: account.BankDetails?.BankName ?? account.Institution ?? "Bank account",
                Currency: account.BaseCurrency,
                CurrentBalance: latestSnapshot?.CashBalance ?? latestLine?.RunningBalance ?? 0m,
                PendingSettlement: latestSnapshot?.PendingSettlement ?? 0m,
                LastStatementDate: latestLine?.StatementDate,
                StatementLineCount: bankLines.Count,
                LatestTransactionType: latestLine?.TransactionType,
                LatestTransactionAmount: latestLine?.Amount));
        }

        return snapshots
            .OrderBy(snapshot => snapshot.BankName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(snapshot => snapshot.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? MaskAccountNumber(string? accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            return null;
        }

        var trimmed = accountNumber.Trim();
        if (trimmed.Length <= 4)
        {
            return trimmed;
        }

        return $"****{trimmed[^4..]}";
    }

    private static string BuildStructureLabel(AccountSummaryDto account)
    {
        var segments = new List<string>(3);

        if (account.EntityId.HasValue)
        {
            segments.Add($"Entity {FormatKey(account.EntityId.Value)}");
        }

        if (account.SleeveId.HasValue)
        {
            segments.Add($"Sleeve {FormatKey(account.SleeveId.Value)}");
        }

        if (account.VehicleId.HasValue)
        {
            segments.Add($"Vehicle {FormatKey(account.VehicleId.Value)}");
        }

        return segments.Count == 0 ? "Fund-level" : string.Join(" • ", segments);
    }

    private static string BuildWorkflowLabel(AccountSummaryDto account)
    {
        var segments = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(account.StrategyId))
        {
            segments.Add($"Strategy {account.StrategyId}");
        }

        if (!string.IsNullOrWhiteSpace(account.RunId))
        {
            segments.Add($"Run {account.RunId}");
        }

        if (!string.IsNullOrWhiteSpace(account.PortfolioId))
        {
            segments.Add($"Portfolio {account.PortfolioId}");
        }

        if (!string.IsNullOrWhiteSpace(account.LedgerReference))
        {
            segments.Add($"Ledger {account.LedgerReference}");
        }

        return segments.Count == 0 ? "Manual / external" : string.Join(" • ", segments);
    }

    private static string FormatKey(Guid value)
        => value.ToString("N")[..8].ToUpperInvariant();
}
