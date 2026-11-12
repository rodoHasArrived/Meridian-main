using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.DirectLending;
using Meridian.Ledger;

namespace Meridian.Application.DirectLending;

/// <summary>One journal line before it is bound to a ledger or outbox entry type.</summary>
public readonly record struct DirectLendingJournalLine(string Account, LedgerAccountType AccountType, decimal Debit, decimal Credit)
{
    /// <summary>The explicit registered role for a line produced by Meridian's shared projection.</summary>
    [JsonIgnore]
    public LedgerAccountRole? AccountRole { get; init; }
}

/// <summary>
/// Loan-event journal economics and registered account roles,
/// shared by both journal paths (<see cref="LoanAccountingProjector"/> in the command transaction and
/// <see cref="DirectLendingOutboxDispatcher"/> from the outbox) so they cannot post differently.
/// Zero-amount lines are omitted.
/// </summary>
public static partial class DirectLendingJournalLines
{
    private static readonly IChartOfAccounts ChartOfAccounts = StandardChartOfAccounts.Instance;

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
    public static IReadOnlyList<DirectLendingJournalLine> ForMixedPayment(JsonElement root) =>
        ForMixedPayment(root, null);

    private static IReadOnlyList<DirectLendingJournalLine> ForMixedPayment(JsonElement root, string? financialAccountId)
    {
        var amount = GetDecimal(root, "amount", "Amount");
        var lines = new List<DirectLendingJournalLine>();
        void Add(LedgerAccountRole role, decimal debit, decimal credit)
        {
            if (debit > 0m || credit > 0m)
            {
                lines.Add(CreateLine(role, debit, credit, financialAccountId));
            }
        }

        if (!TryGetResolution(root, out var resolution) || !resolution.TryGetProperty("excessToPrincipal", out _))
        {
            Add(LedgerAccountRole.Cash, amount, 0m);
            Add(LedgerAccountRole.LoanAndAccrualsClearing, 0m, amount);
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

        Add(LedgerAccountRole.Cash, amount, 0m);
        Add(LedgerAccountRole.LoanAccruedInterestReceivable, 0m, toInterest);
        Add(LedgerAccountRole.LoanCommitmentFeeReceivable, 0m, toCommitmentFee);
        Add(LedgerAccountRole.LoanFeeReceivable, 0m, toFees);
        Add(LedgerAccountRole.LoanPenaltyReceivable, 0m, toPenalty);
        Add(LedgerAccountRole.LoanPrincipal, 0m, toPrincipal);
        Add(LedgerAccountRole.LoanPrepaidInterestLiability, 0m, toPrepaidInterest);
        Add(LedgerAccountRole.LoanUnappliedCashLiability, 0m, heldUnapplied);
        // PRD-115 decision 3: the prepayment penalty on excess is charged by the payment itself.
        Add(LedgerAccountRole.LoanPenaltyReceivable, penaltyCharged, 0m);
        Add(LedgerAccountRole.LoanPenaltyIncome, 0m, penaltyCharged);
        return lines;
    }

    /// <summary>
    /// PRD-115 increment 7: held cash applied to amounts owed. Dr UnappliedCashLiability for the amount
    /// applied; Cr each receivable settled and LoanPrincipal. No cash moves.
    /// </summary>
    public static IReadOnlyList<DirectLendingJournalLine> ForUnappliedCashApplied(JsonElement root) =>
        ForUnappliedCashApplied(root, null);

    private static IReadOnlyList<DirectLendingJournalLine> ForUnappliedCashApplied(JsonElement root, string? financialAccountId)
    {
        var applied = GetDecimal(root, "appliedAmount", "AppliedAmount");
        var breakdown = root.TryGetProperty("breakdown", out var element) || root.TryGetProperty("Breakdown", out element) ? element : default;
        var toInterest = GetDecimal(breakdown, "toInterest");
        var toCommitmentFee = GetDecimal(breakdown, "toCommitmentFee");
        var toFees = GetDecimal(breakdown, "toFees");
        var toPenalty = GetDecimal(breakdown, "toPenalty");
        var toPrincipal = GetDecimal(breakdown, "toPrincipal");
        if (toInterest + toCommitmentFee + toFees + toPenalty + toPrincipal != applied)
        {
            throw new InvalidOperationException(
                $"Unapplied cash application of {applied} does not match its breakdown; the journal would not balance.");
        }

        var lines = new List<DirectLendingJournalLine>();
        void Add(LedgerAccountRole role, decimal debit, decimal credit)
        {
            if (debit > 0m || credit > 0m)
            {
                lines.Add(CreateLine(role, debit, credit, financialAccountId));
            }
        }

        Add(LedgerAccountRole.LoanUnappliedCashLiability, applied, 0m);
        Add(LedgerAccountRole.LoanAccruedInterestReceivable, 0m, toInterest);
        Add(LedgerAccountRole.LoanCommitmentFeeReceivable, 0m, toCommitmentFee);
        Add(LedgerAccountRole.LoanFeeReceivable, 0m, toFees);
        Add(LedgerAccountRole.LoanPenaltyReceivable, 0m, toPenalty);
        Add(LedgerAccountRole.LoanPrincipal, 0m, toPrincipal);
        return lines;
    }

    /// <summary>PRD-115 increment 7: held cash returned to the borrower. Dr UnappliedCashLiability / Cr Cash.</summary>
    public static IReadOnlyList<DirectLendingJournalLine> ForUnappliedCashRefunded(JsonElement root) =>
        ForUnappliedCashRefunded(root, null);

    private static IReadOnlyList<DirectLendingJournalLine> ForUnappliedCashRefunded(JsonElement root, string? financialAccountId)
    {
        var amount = GetDecimal(root, "amount", "Amount");
        return amount > 0m
            ? [CreateLine(LedgerAccountRole.LoanUnappliedCashLiability, amount, 0m, financialAccountId), CreateLine(LedgerAccountRole.Cash, 0m, amount, financialAccountId)]
            : [];
    }

    /// <summary>
    /// The interest lines of a daily accrual: Cr InterestIncome for the day's cash interest, settled
    /// first from prepaid interest (Dr PrepaidInterestLiability) and the rest as a receivable
    /// (Dr AccruedInterestReceivable). PIK, commitment-fee and penalty lines are unchanged and posted
    /// by the callers.
    /// </summary>
    public static IReadOnlyList<DirectLendingJournalLine> ForAccruedInterest(JsonElement root) =>
        ForAccruedInterest(root, null);

    private static IReadOnlyList<DirectLendingJournalLine> ForAccruedInterest(JsonElement root, string? financialAccountId)
    {
        var interest = GetDecimal(root, "interestAmount", "InterestAmount");
        var consumed = Math.Min(Math.Max(0m, GetDecimal(root, "prepaidInterestConsumed", "PrepaidInterestConsumed")), Math.Max(0m, interest));
        var lines = new List<DirectLendingJournalLine>();
        if (consumed > 0m)
        {
            lines.Add(CreateLine(LedgerAccountRole.LoanPrepaidInterestLiability, consumed, 0m, financialAccountId));
        }

        if (interest - consumed > 0m)
        {
            lines.Add(CreateLine(LedgerAccountRole.LoanAccruedInterestReceivable, interest - consumed, 0m, financialAccountId));
        }

        if (interest > 0m)
        {
            lines.Add(CreateLine(LedgerAccountRole.LoanInterestIncome, 0m, interest, financialAccountId));
        }

        return lines;
    }

    internal static LedgerAccountRole RequiredRole(DirectLendingJournalLine line) =>
        line.AccountRole ?? throw new LedgerValidationException("Shared loan projection did not retain an explicit posting-account role.");

    private static DirectLendingJournalLine CreateLine(
        LedgerAccountRole role,
        decimal debit,
        decimal credit,
        string? financialAccountId)
    {
        var account = financialAccountId is null
            ? ChartOfAccounts.GetDefinition(role)
            : ChartOfAccounts.Resolve(role, financialAccountId);
        return new DirectLendingJournalLine(account.Name, account.AccountType, debit, credit) { AccountRole = role };
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

        var amount = TryGetDecimal(element, propertyNames, out var value) ? value : 0m;
        if (amount < 0m)
        {
            // Validate before zero-line filtering and prepaid-interest allocation, so
            // malformed retained values cannot disappear from either posting path.
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Direct-lending journal amount '{propertyNames[0]}' cannot be negative (was {amount})."));
        }

        return amount;
    }

    private static bool TryGetDecimal(JsonElement root, string[] propertyNames, out decimal value)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (element.ValueKind != JsonValueKind.Number)
            {
                throw new DirectLendingCommandException(
                    new DirectLendingCommandError(
                        DirectLendingErrorCode.Validation,
                        $"Direct-lending ledger payload member '{propertyName}' must be a JSON number; the journal was not posted."));
            }

            value = element.GetDecimal();
            return true;
        }

        value = 0m;
        return false;
    }
}
