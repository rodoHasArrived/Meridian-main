using System.Text.Json;
using LedgerAccountType = Meridian.Ledger.LedgerAccountType;

namespace Meridian.Application.DirectLending;

/// <summary>One journal line before it is bound to a ledger or outbox entry type.</summary>
public readonly record struct DirectLendingJournalLine(string Account, LedgerAccountType AccountType, decimal Debit, decimal Credit);

/// <summary>
/// PRD-115 increment 5: the journal lines for a mixed payment and for a daily accrual's interest,
/// shared by both journal paths (<see cref="LoanAccountingProjector"/> in the command transaction and
/// <see cref="DirectLendingOutboxDispatcher"/> from the outbox) so they cannot post differently.
/// Zero-amount lines are omitted.
/// </summary>
public static class DirectLendingJournalLines
{
    public const string PrepaidInterestLiability = "PrepaidInterestLiability";
    public const string UnappliedCashLiability = "UnappliedCashLiability";

    /// <summary>
    /// A mixed payment split by allocation: Dr Cash for the receipt; Cr each receivable it settled,
    /// Cr LoanPrincipal for scheduled and prepaid principal, Cr PrepaidInterestLiability and
    /// UnappliedCashLiability for routed excess; plus Dr PenaltyReceivable / Cr PenaltyIncome for a
    /// prepayment penalty charged on excess. Payments recorded before PRD-115 (no excess members on
    /// the resolution) keep their original Dr Cash / Cr LoanAndAccrualsClearing posting: historical
    /// clearing is reported, not rewritten.
    /// </summary>
    public static IReadOnlyList<DirectLendingJournalLine> ForMixedPayment(JsonElement root)
    {
        var amount = GetDecimal(root, "amount", "Amount");
        var lines = new List<DirectLendingJournalLine>();
        void Add(string account, LedgerAccountType type, decimal debit, decimal credit)
        {
            if (debit > 0m || credit > 0m)
            {
                lines.Add(new DirectLendingJournalLine(account, type, debit, credit));
            }
        }

        if (!TryGetResolution(root, out var resolution) || !resolution.TryGetProperty("excessToPrincipal", out _))
        {
            Add("Cash", LedgerAccountType.Asset, amount, 0m);
            Add("LoanAndAccrualsClearing", LedgerAccountType.Asset, 0m, amount);
            return lines;
        }

        var breakdown = resolution.TryGetProperty("breakdown", out var element) ? element : default;
        var toInterest = GetDecimal(breakdown, "toInterest");
        var toCommitmentFee = GetDecimal(breakdown, "toCommitmentFee");
        var toFees = GetDecimal(breakdown, "toFees");
        var toPenalty = GetDecimal(breakdown, "toPenalty");
        var toPrincipal = GetDecimal(breakdown, "toPrincipal") + GetDecimal(resolution, "excessToPrincipal");
        var toPrepaidInterest = GetDecimal(resolution, "excessToPrepaidInterest");
        var heldUnapplied = GetDecimal(resolution, "unappliedAmount");
        var penaltyCharged = GetDecimal(resolution, "prepaymentPenaltyCharged");

        var credited = toInterest + toCommitmentFee + toFees + toPenalty + toPrincipal + toPrepaidInterest + heldUnapplied;
        if (credited != amount)
        {
            throw new InvalidOperationException(
                $"Mixed payment of {amount} resolves to {credited}; the split journal would not balance.");
        }

        Add("Cash", LedgerAccountType.Asset, amount, 0m);
        Add("AccruedInterestReceivable", LedgerAccountType.Asset, 0m, toInterest);
        Add("CommitmentFeeReceivable", LedgerAccountType.Asset, 0m, toCommitmentFee);
        Add("FeeReceivable", LedgerAccountType.Asset, 0m, toFees);
        Add("PenaltyReceivable", LedgerAccountType.Asset, 0m, toPenalty);
        Add("LoanPrincipal", LedgerAccountType.Asset, 0m, toPrincipal);
        Add(PrepaidInterestLiability, LedgerAccountType.Liability, 0m, toPrepaidInterest);
        Add(UnappliedCashLiability, LedgerAccountType.Liability, 0m, heldUnapplied);
        // PRD-115 decision 3: the prepayment penalty on excess is charged by the payment itself.
        Add("PenaltyReceivable", LedgerAccountType.Asset, penaltyCharged, 0m);
        Add("PenaltyIncome", LedgerAccountType.Revenue, 0m, penaltyCharged);
        return lines;
    }

    /// <summary>
    /// The interest lines of a daily accrual: Cr InterestIncome for the day's cash interest, settled
    /// first from prepaid interest (Dr PrepaidInterestLiability) and the rest as a receivable
    /// (Dr AccruedInterestReceivable). PIK, commitment-fee and penalty lines are unchanged and posted
    /// by the callers.
    /// </summary>
    public static IReadOnlyList<DirectLendingJournalLine> ForAccruedInterest(JsonElement root)
    {
        var interest = GetDecimal(root, "interestAmount", "InterestAmount");
        var consumed = Math.Min(Math.Max(0m, GetDecimal(root, "prepaidInterestConsumed", "PrepaidInterestConsumed")), Math.Max(0m, interest));
        var lines = new List<DirectLendingJournalLine>();
        if (consumed > 0m)
        {
            lines.Add(new DirectLendingJournalLine(PrepaidInterestLiability, LedgerAccountType.Liability, consumed, 0m));
        }

        if (interest - consumed > 0m)
        {
            lines.Add(new DirectLendingJournalLine("AccruedInterestReceivable", LedgerAccountType.Asset, interest - consumed, 0m));
        }

        if (interest > 0m)
        {
            lines.Add(new DirectLendingJournalLine("InterestIncome", LedgerAccountType.Revenue, 0m, interest));
        }

        return lines;
    }

    private static bool TryGetResolution(JsonElement root, out JsonElement resolution)
        => (root.TryGetProperty("resolution", out resolution) || root.TryGetProperty("Resolution", out resolution))
            && resolution.ValueKind == JsonValueKind.Object;

    private static decimal GetDecimal(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return 0m;
        }

        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDecimal();
            }
        }

        return 0m;
    }
}
