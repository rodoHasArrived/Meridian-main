using Meridian.Contracts.AccountingSystem;
using Meridian.ProviderSdk.AccountingSystem;

namespace Meridian.DataIntegration.AccountingSystem.Fixtures;

public sealed class XeroFixtureAccountingProvider : IAccountingSystemProvider
{
    public const string Id = "xero-fixture";

    public string ProviderId => Id;

    public string DisplayName => "Xero Fixture";

    public AccountingSystemProviderCapabilities Capabilities { get; } = new(
        SupportsChartOfAccounts: true,
        SupportsJournalEntries: true,
        SupportsTrialBalance: true,
        SupportsPosting: false,
        EvidenceKinds:
        [
            "XeroAccount",
            "XeroManualJournal",
            "XeroTrialBalance"
        ]);

    public Task<AccountingSystemImportDetailDto> ImportAsync(
        AccountingSystemImportRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var periodStart = request.PeriodStart ?? new DateOnly(2026, 1, 1);
        var periodEnd = request.PeriodEnd ?? new DateOnly(2026, 1, 31);
        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var chart = new[]
        {
            new AccountingSystemChartAccountDto("xero-bank-001", "090", "Operating Bank", "Asset", "USD", true, EvidenceRef: "xero-fixture:account:xero-bank-001"),
            new AccountingSystemChartAccountDto("xero-invest-001", "620", "Investment Portfolio", "Asset", "USD", true, EvidenceRef: "xero-fixture:account:xero-invest-001"),
            new AccountingSystemChartAccountDto("xero-income-001", "200", "Investment Revenue", "Revenue", "USD", true, EvidenceRef: "xero-fixture:account:xero-income-001"),
            new AccountingSystemChartAccountDto("xero-expense-001", "404", "Trading Costs", "Expense", "USD", true, EvidenceRef: "xero-fixture:account:xero-expense-001")
        };
        var journal = new[]
        {
            new AccountingSystemJournalEntryDto(
                "xero-mj-100",
                periodStart.AddDays(5),
                "Xero fixture investor contribution",
                "USD",
                TotalDebits: 180_000m,
                TotalCredits: 180_000m,
                Lines:
                [
                    new AccountingSystemJournalLineDto("xero-mj-100-1", "xero-bank-001", "090", "Contribution received", 180_000m, 0m, "USD", "xero-fixture:journal:xero-mj-100:1"),
                    new AccountingSystemJournalLineDto("xero-mj-100-2", "xero-income-001", "200", "Contribution offset", 0m, 180_000m, "USD", "xero-fixture:journal:xero-mj-100:2")
                ],
                EvidenceRef: "xero-fixture:journal:xero-mj-100"),
            new AccountingSystemJournalEntryDto(
                "xero-mj-101",
                periodStart.AddDays(19),
                "Xero fixture trading costs",
                "USD",
                TotalDebits: 950m,
                TotalCredits: 950m,
                Lines:
                [
                    new AccountingSystemJournalLineDto("xero-mj-101-1", "xero-expense-001", "404", "Trading costs recognized", 950m, 0m, "USD", "xero-fixture:journal:xero-mj-101:1"),
                    new AccountingSystemJournalLineDto("xero-mj-101-2", "xero-bank-001", "090", "Bank payment", 0m, 950m, "USD", "xero-fixture:journal:xero-mj-101:2")
                ],
                EvidenceRef: "xero-fixture:journal:xero-mj-101")
        };
        var trialBalance = new[]
        {
            new AccountingSystemTrialBalanceLineDto("xero-bank-001", "090", "Operating Bank", "Asset", 179_050m, 0m, "USD", periodEnd, "xero-fixture:trial-balance:xero-bank-001"),
            new AccountingSystemTrialBalanceLineDto("xero-invest-001", "620", "Investment Portfolio", "Asset", 0m, 0m, "USD", periodEnd, "xero-fixture:trial-balance:xero-invest-001"),
            new AccountingSystemTrialBalanceLineDto("xero-income-001", "200", "Investment Revenue", "Revenue", 0m, 180_000m, "USD", periodEnd, "xero-fixture:trial-balance:xero-income-001"),
            new AccountingSystemTrialBalanceLineDto("xero-expense-001", "404", "Trading Costs", "Expense", 950m, 0m, "USD", periodEnd, "xero-fixture:trial-balance:xero-expense-001")
        };

        return Task.FromResult(BuildImportDetail(
            providerId: ProviderId,
            displayName: DisplayName,
            importId: $"xero-fixture-{periodEnd:yyyyMMdd}",
            fundProfileId,
            request.LedgerBookId,
            request.PersistPreview,
            periodStart,
            periodEnd,
            chart,
            journal,
            trialBalance,
            "Xero fixture data is read-only and does not represent a live Xero tenant."));
    }

    private static string NormalizeFundProfileId(string? fundProfileId)
        => string.IsNullOrWhiteSpace(fundProfileId) ? "default-fund" : fundProfileId.Trim();

    private static AccountingSystemImportDetailDto BuildImportDetail(
        string providerId,
        string displayName,
        string importId,
        string fundProfileId,
        Guid? ledgerBookId,
        bool persistPreview,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<AccountingSystemChartAccountDto> chart,
        IReadOnlyList<AccountingSystemJournalEntryDto> journal,
        IReadOnlyList<AccountingSystemTrialBalanceLineDto> trialBalance,
        string fixtureWarning)
    {
        var summary = new AccountingSystemImportSummaryDto(
            importId,
            providerId,
            displayName,
            fundProfileId,
            ledgerBookId,
            persistPreview ? AccountingSystemImportStateDto.Imported : AccountingSystemImportStateDto.Previewed,
            periodStart,
            periodEnd,
            DateTimeOffset.UtcNow,
            chart.Count,
            journal.Count,
            trialBalance.Count,
            EvidenceReferences:
            [
                $"{providerId}:chart-of-accounts",
                $"{providerId}:journal",
                $"{providerId}:trial-balance"
            ],
            Warnings:
            [
                fixtureWarning,
                "Meridian remains the source of all ledger truth; external posting/export is disabled for the import-first evidence lane."
            ]);

        return new AccountingSystemImportDetailDto(summary, chart, journal, trialBalance);
    }
}

public sealed class NetSuiteFixtureAccountingProvider : IAccountingSystemProvider
{
    public const string Id = "netsuite-fixture";

    public string ProviderId => Id;

    public string DisplayName => "NetSuite Fixture";

    public AccountingSystemProviderCapabilities Capabilities { get; } = new(
        SupportsChartOfAccounts: true,
        SupportsJournalEntries: true,
        SupportsTrialBalance: true,
        SupportsPosting: false,
        EvidenceKinds:
        [
            "NetSuiteAccount",
            "NetSuiteJournalEntry",
            "NetSuiteTrialBalance"
        ]);

    public Task<AccountingSystemImportDetailDto> ImportAsync(
        AccountingSystemImportRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var periodStart = request.PeriodStart ?? new DateOnly(2026, 1, 1);
        var periodEnd = request.PeriodEnd ?? new DateOnly(2026, 1, 31);
        var fundProfileId = string.IsNullOrWhiteSpace(request.FundProfileId)
            ? "default-fund"
            : request.FundProfileId.Trim();
        var chart = new[]
        {
            new AccountingSystemChartAccountDto("ns-1000", "1000", "Operating Cash", "Asset", "USD", true, EvidenceRef: "netsuite-fixture:account:ns-1000"),
            new AccountingSystemChartAccountDto("ns-1200", "1200", "Investments", "Asset", "USD", true, EvidenceRef: "netsuite-fixture:account:ns-1200"),
            new AccountingSystemChartAccountDto("ns-4100", "4100", "Investment Income", "Income", "USD", true, EvidenceRef: "netsuite-fixture:account:ns-4100"),
            new AccountingSystemChartAccountDto("ns-7100", "7100", "Trading Expense", "Expense", "USD", true, EvidenceRef: "netsuite-fixture:account:ns-7100")
        };
        var journal = new[]
        {
            new AccountingSystemJournalEntryDto(
                "ns-je-100",
                periodStart.AddDays(6),
                "NetSuite fixture contribution",
                "USD",
                TotalDebits: 310_000m,
                TotalCredits: 310_000m,
                Lines:
                [
                    new AccountingSystemJournalLineDto("ns-je-100-1", "ns-1000", "1000", "Contribution receipt", 310_000m, 0m, "USD", "netsuite-fixture:journal:ns-je-100:1"),
                    new AccountingSystemJournalLineDto("ns-je-100-2", "ns-4100", "4100", "Contribution offset", 0m, 310_000m, "USD", "netsuite-fixture:journal:ns-je-100:2")
                ],
                EvidenceRef: "netsuite-fixture:journal:ns-je-100"),
            new AccountingSystemJournalEntryDto(
                "ns-je-101",
                periodStart.AddDays(21),
                "NetSuite fixture trading expense",
                "USD",
                TotalDebits: 1_875m,
                TotalCredits: 1_875m,
                Lines:
                [
                    new AccountingSystemJournalLineDto("ns-je-101-1", "ns-7100", "7100", "Trading expense accrual", 1_875m, 0m, "USD", "netsuite-fixture:journal:ns-je-101:1"),
                    new AccountingSystemJournalLineDto("ns-je-101-2", "ns-1000", "1000", "Cash settlement", 0m, 1_875m, "USD", "netsuite-fixture:journal:ns-je-101:2")
                ],
                EvidenceRef: "netsuite-fixture:journal:ns-je-101")
        };
        var trialBalance = new[]
        {
            new AccountingSystemTrialBalanceLineDto("ns-1000", "1000", "Operating Cash", "Asset", 308_125m, 0m, "USD", periodEnd, "netsuite-fixture:trial-balance:ns-1000"),
            new AccountingSystemTrialBalanceLineDto("ns-1200", "1200", "Investments", "Asset", 0m, 0m, "USD", periodEnd, "netsuite-fixture:trial-balance:ns-1200"),
            new AccountingSystemTrialBalanceLineDto("ns-4100", "4100", "Investment Income", "Income", 0m, 310_000m, "USD", periodEnd, "netsuite-fixture:trial-balance:ns-4100"),
            new AccountingSystemTrialBalanceLineDto("ns-7100", "7100", "Trading Expense", "Expense", 1_875m, 0m, "USD", periodEnd, "netsuite-fixture:trial-balance:ns-7100")
        };

        return Task.FromResult(BuildImportDetail(
            providerId: ProviderId,
            displayName: DisplayName,
            importId: $"netsuite-fixture-{periodEnd:yyyyMMdd}",
            fundProfileId,
            request.LedgerBookId,
            request.PersistPreview,
            periodStart,
            periodEnd,
            chart,
            journal,
            trialBalance,
            "NetSuite fixture data is read-only and does not represent a live NetSuite account."));
    }

    private static AccountingSystemImportDetailDto BuildImportDetail(
        string providerId,
        string displayName,
        string importId,
        string fundProfileId,
        Guid? ledgerBookId,
        bool persistPreview,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<AccountingSystemChartAccountDto> chart,
        IReadOnlyList<AccountingSystemJournalEntryDto> journal,
        IReadOnlyList<AccountingSystemTrialBalanceLineDto> trialBalance,
        string fixtureWarning)
    {
        var summary = new AccountingSystemImportSummaryDto(
            importId,
            providerId,
            displayName,
            fundProfileId,
            ledgerBookId,
            persistPreview ? AccountingSystemImportStateDto.Imported : AccountingSystemImportStateDto.Previewed,
            periodStart,
            periodEnd,
            DateTimeOffset.UtcNow,
            chart.Count,
            journal.Count,
            trialBalance.Count,
            EvidenceReferences:
            [
                $"{providerId}:chart-of-accounts",
                $"{providerId}:journal",
                $"{providerId}:trial-balance"
            ],
            Warnings:
            [
                fixtureWarning,
                "Meridian remains the source of all ledger truth; external posting/export is disabled for the import-first evidence lane."
            ]);

        return new AccountingSystemImportDetailDto(summary, chart, journal, trialBalance);
    }
}
