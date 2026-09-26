using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Per-module coverage guard for the cross-family v2 → v1 bridge
/// (<see cref="SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert"/>), built the way
/// <see cref="SecurityAssetTermsSchemaRoundTripTests"/> guards the v1 codec: the economic-terms
/// modules the serializer emits are enumerated, each is classified as <b>bridged</b> (survives the
/// v2 → v1 → read cycle at a named flat key) or <b>dropped</b> (does not), and the flattened key set
/// is asserted exactly. The bridge is lossy by construction — the flat family is keyed per asset
/// class and the bridge does not know the class — so this suite does not make it lossless; it makes
/// the loss a test failure rather than a discovery in a restatement. A fifteenth module added to
/// <c>SecurityEconomicDefinitionAdapter.BuildEconomicTermsJson</c> fails
/// <see cref="EconomicSerializer_EmitsExactlyTheModulesTheBridgeClassifies"/> until it is placed in
/// <see cref="SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.BridgedModules"/> or
/// <see cref="SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.DroppedModules"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityEconomicTermsV2BridgeCoverageTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    /// <summary>
    /// One fully-populated v2 economic-terms document in the exact nested shape
    /// <c>BuildEconomicTermsJson</c> writes: every module present, every field of every module
    /// carrying a value, so a field that is dropped is dropped because the bridge does not read it,
    /// not because the fixture did not carry it.
    /// </summary>
    private static readonly JsonElement FullEconomicTerms = Json("""
    {
        "schemaVersion": 2,
        "maturity": { "effectiveDate": "2020-01-15", "issueDate": "2020-01-15", "maturityDate": "2030-01-15" },
        "coupon": { "couponType": "Fixed", "couponRate": 4.25, "paymentFrequency": "SemiAnnual", "dayCount": "Thirty360" },
        "discount": { "discountRate": 3.9, "yieldRate": 4.1 },
        "accrual": {
            "accrualMethod": "Standard", "accrualStartDate": "2020-01-15", "exDividendDays": 2,
            "businessDayConvention": "ModifiedFollowing", "holidayCalendar": "NYC", "dayCount": "Actual365"
        },
        "payment": { "paymentFrequency": "Quarterly", "paymentLagDays": 2, "paymentCurrency": "USD" },
        "redemption": { "redemptionType": "Par", "redemptionPrice": 100.0, "isBullet": true, "isAmortizing": false },
        "call": {
            "isCallable": true, "firstCallDate": "2025-01-15", "callPrice": 101.5,
            "callSchedule": [ { "callDt": "2025-01-15", "callPx": 101.5, "isParCall": false, "callType": "MakeWhole" } ],
            "makeWholeSpreadBps": 25, "isPuttable": true,
            "putSchedule": [ { "putDt": "2027-01-15", "putPx": 100.0 } ]
        },
        "auction": { "auctionDate": "2020-01-10", "auctionType": "Competitive" },
        "sweep": { "programName": "Overnight Sweep", "sweepVehicleType": "MoneyMarketFund", "sweepFrequency": "Daily", "targetAccountType": "Operating" },
        "financing": { "counterparty": "Dealer A", "collateralType": "UST", "haircut": 2.0, "openDate": "2020-01-15", "closeDate": "2020-01-16" },
        "issuer": {
            "issuerName": "Acme Corp", "institutionName": "Acme Bank", "issuerProgram": "MTN", "leiCode": "5493001KJTIIGC8Y1R12",
            "ultimateParentName": "Acme Holdings", "issuerSector": "Industrials", "issuerCountry": "US"
        },
        "equityBehavior": { "shareClass": "A", "votingRights": "FullVoting", "distributionType": "Cumulative" },
        "fund": { "fundFamily": "Acme Government MMF", "weightedAverageMaturityDays": 34, "sweepEligible": true, "liquidityFeeEligible": false },
        "structuredProduct": {
            "factor": 0.8125, "factorDate": "2026-01-01", "weightedAvgCoupon": 5.1, "weightedAvgMaturityMonths": 320,
            "weightedAvgLoanAgeMos": 40, "collateralType": "Residential", "poolIdentifier": "FN-123456", "trancheClass": "A1",
            "prepaymentAssumption": { "model": "Psa", "speed": 150 }, "averageLifeYears": 6.5, "isInterestOnly": false,
            "isPrincipalOnly": false, "notionalBalance": 25000000, "originator": "Acme Mortgage", "creditEnhancementPct": 12.5
        }
    }
    """);

    /// <summary>
    /// The complete flat key set <c>Convert</c> writes for <see cref="FullEconomicTerms"/>. Anything
    /// the bridge learns to carry must be added here; anything it stops carrying fails here.
    /// </summary>
    private static readonly string[] ExpectedFlatKeys =
    [
        "schemaVersion",
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlattenedFromMarkerProperty,
        "maturityDate", "issueDate", "effectiveDate",
        "couponType", "couponRate", "paymentFrequency", "dayCount",
        "accrualStartDate",
        "discountRate", "yieldRate"
    ];

    private static IReadOnlyList<string> PropertyNames(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).ToArray();

    // ── Module inventory ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EconomicSerializer_EmitsExactlyTheModulesTheBridgeClassifies()
    {
        // The real emitted key set, read off the adapter rather than restated by hand: every module
        // key is written (null when the definition has no such module), so any projection exposes
        // the full inventory.
        var projection = new SecurityProjectionRecord(
            Guid.Parse("cdcdcdcd-0000-0000-0000-000000000001"),
            "Equity",
            SecurityStatusDto.Active,
            "Coverage equity",
            "USD",
            "Ticker",
            "CVR",
            Json("""{"displayName":"Coverage equity","currency":"USD"}"""),
            Json("""{"schemaVersion":1,"shareClass":"Common"}"""),
            Json("""{"sourceSystem":"tests","updatedBy":"tests","asOf":"2026-01-01T00:00:00Z"}"""),
            1,
            DateTimeOffset.UtcNow,
            null,
            [],
            []);

        var emitted = PropertyNames(SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection).EconomicTerms)
            .Where(name => name != "schemaVersion")
            .ToArray();

        emitted.Should().BeEquivalentTo(
            SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.EconomicTermsModules,
            "every module the economic serializer emits must be classified as bridged or dropped by the v2 → v1 bridge");

        PropertyNames(FullEconomicTerms)
            .Where(name => name != "schemaVersion")
            .Should().BeEquivalentTo(emitted, "the coverage fixture must carry every emitted module");

        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.BridgedModules
            .Intersect(SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.DroppedModules, StringComparer.Ordinal)
            .Should().BeEmpty("a module is either bridged or dropped, never both");
    }

    // ── What survives ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_WritesExactlyTheEnumeratedFlatKeys()
    {
        var flattened = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(FullEconomicTerms).Payload;

        PropertyNames(flattened).Should().BeEquivalentTo(
            ExpectedFlatKeys,
            "the bridge's output is enumerated so that a change in what it carries is a test change, not a surprise");
    }

    [Theory]
    [InlineData("maturity", "maturityDate", "maturityDate", "\"2030-01-15\"")]
    [InlineData("maturity", "issueDate", "issueDate", "\"2020-01-15\"")]
    [InlineData("maturity", "effectiveDate", "effectiveDate", "\"2020-01-15\"")]
    [InlineData("coupon", "couponType", "couponType", "\"Fixed\"")]
    [InlineData("coupon", "couponRate", "couponRate", "4.25")]
    [InlineData("coupon", "paymentFrequency", "paymentFrequency", "\"SemiAnnual\"")]
    [InlineData("coupon", "dayCount", "dayCount", "\"Thirty360\"")]
    [InlineData("accrual", "accrualStartDate", "accrualStartDate", "\"2020-01-15\"")]
    [InlineData("discount", "discountRate", "discountRate", "3.9")]
    [InlineData("discount", "yieldRate", "yieldRate", "4.1")]
    public void Convert_BridgedField_SurvivesAtItsFlatKey(string module, string sourceField, string flatKey, string expectedRawValue)
    {
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.BridgedModules.Should().Contain(module);
        FullEconomicTerms.GetProperty(module).TryGetProperty(sourceField, out _).Should().BeTrue("the fixture must carry the source field");

        var flattened = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(FullEconomicTerms).Payload;

        flattened.TryGetProperty(flatKey, out var value).Should().BeTrue($"{module}.{sourceField} is a bridged field");
        value.GetRawText().Should().Be(expectedRawValue);
    }

    [Fact]
    public void Convert_CouponBlockWinsOverPaymentAndAccrualBlocks()
    {
        // The fixture deliberately carries a different frequency and day count on the payment and
        // accrual blocks so the precedence is observable.
        var flattened = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(FullEconomicTerms).Payload;

        flattened.GetProperty("paymentFrequency").GetString().Should().Be("SemiAnnual");
        flattened.GetProperty("dayCount").GetString().Should().Be("Thirty360");
    }

    // ── What is lost ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("redemption")]
    [InlineData("call")]
    [InlineData("auction")]
    [InlineData("sweep")]
    [InlineData("financing")]
    [InlineData("issuer")]
    [InlineData("equityBehavior")]
    [InlineData("fund")]
    [InlineData("structuredProduct")]
    public void Convert_DroppedModule_LeavesNoTraceInTheFlatPayload(string module)
    {
        // This asserts the DOCUMENTED loss. If the bridge learns to carry one of these modules —
        // which needs an asset-class-aware flattening, since the flat spelling of a call date or a
        // pool factor differs per class — move the module to BridgedModules, add its flat keys to
        // ExpectedFlatKeys, and give it a row in Convert_BridgedField_SurvivesAtItsFlatKey.
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.DroppedModules.Should().Contain(module);
        var source = FullEconomicTerms.GetProperty(module);
        source.EnumerateObject().Should().NotBeEmpty("the fixture must populate the module so its loss is observable");

        var flattened = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(FullEconomicTerms).Payload;

        flattened.TryGetProperty(module, out _).Should().BeFalse("the module object itself is not carried");
        foreach (var field in source.EnumerateObject())
        {
            flattened.TryGetProperty(field.Name, out _)
                .Should().BeFalse($"{module}.{field.Name} is dropped by the v2 → v1 bridge");
        }
    }

    [Theory]
    [InlineData("accrual", "accrualMethod")]
    [InlineData("accrual", "exDividendDays")]
    [InlineData("accrual", "businessDayConvention")]
    [InlineData("accrual", "holidayCalendar")]
    [InlineData("payment", "paymentLagDays")]
    [InlineData("payment", "paymentCurrency")]
    public void Convert_DroppedFieldInsideABridgedModule_IsNotCarried(string module, string field)
    {
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.BridgedModules.Should().Contain(module);
        FullEconomicTerms.GetProperty(module).TryGetProperty(field, out _).Should().BeTrue("the fixture must carry the field");

        var flattened = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(FullEconomicTerms).Payload;

        flattened.TryGetProperty(field, out _).Should().BeFalse($"{module}.{field} has no flat v1 counterpart");
    }

    // ── The marker ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_MarksTheOutputAsFlattenedFromEconomicTerms()
    {
        var flattened = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(FullEconomicTerms).Payload;

        flattened.GetProperty(SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlattenedFromMarkerProperty)
            .GetInt32().Should().Be(EconomicTermsSchema.Current);
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.WasFlattenedFromEconomicTerms(flattened).Should().BeTrue();

        // A payload that was always flat carries no marker, and the v0 stamping route adds none.
        var retained = SecurityAssetSpecificTermsUpcasterChain.Normalize(Json("""{"maturity":"2030-01-15","couponType":"Fixed"}""")).Payload;
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.WasFlattenedFromEconomicTerms(retained).Should().BeFalse();
    }

    [Fact]
    public void ToProjection_RebuildFallbackCarriesTheMarker_AndRetainedTermsDoNot()
    {
        // The rebuild path: an event stored without legacyAssetSpecificTerms folds into an economic
        // record whose LegacyAssetSpecificTerms is null, and ToProjection takes the lossy route.
        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(EconomicRecord(legacyAssetSpecificTerms: null));

        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.WasFlattenedFromEconomicTerms(rebuilt.AssetSpecificTerms)
            .Should().BeTrue("a projection reconstructed by the lossy route must be distinguishable from one that always carried flat terms");
        rebuilt.AssetSpecificTerms.GetProperty("maturityDate").GetString().Should().Be("2030-01-15");
        rebuilt.AssetSpecificTerms.TryGetProperty("isCallable", out _).Should().BeFalse("the call module is dropped on this route");

        // The authoritative path: the retained v1 payload is used verbatim and is never marked.
        var retainedTerms = Json("""{"schemaVersion":1,"maturity":"2030-01-15","couponType":"Fixed","isCallable":true,"callDate":"2025-01-15"}""");
        var retained = SecurityEconomicDefinitionAdapter.ToProjection(EconomicRecord(retainedTerms));

        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.WasFlattenedFromEconomicTerms(retained.AssetSpecificTerms).Should().BeFalse();
        retained.AssetSpecificTerms.GetRawText().Should().Be(retainedTerms.GetRawText());
    }

    private static SecurityEconomicDefinitionRecord EconomicRecord(JsonElement? legacyAssetSpecificTerms)
        => new(
            SecurityId: Guid.Parse("cdcdcdcd-0000-0000-0000-000000000002"),
            AssetClass: "FixedIncome",
            AssetFamily: "CorporateDebt",
            SubType: "CorporateBond",
            TypeName: "Bond",
            IssuerType: "Corporate",
            RiskCountry: "US",
            Status: SecurityStatusDto.Active,
            DisplayName: "Callable coverage bond",
            Currency: "USD",
            Classification: Json("""{"assetClass":"FixedIncome"}"""),
            CommonTerms: Json("""{"displayName":"Callable coverage bond","currency":"USD"}"""),
            EconomicTerms: FullEconomicTerms,
            Provenance: Json("""{"sourceSystem":"tests","updatedBy":"tests","asOf":"2026-01-01T00:00:00Z"}"""),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow,
            EffectiveTo: null,
            Identifiers: [],
            LegacyAssetClass: "Bond",
            LegacyAssetSpecificTerms: legacyAssetSpecificTerms);
}
