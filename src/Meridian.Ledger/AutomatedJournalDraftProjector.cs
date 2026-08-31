using static Meridian.Contracts.Text.TextPrimitives;
namespace Meridian.Ledger;

/// <summary>
/// Creates balanced journal drafts for common automated accounting events.
/// </summary>
public static class AutomatedJournalDraftProjector
{
    /// <summary>
    /// Projects a normalized economic event into balanced journal lines and audit metadata.
    /// </summary>
    public static AutomatedJournalDraft Project(AutomatedJournalEvent journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        ValidateAmount(journalEvent.Amount);

        var symbol = NormalizeSymbol(journalEvent.Symbol);
        var financialAccountId = NormalizeOptional(journalEvent.FinancialAccountId);
        var description = string.IsNullOrWhiteSpace(journalEvent.Description)
            ? DefaultDescription(journalEvent.Kind, symbol)
            : journalEvent.Description.Trim();
        // Automated economic events (dividends, fees, ...) are scoped by financial account on the
        // ledger account itself, not by a dimensional line scope, so their draft lines carry none.
        var lines = ProjectLines(journalEvent.Kind, symbol, journalEvent.Amount, financialAccountId)
            .Select(static line => (line.account, line.debit, line.credit, (LedgerLineDimensionSet?)null))
            .ToArray();
        var metadata = new JournalEntryMetadata(
            ActivityType: journalEvent.Kind.ToString(),
            Symbol: symbol,
            SecurityId: journalEvent.SecurityId,
            FinancialAccountId: financialAccountId,
            EffectiveDate: journalEvent.EffectiveDate ?? DateOnly.FromDateTime(journalEvent.Timestamp.UtcDateTime),
            IdempotencyKey: NormalizeOptional(journalEvent.IdempotencyKey),
            Tags: BuildTags(journalEvent.SourceEventId),
            EvidenceReferences: journalEvent.EvidenceReferences.Select(static item => item.Normalize()).ToArray());

        return new AutomatedJournalDraft(journalEvent with { Symbol = symbol, FinancialAccountId = financialAccountId }, description, lines, metadata);
    }

    private static IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> ProjectLines(
        AutomatedJournalEventKind kind,
        string symbol,
        decimal amount,
        string? financialAccountId)
    {
        var cash = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.Cash
            : LedgerAccounts.CashAccount(financialAccountId);
        var dividendIncome = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.DividendIncome
            : LedgerAccounts.DividendIncomeFor(financialAccountId);
        var cashInterestIncome = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.CashInterestIncome
            : LedgerAccounts.CashInterestIncomeFor(financialAccountId);
        var corporateActionIncome = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.CorporateActionIncome
            : LedgerAccounts.CorporateActionIncomeFor(financialAccountId);
        var corporateActionExpense = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.CorporateActionExpense
            : LedgerAccounts.CorporateActionExpenseFor(financialAccountId);
        var dividendReceivable = LedgerAccounts.DividendReceivable(symbol, financialAccountId);
        var accrualScope = financialAccountId ?? symbol;
        var managementFeeExpense = LedgerAccounts.ManagementFeeExpenseFor(accrualScope);
        var managementFeePayable = LedgerAccounts.ManagementFeePayableFor(accrualScope);
        var performanceFeeExpense = LedgerAccounts.PerformanceFeeExpenseFor(accrualScope);
        var performanceFeePayable = LedgerAccounts.PerformanceFeePayableFor(accrualScope);
        var commissionExpense = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.CommissionExpense
            : LedgerAccounts.CommissionExpenseFor(financialAccountId);
        var commissionPayable = LedgerAccounts.CommissionPayableFor(accrualScope);
        var withholdingTaxExpense = LedgerAccounts.WithholdingTaxExpenseFor(accrualScope);
        var withholdingTaxPayable = LedgerAccounts.WithholdingTaxPayableFor(accrualScope);

        return kind switch
        {
            AutomatedJournalEventKind.DividendDeclared =>
            [
                (dividendReceivable, amount, 0m),
                (dividendIncome, 0m, amount),
            ],
            AutomatedJournalEventKind.DividendReceived =>
            [
                (cash, amount, 0m),
                (dividendReceivable, 0m, amount),
            ],
            AutomatedJournalEventKind.CashInterestCredited =>
            [
                (cash, amount, 0m),
                (cashInterestIncome, 0m, amount),
            ],
            AutomatedJournalEventKind.CorporateActionIncome =>
            [
                (cash, amount, 0m),
                (corporateActionIncome, 0m, amount),
            ],
            AutomatedJournalEventKind.CorporateActionExpense =>
            [
                (corporateActionExpense, amount, 0m),
                (cash, 0m, amount),
            ],
            AutomatedJournalEventKind.ManagementFeeAccrued =>
            [
                (managementFeeExpense, amount, 0m),
                (managementFeePayable, 0m, amount),
            ],
            AutomatedJournalEventKind.PerformanceFeeAccrued =>
            [
                (performanceFeeExpense, amount, 0m),
                (performanceFeePayable, 0m, amount),
            ],
            AutomatedJournalEventKind.CommissionAccrued =>
            [
                (commissionExpense, amount, 0m),
                (commissionPayable, 0m, amount),
            ],
            AutomatedJournalEventKind.WithholdingTaxAccrued =>
            [
                (withholdingTaxExpense, amount, 0m),
                (withholdingTaxPayable, 0m, amount),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported automated journal event kind."),
        };
    }

    private static IReadOnlyDictionary<string, string>? BuildTags(string? sourceEventId)
        => string.IsNullOrWhiteSpace(sourceEventId)
            ? null
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sourceEventId"] = sourceEventId.Trim(),
            };

    private static string DefaultDescription(AutomatedJournalEventKind kind, string symbol)
        => kind switch
        {
            AutomatedJournalEventKind.DividendDeclared => $"Dividend declared for {symbol}",
            AutomatedJournalEventKind.DividendReceived => $"Dividend received for {symbol}",
            AutomatedJournalEventKind.CashInterestCredited => $"Cash interest credited for {symbol}",
            AutomatedJournalEventKind.CorporateActionIncome => $"Corporate action income for {symbol}",
            AutomatedJournalEventKind.CorporateActionExpense => $"Corporate action expense for {symbol}",
            AutomatedJournalEventKind.ManagementFeeAccrued => $"Management fee accrued for {symbol}",
            AutomatedJournalEventKind.PerformanceFeeAccrued => $"Performance fee accrued for {symbol}",
            AutomatedJournalEventKind.CommissionAccrued => $"Commission accrued for {symbol}",
            AutomatedJournalEventKind.WithholdingTaxAccrued => $"Withholding tax accrued for {symbol}",
            _ => $"Automated journal event for {symbol}",
        };

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Automated journal event amount must be positive.");
    }

    private static string NormalizeSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim().ToUpperInvariant();
    }
}
