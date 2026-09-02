using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Meridian.Application.SecurityMaster;

internal static class SecurityMasterMapping
{
    public static CreateSecurity ToCreateCommand(CreateSecurityRequest request)
        => new(
            SecurityId.NewSecurityId(request.SecurityId),
            ToCommonTerms(request.CommonTerms),
            ToFSharpList(request.Identifiers.Select(ToIdentifier)),
            ToSecurityKind(request.AssetClass, request.AssetSpecificTerms, SecurityKindMappingMode.Write),
            request.EffectiveFrom,
            ToProvenance(request.SourceSystem, request.UpdatedBy, request.SourceRecordId, request.Reason, request.EffectiveFrom));

    public static AmendTerms ToAmendCommand(AmendSecurityTermsRequest request, SecurityProjectionRecord current)
        => new(
            SecurityId.NewSecurityId(request.SecurityId),
            request.ExpectedVersion,
            request.CommonTerms is JsonElement common ? FSharpOption<CommonTerms>.Some(ToCommonTerms(common)) : FSharpOption<CommonTerms>.None,
            request.AssetSpecificTermsPatch is JsonElement assetSpecific
                ? FSharpOption<SecurityKind>.Some(ToSecurityKind(current.AssetClass, assetSpecific, SecurityKindMappingMode.Write))
                : FSharpOption<SecurityKind>.None,
            ToFSharpList(request.IdentifiersToAdd.Select(ToIdentifier)),
            ToFSharpList(request.IdentifiersToExpire.Select(ToIdentifier)),
            request.EffectiveFrom,
            ToProvenance(request.SourceSystem, request.UpdatedBy, request.SourceRecordId, request.Reason, request.EffectiveFrom));

    public static DeactivateSecurity ToDeactivateCommand(DeactivateSecurityRequest request)
        => new(
            SecurityId.NewSecurityId(request.SecurityId),
            request.ExpectedVersion,
            request.EffectiveTo,
            ToProvenance(request.SourceSystem, request.UpdatedBy, request.SourceRecordId, request.Reason, request.EffectiveTo));

    public static SecurityMasterRecord ToRecord(SecurityProjectionRecord record)
        => new(
            SecurityId.NewSecurityId(record.SecurityId),
            ToSecurityStatus(record.Status),
            ToCommonTerms(record.CommonTerms),
            ToFSharpList(record.Identifiers.Select(ToIdentifier)),
            ToSecurityKind(record.AssetClass, record.AssetSpecificTerms),
            record.Version,
            record.EffectiveFrom,
            ToOption(record.EffectiveTo),
            ToProvenance(record.Provenance));

    public static SecurityProjectionRecord ToProjection(SecurityMasterSnapshotWrapper snapshot, IReadOnlyList<SecurityAliasDto>? aliases = null)
        => new(
            snapshot.SecurityId,
            snapshot.AssetClass,
            ToSecurityStatus(snapshot.Status),
            snapshot.DisplayName,
            snapshot.Currency,
            snapshot.PrimaryIdentifierKind,
            snapshot.PrimaryIdentifierValue,
            ParseJson(snapshot.CommonTermsJson),
            ParseJson(snapshot.AssetSpecificTermsJson),
            ParseJson(snapshot.ProvenanceJson),
            snapshot.Version,
            snapshot.EffectiveFrom,
            snapshot.EffectiveTo.HasValue ? snapshot.EffectiveTo.Value : null,
            snapshot.Identifiers.Select(ToIdentifierDto).ToArray(),
            aliases ?? Array.Empty<SecurityAliasDto>());

    public static SecurityDetailDto ToDetail(SecurityProjectionRecord projection)
        => new(
            projection.SecurityId,
            projection.AssetClass,
            projection.Status,
            projection.DisplayName,
            projection.Currency,
            projection.CommonTerms,
            projection.AssetSpecificTerms,
            NormalizeIdentifiers(projection.Identifiers),
            projection.Aliases,
            projection.Version,
            projection.EffectiveFrom,
            projection.EffectiveTo);

    public static SecurityMasterEventEnvelope ToEventEnvelope(
        SecurityEconomicDefinitionRecord economic,
        string eventType,
        string actor,
        string sourceSystem,
        string? reason,
        long streamVersion)
    {
        var metadata = JsonSerializer.SerializeToElement(
            new Dictionary<string, object?>
            {
                ["sourceSystem"] = sourceSystem,
                ["reason"] = reason,
                ["schemaVersion"] = SecurityMasterSchemaVersions.EconomicTerms,
                ["payloadType"] = "SecurityEconomicDefinition"
            },
            SecurityMasterJsonContext.Default.DictionaryStringObject);

        return new SecurityMasterEventEnvelope(
            GlobalSequence: null,
            SecurityId: economic.SecurityId,
            StreamVersion: streamVersion,
            EventType: eventType,
            EventTimestamp: DateTimeOffset.UtcNow,
            Actor: actor,
            CorrelationId: null,
            CausationId: null,
            Payload: JsonSerializer.SerializeToElement(
                economic,
                SecurityMasterJsonContext.Default.SecurityEconomicDefinitionRecord),
            Metadata: metadata);
    }

    public static SecurityEconomicDefinitionRecord FromEconomicPayload(JsonElement payload)
    {
        if (payload.TryGetProperty("classification", out _) && payload.TryGetProperty("economicTerms", out _))
        {
            return JsonSerializer.Deserialize(payload, SecurityMasterJsonContext.Default.SecurityEconomicDefinitionRecord)
                ?? throw new InvalidOperationException("Security economic definition payload could not be deserialized.");
        }

        var projection = JsonSerializer.Deserialize(payload, SecurityMasterJsonContext.Default.SecurityProjectionRecord)
            ?? throw new InvalidOperationException("Security projection payload could not be deserialized.");
        return SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
    }

    public static SecurityProjectionRecord FromProjectionPayload(JsonElement payload)
    {
        var economicRecord = FromEconomicPayload(payload);
        return SecurityEconomicDefinitionAdapter.ToProjection(economicRecord);
    }

    public static SecuritySnapshotRecord ToSnapshot(SecurityEconomicDefinitionRecord economic, DateTimeOffset snapshotTimestamp)
        => new(
            economic.SecurityId,
            economic.Version,
            snapshotTimestamp,
            JsonSerializer.SerializeToElement(
                economic,
                SecurityMasterJsonContext.Default.SecurityEconomicDefinitionRecord));

    private static SecurityStatus ToSecurityStatus(SecurityStatusDto status)
        => status == SecurityStatusDto.Active ? SecurityStatus.Active : SecurityStatus.Inactive;

    private static SecurityStatusDto ToSecurityStatus(string status)
        => string.Equals(status, "Inactive", StringComparison.OrdinalIgnoreCase)
            ? SecurityStatusDto.Inactive
            : SecurityStatusDto.Active;

    private static IReadOnlyList<SecurityIdentifierDto> NormalizeIdentifiers(IReadOnlyList<SecurityIdentifierDto> identifiers)
        => identifiers
            .Select(static identifier => identifier with
            {
                NormalizedValue = SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier),
                NormalizedProvider = SecurityIdentifierNormalizer.GetOrComputeNormalizedProvider(identifier)
            })
            .ToArray();

    private static Identifier ToIdentifier(SecurityIdentifierDto identifier)
        => new(
            ToIdentifierKind(identifier.Kind, identifier.Provider),
            identifier.Value,
            identifier.IsPrimary,
            identifier.ValidFrom,
            ToOption(identifier.ValidTo),
            ToOption(identifier.Provider));

    private static IdentifierKind ToIdentifierKind(SecurityIdentifierKind kind, string? provider)
        => kind switch
        {
            SecurityIdentifierKind.Ticker => IdentifierKind.Ticker,
            SecurityIdentifierKind.Isin => IdentifierKind.Isin,
            SecurityIdentifierKind.Cusip => IdentifierKind.Cusip,
            SecurityIdentifierKind.Sedol => IdentifierKind.Sedol,
            SecurityIdentifierKind.Figi => IdentifierKind.Figi,
            SecurityIdentifierKind.OccOptionSymbol => IdentifierKind.OccOptionSymbol,
            SecurityIdentifierKind.ProviderSymbol => IdentifierKind.NewProviderSymbol(provider ?? string.Empty),
            SecurityIdentifierKind.InternalCode => IdentifierKind.InternalCode,
            SecurityIdentifierKind.Lei => IdentifierKind.Lei,
            SecurityIdentifierKind.PermId => IdentifierKind.PermId,
            SecurityIdentifierKind.Bbgid => IdentifierKind.Bbgid,
            SecurityIdentifierKind.Wkn => IdentifierKind.Wkn,
            SecurityIdentifierKind.Valoren => IdentifierKind.Valoren,
            SecurityIdentifierKind.PermTicker => IdentifierKind.PermTicker,
            SecurityIdentifierKind.Ric => IdentifierKind.Ric,
            SecurityIdentifierKind.Cik => IdentifierKind.Cik,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported security identifier kind.")
        };

    private static SecurityIdentifierDto ToIdentifierDto(SecurityIdentifierSnapshot identifier)
    {
        // Read-tolerant: a kind stamped by a newer node degrades to Unknown so the snapshot stays
        // readable; the strict write-side mapping (ToIdentifier) still rejects Unknown, so an
        // unrecognized kind is never silently re-persisted.
        var kind = SecurityMasterEnumReads.ParseOrFallback(identifier.Kind, SecurityIdentifierKind.Unknown);
        return new(
            kind,
            identifier.Value,
            identifier.IsPrimary,
            identifier.ValidFrom,
            identifier.ValidTo.HasValue ? identifier.ValidTo.Value : null,
            string.IsNullOrWhiteSpace(identifier.Provider) ? null : identifier.Provider,
            SecurityIdentifierNormalizer.NormalizeValue(kind, identifier.Value),
            SecurityIdentifierNormalizer.NormalizeProvider(identifier.Provider));
    }

    private static CommonTerms ToCommonTerms(JsonElement json)
        => new(
            GetRequiredString(json, "displayName"),
            GetRequiredString(json, "currency"),
            ToOption(GetOptionalString(json, "countryOfRisk")),
            ToOption(GetOptionalString(json, "issuerName")),
            ToOption(GetOptionalString(json, "exchange")),
            ToOption(GetOptionalDecimal(json, "lotSize")),
            ToOption(GetOptionalDecimal(json, "tickSize")),
            ToOption(GetOptionalString(json, "primaryListingMic")),
            ToOption(GetOptionalString(json, "countryOfIncorporation")),
            ToOption(GetOptionalInt(json, "settlementCycleDays")),
            ToOption(GetOptionalString(json, "holidayCalendarId")));

    /// <summary>
    /// Distinguishes command mapping (create/amend requests) from record reconstruction. Write
    /// mapping is strict: a malformed payload must fail the command instead of being coerced into
    /// a different kind. Read mapping stays tolerant so legacy rows remain loadable.
    /// </summary>
    private enum SecurityKindMappingMode
    {
        Read,
        Write
    }

    private static SecurityKind ToSecurityKind(string assetClass, JsonElement json, SecurityKindMappingMode mode = SecurityKindMappingMode.Read)
    {
        EnsureSupportedAssetSchemaVersion(assetClass, json);
        var terms = ResolveAssetTermsJson(json);

        var kind = assetClass switch
        {
            "Equity" => SecurityKind.NewEquity(new EquityTerms(
                ToOption(GetOptionalString(json, "shareClass")),
                ToVotingRightsCatOption(GetOptionalString(json, "votingRightsCat")),
                ToEquityClassificationOption(json, mode))),
            "Option" => SecurityKind.NewOption(new OptionTerms(
                SecurityId.NewSecurityId(GetRequiredGuid(json, "underlyingId")),
                GetRequiredString(json, "putCall"),
                GetRequiredDecimal(json, "strike"),
                GetRequiredDateOnly(json, "expiry"),
                GetRequiredDecimal(json, "multiplier"),
                ToOption(GetOptionalString(json, "optChainId")),
                ParseExerciseStyle(GetOptionalString(json, "exerciseStyle")),
                ToOption(GetOptionalString(json, "settlementType")),
                GetOptionalBoolean(json, "isAdjusted") ?? false,
                ToOption(GetOptionalDateOnly(json, "lastTradingDt")))),
            "Future" => SecurityKind.NewFuture(new FutureTerms(
                GetRequiredString(json, "rootSymbol"),
                GetRequiredString(json, "contractMonth"),
                GetRequiredDateOnly(json, "expiry"),
                GetRequiredDecimal(json, "multiplier"),
                ToOption(GetOptionalDateOnly(json, "lastTradingDt")),
                ToOption(GetOptionalDateOnly(json, "firstNoticeDt")),
                ToOption(GetOptionalDateOnly(json, "deliveryMonthDt")),
                ToOption(GetOptionalString(json, "settlementType")),
                ToOption(GetOptionalString(json, "deliveryLocationCode")),
                GetOptionalBoolean(json, "isRollTarget") ?? false,
                ToOption(GetOptionalInt(json, "rollWindowDays")))),
            "Bond" => SecurityKind.NewBond(ToBondTerms(json, mode)),
            "FxSpot" => SecurityKind.NewFxSpot(new FxSpotTerms(
                GetRequiredString(json, "baseCurrency"),
                GetRequiredString(json, "quoteCurrency"))),
            "Deposit" => SecurityKind.NewDeposit(new DepositTerms(
                GetRequiredString(json, "depositType"),
                GetRequiredString(json, "institutionName"),
                ToOption(GetOptionalDateOnly(json, "maturity")),
                ToOption(GetOptionalDecimal(json, "interestRate")),
                ToOption(GetOptionalString(json, "dayCount")),
                GetOptionalBoolean(json, "isCallable") ?? false)),
            "MoneyMarketFund" => SecurityKind.NewMoneyMarketFund(new MoneyMarketFundTerms(
                ToOption(GetOptionalString(json, "fundFamily")),
                GetOptionalBoolean(json, "sweepEligible") ?? false,
                ToOption(GetOptionalInt(json, "weightedAverageMaturityDays")),
                GetOptionalBoolean(json, "liquidityFeeEligible") ?? false)),
            "CertificateOfDeposit" => SecurityKind.NewCertificateOfDeposit(new CertificateOfDepositTerms(
                GetRequiredString(json, "issuerName"),
                GetRequiredDateOnly(json, "maturity"),
                ToOption(GetOptionalDecimal(json, "couponRate")),
                ToOption(GetOptionalDateOnly(json, "callableDate")),
                ToOption(GetOptionalString(json, "dayCount")))),
            "CommercialPaper" => SecurityKind.NewCommercialPaper(new CommercialPaperTerms(
                GetRequiredString(json, "issuerName"),
                GetRequiredDateOnly(json, "maturity"),
                ToOption(GetOptionalDecimal(json, "discountRate")),
                ToOption(GetOptionalString(json, "dayCount")),
                GetOptionalBoolean(json, "isAssetBacked") ?? false)),
            "TreasuryBill" => SecurityKind.NewTreasuryBill(new TreasuryBillTerms(
                GetRequiredDateOnly(json, "maturity"),
                ToOption(GetOptionalDateOnly(json, "auctionDate")),
                ToOption(GetOptionalString(json, "cusip")),
                ToOption(GetOptionalDecimal(json, "discountRate")))),
            "Repo" => SecurityKind.NewRepo(new RepoTerms(
                GetRequiredString(json, "counterparty"),
                GetRequiredDateOnly(json, "startDate"),
                GetRequiredDateOnly(json, "endDate"),
                ToOption(GetOptionalDecimal(json, "repoRate")),
                ToOption(GetOptionalString(json, "collateralType")),
                ToOption(GetOptionalDecimal(json, "haircut")))),
            "CashSweep" => SecurityKind.NewCashSweep(new CashSweepTerms(
                GetRequiredString(json, "programName"),
                GetRequiredString(json, "sweepVehicleType"),
                ToOption(GetOptionalString(json, "sweepFrequency")),
                ToOption(GetOptionalString(json, "targetAccountType")),
                ToOption(GetOptionalDecimal(json, "yieldRate")))),
            "OtherSecurity" => SecurityKind.NewOtherSecurity(new OtherSecurityTerms(
                GetRequiredString(json, "category"),
                ToOption(GetOptionalString(json, "subType")),
                ToOption(GetOptionalDateOnly(json, "maturity")),
                ToOption(GetOptionalString(json, "issuerName")),
                ToOption(GetOptionalString(json, "settlementType")))),
            "CustomAsset" => ToCustomAssetKind(json, mode),
            "Swap" => SecurityKind.NewSwap(new SwapTerms(
                GetRequiredDateOnly(json, "effectiveDate"),
                GetRequiredDateOnly(json, "maturityDate"),
                ToFSharpList(GetRequiredArray(json, "legs").EnumerateArray().Select(ToSwapLeg)))),
            "DirectLoan" => SecurityKind.NewDirectLoan(new DirectLoanTerms(
                GetRequiredString(json, "borrower"),
                ToOption(GetOptionalDateOnly(json, "maturity")),
                ToFSharpList(GetRequiredArray(json, "covenants").EnumerateArray().Select(ToCovenant)),
                ToOption(GetOptionalString(json, "referenceIndex")),
                ToOption(GetOptionalDecimal(json, "spreadBps")),
                ToOption(GetOptionalDecimal(json, "currentCouponRate")),
                ToOption(GetOptionalString(json, "resetFrequency")),
                ToFSharpList(GetOptionalArrayItems(json, "principalSchedule").Select(ToPrincipalPaymentEntry)),
                ToOption(GetOptionalString(json, "pricingSource")))),
            "StructuredCredit" => SecurityKind.NewStructuredCredit(new StructuredCreditTerms(
                GetRequiredString(terms, "tranche"),
                ToOption(GetOptionalString(terms, "poolId")),
                GetRequiredString(terms, "collateralType"),
                GetRequiredDecimal(terms, "originalFace"),
                ToOption(GetOptionalDecimal(terms, "currentFactor")),
                GetRequiredString(terms, "couponOrIndex"),
                ToOption(GetOptionalString(terms, "factorSchedule")),
                ToFSharpList(GetOptionalArrayItemsStrict(terms, "factorScheduleEntries").Select(ToFactorScheduleEntry)),
                ToOption(GetOptionalDateOnly(terms, "maturity")))),
            "PrivateFundInterest" => SecurityKind.NewPrivateFundInterest(new PrivateFundInterestTerms(
                GetRequiredString(terms, "gpSponsor"),
                GetRequiredString(terms, "strategy"),
                GetRequiredInt(terms, "vintage"),
                GetRequiredDecimal(terms, "commitment"),
                ToOption(GetOptionalDecimal(terms, "fundedAmount")),
                ToOption(GetOptionalDecimal(terms, "unfundedAmount")),
                GetRequiredDateOnly(terms, "navDate"),
                ToOption(GetOptionalString(terms, "lockup")))),
            "PrivateCompanyEquity" => SecurityKind.NewPrivateCompanyEquity(new PrivateCompanyEquityTerms(
                GetRequiredString(terms, "issuer"),
                GetRequiredString(terms, "shareClass"),
                GetRequiredString(terms, "round"),
                ToOption(GetOptionalDecimal(terms, "ownershipPercent")),
                GetRequiredDecimal(terms, "costBasis"),
                ToOption(GetOptionalDecimal(terms, "latestValuation")),
                ToOption(GetOptionalString(terms, "transferRestrictions")))),
            "RealEstateHolding" => SecurityKind.NewRealEstateHolding(new RealEstateHoldingTerms(
                GetRequiredString(terms, "propertyType"),
                GetRequiredString(terms, "addressOrMarket"),
                GetRequiredDecimal(terms, "ownershipPercent"),
                GetRequiredDecimal(terms, "appraisalValue"),
                GetRequiredDateOnly(terms, "valuationDate"),
                ToOption(GetOptionalString(terms, "debtStack")),
                ToOption(GetOptionalString(terms, "sponsor")))),
            "CommitmentGuarantee" => SecurityKind.NewCommitmentGuarantee(new CommitmentGuaranteeTerms(
                GetRequiredString(terms, "counterparty"),
                ToOption(GetOptionalString(terms, "beneficiary")),
                GetRequiredDecimal(terms, "committedAmount"),
                ToOption(GetOptionalDecimal(terms, "unfundedAmount")),
                GetRequiredDateOnly(terms, "effectiveDate"),
                ToOption(GetOptionalDateOnly(terms, "expiryDate")),
                ToOption(GetOptionalDecimal(terms, "feeRate")),
                ToOption(GetOptionalString(terms, "collateral")),
                ToFSharpList(GetOptionalArrayItems(terms, "covenants").Select(ToCovenant)))),
            "Commodity" => SecurityKind.NewCommodity(new CommodityTerms(
                GetRequiredString(json, "commodityType"),
                ToOption(GetOptionalString(json, "denomination")),
                ToOption(GetOptionalDecimal(json, "contractSize")))),
            "CryptoCurrency" => SecurityKind.NewCryptoCurrency(new CryptoTerms(
                GetRequiredString(json, "baseCurrency"),
                GetRequiredString(json, "quoteCurrency"),
                ToOption(GetOptionalString(json, "network")))),
            "Cfd" => SecurityKind.NewCfd(new CfdTerms(
                GetRequiredString(json, "underlyingAssetClass"),
                ToOption(GetOptionalString(json, "underlyingDescription")),
                ToOption(GetOptionalDecimal(json, "leverage")))),
            "Warrant" => SecurityKind.NewWarrant(new WarrantTerms(
                SecurityId.NewSecurityId(GetRequiredGuid(json, "underlyingId")),
                GetRequiredString(json, "warrantType"),
                ToOption(GetOptionalDecimal(json, "strike")),
                ToOption(GetOptionalDateOnly(json, "expiry")),
                ToOption(GetOptionalDecimal(json, "multiplier")))),
            "InvestmentFund" => SecurityKind.NewInvestmentFund(new InvestmentFundTerms(
                ToOption(GetOptionalString(json, "fundType")),
                ToOption(GetOptionalString(json, "fundFamily")),
                ToOption(GetOptionalString(json, "navCurrency")),
                ToDistributionPolicyOption(GetOptionalString(json, "distributionPolicy")),
                ToOption(GetOptionalBoolean(json, "isStableNav")),
                ToOption(GetOptionalString(json, "pricingSource")))),
            // Unknown classes degrade to OtherSecurity with the raw class preserved as the category
            // instead of failing every read of the row. A newer node can register a class this node
            // has no deserializer for; throwing here made that a total read outage per security
            // (see the InvestmentFund regression in SecurityMasterMappingInteropTests).
            _ => SecurityKind.NewOtherSecurity(new OtherSecurityTerms(
                assetClass,
                ToOption(GetOptionalString(json, "subType")),
                ToOption(GetOptionalDateOnly(json, "maturity")),
                ToOption(GetOptionalString(json, "issuerName")),
                ToOption(GetOptionalString(json, "settlementType"))))
        };

        EnsureDeclaredVocabulariesOnWrite(assetClass, json, mode);
        return kind;
    }

    /// <summary>
    /// The schema-driven backstop for closed-vocabulary discriminants on the WRITE path: every
    /// declared vocabulary in <see cref="SecurityAssetTermsSchema"/> is enforced here, so adding one
    /// to the table constrains create/amend commands without a matching edit to this mapping.
    /// <para>It runs AFTER the kind is built purely for diagnosis quality: the decode sites that
    /// branch on a discriminant (the equity classification, the bond coupon structure) already throw
    /// with the precise reason their case needs, and letting them speak first keeps those messages.
    /// The mapping is a pure function, so nothing has been committed by the time this runs — the
    /// ordering costs nothing but the wording of the exception.</para>
    /// </summary>
    private static void EnsureDeclaredVocabulariesOnWrite(
        string assetClass, JsonElement json, SecurityKindMappingMode mode)
    {
        if (mode != SecurityKindMappingMode.Write || json.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var field in SecurityAssetTermsSchema.DiscriminantFields(assetClass))
        {
            var raw = GetOptionalString(json, field.Key);
            if (raw is null || field.Allows(raw))
            {
                continue;
            }

            throw new InvalidOperationException(UndeclaredDiscriminantValue(assetClass, field.Key, raw));
        }
    }

    /// <summary>
    /// The shared rejection reason for a discriminant value outside its declared vocabulary, so the
    /// decode sites and the schema backstop reject a given value with identical wording.
    /// </summary>
    private static string UndeclaredDiscriminantValue(string assetClass, string key, string? value)
        => SecurityAssetTermsSchema.Field(assetClass, key) is { } field
            ? field.DescribeUndeclaredValue(assetClass, value)
            : $"Value '{value}' is not a declared '{key}' value for asset class '{assetClass}'.";

    /// <summary>
    /// Maps a CustomAsset payload to the first-class <see cref="SecurityKind.CustomAsset"/> case,
    /// carrying the document verbatim so the profile envelope and dynamic profile fields survive
    /// amend round-trips. A legacy CustomAsset row without a profile envelope degrades to the
    /// pre-existing OtherSecurity salvage on READS only; create/amend commands that name
    /// CustomAsset without a profile envelope fail here instead of being silently re-typed, so the
    /// F# CustomAsset invariants (profile envelope, profileFields object) always run for writes.
    /// <para>The <c>profileVersion ?? 1</c> default is READ tolerance only: the F# write-path
    /// validation (<c>validateKind</c>) parses the document and rejects create/amend commands whose
    /// envelope lacks a numeric <c>profileVersion</c> or an object-valued <c>profileFields</c>, so
    /// the default can never mint a canonical record with an incomplete envelope.</para>
    /// </summary>
    private static SecurityKind ToCustomAssetKind(JsonElement json, SecurityKindMappingMode mode)
    {
        if (json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty("customProfileId", out var customProfileId)
            && customProfileId.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(customProfileId.GetString()))
        {
            return SecurityKind.NewCustomAsset(new CustomAssetTerms(
                customProfileId.GetString()!,
                GetOptionalInt(json, "profileVersion") ?? 1,
                json.GetRawText()));
        }

        if (mode == SecurityKindMappingMode.Write)
        {
            throw new InvalidOperationException(
                "CustomAsset terms must include a non-empty 'customProfileId' referencing an approved asset profile. " +
                "Select an approved profile (or use asset class 'OtherSecurity' for unprofiled instruments) instead of submitting an envelope-less CustomAsset.");
        }

        return SecurityKind.NewOtherSecurity(new OtherSecurityTerms(
            GetOptionalString(json, "category") ?? "CustomAsset",
            ToOption(GetOptionalString(json, "subType")),
            ToOption(GetOptionalDateOnly(json, "maturity")),
            ToOption(GetOptionalString(json, "issuerName")),
            ToOption(GetOptionalString(json, "settlementType"))));
    }

    private static BondSubclass ParseBondSubclass(string? subclass) => subclass switch
    {
        "Sovereign" => BondSubclass.Sovereign,
        "Municipal" => BondSubclass.Municipal,
        "Agency" => BondSubclass.Agency,
        "Convertible" => BondSubclass.Convertible,
        "InflationLinked" => BondSubclass.InflationLinked,
        "FloatingRate" => BondSubclass.FloatingRate,
        "SinkingFund" => BondSubclass.SinkingFund,
        "StepRate" => BondSubclass.StepRate,
        "FixedToFloat" => BondSubclass.FixedToFloat,
        "Vrdn" => BondSubclass.Vrdn,
        "AuctionRate" => BondSubclass.AuctionRate,
        "BankLoan" => BondSubclass.BankLoan,
        "AssetBacked" => BondSubclass.AssetBacked,
        "MortgageBacked" => BondSubclass.MortgageBacked,
        "AgencyMbs" => BondSubclass.AgencyMbs,
        "CommercialMbs" => BondSubclass.CommercialMbs,
        "Cmo" => BondSubclass.Cmo,
        "Clo" => BondSubclass.Clo,
        "Cdo" => BondSubclass.Cdo,
        "PrincipalOnly" => BondSubclass.PrincipalOnly,
        "InterestOnly" => BondSubclass.InterestOnly,
        "InverseInterestOnly" => BondSubclass.InverseInterestOnly,
        "Corporate" => BondSubclass.Corporate,
        null or "" => BondSubclass.Corporate,
        var other => BondSubclass.NewOther(other)
    };

    /// <summary>
    /// Decodes the flat bond coupon contract. <c>couponType</c> is a closed schema vocabulary
    /// (<c>Fixed</c>, <c>Floating</c>, <c>ZeroCoupon</c>, <c>Step</c>, <c>InflationLinked</c>) with
    /// no escape: an unrecognized value cannot be carried, it collapses to <c>Fixed</c> and takes
    /// the label plus every structure-specific field (<c>floatingIndex</c>, the step schedule, the
    /// inflation block) with it. So the fallback arm is READ tolerance only — the same
    /// read-tolerant/write-strict split the CustomAsset envelope check and the equity classification
    /// decode already apply. An absent <c>couponType</c> still means <c>Fixed</c> on both paths: it
    /// is the serializer's own spelling for a plain fixed coupon, not an unreadable value.
    /// </summary>
    private static BondTerms ToBondTerms(JsonElement json, SecurityKindMappingMode mode)
    {
        var couponType = GetOptionalString(json, "couponType") ?? "Fixed";
        BondCouponStructure coupon = couponType switch
        {
            "Floating" => BondCouponStructure.NewFloating(
                GetRequiredString(json, "floatingIndex"),
                ToOption(GetOptionalDecimal(json, "spreadBps")),
                ToOption(GetOptionalDecimal(json, "capRate")),
                ToOption(GetOptionalDecimal(json, "floorRate")),
                ToOption(GetOptionalString(json, "dayCount"))),
            "ZeroCoupon" => BondCouponStructure.ZeroCoupon,
            "Step" => BondCouponStructure.NewStep(
                ToFSharpList(GetOptionalArrayItemsStrict(json, "stepSchedule").Select(ToStepCouponEntry)),
                ToOption(GetOptionalString(json, "dayCount"))),
            // The scalar couponRate slot carries the inflation-linked REAL rate; couponType
            // discriminates, so a fixed-coupon read can never pick up an indexed rate.
            "InflationLinked" => BondCouponStructure.NewInflationLinked(
                GetOptionalDecimal(json, "couponRate") ?? 0m,
                GetRequiredString(json, "inflationIndex"),
                ToOption(GetOptionalDecimal(json, "inflationBaseIndexValue")),
                ToOption(GetOptionalDecimal(json, "inflationIndexRatio")),
                ToOption(GetOptionalString(json, "dayCount"))),
            "Fixed" => BondCouponStructure.NewFixed(
                GetOptionalDecimal(json, "couponRate") ?? 0m,
                ToOption(GetOptionalString(json, "dayCount"))),
            // A WRITE fails closed on an unrecognized coupon type: silently persisting a typo
            // ("Floter") as a fixed coupon would change the bond's economics and drop the fields
            // the named structure owns.
            _ when mode == SecurityKindMappingMode.Write =>
                throw new InvalidOperationException(UndeclaredDiscriminantValue("Bond", "couponType", couponType)),
            // Read tolerance: an unrecognized coupon type must not fail every read of the row.
            _ => BondCouponStructure.NewFixed(
                GetOptionalDecimal(json, "couponRate") ?? 0m,
                ToOption(GetOptionalString(json, "dayCount")))
        };
        return new BondTerms(
            GetRequiredDateOnly(json, "maturity"),
            ToOption(GetOptionalDateOnly(json, "issueDate")),
            coupon,
            GetOptionalBoolean(json, "isCallable") ?? false,
            ToOption(GetOptionalDateOnly(json, "callDate")),
            ToOption(GetOptionalString(json, "issuerName")),
            ToOption(GetOptionalString(json, "seniority")),
            ParseBondSubclass(GetOptionalString(json, "subclass")),
            ToOption(GetOptionalDecimal(json, "par")),
            ToPaymentFrequencyOption(GetOptionalString(json, "paymentFrequency")),
            ToOption(GetOptionalDateOnly(json, "legalFinalMaturity")),
            ToOption(GetOptionalDateOnly(json, "preRefundDate")),
            ToOption(GetOptionalDateOnly(json, "mandatoryPutDate")),
            ToFSharpList(GetOptionalArrayItemsStrict(json, "principalSchedule").Select(ToPrincipalPaymentEntry)));
    }

    private static SwapLeg ToSwapLeg(JsonElement json)
        => new(
            GetRequiredString(json, "legType"),
            GetRequiredString(json, "currency"),
            ToOption(GetOptionalString(json, "index")),
            ToOption(GetOptionalDecimal(json, "fixedRate")));

    private static Covenant ToCovenant(JsonElement json)
        => new(
            GetRequiredString(json, "covenantType"),
            GetRequiredString(json, "threshold"),
            ToOption(GetOptionalString(json, "notes")));

    private static PrincipalPaymentEntry ToPrincipalPaymentEntry(JsonElement json)
        => new(
            GetRequiredDateOnly(json, "paymentDate"),
            GetRequiredDecimal(json, "amount"));

    private static StepCouponEntry ToStepCouponEntry(JsonElement json)
        => new(
            GetRequiredDateOnly(json, "effectiveDate"),
            GetRequiredDecimal(json, "rate"));

    private static FactorScheduleEntry ToFactorScheduleEntry(JsonElement json)
        => new(
            GetRequiredDateOnly(json, "asOfDate"),
            GetRequiredDecimal(json, "factor"));

    private static Provenance ToProvenance(string sourceSystem, string updatedBy, string? sourceRecordId, string? reason, DateTimeOffset asOf)
        => new(sourceSystem, ToOption(sourceRecordId), asOf, updatedBy, ToOption(reason));

    private static Provenance ToProvenance(JsonElement json)
        => new(
            GetRequiredString(json, "sourceSystem"),
            ToOption(GetOptionalString(json, "sourceRecordId")),
            GetRequiredDateTimeOffset(json, "asOf"),
            GetRequiredString(json, "updatedBy"),
            ToOption(GetOptionalString(json, "reason")));

    private static FSharpList<T> ToFSharpList<T>(IEnumerable<T> values)
        => ListModule.OfSeq(values);

    private static FSharpOption<ExerciseStyle> ParseExerciseStyle(string? value)
        => value?.Trim() switch
        {
            "American" => FSharpOption<ExerciseStyle>.Some(ExerciseStyle.American),
            "European" => FSharpOption<ExerciseStyle>.Some(ExerciseStyle.European),
            "Bermudan" => FSharpOption<ExerciseStyle>.Some(ExerciseStyle.Bermudan),
            _ => FSharpOption<ExerciseStyle>.None
        };

    private static FSharpOption<string> ToOption(string? value)
        => string.IsNullOrWhiteSpace(value) ? FSharpOption<string>.None : FSharpOption<string>.Some(value);

    private static FSharpOption<decimal> ToOption(decimal? value)
        => value.HasValue ? FSharpOption<decimal>.Some(value.Value) : FSharpOption<decimal>.None;

    private static FSharpOption<int> ToOption(int? value)
        => value.HasValue ? FSharpOption<int>.Some(value.Value) : FSharpOption<int>.None;

    private static FSharpOption<bool> ToOption(bool? value)
        => value.HasValue ? FSharpOption<bool>.Some(value.Value) : FSharpOption<bool>.None;

    private static FSharpOption<DistributionPolicy> ToDistributionPolicyOption(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? FSharpOption<DistributionPolicy>.None
            : FSharpOption<DistributionPolicy>.Some(value.Trim() switch
            {
                "Accumulating" => DistributionPolicy.Accumulating,
                "Distributing" => DistributionPolicy.Distributing,
                "Sweep" => DistributionPolicy.Sweep,
                var other => DistributionPolicy.NewOtherDistribution(other)
            });

    private static FSharpOption<DateOnly> ToOption(DateOnly? value)
        => value.HasValue ? FSharpOption<DateOnly>.Some(value.Value) : FSharpOption<DateOnly>.None;

    private static FSharpOption<DateTimeOffset> ToOption(DateTimeOffset? value)
        => value.HasValue ? FSharpOption<DateTimeOffset>.Some(value.Value) : FSharpOption<DateTimeOffset>.None;

    private static FSharpOption<PaymentFrequency> ToPaymentFrequencyOption(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? FSharpOption<PaymentFrequency>.None
            : FSharpOption<PaymentFrequency>.Some(value.Trim() switch
            {
                "Daily" => PaymentFrequency.Daily,
                "Weekly" => PaymentFrequency.Weekly,
                "Monthly" => PaymentFrequency.Monthly,
                "Quarterly" => PaymentFrequency.Quarterly,
                "SemiAnnual" => PaymentFrequency.SemiAnnual,
                "Annual" => PaymentFrequency.Annual,
                var other => PaymentFrequency.NewOtherFrequency(other)
            });

    private static JsonElement ParseJson(string json)
        => JsonDocument.Parse(json).RootElement.Clone();

    private static void EnsureSupportedAssetSchemaVersion(string assetClass, JsonElement json)
    {
        var schemaVersion = GetOptionalInt(json, "schemaVersion")
            ?? SecurityMasterSchemaVersions.DefaultAssetSpecificTerms;
        var isProfileBacked = IsProfileBackedAssetPayload(assetClass, json);
        if (SecurityMasterSchemaVersions.IsAcceptedAssetSpecificTermsVersion(schemaVersion, isProfileBacked))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported schemaVersion '{schemaVersion}' for asset class '{assetClass}'.");
    }

    private static bool IsProfileBackedAssetPayload(string assetClass, JsonElement json)
        => SupportsProfileBackedTerms(assetClass)
           && json.TryGetProperty("customProfileId", out var customProfileId)
           && customProfileId.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(customProfileId.GetString());

    private static bool SupportsProfileBackedTerms(string assetClass)
        => SecurityAssetClassCatalog.GetOrDefault(assetClass).SupportsProfileBackedTerms;

    private static JsonElement ResolveAssetTermsJson(JsonElement json)
        => json.TryGetProperty("profileFields", out var profileFields) && profileFields.ValueKind == JsonValueKind.Object
            ? profileFields
            : json;

    private static JsonElement GetRequiredArray(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidOperationException($"Missing required array '{propertyName}'.");

    private static IEnumerable<JsonElement> GetOptionalArrayItems(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in value.EnumerateArray())
        {
            yield return item;
        }
    }

    /// <summary>
    /// Like <see cref="GetOptionalArrayItems"/>, but a property that is PRESENT with the wrong JSON
    /// kind fails instead of reading as absent. Used for contractual schedules: silently treating a
    /// malformed <c>principalSchedule</c>/<c>factorScheduleEntries</c> as missing would let domain
    /// validation succeed and persist a snapshot that deleted the submitted schedule — projecting a
    /// sinker as a bullet — rather than rejecting the invalid terms.
    /// </summary>
    private static IEnumerable<JsonElement> GetOptionalArrayItemsStrict(JsonElement json, string propertyName)
    {
        if (json.TryGetProperty(propertyName, out var value)
            && value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined))
        {
            throw new InvalidOperationException(
                $"Property '{propertyName}' must be a JSON array when present, but was {value.ValueKind}.");
        }

        return GetOptionalArrayItems(json, propertyName);
    }

    private static string GetRequiredString(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidOperationException($"Missing required string '{propertyName}'.");

    private static string? GetOptionalString(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal GetRequiredDecimal(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.TryGetDecimal(out var decimalValue)
            ? decimalValue
            : throw new InvalidOperationException($"Missing required decimal '{propertyName}'.");

    private static decimal? GetOptionalDecimal(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetDecimal(out var decimalValue)
            ? decimalValue
            : null;

    private static int? GetOptionalInt(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var intValue)
            ? intValue
            : null;

    private static int GetRequiredInt(JsonElement json, string propertyName)
        => GetOptionalInt(json, propertyName)
           ?? throw new InvalidOperationException($"Missing required integer '{propertyName}'.");

    private static bool? GetOptionalBoolean(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? value.GetBoolean()
            : null;

    private static Guid GetRequiredGuid(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var guid)
            ? guid
            : throw new InvalidOperationException($"Missing required guid '{propertyName}'.");

    private static DateOnly GetRequiredDateOnly(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), out var date)
            ? date
            : throw new InvalidOperationException($"Missing required date '{propertyName}'.");

    private static DateOnly? GetOptionalDateOnly(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), out var date)
            ? date
            : null;

    private static DateTimeOffset GetRequiredDateTimeOffset(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var date)
            ? date
            : throw new InvalidOperationException($"Missing required timestamp '{propertyName}'.");

    private static IEnumerable<string> GetOptionalStringArray(JsonElement json, string propertyName)
    {
        if (json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    yield return item.GetString()!;
            }
        }
    }

    private static JsonElement? GetOptionalObject(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static JsonElement GetRequiredObject(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidOperationException($"Missing required object '{propertyName}'.");

    private static FSharpOption<Meridian.Contracts.Domain.Enums.InstrumentType> ToInstrumentTypeOption(int? raw)
        => raw.HasValue
            ? FSharpOption<Meridian.Contracts.Domain.Enums.InstrumentType>.Some((Meridian.Contracts.Domain.Enums.InstrumentType)raw.Value)
            : FSharpOption<Meridian.Contracts.Domain.Enums.InstrumentType>.None;

    private static FSharpOption<BondSubclass> ToBondSubclassOption(string? raw)
        => raw switch
        {
            "Corporate" => FSharpOption<BondSubclass>.Some(BondSubclass.Corporate),
            "Government" => FSharpOption<BondSubclass>.Some(BondSubclass.Sovereign),
            "Sovereign" => FSharpOption<BondSubclass>.Some(BondSubclass.Sovereign),
            "Municipal" => FSharpOption<BondSubclass>.Some(BondSubclass.Municipal),
            "Convertible" => FSharpOption<BondSubclass>.Some(BondSubclass.Convertible),
            "AssetBacked" => FSharpOption<BondSubclass>.Some(BondSubclass.AssetBacked),
            "MortgageBacked" => FSharpOption<BondSubclass>.Some(BondSubclass.MortgageBacked),
            not null => FSharpOption<BondSubclass>.Some(BondSubclass.NewOther(raw)),
            null => FSharpOption<BondSubclass>.None
        };

    private static FSharpOption<VotingRightsCat> ToVotingRightsCatOption(string? raw)
        => raw switch
        {
            "FullVoting" => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.FullVoting),
            "LimitedVoting" => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.LimitedVoting),
            "NonVoting" => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.NonVoting),
            "DualClass" => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.DualClass),
            "SuperVoting" => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.SuperVoting),
            not null => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.NewOtherVotingRights(raw)),
            null => FSharpOption<VotingRightsCat>.None
        };

    private static DividendType ToDividendType(string raw)
        => raw switch
        {
            "Fixed" => DividendType.Fixed,
            "Floating" => DividendType.Floating,
            "Cumulative" => DividendType.Cumulative,
            _ => throw new InvalidOperationException($"Unsupported dividend type '{raw}'.")
        };

    private static FSharpOption<ParticipationTerms> ToParticipationTermsOption(JsonElement? json)
        => json.HasValue
            ? FSharpOption<ParticipationTerms>.Some(new ParticipationTerms(
                GetOptionalBoolean(json.Value, "participatesInCommonDividends") ?? false,
                ToOption(GetOptionalDecimal(json.Value, "additionalDividendThreshold"))))
            : FSharpOption<ParticipationTerms>.None;

    private static LiquidationPreference ToLiquidationPreference(JsonElement json)
        => GetRequiredString(json, "kind") switch
        {
            "Pari" => LiquidationPreference.Pari,
            "Senior" => LiquidationPreference.NewSenior(GetRequiredDecimal(json, "multiple")),
            "Subordinated" => LiquidationPreference.Subordinated,
            var raw => throw new InvalidOperationException($"Unsupported liquidation preference '{raw}'.")
        };

    private static PreferredTerms ToPreferredTerms(JsonElement json)
        => new(
            ToOption(GetOptionalDecimal(json, "dividendRate")),
            ToDividendType(GetRequiredString(json, "dividendType")),
            ToOption(GetOptionalDecimal(json, "redemptionPrice")),
            ToOption(GetOptionalDateOnly(json, "redemptionDate")),
            ToOption(GetOptionalDateOnly(json, "callableDate")),
            ToParticipationTermsOption(GetOptionalObject(json, "participationTerms")),
            ToLiquidationPreference(GetRequiredObject(json, "liquidationPreference")));

    private static ConvertibleTerms ToConvertibleTerms(JsonElement json)
        => new(
            SecurityId.NewSecurityId(GetRequiredGuid(json, "underlyingSecurityId")),
            GetRequiredDecimal(json, "conversionRatio"),
            ToOption(GetOptionalDecimal(json, "conversionPrice")),
            ToOption(GetOptionalDateOnly(json, "conversionStartDate")),
            ToOption(GetOptionalDateOnly(json, "conversionEndDate")));

    private static FSharpOption<EquityClassification> ToEquityClassificationOption(
        JsonElement json, SecurityKindMappingMode mode)
    {
        var raw = GetOptionalString(json, "classification");
        return raw switch
        {
            "Common" => FSharpOption<EquityClassification>.Some(EquityClassification.Common),
            "Preferred" => FSharpOption<EquityClassification>.Some(
                EquityClassification.NewPreferred(ToPreferredTerms(GetRequiredObject(json, "preferredTerms")))),
            "Convertible" => FSharpOption<EquityClassification>.Some(
                EquityClassification.NewConvertible(ToConvertibleTerms(GetRequiredObject(json, "convertibleTerms")))),
            "ConvertiblePreferred" => FSharpOption<EquityClassification>.Some(
                EquityClassification.NewConvertiblePreferred(
                    ToPreferredTerms(GetRequiredObject(json, "preferredTerms")),
                    ToConvertibleTerms(GetRequiredObject(json, "convertibleTerms")))),
            // A write selecting "Other" must NAME the classification: defaulting a missing
            // otherClassification to the placeholder "Other" would persist an economically
            // meaningless label as the security's classification.
            "Other" when mode == SecurityKindMappingMode.Write =>
                GetOptionalString(json, "otherClassification") is { } named && !string.IsNullOrWhiteSpace(named)
                    ? FSharpOption<EquityClassification>.Some(EquityClassification.NewOther(named))
                    : throw new InvalidOperationException(
                        "An equity classification of 'Other' requires a non-empty 'otherClassification' naming the " +
                        "classification. Supply it, or use one of the declared classifications " +
                        "(Common, Preferred, Convertible, ConvertiblePreferred)."),
            "Other" => FSharpOption<EquityClassification>.Some(
                EquityClassification.NewOther(GetOptionalString(json, "otherClassification") ?? "Other")),
            null => FSharpOption<EquityClassification>.None,
            // A WRITE fails closed on an unrecognized discriminant: silently persisting a typo
            // ("Commmon") as Other(raw) would change the security's economic classification.
            _ when mode == SecurityKindMappingMode.Write =>
                throw new InvalidOperationException(
                    $"Unknown equity classification '{raw}'. Declared classifications are Common, Preferred, " +
                    "Convertible, and ConvertiblePreferred; use 'Other' with 'otherClassification' for anything else."),
            // Read tolerance: rows written before the serializer emitted the "Other" discriminant
            // carry the raw label in the classification slot. Treat any unrecognized value as an
            // Other classification instead of failing every read of the row.
            _ => FSharpOption<EquityClassification>.Some(EquityClassification.NewOther(raw))
        };
    }

    public static JsonElement BuildPreferredEquityTermsPatch(SecurityProjectionRecord current, AmendPreferredEquityTermsRequest request)
    {
        if (!string.Equals(current.AssetClass, "Equity", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Security '{current.SecurityId}' is not an equity and cannot accept preferred term amendments.");

        var assetSpecificNode = JsonNode.Parse(current.AssetSpecificTerms.GetRawText()) as JsonObject
            ?? throw new InvalidOperationException("Current asset-specific terms payload is not a JSON object.");
        var classification = assetSpecificNode["classification"]?.GetValue<string>();

        if (classification is not ("Preferred" or "ConvertiblePreferred"))
            throw new InvalidOperationException($"Security '{current.SecurityId}' does not currently have preferred-equity terms.");

        var existingJson = (assetSpecificNode["preferredTerms"] as JsonObject)?.ToJsonString();
        var preferredTermsNode = (existingJson is not null
            ? JsonNode.Parse(existingJson) as JsonObject
            : null) ?? new JsonObject();

        if (request.DividendRate is not null)
            preferredTermsNode["dividendRate"] = JsonValue.Create(request.DividendRate);
        if (request.DividendType is not null)
            preferredTermsNode["dividendType"] = request.DividendType;
        if (request.RedemptionPrice is not null)
            preferredTermsNode["redemptionPrice"] = JsonValue.Create(request.RedemptionPrice);
        if (request.RedemptionDate is not null)
            preferredTermsNode["redemptionDate"] = request.RedemptionDate.Value.ToString("yyyy-MM-dd");
        if (request.CallableDate is not null)
            preferredTermsNode["callableDate"] = request.CallableDate.Value.ToString("yyyy-MM-dd");

        if (request.ParticipatesInCommonDividends is not null || request.AdditionalDividendThreshold is not null)
        {
            var existingPart = (preferredTermsNode["participationTerms"] as JsonObject)?.ToJsonString();
            var participationNode = (existingPart is not null
                ? JsonNode.Parse(existingPart) as JsonObject
                : null) ?? new JsonObject();
            if (request.ParticipatesInCommonDividends is not null)
                participationNode["participatesInCommonDividends"] = JsonValue.Create(request.ParticipatesInCommonDividends.Value);
            if (request.AdditionalDividendThreshold is not null)
                participationNode["additionalDividendThreshold"] = JsonValue.Create(request.AdditionalDividendThreshold);
            preferredTermsNode["participationTerms"] = participationNode;
        }

        if (request.LiquidationPreferenceKind is not null)
        {
            var existingLiq = (preferredTermsNode["liquidationPreference"] as JsonObject)?.ToJsonString();
            var liquidationNode = (existingLiq is not null
                ? JsonNode.Parse(existingLiq) as JsonObject
                : null) ?? new JsonObject();
            liquidationNode["kind"] = request.LiquidationPreferenceKind;
            if (request.LiquidationPreferenceMultiple is not null)
                liquidationNode["multiple"] = JsonValue.Create(request.LiquidationPreferenceMultiple);
            preferredTermsNode["liquidationPreference"] = liquidationNode;
        }

        assetSpecificNode["preferredTerms"] = preferredTermsNode;
        return JsonSerializer.SerializeToElement(assetSpecificNode);
    }


    public static JsonElement BuildConvertibleEquityTermsPatch(SecurityProjectionRecord current, AmendConvertibleEquityTermsRequest request)
    {
        if (!string.Equals(current.AssetClass, "Equity", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Security '{current.SecurityId}' is not an equity and cannot accept convertible term amendments.");

        var assetSpecificNode = JsonNode.Parse(current.AssetSpecificTerms.GetRawText()) as JsonObject
            ?? throw new InvalidOperationException("Current asset-specific terms payload is not a JSON object.");
        var classification = assetSpecificNode["classification"]?.GetValue<string>();

        if (classification is not ("Convertible" or "ConvertiblePreferred"))
            throw new InvalidOperationException($"Security '{current.SecurityId}' does not currently have convertible-equity terms.");

        var convertibleTermsNode = new JsonObject
        {
            ["underlyingSecurityId"] = request.UnderlyingSecurityId,
            ["conversionRatio"] = JsonValue.Create(request.ConversionRatio),
            ["conversionPrice"] = JsonValue.Create(request.ConversionPrice),
            ["conversionStartDate"] = JsonValue.Create(request.ConversionStartDate),
            ["conversionEndDate"] = JsonValue.Create(request.ConversionEndDate)
        };

        assetSpecificNode["convertibleTerms"] = convertibleTermsNode;
        return JsonSerializer.SerializeToElement(assetSpecificNode);
    }
}
