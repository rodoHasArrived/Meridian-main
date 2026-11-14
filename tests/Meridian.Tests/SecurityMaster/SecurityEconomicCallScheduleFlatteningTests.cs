using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Pins ADR-027 stage 1 increment 1: the economic Call module's schedule lists land in the flat
/// <c>callSchedule</c>/<c>putSchedule</c> arrays for the classes that declare them, in the frozen
/// ADR-024 entry form, stamped at the arrays' terms version. The conversion rules are declared
/// semantics, pinned here exactly: <c>callPx</c> is a percentage of par, a par call writes 100 at
/// that basis, <c>callType</c> "MakeWhole" is a price type carrying the module-level spread while
/// "European"/"American"/"Bermudan" are exercise styles, and members the economic family never
/// carried stay null (ADR-025's defaults govern their reads).
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityEconomicCallScheduleFlatteningTests
{
    [Fact]
    public void Bond_CallAndPutLists_LandInTheFrozenFlatForm()
    {
        var flat = Convert("""
            {"termsFormat":"economic","schemaVersion":2,
             "maturity":{"maturityDate":"2032-06-30"},
             "call":{"isCallable":true,"firstCallDate":"2027-01-01","makeWholeSpreadBps":null,
                     "callSchedule":[
                        {"callDt":"2027-01-01","callPx":102.5,"isParCall":false,"callType":"American"},
                        {"callDt":"2028-01-01","callPx":null,"isParCall":true,"callType":null}],
                     "isPuttable":true,
                     "putSchedule":[{"putDt":"2029-01-01","putPx":99.0}]}}
            """, "Bond");

        flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32()
            .Should().Be(3, "the flattened document holds the version-3 arrays, so it stamps at their version");

        var calls = flat.GetProperty("callSchedule");
        calls.GetArrayLength().Should().Be(2);
        AssertFrozenMembers(calls[0], ["callDate", "callEndDate", "callPrice", "exerciseFrequency", "exerciseStyle",
            "makeWholeSpreadBps", "maximumNoticeDays", "minimumNoticeDays", "noticeDayType",
            "partialRedemption", "priceBasis", "priceType"]);
        calls[0].GetProperty("callDate").GetString().Should().Be("2027-01-01");
        calls[0].GetProperty("callPrice").GetDecimal().Should().Be(102.5m);
        calls[0].GetProperty("priceBasis").GetString().Should().Be("PercentOfPar");
        calls[0].GetProperty("priceType").GetString().Should().Be("Fixed");
        calls[0].GetProperty("exerciseStyle").GetString().Should().Be("American");
        calls[0].GetProperty("callEndDate").ValueKind.Should().Be(JsonValueKind.Null, "windows were never carried by the economic family");

        // A par call without an explicit price is 100% of par: that is what a par call means.
        calls[1].GetProperty("callPrice").GetDecimal().Should().Be(100m);
        calls[1].GetProperty("priceBasis").GetString().Should().Be("PercentOfPar");
        calls[1].GetProperty("exerciseStyle").ValueKind.Should().Be(JsonValueKind.Null);

        var puts = flat.GetProperty("putSchedule");
        puts.GetArrayLength().Should().Be(1);
        AssertFrozenMembers(puts[0], ["exerciseFrequency", "exerciseStyle", "maximumNoticeDays", "minimumNoticeDays",
            "noticeDayType", "priceBasis", "putDate", "putEndDate", "putPrice"]);
        puts[0].GetProperty("putDate").GetString().Should().Be("2029-01-01");
        puts[0].GetProperty("putPrice").GetDecimal().Should().Be(99.0m);
        puts[0].GetProperty("priceBasis").GetString().Should().Be("PercentOfPar");

        // The source document stays retained until every module is recoverable (stage 1 end state).
        flat.TryGetProperty(SecurityTermsEnvelope.RetainedEconomicTermsKey, out _).Should().BeTrue();
    }

    [Fact]
    public void MakeWholeCallType_IsAPriceType_CarryingTheModuleSpread()
    {
        var flat = Convert("""
            {"call":{"makeWholeSpreadBps":50,
                     "callSchedule":[{"callDt":"2027-01-01","callPx":null,"isParCall":false,"callType":"MakeWhole"}]}}
            """, "Bond");

        var entry = flat.GetProperty("callSchedule")[0];
        entry.GetProperty("priceType").GetString().Should().Be("MakeWhole");
        entry.GetProperty("makeWholeSpreadBps").GetDecimal().Should().Be(50m);
        entry.GetProperty("exerciseStyle").ValueKind.Should().Be(JsonValueKind.Null, "MakeWhole names pricing, not an exercise style");
        entry.GetProperty("callPrice").ValueKind.Should().Be(JsonValueKind.Null);
        entry.GetProperty("priceBasis").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("Bond", 3)]
    [InlineData("CertificateOfDeposit", 3)]
    [InlineData("StructuredCredit", 4)]
    public void DeclaringClasses_StampAtTheirArrayVersion(string assetClass, int expectedVersion)
    {
        var flat = Convert("""
            {"call":{"callSchedule":[{"callDt":"2027-01-01","callPx":101.0,"isParCall":false,"callType":"European"}]}}
            """, assetClass);

        flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(expectedVersion);
        flat.GetProperty("callSchedule").GetArrayLength().Should().Be(1);
        flat.GetProperty("putSchedule").GetArrayLength().Should().Be(0, "the flat writer emits both arrays together");
    }

    [Fact]
    public void ConvertedDocument_ReadsBackAndItsStampDescribesItsContent()
    {
        var flat = Convert("""
            {"maturity":{"maturityDate":"2032-06-30"},
             "call":{"callSchedule":[{"callDt":"2027-01-01","callPx":102.5,"isParCall":false,"callType":"European"}],
                     "putSchedule":[]}}
            """, "Bond");

        var readable = () => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms("Bond", flat);
        readable.Should().NotThrow("the recovered document must pass the read guard at its own stamp");
        SecurityTermsEnvelope.GetDocumentStampVersion("Bond", flat)
            .Should().Be(flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32(),
                "the S2 stamp describes the document: re-stamping it would not change it");
    }

    // ── Schedule conventions (ADR-027 stage 1, increment 2) ─────────────────

    [Fact]
    public void Bond_AccrualConventions_LandInTheFrozenBlock()
    {
        var flat = Convert("""
            {"maturity":{"maturityDate":"2032-06-30"},
             "accrual":{"businessDayConvention":"ModifiedFollowing","holidayCalendar":"USNY","dayCount":"Thirty360"}}
            """, "Bond");

        flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32()
            .Should().Be(2, "the document holds the version-2 conventions block and no schedule arrays");

        var block = flat.GetProperty("scheduleConventions");
        AssertFrozenMembers(block, ["accrualAdjusted", "businessDayConvention", "couponCashBasis", "couponRateUnit",
            "direction", "endOfMonthRule", "firstRegularDate", "lastRegularDate", "paymentCalendars",
            "paymentLagBusinessDays", "rollConvention", "stubPolicy"]);
        block.GetProperty("businessDayConvention").GetString().Should().Be("ModifiedFollowing", "the verbatim spelling the flat parser recognizes");
        block.GetProperty("paymentCalendars").GetArrayLength().Should().Be(1);
        block.GetProperty("paymentCalendars")[0].GetString().Should().Be("USNY", "a single joint calendar id is one payment calendar");
        block.GetProperty("rollConvention").ValueKind.Should().Be(JsonValueKind.Null, "members the economic family never carried stay null");

        flat.TryGetProperty("callSchedule", out _).Should().BeFalse("the stamp sits below the arrays' version, so their keys are absent");
        flat.GetProperty("dayCount").GetString().Should().Be("Thirty360", "the scalar mappings are untouched");
    }

    [Fact]
    public void JointCalendarId_SplitsBackIntoItsPaymentCalendars()
    {
        // The economic holidayCalendar is BusinessDayCalendars.JointCalendarId's +-joined form;
        // the recovery inverts it exactly (the forward rule already canonicalized the parts).
        var flat = Convert("""
            {"accrual":{"holidayCalendar":"GBLO+USNY"}}
            """, "Bond");

        var calendars = flat.GetProperty("scheduleConventions").GetProperty("paymentCalendars");
        calendars.GetArrayLength().Should().Be(2);
        calendars[0].GetString().Should().Be("GBLO");
        calendars[1].GetString().Should().Be("USNY");

        var malformed = () => Convert("""
            {"accrual":{"holidayCalendar":"GBLO++USNY"}}
            """, "Bond");
        malformed.Should().Throw<InvalidOperationException>().WithMessage("Declared schedule economic replay requires retained legacy asset-specific terms;*");
    }

    [Fact]
    public void ConventionsAndSchedulesTogether_StampAtTheHigherVersion()
    {
        var flat = Convert("""
            {"accrual":{"businessDayConvention":"Following"},
             "call":{"callSchedule":[{"callDt":"2027-01-01","callPx":101.0,"isParCall":false,"callType":"European"}]}}
            """, "Bond");

        flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(3);
        flat.GetProperty("scheduleConventions").GetProperty("businessDayConvention").GetString().Should().Be("Following");
        flat.GetProperty("callSchedule").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void SchedulesOnly_CarryTheNullConventionsKey_PerTheFrozenFill()
    {
        var flat = Convert("""
            {"call":{"callSchedule":[{"callDt":"2027-01-01","callPx":101.0,"isParCall":false,"callType":"European"}]}}
            """, "Bond");

        // Stamped at 3, at or above the block's Since 2: the key is present, null when nothing is
        // declared (ADR-024 decision 3's frozen fill).
        flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(3);
        flat.GetProperty("scheduleConventions").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void StructuredNote_DeclaresNeitherHome_SoBothStillRefuse()
    {
        // StructuredNote declares neither the scheduleConventions block nor the
        // callSchedule/putSchedule arrays (the Since-2 block near its schema entry belongs to
        // Swap.legs); declared schedule content keeps the pre-increment refusal.
        var withConventions = () => Convert("""
            {"accrual":{"holidayCalendar":"GBLO"}}
            """, "StructuredNote");
        withConventions.Should().Throw<InvalidOperationException>().WithMessage("Declared schedule economic replay requires retained legacy asset-specific terms;*");

        var withCalls = () => Convert("""
            {"call":{"callSchedule":[{"callDt":"2027-01-01"}]}}
            """, "StructuredNote");
        withCalls.Should().Throw<InvalidOperationException>().WithMessage("Declared schedule economic replay requires retained legacy asset-specific terms;*");
    }

    [Fact]
    public void CertificateOfDeposit_RecoversConventions_AtItsBlockVersion()
    {
        var flat = Convert("""
            {"accrual":{"holidayCalendar":"GBLO"}}
            """, "CertificateOfDeposit");
        flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(2);
        flat.GetProperty("scheduleConventions").GetProperty("paymentCalendars")[0].GetString().Should().Be("GBLO");
    }

    [Fact]
    public void StructuredCredit_ConventionsStampAtThree_SchedulesPushToFour()
    {
        var conventionsOnly = Convert("""
            {"accrual":{"businessDayConvention":"ModifiedFollowing"}}
            """, "StructuredCredit");
        conventionsOnly.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(3);
        conventionsOnly.TryGetProperty("callSchedule", out _).Should().BeFalse();

        var both = Convert("""
            {"accrual":{"businessDayConvention":"ModifiedFollowing"},
             "call":{"callSchedule":[{"callDt":"2027-01-01","callPx":101.0,"isParCall":false,"callType":"European"}]}}
            """, "StructuredCredit");
        both.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32().Should().Be(4);
        both.GetProperty("scheduleConventions").GetProperty("businessDayConvention").GetString().Should().Be("ModifiedFollowing");
    }

    [Fact]
    public void ConventionsDocument_ReadsBackAndItsStampDescribesItsContent()
    {
        var flat = Convert("""
            {"maturity":{"maturityDate":"2032-06-30"},
             "accrual":{"businessDayConvention":"ModifiedFollowing","holidayCalendar":"USNY"}}
            """, "Bond");

        var readable = () => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms("Bond", flat);
        readable.Should().NotThrow();
        SecurityTermsEnvelope.GetDocumentStampVersion("Bond", flat)
            .Should().Be(flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32());
    }

    [Fact]
    public void UndeclaredCallTypeLabels_RideBackIntoExerciseStyleVerbatim()
    {
        // The flat entry field preserves undeclared exercise-style strings
        // (WithVerbatimUndeclaredValue) and the economic upgrade copies them into callType
        // unchanged, so the recovery keeps them: nulling the label would silently lose it on the
        // flat → economic → flat round-trip (a Codex review finding on the first cut).
        var flat = Convert("""
            {"call":{"callSchedule":[
                {"callDt":"2027-01-01","callPx":101.0,"isParCall":false,"callType":"AnyTime"},
                {"callDt":"2028-01-01","callPx":null,"isParCall":false,"callType":"makewhole"}]}}
            """, "Bond");

        var calls = flat.GetProperty("callSchedule");
        calls[0].GetProperty("exerciseStyle").GetString().Should().Be("AnyTime");
        calls[0].GetProperty("priceType").GetString().Should().Be("Fixed", "the priced entry still prices as Fixed");
        calls[1].GetProperty("exerciseStyle").GetString().Should().Be("makewhole",
            "comparisons are ordinal, exactly as the economic upgrade's: a case-variant MakeWhole is a verbatim label, not a price type");
        calls[1].GetProperty("priceType").ValueKind.Should().Be(JsonValueKind.Null);
        calls[1].GetProperty("makeWholeSpreadBps").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Factor schedule entries (ADR-027 stage 1, increment 4) ──────────────

    [Fact]
    public void StructuredCredit_FactorEntries_LandInTheFlatArray()
    {
        var flat = Convert("""
            {"structuredProduct":{"trancheClass":"A1","factor":0.9,
             "factorScheduleEntries":[
                {"asOfDate":"2026-01-25","factor":0.98},
                {"asOfDate":"2026-02-25","factor":0.95}]}}
            """, "StructuredCredit");

        var entries = flat.GetProperty("factorScheduleEntries");
        entries.GetArrayLength().Should().Be(2);
        AssertFrozenMembers(entries[0], ["asOfDate", "factor"]);
        entries[0].GetProperty("asOfDate").GetString().Should().Be("2026-01-25");
        entries[0].GetProperty("factor").GetDecimal().Should().Be(0.98m);
        entries[1].GetProperty("asOfDate").GetString().Should().Be("2026-02-25");
        entries[1].GetProperty("factor").GetDecimal().Should().Be(0.95m);

        // The scalar factor keeps its own flat home; the dated schedule no longer rides only in
        // the retained carry while the flat projections miss it.
        flat.GetProperty("currentFactor").GetDecimal().Should().Be(0.9m);
        flat.GetProperty("tranche").GetString().Should().Be("A1");
        flat.TryGetProperty(SecurityTermsEnvelope.RetainedEconomicTermsKey, out _).Should().BeTrue();
    }

    [Fact]
    public void FactorEntriesAlone_StampAtTheClassFloor_TheArrayIsAVersionOneField()
    {
        var flat = Convert("""
            {"structuredProduct":{"factorScheduleEntries":[{"asOfDate":"2026-01-25","factor":0.98}]}}
            """, "StructuredCredit");

        flat.GetProperty(SecurityTermsEnvelope.AssetClassTermsVersionKey).GetInt32()
            .Should().Be(2, "factorScheduleEntries is declared since StructuredCredit version 1, so only the stamp floor governs");
        flat.GetProperty("factorScheduleEntries").GetArrayLength().Should().Be(1);

        var readable = () => SecurityTermsEnvelope.EnsureReadableAsAssetSpecificTerms("StructuredCredit", flat);
        readable.Should().NotThrow("the recovered document must pass the read guard at its own stamp");
    }

    [Fact]
    public void FactorEntries_KeepTheirStoredOrder()
    {
        // The domain invariant orders by date before checking monotonicity; the stored order
        // itself is the writer's, and the recovery preserves it exactly.
        var flat = Convert("""
            {"structuredProduct":{"factorScheduleEntries":[
                {"asOfDate":"2026-02-25","factor":0.95},
                {"asOfDate":"2026-01-25","factor":0.98}]}}
            """, "StructuredCredit");

        var entries = flat.GetProperty("factorScheduleEntries");
        entries[0].GetProperty("asOfDate").GetString().Should().Be("2026-02-25");
        entries[1].GetProperty("asOfDate").GetString().Should().Be("2026-01-25");
    }

    [Fact]
    public void NonDeclaringClass_KeepsTheRetainedCarry_WithoutTheFlatFactorKey()
    {
        var flat = Convert("""
            {"maturity":{"maturityDate":"2032-06-30"},
             "structuredProduct":{"factorScheduleEntries":[{"asOfDate":"2026-01-25","factor":0.98}]}}
            """, "Bond");

        flat.TryGetProperty("factorScheduleEntries", out _).Should().BeFalse("Bond declares no factor-schedule home");
        flat.TryGetProperty(SecurityTermsEnvelope.RetainedEconomicTermsKey, out _).Should().BeTrue();
    }

    [Fact]
    public void UnsetFactorEntries_ConvertWithoutDeclaringTheArray()
    {
        var flat = Convert("""
            {"structuredProduct":{"factorScheduleEntries":[null,{"asOfDate":null,"factor":null}],
             "FactorScheduleEntries":[]}}
            """, "StructuredCredit");

        flat.TryGetProperty("factorScheduleEntries", out _).Should().BeFalse(
            "null and all-null entries are unset, and a case-variant key without content is tolerated like an absent one");
    }

    private static JsonElement Convert(string economicJson, string assetClass)
        => SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster
            .Convert(JsonSerializer.Deserialize<JsonElement>(economicJson), assetClass)
            .Payload;

    private static void AssertFrozenMembers(JsonElement entry, string[] expected)
        => entry.EnumerateObject().Select(static member => member.Name).Should().Equal(
            expected, "the written entry form is frozen by ADR-024: every member, in alphabetical order");
}
