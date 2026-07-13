using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Resolves a security's raw term JSON into <see cref="StructuredCashFlowTerms"/>. This is the single
/// place that knows the vendor key aliases for each economic term and how to walk the three term
/// blobs (asset-specific terms, their nested <c>profileFields</c>, then common terms). Value coercion
/// is delegated to <see cref="SecurityTermReader"/> so the cash-flow and obligation projections share
/// one probing implementation instead of re-deriving fuzzy key lookups at each call site.
/// </summary>
public static class StructuredCashFlowTermsResolver
{
    // Known vendor spellings for each term. Order is priority: the first key that resolves wins.
    private static readonly string[] MaturityAliases = ["maturityDate", "maturity", "legalFinalMaturity"];
    private static readonly string[] IssueAliases = ["issueDate", "originationDate", "effectiveDate"];
    private static readonly string[] PrincipalFaceAliases = ["par", "originalFace", "notional", "principal", "principalAmount"];
    private static readonly string[] CurrentFactorAliases = ["currentFactor", "factor"];
    private static readonly string[] CouponRateAliases = ["fixedCouponRate", "couponRate", "coupon", "annualRate"];
    private static readonly string[] PaymentFrequencyAliases = ["paymentFrequency", "paymentFrequencyPerYear", "couponFrequency"];
    private static readonly string[] DayCountAliases = ["dayCountConvention", "dayCount", "dayCountBasis"];

    // Factor-schedule container keys and, within each row, the date/factor key aliases.
    private static readonly string[] FactorScheduleAliases = ["factorSchedule", "factorSchedules"];
    private static readonly string[] FactorScheduleDateAliases = ["asOfDate", "factorDate", "effectiveDate", "date"];
    private static readonly string[] FactorScheduleFactorAliases = ["factor", "currentFactor"];

    /// <summary>Resolves the typed cash flow terms for <paramref name="security"/>.</summary>
    public static StructuredCashFlowTerms Resolve(SecurityDetailDto security)
    {
        ArgumentNullException.ThrowIfNull(security);

        var sources = EnumerateTermSources(security).ToArray();

        return new StructuredCashFlowTerms(
            MaturityDate: SecurityTermReader.ReadDate(sources, MaturityAliases),
            IssueDate: SecurityTermReader.ReadDate(sources, IssueAliases),
            PrincipalFace: SecurityTermReader.ReadDecimal(sources, PrincipalFaceAliases),
            CurrentFactor: SecurityTermReader.ReadDecimal(sources, CurrentFactorAliases),
            CouponRate: SecurityTermReader.ReadDecimal(sources, CouponRateAliases),
            PaymentFrequency: SecurityTermReader.ReadString(sources, PaymentFrequencyAliases),
            DayCountConvention: SecurityTermReader.ReadString(sources, DayCountAliases),
            FactorSchedule: ReadFactorSchedule(sources));
    }

    private static IReadOnlyList<StructuredFactorScheduleEntry> ReadFactorSchedule(IReadOnlyList<JsonElement> sources)
    {
        var entries = new List<StructuredFactorScheduleEntry>();
        foreach (var source in sources)
        {
            foreach (var alias in FactorScheduleAliases)
            {
                if (!SecurityTermReader.TryGetProperty(source, alias, out var array) || array.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var asOf = SecurityTermReader.ReadDate(item, FactorScheduleDateAliases);
                    var factor = SecurityTermReader.ReadDecimal(item, FactorScheduleFactorAliases);
                    if (asOf is null || factor is null)
                    {
                        continue;
                    }

                    entries.Add(new StructuredFactorScheduleEntry(asOf.Value, factor.Value));
                }

                if (entries.Count > 0)
                {
                    // First container that yields usable rows wins, mirroring alias priority.
                    return DedupeByDate(entries);
                }
            }
        }

        return Array.Empty<StructuredFactorScheduleEntry>();
    }

    private static IReadOnlyList<StructuredFactorScheduleEntry> DedupeByDate(IEnumerable<StructuredFactorScheduleEntry> entries)
        => entries
            .GroupBy(static entry => entry.AsOfDate)
            .Select(static group => group.Last())
            .OrderBy(static entry => entry.AsOfDate)
            .ToArray();

    private static IEnumerable<JsonElement> EnumerateTermSources(SecurityDetailDto security)
    {
        yield return security.AssetSpecificTerms;
        if (SecurityTermReader.TryGetProperty(security.AssetSpecificTerms, "profileFields", out var profileFields) &&
            profileFields.ValueKind == JsonValueKind.Object)
        {
            yield return profileFields;
        }

        yield return security.CommonTerms;
    }
}
