namespace Meridian.Contracts.DirectLending;

/// <summary>Shared, deterministic cash allocation for the C# and F# servicing paths.</summary>
public static class DirectLendingPaymentResolver
{
    public const string CashAccountingVersion = "direct-lending-cash-v1";

    /// <summary>Matches the authoritative ledger's numeric(38,10) storage without rounding.</summary>
    public static bool IsLedgerAmount(decimal amount) =>
        decimal.Round(amount, 10, MidpointRounding.ToEven) == amount &&
        amount > -10_000_000_000_000_000_000_000_000_000m && amount < 10_000_000_000_000_000_000_000_000_000m;

    public static string? ValidatePolicy(DirectLendingTermsDto terms)
    {
        var policy = terms.ExcessPaymentPolicy;
        if (policy is null)
            return null;
        if (policy.Version != 1 || !Enum.IsDefined(policy.Treatment) || !Enum.IsDefined(policy.Application))
            return "The excess-payment policy version or treatment is unsupported.";
        if (policy.Treatment != ExcessPaymentTreatment.PrepaidInterest && policy.Application != PrepaidInterestApplication.HoldForReview)
            return "A prepaid-interest release instruction requires a prepaid-interest treatment.";
        if (string.IsNullOrWhiteSpace(policy.ContractualReference))
            return "An excess-payment policy requires a contractual evidence reference.";
        if (policy.Treatment == ExcessPaymentTreatment.PrincipalPrepayment && !terms.PrepaymentAllowed)
            return "Principal prepayment is prohibited by the loan terms.";
        if (policy.Treatment == ExcessPaymentTreatment.PrincipalPrepayment && terms.PrepaymentPenaltyRate > 0m)
            return "Penalty-bearing principal prepayment requires reviewed penalty and allocation instructions; version 1 cannot infer them.";
        return null;
    }

    public static string? ValidatePrincipalPayment(
        LoanServicingStateDto servicing, DirectLendingTermsDto terms, decimal amount, DateOnly effectiveDate)
    {
        if (amount <= 0m)
            return "Principal payment amount must be positive.";
        if (!IsLedgerAmount(amount))
            return "Principal payment must be exactly representable by the ledger's numeric(38,10) amount contract.";
        if (amount > servicing.Balances.PrincipalOutstanding)
            return "Principal-only payment exceeds outstanding principal; use a mixed-payment receipt to retain excess cash.";
        if (effectiveDate < terms.MaturityDate && !terms.PrepaymentAllowed)
            return "Principal prepayment is prohibited by the loan terms; use a mixed-payment receipt to hold cash.";
        if (effectiveDate < terms.MaturityDate && terms.ExcessPaymentPolicy is not null && terms.PrepaymentPenaltyRate > 0m)
            return "Penalty-bearing principal prepayment requires reviewed penalty and allocation instructions.";
        return ValidatePolicy(terms);
    }

    public static string? ValidateTermsAmendment(
        LoanServicingStateDto servicing, DirectLendingTermsDto currentTerms, DirectLendingTermsDto newTerms)
    {
        if (currentTerms.BaseCurrency != newTerms.BaseCurrency &&
            (servicing.Balances.UnappliedCash != 0m || servicing.Balances.PrepaidInterest != 0m))
            return "The loan currency cannot change while unapplied cash or prepaid interest is retained; a reviewed redenomination workflow is required.";
        return ValidatePolicy(newTerms);
    }

    public static MixedPaymentResolutionDto Resolve(
        LoanServicingStateDto servicing,
        DirectLendingTermsDto terms,
        decimal amount,
        PaymentBreakdownDto? requestedBreakdown,
        DateOnly effectiveDate)
    {
        ArgumentNullException.ThrowIfNull(servicing);
        ArgumentNullException.ThrowIfNull(terms);
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");
        if (!IsLedgerAmount(amount))
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment must be exactly representable by the ledger's numeric(38,10) amount contract.");
        if (ValidatePolicy(terms) is { } policyError)
            throw new ArgumentException(policyError, nameof(terms));
        if (terms.BaseCurrency is not (CurrencyCode.USD or CurrencyCode.EUR or CurrencyCode.GBP or CurrencyCode.JPY))
            throw new ArgumentException("A cash receipt requires a known contractual currency.", nameof(terms));
        if (requestedBreakdown is { } requested &&
            (requested.ToInterest < 0m || requested.ToCommitmentFee < 0m || requested.ToFees < 0m ||
             requested.ToPenalty < 0m || requested.ToPrincipal < 0m))
            throw new ArgumentException("Payment allocation amounts cannot be negative.", nameof(requestedBreakdown));

        var remaining = amount;
        var balances = servicing.Balances;
        var policy = terms.ExcessPaymentPolicy;
        var atMaturity = effectiveDate >= terms.MaturityDate;
        var inInterestOnlyPeriod = !atMaturity && terms.InterestOnlyMonths > 0 &&
            effectiveDate < terms.OriginationDate.AddMonths(terms.InterestOnlyMonths);
        var principalPermitted = atMaturity || (terms.PrepaymentAllowed &&
            (policy is null || !(terms.PrepaymentPenaltyRate > 0m)));

        decimal Take(decimal requestedAmount, decimal outstanding)
        {
            var applied = Math.Min(remaining, Math.Min(Math.Max(0m, requestedAmount), Math.Max(0m, outstanding)));
            remaining -= applied;
            return applied;
        }

        var principalRequest = requestedBreakdown?.ToPrincipal ??
            (atMaturity || (policy is null && !inInterestOnlyPeriod) ? balances.PrincipalOutstanding : 0m);
        var breakdown = new PaymentBreakdownDto(
            Take(requestedBreakdown?.ToInterest ?? balances.InterestAccruedUnpaid, balances.InterestAccruedUnpaid),
            Take(requestedBreakdown?.ToCommitmentFee ?? balances.CommitmentFeeAccruedUnpaid, balances.CommitmentFeeAccruedUnpaid),
            Take(requestedBreakdown?.ToFees ?? balances.FeesAccruedUnpaid, balances.FeesAccruedUnpaid),
            Take(requestedBreakdown?.ToPenalty ?? balances.PenaltyAccruedUnpaid, balances.PenaltyAccruedUnpaid),
            Take(principalPermitted ? principalRequest : 0m, balances.PrincipalOutstanding));

        decimal prepaidInterest = 0m;
        if (remaining > 0m && policy?.Treatment == ExcessPaymentTreatment.PrincipalPrepayment)
        {
            breakdown = breakdown with
            {
                ToPrincipal = breakdown.ToPrincipal + Take(remaining, balances.PrincipalOutstanding - breakdown.ToPrincipal)
            };
        }
        else if (remaining > 0m && !atMaturity && policy?.Treatment == ExcessPaymentTreatment.PrepaidInterest)
        {
            prepaidInterest = remaining;
            remaining = 0m;
        }

        if (!new[] { breakdown.ToInterest, breakdown.ToCommitmentFee, breakdown.ToFees,
                breakdown.ToPenalty, breakdown.ToPrincipal, remaining, prepaidInterest }.All(IsLedgerAmount))
            throw new ArgumentException("Resolved allocations must be exactly representable by the ledger's numeric(38,10) amount contract.", nameof(servicing));

        var reviewReason = remaining > 0m && policy is null
            ? "No excess-payment election is declared; cash is retained pending contractual review."
            : prepaidInterest > 0m && policy?.Application == PrepaidInterestApplication.HoldForReview
                ? "Prepaid interest is retained; release requires reviewed contractual period instructions."
                : remaining > 0m && atMaturity && policy?.Treatment == ExcessPaymentTreatment.PrepaidInterest
                    ? "No future contractual interest period is established at or after maturity; excess cash is held unapplied."
                : remaining > 0m && !principalPermitted && principalRequest > 0m
                    ? "Requested principal allocation is not permitted by the supported contractual instructions."
                    : null;

        return new MixedPaymentResolutionDto(
            breakdown,
            requestedBreakdown is null ? "WaterfallAuto" : "Manual",
            CashAccountingVersion,
            remaining,
            prepaidInterest,
            CashAccountingVersion,
            reviewReason);
    }
}
