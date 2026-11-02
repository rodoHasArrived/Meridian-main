using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Meridian.Contracts.DirectLending;

/// <summary>Shared, deterministic cash allocation for the C# and F# servicing paths.</summary>
public static class DirectLendingPaymentResolver
{
    public const string CashAccountingVersion = "direct-lending-cash-v1";
    private static readonly JsonSerializerOptions TermsJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Matches the authoritative ledger's numeric(38,10) storage without rounding.</summary>
    public static bool IsLedgerAmount(decimal amount) =>
        decimal.Round(amount, 10, MidpointRounding.ToEven) == amount &&
        amount > -10_000_000_000_000_000_000_000_000_000m && amount < 10_000_000_000_000_000_000_000_000_000m;

    public static string ComputeTermsHash(DirectLendingTermsDto terms) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(terms, TermsJson))));

    public static string? ValidatePolicy(DirectLendingTermsDto terms) =>
        terms.ExcessPaymentApplication is { } rule && !Enum.IsDefined(rule)
            ? "The contractual excess-payment application is unsupported." : null;

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

    /// <summary>Refuses ambiguous backdated routing; recording time is not a contractual effective date.</summary>
    public static string? ValidatePaymentTermsSelection(LoanContractDetailDto contract, DateOnly effectiveDate)
    {
        if (contract.CurrentTermsVersion <= 1)
            return null;
        var current = contract.TermsVersions.SingleOrDefault(item => item.VersionNumber == contract.CurrentTermsVersion);
        if (current is null)
            return "The current payment terms version is not retained.";
        if (effectiveDate < DateOnly.FromDateTime(current.RecordedAt.UtcDateTime) &&
            contract.TermsVersions.Any(item => item.VersionNumber < current.VersionNumber && !SameRoutingTerms(item.Terms, current.Terms)))
            return "Backdated receipt crosses amended payment terms without contractual effective-date evidence; reviewed terms-version selection is required.";
        return null;
    }

    private static bool SameRoutingTerms(DirectLendingTermsDto left, DirectLendingTermsDto right) =>
        left.EffectiveExcessPaymentApplication == right.EffectiveExcessPaymentApplication &&
        left.PrepaymentAllowed == right.PrepaymentAllowed && left.PrepaymentPenaltyRate == right.PrepaymentPenaltyRate &&
        left.InterestOnlyMonths == right.InterestOnlyMonths && left.OriginationDate == right.OriginationDate &&
        left.MaturityDate == right.MaturityDate && left.BaseCurrency == right.BaseCurrency;

    /// <summary>Existing penalty convention, applied only to the separately routed excess-principal basis.</summary>
    public static decimal CalculateExcessPrepaymentPenalty(DirectLendingTermsDto terms, MixedPaymentResolutionDto resolution)
    {
        if (resolution.ExcessToPrincipal <= 0m || !(terms.PrepaymentPenaltyRate > 0m))
            return 0m;
        var amount = decimal.Round(resolution.ExcessToPrincipal * terms.PrepaymentPenaltyRate.Value, 2, MidpointRounding.AwayFromZero);
        if (!IsLedgerAmount(amount))
            throw new ArgumentException("The excess-principal penalty exceeds the authoritative ledger amount contract.", nameof(terms));
        return amount;
    }

    public static MixedPaymentResolutionDto Resolve(
        LoanServicingStateDto servicing, DirectLendingTermsDto terms, decimal amount,
        PaymentBreakdownDto? requestedBreakdown, DateOnly effectiveDate)
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
        var inInterestOnlyPeriod = terms.InterestOnlyMonths > 0 && effectiveDate < terms.OriginationDate.AddMonths(terms.InterestOnlyMonths);
        decimal Take(decimal requestedAmount, decimal outstanding)
        {
            var applied = Math.Min(remaining, Math.Min(Math.Max(0m, requestedAmount), Math.Max(0m, outstanding)));
            remaining -= applied;
            return applied;
        }
        // Preserve the existing allocation; the contractual rule routes only its residual.
        var breakdown = new PaymentBreakdownDto(
            Take(requestedBreakdown?.ToInterest ?? balances.InterestAccruedUnpaid, balances.InterestAccruedUnpaid),
            Take(requestedBreakdown?.ToCommitmentFee ?? balances.CommitmentFeeAccruedUnpaid, balances.CommitmentFeeAccruedUnpaid),
            Take(requestedBreakdown?.ToFees ?? balances.FeesAccruedUnpaid, balances.FeesAccruedUnpaid),
            Take(requestedBreakdown?.ToPenalty ?? balances.PenaltyAccruedUnpaid, balances.PenaltyAccruedUnpaid),
            Take(requestedBreakdown?.ToPrincipal ?? (inInterestOnlyPeriod ? 0m : balances.PrincipalOutstanding), balances.PrincipalOutstanding));
        decimal excessPrincipal = 0m, prepaid = 0m;
        string? note = null;
        if (remaining > 0m)
        {
            switch (terms.EffectiveExcessPaymentApplication)
            {
                case ExcessPaymentApplication.PrincipalPrepayment when terms.PrepaymentAllowed:
                    excessPrincipal = Take(remaining, balances.PrincipalOutstanding - breakdown.ToPrincipal);
                    break;
                case ExcessPaymentApplication.PrincipalPrepayment:
                    note = "Principal prepayment is not allowed; excess cash is held unapplied.";
                    break;
                case ExcessPaymentApplication.PrepaidInterest:
                    prepaid = remaining;
                    remaining = 0m;
                    break;
            }
        }
        if (!new[] { breakdown.ToInterest, breakdown.ToCommitmentFee, breakdown.ToFees, breakdown.ToPenalty,
                breakdown.ToPrincipal, excessPrincipal, prepaid, remaining }.All(IsLedgerAmount))
            throw new ArgumentException("Resolved allocations must be exactly representable by the ledger's numeric(38,10) amount contract.", nameof(servicing));

        return new MixedPaymentResolutionDto(breakdown, requestedBreakdown is null ? "WaterfallAuto" : "Manual", CashAccountingVersion, remaining)
        {
            ExcessToPrincipal = excessPrincipal,
            ExcessToPrepaidInterest = prepaid,
            ExcessRuleApplied = terms.ExcessPaymentApplication,
            ExcessRuleNote = note,
            CashAccountingVersion = CashAccountingVersion
        };
    }
}
