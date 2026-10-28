using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster.CashFlow;

/// <summary>
/// The opening outstanding face shared by calculated cash flows and quoted valuation. Dated
/// factors include principal payments only through their evidence date; later completed payments
/// still reduce the balance. An undated scalar is assumed current and is not reduced twice.
/// </summary>
internal static class SecurityPrincipalBalance
{
    public static decimal OpeningOutstanding(
        SecurityDetailDto security,
        StructuredCashFlowTerms terms,
        DateOnly asOfDate,
        bool roundToCashPrecision = true)
    {
        var principalBasis = terms.PrincipalFace is > 0m ? terms.PrincipalFace.Value : 100m;
        StructuredFactorScheduleEntry? appliedFactorEntry = null;
        for (var i = terms.FactorSchedule.Count - 1; i >= 0; i--)
        {
            if (terms.FactorSchedule[i].AsOfDate <= asOfDate)
            {
                appliedFactorEntry = terms.FactorSchedule[i];
                break;
            }
        }

        // A scalar current factor is DATED evidence when a factorDate is retained — and dated
        // evidence from the FUTURE does not describe today's balance: a 0.8 factor effective next
        // month would understate today's outstanding (and every projected interest and principal
        // row) by 20%. Only an undated scalar, or one dated on or before the projection as-of,
        // may seed the balance; a future-dated scalar leaves the latest eligible schedule point
        // (already resolved above) or the unfactored walk in charge.
        var scalarFactorDate = ReadScalarFactorDate(security);
        var scalarFactorApplies = scalarFactorDate is not DateOnly scalarAsOf || scalarAsOf <= asOfDate;
        var scheduledFactor = appliedFactorEntry?.Factor ?? (scalarFactorApplies ? terms.CurrentFactor : null);
        var hasAuthoritativeFactor = scheduledFactor is >= 0m;
        var factor = hasAuthoritativeFactor ? scheduledFactor.GetValueOrDefault() : 1m;
        var outstanding = RoundBalance(principalBasis * factor);
        if (terms.PrincipalFace is > 0m && terms.MaturityDate is { } maturity)
        {
            var issueDate = terms.IssueDate ?? DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime.Date);
            var contractualPrincipal = (terms.PrincipalSchedule ?? [])
                .Where(entry => entry.PaymentDate >= issueDate && entry.PaymentDate <= maturity);
            if (!hasAuthoritativeFactor)
            {
                // Without an as-of factor, completed contractual payments are the best retained
                // evidence of today's outstanding.
                var paidBeforeAsOf = contractualPrincipal
                    .Where(entry => entry.PaymentDate < asOfDate)
                    .Sum(static entry => entry.Amount);
                outstanding = RoundBalance(decimal.Max(0m, outstanding - paidBeforeAsOf));
            }
            else if ((appliedFactorEntry?.AsOfDate ?? scalarFactorDate) is DateOnly factorEvidenceDate)
            {
                // A DATED factor reflects principal events only up to its as-of date: a January
                // factor of 0.8 does not know about February's contractual payment, so completed
                // payments dated after the factor and before today still reduce the opening
                // balance — otherwise later interest, maturity principal, and ledger postings are
                // overstated. A scalar currentFactor with a retained factorDate is dated evidence
                // too; only a genuinely undated scalar keeps the assumed-current fallback below.
                var paidAfterFactor = contractualPrincipal
                    .Where(entry => entry.PaymentDate > factorEvidenceDate && entry.PaymentDate < asOfDate)
                    .Sum(static entry => entry.Amount);
                outstanding = RoundBalance(decimal.Max(0m, outstanding - paidAfterFactor));
            }
            // Undated scalar factor: assumed to already reflect every completed payment — reducing
            // again would double-count.
        }

        return outstanding;

        // Projections retain their four-place cash amounts. Quotations preserve factor precision
        // until the final market-value calculation, so a small reference face cannot round a large holding.
        decimal RoundBalance(decimal amount)
            => roundToCashPrecision ? decimal.Round(amount, 4, MidpointRounding.AwayFromZero) : amount;
    }

    /// <summary>
    /// The date the security's scalar current factor is effective from, when its terms retain one
    /// (<c>factorDate</c> or <c>currentFactorDate</c>). A scalar dated after a valuation or
    /// projection date does not describe that date's balance; the valuation models apply the same
    /// rule so quoted and projected values read one factor.
    /// </summary>
    private static DateOnly? ReadScalarFactorDate(SecurityDetailDto security)
        => SecurityTermReader.ReadDate(
            EnumerateFactorDateSources(SecurityAssetClassTermsUpgrader.UpgradeForRead(security.AssetClass, security.AssetSpecificTerms)),
            ["factorDate", "currentFactorDate"]);

    /// <summary>
    /// Term sources for the scalar factor-date lookup, in the SAME nested-first precedence
    /// <see cref="StructuredCashFlowTermsResolver"/> uses for the factor and schedule themselves:
    /// a profile-backed record persists its governed scalar terms beneath
    /// <c>profileFields</c>, so probing only the envelope root would miss the retained date and
    /// misread a dated factor as current.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateFactorDateSources(JsonElement assetSpecificTerms)
    {
        if (assetSpecificTerms.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (SecurityTermReader.TryGetProperty(assetSpecificTerms, "profileFields", out var profileFields)
            && profileFields.ValueKind == JsonValueKind.Object)
        {
            yield return profileFields;
        }

        yield return assetSpecificTerms;
    }
}
