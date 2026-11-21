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
    // Top-level terms are read by role: each class declares which of its schema keys play which
    // role on its catalog descriptor, merged with the shared vendor spellings
    // (SecurityCashFlowTermRoles.Spellings, review D9).

    // Swap-leg reads, keyed by the schema path of the field each reads under Swap ("legs" is the
    // container; "legs.resetSchedule.resetDate" a member of a reset entry). Each list is the declared
    // spellings plus vendor variants, in priority order; SecurityCashFlowTermRoleTests holds every
    // list to the schema's spellings for its path. "legType" is the spelling the F# SwapLeg serializer
    // persists.
    private static readonly Dictionary<string, string[]> LegSpellingsByPath = new(StringComparer.Ordinal)
    {
        ["legs"] = ["legs", "swapLegs", "cashFlowLegs"],
        ["legs.legId"] = ["legId", "id", "name"],
        ["legs.legType"] = ["legType", "rateType", "type"],
        ["legs.direction"] = ["direction", "payReceive", "payOrReceive", "side"],
        ["legs.fixedRate"] = ["fixedRate", "rate", "couponRate"],
        ["legs.index"] = ["index", "indexName", "referenceIndex"],
        ["legs.spreadBps"] = ["spreadBps"],
        ["legs.currentIndexRate"] = ["currentIndexRate", "lastFixing", "currentRate", "indexRate"],
        ["legs.notional"] = ["notional", "notionalAmount", "faceAmount", "principal"],
        ["legs.paymentFrequency"] = ["paymentFrequency", "frequency"],
        ["legs.notionalSchedule"] = ["notionalSchedule", "amortizationSchedule"],
        ["legs.resetSchedule"] = ["resetSchedule", "fixings"],
        ["legs.dayCount"] = ["dayCountConvention", "dayCount", "dayCountBasis"],
        ["legs.exchangesPrincipal"] = ["exchangesPrincipal", "principalExchange", "notionalExchange"],
        ["legs.notionalSchedule.effectiveDate"] = ["effectiveDate", "date"],
        ["legs.notionalSchedule.notional"] = ["notional", "amount"],
        // effectiveDate and indexRate are the schema's declared aliases; they are read last, after
        // the vendor spellings that were already read.
        ["legs.resetSchedule.resetDate"] = ["resetDate", "fixingDate", "date", "effectiveDate"],
        ["legs.resetSchedule.fixingRate"] = ["fixingRate", "fixing", "rate", "indexRate"],
    };

    private static readonly string[] LegContainerAliases = LegSpellingsByPath["legs"];
    private static readonly string[] LegIdAliases = LegSpellingsByPath["legs.legId"];
    private static readonly string[] LegRateKindAliases = LegSpellingsByPath["legs.legType"];
    private static readonly string[] LegDirectionAliases = LegSpellingsByPath["legs.direction"];
    private static readonly string[] LegFixedRateAliases = LegSpellingsByPath["legs.fixedRate"];
    private static readonly string[] LegIndexAliases = LegSpellingsByPath["legs.index"];
    private static readonly string[] LegSpreadBpsAliases = LegSpellingsByPath["legs.spreadBps"];
    private static readonly string[] LegCurrentIndexRateAliases = LegSpellingsByPath["legs.currentIndexRate"];
    private static readonly string[] LegNotionalAliases = LegSpellingsByPath["legs.notional"];
    private static readonly string[] LegPaymentFrequencyAliases = LegSpellingsByPath["legs.paymentFrequency"];
    private static readonly string[] LegNotionalScheduleAliases = LegSpellingsByPath["legs.notionalSchedule"];
    private static readonly string[] LegResetScheduleAliases = LegSpellingsByPath["legs.resetSchedule"];
    private static readonly string[] LegDayCountAliases = LegSpellingsByPath["legs.dayCount"];
    private static readonly string[] LegPrincipalExchangeAliases = LegSpellingsByPath["legs.exchangesPrincipal"];
    private static readonly string[] NotionalStepDateAliases = LegSpellingsByPath["legs.notionalSchedule.effectiveDate"];
    private static readonly string[] NotionalStepAmountAliases = LegSpellingsByPath["legs.notionalSchedule.notional"];
    private static readonly string[] ResetDateAliases = LegSpellingsByPath["legs.resetSchedule.resetDate"];
    private static readonly string[] ResetFixingAliases = LegSpellingsByPath["legs.resetSchedule.fixingRate"];

    /// <summary>The swap-leg schema paths the resolver reads, under <c>Swap</c>.</summary>
    public static IReadOnlyCollection<string> LegSpellingPaths => LegSpellingsByPath.Keys;

    /// <summary>The spellings read for the swap-leg field at <paramref name="path"/>, in priority order.</summary>
    public static IReadOnlyList<string> LegSpellings(string path) => LegSpellingsByPath[path];

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

        // A financing trade (repo) declares its start and end dates as its issue and maturity.
        var assetClass = security.AssetClass;
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(assetClass);
        return new StructuredCashFlowTerms(
            MaturityDate: SecurityTermReader.ReadDate(sources, Spellings(assetClass, SecurityCashFlowTermRole.Maturity)),
            IssueDate: SecurityTermReader.ReadDate(sources, Spellings(assetClass, SecurityCashFlowTermRole.Issue)),
            PrincipalFace: SecurityTermReader.ReadDecimal(sources, Spellings(assetClass, SecurityCashFlowTermRole.PrincipalFace)),
            CurrentFactor: SecurityTermReader.ReadDecimal(sources, Spellings(assetClass, SecurityCashFlowTermRole.CurrentFactor)),
            // Discount yield is not a contractual coupon: bills and discount paper pay principal
            // only, and discount accretion belongs to the carrying-value calculation.
            CouponRate: descriptor.PaysNoContractualCoupon
                        || (couponKind == SecurityCouponKind.ZeroCoupon && descriptor.CarriesBondCouponStructure)
                ? 0m
                : SecurityTermReader.ReadDecimal(sources, Spellings(assetClass, SecurityCashFlowTermRole.CouponRate)),
            PaymentFrequency: SecurityTermReader.ReadString(sources, Spellings(assetClass, SecurityCashFlowTermRole.PaymentFrequency)),
            DayCountConvention: SecurityTermReader.ReadString(sources, Spellings(assetClass, SecurityCashFlowTermRole.DayCount)),
            FactorSchedule: schedules.FactorSchedule.Entries,
            Legs: ReadLegs(sources) ?? ReadBondFloatingLeg(assetClass, sources, couponKind, rateUnit),
            PrincipalSchedule: schedules.PrincipalSchedule.IsPresent ? schedules.PrincipalSchedule.Entries : null,
            StepCouponSchedule: schedules.StepCouponSchedule.IsPresent ? schedules.StepCouponSchedule.Entries : null,
            InflationIndex: SecurityTermReader.ReadString(sources, Spellings(assetClass, SecurityCashFlowTermRole.InflationIndex)),
            InflationBaseIndexValue: SecurityTermReader.ReadDecimal(sources, Spellings(assetClass, SecurityCashFlowTermRole.InflationBaseIndexValue)),
            InflationIndexRatio: SecurityTermReader.ReadDecimal(sources, Spellings(assetClass, SecurityCashFlowTermRole.InflationIndexRatio)))
        {
            CouponKind = couponKind,
            CouponRateUnit = rateUnit,
            RecordRateUnit = SecurityRecordRateUnit.Read(security.AssetClass, security.AssetSpecificTerms),
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
                Notional: SecurityTermReader.ReadDecimal(sources, Spellings(assetClass, SecurityCashFlowTermRole.PrincipalFace)),
                PaymentFrequency: SecurityTermReader.ReadString(sources, Spellings(assetClass, SecurityCashFlowTermRole.PaymentFrequency)),
                DayCountConvention: SecurityTermReader.ReadString(sources, Spellings(assetClass, SecurityCashFlowTermRole.DayCount)),
                ExchangesPrincipal: true,
                ResetSchedule: resets.Select(static item => (
                        Date: SecurityTermReader.ReadDate(item, ResetDateAliases),
                        Rate: SecurityTermReader.ReadDecimal(item, ResetFixingAliases)))
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

    private static string[] Spellings(string assetClass, SecurityCashFlowTermRole role)
        => SecurityCashFlowTermRoles.Spellings(assetClass, role);

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
                .Select(static item => (Date: SecurityTermReader.ReadDate(item, NotionalStepDateAliases), Notional: SecurityTermReader.ReadDecimal(item, NotionalStepAmountAliases)))
                .Where(static step => step.Date is not null && step.Notional is > 0m)
                .Select(static step => new StructuredCashFlowNotionalStep(step.Date!.Value, step.Notional!.Value))
                .ToArray(),
            ResetSchedule: ReadScheduleItems(leg, LegResetScheduleAliases)
                .Select(static item => (Date: SecurityTermReader.ReadDate(item, ResetDateAliases), Fixing: SecurityTermReader.ReadDecimal(item, ResetFixingAliases)))
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
