using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Reflection;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// ADR-027 S3-E contracts a group only after the group's fields have a canonical module home.
/// Each group separately proves economic-event replay recovers exactly the same flat codec bytes;
/// no fixture or stored event is rewritten to accommodate the record's implementation shape.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityCodecRecordContractionTests
{
    [Fact]
    public void EveryKindPayload_StoresOnlyRegisteredEconomicModules()
    {
        static Type HomeType(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Microsoft.FSharp.Core.FSharpOption<>)
                ? type.GetGenericArguments()[0] : type;

        var homes = FSharpType.GetRecordFields(typeof(SecurityTermModules), null)
            .Select(field => HomeType(field.PropertyType)).ToHashSet();
        var kinds = FSharpType.GetUnionCases(typeof(SecurityKind), null);
        foreach (var kind in kinds)
        {
            var payload = kind.GetFields().Should().ContainSingle().Which.PropertyType;
            FSharpType.IsRecord(payload, null).Should().BeTrue($"{kind.Name} has a codec record");
            if (homes.Contains(payload))
                continue; // A kind may use its canonical module directly through a type alias.
            foreach (var field in FSharpType.GetRecordFields(payload, null))
                homes.Should().Contain(HomeType(field.PropertyType),
                    $"{kind.Name}.{field.Name} must use a registered canonical module instead of per-class storage");
        }
    }

    public static TheoryData<string, string[]> ReplayGroups => new()
    {
        { "exercise", ["Option", "Future", "Warrant", "Swaption", "CapFloor"] },
        { "currency pairs", ["FxSpot", "CryptoCurrency", "FxForward", "NonDeliverableForward"] },
        { "notionals", ["FxForward", "NonDeliverableForward", "CreditDefaultSwap", "Swaption", "CapFloor", "StructuredNote", "PrivateFundInterest", "CommitmentGuarantee"] },
        { "profile envelope", ["CustomAsset"] },
        { "credit protection", ["CreditDefaultSwap"] },
        { "structured payoff", ["StructuredNote"] },
        { "private valuations", ["PrivateFundInterest", "PrivateCompanyEquity", "RealEstateHolding"] },
        { "listing detail", ["Option", "Future"] },
        { "structured pools", ["StructuredCredit"] },
        { "tranche contracts", ["StructuredCredit"] },
        { "equity capital", ["Equity", "PrivateCompanyEquity"] },
        { "issuer references", ["Bond", "Deposit", "CertificateOfDeposit", "CommercialPaper", "TreasuryBill", "OtherSecurity", "DirectLoan", "PrivateFundInterest", "PrivateCompanyEquity", "RealEstateHolding", "CommitmentGuarantee", "StructuredNote"] },
        { "financing", ["Repo"] },
        { "sweeps", ["CashSweep"] },
        { "funds", ["MoneyMarketFund", "InvestmentFund"] },
        { "coupon structures", ["Bond"] },
        { "principal schedules", ["Bond", "DirectLoan"] },
        { "swap legs", ["Swap"] },
        { "covenant entries", ["DirectLoan", "CommitmentGuarantee"] }
    };

    [Theory]
    [MemberData(nameof(ReplayGroups))]
    public void EachFieldGroup_PreservesImageBytesAndEconomicOnlyFieldValues(string group, string[] classes)
    {
        foreach (var assetClass in classes)
        {
            var before = SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
                assetClass, SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);
            var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, before);
            var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
            var withImage = SecurityEconomicDefinitionAdapter.ToProjection(economic);
            withImage.AssetSpecificTerms.GetRawText().Should().Be(before.GetRawText(),
                $"{group} contraction must preserve {assetClass}'s retained flat image bytes");
            var replayed = SecurityEconomicDefinitionAdapter.ToProjection(
                economic with { LegacyAssetSpecificTerms = null });
            // Assert this group's recovered fields directly. A separate group may still own a
            // required source field (for example Bond.par or a swaption's underlying maturity),
            // so an economic-only fragment is not necessarily a complete writable class record.
            var after = replayed.AssetSpecificTerms;

            foreach (var field in ContractedFields(group).Where(field => before.TryGetProperty(field, out _)))
            {
                var present = after.TryGetProperty(field, out var recovered);
                var expected = before.GetProperty(field);
                if (expected.ValueKind == JsonValueKind.Null)
                {
                    (!present || recovered.ValueKind == JsonValueKind.Null).Should().BeTrue(
                        $"{group} must preserve the undeclared {assetClass}.{field}");
                    continue;
                }
                present.Should().BeTrue($"{group} must recover {assetClass}.{field} from its canonical module");
                JsonElement.DeepEquals(recovered, expected).Should().BeTrue(
                    $"{group} contraction must preserve {assetClass}.{field} on economic-only replay");
            }
        }
    }

    [Theory]
    [InlineData("Option", "underlyingId")]
    [InlineData("Warrant", "underlyingId")]
    [InlineData("Future", "rootSymbol")]
    [InlineData("CertificateOfDeposit", "issuerName")]
    [InlineData("StructuredCredit", "tranche")]
    [InlineData("PrivateFundInterest", "gpSponsor")]
    [InlineData("RealEstateHolding", "propertyType")]
    [InlineData("FxForward", "baseCurrency")]
    public void NestedModules_PreserveTheFirstHistoricalMissingFieldRefusal(string assetClass, string field)
    {
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(
            assetClass, JsonSerializer.SerializeToElement(new { schemaVersion = 1 }));
        Action decode = () => SecurityMasterMapping.ToRecord(projection);

        decode.Should().Throw<InvalidOperationException>().WithMessage($"*'{field}'*");
    }

    private static string[] ContractedFields(string group) => group switch
    {
        "exercise" => ["underlyingId", "putCall", "strike", "strikeRate", "payerReceiver", "exerciseStyle", "warrantType", "capFloorType", "capStrike", "floorStrike", "premiumAmount", "multiplier"],
        "currency pairs" => ["baseCurrency", "quoteCurrency", "settlementCurrency", "forwardRate"],
        "notionals" => ["notional", "denomination", "commitment", "committedAmount", "fundedAmount", "unfundedAmount"],
        "profile envelope" => ["customProfileId", "profileVersion", "profileFields"],
        "credit protection" => ["referenceEntity", "referenceObligation", "protectionSide", "seniority", "restructuringClause", "upfrontPoints", "recoveryRateAssumption", "indexName"],
        "structured payoff" => ["underlyingDescription", "principalProtectionPct", "participationRate", "returnCap", "barrierLevel"],
        "private valuations" => ["vintage", "navDate", "lockup", "ownershipPercent", "costBasis", "latestValuation", "transferRestrictions", "addressOrMarket", "appraisalValue", "valuationDate", "debtStack"],
        "listing detail" => ["optChainId", "settlementType", "isAdjusted", "lastTradingDt", "rootSymbol", "contractMonth", "firstNoticeDt", "deliveryMonthDt", "deliveryLocationCode", "isRollTarget", "rollWindowDays"],
        "equity capital" => ["shareClass", "votingRightsCat", "classification", "otherClassification", "preferredTerms", "convertibleTerms"],
        "tranche contracts" => ["couponOrIndex", "structuredCreditKind", "seniority", "attachmentPoint", "detachmentPoint", "reinvestmentPeriodEnd", "nonCallPeriodEnd"],
        "structured pools" => ["tranche", "poolId", "collateralType", "originalFace", "currentFactor", "factorSchedule", "factorScheduleEntries", "weightedAverageCoupon", "weightedAverageMaturityMonths", "weightedAverageLoanAgeMonths"],
        "issuer references" => ["issuerName", "institutionName", "cusip", "borrower", "gpSponsor", "strategy", "issuer", "round", "sponsor", "counterparty", "beneficiary"],
        "financing" => ["counterparty", "startDate", "endDate", "collateralType", "haircut"],
        "sweeps" => ["programName", "sweepVehicleType", "sweepFrequency", "targetAccountType"],
        "funds" => ["fundFamily", "weightedAverageMaturityDays", "sweepEligible", "liquidityFeeEligible"],
        "coupon structures" => ["couponType", "couponRate", "floatingIndex", "spreadBps", "capRate", "floorRate", "stepSchedule", "inflationIndex", "inflationBaseIndexValue", "inflationIndexRatio", "dayCount"],
        "principal schedules" => ["principalSchedule"],
        "swap legs" => ["legs"],
        "covenant entries" => ["covenants"],
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null)
    };

    [Theory]
    [InlineData(typeof(OptionTerms), "UnderlyingId", "PutCall", "Strike", "Multiplier", "ExerciseStyle", "OptChainId", "SettlementType", "IsAdjusted", "LastTradingDt")]
    [InlineData(typeof(FutureTerms), "Multiplier", "RootSymbol", "ContractMonth", "LastTradingDt", "FirstNoticeDt", "DeliveryMonthDt", "SettlementType", "DeliveryLocationCode", "IsRollTarget", "RollWindowDays")]
    [InlineData(typeof(FxSpotTerms), "BaseCurrency", "QuoteCurrency")]
    [InlineData(typeof(CryptoTerms), "BaseCurrency", "QuoteCurrency")]
    [InlineData(typeof(WarrantTerms), "UnderlyingId", "WarrantType", "Strike", "Multiplier")]
    [InlineData(typeof(CreditDefaultSwapTerms), "ReferenceEntity", "ProtectionSide", "RecoveryRateAssumption")]
    [InlineData(typeof(StructuredNoteTerms), "UnderlyingDescription", "Denomination", "PrincipalProtectionPct", "ParticipationRate", "ReturnCap", "BarrierLevel")]
    [InlineData(typeof(StructuredCreditTerms), "Tranche", "PoolId", "CollateralType", "OriginalFace", "CurrentFactor", "FactorSchedule", "FactorScheduleEntries", "WeightedAverageCoupon", "WeightedAverageMaturityMonths", "WeightedAverageLoanAgeMonths")]
    [InlineData(typeof(PrivateCompanyEquityTerms), "OwnershipPercent", "CostBasis", "LatestValuation", "TransferRestrictions")]
    public void ContractedRecords_HaveNoDuplicateScalarStorage(Type recordType, params string[] removedFields)
    {
        FSharpType.GetRecordFields(recordType, null).Select(static property => property.Name)
            .Should().NotIntersectWith(removedFields,
                "a module home must replace duplicate per-class storage, rather than add another copy");
    }
}
