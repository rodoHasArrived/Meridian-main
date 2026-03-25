using Meridian.Contracts.Banking;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;

namespace Meridian.Strategies.Services;

public sealed class ReconciliationProjectionService
{
    public IReadOnlyList<PortfolioLedgerCheckDto> BuildChecks(
        StrategyRunDetail detail,
        ReconciliationRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(request);

        var checks = new List<PortfolioLedgerCheckDto>();
        var portfolio = detail.Portfolio;
        var ledger = detail.Ledger;

        if (portfolio is not null && ledger is not null)
        {
            var ledgerCash = ledger.TrialBalance
                .Where(static line => string.Equals(line.AccountName, "Cash", StringComparison.OrdinalIgnoreCase))
                .Sum(static line => line.Balance);

            checks.Add(CreateAmountCheck("cash-balance", "Portfolio cash vs ledger cash", portfolio.Cash, ledgerCash, portfolio.AsOf, ledger.AsOf));

            var ledgerNetEquity = ledger.AssetBalance - ledger.LiabilityBalance;
            checks.Add(CreateAmountCheck("net-equity", "Portfolio total equity vs ledger net assets", portfolio.TotalEquity, ledgerNetEquity, portfolio.AsOf, ledger.AsOf));
        }

        var portfolioPositions = portfolio?.Positions ?? [];
        var longSymbols = new HashSet<string>(
            portfolioPositions.Where(static position => !position.IsShort).Select(static position => position.Symbol),
            StringComparer.OrdinalIgnoreCase);
        var shortSymbols = new HashSet<string>(
            portfolioPositions.Where(static position => position.IsShort).Select(static position => position.Symbol),
            StringComparer.OrdinalIgnoreCase);

        var ledgerTrialBalance = ledger?.TrialBalance ?? [];
        var ledgerLongSymbols = new HashSet<string>(
            ledgerTrialBalance
                .Where(static line => string.Equals(line.AccountName, "Securities", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(line.Symbol))
                .Select(static line => line.Symbol!),
            StringComparer.OrdinalIgnoreCase);
        var ledgerShortSymbols = new HashSet<string>(
            ledgerTrialBalance
                .Where(static line => string.Equals(line.AccountName, "Short Securities Payable", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(line.Symbol))
                .Select(static line => line.Symbol!),
            StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in longSymbols.Order(StringComparer.OrdinalIgnoreCase))
        {
            checks.Add(CreateCoverageCheck($"long-{symbol}", $"Long position coverage for {symbol}", true, ledgerLongSymbols.Contains(symbol), portfolio?.AsOf, ledger?.AsOf, "long", "ledger", ledgerShortSymbols.Contains(symbol) ? "short" : "long"));
        }

        foreach (var symbol in shortSymbols.Order(StringComparer.OrdinalIgnoreCase))
        {
            checks.Add(CreateCoverageCheck($"short-{symbol}", $"Short position coverage for {symbol}", true, ledgerShortSymbols.Contains(symbol), portfolio?.AsOf, ledger?.AsOf, "short", "ledger", ledgerLongSymbols.Contains(symbol) ? "long" : "short"));
        }

        foreach (var symbol in ledgerLongSymbols.Except(longSymbols, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            checks.Add(CreateCoverageCheck($"ledger-long-{symbol}", $"Ledger long coverage without portfolio position for {symbol}", false, true, portfolio?.AsOf, ledger?.AsOf, "long", "portfolio", "long"));
        }

        foreach (var symbol in ledgerShortSymbols.Except(shortSymbols, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            checks.Add(CreateCoverageCheck($"ledger-short-{symbol}", $"Ledger short coverage without portfolio position for {symbol}", false, true, portfolio?.AsOf, ledger?.AsOf, "short", "portfolio", "short"));
        }

        if (portfolio is not null && ledger is null)
        {
            checks.Add(CreateCoverageCheck("ledger-summary-missing", "Ledger summary coverage", true, false, portfolio.AsOf, null, "summary", "ledger", string.Empty));
        }

        if (portfolio is null && ledger is not null)
        {
            checks.Add(CreateCoverageCheck("portfolio-summary-missing", "Portfolio summary coverage", false, true, null, ledger.AsOf, "summary", "portfolio", "summary"));
        }

        return checks;
    }

    private static PortfolioLedgerCheckDto CreateAmountCheck(
        string checkId,
        string label,
        decimal expectedAmount,
        decimal actualAmount,
        DateTimeOffset? expectedAsOf,
        DateTimeOffset? actualAsOf,
        string expectedSource = "portfolio",
        string actualSource = "ledger") =>
        new()
        {
            CheckId = checkId,
            Label = label,
            ExpectedSource = expectedSource,
            ActualSource = actualSource,
            ExpectedAmount = expectedAmount,
            ActualAmount = actualAmount,
            HasExpectedAmount = true,
            HasActualAmount = true,
            ExpectedPresent = true,
            ActualPresent = true,
            ExpectedAsOf = expectedAsOf ?? default,
            ActualAsOf = actualAsOf ?? default,
            HasExpectedAsOf = expectedAsOf.HasValue,
            HasActualAsOf = actualAsOf.HasValue,
            CategoryHint = "amount",
            MissingSourceHint = string.Empty,
            ActualKind = "amount"
        };

    private static PortfolioLedgerCheckDto CreateCoverageCheck(
        string checkId,
        string label,
        bool expectedPresent,
        bool actualPresent,
        DateTimeOffset? expectedAsOf,
        DateTimeOffset? actualAsOf,
        string categoryHint,
        string missingSourceHint,
        string actualKind) =>
        new()
        {
            CheckId = checkId,
            Label = label,
            ExpectedSource = "portfolio",
            ActualSource = "ledger",
            ExpectedAmount = 0m,
            ActualAmount = 0m,
            HasExpectedAmount = false,
            HasActualAmount = false,
            ExpectedPresent = expectedPresent,
            ActualPresent = actualPresent,
            ExpectedAsOf = expectedAsOf ?? default,
            ActualAsOf = actualAsOf ?? default,
            HasExpectedAsOf = expectedAsOf.HasValue,
            HasActualAsOf = actualAsOf.HasValue,
            CategoryHint = categoryHint,
            MissingSourceHint = missingSourceHint,
            ActualKind = actualKind
        };

    /// <summary>
    /// Build reconciliation checks that compare bank-side transaction totals against
    /// the internal ledger's cash balance.  Returns an empty list when both sides have
    /// no data.
    /// </summary>
    public IReadOnlyList<PortfolioLedgerCheckDto> BuildBankingChecks(
        IReadOnlyList<BankTransactionDto> bankTransactions,
        LedgerSummary? ledger)
    {
        ArgumentNullException.ThrowIfNull(bankTransactions);

        var activeTxns = bankTransactions.Where(static t => !t.IsVoided).ToArray();
        var hasBankData = activeTxns.Length > 0;
        var bankNetAmount = activeTxns.Sum(static t => t.Amount);

        var hasLedgerData = ledger is not null;
        var ledgerCash = ledger?.TrialBalance
            .Where(static l => string.Equals(l.AccountName, "Cash", StringComparison.OrdinalIgnoreCase))
            .Sum(static l => l.Balance) ?? 0m;

        if (!hasBankData && !hasLedgerData)
        {
            return Array.Empty<PortfolioLedgerCheckDto>();
        }

        if (hasBankData && hasLedgerData)
        {
            return
            [
                CreateAmountCheck(
                    "bank-net-vs-ledger-cash",
                    "Bank net transactions vs ledger cash",
                    bankNetAmount,
                    ledgerCash,
                    null,
                    ledger!.AsOf,
                    expectedSource: "bank",
                    actualSource: "ledger")
            ];
        }

        if (hasBankData)
        {
            // Bank has transactions but no internal ledger exists
            return
            [
                CreateCoverageCheck(
                    "bank-ledger-coverage-missing",
                    "Ledger coverage for bank transactions",
                    expectedPresent: true,
                    actualPresent: false,
                    null, null,
                    categoryHint: "bank",
                    missingSourceHint: "ledger",
                    actualKind: "ledger")
            ];
        }

        // Ledger exists but no bank transactions recorded for this entity
        return
        [
            CreateCoverageCheck(
                "bank-coverage-missing",
                "Bank transaction coverage for ledger",
                expectedPresent: false,
                actualPresent: true,
                null, ledger!.AsOf,
                categoryHint: "bank",
                missingSourceHint: "bank",
                actualKind: "ledger")
        ];
    }
}
