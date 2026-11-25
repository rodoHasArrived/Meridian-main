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

    private async Task<IReadOnlyList<StrategyRunEntry>> LoadRunsAsync(
        string fundProfileId,
        CancellationToken ct)
    {
        var runs = new List<StrategyRunEntry>();
        await foreach (var run in _strategyRepository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            {
                runs.Add(run);
            }
        }

        return runs
            .OrderByDescending(static run => run.StartedAt)
            .ThenByDescending(static run => run.RunId, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<StrategyRunEntry>> LoadFundScopedRunsAsync(CancellationToken ct)
    {
        var runs = new List<StrategyRunEntry>();
        await foreach (var run in _strategyRepository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(run.FundProfileId))
            {
                runs.Add(run);
            }
        }

        return runs
            .OrderByDescending(static run => run.StartedAt)
            .ThenByDescending(static run => run.RunId, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<AccountWorkspaceProjection>> GetAccountProjectionsAsync(
        Guid fundId,
        CancellationToken ct)
    {
        var grouped = await _fundAccountService.GetFundAccountsAsync(fundId, ct).ConfigureAwait(false);
        var accounts = grouped.CustodianAccounts
            .Concat(grouped.BankAccounts)
            .Concat(grouped.BrokerageAccounts)
            .Concat(grouped.OtherAccounts)
            .ToArray();

        return await BuildAccountWorkspaceProjectionsAsync(accounts, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<AccountWorkspaceProjection>> GetAllAccountProjectionsAsync(CancellationToken ct)
    {
        var accounts = await _fundAccountService
            .QueryAccountsAsync(new AccountStructureQuery(ActiveOnly: true), ct)
            .ConfigureAwait(false);

        return await BuildAccountWorkspaceProjectionsAsync(accounts, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<AccountWorkspaceProjection>> BuildAccountWorkspaceProjectionsAsync(
        IReadOnlyList<AccountSummaryDto> accounts,
        CancellationToken ct)
    {
        var reconciliationCut = await ReadAccountReconciliationCutAsync(accounts, ct).ConfigureAwait(false);
        var projections = new List<AccountWorkspaceProjection>(accounts.Count);
        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();

            var latestSnapshot = await _fundAccountService
                .GetLatestBalanceSnapshotAsync(account.AccountId, ct)
                .ConfigureAwait(false);
            var reconciliationRuns = reconciliationCut.Runs[account.AccountId];
            var reconciliationResults = reconciliationRuns.SelectMany(run => reconciliationCut.Results[run.ReconciliationRunId]).ToArray();
            var openBreaks = reconciliationResults.Count(result => result.IsOpenBreak);

            var summary = new FundAccountSummary(
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
                ReconciliationRuns: reconciliationRuns.Length,
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
                WorkflowLabel: BuildWorkflowLabel(account));

            projections.Add(new AccountWorkspaceProjection(summary, latestSnapshot, account.FundId,
                reconciliationRuns, reconciliationResults));
        }

        return projections
            .OrderBy(projection => projection.Summary.AccountType.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(projection => projection.Summary.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<BankAccountSnapshot>> GetBankSnapshotsAsync(
        Guid fundId,
        CancellationToken ct)
    {
        var grouped = await _fundAccountService.GetFundAccountsAsync(fundId, ct).ConfigureAwait(false);
        var bankingAccounts = grouped.BankAccounts
            .Concat(grouped.BrokerageAccounts.Where(static account => account.BankDetails is not null))
            .ToArray();

        var snapshots = new List<BankAccountSnapshot>(bankingAccounts.Length);
        foreach (var account in bankingAccounts)
        {
            ct.ThrowIfCancellationRequested();

            var latestSnapshot = await _fundAccountService
                .GetLatestBalanceSnapshotAsync(account.AccountId, ct)
                .ConfigureAwait(false);
            var bankLines = await _fundAccountService
                .GetBankStatementLinesAsync(account.AccountId, ct: ct)
                .ConfigureAwait(false);
            var latestLine = BankStatementClosingBalanceAuthority.SelectLatestLine(bankLines, account.BaseCurrency);

            snapshots.Add(new BankAccountSnapshot(
                AccountId: account.AccountId,
                DisplayName: account.DisplayName,
                AccountCode: account.AccountCode,
                BankName: account.BankDetails?.BankName ?? account.Institution ?? "Bank account",
                Currency: account.BaseCurrency,
                CurrentBalance: latestSnapshot?.CashBalance ?? BankStatementClosingBalanceAuthority.SelectClosingBalance(bankLines, account.BaseCurrency),
                PendingSettlement: latestSnapshot?.PendingSettlement ?? 0m,
                LastStatementDate: BankStatementClosingBalanceAuthority.LatestStatementDate(bankLines),
                StatementLineCount: bankLines.Count,
                LatestTransactionType: latestLine?.TransactionType,
                LatestTransactionAmount: latestLine?.Amount));
        }

        return snapshots
            .OrderBy(static snapshot => snapshot.BankName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static snapshot => snapshot.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<CashFinancingBuildResult> BuildCashFinancingSummaryAsync(
        string currency,
        IReadOnlyList<AccountWorkspaceProjection> accountProjections,
        IReadOnlyList<StrategyRunEntry> runs,
        CancellationToken ct)
    {
        var totalCash = accountProjections.Sum(static projection => projection.Summary.CashBalance);
        var pendingSettlement = accountProjections.Sum(static projection => projection.LatestSnapshot?.PendingSettlement ?? 0m);
        var financing = 0m;
        var realized = 0m;
        var unrealized = 0m;
        var longMarketValue = 0m;
        var shortMarketValue = 0m;
        var grossExposure = 0m;
        var netExposure = 0m;
        var totalEquity = 0m;
        var contributingRunCount = 0;
        var runSources = new List<PortfolioReportingRunSource>();

        foreach (var run in runs)
        {
            ct.ThrowIfCancellationRequested();
            var portfolio = await _portfolioReadService.BuildSummaryAsync(run, ct).ConfigureAwait(false);
            if (portfolio is null)
            {
                continue;
            }

            contributingRunCount++;
            financing += portfolio.Financing;
            realized += portfolio.RealizedPnl;
            unrealized += portfolio.UnrealizedPnl;
            longMarketValue += portfolio.LongMarketValue;
            shortMarketValue += portfolio.ShortMarketValue;
            grossExposure += portfolio.GrossExposure;
            netExposure += portfolio.NetExposure;
            totalEquity += portfolio.TotalEquity;
            runSources.Add(new PortfolioReportingRunSource(
                run.RunId,
                run.StrategyId,
                run.StrategyName,
                run.FundProfileId,
                run.FundDisplayName,
                portfolio));
        }

        var highlights = new List<string>
        {
            accountProjections.Count == 0
                ? "No linked fund accounts have been configured yet."
                : $"{accountProjections.Count} fund account(s) are contributing banking and custody balances.",
            contributingRunCount == 0
                ? "No recorded fund-scoped runs are contributing portfolio posture yet."
                : $"{contributingRunCount} recorded run(s) are contributing capital posture.",
            financing == 0m
                ? "No financing costs have been recorded for the current fund scope."
                : $"Financing costs total {financing:C2} across linked runs."
        };

        return new CashFinancingBuildResult(
            new CashFinancingSummary(
                Currency: currency,
                TotalCash: totalCash,
                PendingSettlement: pendingSettlement,
                FinancingCost: financing,
                MarginBalance: 0m,
                RealizedPnl: realized,
                UnrealizedPnl: unrealized,
                LongMarketValue: longMarketValue,
                ShortMarketValue: shortMarketValue,
                GrossExposure: grossExposure,
                NetExposure: netExposure,
                TotalEquity: totalEquity,
                Highlights: highlights),
            runSources);
    }

    private sealed record CashFinancingBuildResult(
        CashFinancingSummary Summary,
        IReadOnlyList<PortfolioReportingRunSource> RunSources);

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
