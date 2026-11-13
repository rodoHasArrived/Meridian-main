using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

/// <summary>
/// The bond <c>SecurityTerms</c> input hash and the lot <c>SecurityHash</c> evidence identity are
/// invariant to the declared <c>termsFormat</c> stamp a write, a replay or migration 068 adds to
/// unchanged terms, and receipts retained before that rule (over untagged or over already-tagged
/// terms) still verify, while any economic change still fails.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BondSecurityTermsDigestTests
{
    private static readonly Guid SecurityId = Guid.Parse("5e1f3c0a-9c55-4c42-9f0e-7d0c3b1d2a68");
    private static readonly DateTimeOffset AsOf = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly BondCalculationContext Context = new(AsOf, new DateOnly(2026, 4, 15));

    internal const string UntaggedTerms =
        """{"assetClassTermsVersion":2,"issueDate":"2026-01-15","maturity":"2028-01-15","couponType":"Fixed","couponRate":5,"dayCount":"30/360","par":100,"paymentFrequency":"SemiAnnual","isCallable":false,"scheduleConventions":{"accrualAdjusted":false,"couponRateUnit":"Percent","couponCashBasis":"Level"}}""";

    // PostgreSQL jsonb text of UntaggedTerms (keys by length, then bytes; ", " and ": " separators),
    // and the same row after migration 068's jsonb_set(..., '{termsFormat}', '"flat"', true).
    internal const string UntaggedJsonbTerms =
        """{"par": 100, "dayCount": "30/360", "maturity": "2028-01-15", "issueDate": "2026-01-15", "couponRate": 5, "couponType": "Fixed", "isCallable": false, "paymentFrequency": "SemiAnnual", "scheduleConventions": {"couponRateUnit": "Percent", "accrualAdjusted": false, "couponCashBasis": "Level"}, "assetClassTermsVersion": 2}""";

    internal const string StampedJsonbTerms =
        """{"par": 100, "dayCount": "30/360", "maturity": "2028-01-15", "issueDate": "2026-01-15", "couponRate": 5, "couponType": "Fixed", "isCallable": false, "termsFormat": "flat", "paymentFrequency": "SemiAnnual", "scheduleConventions": {"couponRateUnit": "Percent", "accrualAdjusted": false, "couponCashBasis": "Level"}, "assetClassTermsVersion": 2}""";

    // ── RemoveRedundantDeclaredFormat ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"schemaVersion":1,"shareClass":"Common","note":"café <b>","n":1.50}""")]
    [InlineData("""{"shareClass":"Common","n":1.50}""")]
    [InlineData("""{ "schemaVersion" : 1 ,  "shareClass": "Common" }""")]
    [InlineData("""{"schemaVersion":1}""")]
    [InlineData("""{}""")]
    [InlineData("""{ }""")]
    [InlineData("""{"customProfileId":"co-invest-spv","profileVersion":2,"profileFields":{"vehicleName":"SPV I"}}""")]
    [InlineData(UntaggedTerms)]
    [InlineData(UntaggedJsonbTerms)]
    public void RemoveRedundantDeclaredFormat_IsTheByteExactInverseOfTheStamp_AndIdempotent(string untagged)
    {
        var stamped = SecurityTermsEnvelope.StampDeclaredFormat(untagged);
        stamped.Should().NotBe(untagged);
        SecurityTermsEnvelope.StampDeclaredFormat(Parse(untagged)).GetRawText().Should().Be(stamped);
        SecurityTermsLegacyEventClassifier.StampDeclaredFormat(Parse(untagged)).GetRawText().Should().Be(stamped);

        var removed = SecurityTermsEnvelope.RemoveRedundantDeclaredFormat(Parse(stamped));

        removed.Should().Be(untagged);
        SecurityTermsEnvelope.RemoveRedundantDeclaredFormat(Parse(removed)).Should().Be(untagged);
    }

    [Theory]
    [InlineData(
        """{"par": 100, "termsFormat": "flat", "schemaVersion": 1}""",
        """{"par": 100, "schemaVersion": 1}""")]
    [InlineData(
        """{"termsFormat": "flat", "assetClassTermsVersion": 2}""",
        """{"assetClassTermsVersion": 2}""")]
    [InlineData("""{"termsFormat": "flat"}""", """{}""")]
    [InlineData(StampedJsonbTerms, UntaggedJsonbTerms)]
    public void RemoveRedundantDeclaredFormat_UndoesAJsonbSetStamp_InPostgresCanonicalText(string stamped, string untagged)
        => SecurityTermsEnvelope.RemoveRedundantDeclaredFormat(Parse(stamped)).Should().Be(untagged);

    [Theory]
    // Undeclared, or not an object: nothing to remove.
    [InlineData("""{"schemaVersion":1,"shareClass":"Common"}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""null""")]
    // A non-string termsFormat declares nothing.
    [InlineData("""{"schemaVersion":1,"termsFormat":null,"shareClass":"Common"}""")]
    // Declarations the shape rule would not infer could change what the terms decode to.
    [InlineData("""{"schemaVersion":1,"termsFormat":"economic","shareClass":"Common"}""")]
    [InlineData("""{"schemaVersion":1,"termsFormat":"customProfile","shareClass":"Common"}""")]
    [InlineData("""{"customProfileId":"co-invest-spv","termsFormat":"flat","profileFields":{}}""")]
    [InlineData("""{"schemaVersion":2,"termsFormat":"flat","maturity":{"maturityDate":"2030-01-01"},"coupon":null}""")]
    [InlineData("""{"schemaVersion":1,"termsFormat":"unknownFamily","shareClass":"Common"}""")]
    // A duplicate declaration is never cut.
    [InlineData("""{"termsFormat":"flat","schemaVersion":1,"termsFormat":"flat"}""")]
    // A nested termsFormat is a term field, not the envelope key.
    [InlineData("""{"schemaVersion":1,"nested":{"termsFormat":"flat"}}""")]
    public void RemoveRedundantDeclaredFormat_LeavesEveryOtherPayloadByteForByte(string json)
    {
        SecurityTermsEnvelope.RemoveRedundantDeclaredFormat(Parse(json)).Should().Be(json);
        SecurityTermsEnvelope.WithoutRedundantDeclaredFormat(Parse(json)).GetRawText().Should().Be(json);
    }

    // ── BondSecurityTermsDigest ────────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_IsInvariantToTheStamp_ComputeRawTextIsNot()
    {
        var common = Parse("""{"issuer":"Acme"}""");
        var untagged = Parse(UntaggedTerms);
        var stamped = SecurityTermsEnvelope.StampDeclaredFormat(untagged);

        BondSecurityTermsDigest.Compute(common, stamped).Should().Be(BondSecurityTermsDigest.Compute(common, untagged))
            .And.Be(BondSecurityTermsDigest.ComputeRawText(common, untagged));
        BondSecurityTermsDigest.ComputeRawText(common, stamped).Should().NotBe(BondSecurityTermsDigest.Compute(common, stamped));
        BondSecurityTermsDigest.Compute(common, Parse(StampedJsonbTerms))
            .Should().Be(BondSecurityTermsDigest.ComputeRawText(common, Parse(UntaggedJsonbTerms)));
    }

    [Fact]
    public void Resolve_ReportsTheRetainedRawHash_OnlyForExactlyTheseTerms()
    {
        var common = Parse("""{"issuer":"Acme"}""");
        var stamped = SecurityTermsEnvelope.StampDeclaredFormat(Parse(UntaggedTerms));
        var canonical = BondSecurityTermsDigest.Compute(common, stamped);
        var legacyTagged = BondSecurityTermsDigest.ComputeRawText(common, stamped);
        var otherTerms = SecurityTermsEnvelope.StampDeclaredFormat(Parse(UntaggedTerms.Replace("\"couponRate\":5", "\"couponRate\":6")));

        BondSecurityTermsDigest.Resolve(common, stamped, null).Should().Be(canonical);
        BondSecurityTermsDigest.Resolve(common, stamped, canonical).Should().Be(canonical);
        BondSecurityTermsDigest.Resolve(common, stamped, legacyTagged).Should().Be(legacyTagged);
        BondSecurityTermsDigest.Resolve(common, otherTerms, legacyTagged).Should().Be(BondSecurityTermsDigest.Compute(common, otherTerms));
        BondSecurityTermsDigest.Resolve(common, stamped, new string('a', 64)).Should().Be(canonical);
    }

    // ── Settlement receipts through the real analytics engine ─────────────────────────────────

    [Fact]
    public async Task UntaggedEraReceipt_VerifiesAfterTheInMemoryStamp()
    {
        var untagged = Bond(UntaggedTerms);
        var receipt = await PrepareAsync(untagged);
        receipt.Inputs.Single(input => input.Kind == "SecurityTerms").ContentSha256
            .Should().Be(BondSecurityTermsDigest.ComputeRawText(untagged.CommonTerms, untagged.AssetSpecificTerms),
                "for untagged terms the canonical hash is the raw-text hash receipts retained before this rule");

        var stamped = untagged with
        {
            AssetSpecificTerms = SecurityTermsLegacyEventClassifier.StampDeclaredFormat(untagged.AssetSpecificTerms)
        };
        SecurityTermsEnvelope.GetDeclaredFormat(stamped.AssetSpecificTerms).Should().Be(SecurityTermsFormats.Flat);

        (await VerifyAsync(stamped, receipt)).Should().BeEmpty();
    }

    [Fact]
    public async Task UntaggedEraReceipt_VerifiesAfterMigration068StampsTheStoredRow()
    {
        var receipt = await PrepareAsync(Bond(UntaggedJsonbTerms));

        (await VerifyAsync(Bond(StampedJsonbTerms), receipt)).Should().BeEmpty();
    }

    [Fact]
    public async Task AlreadyTaggedReceipt_RetainingTheRawTextHash_StillVerifies()
    {
        var tagged = Bond(SecurityTermsEnvelope.StampDeclaredFormat(UntaggedTerms));
        var current = await PrepareAsync(tagged);
        // A flat writer stamped these terms before this rule, so the retained receipt hashed the raw text with the tag.
        var legacy = WithSecurityTermsHash(current, BondSecurityTermsDigest.ComputeRawText(tagged.CommonTerms, tagged.AssetSpecificTerms));
        legacy.Inputs.Should().NotEqual(current.Inputs);

        (await VerifyAsync(tagged, legacy)).Should().BeEmpty();
        (await VerifyAsync(tagged, current)).Should().BeEmpty();
    }

    [Fact]
    public async Task EconomicChange_FailsWithSettlementInputsChanged_ForEveryRetainedForm()
    {
        var untagged = Bond(UntaggedTerms);
        var tagged = Bond(SecurityTermsEnvelope.StampDeclaredFormat(UntaggedTerms));
        var untaggedEra = await PrepareAsync(untagged);
        var taggedEra = WithSecurityTermsHash(await PrepareAsync(tagged),
            BondSecurityTermsDigest.ComputeRawText(tagged.CommonTerms, tagged.AssetSpecificTerms));
        // Same metrics: only a non-pricing term moves, so the input manifest alone must refuse it.
        var changed = Bond(SecurityTermsEnvelope.StampDeclaredFormat(
            UntaggedTerms.Replace("\"isCallable\":false", "\"isCallable\":false,\"issuerNote\":\"amended\"")));
        var coupon = Bond(SecurityTermsEnvelope.StampDeclaredFormat(UntaggedTerms.Replace("\"couponRate\":5", "\"couponRate\":5.5")));
        var declaredEconomic = Bond(UntaggedTerms.Replace("{\"assetClassTermsVersion\"", "{\"termsFormat\":\"economic\",\"assetClassTermsVersion\""));

        foreach (var receipt in new[] { untaggedEra, taggedEra })
        {
            (await VerifyAsync(changed, receipt)).Should().ContainSingle().Which.Code.Should().Be("SettlementInputsChanged");
            (await VerifyAsync(coupon, receipt)).Should().ContainSingle().Which.Code.Should().Be("SettlementInputsChanged");
            (await VerifyAsync(declaredEconomic, receipt)).Should().NotBeEmpty();
        }
    }

    // ── Lot SecurityHash evidence ──────────────────────────────────────────────────────────────

    [Fact]
    public void LotSecurityHash_UntaggedEraEvidence_MatchesTheStampedProjection_AndEconomicChangesDoNot()
    {
        var untagged = Projection(UntaggedJsonbTerms);
        var stamped = Projection(StampedJsonbTerms);
        var retained = OpenLotAmortization.SecurityHash(untagged);

        OpenLotAmortization.SecurityHash(stamped).Should().NotBe(retained);
        OpenLotAmortization.AcceptedSecurityHashes(stamped).Should().Equal(OpenLotAmortization.SecurityHash(stamped), retained);
        OpenLotAmortization.AcceptedSecurityHashes(untagged).Should().Equal(retained);
        OpenLotAmortization.SameSecurityHash(untagged, stamped).Should().BeTrue();
        OpenLotAmortization.SameSecurityHash(stamped, stamped).Should().BeTrue();

        var changed = Projection(StampedJsonbTerms.Replace("\"couponRate\": 5", "\"couponRate\": 6"));
        OpenLotAmortization.AcceptedSecurityHashes(changed).Should().NotContain(retained)
            .And.NotContain(OpenLotAmortization.SecurityHash(stamped));
        OpenLotAmortization.SameSecurityHash(untagged, changed).Should().BeFalse();
        OpenLotAmortization.SameSecurityHash(untagged, Projection(StampedJsonbTerms.Replace("\"flat\"", "\"economic\""))).Should().BeFalse();
    }

    private static async Task<BondSettlementConsiderationDto> PrepareAsync(SecurityDetailDto security)
    {
        var result = await new BondSettlementConsiderationService(Analytics(security)).PrepareAsync(new(
            new(SecurityId, Context, CleanPrice: 99m), 100_000m, 1m, 0m, 100_250m,
            "cash-source", "Assets/Cash", "Assets/PurchasedInterest", "Expenses/Fees"));
        result.Blockers.Should().BeEmpty();
        return result.Settlement!;
    }

    private static Task<IReadOnlyList<BondAnalyticsBlocker>> VerifyAsync(SecurityDetailDto security, BondSettlementConsiderationDto receipt)
        => new BondSettlementConsiderationService(Analytics(security)).VerifyAsync(receipt);

    private static BondSettlementConsiderationDto WithSecurityTermsHash(BondSettlementConsiderationDto receipt, string hash)
        => receipt with
        {
            Inputs = receipt.Inputs.Select(input => input.Kind == "SecurityTerms" ? input with { ContentSha256 = hash } : input).ToArray()
        };

    private static BondAnalyticsService Analytics(SecurityDetailDto security)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        return new(query, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]));
    }

    private static SecurityDetailDto Bond(string assetSpecificTerms)
        => new(SecurityId, "Bond", SecurityStatusDto.Active, "Digest bond", "USD",
            Parse("""{"issuer": "Acme"}"""), Parse(assetSpecificTerms), [], [], 7, AsOf, null);

    private static SecurityProjectionRecord Projection(string assetSpecificTerms)
        => new(SecurityId, "Bond", SecurityStatusDto.Active, "Digest bond", "USD", "Cusip", "123456AB7",
            Parse("""{"issuer": "Acme"}"""), Parse(assetSpecificTerms), Parse("""{"source": "test"}"""), 7, AsOf, null, [], []);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
