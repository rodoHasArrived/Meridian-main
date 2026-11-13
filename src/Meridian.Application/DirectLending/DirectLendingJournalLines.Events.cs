using System.Text.Json;
using Meridian.Contracts.DirectLending;
using Meridian.Ledger;

namespace Meridian.Application.DirectLending;

public static partial class DirectLendingJournalLines
{
    /// <summary>
    /// Projects one loan event for both authoritative journal posting and outbox draft creation.
    /// Account names and classifications are resolved only through the shared chart; zero-amount
    /// and non-accounting events produce no lines. Historical account identities are preserved.
    /// </summary>
    public static IReadOnlyList<DirectLendingJournalLine> ForEvent(
        string eventType,
        JsonElement root,
        string financialAccountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(financialAccountId);
        var lines = new List<DirectLendingJournalLine>();
        void Add(LedgerAccountRole role, decimal debit, decimal credit)
        {
            if (debit < 0m || credit < 0m)
            {
                var account = ChartOfAccounts.Resolve(role, financialAccountId);
                throw new DirectLendingCommandException(
                    new DirectLendingCommandError(
                        DirectLendingErrorCode.Validation,
                        $"Direct-lending ledger event '{eventType}' produced a negative amount for account '{account.Name}'; the journal was not posted."));
            }

            if (debit > 0m || credit > 0m)
            {
                lines.Add(CreateLine(role, debit, credit, financialAccountId));
            }
        }

        switch (eventType)
        {
            case "loan.drawdown-booked":
                var drawdownAmount = GetRequiredDecimal(root, eventType, "amount", "Amount");
                Add(LedgerAccountRole.LoanPrincipal, drawdownAmount, 0m);
                Add(LedgerAccountRole.Cash, 0m, drawdownAmount);
                break;

            case "loan.daily-accrual-posted":
                var commitmentFee = GetEventDecimal(root, "commitmentFeeAmount", "CommitmentFeeAmount");
                var penalty = GetEventDecimal(root, "penaltyAmount", "PenaltyAmount");
                var pikInterest = GetEventDecimal(root, "pikInterestAmount", "PikInterestAmount");
                // PRD-115: interest settled from prepaid interest first (shared with DirectLendingOutboxDispatcher).
                foreach (var line in ForAccruedInterest(root, financialAccountId))
                {
                    Add(RequiredRole(line), line.Debit, line.Credit);
                }

                if (IsPikCapitalizationDeferred(root))
                {
                    // Capitalization policy: PIK is earned now but is neither cash nor principal
                    // until a capitalization date moves it (loan.pik-capitalized).
                    Add(LedgerAccountRole.LoanPikInterestReceivable, pikInterest, 0m);
                    Add(LedgerAccountRole.LoanPikInterestIncome, 0m, pikInterest);
                }
                else
                {
                    // Legacy PIK toggle: PIK interest capitalizes into loan principal the same day.
                    Add(LedgerAccountRole.LoanPrincipal, pikInterest, 0m);
                    Add(LedgerAccountRole.LoanInterestIncome, 0m, pikInterest);
                }
                Add(LedgerAccountRole.LoanCommitmentFeeReceivable, commitmentFee, 0m);
                Add(LedgerAccountRole.LoanCommitmentFeeIncome, 0m, commitmentFee);
                Add(LedgerAccountRole.LoanPenaltyReceivable, penalty, 0m);
                Add(LedgerAccountRole.LoanPenaltyIncome, 0m, penalty);
                break;

            case "loan.daily-accrual-reversed":
                var reversedCommitmentFee = GetEventDecimal(root, "commitmentFeeAmount", "CommitmentFeeAmount");
                var reversedPenalty = GetEventDecimal(root, "penaltyAmount", "PenaltyAmount");
                var reversedPikInterest = GetEventDecimal(root, "pikInterestAmount", "PikInterestAmount");
                // PRD-115: the exact inverse of the posting, so interest that consumed prepaid interest
                // restores PrepaidInterestLiability instead of crediting a receivable never debited.
                foreach (var line in ForAccruedInterest(root, financialAccountId))
                {
                    Add(RequiredRole(line), line.Credit, line.Debit);
                }
                if (IsPikCapitalizationDeferred(root))
                {
                    Add(LedgerAccountRole.LoanPikInterestIncome, reversedPikInterest, 0m);
                    Add(LedgerAccountRole.LoanPikInterestReceivable, 0m, reversedPikInterest);
                }
                else
                {
                    Add(LedgerAccountRole.LoanInterestIncome, reversedPikInterest, 0m);
                    Add(LedgerAccountRole.LoanPrincipal, 0m, reversedPikInterest);
                }
                Add(LedgerAccountRole.LoanCommitmentFeeIncome, reversedCommitmentFee, 0m);
                Add(LedgerAccountRole.LoanCommitmentFeeReceivable, 0m, reversedCommitmentFee);
                Add(LedgerAccountRole.LoanPenaltyIncome, reversedPenalty, 0m);
                Add(LedgerAccountRole.LoanPenaltyReceivable, 0m, reversedPenalty);
                break;

            // PRD-115: payments now settle FeeReceivable and PenaltyReceivable directly, so the
            // assessments that create those receivables must reach the authoritative ledger too
            // (the same pairs the outbox dispatcher drafts), or settlement would drive them negative.
            case "loan.fee-assessed":
                var assessedFee = GetRequiredDecimal(root, eventType, "amount", "Amount");
                Add(LedgerAccountRole.LoanFeeReceivable, assessedFee, 0m);
                Add(LedgerAccountRole.LoanFeeIncome, 0m, assessedFee);
                break;

            case "loan.prepayment-penalty-charged":
                var chargedPenalty = GetRequiredDecimal(root, eventType, "penaltyAmount", "PenaltyAmount");
                Add(LedgerAccountRole.LoanPenaltyReceivable, chargedPenalty, 0m);
                Add(LedgerAccountRole.LoanPenaltyIncome, 0m, chargedPenalty);
                break;

            case "loan.pik-capitalized":
                var capitalizedPik = GetRequiredDecimal(root, eventType, "amount", "Amount");
                Add(LedgerAccountRole.LoanPrincipal, capitalizedPik, 0m);
                Add(LedgerAccountRole.LoanPikInterestReceivable, 0m, capitalizedPik);
                break;

            case "loan.principal-payment-applied":
                var appliedPrincipal = GetRequiredDecimal(root, eventType, "AppliedAmount", "appliedAmount", "amount", "Amount");
                Add(LedgerAccountRole.Cash, appliedPrincipal, 0m);
                Add(LedgerAccountRole.LoanPrincipal, 0m, appliedPrincipal);
                break;

            case "loan.mixed-payment-applied":
                GetRequiredDecimal(root, eventType, "amount", "Amount");
                // PRD-115: split by allocation (shared with DirectLendingOutboxDispatcher).
                foreach (var line in ForMixedPayment(root, financialAccountId))
                {
                    Add(RequiredRole(line), line.Debit, line.Credit);
                }
                break;

            case "loan.unapplied-cash-applied":
            case "loan.unapplied-cash-refunded":
                if (eventType == "loan.unapplied-cash-applied")
                {
                    GetRequiredDecimal(root, eventType, "appliedAmount", "AppliedAmount");
                }
                else
                {
                    GetRequiredDecimal(root, eventType, "amount", "Amount");
                }

                // PRD-115 increment 7 (shared with DirectLendingOutboxDispatcher).
                foreach (var line in eventType == "loan.unapplied-cash-applied"
                    ? ForUnappliedCashApplied(root, financialAccountId)
                    : ForUnappliedCashRefunded(root, financialAccountId))
                {
                    Add(RequiredRole(line), line.Debit, line.Credit);
                }
                break;

            case "loan.write-off-applied":
                var writeOffAmount = GetRequiredDecimal(root, eventType, "AppliedAmount", "appliedAmount", "Amount", "amount");
                Add(LedgerAccountRole.LoanWriteOffExpense, writeOffAmount, 0m);
                Add(LedgerAccountRole.LoanPrincipal, 0m, writeOffAmount);
                break;

            case "loan.discount-premium-amortized":
            case "DiscountPremiumAmortized":
                var discountAmortization = GetEventDecimal(root, "discountAmort", "discountAmortization");
                var premiumAmortization = GetEventDecimal(root, "premiumAmort", "premiumAmortization");
                Add(LedgerAccountRole.LoanDiscountAmortizationExpense, discountAmortization, 0m);
                Add(LedgerAccountRole.LoanDiscount, 0m, discountAmortization);
                Add(LedgerAccountRole.LoanPremium, premiumAmortization, 0m);
                Add(LedgerAccountRole.LoanPremiumAmortizationIncome, 0m, premiumAmortization);
                break;

            case "loan.restructured":
            case "LoanRestructured":
                var principalHaircut = GetEventDecimal(root, "PrincipalHaircut", "principalHaircut", "haircutAmount");
                Add(LedgerAccountRole.LoanRestructuringLoss, principalHaircut, 0m);
                Add(LedgerAccountRole.LoanPrincipal, 0m, principalHaircut);
                break;
        }

        if (lines.Sum(line => line.Debit) != lines.Sum(line => line.Credit))
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Direct-lending ledger event '{eventType}' produced an unbalanced journal; the journal was not posted."));
        }

        return lines;
    }

    /// <summary>
    /// Reads an optional amount. An absent member means the component does not apply and reads as
    /// zero; a member that is present but not a JSON number is rejected rather than read as zero.
    /// </summary>
    private static decimal GetEventDecimal(JsonElement root, params string[] propertyNames) =>
        TryGetDecimal(root, propertyNames, out var value) ? value : 0m;

    /// <summary>
    /// Reads the amount a ledger-impacting event cannot post without. A missing amount fails the
    /// command instead of silently producing no journal for a persisted loan event.
    /// </summary>
    private static decimal GetRequiredDecimal(JsonElement root, string eventType, params string[] propertyNames)
    {
        if (TryGetDecimal(root, propertyNames, out var value))
        {
            return value;
        }

        throw new DirectLendingCommandException(
            new DirectLendingCommandError(
                DirectLendingErrorCode.Validation,
                $"Direct-lending ledger event '{eventType}' payload is missing required amount '{propertyNames[0]}'; the journal was not posted."));
    }

    private static bool IsPikCapitalizationDeferred(JsonElement root) =>
        root.TryGetProperty("pikCapitalizationDeferred", out var value) && value.ValueKind == JsonValueKind.True;

}
