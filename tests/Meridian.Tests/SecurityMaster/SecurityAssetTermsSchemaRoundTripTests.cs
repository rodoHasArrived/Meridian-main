using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Schema-driven codec guard: for every asset class declared in <see cref="SecurityAssetTermsSchema"/>,
/// a fully-populated payload must survive the full codec loop — C# deserializer → F# domain record →
/// F# serializer → C# deserializer → F# serializer — byte-stable, and the serialized field set must
/// equal the declared schema field set exactly.
///
/// <para>This is what turns the schema table from a drift <b>detector</b> into a drift <b>eliminator</b>:
/// the serialize side (F# <c>Interop.SecurityMaster</c>) and deserialize side
/// (<c>SecurityMasterMapping.ToSecurityKind</c>) are hand-written, and before this guard they could
/// silently diverge per field (the bond nested-coupon null-column incident, the dropped equity
/// <c>votingRightsCat</c>, the <c>EquityClassification.Other</c> read outage). Adding a schema field now
/// fails this test until the payload — and therefore both codec sides — carry it.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetTermsSchemaRoundTripTests
{
    private static readonly Guid UnderlyingId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    /// <summary>
    /// One fully-populated asset-specific-terms payload per declared asset class. Every declared
    /// schema key must appear (a key whose domain value is structurally absent in this variant — e.g.
    /// a fixed-coupon field on a floating bond — appears with an explicit null). The coverage test
    /// below fails when a class or key is missing, so schema growth forces this table to grow with it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, object> FullPayloads = new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["Equity"] = new
        {
            shareClass = "B",
            votingRightsCat = "DualClass",
            classification = "ConvertiblePreferred",
            otherClassification = (string?)null,
            preferredTerms = new
            {
                dividendRate = 6.25m,
                dividendType = "Cumulative",
                redemptionPrice = 102.5m,
                redemptionDate = "2031-06-30",
                callableDate = "2028-06-30",
                participationTerms = new
                {
                    participatesInCommonDividends = true,
                    additionalDividendThreshold = 1.5m
                },
                liquidationPreference = new { kind = "Senior", multiple = 2.0m }
            },
            convertibleTerms = new
            {
                underlyingSecurityId = UnderlyingId,
                conversionRatio = 4.2m,
                conversionPrice = 25.75m,
                conversionStartDate = "2027-01-01",
                conversionEndDate = "2031-01-01"
            }
        },
        ["Option"] = new
        {
            underlyingId = UnderlyingId,
            putCall = "Call",
            strike = 105.5m,
            expiry = "2027-12-17",
            multiplier = 100m,
            optChainId = "SPY-20271217",
            exerciseStyle = "American",
            settlementType = "Physical",
            isAdjusted = true,
            lastTradingDt = "2027-12-16"
        },
        ["Future"] = new
        {
            rootSymbol = "CL",
            contractMonth = "2027-03",
            expiry = "2027-03-22",
            multiplier = 1000m,
            lastTradingDt = "2027-03-21",
            firstNoticeDt = "2027-02-25",
            deliveryMonthDt = "2027-03-01",
            settlementType = "Physical",
            deliveryLocationCode = "CUSHING",
            isRollTarget = true,
            rollWindowDays = 5
        },
        ["Bond"] = new
        {
            maturity = "2035-06-15",
            issueDate = "2025-06-15",
            couponType = "Floating",
            couponRate = (decimal?)null,
            floatingIndex = "SOFR",
            spreadBps = 185m,
            capRate = 9.5m,
            floorRate = 0.5m,
            stepSchedule = Array.Empty<object>(),
            inflationIndex = (string?)null,
            inflationBaseIndexValue = (decimal?)null,
            inflationIndexRatio = (decimal?)null,
            dayCount = "ACT/360",
            isCallable = true,
            callDate = "2030-06-15",
            issuerName = "Meridian Capital Corp",
            seniority = "SeniorSecured",
            subclass = "Clo",
            par = 1000m,
            paymentFrequency = "Quarterly",
            legalFinalMaturity = "2036-06-15",
            preRefundDate = "2033-06-15",
            mandatoryPutDate = "2031-06-15",
            principalSchedule = new object[]
            {
                new { paymentDate = "2030-06-15", amount = 400m },
                new { paymentDate = "2031-06-15", amount = 400m }
            }
        },
        ["FxSpot"] = new { baseCurrency = "EUR", quoteCurrency = "USD" },
        ["Deposit"] = new
        {
            depositType = "TimeDeposit",
            institutionName = "First Meridian Bank",
            maturity = "2026-12-31",
            interestRate = 4.15m,
            dayCount = "ACT/365",
            isCallable = true
        },
        ["MoneyMarketFund"] = new
        {
            fundFamily = "Meridian Government MMF",
            sweepEligible = true,
            weightedAverageMaturityDays = 34,
            liquidityFeeEligible = true
        },
        ["CertificateOfDeposit"] = new
        {
            issuerName = "First Meridian Bank",
            maturity = "2027-09-30",
            couponRate = 4.4m,
            callableDate = "2026-09-30",
            dayCount = "ACT/365"
        },
        ["CommercialPaper"] = new
        {
            issuerName = "Meridian Funding LLC",
            maturity = "2026-11-15",
            discountRate = 5.05m,
            dayCount = "ACT/360",
            isAssetBacked = true
        },
        ["TreasuryBill"] = new
        {
            maturity = "2026-10-15",
            auctionDate = "2026-04-15",
            cusip = "912796YK4",
            discountRate = 5.12m
        },
        ["Repo"] = new
        {
            counterparty = "Meridian Prime Broker",
            startDate = "2026-08-12",
            endDate = "2026-08-19",
            repoRate = 5.3m,
            collateralType = "UST",
            haircut = 2m
        },
        ["CashSweep"] = new
        {
            programName = "Overnight Government Sweep",
            sweepVehicleType = "MoneyMarketFund",
            sweepFrequency = "Daily",
            targetAccountType = "Custody",
            yieldRate = 4.9m
        },
        ["OtherSecurity"] = new
        {
            category = "InsuranceLinkedNote",
            subType = "CatastropheBond",
            maturity = "2029-06-01",
            issuerName = "Meridian Re SPV",
            settlementType = "Cash"
        },
        ["CustomAsset"] = new
        {
            schemaVersion = SecurityMasterSchemaVersions.CustomAssetProfileTerms,
            category = "StructuredCredit",
            subType = "MBS",
            customProfileId = "structured-credit-io-po",
            profileVersion = 3,
            profileFields = new
            {
                tranche = "A1",
                collateralType = "AgencyMbs",
                originalFace = 25_000_000m,
                couponOrIndex = "SOFR+120"
            },
            profileApproval = new
            {
                approvedBy = "governance@meridian",
                approvedAtUtc = "2026-06-01T00:00:00Z",
                approvalReference = "profile:structured-credit-io-po:v3"
            },
            evidenceLinks = Array.Empty<object>()
        },
        ["Swap"] = new
        {
            effectiveDate = "2026-01-15",
            maturityDate = "2031-01-15",
            legs = new object[]
            {
                new { legType = "Fixed", currency = "USD", index = (string?)null, fixedRate = 3.75m },
                new { legType = "Floating", currency = "USD", index = "SOFR", fixedRate = (decimal?)null }
            }
        },
        ["DirectLoan"] = new
        {
            borrower = "Meridian Industrials LLC",
            maturity = "2030-03-31",
            referenceIndex = "SOFR",
            spreadBps = 425m,
            currentCouponRate = 9.55m,
            resetFrequency = "Quarterly",
            pricingSource = "IHSMarkit",
            covenants = new object[]
            {
                new { covenantType = "MaxLeverage", threshold = "4.5x", notes = "Tested quarterly" }
            },
            principalSchedule = new object[]
            {
                new { paymentDate = "2027-03-31", amount = 1_250_000m },
                new { paymentDate = "2028-03-31", amount = 1_250_000m }
            }
        },
        ["StructuredCredit"] = new
        {
            tranche = "B",
            poolId = "MRDN-2026-1",
            collateralType = "CLO",
            originalFace = 10_000_000m,
            currentFactor = 0.8235m,
            couponOrIndex = "SOFR+250",
            factorSchedule = "See trustee report 2026-07",
            factorScheduleEntries = new object[]
            {
                new { asOfDate = "2026-06-01", factor = 0.8412m },
                new { asOfDate = "2026-07-01", factor = 0.8235m }
            },
            maturity = "2031-06-15"
        },
        ["PrivateFundInterest"] = new
        {
            gpSponsor = "Meridian Growth Partners",
            strategy = "Buyout",
            vintage = 2024,
            commitment = 5_000_000m,
            fundedAmount = 3_250_000m,
            unfundedAmount = 1_750_000m,
            navDate = "2026-06-30",
            lockup = "8y+2x1y"
        },
        ["PrivateCompanyEquity"] = new
        {
            issuer = "Meridian Robotics Inc",
            shareClass = "Series C Preferred",
            round = "Series C",
            ownershipPercent = 2.75m,
            costBasis = 4_000_000m,
            latestValuation = 6_500_000m,
            transferRestrictions = "ROFR + board consent"
        },
        ["RealEstateHolding"] = new
        {
            propertyType = "Industrial",
            addressOrMarket = "Dallas-Fort Worth",
            ownershipPercent = 45m,
            appraisalValue = 32_000_000m,
            valuationDate = "2026-03-31",
            debtStack = "Senior 60% LTV",
            sponsor = "Meridian Real Assets"
        },
        ["CommitmentGuarantee"] = new
        {
            counterparty = "Meridian Credit Bank",
            beneficiary = "PortCo Holdings",
            committedAmount = 12_000_000m,
            unfundedAmount = 7_000_000m,
            effectiveDate = "2026-01-01",
            expiryDate = "2029-01-01",
            feeRate = 0.75m,
            collateral = "Pledged receivables",
            covenants = new object[]
            {
                new { covenantType = "MinLiquidity", threshold = "$5m", notes = (string?)null }
            }
        },
        ["Commodity"] = new
        {
            commodityType = "Gold",
            denomination = "TroyOunce",
            contractSize = 100m
        },
        ["CryptoCurrency"] = new
        {
            baseCurrency = "BTC",
            quoteCurrency = "USD",
            network = "Bitcoin"
        },
        ["Cfd"] = new
        {
            underlyingAssetClass = "Equity",
            underlyingDescription = "SPX index CFD",
            leverage = 5m
        },
        ["Warrant"] = new
        {
            underlyingId = UnderlyingId,
            warrantType = "Call",
            strike = 12.5m,
            expiry = "2029-05-31",
            multiplier = 1m
        },
        ["InvestmentFund"] = new
        {
            fundType = "ETF",
            fundFamily = "Meridian Funds",
            navCurrency = "USD",
            distributionPolicy = "Distributing",
            isStableNav = false,
            pricingSource = "Bloomberg"
        }
    };

    public static TheoryData<string> DeclaredAssetClasses()
    {
        var data = new TheoryData<string>();
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            data.Add(assetClass);
        }

        return data;
    }

    [Fact]
    public void FullPayloads_CoverExactlyTheDeclaredAssetClasses()
    {
        FullPayloads.Keys.Should().BeEquivalentTo(
            SecurityAssetTermsSchema.AssetClasses,
            "every asset class the schema declares needs a fully-populated round-trip payload, " +
            "and payloads for undeclared classes indicate schema drift");
    }

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void FullPayload_DeclaresEverySchemaField(string assetClass)
    {
        var payloadKeys = PayloadKeys(assetClass);
        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
        {
            payloadKeys.Should().Contain(
                field.Key,
                $"the '{assetClass}' round-trip payload must exercise declared field '{field.Key}' " +
                "(use an explicit null when the variant cannot carry it)");
        }
    }

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void FullPayload_SerializesToExactlyTheDeclaredFieldSet(string assetClass)
    {
        var serialized = SerializeThroughDomain(assetClass, FullPayloads[assetClass]);
        var serializedKeys = ElementKeys(serialized);
        serializedKeys.Remove("schemaVersion");

        var declaredKeys = SecurityAssetTermsSchema.Fields(assetClass)
            .Select(static field => field.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (string.Equals(assetClass, "CustomAsset", StringComparison.Ordinal))
        {
            // The custom-asset document is opaque and profile-governed: dynamic keys are expected,
            // but the envelope the platform depends on must always be present.
            foreach (var field in SecurityAssetTermsSchema.Fields(assetClass).Where(static f => f.Required))
            {
                serializedKeys.Should().Contain(
                    field.Key, $"custom-asset envelope field '{field.Key}' must survive serialization");
            }

            return;
        }

        serializedKeys.Should().BeEquivalentTo(
            declaredKeys,
            $"the serialized '{assetClass}' payload and SecurityAssetTermsSchema must declare the same field set — " +
            "a field on one side only is codec drift (a value either unreadable or silently dropped)");
    }

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void FullPayload_RoundTripsByteStableThroughBothCodecs(string assetClass)
    {
        AssertRoundTripIsByteStable(assetClass, FullPayloads[assetClass]);
    }

    [Fact]
    public void Bond_FixedAndZeroCouponVariants_RoundTripByteStable()
    {
        AssertRoundTripIsByteStable("Bond", new
        {
            maturity = "2030-01-01",
            issueDate = "2024-01-01",
            couponType = "Fixed",
            couponRate = 4.25m,
            dayCount = "30/360",
            isCallable = false,
            issuerName = "ACME Corp",
            seniority = "Senior",
            subclass = "Corporate",
            par = 1000m,
            paymentFrequency = "SemiAnnual"
        });

        AssertRoundTripIsByteStable("Bond", new
        {
            maturity = "2030-01-01",
            couponType = "ZeroCoupon",
            isCallable = false,
            subclass = "Sovereign"
        });
    }

    [Fact]
    public void Bond_StepCouponVariant_RoundTripsByteStable()
    {
        // Step-rate bonds were previously classifiable (BondSubclass.StepRate) but not computable —
        // BondCouponStructure had no schedule case. The dated step schedule must survive the codec
        // loop so accrual/projection math can resolve the rate per period.
        AssertRoundTripIsByteStable("Bond", new
        {
            maturity = "2032-06-30",
            issueDate = "2026-06-30",
            couponType = "Step",
            stepSchedule = new object[]
            {
                new { effectiveDate = "2026-06-30", rate = 3.0m },
                new { effectiveDate = "2028-06-30", rate = 4.0m },
                new { effectiveDate = "2030-06-30", rate = 5.0m }
            },
            dayCount = "30/360",
            isCallable = true,
            callDate = "2028-06-30",
            subclass = "StepRate",
            par = 1000m,
            paymentFrequency = "SemiAnnual"
        });
    }

    [Fact]
    public void Bond_InflationLinkedVariant_RoundTripsByteStable()
    {
        // Inflation-linked bonds previously had nowhere to put an index ratio; the real rate rides
        // the couponRate slot (discriminated by couponType) and the indexation fields must survive.
        AssertRoundTripIsByteStable("Bond", new
        {
            maturity = "2036-01-15",
            issueDate = "2026-01-15",
            couponType = "InflationLinked",
            couponRate = 1.25m,
            inflationIndex = "CPI-U",
            inflationBaseIndexValue = 305.109m,
            inflationIndexRatio = 1.0432m,
            dayCount = "ACT/ACT",
            isCallable = false,
            subclass = "InflationLinked",
            par = 1000m,
            paymentFrequency = "SemiAnnual"
        });
    }

    [Fact]
    public void Bond_EveryDeclaredCouponType_SerializesBackToTheValueItWasGiven()
    {
        // Byte-stability alone cannot see this class of defect: an undeclared couponType collapses
        // to Fixed on the FIRST pass, so both passes agree and the loop looks lossless while the
        // submitted value is already gone. Compare the SUBMITTED discriminant against the
        // serialized one, and do it over the schema's declared vocabulary so the closed set and the
        // F# serializer cannot drift apart — a coupon structure added to one and not the other
        // fails here rather than silently degrading records in production.
        var payloadsByCouponType = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Fixed"] = new { maturity = "2030-01-01", couponType = "Fixed", couponRate = 4.25m, isCallable = false, subclass = "Corporate" },
            ["Floating"] = new { maturity = "2030-01-01", couponType = "Floating", floatingIndex = "SOFR", spreadBps = 185m, isCallable = false, subclass = "FloatingRate" },
            ["ZeroCoupon"] = new { maturity = "2030-01-01", couponType = "ZeroCoupon", isCallable = false, subclass = "Sovereign" },
            ["Step"] = new
            {
                maturity = "2032-06-30",
                couponType = "Step",
                stepSchedule = new object[] { new { effectiveDate = "2026-06-30", rate = 3.0m } },
                isCallable = false,
                subclass = "StepRate"
            },
            ["InflationLinked"] = new
            {
                maturity = "2036-01-15",
                couponType = "InflationLinked",
                couponRate = 1.25m,
                inflationIndex = "CPI-U",
                isCallable = false,
                subclass = "InflationLinked"
            }
        };

        var declared = SecurityAssetTermsSchema.AllowedValues("Bond", "couponType");
        declared.Should().NotBeEmpty("couponType is a closed vocabulary the codec cannot round-trip outside of");
        payloadsByCouponType.Keys.Should().BeEquivalentTo(
            declared,
            "every declared coupon structure needs a payload proving it survives the codec");

        foreach (var couponType in declared)
        {
            var canonical = SerializeThroughDomain("Bond", payloadsByCouponType[couponType]);

            canonical.GetProperty("couponType").GetString().Should().Be(
                couponType,
                "the serializer must write back the coupon structure the payload named");
        }
    }

    [Fact]
    public void Equity_LegacyRawOtherClassification_ReadCanonicalizesAndThenRoundTripsByteStable()
    {
        // Rows written before the serializer emitted the "Other" discriminant carry the raw label
        // in the classification slot. The READ path must tolerate them (Other(raw)) — the WRITE
        // path now rejects unknown discriminants outright — and the canonical form they
        // re-serialize to must itself be stable.
        var legacyProjection = new SecurityProjectionRecord(
            Guid.NewGuid(),
            "Equity",
            SecurityStatusDto.Active,
            "Legacy tracking stock",
            "USD",
            "InternalCode",
            "LEGACY-EQ-1",
            JsonSerializer.SerializeToElement(new { displayName = "Legacy tracking stock", currency = "USD" }),
            JsonSerializer.SerializeToElement(new { shareClass = "T", classification = "TrackingStock" }),
            JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "schema-round-trip-tests",
                asOf = "2026-01-01T00:00:00+00:00",
                updatedBy = "schema-round-trip-tests"
            }),
            1,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "LEGACY-EQ-1", true, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))],
            []);

        var record = SecurityMasterMapping.ToRecord(legacyProjection);
        var canonical = JsonDocument
            .Parse(new SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson)
            .RootElement.Clone();

        canonical.GetProperty("classification").GetString().Should().Be("Other");
        canonical.GetProperty("otherClassification").GetString().Should().Be("TrackingStock");

        AssertRoundTripIsByteStable("Equity", JsonSerializer.Deserialize<object>(canonical.GetRawText())!);
    }

    [Fact]
    public void CustomAsset_ProfileEnvelope_SurvivesTheDomainAsAFirstClassKind()
    {
        var snapshot = CreateSnapshot("CustomAsset", FullPayloads["CustomAsset"]);

        // Regression anchor: CustomAsset previously collapsed into OtherSecurity, so the snapshot's
        // asset class came back "OtherSecurity" and the profile envelope was dropped on amend.
        snapshot.AssetClass.Should().Be("CustomAsset");

        using var document = JsonDocument.Parse(snapshot.AssetSpecificTermsJson);
        document.RootElement.GetProperty("customProfileId").GetString().Should().Be("structured-credit-io-po");
        document.RootElement.GetProperty("profileVersion").GetInt32().Should().Be(3);
        document.RootElement.GetProperty("profileFields").GetProperty("tranche").GetString().Should().Be("A1");
        document.RootElement.GetProperty("profileApproval").GetProperty("approvedBy").GetString()
            .Should().Be("governance@meridian");
        document.RootElement.GetProperty("category").GetString().Should().Be("StructuredCredit");
    }

    private static HashSet<string> PayloadKeys(string assetClass)
        => ElementKeys(JsonSerializer.SerializeToElement(FullPayloads[assetClass]));

    private static HashSet<string> ElementKeys(JsonElement element)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            keys.Add(property.Name);
        }

        return keys;
    }

    /// <summary>
    /// Runs a payload once through the real codec seam (C# deserializer → F# domain → F# serializer)
    /// and returns the canonical serialized asset-specific-terms document.
    /// </summary>
    private static JsonElement SerializeThroughDomain(string assetClass, object payload)
    {
        var snapshot = CreateSnapshot(assetClass, payload);
        return JsonDocument.Parse(snapshot.AssetSpecificTermsJson).RootElement.Clone();
    }

    private static void AssertRoundTripIsByteStable(string assetClass, object payload)
    {
        var snapshot = CreateSnapshot(assetClass, payload);
        var firstPass = snapshot.AssetSpecificTermsJson;

        var projection = SecurityMasterMapping.ToProjection(snapshot);
        var record = SecurityMasterMapping.ToRecord(projection);
        var secondPass = new SecurityMasterSnapshotWrapper(record).AssetSpecificTermsJson;

        secondPass.Should().Be(
            firstPass,
            $"the '{assetClass}' codec loop (serialize → deserialize → serialize) must be lossless");
    }

    private static SecurityMasterSnapshotWrapper CreateSnapshot(string assetClass, object payload)
    {
        var effectiveFrom = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var request = new CreateSecurityRequest(
            SecurityId: Guid.NewGuid(),
            AssetClass: assetClass,
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = $"{assetClass} schema round-trip",
                currency = "USD"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(payload),
            Identifiers:
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"SCHEMA-{assetClass}", true, effectiveFrom)
            ],
            EffectiveFrom: effectiveFrom,
            SourceSystem: "schema-round-trip-tests",
            UpdatedBy: "schema-round-trip-tests",
            SourceRecordId: $"{assetClass}-schema-round-trip",
            Reason: "SecurityAssetTermsSchema round-trip guard");

        var command = SecurityMasterMapping.ToCreateCommand(request);
        var result = SecurityMasterCommandFacade.Create(command);

        result.IsSuccess.Should().BeTrue(
            string.Join("; ", result.ErrorDetails.Select(error => $"[{error.Code}] {error.Message}")));
        result.Snapshot.Should().NotBeNull();
        return result.Snapshot;
    }
}
