using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>
/// Book accounting for short carrying charges: a borrow fee is an expense paid in cash; a payment in lieu is
/// dividend expense paid in cash. Neither changes the short lot's quantity or liability basis, so neither is a
/// lot mutation; both are governed period journals that must reference the open short lot they belong to.
/// </summary>
public static class ShortCarryProjector
{
    public static readonly IReadOnlySet<int> SupportedDaysPerYear = new HashSet<int> { 360, 365 };

    /// <summary>The functional-currency charge, at ledger rounding, or the reason it cannot be calculated.</summary>
    public static decimal? TryComputeAmount(ShortCarryInstruction instruction, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        refusal = Refuse(instruction);
        if (refusal is not null)
            return null;
        var amount = instruction.Kind == ShortCarryKind.BorrowFee
            ? instruction.Quantity * instruction.ReferencePrice!.Value * instruction.AnnualRate!.Value * instruction.AccruedDays / instruction.DaysPerYear!.Value
            : instruction.Quantity * instruction.AmountPerUnit!.Value;
        var rounded = LedgerCurrencyRounding.RoundCurrency(amount);
        if (rounded <= 0m)
        {
            refusal = "The short carrying charge rounds to zero; nothing is posted.";
            return null;
        }
        return rounded;
    }

    /// <summary>Expected journal lines for the instruction against the lot's liability carrying account.</summary>
    public static IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Lines(
        ShortCarryInstruction instruction, LedgerAccount carryingAccount)
    {
        ArgumentNullException.ThrowIfNull(carryingAccount);
        if (!OpenLotCarryingAccounts.Matches(carryingAccount, OpenLotDirection.Short))
            throw new ArgumentException("Short carrying charges require the lot's explicit liability carrying account.");
        var amount = TryComputeAmount(instruction, out var refusal) ?? throw new ArgumentException(refusal);
        var expense = instruction.Kind == ShortCarryKind.BorrowFee
            ? LedgerAccounts.StockBorrowFeeExpenseOrDefault(carryingAccount.FinancialAccountId)
            : LedgerAccounts.DividendExpenseOrDefault(carryingAccount.FinancialAccountId);
        return [(expense, amount, 0m), (LedgerAccounts.CashFor(carryingAccount.FinancialAccountId), 0m, amount)];
    }

    private static string? Refuse(ShortCarryInstruction instruction)
    {
        if (instruction.Direction != OpenLotDirection.Short)
            return "Borrow fees and payments in lieu apply to Short lots only.";
        if (instruction.TaxLotRecordId == Guid.Empty || string.IsNullOrWhiteSpace(instruction.LotId))
            return "A short carrying charge must name the open short lot it belongs to.";
        if (instruction.Quantity <= 0m)
            return "The borrowed quantity must be positive.";
        if (instruction.AccrualEnd <= instruction.AccrualStart)
            return "The accrual interval must be [start, end) with end after start.";
        if (string.IsNullOrWhiteSpace(instruction.Currency) || string.IsNullOrWhiteSpace(instruction.FunctionalCurrency))
            return "Charge and functional currencies are required.";
        if (!string.Equals(instruction.Currency.Trim(), instruction.FunctionalCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
            return "A non-functional-currency short carrying charge requires the separate foreign-currency short treatment.";
        if (string.IsNullOrWhiteSpace(instruction.SourceEvidenceId))
            return "The lender statement or dividend notice evidence identity is required.";
        if (instruction.ModelVersion != ShortCarryInstruction.CurrentModelVersion)
            return $"Unsupported short carry model version '{instruction.ModelVersion}'.";
        return instruction.Kind switch
        {
            ShortCarryKind.BorrowFee when instruction.ReferencePrice is not > 0m => "A borrow fee requires the lender's positive reference price.",
            ShortCarryKind.BorrowFee when instruction.AnnualRate is not >= 0m => "A borrow fee requires a non-negative annual rate.",
            ShortCarryKind.BorrowFee when instruction.DaysPerYear is not { } days || !SupportedDaysPerYear.Contains(days) => "A borrow fee requires a 360 or 365 day basis.",
            ShortCarryKind.BorrowFee when instruction.AmountPerUnit is not null => "A borrow fee carries no per-unit amount.",
            ShortCarryKind.PaymentInLieu when instruction.AmountPerUnit is not > 0m => "A payment in lieu requires the declared positive dividend per unit.",
            ShortCarryKind.PaymentInLieu when instruction.ReferencePrice is not null || instruction.AnnualRate is not null || instruction.DaysPerYear is not null
                => "A payment in lieu carries no borrow-fee rate inputs.",
            ShortCarryKind.BorrowFee or ShortCarryKind.PaymentInLieu => null,
            _ => "Unsupported short carry kind."
        };
    }
}
