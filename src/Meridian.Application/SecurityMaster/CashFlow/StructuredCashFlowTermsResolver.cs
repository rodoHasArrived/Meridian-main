using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster.CashFlow;

/// <summary>
/// Resolves a security's raw term JSON into <see cref="StructuredCashFlowTerms"/>. This is the single
/// place that knows the vendor key aliases for each economic term and how to walk the three term
/// blobs (asset-specific terms, their nested <c>profileFields</c>, then common terms). Centralizing
/// the probing keeps the projection and ledger code working against a typed contract instead of
/// re-deriving fuzzy key lookups at each call site.
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
            MaturityDate: ReadDate(sources, MaturityAliases),
            IssueDate: ReadDate(sources, IssueAliases),
            PrincipalFace: ReadDecimal(sources, PrincipalFaceAliases),
            CurrentFactor: ReadDecimal(sources, CurrentFactorAliases),
            CouponRate: ReadDecimal(sources, CouponRateAliases),
            PaymentFrequency: ReadString(sources, PaymentFrequencyAliases),
            DayCountConvention: ReadString(sources, DayCountAliases),
            FactorSchedule: ReadFactorSchedule(sources));
    }

    private static IReadOnlyList<StructuredFactorScheduleEntry> ReadFactorSchedule(IReadOnlyList<JsonElement> sources)
    {
        var entries = new List<StructuredFactorScheduleEntry>();
        foreach (var source in sources)
        {
            foreach (var alias in FactorScheduleAliases)
            {
                if (!TryGetProperty(source, alias, out var array) || array.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var row = new[] { item };
                    var asOf = ReadDate(row, FactorScheduleDateAliases);
                    var factor = ReadDecimal(row, FactorScheduleFactorAliases);
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

    private static string? ReadString(IReadOnlyList<JsonElement> sources, string[] propertyNames)
    {
        foreach (var source in sources)
        {
            foreach (var propertyName in propertyNames)
            {
                if (!TryGetProperty(source, propertyName, out var property))
                {
                    continue;
                }

                if (property.ValueKind == JsonValueKind.String)
                {
                    var value = property.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
                else if (property.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    return property.ToString();
                }
            }
        }

        return null;
    }

    private static decimal? ReadDecimal(IReadOnlyList<JsonElement> sources, string[] propertyNames)
    {
        foreach (var source in sources)
        {
            foreach (var propertyName in propertyNames)
            {
                if (!TryGetProperty(source, propertyName, out var property))
                {
                    continue;
                }

                if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number))
                {
                    return number;
                }

                if (property.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(property.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static DateOnly? ReadDate(IReadOnlyList<JsonElement> sources, string[] propertyNames)
    {
        foreach (var source in sources)
        {
            foreach (var propertyName in propertyNames)
            {
                if (!TryGetProperty(source, propertyName, out var property) ||
                    property.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var raw = property.GetString();
                if (DateOnly.TryParse(raw, out var date))
                {
                    return date;
                }

                if (DateTimeOffset.TryParse(raw, out var timestamp))
                {
                    return DateOnly.FromDateTime(timestamp.UtcDateTime.Date);
                }
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> EnumerateTermSources(SecurityDetailDto security)
    {
        yield return security.AssetSpecificTerms;
        if (TryGetProperty(security.AssetSpecificTerms, "profileFields", out var profileFields) &&
            profileFields.ValueKind == JsonValueKind.Object)
        {
            yield return profileFields;
        }

        yield return security.CommonTerms;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
