using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Guards the asset-class-aware half of the cross-family v2 → v1 bridge
/// (<see cref="SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(JsonElement, string?)"/>):
/// every flat key it writes is declared for the class, and a fully populated v1 record taken through
/// v1 → economic definition → (legacy terms discarded, as on event replay) → v1 comes back with the
/// same value at every key the economic document carries.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityEconomicTermsClassAwareBridgeTests
{
    public static TheoryData<string> DeclaredAssetClasses() => SecurityAssetTermsSchemaRoundTripTests.DeclaredAssetClasses();

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void RecoverableKeys_AreDeclaredForTheirClass(string assetClass)
    {
        var declared = SecurityAssetTermsSchema.Fields(assetClass).Select(static field => field.Key).ToHashSet(StringComparer.Ordinal);

        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.RecoverableKeys(assetClass)
            .Should().OnlyContain(key => declared.Contains(key), $"the class-aware bridge may only write keys {assetClass} declares")
            .And.OnlyHaveUniqueItems();

        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.UnrecoverableKeys(assetClass)
            .Concat(SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.RecoverableKeys(assetClass))
            .Should().BeEquivalentTo(declared, "recoverable and unrecoverable keys partition the declared schema");
    }

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void ReplayWithoutLegacyTerms_RecoversEveryCarriedValueAtItsDeclaredKey(string assetClass)
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            assetClass,
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, original));

        if (assetClass == "Equity")
        {
            // This fixture is ConvertiblePreferred. Its economic document cannot recover the
            // classification or complete terms, so replay must refuse instead of degrading it.
            Action replay = () => SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
            replay.Should().Throw<InvalidOperationException>().WithMessage("*requires retained legacy*");
            return;
        }

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null }).AssetSpecificTerms;

        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.WasFlattenedFromEconomicTerms(rebuilt).Should().BeTrue();
        var declared = SecurityAssetTermsSchema.Fields(assetClass).Select(static field => field.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var property in rebuilt.EnumerateObject())
        {
            if (SecurityTermsEnvelope.IsReservedKey(property.Name))
            {
                continue;
            }

            declared.Should().Contain(property.Name, $"the rebuilt {assetClass} payload must read like an original v1 write");
            original.TryGetProperty(property.Name, out var originalValue).Should().BeTrue();
            if (originalValue.ValueKind == JsonValueKind.Number)
            {
                property.Value.GetDecimal().Should().Be(originalValue.GetDecimal(), $"{assetClass}.{property.Name} must survive replay");
            }
            else
            {
                property.Value.GetRawText().Should().Be(originalValue.GetRawText(), $"{assetClass}.{property.Name} must survive replay");
            }
        }
    }

    [Theory]
    [InlineData("CertificateOfDeposit", "callableDate")]
    [InlineData("CertificateOfDeposit", "issuerName")]
    [InlineData("Deposit", "institutionName")]
    [InlineData("Deposit", "isCallable")]
    [InlineData("StructuredCredit", "currentFactor")]
    [InlineData("StructuredCredit", "poolId")]
    [InlineData("StructuredCredit", "tranche")]
    [InlineData("StructuredCredit", "originalFace")]
    [InlineData("StructuredCredit", "weightedAverageCoupon")]
    [InlineData("StructuredCredit", "weightedAverageMaturityMonths")]
    [InlineData("StructuredCredit", "weightedAverageLoanAgeMonths")]
    [InlineData("StructuredCredit", "factorSchedule")]
    [InlineData("Repo", "counterparty")]
    [InlineData("Repo", "haircut")]
    [InlineData("CashSweep", "programName")]
    [InlineData("MoneyMarketFund", "liquidityFeeEligible")]
    [InlineData("TreasuryBill", "auctionDate")]
    [InlineData("CommercialPaper", "isAssetBacked")]
    [InlineData("Bond", "maturity")]
    [InlineData("Bond", "couponType")]
    [InlineData("Bond", "issueDate")]
    [InlineData("Bond", "isCallable")]
    [InlineData("Bond", "callDate")]
    [InlineData("Bond", "issuerName")]
    [InlineData("Bond", "paymentFrequency")]
    [InlineData("Swap", "maturityDate")]
    [InlineData("CreditDefaultSwap", "maturityDate")]
    [InlineData("Option", "expiry")]
    public void ReplayWithoutLegacyTerms_RecoversPreviouslyDroppedTerms(string assetClass, string flatKey)
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            assetClass,
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, original));

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null }).AssetSpecificTerms;

        rebuilt.TryGetProperty(flatKey, out var value).Should().BeTrue($"{assetClass}.{flatKey} is carried by the economic document");
        value.GetRawText().Should().Be(original.GetProperty(flatKey).GetRawText());
    }

    [Fact]
    public void ReplayWithoutLegacyTerms_RestoresTheCreditDefaultSwapCouponInBasisPoints()
    {
        // The upgrade carries FixedCouponBps / 10000 as coupon.couponRate; replay must invert the
        // unit change rather than drop the schema-required coupon.
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            "CreditDefaultSwap",
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads["CreditDefaultSwap"]);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("CreditDefaultSwap", original));

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null }).AssetSpecificTerms;

        rebuilt.GetProperty("fixedCouponBps").GetDecimal().Should().Be(original.GetProperty("fixedCouponBps").GetDecimal());
    }

    [Theory]
    [InlineData("InterestOnly", "InterestOnly")]
    [InlineData("PrincipalOnly", "PrincipalOnly")]
    [InlineData("Clo", null)]
    public void ReplayWithoutLegacyTerms_StructuredCreditRecoversStripKindsFromTheEconomicFlags(string kind, string? expectedRebuiltKind)
    {
        // The economic document carries the typed kind only as the IO/PO flags, so replay can give
        // an IO or PO strip its kind back; any other family is not recoverable and stays absent
        // rather than being guessed.
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            "StructuredCredit",
            new
            {
                tranche = "X-1",
                collateralType = "AgencyPool",
                originalFace = 1_000_000m,
                couponOrIndex = "5.5",
                maturity = "2036-01-15",
                structuredCreditKind = kind
            });
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("StructuredCredit", original));

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null }).AssetSpecificTerms;

        if (expectedRebuiltKind is null)
        {
            rebuilt.TryGetProperty("structuredCreditKind", out _).Should().BeFalse("the economic document does not carry this family");
        }
        else
        {
            rebuilt.GetProperty("structuredCreditKind").GetString().Should().Be(expectedRebuiltKind);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReplayWithoutLegacyTerms_CommercialPaperKeepsItsRequiredAssetBackedFlag(bool isAssetBacked)
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            "CommercialPaper",
            new
            {
                issuerName = "Meridian Funding LLC",
                maturity = "2026-11-15",
                discountRate = 5.05m,
                dayCount = "ACT/360",
                isAssetBacked
            });
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("CommercialPaper", original));

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null }).AssetSpecificTerms;

        rebuilt.GetProperty("isAssetBacked").GetBoolean().Should().Be(isAssetBacked);
    }

    [Theory]
    [InlineData("Preferred", "Pari")]
    [InlineData("Preferred", "Senior")]
    [InlineData("ConvertiblePreferred", "Subordinated")]
    public void PreferredReplay_PreservesRetainedTermsAndRefusesAnIncompleteFallback(string classification, string preference)
    {
        var payload = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads["Equity"]))!;
        payload["classification"] = classification;
        payload["preferredTerms"]!["liquidationPreference"]!["kind"] = preference;
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("Equity",
            JsonSerializer.SerializeToElement(payload));
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Equity", original));

        SecurityEconomicDefinitionAdapter.ToProjection(economic).AssetSpecificTerms.GetRawText()
            .Should().Be(original.GetRawText());
        Action replay = () => SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        replay.Should().Throw<InvalidOperationException>().WithMessage("*complete classification*");
    }

    [Theory]
    [InlineData("Preferred")]
    [InlineData("ConvertiblePreferred")]
    public void PreferredReplay_RefusesMissingEconomicsEvenWhenNoPreferredModuleSurvives(string classification)
    {
        var payload = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads["Equity"]))!;
        payload["classification"] = classification;
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("Equity",
            JsonSerializer.SerializeToElement(payload));
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Equity", original)) with
        {
            LegacyAssetSpecificTerms = null,
            AssetFamily = "PreferredEquity",
            SubType = "PreferredShare",
            Classification = JsonSerializer.SerializeToElement(new
            {
                assetClass = "Equity",
                assetFamily = "PreferredEquity",
                subType = "PreferredShare",
                classification
            }),
            EconomicTerms = JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 2,
                equityBehavior = new { shareClass = "A" }
            })
        };

        Action replay = () => SecurityEconomicDefinitionAdapter.ToProjection(economic);
        replay.Should().Throw<InvalidOperationException>().WithMessage("*requires retained legacy*");
    }

    [Fact]
    public void CommonEquityReplay_RequiresRetainedTerms_BecauseStructuralMetadataDoesNotProveCommon()
    {
        var original = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain("Equity",
            new { shareClass = "A", classification = "Common", votingRightsCat = "FullVoting" });
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Equity", original));
        SecurityEconomicDefinitionAdapter.ToProjection(economic).AssetSpecificTerms.GetRawText()
            .Should().Be(original.GetRawText());
        Action replay = () => SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });
        replay.Should().Throw<InvalidOperationException>().WithMessage("*complete classification*",
            "legacy preferred equities carry the same structural Common metadata, so absence of preferred clues is ambiguous");
    }

    [Fact]
    public void Convert_UnknownOrMissingClass_FallsBackToTheClassIndependentBridge()
    {
        using var document = JsonDocument.Parse("""{"schemaVersion":2,"maturity":{"maturityDate":"2030-01-15"},"call":{"isCallable":true}}""");

        var unknown = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(document.RootElement, "NotAClass").Payload;
        var missing = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(document.RootElement, null).Payload;
        var generic = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(document.RootElement).Payload;

        unknown.GetRawText().Should().Be(generic.GetRawText());
        missing.GetRawText().Should().Be(generic.GetRawText());
        generic.GetProperty("maturityDate").GetString().Should().Be("2030-01-15");
    }

    [Fact]
    public void UnrecoverableKeys_NameWhatTheEconomicDocumentNeverCarried()
    {
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.UnrecoverableKeys("Option")
            .Should().Contain(["strike", "putCall", "underlyingId"]);
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.UnrecoverableKeys("Swap").Should().Contain("legs");
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.UnrecoverableKeys("Bond").Should().Contain("subclass");
    }

    // ── Schedule conventions read ahead of write (ADR-024; Accrual rules frozen by ADR-025) ─────

    // The economic terms each fully-populated payload bridges to without a block, as pinned at base
    // by SecurityTermsWriteGoldenTests. The block may change only the accrual module.
    private const string BondEconomicTerms =
        """{"schemaVersion":2,"termsFormat":"economic","maturity":{"effectiveDate":null,"issueDate":"2025-06-15","maturityDate":"2035-06-15"},"coupon":{"couponType":"Floating","couponRate":null,"paymentFrequency":"Quarterly","dayCount":"ACT/360"},"discount":null,"accrual":null,"payment":null,"redemption":null,"call":{"isCallable":true,"firstCallDate":"2030-06-15","callPrice":null,"callSchedule":[],"makeWholeSpreadBps":null,"isPuttable":false,"putSchedule":[]},"auction":null,"sweep":null,"financing":null,"issuer":{"issuerName":"Meridian Capital Corp","institutionName":null,"issuerProgram":null,"leiCode":null,"ultimateParentName":null,"issuerSector":null,"issuerCountry":null},"equityBehavior":null,"fund":null,"structuredProduct":{"factor":null,"factorDate":null,"weightedAvgCoupon":null,"weightedAvgMaturityMonths":null,"weightedAvgLoanAgeMos":null,"collateralType":null,"poolIdentifier":null,"trancheClass":null,"prepaymentAssumption":null,"averageLifeYears":null,"isInterestOnly":false,"isPrincipalOnly":false,"notionalBalance":null,"originator":null,"creditEnhancementPct":null,"factorScheduleReference":null}}""";

    private const string CertificateOfDepositEconomicTerms =
        """{"schemaVersion":2,"termsFormat":"economic","maturity":{"effectiveDate":null,"issueDate":null,"maturityDate":"2027-09-30"},"coupon":{"couponType":"Fixed","couponRate":4.4,"paymentFrequency":null,"dayCount":"ACT/365"},"discount":null,"accrual":null,"payment":null,"redemption":null,"call":{"isCallable":true,"firstCallDate":"2026-09-30","callPrice":null,"callSchedule":[],"makeWholeSpreadBps":null,"isPuttable":false,"putSchedule":[]},"auction":null,"sweep":null,"financing":null,"issuer":{"issuerName":"First Meridian Bank","institutionName":null,"issuerProgram":null,"leiCode":null,"ultimateParentName":null,"issuerSector":null,"issuerCountry":null},"equityBehavior":null,"fund":null,"structuredProduct":null}""";

    private const string StructuredCreditEconomicTerms =
        """{"schemaVersion":2,"termsFormat":"economic","maturity":{"effectiveDate":null,"issueDate":null,"maturityDate":"2031-06-15"},"coupon":{"couponType":"SOFR\u002B250","couponRate":null,"paymentFrequency":null,"dayCount":null},"discount":null,"accrual":null,"payment":null,"redemption":null,"call":null,"auction":null,"sweep":null,"financing":null,"issuer":{"issuerName":"MRDN-2026-1","institutionName":null,"issuerProgram":"CLO","leiCode":null,"ultimateParentName":null,"issuerSector":null,"issuerCountry":null},"equityBehavior":null,"fund":null,"structuredProduct":{"factor":0.8235,"factorDate":null,"weightedAvgCoupon":7.85,"weightedAvgMaturityMonths":58,"weightedAvgLoanAgeMos":14,"collateralType":"CLO","poolIdentifier":"MRDN-2026-1","trancheClass":"B","prepaymentAssumption":null,"averageLifeYears":null,"isInterestOnly":false,"isPrincipalOnly":false,"notionalBalance":10000000,"originator":null,"creditEnhancementPct":null,"factorScheduleReference":"See trustee report 2026-07"}}""";

    private const string SwapEconomicTerms =
        """{"schemaVersion":2,"termsFormat":"economic","maturity":{"effectiveDate":"2026-01-15","issueDate":"2026-01-15","maturityDate":"2031-01-15"},"coupon":null,"discount":null,"accrual":null,"payment":null,"redemption":null,"call":null,"auction":null,"sweep":null,"financing":null,"issuer":null,"equityBehavior":null,"fund":null,"structuredProduct":null}""";

    private const string NullAccrual = "\"accrual\":null";

    [Theory]
    [InlineData("Bond")]
    [InlineData("CertificateOfDeposit")]
    [InlineData("StructuredCredit")]
    public void UnrecoverableKeys_IncludeTheScheduleConventionsBlock(string assetClass)
    {
        // The economic document carries only the block's business-day convention and joint payment
        // calendar (in Accrual), so the block as a whole is not recoverable on replay until a contract
        // step maps it; a swap's leg blocks travel inside the already-unrecoverable legs.
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.UnrecoverableKeys(assetClass).Should().Contain("scheduleConventions");
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.RecoverableKeys(assetClass).Should().NotContain("scheduleConventions");
    }

    [Theory]
    [InlineData("Bond", BondEconomicTerms, """{"accrualMethod":null,"accrualStartDate":null,"exDividendDays":null,"businessDayConvention":"ModifiedFollowing","holidayCalendar":"GBLO\u002BUSNY","dayCount":null}""")]
    [InlineData("CertificateOfDeposit", CertificateOfDepositEconomicTerms, """{"accrualMethod":null,"accrualStartDate":null,"exDividendDays":null,"businessDayConvention":"Following","holidayCalendar":"USNY","dayCount":null}""")]
    [InlineData("StructuredCredit", StructuredCreditEconomicTerms, """{"accrualMethod":null,"accrualStartDate":null,"exDividendDays":null,"businessDayConvention":"ModifiedFollowing","holidayCalendar":"GBLO\u002BUSNY","dayCount":null}""")]
    [InlineData("Swap", SwapEconomicTerms, null)]
    public void ReadableTwin_BridgesItsScheduleConventionsIntoTheAccrualModule(string assetClass, string economicTermsWithoutBlock, string? expectedAccrual)
    {
        // The frozen rules: the block fills Accrual on a bond, CD or structured credit only, with the
        // convention verbatim and the payment calendars as one joint calendar id. A swap's legs carry
        // their own blocks, and the swap has no Accrual module to fill.
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, SecurityAssetTermsSchemaRoundTripTests.ReadableTwin(assetClass)));

        economic.EconomicTerms.GetRawText().Should().Be(WithAccrual(economicTermsWithoutBlock, expectedAccrual));
    }

    [Theory]
    [InlineData(
        """{"stubPolicy":"Long"}""",
        null)]
    [InlineData(
        """{"accrualAdjusted":false,"businessDayConvention":"ModifiedFollowing"}""",
        """{"accrualMethod":null,"accrualStartDate":null,"exDividendDays":null,"businessDayConvention":"ModifiedFollowing","holidayCalendar":null,"dayCount":null}""")]
    [InlineData(
        """{"paymentCalendars":["USNY"]}""",
        """{"accrualMethod":null,"accrualStartDate":null,"exDividendDays":null,"businessDayConvention":null,"holidayCalendar":"USNY","dayCount":null}""")]
    [InlineData(
        """{"businessDayConvention":"Following","paymentCalendars":["USNY","GBLO","usny","GBLO"]}""",
        """{"accrualMethod":null,"accrualStartDate":null,"exDividendDays":null,"businessDayConvention":"Following","holidayCalendar":"GBLO\u002BUSNY","dayCount":null}""")]
    public void BondScheduleConventions_FillAccrualUnderTheFrozenRules(string block, string? expectedAccrual)
    {
        // Only a convention or a calendar fills the module; accrualAdjusted=false keeps the convention
        // verbatim rather than turning it into Unadjusted; duplicate and case-variant calendars collapse
        // to one joint id, the first spelling kept and the ids sorted ordinally.
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", BondTwin(block)));

        economic.EconomicTerms.GetRawText().Should().Be(WithAccrual(BondEconomicTerms, expectedAccrual));
    }

    [Fact]
    public void BondScheduleConventions_PaymentCalendarsGovernTheAccrualCalendar_AndCommonTermsKeepTheirOwn()
    {
        // The security-level holiday calendar stays in the common terms; the block's payment
        // calendars, not a merge of the two, are the accrual calendar.
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", BondTwin("""{"paymentCalendars":["GBLO"]}""")) with
        {
            CommonTerms = JsonSerializer.SerializeToElement(new { displayName = "Legacy Bond", currency = "USD", holidayCalendarId = "USNY" })
        };

        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);

        economic.EconomicTerms.GetRawText().Should().Be(WithAccrual(
            BondEconomicTerms,
            """{"accrualMethod":null,"accrualStartDate":null,"exDividendDays":null,"businessDayConvention":null,"holidayCalendar":"GBLO","dayCount":null}"""));

        // Through the domain: the record decoded from the row, and the common terms it writes back,
        // keep the security's own calendar rather than the block's.
        var record = SecurityMasterMapping.ToRecord(projection);
        record.Common.HolidayCalendarId.Should().NotBeNull("the common terms carry the security's holiday calendar");
        record.Common.HolidayCalendarId!.Value.Should().Be("USNY", "the block's payment calendars never replace or merge into the common calendar");
        using var writtenCommonTerms = JsonDocument.Parse(new SecurityMasterSnapshotWrapper(record).CommonTermsJson);
        writtenCommonTerms.RootElement.GetProperty("holidayCalendarId").GetString().Should().Be("USNY");
    }

    [Fact]
    public void BondScheduleConventions_WithoutPaymentCalendars_NeverFallBackToTheCommonTermsCalendar()
    {
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", BondTwin("""{"businessDayConvention":"Following"}""")) with
        {
            CommonTerms = JsonSerializer.SerializeToElement(new { displayName = "Legacy Bond", currency = "USD", holidayCalendarId = "USNY" })
        };

        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);

        economic.EconomicTerms.GetProperty("accrual").GetRawText().Should().Be(
            """{"accrualMethod":null,"accrualStartDate":null,"exDividendDays":null,"businessDayConvention":"Following","holidayCalendar":null,"dayCount":null}""");
    }

    [Theory]
    [InlineData("Bond")]
    [InlineData("CertificateOfDeposit")]
    [InlineData("StructuredCredit")]
    public void ReplayWithoutLegacyTerms_FlattensAtTheWriteVersionWithoutTheBlock_AndRetainsTheAccrualVerbatim(string assetClass)
    {
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, SecurityAssetTermsSchemaRoundTripTests.ReadableTwin(assetClass)));
        var accrual = economic.EconomicTerms.GetProperty("accrual");
        accrual.ValueKind.Should().Be(JsonValueKind.Object, "the twin's block fills the accrual module");

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null }).AssetSpecificTerms;

        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.WasFlattenedFromEconomicTerms(rebuilt).Should().BeTrue();
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(rebuilt).Should().Be(
            SecurityAssetClassCatalog.GetTermsVersion(assetClass),
            "the bridge flattens at the version this node writes, never at a version it only reads");
        rebuilt.EnumerateObject().Select(static property => property.Name)
            .Should().NotContain(name => string.Equals(name, "scheduleConventions", StringComparison.OrdinalIgnoreCase));
        SecurityTermsEnvelope.TryGetRetainedEconomicTerms(rebuilt, out var retained).Should().BeTrue();
        retained.GetProperty("accrual").GetRawText().Should().Be(accrual.GetRawText(), "the accrual travels verbatim in the retained economic terms");
    }

    private static JsonElement BondTwin(string block)
        => SecurityAssetTermsSchemaRoundTripTests.ReadableTwin("Bond", SecurityAssetTermsSchemaRoundTripTests.FullPayloads["Bond"], [block]);

    private static string WithAccrual(string economicTermsWithoutBlock, string? accrual)
        => accrual is null
            ? economicTermsWithoutBlock
            : economicTermsWithoutBlock.Replace(NullAccrual, $"\"accrual\":{accrual}", StringComparison.Ordinal);
}
