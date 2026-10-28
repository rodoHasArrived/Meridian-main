using System.Text.Json;
using System.Text.Json.Nodes;
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
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(rebuilt).Should().Be(
            SecurityAssetClassCatalog.GetStampFloor(assetClass),
            "economic-only fallback cannot certify the newer schedule-write contract");
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
        """{"schemaVersion":2,"termsFormat":"economic","maturity":{"effectiveDate":null,"issueDate":"2025-06-15","maturityDate":"2035-06-15"},"coupon":{"couponType":"Floating","couponRate":null,"paymentFrequency":"Quarterly","dayCount":"ACT/360"},"discount":null,"accrual":null,"payment":null,"redemption":null,"call":{"isCallable":true,"firstCallDate":"2030-06-15","callPrice":null,"callSchedule":[],"makeWholeSpreadBps":null,"isPuttable":false,"putSchedule":[]},"auction":null,"sweep":null,"financing":null,"issuer":{"issuerName":"Meridian Capital Corp","institutionName":null,"issuerProgram":null,"leiCode":null,"ultimateParentName":null,"issuerSector":null,"issuerCountry":null},"equityBehavior":null,"fund":null,"structuredProduct":null}""";

    private const string CertificateOfDepositEconomicTerms =
        """{"schemaVersion":2,"termsFormat":"economic","maturity":{"effectiveDate":null,"issueDate":null,"maturityDate":"2027-09-30"},"coupon":{"couponType":"Fixed","couponRate":4.4,"paymentFrequency":null,"dayCount":"ACT/365"},"discount":null,"accrual":null,"payment":null,"redemption":null,"call":{"isCallable":true,"firstCallDate":"2026-09-30","callPrice":null,"callSchedule":[],"makeWholeSpreadBps":null,"isPuttable":false,"putSchedule":[]},"auction":null,"sweep":null,"financing":null,"issuer":{"issuerName":"First Meridian Bank","institutionName":null,"issuerProgram":null,"leiCode":null,"ultimateParentName":null,"issuerSector":null,"issuerCountry":null},"equityBehavior":null,"fund":null,"structuredProduct":null}""";

    private const string StructuredCreditEconomicTerms =
        """{"schemaVersion":2,"termsFormat":"economic","maturity":{"effectiveDate":null,"issueDate":null,"maturityDate":"2031-06-15"},"coupon":{"couponType":"SOFR\u002B250","couponRate":null,"paymentFrequency":null,"dayCount":null},"discount":null,"accrual":null,"payment":null,"redemption":null,"call":null,"auction":null,"sweep":null,"financing":null,"issuer":{"issuerName":"MRDN-2026-1","institutionName":null,"issuerProgram":"CLO","leiCode":null,"ultimateParentName":null,"issuerSector":null,"issuerCountry":null},"equityBehavior":null,"fund":null,"structuredProduct":{"factor":0.8235,"factorDate":null,"weightedAvgCoupon":7.85,"weightedAvgMaturityMonths":58,"weightedAvgLoanAgeMos":14,"collateralType":"CLO","poolIdentifier":"MRDN-2026-1","trancheClass":"B","prepaymentAssumption":null,"averageLifeYears":null,"isInterestOnly":false,"isPrincipalOnly":false,"notionalBalance":10000000,"originator":null,"creditEnhancementPct":null,"factorScheduleReference":"See trustee report 2026-07","factorScheduleEntries":[{"asOfDate":"2026-06-01","factor":0.8412},{"asOfDate":"2026-07-01","factor":0.8235}]}}""";

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
    public void ReplayWithoutLegacyTerms_RefusesDeclaredAccrual_WhileRetainedFlatTermsRemainAuthoritative(string assetClass)
    {
        var twin = SecurityAssetTermsSchemaRoundTripTests.ReadableTwin(assetClass);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, twin));
        economic.EconomicTerms.GetProperty("accrual").ValueKind.Should().Be(JsonValueKind.Object,
            "the historical twin declares schedule accrual information");

        Action replay = () => SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });

        replay.Should().Throw<InvalidOperationException>().WithMessage("*Declared schedule economic replay requires retained legacy*");
        SecurityEconomicDefinitionAdapter.ToProjection(economic).AssetSpecificTerms.GetRawText().Should().Be(twin.GetRawText(),
            "the established retained-flat path preserves the complete declaration");
    }

    // ── Call and put schedules read ahead of write (ADR-024; the Call fill frozen by ADR-025) ────

    // The accrual each class's readable twin block fills (the frozen 4a rules), so a schedule twin's
    // economic terms differ from its class's constant above only in the accrual and call modules.
    private const string BondTwinAccrual =
        """{"accrualMethod":null,"accrualStartDate":null,"exDividendDays":null,"businessDayConvention":"ModifiedFollowing","holidayCalendar":"GBLO\u002BUSNY","dayCount":null}""";

    private const string CertificateOfDepositTwinAccrual =
        """{"accrualMethod":null,"accrualStartDate":null,"exDividendDays":null,"businessDayConvention":"Following","holidayCalendar":"USNY","dayCount":null}""";

    private const string StructuredCreditTwinAccrual =
        """{"accrualMethod":null,"accrualStartDate":null,"exDividendDays":null,"businessDayConvention":"ModifiedFollowing","holidayCalendar":"GBLO\u002BUSNY","dayCount":null}""";

    [Theory]
    [InlineData("Bond")]
    [InlineData("CertificateOfDeposit")]
    [InlineData("StructuredCredit")]
    public void UnrecoverableKeys_IncludeTheCallAndPutSchedules(string assetClass)
    {
        // The economic Call module keeps only each entry's date, a par price and a call type, so the
        // arrays as a whole are not recoverable on replay until a contract step maps them.
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.UnrecoverableKeys(assetClass).Should().Contain(["callSchedule", "putSchedule"]);
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.RecoverableKeys(assetClass)
            .Should().NotContain("callSchedule")
            .And.NotContain("putSchedule");
    }

    [Fact]
    public void ReadableBondTwinAtVersionThree_FillsTheEconomicCallModuleUnderTheFrozenRule()
    {
        // Calls sort by date. Only a Fixed price on a PercentOfPar basis (an absent type or basis
        // counts as each) is a call price, so neither the make-whole floor nor the accreted-value price
        // is; a carried price of exactly 100 is a par call; the call type is MakeWhole for a make-whole
        // entry and otherwise the exercise style. The scalars stay the bond's own: isCallable verbatim,
        // firstCallDate from callDate rather than the earliest entry, and no call price. Puts carry only
        // a date and a par price, and a declared put makes the bond puttable; mandatoryPutDate still
        // maps nowhere.
        var twin = ScheduleTwin(
            "Bond",
            """[{"callDate":"2030-06-15","callEndDate":"2031-06-14","exerciseStyle":"American","callPrice":100,"priceBasis":"PercentOfPar","priceType":"Fixed","partialRedemption":"WholeOnly","minimumNoticeDays":30,"maximumNoticeDays":60,"noticeDayType":"Calendar"},{"callDate":"2032-06-15","exerciseStyle":"European","callPrice":98.5,"priceBasis":"PercentOfAccretedValue"},{"callDate":"2027-06-15","callEndDate":"2030-06-14","exerciseStyle":"American","callPrice":100,"priceType":"MakeWhole","makeWholeSpreadBps":25},{"callDate":"2028-06-15","exerciseStyle":"Bermudan","exerciseFrequency":"SemiAnnual","callPrice":101.5}]""",
            """[{"putDate":"2031-06-15","exerciseStyle":"European","putPrice":100,"priceBasis":"PercentOfPar","minimumNoticeDays":15,"maximumNoticeDays":30,"noticeDayType":"Business"},{"putDate":"2033-06-15","putPrice":97,"priceBasis":"PercentOfAccretedValue"},{"putDate":"2029-12-15","putPrice":99.5}]""");
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(twin).Should().Be(3);
        twin.GetProperty("callDate").GetString().Should().Be("2030-06-15");
        twin.GetProperty("mandatoryPutDate").GetString().Should().Be("2031-06-15");

        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", twin));

        economic.EconomicTerms.GetRawText().Should().Be(
            """{"schemaVersion":2,"termsFormat":"economic","maturity":{"effectiveDate":null,"issueDate":"2025-06-15","maturityDate":"2035-06-15"},"coupon":{"couponType":"Floating","couponRate":null,"paymentFrequency":"Quarterly","dayCount":"ACT/360"},"discount":null,"accrual":{"accrualMethod":null,"accrualStartDate":null,"exDividendDays":null,"businessDayConvention":"ModifiedFollowing","holidayCalendar":"GBLO\u002BUSNY","dayCount":null},"payment":null,"redemption":null,"call":{"isCallable":true,"firstCallDate":"2030-06-15","callPrice":null,"callSchedule":[{"callDt":"2027-06-15","callPx":null,"isParCall":false,"callType":"MakeWhole"},{"callDt":"2028-06-15","callPx":101.5,"isParCall":false,"callType":"Bermudan"},{"callDt":"2030-06-15","callPx":100,"isParCall":true,"callType":"American"},{"callDt":"2032-06-15","callPx":null,"isParCall":false,"callType":"European"}],"makeWholeSpreadBps":25,"isPuttable":true,"putSchedule":[{"putDt":"2029-12-15","putPx":99.5},{"putDt":"2031-06-15","putPx":100},{"putDt":"2033-06-15","putPx":null}]},"auction":null,"sweep":null,"financing":null,"issuer":{"issuerName":"Meridian Capital Corp","institutionName":null,"issuerProgram":null,"leiCode":null,"ultimateParentName":null,"issuerSector":null,"issuerCountry":null},"equityBehavior":null,"fund":null,"structuredProduct":null}""");
    }

    [Theory]
    // A bond that is not callable stays not callable whatever calls it declares; an entry without an
    // exercise style has no call type.
    [InlineData(
        "Bond", """{"isCallable":false}""", """[{"callDate":"2030-06-15","callPrice":100}]""", "[]",
        """{"isCallable":false,"firstCallDate":"2030-06-15","callPrice":null,"callSchedule":[{"callDt":"2030-06-15","callPx":100,"isParCall":true,"callType":null}],"makeWholeSpreadBps":null,"isPuttable":false,"putSchedule":[]}""")]
    // Puts alone leave the call list empty and make the bond puttable.
    [InlineData(
        "Bond", null, "[]", """[{"putDate":"2031-06-15","putPrice":100,"priceBasis":"PercentOfPar"}]""",
        """{"isCallable":true,"firstCallDate":"2030-06-15","callPrice":null,"callSchedule":[],"makeWholeSpreadBps":null,"isPuttable":true,"putSchedule":[{"putDt":"2031-06-15","putPx":100}]}""")]
    // A certificate of deposit is callable only through its own callableDate, never through an entry.
    [InlineData(
        "CertificateOfDeposit", """{"callableDate":null}""", """[{"callDate":"2026-09-30","callEndDate":"2027-03-31","exerciseStyle":"American","callPrice":100,"minimumNoticeDays":10,"noticeDayType":"Calendar"}]""", "[]",
        """{"isCallable":false,"firstCallDate":null,"callPrice":null,"callSchedule":[{"callDt":"2026-09-30","callPx":100,"isParCall":true,"callType":"American"}],"makeWholeSpreadBps":null,"isPuttable":false,"putSchedule":[]}""")]
    // Structured credit has no scalar call terms: conventions alone, with empty or absent lists, add no
    // module.
    [InlineData("StructuredCredit", null, "[]", "[]", "null")]
    [InlineData("StructuredCredit", null, null, null, "null")]
    // A declared call makes it callable, with the lists, the puttable flag and the spread, but never a
    // first call date or price (nonCallPeriodEnd never feeds the module).
    [InlineData(
        "StructuredCredit", null, """[{"callDate":"2028-07-15","exerciseStyle":"Bermudan","exerciseFrequency":"Quarterly","callPrice":100,"priceBasis":"PercentOfPar","priceType":"Fixed","partialRedemption":"ProRata"},{"callDate":"2027-07-15","exerciseStyle":"European","callPrice":100,"priceType":"MakeWhole","makeWholeSpreadBps":50}]""", """[{"putDate":"2029-07-15","putPrice":100}]""",
        """{"isCallable":true,"firstCallDate":null,"callPrice":null,"callSchedule":[{"callDt":"2027-07-15","callPx":null,"isParCall":false,"callType":"MakeWhole"},{"callDt":"2028-07-15","callPx":100,"isParCall":true,"callType":"Bermudan"}],"makeWholeSpreadBps":50,"isPuttable":true,"putSchedule":[{"putDt":"2029-07-15","putPx":100}]}""")]
    // Puts alone make it puttable but not callable.
    [InlineData(
        "StructuredCredit", null, "[]", """[{"putDate":"2029-07-15","exerciseStyle":"European"}]""",
        """{"isCallable":false,"firstCallDate":null,"callPrice":null,"callSchedule":[],"makeWholeSpreadBps":null,"isPuttable":true,"putSchedule":[{"putDt":"2029-07-15","putPx":null}]}""")]
    // An exercise style outside the vocabulary is the call type verbatim.
    [InlineData(
        "Bond", null, """[{"callDate":"2030-06-15","exerciseStyle":"american","callPrice":101}]""", "[]",
        """{"isCallable":true,"firstCallDate":"2030-06-15","callPrice":null,"callSchedule":[{"callDt":"2030-06-15","callPx":101,"isParCall":false,"callType":"american"}],"makeWholeSpreadBps":null,"isPuttable":false,"putSchedule":[]}""")]
    // The spread comes from the first make-whole entry, after sorting, that carries one: a make-whole
    // entry without a spread is passed over, and a Fixed entry's spread never counts.
    [InlineData(
        "Bond", null, """[{"callDate":"2029-06-15","priceType":"MakeWhole","makeWholeSpreadBps":35},{"callDate":"2027-06-15","priceType":"MakeWhole","makeWholeSpreadBps":20},{"callDate":"2026-06-15","priceType":"MakeWhole"},{"callDate":"2025-12-15","priceType":"Fixed","callPrice":102,"makeWholeSpreadBps":99}]""", "[]",
        """{"isCallable":true,"firstCallDate":"2030-06-15","callPrice":null,"callSchedule":[{"callDt":"2025-12-15","callPx":102,"isParCall":false,"callType":null},{"callDt":"2026-06-15","callPx":null,"isParCall":false,"callType":"MakeWhole"},{"callDt":"2027-06-15","callPx":null,"isParCall":false,"callType":"MakeWhole"},{"callDt":"2029-06-15","callPx":null,"isParCall":false,"callType":"MakeWhole"}],"makeWholeSpreadBps":20,"isPuttable":false,"putSchedule":[]}""")]
    // Every comparison is ordinal: a case-variant price type or basis is neither MakeWhole nor a Fixed
    // price on a PercentOfPar basis, so its entry carries no price, keeps its exercise style as the
    // call type and adds no spread, and a case-variant put basis carries no put price.
    [InlineData(
        "Bond", null, """[{"callDate":"2029-06-15","exerciseStyle":"European","callPrice":100,"priceType":"makeWhole","makeWholeSpreadBps":30},{"callDate":"2030-06-15","callPrice":100,"priceType":"fixed"},{"callDate":"2031-06-15","exerciseStyle":"American","callPrice":100,"priceBasis":"percentOfPar"}]""", """[{"putDate":"2032-06-15","putPrice":100,"priceBasis":"percentOfPar"}]""",
        """{"isCallable":true,"firstCallDate":"2030-06-15","callPrice":null,"callSchedule":[{"callDt":"2029-06-15","callPx":null,"isParCall":false,"callType":"European"},{"callDt":"2030-06-15","callPx":null,"isParCall":false,"callType":null},{"callDt":"2031-06-15","callPx":null,"isParCall":false,"callType":"American"}],"makeWholeSpreadBps":null,"isPuttable":true,"putSchedule":[{"putDt":"2032-06-15","putPx":null}]}""")]
    // The sort is stable: entries on one date keep their stored order (no tie-break by price or call
    // type), and of two make-whole spreads on one date the first stored wins, not the smaller.
    [InlineData(
        "Bond", null, """[{"callDate":"2027-06-15","exerciseStyle":"European","callPrice":101,"priceType":"Fixed"},{"callDate":"2027-06-15","priceType":"MakeWhole","makeWholeSpreadBps":30},{"callDate":"2026-06-15","exerciseStyle":"Bermudan","callPrice":102},{"callDate":"2027-06-15","priceType":"MakeWhole","makeWholeSpreadBps":15},{"callDate":"2027-06-15","exerciseStyle":"American","callPrice":100}]""", """[{"putDate":"2031-06-15","putPrice":101},{"putDate":"2031-06-15","putPrice":99}]""",
        """{"isCallable":true,"firstCallDate":"2030-06-15","callPrice":null,"callSchedule":[{"callDt":"2026-06-15","callPx":102,"isParCall":false,"callType":"Bermudan"},{"callDt":"2027-06-15","callPx":101,"isParCall":false,"callType":"European"},{"callDt":"2027-06-15","callPx":null,"isParCall":false,"callType":"MakeWhole"},{"callDt":"2027-06-15","callPx":null,"isParCall":false,"callType":"MakeWhole"},{"callDt":"2027-06-15","callPx":100,"isParCall":true,"callType":"American"}],"makeWholeSpreadBps":30,"isPuttable":true,"putSchedule":[{"putDt":"2031-06-15","putPx":101},{"putDt":"2031-06-15","putPx":99}]}""")]
    // Empty or absent lists on a bond or certificate of deposit stamped with the schedules' version
    // leave the call module, and so the whole document, exactly as the class constants above (4a).
    [InlineData(
        "Bond", null, "[]", "[]",
        """{"isCallable":true,"firstCallDate":"2030-06-15","callPrice":null,"callSchedule":[],"makeWholeSpreadBps":null,"isPuttable":false,"putSchedule":[]}""")]
    [InlineData(
        "Bond", null, null, null,
        """{"isCallable":true,"firstCallDate":"2030-06-15","callPrice":null,"callSchedule":[],"makeWholeSpreadBps":null,"isPuttable":false,"putSchedule":[]}""")]
    [InlineData(
        "CertificateOfDeposit", null, "[]", "[]",
        """{"isCallable":true,"firstCallDate":"2026-09-30","callPrice":null,"callSchedule":[],"makeWholeSpreadBps":null,"isPuttable":false,"putSchedule":[]}""")]
    public void DeclaredSchedules_ChangeOnlyTheEconomicCallModule_UnderTheFrozenRule(
        string assetClass,
        string? scalarEdits,
        string? callSchedule,
        string? putSchedule,
        string expectedCall)
    {
        var twin = ScheduleTwin(assetClass, callSchedule, putSchedule, scalarEdits);
        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(twin).Should().Be(SchedulesSince(assetClass));

        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, twin));

        economic.EconomicTerms.GetRawText().Should().Be(WithCall(EconomicTermsOfTheScheduleTwin(assetClass), expectedCall));
    }

    [Fact]
    public void RetainedEconomicTerms_FillAnEmptyDeclaredList_WhileADeclaredNonEmptyListWins()
    {
        // A projection rebuilt from an economic-only event carries the original economic document. The
        // declared calls replace its call list; the bond declares no puts, so the retained put list still
        // fills the empty one (SecurityTermsEnvelope.FoldRetainedEconomicTerms).
        var terms = JsonNode.Parse(ScheduleTwin(
            "Bond",
            """[{"callDate":"2030-06-15","exerciseStyle":"American","callPrice":100}]""",
            "[]").GetRawText())!.AsObject();
        terms[SecurityTermsEnvelope.RetainedEconomicTermsKey] = JsonNode.Parse(
            """{"schemaVersion":2,"termsFormat":"economic","call":{"isCallable":true,"firstCallDate":"2026-01-15","callPrice":null,"callSchedule":[{"callDt":"2026-01-15","callPx":101,"isParCall":false,"callType":"European"}],"makeWholeSpreadBps":null,"isPuttable":true,"putSchedule":[{"putDt":"2028-06-15","putPx":100}]}}""");

        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", ToElement(terms)));

        var call = economic.EconomicTerms.GetProperty("call");
        call.GetProperty("callSchedule").GetRawText().Should().Be(
            """[{"callDt":"2030-06-15","callPx":100,"isParCall":true,"callType":"American"}]""",
            "a declared, non-empty list replaces the retained one");
        call.GetProperty("putSchedule").GetRawText().Should().Be(
            """[{"putDt":"2028-06-15","putPx":100}]""",
            "an empty declared list is still filled from the retained document");
    }

    [Theory]
    [InlineData("2032-12-15")]
    [InlineData(null)]
    public void ReplayWithoutLegacyTerms_RefusesDeclaredExerciseLists_RegardlessOfScalarCallDate(string? callDate)
    {
        var twin = ScheduleTwin(
            "Bond",
            """[{"callDate":"2031-06-15","callPrice":100},{"callDate":"2027-06-15","priceType":"MakeWhole","makeWholeSpreadBps":25}]""",
            """[{"putDate":"2029-12-15","putPrice":100}]""",
            JsonSerializer.Serialize(new { callDate }));
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", twin));
        var call = economic.EconomicTerms.GetProperty("call");
        call.GetProperty("firstCallDate").GetString().Should().Be(callDate,
            "declared exercise dates never replace the bond's own scalar call date");
        call.GetProperty("callSchedule").GetArrayLength().Should().Be(2);
        call.GetProperty("putSchedule").GetArrayLength().Should().Be(1);

        Action replay = () => SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null });

        replay.Should().Throw<InvalidOperationException>().WithMessage("*Declared schedule economic replay requires retained legacy*");
        SecurityEconomicDefinitionAdapter.ToProjection(economic).AssetSpecificTerms.GetRawText().Should().Be(twin.GetRawText());
    }

    [Theory]
    [InlineData("2032-12-15")]
    [InlineData(null)]
    public void OrdinaryBondReplay_RecoversItsScalarCallDateAtTheHistoricalStampFloor(string? callDate)
    {
        var terms = JsonNode.Parse(SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            "Bond", SecurityAssetTermsSchemaRoundTripTests.FullPayloads["Bond"]).GetRawText())!.AsObject();
        SetOrRemove(terms, "callDate", callDate is null ? null : JsonValue.Create(callDate));
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Bond", ToElement(terms)));

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(economic with { LegacyAssetSpecificTerms = null }).AssetSpecificTerms;

        SecurityTermsEnvelope.ResolveAssetClassTermsVersion(rebuilt).Should().Be(SecurityAssetClassCatalog.GetStampFloor("Bond"));
        rebuilt.GetProperty("isCallable").GetBoolean().Should().BeTrue();
        if (callDate is null)
            rebuilt.TryGetProperty("callDate", out _).Should().BeFalse("no declared exercise date substitutes for a missing scalar");
        else
            rebuilt.GetProperty("callDate").GetString().Should().Be(callDate);
        rebuilt.EnumerateObject().Select(static property => property.Name).Should().NotContain(name =>
            name.Equals("scheduleConventions", StringComparison.OrdinalIgnoreCase)
            || name.Equals("callSchedule", StringComparison.OrdinalIgnoreCase)
            || name.Equals("putSchedule", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The readable twin of <paramref name="assetClass"/> at the version that introduced its call and
    /// put schedules (<see cref="SchedulesSince"/>), carrying exactly the given arrays. A null array
    /// leaves its key absent; each member of <paramref name="scalarEdits"/> replaces that key, and a
    /// null member removes it.
    /// </summary>
    private static JsonElement ScheduleTwin(string assetClass, string? callSchedule, string? putSchedule, string? scalarEdits = null)
    {
        var terms = JsonNode.Parse(
            SecurityAssetTermsSchemaRoundTripTests.ReadableTwin(assetClass, SchedulesSince(assetClass)).GetRawText())!.AsObject();
        SetOrRemove(terms, "callSchedule", callSchedule is null ? null : JsonNode.Parse(callSchedule));
        SetOrRemove(terms, "putSchedule", putSchedule is null ? null : JsonNode.Parse(putSchedule));
        if (scalarEdits is not null)
        {
            foreach (var (key, value) in JsonNode.Parse(scalarEdits)!.AsObject())
            {
                SetOrRemove(terms, key, value?.DeepClone());
            }
        }

        return ToElement(terms);
    }

    private static void SetOrRemove(JsonObject terms, string key, JsonNode? value)
    {
        if (value is null)
        {
            terms.Remove(key);
        }
        else
        {
            terms[key] = value;
        }
    }

    private static int SchedulesSince(string assetClass)
        => SecurityAssetTermsSchema.Field(assetClass, "callSchedule")!.SinceTermsVersion;

    private static string EconomicTermsOfTheScheduleTwin(string assetClass)
        => assetClass switch
        {
            "Bond" => WithAccrual(BondEconomicTerms, BondTwinAccrual),
            "CertificateOfDeposit" => WithAccrual(CertificateOfDepositEconomicTerms, CertificateOfDepositTwinAccrual),
            "StructuredCredit" => WithAccrual(StructuredCreditEconomicTerms, StructuredCreditTwinAccrual),
            _ => throw new ArgumentOutOfRangeException(nameof(assetClass), assetClass, "Only bonds, CDs and structured credit declare call and put schedules.")
        };

    // Replaces the call module of an economic terms document: the text from its "call" key up to the
    // next module, "auction".
    private static string WithCall(string economicTerms, string call)
    {
        const string CallKey = "\"call\":";
        const string NextModule = ",\"auction\":";
        var start = economicTerms.IndexOf(CallKey, StringComparison.Ordinal);
        var end = economicTerms.IndexOf(NextModule, start, StringComparison.Ordinal);
        return economicTerms[..start] + CallKey + call + economicTerms[end..];
    }

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private static JsonElement BondTwin(string block)
        => SecurityAssetTermsSchemaRoundTripTests.ReadableTwin("Bond", SecurityAssetTermsSchemaRoundTripTests.FullPayloads["Bond"], [block]);

    private static string WithAccrual(string economicTermsWithoutBlock, string? accrual)
        => accrual is null
            ? economicTermsWithoutBlock
            : economicTermsWithoutBlock.Replace(NullAccrual, $"\"accrual\":{accrual}", StringComparison.Ordinal);
}
