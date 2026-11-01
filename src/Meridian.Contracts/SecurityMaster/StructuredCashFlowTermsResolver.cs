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

    // Inflation-linked indexation emitted by the Bond codec for couponType = "InflationLinked".
    private static readonly string[] InflationIndexAliases = ["inflationIndex"];
    private static readonly string[] InflationBaseIndexValueAliases = ["inflationBaseIndexValue", "baseIndexValue"];
    private static readonly string[] InflationIndexRatioAliases = ["inflationIndexRatio", "indexRatio"];

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
    private static readonly string[] LegNotionalScheduleAliases = ["notionalSchedule", "amortizationSchedule"];
    private static readonly string[] LegResetScheduleAliases = ["resetSchedule", "fixings"];
    private static readonly string[] LegDayCountAliases = ["dayCountConvention", "dayCount", "dayCountBasis"];
    private static readonly string[] LegPrincipalExchangeAliases = ["exchangesPrincipal", "principalExchange", "notionalExchange"];

    /// <summary>Resolves the typed cash flow terms for <paramref name="security"/>.</summary>
    public static StructuredCashFlowTerms Resolve(SecurityDetailDto security)
    {
        ArgumentNullException.ThrowIfNull(security);

        var assetSpecificTerms = SecurityAssetClassTermsUpgrader.UpgradeForRead(security.AssetClass, security.AssetSpecificTerms);
        var sources = EnumerateTermSources(assetSpecificTerms, security.CommonTerms).ToArray();
        var schedules = SecurityEconomicScheduleParser.Parse(assetSpecificTerms, security.CommonTerms);
        schedules.ThrowIfInvalid();

        var couponKind = ResolveCouponKind(sources);
        var declared = SecurityVersionedTerms.ReadDeclaredScheduleConventions(security.AssetClass, security.AssetSpecificTerms);
        var rateUnit = declared.Block.CouponRateUnit switch
        {
            "Percent" => SecurityCouponRateUnit.Percent,
            "Fraction" => SecurityCouponRateUnit.Fraction,
            _ => SecurityCouponRateUnit.Unspecified
        };

        // A financing trade (repo) has a start and an end date rather than an issue and a maturity.
        var isFinancing = SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).CashFlowBehavior == SecurityCashFlowBehavior.FinancingInterest;
        return new StructuredCashFlowTerms(
            MaturityDate: SecurityTermReader.ReadDate(sources, isFinancing ? ["endDate", .. MaturityAliases] : MaturityAliases),
            IssueDate: SecurityTermReader.ReadDate(sources, isFinancing ? ["startDate", .. IssueAliases] : IssueAliases),
            PrincipalFace: SecurityTermReader.ReadDecimal(sources, PrincipalFaceAliases),
            CurrentFactor: SecurityTermReader.ReadDecimal(sources, CurrentFactorAliases),
            CouponRate: couponKind == SecurityCouponKind.ZeroCoupon
                && SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).CarriesBondCouponStructure
                    ? 0m : ResolveCouponRate(security.AssetClass, sources),
            PaymentFrequency: SecurityTermReader.ReadString(sources, PaymentFrequencyAliases),
            DayCountConvention: SecurityTermReader.ReadString(sources, DayCountAliases),
            FactorSchedule: schedules.FactorSchedule.Entries,
            Legs: ReadLegs(sources) ?? ReadBondFloatingLeg(security.AssetClass, sources, couponKind, rateUnit),
            PrincipalSchedule: schedules.PrincipalSchedule.IsPresent ? schedules.PrincipalSchedule.Entries : null,
            StepCouponSchedule: schedules.StepCouponSchedule.IsPresent ? schedules.StepCouponSchedule.Entries : null,
            InflationIndex: SecurityTermReader.ReadString(sources, InflationIndexAliases),
            InflationBaseIndexValue: SecurityTermReader.ReadDecimal(sources, InflationBaseIndexValueAliases),
            InflationIndexRatio: SecurityTermReader.ReadDecimal(sources, InflationIndexRatioAliases))
        {
            CouponKind = couponKind,
            CouponRateUnit = rateUnit,
            CapRate = SecurityTermReader.ReadDecimal(sources, "capRate"),
            FloorRate = SecurityTermReader.ReadDecimal(sources, "floorRate")
        };
    }

    private static SecurityCouponKind ResolveCouponKind(IReadOnlyList<JsonElement> sources)
    {
        foreach (var source in sources)
        {
            var token = SecurityTermReader.ReadString(source, "couponType", "couponKind");
            if (token is null && SecurityTermReader.TryGetProperty(source, "coupon", out var coupon))
            {
                token = SecurityTermReader.ReadString(coupon, "kind");
            }
            if (token is not null)
            {
                return Enum.TryParse<SecurityCouponKind>(token, ignoreCase: true, out var kind)
                    && Enum.IsDefined(kind) && !int.TryParse(token, out _) ? kind : SecurityCouponKind.Unknown;
            }
        }
        return SecurityCouponKind.Unknown;
    }

    private static IReadOnlyList<StructuredCashFlowLeg>? ReadBondFloatingLeg(
        string assetClass, IReadOnlyList<JsonElement> sources, SecurityCouponKind kind, SecurityCouponRateUnit unit)
    {
        if (kind != SecurityCouponKind.Floating || !SecurityAssetClassCatalog.GetOrDefault(assetClass).CarriesBondCouponStructure)
        {
            return null;
        }

        // An explicitly empty governed fixing schedule asserts no observations. Do not resurrect
        // lower-priority pass-through fixings, which would turn forecasts into observed rates.
        IReadOnlyList<JsonElement> resets = [];
        var hasDeclaredResets = false;
        foreach (var source in sources)
        {
            if (LegResetScheduleAliases.Any(alias => SecurityTermReader.TryGetProperty(source, alias, out var value)
                && value.ValueKind == JsonValueKind.Array))
            {
                resets = ReadScheduleItems(source, LegResetScheduleAliases);
                hasDeclaredResets = true;
                break;
            }
        }
        return
        [
            new StructuredCashFlowLeg(
                "bond-coupon", CashFlowLegRateKind.Floating, CashFlowLegDirection.Receive,
                FixedRate: null,
                IndexName: SecurityTermReader.ReadString(sources, "floatingIndex", "floatingRateIndex"),
                SpreadBps: SecurityTermReader.ReadDecimal(sources, "spreadBps", "floatingSpreadBps"),
                CurrentIndexRate: SecurityTermReader.ReadDecimal(sources, LegCurrentIndexRateAliases),
                Notional: SecurityTermReader.ReadDecimal(sources, PrincipalFaceAliases),
                PaymentFrequency: SecurityTermReader.ReadString(sources, PaymentFrequencyAliases),
                DayCountConvention: SecurityTermReader.ReadString(sources, DayCountAliases),
                ExchangesPrincipal: true,
                ResetSchedule: resets.Select(static item => (
                        Date: SecurityTermReader.ReadDate(item, "resetDate", "fixingDate", "date"),
                        Rate: SecurityTermReader.ReadDecimal(item, "fixingRate", "fixing", "rate")))
                    .Where(static item => item.Date.HasValue)
                    .Select(static item => new StructuredCashFlowResetFixing(item.Date!.Value, item.Rate)).ToArray())
            {
                CapRate = SecurityTermReader.ReadDecimal(sources, "capRate"),
                FloorRate = SecurityTermReader.ReadDecimal(sources, "floorRate"),
                RateUnit = unit,
                HasDeclaredResetSchedule = hasDeclaredResets,
                CurrentIndexResetDate = SecurityTermReader.ReadDate(sources, "currentIndexResetDate", "currentIndexRateDate"),
                CurrentIndexRateSource = SecurityTermReader.ReadString(sources, "currentIndexRateSource")
            }
        ];
    }

    // Discount yield is not a contractual coupon. Bills and discount paper pay principal only;
    // discount accretion belongs to the carrying-value calculation, not coupon accrual.
    private static decimal? ResolveCouponRate(string assetClass, JsonElement[] sources)
        => SecurityAssetClassCatalog.GetOrDefault(assetClass).AssetClass switch
        {
            "TreasuryBill" or "CommercialPaper" => 0m,
            "Deposit" => SecurityTermReader.ReadDecimal(sources, ["interestRate", .. CouponRateAliases]),
            "Repo" => SecurityTermReader.ReadDecimal(sources, ["repoRate", .. CouponRateAliases]),
            "DirectLoan" => SecurityTermReader.ReadDecimal(sources, ["currentCouponRate", .. CouponRateAliases]),
            // Only a numeric couponOrIndex is a fixed rate. An index name is unresolved economics.
            "StructuredCredit" => SecurityTermReader.ReadDecimal(sources, [.. CouponRateAliases, "couponOrIndex"]),
            _ => SecurityTermReader.ReadDecimal(sources, CouponRateAliases)
        };

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
            ExchangesPrincipal: ReadBoolean(leg, LegPrincipalExchangeAliases),
            NotionalSchedule: ReadScheduleItems(leg, LegNotionalScheduleAliases)
                .Select(static item => (Date: SecurityTermReader.ReadDate(item, "effectiveDate", "date"), Notional: SecurityTermReader.ReadDecimal(item, "notional", "amount")))
                .Where(static step => step.Date is not null && step.Notional is > 0m)
                .Select(static step => new StructuredCashFlowNotionalStep(step.Date!.Value, step.Notional!.Value))
                .ToArray(),
            ResetSchedule: ReadScheduleItems(leg, LegResetScheduleAliases)
                .Select(static item => (Date: SecurityTermReader.ReadDate(item, "resetDate", "fixingDate", "date"), Fixing: SecurityTermReader.ReadDecimal(item, "fixingRate", "fixing", "rate")))
                .Where(static entry => entry.Date is not null)
                .Select(static entry => new StructuredCashFlowResetFixing(entry.Date!.Value, entry.Fixing))
                .ToArray());
    }

    // Read-side tolerance: an entry that cannot be typed is skipped (the write path refuses it).
    private static IReadOnlyList<JsonElement> ReadScheduleItems(JsonElement leg, string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (SecurityTermReader.TryGetProperty(leg, alias, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object).ToArray();
            }
        }

        return [];
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

    /// <summary>
    /// Enumerates governed profile fields, outer asset terms, and common terms in authority order.
    /// The asset terms are read at the class's current terms version
    /// (<see cref="SecurityAssetClassTermsUpgrader"/>, ADR-024).
    /// </summary>
    public static IEnumerable<JsonElement> EnumerateTermSources(SecurityDetailDto security)
    {
        ArgumentNullException.ThrowIfNull(security);
        var assetSpecificTerms = SecurityAssetClassTermsUpgrader.UpgradeForRead(security.AssetClass, security.AssetSpecificTerms);
        return EnumerateTermSources(assetSpecificTerms, security.CommonTerms);
    }

    private static IEnumerable<JsonElement> EnumerateTermSources(JsonElement assetSpecificTerms, JsonElement commonTerms)
    {
        // Governed profile fields outrank outer duplicates: profileFields values are schema- and
        // profile-validated on write, while extra outer keys on an envelope are ungoverned
        // pass-through. Probing the outer object first would let an unvalidated outer `maturity`
        // silently shadow the governed one in every projection and conflict comparison.
        if (SecurityTermReader.TryGetProperty(assetSpecificTerms, "profileFields", out var profileFields) &&
            profileFields.ValueKind == JsonValueKind.Object)
        {
            yield return profileFields;
        }

        yield return assetSpecificTerms;
        yield return commonTerms;
    }
}
