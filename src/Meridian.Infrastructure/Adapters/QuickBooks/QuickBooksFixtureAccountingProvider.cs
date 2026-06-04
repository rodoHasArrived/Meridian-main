using Meridian.Contracts.AccountingSystem;
using Meridian.ProviderSdk.AccountingSystem;

namespace Meridian.Infrastructure.Adapters.QuickBooks;

public sealed class QuickBooksFixtureAccountingProvider : IAccountingSystemProvider
{
    public const string Id = "quickbooks-fixture";

    public string ProviderId => Id;

    public string DisplayName => "QuickBooks Fixture";

    public AccountingSystemProviderCapabilities Capabilities { get; } = new(
        SupportsChartOfAccounts: true,
        SupportsJournalEntries: true,
        SupportsTrialBalance: true,
        SupportsPosting: false,
        EvidenceKinds:
        [
            "QuickBooksAccount",
            "QuickBooksJournalEntry",
            "QuickBooksTrialBalance"
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
        var importId = $"qbo-fixture-{periodEnd:yyyyMMdd}";

        var chart = new[]
        {
            new AccountingSystemChartAccountDto("qbo-1000", "Assets:Cash:Operating", "Operating Cash", "Asset", "USD", true, EvidenceRef: "quickbooks-fixture:account:qbo-1000"),
            new AccountingSystemChartAccountDto("qbo-1500", "Assets:Investments:Public", "Public Investments", "Asset", "USD", true, EvidenceRef: "quickbooks-fixture:account:qbo-1500"),
            new AccountingSystemChartAccountDto("qbo-4000", "Income:Investment", "Investment Income", "Income", "USD", true, EvidenceRef: "quickbooks-fixture:account:qbo-4000"),
            new AccountingSystemChartAccountDto("qbo-6100", "Expenses:Trading", "Trading Expenses", "Expense", "USD", true, EvidenceRef: "quickbooks-fixture:account:qbo-6100")
        };

        var journal = new[]
        {
            new AccountingSystemJournalEntryDto(
                "qbo-je-100",
                periodStart.AddDays(4),
                "Fixture capital contribution",
                "USD",
                TotalDebits: 250_000m,
                TotalCredits: 250_000m,
                Lines:
                [
                    new AccountingSystemJournalLineDto("qbo-je-100-1", "qbo-1000", "Assets:Cash:Operating", "Capital contribution received", 250_000m, 0m, "USD", "quickbooks-fixture:journal:qbo-je-100:1"),
                    new AccountingSystemJournalLineDto("qbo-je-100-2", "qbo-4000", "Income:Investment", "Capital contribution offset", 0m, 250_000m, "USD", "quickbooks-fixture:journal:qbo-je-100:2")
                ],
                EvidenceRef: "quickbooks-fixture:journal:qbo-je-100"),
            new AccountingSystemJournalEntryDto(
                "qbo-je-101",
                periodStart.AddDays(16),
                "Fixture trading fee accrual",
                "USD",
                TotalDebits: 1_250m,
                TotalCredits: 1_250m,
                Lines:
                [
                    new AccountingSystemJournalLineDto("qbo-je-101-1", "qbo-6100", "Expenses:Trading", "Trading fee expense", 1_250m, 0m, "USD", "quickbooks-fixture:journal:qbo-je-101:1"),
                    new AccountingSystemJournalLineDto("qbo-je-101-2", "qbo-1000", "Assets:Cash:Operating", "Cash fee payment", 0m, 1_250m, "USD", "quickbooks-fixture:journal:qbo-je-101:2")
                ],
                EvidenceRef: "quickbooks-fixture:journal:qbo-je-101")
        };

        var trialBalance = new[]
        {
            new AccountingSystemTrialBalanceLineDto("qbo-1000", "Assets:Cash:Operating", "Operating Cash", "Asset", 248_750m, 0m, "USD", periodEnd, "quickbooks-fixture:trial-balance:qbo-1000"),
            new AccountingSystemTrialBalanceLineDto("qbo-1500", "Assets:Investments:Public", "Public Investments", "Asset", 0m, 0m, "USD", periodEnd, "quickbooks-fixture:trial-balance:qbo-1500"),
            new AccountingSystemTrialBalanceLineDto("qbo-4000", "Income:Investment", "Investment Income", "Income", 0m, 250_000m, "USD", periodEnd, "quickbooks-fixture:trial-balance:qbo-4000"),
            new AccountingSystemTrialBalanceLineDto("qbo-6100", "Expenses:Trading", "Trading Expenses", "Expense", 1_250m, 0m, "USD", periodEnd, "quickbooks-fixture:trial-balance:qbo-6100")
        };

        var summary = new AccountingSystemImportSummaryDto(
            importId,
            ProviderId,
            DisplayName,
            fundProfileId,
            request.LedgerBookId,
            request.PersistPreview ? AccountingSystemImportStateDto.Imported : AccountingSystemImportStateDto.Previewed,
            periodStart,
            periodEnd,
            DateTimeOffset.UtcNow,
            chart.Length,
            journal.Length,
            trialBalance.Length,
            EvidenceReferences:
            [
                "quickbooks-fixture:chart-of-accounts",
                "quickbooks-fixture:journal",
                "quickbooks-fixture:trial-balance"
            ],
            Warnings:
            [
                "Fixture data is read-only and does not represent a live QuickBooks Online company.",
                "Meridian remains the source of all ledger truth; external posting/export is disabled for the contract-first evidence lane."
            ]);

        return Task.FromResult(new AccountingSystemImportDetailDto(summary, chart, journal, trialBalance));
    }
}
