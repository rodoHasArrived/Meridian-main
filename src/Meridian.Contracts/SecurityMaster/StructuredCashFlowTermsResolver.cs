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
    // "factorScheduleEntries" is the typed array the F# StructuredCredit serializer emits; the
    // other spellings cover vendor payloads. Free-text "factorSchedule" strings are skipped by the
    // array check below, so the typed key must resolve first.
    private static readonly string[] FactorScheduleAliases = ["factorScheduleEntries", "factorSchedule", "factorSchedules"];
    private static readonly string[] FactorScheduleDateAliases = ["asOfDate", "factorDate", "effectiveDate", "date"];
    private static readonly string[] FactorScheduleFactorAliases = ["factor", "currentFactor"];

    // Contractual principal schedule emitted by the Bond codec.
    private static readonly string[] PrincipalScheduleAliases = ["principalSchedule"];
    private static readonly string[] PrincipalScheduleDateAliases = ["paymentDate"];
    private static readonly string[] PrincipalScheduleAmountAliases = ["amount"];

    // Leg-container keys and, within each leg row, the per-field key aliases. "legType" is the
    // spelling the F# SwapLeg serializer persists; the rest cover vendor variants.
    private static readonly string[] LegContainerAliases = ["legs", "swapLegs", "cashFlowLegs"];
    private static readonly string[] LegIdAliases = ["legId", "id", "name"];
    private static readonly string[] LegRateKindAliases = ["legType", "rateType", "type"];
    private static readonly string[] LegDirectionAliases = ["direction", "payReceive", "payOrReceive", "side"];
    private static readonly string[] LegFixedRateAliases = ["fixedRate", "rate", "couponRate"];
    private static readonly string[] LegIndexAliases = ["index", "indexName", "referenceIndex"];
    private static readonly string[] LegSpreadBpsAliases = ["spreadBps"];
    private static readonly string[] LegCurrentIndexRateAliases = ["currentIndexRate", "lastFixing", "currentRate", "indexRate"];
    private static readonly string[] LegNotionalAliases = ["notional", "notionalAmount", "faceAmount", "principal"];
    private static readonly string[] LegPaymentFrequencyAliases = ["paymentFrequency", "frequency"];
    private static readonly string[] LegDayCountAliases = ["dayCountConvention", "dayCount", "dayCountBasis"];
    private static readonly string[] LegPrincipalExchangeAliases = ["exchangesPrincipal", "principalExchange", "notionalExchange"];

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
            FactorSchedule: ReadFactorSchedule(sources),
            Legs: ReadLegs(sources),
            PrincipalSchedule: ReadPrincipalSchedule(sources));
    }

    private static IReadOnlyList<StructuredPrincipalScheduleEntry> ReadPrincipalSchedule(
        IReadOnlyList<JsonElement> sources)
    {
        foreach (var source in sources)
        {
            foreach (var alias in PrincipalScheduleAliases)
            {
                if (!SecurityTermReader.TryGetProperty(source, alias, out var array) ||
                    array.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var entries = new List<StructuredPrincipalScheduleEntry>();
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var paymentDate = SecurityTermReader.ReadDate(item, PrincipalScheduleDateAliases);
                    var amount = SecurityTermReader.ReadDecimal(item, PrincipalScheduleAmountAliases);
                    if (paymentDate is null || amount is null || amount.Value <= 0m)
                    {
                        continue;
                    }

                    entries.Add(new StructuredPrincipalScheduleEntry(paymentDate.Value, amount.Value));
                }

                // The array's PRESENCE claims ownership, mirroring the factor schedule: a bullet
                // bond's canonical serializer emits principalSchedule: [] to assert "no contractual
                // instalments", so the result returns even when zero rows parse — probing
                // lower-priority sources (including commonTerms) could resurrect a stale
                // pass-through schedule and turn the bullet into a sinker.
                // The ledger bridge derives one principal journal id per security/date, so
                // same-day contractual rows combine before they reach projection consumers.
                return entries
                    .GroupBy(static entry => entry.PaymentDate)
                    .Select(static group => new StructuredPrincipalScheduleEntry(
                        group.Key,
                        group.Sum(static entry => entry.Amount)))
                    .OrderBy(static entry => entry.PaymentDate)
                    .ToArray();
            }
        }

        return Array.Empty<StructuredPrincipalScheduleEntry>();
    }

    private static IReadOnlyList<StructuredCashFlowLeg>? ReadLegs(IReadOnlyList<JsonElement> sources)
    {
        foreach (var source in sources)
        {
            foreach (var alias in LegContainerAliases)
            {
                if (!SecurityTermReader.TryGetProperty(source, alias, out var array) || array.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var legs = new List<StructuredCashFlowLeg>();
                var ordinal = 0;
                foreach (var item in array.EnumerateArray())
                {
                    ordinal++;
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    legs.Add(ReadLeg(item, ordinal));
                }

                if (legs.Count > 0)
                {
                    // First container that yields usable rows wins, mirroring alias priority.
                    return legs;
                }
            }
        }

        return null;
    }

    private static StructuredCashFlowLeg ReadLeg(JsonElement leg, int ordinal)
    {
        var rateKindToken = SecurityTermReader.ReadString(leg, LegRateKindAliases);
        var directionToken = SecurityTermReader.ReadString(leg, LegDirectionAliases);
        var fixedRate = SecurityTermReader.ReadDecimal(leg, LegFixedRateAliases);
        var indexName = SecurityTermReader.ReadString(leg, LegIndexAliases);

        return new StructuredCashFlowLeg(
            LegId: SecurityTermReader.ReadString(leg, LegIdAliases) ?? $"leg-{ordinal}",
            RateKind: ResolveRateKind(rateKindToken, fixedRate, indexName),
            Direction: ResolveDirection(directionToken) ?? ResolveDirection(rateKindToken),
            FixedRate: fixedRate,
            IndexName: indexName,
            SpreadBps: SecurityTermReader.ReadDecimal(leg, LegSpreadBpsAliases),
            CurrentIndexRate: SecurityTermReader.ReadDecimal(leg, LegCurrentIndexRateAliases),
            Notional: SecurityTermReader.ReadDecimal(leg, LegNotionalAliases),
            PaymentFrequency: SecurityTermReader.ReadString(leg, LegPaymentFrequencyAliases),
            DayCountConvention: SecurityTermReader.ReadString(leg, LegDayCountAliases),
            ExchangesPrincipal: ReadBoolean(leg, LegPrincipalExchangeAliases));
    }

    private static CashFlowLegRateKind ResolveRateKind(string? token, decimal? fixedRate, string? indexName)
    {
        if (token is not null)
        {
            var normalized = token.ToUpperInvariant();
            if (normalized.Contains("FLOAT", StringComparison.Ordinal) || normalized.Contains("FLT", StringComparison.Ordinal))
            {
                return CashFlowLegRateKind.Floating;
            }

            if (normalized.Contains("FIX", StringComparison.Ordinal))
            {
                return CashFlowLegRateKind.Fixed;
            }
        }

        if (fixedRate is not null)
        {
            return CashFlowLegRateKind.Fixed;
        }

        return indexName is not null ? CashFlowLegRateKind.Floating : CashFlowLegRateKind.Fixed;
    }

    private static CashFlowLegDirection? ResolveDirection(string? token)
    {
        if (token is null)
        {
            return null;
        }

        var normalized = token.ToUpperInvariant();
        if (normalized.Contains("PAY", StringComparison.Ordinal))
        {
            return CashFlowLegDirection.Pay;
        }

        return normalized.Contains("REC", StringComparison.Ordinal) ? CashFlowLegDirection.Receive : null;
    }

    private static bool ReadBoolean(JsonElement source, string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!SecurityTermReader.TryGetProperty(source, propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return property.GetBoolean();
            }

            if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return false;
    }

    private static IReadOnlyList<StructuredFactorScheduleEntry> ReadFactorSchedule(IReadOnlyList<JsonElement> sources)
    {
        foreach (var source in sources)
        {
            foreach (var alias in FactorScheduleAliases)
            {
                if (!SecurityTermReader.TryGetProperty(source, alias, out var array) || array.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                // The array's PRESENCE claims ownership, and sources enumerate governed-first:
                // a profile-backed record whose governed profileFields carry an EMPTY (or
                // unusable) factorScheduleEntries deliberately asserts "no factor history", so
                // the result returns even when zero rows parse — falling through would let an
                // ungoverned outer pass-through array supply economics governance left empty.
                var entries = new List<StructuredFactorScheduleEntry>();
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

                return DedupeByDate(entries);
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
        // Governed profile fields outrank outer duplicates: profileFields values are schema- and
        // profile-validated on write, while extra outer keys on an envelope are ungoverned
        // pass-through. Probing the outer object first would let an unvalidated outer `maturity`
        // silently shadow the governed one in every projection and conflict comparison.
        if (SecurityTermReader.TryGetProperty(security.AssetSpecificTerms, "profileFields", out var profileFields) &&
            profileFields.ValueKind == JsonValueKind.Object)
        {
            yield return profileFields;
        }

        yield return security.AssetSpecificTerms;
        yield return security.CommonTerms;
    }
}
