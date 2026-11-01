using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Guards the single declarative asset-specific-terms field/type table (<see cref="SecurityAssetTermsSchema"/>)
/// against drift: it must stay in lock-step with the authoritative asset-class catalog, declare each
/// class's fields without duplication, and remain the shared reference the projection and validation
/// codecs are measured against. Also binds the store's projection coverage to the catalog so the
/// catalog-vs-projection gap stays explicit and enforced rather than accidental, and holds the
/// Asset Operations classes to a stricter rule: a class whose declared capabilities include
/// LedgerProjection cannot be written off as an intentional gap, only tracked on a shrinking
/// backlog. Pins the readable-before-writable scheduleConventions block and the callSchedule and
/// putSchedule arrays with their frozen entry shapes, the fields a node writes, and the walkers the
/// write guard and the round-trip guard read versioned terms through.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetTermsSchemaTests
{
    // The asset classes the platform deliberately does not give a dedicated relational projection.
    // Adding a catalog class without a projection forces this list to be updated in review, so the
    // catalog-vs-projection gap is a conscious decision instead of silent drift.
    private static readonly string[] IntentionallyUnprojectedAssetClasses =
    [
        // The profile-governed class: its fields vary per approved profile, so there is no fixed
        // column set to project. Every other catalog class is projected (migration 044 closed the
        // last eight declared gaps and the whole ops-capable backlog).
        "CustomAsset"
    ];

    // The Asset Operations classes still waiting for a relational terms projection. These are NOT
    // intentional gaps: an ops-capable class declares ProjectedCashFlows, Reconciliation and
    // LedgerProjection, so its economic terms drive money movement and leaving them reachable only
    // by parsing the JSONB blob one security at a time is a coverage gap. They sit in their own list
    // because the guards below hold the backlog to a different standard than a declared gap — it may
    // only ever shrink, and each entry costs a SecurityTermsProjectionRegistry descriptor plus its
    // DDL to clear.
    private static readonly string[] OpsCapableProjectionBacklog = [];

    // The ops-capable classes that were unprojected when the backlog guard was written. The backlog
    // may only ever be a SUBSET of this: an entry can leave it by gaining a projection, and nothing
    // can join it. Without this frozen ceiling the "shrink-only" guards below are satisfied by
    // deleting a descriptor and adding the class back to the backlog, which is the regression the
    // ratchet exists to prevent.
    private static readonly string[] OpsCapableProjectionBacklogCeiling =
    [
        "PrivateFundInterest",
        "PrivateCompanyEquity",
        "RealEstateHolding",
        "CommitmentGuarantee"
    ];

    [Fact]
    public void Schema_DeclaresEveryCatalogAssetClass()
    {
        SecurityAssetTermsSchema.AssetClasses
            .Should().BeEquivalentTo(
                SecurityAssetClassCatalog.AssetClasses,
                "the terms schema is the single source of truth for every catalog asset class");
    }

    [Fact]
    public void Schema_HasNoDuplicateFieldKeysWithinAnyAssetClass()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            var fields = SecurityAssetTermsSchema.Fields(assetClass);
            var distinctKeys = fields
                .Select(static field => field.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            distinctKeys.Should().Be(fields.Count, $"'{assetClass}' must not repeat a field key");
        }
    }

    [Fact]
    public void Schema_EveryFieldHasKeyAndNonNullAliases()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
            {
                field.Key.Should().NotBeNullOrWhiteSpace();
                field.Aliases.Should().NotBeNull();
            }
        }
    }

    [Fact]
    public void Schema_ExposesTheFlatCouponContractTheProjectionReads()
    {
        // Regression anchor for the bond silent-data-loss fix: the coupon is a flat contract, not a
        // nested "coupon" object. If these move, the projection decoder and serializer must move with them.
        SecurityAssetTermsSchema.Field("Bond", "couponType").Should().NotBeNull();
        SecurityAssetTermsSchema.Field("Bond", "couponRate")!.Type.Should().Be(SecurityAssetTermFieldType.Decimal);
        SecurityAssetTermsSchema.Field("Bond", "dayCount").Should().NotBeNull();
        SecurityAssetTermsSchema.Field("Bond", "coupon").Should().BeNull("the coupon is serialized flat, not nested");
    }

    [Fact]
    public void Schema_SwapExposesLegsNotSwapType()
    {
        SecurityAssetTermsSchema.Field("Swap", "legs")!.Type.Should().Be(SecurityAssetTermFieldType.Array);
        SecurityAssetTermsSchema.Field("Swap", "swapType").Should().BeNull("swap economics are carried as legs");
    }

    [Fact]
    public void ProjectionCoverage_IsASubsetOfTheCatalogWithASchemaEntry()
    {
        foreach (var assetClass in PostgresSecurityMasterStore.ProjectedAssetClasses)
        {
            SecurityAssetClassCatalog.AssetClasses.Should().Contain(assetClass,
                $"projected class '{assetClass}' must be a real catalog asset class");
            SecurityAssetTermsSchema.TryGetFields(assetClass, out _).Should().BeTrue(
                $"projected class '{assetClass}' must have a declared terms schema");
        }
    }

    [Fact]
    public void ProjectionCoverage_PartitionsTheCatalogIntoProjectedAndDeclaredGaps()
    {
        var projected = PostgresSecurityMasterStore.ProjectedAssetClasses;

        projected.Should().NotIntersectWith(IntentionallyUnprojectedAssetClasses,
            "a class is either projected or a declared gap, never both");

        projected.Should().NotIntersectWith(OpsCapableProjectionBacklog,
            "a class is either projected or on the backlog, never both");

        IntentionallyUnprojectedAssetClasses.Should().NotIntersectWith(OpsCapableProjectionBacklog,
            "a missing projection is either a decision or a backlog item, and the two carry different obligations");

        projected
            .Concat(IntentionallyUnprojectedAssetClasses)
            .Concat(OpsCapableProjectionBacklog)
            .Should().BeEquivalentTo(SecurityAssetClassCatalog.AssetClasses,
                "every catalog class must be projected, an explicitly declared projection gap, or a declared backlog item");
    }

    [Fact]
    public void ProjectionCoverage_ReachesEveryOpsCapableAssetClassOutsideTheBacklog()
    {
        // The partition guard above is satisfied by naming a class an intentional gap. For an
        // Asset Operations class that answer is not available: the catalog gives it
        // ProjectedCashFlows, Reconciliation and LedgerProjection, so its terms are economics the
        // platform acts on, and "we chose not to project it" would contradict the capabilities it
        // publishes. This guard is what makes the backlog the only way to be ops-capable and
        // unprojected.
        SecurityAssetClassCatalog.AssetOperationsCapableAssetClasses
            .Except(OpsCapableProjectionBacklog, StringComparer.Ordinal)
            .Should().BeSubsetOf(PostgresSecurityMasterStore.ProjectedAssetClasses,
                "an ops-capable class declares LedgerProjection, so its economic terms need a relational "
                + "projection rather than living only inside the asset_specific_terms blob");
    }

    [Fact]
    public void OpsCapableProjectionBacklog_NamesOnlyOpsCapableClassesThatAreStillUnprojected()
    {
        OpsCapableProjectionBacklog.Should().OnlyContain(
            assetClass => SecurityAssetClassCatalog.AssetOperationsCapableAssetClasses.Contains(assetClass),
            "the backlog tracks the ops-capable set; a class outside it belongs in IntentionallyUnprojectedAssetClasses");

        OpsCapableProjectionBacklog.Should().NotIntersectWith(
            PostgresSecurityMasterStore.ProjectedAssetClasses,
            "a class that gained a projection must leave the backlog, or the coverage guard above silently stops covering it");

        OpsCapableProjectionBacklog.Should().BeSubsetOf(
            OpsCapableProjectionBacklogCeiling,
            "the backlog is a ratchet: a class may leave it by gaining a projection, but a projected "
            + "class may never be dropped back onto it to satisfy the coverage guard");
    }

    // ── The scheduleConventions block (ADR-024, readable before writable) ───────────────────────

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Theory]
    [InlineData("Bond", 2)]
    [InlineData("CertificateOfDeposit", 2)]
    [InlineData("StructuredCredit", 3)]
    public void ScheduleConventions_IsAnOptionalObjectIntroducedAboveTheDocumentFloor(string assetClass, int since)
    {
        var field = SecurityAssetTermsSchema.Field(assetClass, "scheduleConventions");

        field.Should().NotBeNull();
        field!.Type.Should().Be(SecurityAssetTermFieldType.Object);
        field.Required.Should().BeFalse();
        field.Aliases.Should().BeEmpty();
        field.Convention.Should().Be(SecurityTermConventionKind.None);
        field.SinceTermsVersion.Should().Be(since);
        SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass).Should().BeGreaterThanOrEqualTo(since);
        SecurityAssetClassCatalog.GetStampFloor(assetClass).Should().Be(since - 1, "the block was introduced above the preserved document floor");
    }

    // ── The callSchedule and putSchedule arrays (ADR-024 readable before writable, ADR-025) ──────

    private static readonly string[] ScheduleReadAheadClasses = ["Bond", "CertificateOfDeposit", "StructuredCredit"];

    // Historical schedule containers remain writable; the calculation companion is still read-only.
    private static readonly IReadOnlyList<string> ScheduleTermKeys = ["scheduleConventions", "callSchedule", "putSchedule"];
    private static readonly string[] CalculationCompanionClasses = ["Bond", "CertificateOfDeposit", "StructuredCredit", "Swap", "DirectLoan"];

    [Theory]
    [InlineData("Bond", 3)]
    [InlineData("CertificateOfDeposit", 3)]
    [InlineData("StructuredCredit", 4)]
    public void CallAndPutSchedules_AreOptionalArraysIntroducedAtTheNextReadableVersion(string assetClass, int since)
    {
        var block = SecurityAssetTermsSchema.Field(assetClass, "scheduleConventions")!;
        var writes = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
        var reads = SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);

        foreach (var key in new[] { "callSchedule", "putSchedule" })
        {
            var field = SecurityAssetTermsSchema.Field(assetClass, key);

            field.Should().NotBeNull(key);
            field!.Key.Should().Be(key);
            field.Type.Should().Be(SecurityAssetTermFieldType.Array, key);
            field.Required.Should().BeFalse(key);
            field.Aliases.Should().BeEmpty(key);
            field.AllowedValues.Should().BeEmpty(key);
            field.Convention.Should().Be(SecurityTermConventionKind.None, key);
            field.SinceTermsVersion.Should().Be(since, key);
            field.SinceTermsVersion.Should().BeGreaterThan(SecurityAssetClassCatalog.GetStampFloor(assetClass), $"{key} remains above the document floor");
            field.SinceTermsVersion.Should().BeLessThanOrEqualTo(writes, $"the contract-step writer now admits {key}");
            field.SinceTermsVersion.Should().BeLessThanOrEqualTo(reads, $"{assetClass} reads {key}");
            field.SinceTermsVersion.Should().BeGreaterThan(block.SinceTermsVersion, $"{key} arrives at a later readable version than the block");
        }

        // Appended after the block, so every earlier refusal text and the order of populated terms hold.
        SecurityAssetTermsSchema.Fields(assetClass).Where(field => field.SinceTermsVersion <= since)
            .TakeLast(3).Select(static field => field.Key).Should().Equal(ScheduleTermKeys);
        SecurityAssetTermsSchema.Fields(assetClass).TakeLast(4).Select(static field => field.Key)
            .Should().Equal(ScheduleTermKeys.Append("cashFlowContract"));
    }

    [Fact]
    public void CallAndPutScheduleEntryShapes_ResolveUnderEveryReadAheadClass_CaseInsensitively_AndNowhereElse()
    {
        var call = SecurityAssetTermsSchema.ElementFields("Bond", "callSchedule");
        var put = SecurityAssetTermsSchema.ElementFields("Bond", "putSchedule");
        call.Should().HaveCount(12);
        put.Should().HaveCount(9);

        SecurityAssetTermsSchema.ElementFieldPaths
            .Where(static path => path.EndsWith(".callSchedule", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".putSchedule", StringComparison.OrdinalIgnoreCase))
            .Should().BeEquivalentTo(
                ScheduleReadAheadClasses.SelectMany(static assetClass => new[] { $"{assetClass}.callSchedule", $"{assetClass}.putSchedule" }),
                "no other class or element registers a call or put entry shape");
        foreach (var assetClass in ScheduleReadAheadClasses)
        {
            SecurityAssetTermsSchema.ElementFields(assetClass, "callSchedule").Should().Equal(call, $"{assetClass} shares the call entry shape");
            SecurityAssetTermsSchema.ElementFields(assetClass, "putSchedule").Should().Equal(put, $"{assetClass} shares the put entry shape");
            SecurityAssetTermsSchema.ElementFields(assetClass.ToLowerInvariant(), "CALLSCHEDULE")
                .Should().Equal(call, "registered paths resolve case-insensitively");
            SecurityAssetTermsSchema.ElementFields(assetClass.ToUpperInvariant(), "putschedule")
                .Should().Equal(put, "registered paths resolve case-insensitively");
        }

        SecurityAssetTermsSchema.ElementFields("Swap", "callSchedule").Should().BeEmpty();
        SecurityAssetTermsSchema.ElementFields("Swap", "putSchedule").Should().BeEmpty();
        SecurityAssetTermsSchema.ElementFields("Swap", "legs.callSchedule").Should().BeEmpty();
        SecurityAssetTermsSchema.ElementFields("Equity", "callSchedule").Should().BeEmpty();
        SecurityAssetTermsSchema.ElementFields("Equity", "putSchedule").Should().BeEmpty();
    }

    [Fact]
    public void CallScheduleEntryShape_DeclaresTwelveOptionalUntaggedMembers()
    {
        var fields = SecurityAssetTermsSchema.ElementFields("Bond", "callSchedule");

        fields.Select(static field => (field.Key, field.Type)).Should().Equal(
            ("callDate", SecurityAssetTermFieldType.Date),
            ("callEndDate", SecurityAssetTermFieldType.Date),
            ("exerciseStyle", SecurityAssetTermFieldType.String),
            ("exerciseFrequency", SecurityAssetTermFieldType.String),
            ("callPrice", SecurityAssetTermFieldType.Decimal),
            ("priceBasis", SecurityAssetTermFieldType.String),
            ("priceType", SecurityAssetTermFieldType.String),
            ("makeWholeSpreadBps", SecurityAssetTermFieldType.Decimal),
            ("partialRedemption", SecurityAssetTermFieldType.String),
            ("minimumNoticeDays", SecurityAssetTermFieldType.Integer),
            ("maximumNoticeDays", SecurityAssetTermFieldType.Integer),
            ("noticeDayType", SecurityAssetTermFieldType.String));

        // The container carries the class's terms version itself, so its members are untagged.
        fields.Should().OnlyContain(static field =>
            !field.Required && field.SinceTermsVersion == 1 && field.Aliases.Count == 0
            && field.UnderlyingRole == null && field.Escape == null && field.Role == SecurityAssetTermRole.Economic);
        fields.Where(static field => field.Convention != SecurityTermConventionKind.None)
            .Select(static field => (field.Key, field.Convention)).Should().Equal(
                ("exerciseFrequency", SecurityTermConventionKind.CouponFrequency));
        fields.Where(static field => field.IsDiscriminant)
            .Select(static field => (field.Key, string.Join("|", field.AllowedValues), field.CarriesUndeclaredValueVerbatim)).Should().Equal(
                ("exerciseStyle", "European|American|Bermudan", true),
                ("priceBasis", "PercentOfPar|PercentOfAccretedValue", true),
                ("priceType", "Fixed|MakeWhole", true),
                ("partialRedemption", "WholeOnly|ProRata|ByLot", true),
                ("noticeDayType", "Business|Calendar", true));
        fields.Where(static field => field.CarriesUndeclaredValueVerbatim).Should().OnlyContain(static field => field.IsDiscriminant);
    }

    [Fact]
    public void PutScheduleEntryShape_DeclaresNineOptionalUntaggedMembers_SharingTheNoticeMembers()
    {
        var fields = SecurityAssetTermsSchema.ElementFields("Bond", "putSchedule");

        fields.Select(static field => (field.Key, field.Type)).Should().Equal(
            ("putDate", SecurityAssetTermFieldType.Date),
            ("putEndDate", SecurityAssetTermFieldType.Date),
            ("exerciseStyle", SecurityAssetTermFieldType.String),
            ("exerciseFrequency", SecurityAssetTermFieldType.String),
            ("putPrice", SecurityAssetTermFieldType.Decimal),
            ("priceBasis", SecurityAssetTermFieldType.String),
            ("minimumNoticeDays", SecurityAssetTermFieldType.Integer),
            ("maximumNoticeDays", SecurityAssetTermFieldType.Integer),
            ("noticeDayType", SecurityAssetTermFieldType.String));

        fields.Should().OnlyContain(static field =>
            !field.Required && field.SinceTermsVersion == 1 && field.Aliases.Count == 0
            && field.UnderlyingRole == null && field.Escape == null && field.Role == SecurityAssetTermRole.Economic);
        fields.Where(static field => field.Convention != SecurityTermConventionKind.None)
            .Select(static field => (field.Key, field.Convention)).Should().Equal(
                ("exerciseFrequency", SecurityTermConventionKind.CouponFrequency));
        fields.Where(static field => field.IsDiscriminant)
            .Select(static field => (field.Key, string.Join("|", field.AllowedValues), field.CarriesUndeclaredValueVerbatim)).Should().Equal(
                ("exerciseStyle", "European|American|Bermudan", true),
                ("priceBasis", "PercentOfPar|PercentOfAccretedValue", true),
                ("noticeDayType", "Business|Calendar", true));
        fields.Where(static field => field.CarriesUndeclaredValueVerbatim).Should().OnlyContain(static field => field.IsDiscriminant);

        // The notice members are one shared declaration, so a call and a put read notice identically.
        var call = SecurityAssetTermsSchema.ElementFields("Bond", "callSchedule");
        var callNotice = call.TakeLast(3).ToArray();
        var putNotice = fields.TakeLast(3).ToArray();
        putNotice.Select(static field => field.Key).Should().Equal("minimumNoticeDays", "maximumNoticeDays", "noticeDayType");
        for (var index = 0; index < putNotice.Length; index++)
        {
            putNotice[index].Should().BeSameAs(callNotice[index], putNotice[index].Key);
        }

        // The members both entries declare have the same shape in each.
        static (string, SecurityAssetTermFieldType, string, bool, SecurityTermConventionKind) Shape(SecurityAssetTermField field)
            => (field.Key, field.Type, string.Join("|", field.AllowedValues), field.CarriesUndeclaredValueVerbatim, field.Convention);
        string[] common = ["exerciseStyle", "exerciseFrequency", "priceBasis"];
        fields.Where(field => common.Contains(field.Key)).Select(Shape)
            .Should().Equal(call.Where(field => common.Contains(field.Key)).Select(Shape));
    }

    [Fact]
    public void SwapLegScheduleConventions_IsTheLastLegField_AnOptionalObjectAtVersionTwo()
    {
        var field = SecurityAssetTermsSchema.ElementFields("Swap", "legs")[^1];

        field.Key.Should().Be("scheduleConventions");
        field.Type.Should().Be(SecurityAssetTermFieldType.Object);
        field.Required.Should().BeFalse();
        field.Aliases.Should().BeEmpty();
        field.SinceTermsVersion.Should().Be(2);
        SecurityAssetTermsSchema.Field("Swap", "legs")!.SinceTermsVersion.Should().Be(1, "the legs container itself is unchanged");
        SecurityAssetTermsSchema.Field("Swap", "scheduleConventions").Should().BeNull("a swap declares the block per leg, not at the top level");
    }

    [Fact]
    public void ScheduleConventionShape_DeclaresTwelveOptionalUntaggedMembers()
    {
        var fields = SecurityAssetTermsSchema.ElementFields("Bond", "scheduleConventions");

        fields.Select(static field => (field.Key, field.Type)).Should().Equal(
            ("businessDayConvention", SecurityAssetTermFieldType.String),
            ("accrualAdjusted", SecurityAssetTermFieldType.Boolean),
            ("paymentCalendars", SecurityAssetTermFieldType.Array),
            ("paymentLagBusinessDays", SecurityAssetTermFieldType.Integer),
            ("rollConvention", SecurityAssetTermFieldType.String),
            ("endOfMonthRule", SecurityAssetTermFieldType.Boolean),
            ("firstRegularDate", SecurityAssetTermFieldType.Date),
            ("lastRegularDate", SecurityAssetTermFieldType.Date),
            ("direction", SecurityAssetTermFieldType.String),
            ("stubPolicy", SecurityAssetTermFieldType.String),
            ("couponRateUnit", SecurityAssetTermFieldType.String),
            ("couponCashBasis", SecurityAssetTermFieldType.String));

        // The block carries the class's terms version itself, so its members are untagged.
        fields.Should().OnlyContain(static field => !field.Required && field.SinceTermsVersion == 1 && field.Aliases.Count == 0);
        fields.Where(static field => field.Convention != SecurityTermConventionKind.None)
            .Select(static field => (field.Key, field.Convention)).Should().Equal(
                ("businessDayConvention", SecurityTermConventionKind.BusinessDayConvention),
                ("rollConvention", SecurityTermConventionKind.RollConvention),
                ("stubPolicy", SecurityTermConventionKind.StubPolicy));
        fields.Where(static field => field.IsDiscriminant)
            .Select(static field => (field.Key, string.Join("|", field.AllowedValues), field.CarriesUndeclaredValueVerbatim)).Should().Equal(
                ("direction", "Forward|Backward", true),
                ("couponRateUnit", "Percent|Fraction", true),
                ("couponCashBasis", "Level|DayCount", true));
    }

    [Fact]
    public void ScheduleConventionShape_ResolvesUnderEveryRegisteredBlock_IncludingTheSwapLeg()
    {
        string[] paths =
        [
            "Bond.scheduleConventions",
            "CertificateOfDeposit.scheduleConventions",
            "StructuredCredit.scheduleConventions",
            "Swap.legs.scheduleConventions"
        ];
        var expected = SecurityAssetTermsSchema.ElementFields("Bond", "scheduleConventions").Select(static field => field.Key).ToArray();
        expected.Should().HaveCount(12);

        SecurityAssetTermsSchema.ElementFieldPaths.Should().Contain(paths);
        SecurityAssetTermsSchema.ElementFields("CertificateOfDeposit", "scheduleConventions").Select(static field => field.Key).Should().Equal(expected);
        SecurityAssetTermsSchema.ElementFields("StructuredCredit", "scheduleConventions").Select(static field => field.Key).Should().Equal(expected);
        SecurityAssetTermsSchema.ElementFields("Swap", "legs.scheduleConventions").Select(static field => field.Key).Should().Equal(expected);
        SecurityAssetTermsSchema.ElementFields("bond", "SCHEDULECONVENTIONS").Select(static field => field.Key)
            .Should().Equal(expected, "registered paths resolve case-insensitively");

        SecurityAssetTermsSchema.ElementFields("Swap", "scheduleConventions").Should().BeEmpty();
        SecurityAssetTermsSchema.ElementFields("Equity", "scheduleConventions").Should().BeEmpty();
    }

    [Fact]
    public void WritableFields_AreExactlyTheFieldsAtOrBelowTheCurrentWriteVersion()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            var writes = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
            var writable = SecurityAssetTermsSchema.WritableFields(assetClass);

            writable.Should().Equal(SecurityAssetTermsSchema.Fields(assetClass).Where(field => field.SinceTermsVersion <= writes), assetClass);
            writable.Should().NotContain(field => field.SinceTermsVersion > writes, assetClass);
        }

        foreach (var assetClass in CalculationCompanionClasses)
        {
            SecurityAssetTermsSchema.Fields(assetClass)
                .Where(field => field.SinceTermsVersion > SecurityAssetClassCatalog.GetTermsVersion(assetClass))
                .Select(static field => field.Key).Should().Equal(new[] { "cashFlowContract" }, assetClass);
            SecurityAssetTermsSchema.WritableFields(assetClass).Select(static field => field.Key).Should().Equal(
                SecurityAssetTermsSchema.Fields(assetClass).Where(static field => field.Key != "cashFlowContract").Select(static field => field.Key),
                assetClass);
        }

        SecurityAssetTermsSchema.WritableFields("StructuredCredit").Select(static field => field.Key)
            .Should().Contain(["seniority", "nonCallPeriodEnd"], "the version-2 tranche terms remain writable")
            .And.Contain(ScheduleTermKeys, "declared schedule containers are writable through the version-4 ceiling");
        SecurityAssetTermsSchema.WritableFields("Swap").Select(static field => field.Key)
            .Should().Equal("effectiveDate", "maturityDate", "legs");
        SecurityAssetTermsSchema.WritableFields("Equity").Should().Equal(SecurityAssetTermsSchema.Fields("Equity"));
        SecurityAssetTermsSchema.WritableFields("Nope").Should().BeEmpty();
    }

    [Theory]
    [InlineData("null", false)]
    [InlineData("[]", false)]
    [InlineData("{}", false)]
    [InlineData("[null]", false)]
    [InlineData("[[],{}]", false)]
    [InlineData("""{"a":null,"b":[],"c":{"d":null}}""", false)]
    [InlineData("""[{"callDate":null}]""", false)]
    [InlineData("false", true)]
    [InlineData("0", true)]
    [InlineData("\"\"", true)]
    [InlineData("[0]", true)]
    [InlineData("""{"a":null,"b":false}""", true)]
    [InlineData("""{"c":{"d":[null,"x"]}}""", true)]
    [InlineData("""[{"callPrice":0}]""", true)]
    [InlineData("""[{"exerciseStyle":""}]""", true)]
    public void IsPopulatedTermValue_TreatsNullEmptyAndAllNullAsUnset_AndEveryScalarAsAValue(string json, bool populated)
        => SecurityAssetTermsSchema.IsPopulatedTermValue(Json(json)).Should().Be(populated);

    [Fact]
    public void IsPopulatedTermValue_AnAbsentValueIsUnset()
        => SecurityAssetTermsSchema.IsPopulatedTermValue(default).Should().BeFalse();

    private static (string Path, string Key, int Since)[] Populated(string assetClass, string payload, int termsVersion)
        => SecurityAssetTermsSchema.PopulatedTermsAbove(assetClass, Json(payload), termsVersion)
            .Select(static term => (term.Path, term.Field.Key, term.Field.SinceTermsVersion))
            .ToArray();

    [Fact]
    public void PopulatedTermsAbove_ReportsAPopulatedBlockAboveTheVersion_UnderTheSpellingThePayloadUses()
    {
        const string Block = """{"maturity":"2030-01-15","scheduleConventions":{"stubPolicy":"Long"}}""";

        Populated("Bond", Block, 1).Should().Equal(("scheduleConventions", "scheduleConventions", 2));
        Populated("Bond", Block, 2).Should().BeEmpty("the block is not above version 2");
        Populated("Bond", """{"scheduleConventions":"x"}""", 1).Should().Equal(("scheduleConventions", "scheduleConventions", 2));
        Populated("Bond", """{"scheduleConventions":false}""", 1).Should().Equal(("scheduleConventions", "scheduleConventions", 2));

        // Keys match case-insensitively, and every spelling is reported in payload order.
        Populated("Bond", """{"ScheduleConventions":{"stubPolicy":"Long"},"SCHEDULECONVENTIONS":{"rollConvention":"EOM"}}""", 1)
            .Should().Equal(
                ("ScheduleConventions", "scheduleConventions", 2),
                ("SCHEDULECONVENTIONS", "scheduleConventions", 2));
    }

    [Fact]
    public void PopulatedTermsAbove_SkipsNullEmptyAndAllNullBlocks()
    {
        foreach (var block in new[] { "null", "{}", "[]", """{"paymentCalendars":[]}""", """{"paymentCalendars":[null]}""", """{"stubPolicy":null,"rollConvention":null}""" })
        {
            Populated("Bond", $$"""{"scheduleConventions":{{block}}}""", 1).Should().BeEmpty(block);
            Populated("Swap", $$"""{"legs":[null,{"legType":"Fixed","scheduleConventions":{{block}}}]}""", 1).Should().BeEmpty(block);
        }
    }

    [Theory]
    [InlineData("legs")]
    [InlineData("swapLegs")]
    [InlineData("cashFlowLegs")]
    [InlineData("Legs")]
    public void PopulatedTermsAbove_WalksSwapLegsUnderTheContainerItsAliasesAndCaseVariants(string container)
    {
        var payload = $$$"""
            {"effectiveDate":"2026-01-15","{{{container}}}":[{"legType":"Fixed","scheduleConventions":null},{"legType":"Floating","SCHEDULECONVENTIONS":{"direction":"Backward"}}]}
            """;

        Populated("Swap", payload, 1).Should().Equal(($"{container}[1].SCHEDULECONVENTIONS", "scheduleConventions", 2));
        Populated("Swap", payload, 2).Should().BeEmpty();
    }

    [Fact]
    public void PopulatedTermsAbove_CountsNonObjectLegs_AndIgnoresAContainerThatIsNotAnArray()
    {
        Populated("Swap", """{"legs":["fixed",7,{"legType":"Fixed","scheduleConventions":{"rollConvention":"15"}}]}""", 1)
            .Should().Equal(("legs[2].scheduleConventions", "scheduleConventions", 2));
        Populated("Swap", """{"legs":{"scheduleConventions":{"rollConvention":"15"}}}""", 1).Should().BeEmpty();
        Populated("Swap", """{"legs":"none","scheduleConventions":{"rollConvention":"15"}}""", 1)
            .Should().BeEmpty("a swap declares the block per leg only");
    }

    [Fact]
    public void PopulatedTermsAbove_ListsEveryTermAboveTheVersionInSchemaOrder()
    {
        const string Payload = """
            {"tranche":"A-1","putSchedule":[{"putDate":"2029-06-15"}],"scheduleConventions":{"stubPolicy":"Long"},"attachmentPoint":0,"callSchedule":[{"callDate":"2030-06-15"}],"seniority":"Senior","weightedAverageCoupon":null}
            """;

        Populated("StructuredCredit", Payload, 1).Should().Equal(
            ("seniority", "seniority", 2),
            ("attachmentPoint", "attachmentPoint", 2),
            ("scheduleConventions", "scheduleConventions", 3),
            ("callSchedule", "callSchedule", 4),
            ("putSchedule", "putSchedule", 4));
        Populated("StructuredCredit", Payload, 2).Should().Equal(
            ("scheduleConventions", "scheduleConventions", 3),
            ("callSchedule", "callSchedule", 4),
            ("putSchedule", "putSchedule", 4));
        Populated("StructuredCredit", Payload, 3).Should().Equal(
            ("callSchedule", "callSchedule", 4),
            ("putSchedule", "putSchedule", 4));
        Populated("StructuredCredit", Payload, 4).Should().BeEmpty();
    }

    [Fact]
    public void PopulatedTermsAbove_NeverWalksAProfileEnvelopeOrProfileFields()
    {
        // A profile envelope of a profile-capable class has no versioned class terms at all.
        const string Envelope = """
            {"customProfileId":"structured-credit-profile","seniority":"Senior","scheduleConventions":{"stubPolicy":"Long"},"profileFields":{"scheduleConventions":{"stubPolicy":"Long"}}}
            """;
        Populated("StructuredCredit", Envelope, 1).Should().BeEmpty();
        Populated("StructuredCredit", Envelope, 2).Should().BeEmpty();

        // profileFields is not a declared term, so it is never walked, even where the root is.
        Populated("Bond", """{"profileFields":{"scheduleConventions":{"stubPolicy":"Long"}}}""", 1).Should().BeEmpty();
        Populated("Swap", """{"profileFields":{"legs":[{"scheduleConventions":{"stubPolicy":"Long"}}]}}""", 1).Should().BeEmpty();
        Populated("Bond", """{"profileFields":{},"scheduleConventions":{"stubPolicy":"Long"}}""", 1)
            .Should().Equal(("scheduleConventions", "scheduleConventions", 2));

        Populated("Bond", "[]", 1).Should().BeEmpty();
        Populated("Bond", "\"text\"", 1).Should().BeEmpty();
    }

    private static string Normalized(string assetClass, string payload, int termsVersion, out IReadOnlyList<string> conflicts)
        => SecurityAssetTermsSchema.NormalizedTermsAbove(assetClass, Json(payload), termsVersion, out conflicts).ToJsonString();

    [Fact]
    public void NormalizedTermsAbove_RemovesNullsAndEmptiesRecursively_AndKeepsEveryValue()
    {
        const string Payload = """
            {"maturity":"2030-01-15","scheduleConventions":{"businessDayConvention":"ModifiedFollowing","accrualAdjusted":false,"paymentCalendars":[null,"GBLO",[],"USNY"],"paymentLagBusinessDays":0,"rollConvention":null,"firstRegularDate":null,"stubPolicy":"","nested":{"a":null,"b":[null],"c":{}}}}
            """;

        Normalized("Bond", Payload, 1, out var conflicts).Should().Be(
            """{"scheduleConventions":{"businessDayConvention":"ModifiedFollowing","accrualAdjusted":false,"paymentCalendars":["GBLO","USNY"],"paymentLagBusinessDays":0,"stubPolicy":""}}""");
        conflicts.Should().BeEmpty();
        Normalized("Bond", Payload, 2, out _).Should().Be("{}", "this payload carries no term above version 2");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"paymentCalendars":[]}""")]
    [InlineData("""{"businessDayConvention":null,"paymentCalendars":[null],"stubPolicy":null}""")]
    public void NormalizedTermsAbove_EveryUnsetSpellingNormalizesToNothing(string block)
    {
        Normalized("Bond", $$"""{"maturity":"2030-01-15","scheduleConventions":{{block}}}""", 1, out var conflicts).Should().Be("{}");
        conflicts.Should().BeEmpty();
        Normalized("Bond", """{"maturity":"2030-01-15"}""", 1, out _).Should().Be("{}");
    }

    [Fact]
    public void NormalizedTermsAbove_KeepsUnknownInnerKeysAndWrongTypesVerbatim()
    {
        Normalized("Bond", """{"scheduleConventions":{"rollConvention":"EOM","futureKnob":"x"}}""", 1, out _)
            .Should().Be("""{"scheduleConventions":{"rollConvention":"EOM","futureKnob":"x"}}""");
        Normalized("Bond", """{"scheduleConventions":{"rollConvention":15,"paymentLagBusinessDays":"2"}}""", 1, out _)
            .Should().Be("""{"scheduleConventions":{"rollConvention":15,"paymentLagBusinessDays":"2"}}""");
        Normalized("CertificateOfDeposit", """{"scheduleConventions":"x"}""", 1, out _)
            .Should().Be("""{"scheduleConventions":"x"}""");
    }

    [Fact]
    public void NormalizedTermsAbove_KeysLegBlocksByCanonicalPath()
    {
        Normalized("Swap", """{"swapLegs":[{"legType":"Fixed","scheduleConventions":null},"x",{"SCHEDULECONVENTIONS":{"stubPolicy":"Long"}}]}""", 1, out var conflicts)
            .Should().Be("""{"legs[2].scheduleConventions":{"stubPolicy":"Long"}}""");
        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void NormalizedTermsAbove_ReportsAPathMoreThanOneSpellingPopulates()
    {
        Normalized("Bond", """{"scheduleConventions":{"stubPolicy":"Long"},"ScheduleConventions":{"stubPolicy":"Short"}}""", 1, out var conflicts)
            .Should().Be("""{"scheduleConventions":{"stubPolicy":"Long"}}""", "the first spelling is kept");
        conflicts.Should().Equal("scheduleConventions");

        Normalized("Swap", """{"legs":[{"scheduleConventions":{"stubPolicy":"Long"},"ScheduleConventions":{"rollConvention":"EOM"}}]}""", 1, out conflicts);
        conflicts.Should().Equal("legs[0].scheduleConventions");

        // The legs container and its alias address the same canonical leg.
        Normalized("Swap", """{"legs":[{"scheduleConventions":{"stubPolicy":"Long"}}],"swapLegs":[{"scheduleConventions":{"stubPolicy":"Long"}}]}""", 1, out conflicts);
        conflicts.Should().Equal("legs[0].scheduleConventions");

        // Only populated spellings conflict.
        Normalized("Bond", """{"scheduleConventions":null,"ScheduleConventions":{"stubPolicy":"Short"}}""", 1, out conflicts)
            .Should().Be("""{"scheduleConventions":{"stubPolicy":"Short"}}""");
        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void NormalizedTermsAbove_IsEmptyForAProfileEnvelope()
    {
        Normalized(
                "StructuredCredit",
                """{"customProfileId":"structured-credit-profile","scheduleConventions":{"stubPolicy":"Long"},"ScheduleConventions":{"stubPolicy":"Short"},"profileFields":{"scheduleConventions":{"stubPolicy":"Long"}}}""",
                2,
                out var conflicts)
            .Should().Be("{}");
        conflicts.Should().BeEmpty();
    }

    // ── The bounded walk: the round-trip guard compares only the terms up to a row's own stamp ──

    private static string Normalized(string assetClass, string payload, int termsVersion, int throughVersion, out IReadOnlyList<string> conflicts)
        => SecurityAssetTermsSchema.NormalizedTermsAbove(assetClass, Json(payload), termsVersion, throughVersion, out conflicts).ToJsonString();

    // Payload order differs from schema order on purpose: the normalized form follows the schema.
    private const string ReadAheadBond = """
        {"maturity":"2030-01-15","putSchedule":[{"putDate":"2029-06-15","putPrice":100}],"callSchedule":[{"callDate":"2030-06-15","callPrice":101.5}],"scheduleConventions":{"stubPolicy":"Long"}}
        """;

    private const string CaseVariantCallSchedules = """
        {"callSchedule":[{"callDate":"2030-06-15"}],"scheduleConventions":{"stubPolicy":"Long"},"CallSchedule":[{"callDate":"2031-06-15"}]}
        """;

    private const string ReadAheadTranche = """
        {"tranche":"A-1","seniority":"Senior","callSchedule":[{"callDate":"2030-06-15"}],"scheduleConventions":{"stubPolicy":"Long"}}
        """;

    private const string ConflictingLegBlocks = """
        {"legs":[{"legType":"Fixed","scheduleConventions":{"stubPolicy":"Long"},"ScheduleConventions":{"rollConvention":"EOM"}}]}
        """;

    [Fact]
    public void NormalizedTermsAbove_Bounded_KeepsOnlyTheTermsInsideTheWindow_InSchemaOrder()
    {
        Normalized("Bond", ReadAheadBond, 1, 2, out var conflicts)
            .Should().Be("""{"scheduleConventions":{"stubPolicy":"Long"}}""", "the arrays are above version 2");
        conflicts.Should().BeEmpty();

        Normalized("Bond", ReadAheadBond, 1, 3, out conflicts).Should().Be(
            """{"scheduleConventions":{"stubPolicy":"Long"},"callSchedule":[{"callDate":"2030-06-15","callPrice":101.5}],"putSchedule":[{"putDate":"2029-06-15","putPrice":100}]}""");
        conflicts.Should().BeEmpty();

        Normalized("Bond", ReadAheadBond, 2, 3, out conflicts).Should().Be(
            """{"callSchedule":[{"callDate":"2030-06-15","callPrice":101.5}],"putSchedule":[{"putDate":"2029-06-15","putPrice":100}]}""",
            "the block is not above version 2");
        conflicts.Should().BeEmpty();

        Normalized("Bond", ReadAheadBond, 1, 1, out conflicts).Should().Be("{}", "an empty window keeps nothing");
        conflicts.Should().BeEmpty();
        Normalized("Bond", ReadAheadBond, 3, 3, out conflicts).Should().Be("{}");
        conflicts.Should().BeEmpty();

        // StructuredCredit reads one version further: the tranche terms at 2, the block at 3, the arrays at 4.
        Normalized("StructuredCredit", ReadAheadTranche, 1, 2, out conflicts).Should().Be("""{"seniority":"Senior"}""");
        conflicts.Should().BeEmpty();
        Normalized("StructuredCredit", ReadAheadTranche, 2, 3, out conflicts).Should().Be("""{"scheduleConventions":{"stubPolicy":"Long"}}""");
        conflicts.Should().BeEmpty();
        Normalized("StructuredCredit", ReadAheadTranche, 3, 4, out conflicts).Should().Be("""{"callSchedule":[{"callDate":"2030-06-15"}]}""");
        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void NormalizedTermsAbove_TheUnboundedOverloadIsBoundedByIntMaxValue()
    {
        (string AssetClass, string Payload)[] cases =
        [
            ("Bond", ReadAheadBond),
            ("Bond", CaseVariantCallSchedules),
            ("StructuredCredit", ReadAheadTranche),
            ("Swap", ConflictingLegBlocks)
        ];

        foreach (var (assetClass, payload) in cases)
        {
            for (var termsVersion = 1; termsVersion <= 4; termsVersion++)
            {
                Normalized(assetClass, payload, termsVersion, out var unboundedConflicts)
                    .Should().Be(Normalized(assetClass, payload, termsVersion, int.MaxValue, out var boundedConflicts), $"{assetClass} above {termsVersion}");
                unboundedConflicts.Should().Equal(boundedConflicts, $"{assetClass} above {termsVersion}");
            }
        }
    }

    [Fact]
    public void NormalizedTermsAbove_Bounded_ReportsOnlyTheConflictsInsideTheWindow()
    {
        Normalized("Bond", CaseVariantCallSchedules, 1, 3, out var conflicts).Should().Be(
            """{"scheduleConventions":{"stubPolicy":"Long"},"callSchedule":[{"callDate":"2030-06-15"}]}""",
            "the first spelling is kept");
        conflicts.Should().Equal("callSchedule");

        Normalized("Bond", CaseVariantCallSchedules, 1, 2, out conflicts).Should().Be("""{"scheduleConventions":{"stubPolicy":"Long"}}""");
        conflicts.Should().BeEmpty("a term above the window is an undeclared key of a row stamped 2, so its spellings cannot conflict");
    }

    [Fact]
    public void NormalizedTermsAbove_Bounded_ExcludesASwapLegBlockAboveTheWindow()
    {
        Normalized("Swap", ConflictingLegBlocks, 1, 1, out var conflicts).Should().Be("{}");
        conflicts.Should().BeEmpty();

        Normalized("Swap", ConflictingLegBlocks, 1, 2, out conflicts).Should().Be("""{"legs[0].scheduleConventions":{"stubPolicy":"Long"}}""");
        conflicts.Should().Equal("legs[0].scheduleConventions");
    }

    [Fact]
    public void NormalizedTermsAbove_DropsUnsetScheduleEntries_AndKeepsTheStoredOrder()
    {
        const string Payload = """
            {"callSchedule":[{"callDate":"2031-06-15","callPrice":101},{"callDate":null,"callPrice":null,"exerciseStyle":null},{"callDate":"2030-06-15","callPrice":102}],"putSchedule":[null,{},{"putDate":"2029-06-15"}]}
            """;

        Normalized("Bond", Payload, 1, out var conflicts).Should().Be(
            """{"callSchedule":[{"callDate":"2031-06-15","callPrice":101},{"callDate":"2030-06-15","callPrice":102}],"putSchedule":[{"putDate":"2029-06-15"}]}""",
            "an unset entry is dropped and the rest keep their stored order, unsorted");
        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void NormalizedTermsAbove_KeepsANumbersSpelling_AndItComparesByValue()
    {
        var normalized = new[] { "101.50", "1.015e2", "101.5" }
            .Select(price => SecurityAssetTermsSchema.NormalizedTermsAbove(
                "Bond", Json($$"""{"callSchedule":[{"callDate":"2030-06-15","callPrice":{{price}}}]}"""), 1, 3, out _))
            .ToArray();

        normalized[1].ToJsonString().Should().Be("""{"callSchedule":[{"callDate":"2030-06-15","callPrice":1.015e2}]}""");
        JsonNode.DeepEquals(normalized[0], normalized[1]).Should().BeTrue();
        JsonNode.DeepEquals(normalized[0], normalized[2]).Should().BeTrue();
        JsonNode.DeepEquals(normalized[1], normalized[2]).Should().BeTrue();

        var otherPrice = SecurityAssetTermsSchema.NormalizedTermsAbove(
            "Bond", Json("""{"callSchedule":[{"callDate":"2030-06-15","callPrice":101.51}]}"""), 1, 3, out _);
        JsonNode.DeepEquals(normalized[2], otherPrice).Should().BeFalse("a different price is a change");
    }
}
