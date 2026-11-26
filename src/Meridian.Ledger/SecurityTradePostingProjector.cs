namespace Meridian.Ledger;

/// <summary>
/// Shared double-entry projection for simulated, paper, and durable execution fills.
/// Callers supply retained lot economics; this service does not select lots or infer position direction.
/// </summary>
public static class SecurityTradePostingProjector
{
    public static IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> Project(
        SecurityTradePosting posting,
        string symbol,
        string? financialAccountId = null,
        IChartOfAccounts? chart = null)
    {
        ArgumentNullException.ThrowIfNull(posting);
        Validate(posting);
        chart ??= StandardChartOfAccounts.Instance;
        var cash = chart.Resolve(LedgerAccountRole.Cash, financialAccountId);
        var securities = chart.Resolve(LedgerAccountRole.Securities, financialAccountId, symbol);
        var lines = new List<(LedgerAccount Account, decimal Debit, decimal Credit)>(4);

        void Add(LedgerAccount account, decimal debit, decimal credit)
        {
            if (debit != 0m || credit != 0m)
                lines.Add((account, debit, credit));
        }

        void Cash(decimal signedAmount)
        {
            if (signedAmount >= 0m)
                Add(cash, signedAmount, 0m);
            else
                Add(cash, 0m, -signedAmount);
        }

        void Result(decimal realized)
        {
            if (realized > 0m)
                Add(chart.Resolve(LedgerAccountRole.RealizedGain, financialAccountId), 0m, realized);
            else if (realized < 0m)
                Add(chart.Resolve(LedgerAccountRole.RealizedLoss, financialAccountId), -realized, 0m);
        }

        switch (posting.Kind)
        {
            case SecurityTradePostingKind.OpenLong:
                Add(securities, posting.GrossValue, 0m);
                Cash(-(posting.GrossValue - posting.BorrowedAmount));
                if (posting.BorrowedAmount > 0m)
                    Add(chart.Resolve(LedgerAccountRole.MarginLoanPayable, financialAccountId), 0m, posting.BorrowedAmount);
                break;
            case SecurityTradePostingKind.CloseLong:
                // A severe-loss margin sale can repay more debt than its proceeds. Cash then
                // decreases; represent that as a credit rather than a negative debit.
                Cash(posting.GrossValue - posting.LoanRepaid);
                if (posting.LoanRepaid > 0m)
                    Add(chart.Resolve(LedgerAccountRole.MarginLoanPayable, financialAccountId), posting.LoanRepaid, 0m);
                if (posting.RealizedPnl < 0m)
                    Result(posting.RealizedPnl);
                Add(securities, 0m, posting.CarryingValue!.Value);
                if (posting.RealizedPnl > 0m)
                    Result(posting.RealizedPnl);
                break;
            case SecurityTradePostingKind.OpenShort:
                Cash(posting.GrossValue);
                Add(chart.Resolve(LedgerAccountRole.ShortSecuritiesPayable, financialAccountId, symbol), 0m, posting.GrossValue);
                break;
            case SecurityTradePostingKind.CoverShort:
                Add(chart.Resolve(LedgerAccountRole.ShortSecuritiesPayable, financialAccountId, symbol), posting.CarryingValue!.Value, 0m);
                if (posting.RealizedPnl < 0m)
                    Result(posting.RealizedPnl);
                Cash(-posting.GrossValue);
                if (posting.RealizedPnl > 0m)
                    Result(posting.RealizedPnl);
                break;
        }

        if (lines.Sum(line => line.Debit) != lines.Sum(line => line.Credit))
            throw new ArgumentException("Retained trade amounts do not produce a balanced journal at decimal precision.", nameof(posting));
        return lines;
    }

    /// <summary>Validates exact trade facts without resolving or mutating any accounts.</summary>
    public static void Validate(SecurityTradePosting posting)
    {
        ArgumentNullException.ThrowIfNull(posting);
        if (!Enum.IsDefined(posting.Kind))
            throw new ArgumentOutOfRangeException(nameof(posting), "Security trade posting kind must be defined.");
        if (posting.Quantity <= 0m || posting.GrossValue < 0m)
            throw new ArgumentException("Security trade quantity must be positive and gross value cannot be negative.", nameof(posting));
        if (posting.Kind is SecurityTradePostingKind.OpenLong or SecurityTradePostingKind.OpenShort
            && posting.GrossValue == 0m)
            throw new ArgumentException("An opening trade must have positive gross value.", nameof(posting));
        if (posting.CarryingValue < 0m || posting.BorrowedAmount < 0m || posting.LoanRepaid < 0m)
            throw new ArgumentException("Security trade basis and financing amounts cannot be negative.", nameof(posting));
        if (posting.Kind is SecurityTradePostingKind.OpenLong or SecurityTradePostingKind.OpenShort
            && posting.CarryingValue is not (null or 0m))
            throw new ArgumentException("An opening trade cannot relieve carrying value.", nameof(posting));
        if (posting.Kind is SecurityTradePostingKind.CloseLong or SecurityTradePostingKind.CoverShort
            && posting.CarryingValue is null)
            throw new ArgumentException("A closing trade requires explicit retained carrying value.", nameof(posting));
        if (posting.Kind != SecurityTradePostingKind.OpenLong && posting.BorrowedAmount != 0m)
            throw new ArgumentException("Only a long opening can borrow cash.", nameof(posting));
        if (posting.BorrowedAmount > posting.GrossValue)
            throw new ArgumentException("Borrowed cash cannot exceed the acquisition value.", nameof(posting));
        if (posting.Kind != SecurityTradePostingKind.CloseLong && posting.LoanRepaid != 0m)
            throw new ArgumentException("Only a long close can repay acquisition debt.", nameof(posting));
    }

    public static IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> Commission(
        decimal amount,
        string? financialAccountId = null,
        IChartOfAccounts? chart = null)
    {
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Transaction charges cannot be negative.");
        if (amount == 0m)
            return [];
        chart ??= StandardChartOfAccounts.Instance;
        return
        [
            (chart.Resolve(LedgerAccountRole.CommissionExpense, financialAccountId), amount, 0m),
            (chart.Resolve(LedgerAccountRole.Cash, financialAccountId), 0m, amount)
        ];
    }

    public static IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> VariationMargin(
        string symbol,
        decimal amount,
        string? financialAccountId = null,
        IChartOfAccounts? chart = null)
    {
        if (amount == 0m)
            return [];
        chart ??= StandardChartOfAccounts.Instance;
        var cash = chart.Resolve(LedgerAccountRole.Cash, financialAccountId);
        var settlement = chart.Resolve(LedgerAccountRole.FuturesMtmSettlement, financialAccountId, symbol);
        return amount > 0m
            ? [(cash, amount, 0m), (settlement, 0m, amount)]
            : [(settlement, -amount, 0m), (cash, 0m, -amount)];
    }

    public static IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> ShortCollateral(
        decimal amount,
        bool release,
        string? financialAccountId = null,
        IChartOfAccounts? chart = null)
    {
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Short collateral cannot be negative.");
        if (amount == 0m)
            return [];
        chart ??= StandardChartOfAccounts.Instance;
        var cash = chart.Resolve(LedgerAccountRole.Cash, financialAccountId);
        var collateral = chart.Resolve(LedgerAccountRole.ShortSaleCollateralReceivable, financialAccountId);
        return release
            ? [(cash, amount, 0m), (collateral, 0m, amount)]
            : [(collateral, amount, 0m), (cash, 0m, amount)];
    }

    /// <summary>Projects the financing cash movement separately from the security trade for statement attribution.</summary>
    public static IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> MarginFinancing(
        decimal borrowed,
        decimal repaid,
        string? financialAccountId = null,
        IChartOfAccounts? chart = null)
    {
        if (borrowed < 0m || repaid < 0m || (borrowed != 0m && repaid != 0m))
            throw new ArgumentException("A fill must have nonnegative borrowing or repayment, not both.");
        if (borrowed == 0m && repaid == 0m)
            return [];
        chart ??= StandardChartOfAccounts.Instance;
        var cash = chart.Resolve(LedgerAccountRole.Cash, financialAccountId);
        var loan = chart.Resolve(LedgerAccountRole.MarginLoanPayable, financialAccountId);
        return borrowed > 0m
            ? [(cash, borrowed, 0m), (loan, 0m, borrowed)]
            : [(loan, repaid, 0m), (cash, 0m, repaid)];
    }
}
