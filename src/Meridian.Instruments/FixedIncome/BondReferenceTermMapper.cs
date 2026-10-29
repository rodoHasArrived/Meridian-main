using System.Text.Json;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.FixedIncome;

/// <summary>Preserves canonical bond structures that do not have satellite projection columns.</summary>
internal static class BondReferenceTermMapper
{
    public static BondReferenceDto Enrich(BondReferenceDto reference, JsonElement? canonicalTerms)
    {
        if (canonicalTerms is not { ValueKind: JsonValueKind.Object } terms)
            return reference;

        var sources = Sources(terms).ToArray();
        var principal = Element(sources, "principalSchedule", JsonValueKind.Array);
        BondSinkingFundDto? sinking = null;
        if (principal is { } schedule)
        {
            var entries = new List<BondSinkingFundEntryDto>();
            foreach (var entry in schedule.EnumerateArray())
            {
                var date = SecurityTermReader.ReadDate(entry, "paymentDate", "sinkDate");
                var amount = SecurityTermReader.ReadDecimal(entry, "amount");
                if (date is not null && amount is not null)
                    entries.Add(new BondSinkingFundEntryDto(date.Value, amount.Value));
            }
            sinking = new(reference.SecurityId, entries.OrderBy(static entry => entry.SinkDate).ToArray(),
                SecurityTermReader.ReadString(sources, "sinkFrequency"), false, reference.Version);
        }

        var inflationIndex = SecurityTermReader.ReadString(sources, "inflationIndex");
        var ratio = SecurityTermReader.ReadDecimal(sources, "inflationIndexRatio", "indexRatio");
        var inflation = inflationIndex is not null || ratio is not null
            ? new BondInflationLinkedDto(reference.SecurityId, inflationIndex,
                SecurityTermReader.ReadDecimal(sources, "inflationBaseIndexValue", "baseIndexValue"),
                SecurityTermReader.ReadDate(sources, "inflationBaseDate", "baseDate"), ratio,
                SecurityTermReader.ReadDate(sources, "inflationFactorDate", "indexRatioDate"),
                SecurityTermReader.ReadDecimal(sources, "inflationIndexFloor", "indexFloor"), reference.Version)
            : null;

        var factor = SecurityTermReader.ReadDecimal(sources, "currentFactor", "factor");
        var factorDate = SecurityTermReader.ReadDate(sources, "factorDate", "currentFactorDate");
        var factors = Element(sources, "factorScheduleEntries", JsonValueKind.Array)
            ?? Element(sources, "factorSchedule", JsonValueKind.Array);
        var principalFactor = factor is not null || factors is not null
            ? new BondPrincipalFactorDto(factor, factorDate, factors) : null;

        var declared = SecurityVersionedTerms.ReadDeclaredScheduleConventions("Bond", terms);
        var conventions = declared.Status == SecurityDeclaredConventionsStatus.Declared ? declared.Block : null;
        var decoding = SecurityVersionedTerms.Decoding("Bond", terms, writeMode: false);
        var calls = decoding.Decodes(SecurityAssetTermsSchema.Field("Bond", SecurityVersionedTerms.CallScheduleKey)!)
            ? Element([terms], SecurityVersionedTerms.CallScheduleKey, JsonValueKind.Array) : null;
        var puts = decoding.Decodes(SecurityAssetTermsSchema.Field("Bond", SecurityVersionedTerms.PutScheduleKey)!)
            ? Element([terms], SecurityVersionedTerms.PutScheduleKey, JsonValueKind.Array) : null;
        var couponKind = SecurityTermReader.ReadString(sources, "couponType") ?? reference.AccrualConvention?.CouponKind;
        var cap = SecurityTermReader.ReadDecimal(sources, "capRate");
        var floor = SecurityTermReader.ReadDecimal(sources, "floorRate");
        var steps = Element(sources, "stepSchedule", JsonValueKind.Array);
        return reference with
        {
            SinkingFund = sinking,
            InflationLinked = inflation,
            PrincipalFactor = principalFactor,
            CouponStructure = couponKind is not null || cap is not null || floor is not null || steps is not null
                ? new BondCouponStructureDto(couponKind, cap, floor, steps, conventions?.CouponRateUnit) : null,
            ScheduleConventions = conventions,
            CallSchedule = calls,
            PutSchedule = puts
        };
    }

    private static IEnumerable<JsonElement> Sources(JsonElement terms)
    {
        if (SecurityTermReader.TryGetProperty(terms, "profileFields", out var profile) && profile.ValueKind == JsonValueKind.Object)
            yield return profile;
        yield return terms;
    }

    private static JsonElement? Element(IEnumerable<JsonElement> sources, string key, JsonValueKind kind)
    {
        foreach (var source in sources)
            if (SecurityTermReader.TryGetProperty(source, key, out var value) && value.ValueKind == kind)
                return value.Clone();
        return null;
    }
}
