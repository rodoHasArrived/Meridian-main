using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Characterizes the historical READ refusal order of required flat fields: when several required
/// keys are absent from a stored row, <see cref="SecurityMasterMapping.ToRecord"/> must name the
/// same first missing key the pre-contraction codec named. The expected table below is a literal
/// copy of the hand-written order the mapping enforced before the order moved into
/// <see cref="SecurityAssetTermsSchema"/>; it is deliberately NOT derived from the schema, so a
/// reordered declaration fails here instead of silently choosing a new first refusal.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterRequiredReadOrderTests
{
    /// <summary>Which document the class reads its required keys from.</summary>
    public enum ReadView
    {
        /// <summary>The stored terms document itself.</summary>
        Root,

        /// <summary>The profile-resolved sub-document (<c>profileFields</c>) when present, else the root.</summary>
        ProfileResolved
    }

    /// <summary>The historical required-read order, per class, copied literally from the retired switch.</summary>
    private static readonly IReadOnlyDictionary<string, (ReadView View, string[] Keys)> ExpectedOrder =
        new Dictionary<string, (ReadView, string[])>(StringComparer.Ordinal)
        {
            ["Option"] = (ReadView.Root, ["underlyingId", "putCall", "strike", "expiry", "multiplier"]),
            ["Future"] = (ReadView.Root, ["rootSymbol", "contractMonth", "expiry", "multiplier"]),
            ["Bond"] = (ReadView.Root, ["maturity"]),
            ["FxSpot"] = (ReadView.Root, ["baseCurrency", "quoteCurrency"]),
            ["Deposit"] = (ReadView.Root, ["depositType", "institutionName"]),
            ["CertificateOfDeposit"] = (ReadView.Root, ["issuerName", "maturity"]),
            ["CommercialPaper"] = (ReadView.Root, ["issuerName", "maturity"]),
            ["TreasuryBill"] = (ReadView.Root, ["maturity"]),
            ["Repo"] = (ReadView.Root, ["counterparty", "startDate", "endDate"]),
            ["CashSweep"] = (ReadView.Root, ["programName", "sweepVehicleType"]),
            ["OtherSecurity"] = (ReadView.Root, ["category"]),
            ["Swap"] = (ReadView.Root, ["effectiveDate", "maturityDate"]),
            ["DirectLoan"] = (ReadView.Root, ["borrower"]),
            ["StructuredCredit"] = (ReadView.ProfileResolved, ["tranche", "collateralType", "originalFace", "couponOrIndex"]),
            ["PrivateFundInterest"] = (ReadView.ProfileResolved, ["gpSponsor", "strategy", "vintage", "commitment", "navDate"]),
            ["PrivateCompanyEquity"] = (ReadView.ProfileResolved, ["issuer", "shareClass", "round", "costBasis"]),
            ["RealEstateHolding"] = (ReadView.ProfileResolved, ["propertyType", "addressOrMarket", "ownershipPercent", "appraisalValue", "valuationDate"]),
            ["CommitmentGuarantee"] = (ReadView.ProfileResolved, ["counterparty", "committedAmount", "effectiveDate"]),
            ["Commodity"] = (ReadView.Root, ["commodityType"]),
            ["CryptoCurrency"] = (ReadView.Root, ["baseCurrency", "quoteCurrency"]),
            ["Cfd"] = (ReadView.Root, ["underlyingAssetClass"]),
            ["Warrant"] = (ReadView.Root, ["underlyingId", "warrantType"]),
            ["FxForward"] = (ReadView.Root, ["baseCurrency", "quoteCurrency", "valueDate"]),
            ["NonDeliverableForward"] = (ReadView.Root, ["baseCurrency", "quoteCurrency", "settlementCurrency", "fixingDate", "valueDate"]),
            ["CreditDefaultSwap"] = (ReadView.Root, ["referenceEntity", "protectionSide", "notional", "fixedCouponBps", "effectiveDate", "maturityDate"]),
            ["Swaption"] = (ReadView.Root, ["payerReceiver", "strikeRate", "expiry", "underlyingMaturityDate", "notional"]),
            ["CapFloor"] = (ReadView.Root, ["capFloorType", "referenceIndex", "notional", "effectiveDate", "maturityDate"]),
            ["StructuredNote"] = (ReadView.Root, ["issuerName", "underlyingDescription", "maturity"])
        };

    /// <summary>
    /// Required fields whose absence a READ absorbs instead of refusing; each must still reconstruct.
    /// </summary>
    private static readonly (string AssetClass, string Key)[] ReadsAbsentRequiredFields =
    [
        ("Option", "isAdjusted"),
        ("Future", "isRollTarget"),
        ("Bond", "isCallable"),
        ("Bond", "subclass"),
        ("Deposit", "isCallable"),
        ("MoneyMarketFund", "sweepEligible"),
        ("MoneyMarketFund", "liquidityFeeEligible"),
        ("CommercialPaper", "isAssetBacked")
    ];

    public static TheoryData<string, int> RefusalCases()
    {
        var data = new TheoryData<string, int>();
        foreach (var (assetClass, (_, keys)) in ExpectedOrder)
        {
            for (var i = 0; i < keys.Length; i++)
            {
                data.Add(assetClass, i);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RefusalCases))]
    public void StoredRowMissingRequiredKeysFromI_RefusesNamingKeyI(string assetClass, int firstMissing)
    {
        var (view, keys) = ExpectedOrder[assetClass];
        var stripped = keys[firstMissing..];

        foreach (var shape in Shapes(assetClass, view, stripped))
        {
            var read = () => SecurityMasterMapping.ToRecord(
                SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, shape.Terms));

            var refusal = read.Should().Throw<Exception>(
                $"{assetClass} ({shape.Name}) is missing required keys {string.Join(", ", stripped)}").Which;
            MessageChain(refusal).Should().Contain($"'{keys[firstMissing]}'",
                $"the historical read order names '{keys[firstMissing]}' first for {assetClass} ({shape.Name})");
        }
    }

    [Fact]
    public void ExpectedOrder_CoversEveryClassThatRefusesAbsenceOnRead()
    {
        // Classes outside the table read no required scalar key on reconstruction: Equity,
        // MoneyMarketFund, InvestmentFund and the envelope-carrying CustomAsset.
        var uncovered = SecurityAssetTermsSchemaRoundTripTests.FullPayloads.Keys
            .Where(assetClass => !ExpectedOrder.ContainsKey(assetClass))
            .OrderBy(static assetClass => assetClass, StringComparer.Ordinal)
            .ToArray();

        uncovered.Should().Equal(["CustomAsset", "Equity", "InvestmentFund", "MoneyMarketFund"]);
    }

    public static TheoryData<string, string> AbsorbedAbsenceCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (assetClass, key) in ReadsAbsentRequiredFields)
        {
            data.Add(assetClass, key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AbsorbedAbsenceCases))]
    public void StoredRowMissingAnAbsorbedRequiredField_StillReads(string assetClass, string key)
    {
        var terms = Twin(assetClass);
        terms.Remove(key).Should().BeTrue($"the {assetClass} twin must carry '{key}' for the absence to be meaningful");

        var read = () => SecurityMasterMapping.ToRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, ToElement(terms)));

        read.Should().NotThrow($"a stored {assetClass} row without '{key}' reads it as its absence default");
    }

    [Fact]
    public void StoredMoneyMarketFundWithoutEitherBoolean_StillReads()
    {
        var terms = Twin("MoneyMarketFund");
        terms.Remove("sweepEligible");
        terms.Remove("liquidityFeeEligible");

        var read = () => SecurityMasterMapping.ToRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("MoneyMarketFund", ToElement(terms)));

        read.Should().NotThrow();
    }

    [Fact]
    public void StoredCustomAssetWithoutAnEnvelope_SalvagesToOtherSecurity()
    {
        var terms = JsonSerializer.SerializeToElement(new
        {
            category = "Litigation Finance",
            bespokeField = "unmodeled"
        });

        var record = SecurityMasterMapping.ToRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("CustomAsset", terms));

        record.Kind.IsOtherSecurity.Should().BeTrue("an envelope-less CustomAsset row degrades to OtherSecurity on read");
    }

    // ── Ratchets: the schema declares the order ─────────────────────────────────────────────────

    [Fact]
    public void SchemaDeclaredReadOrder_MatchesTheHistoricalTable()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            var declared = SecurityAssetTermsSchema.ReadRequiredScalarFields(assetClass)
                .Select(static field => field.Key)
                .ToArray();

            if (ExpectedOrder.TryGetValue(assetClass, out var expected))
            {
                declared.Should().Equal(expected.Keys,
                    $"the schema's declaration order is {assetClass}'s read refusal order, which replay depends on");
                SecurityAssetTermsSchema.ReadsProfileResolvedTerms(assetClass).Should().Be(
                    expected.View == ReadView.ProfileResolved,
                    $"{assetClass} must read its required keys from the same document it always has");
            }
            else
            {
                declared.Should().BeEmpty($"{assetClass} has never refused a read for a missing scalar key");
            }
        }
    }

    [Fact]
    public void ReadRequiredScalarFields_UseOnlyTypesTheCodecHasRequiredReadersFor()
    {
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            foreach (var field in SecurityAssetTermsSchema.ReadRequiredScalarFields(assetClass))
            {
                field.Type.Should().BeOneOf(
                    [
                        SecurityAssetTermFieldType.String,
                        SecurityAssetTermFieldType.Decimal,
                        SecurityAssetTermFieldType.Integer,
                        SecurityAssetTermFieldType.Date,
                        SecurityAssetTermFieldType.Guid
                    ],
                    $"{assetClass}.{field.Key} refuses absence on read, so the codec must have a required reader for its type");
            }
        }
    }

    [Fact]
    public void RequiredFieldIntroducedAboveVersionOne_DeclaresAnAbsenceDefault()
    {
        // Rows stamped before the introducing version never carry the key, so a required field
        // added later that refused absence would make every older row unreadable.
        var offenders = SecurityAssetTermsSchema.AssetClasses
            .SelectMany(static assetClass => SecurityAssetTermsSchema.Fields(assetClass)
                .Where(static field => field.Required && field.SinceTermsVersion > 1 && field.AbsentReadDefault is null)
                .Select(field => $"{assetClass}.{field.Key} (since v{field.SinceTermsVersion})"))
            .ToArray();

        offenders.Should().BeEmpty("a Req(...).Since(n > 1) field must declare ReadsAbsentAs(...)");
    }

    [Fact]
    public void AbsentReadDefaults_AreExactlyTheAbsorbedFields()
    {
        var declared = SecurityAssetTermsSchema.AssetClasses
            .SelectMany(static assetClass => SecurityAssetTermsSchema.Fields(assetClass)
                .Where(static field => field.AbsentReadDefault is not null)
                .Select(field => (assetClass, field.Key)))
            .ToArray();

        declared.Should().BeEquivalentTo(
            ReadsAbsentRequiredFields.Concat([("CustomAsset", "customProfileId"), ("CustomAsset", "profileVersion")]),
            "every declared absence default must be one a read is shown to absorb, and vice versa");
        declared.Should().OnlyContain(
            entry => SecurityAssetTermsSchema.Field(entry.assetClass, entry.Key)!.Required,
            "an absence default only means something on a required field");
    }

    [Fact]
    public void ReadsProfileResolvedTerms_AgreesWithTheGeneratedCodecBindings()
    {
        foreach (var binding in SecurityAssetTermsCodecBindings.Classes)
        {
            SecurityAssetTermsSchema.ReadsProfileResolvedTerms(binding.AssetClass).Should().Be(binding.ReadsResolvedTerms,
                $"the generated {binding.AssetClass} decode and the required-read check must read the same document");
        }
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<(string Name, JsonElement Terms)> Shapes(string assetClass, ReadView view, string[] stripped)
    {
        var flat = Twin(assetClass);
        foreach (var key in stripped)
        {
            flat.Remove(key).Should().BeTrue($"the {assetClass} twin must carry '{key}' for the absence to be meaningful");
        }

        yield return ("flat", ToElement(flat));

        if (view == ReadView.ProfileResolved)
        {
            // The class reads its required keys from the profile-resolved sub-document: a root that
            // still carries every key does not satisfy a profileFields bag that lacks them.
            var enveloped = Twin(assetClass);
            enveloped["profileFields"] = JsonNode.Parse(flat.ToJsonString());
            yield return ("profileFields", ToElement(enveloped));
        }
    }

    private static JsonObject Twin(string assetClass)
        => JsonNode.Parse(SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            assetClass, SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]).GetRawText())!.AsObject();

    private static JsonElement ToElement(JsonObject terms)
        => JsonDocument.Parse(terms.ToJsonString()).RootElement.Clone();

    private static string MessageChain(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }
}
