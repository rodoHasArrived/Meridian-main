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
            ToSecurityKind(request.AssetClass, request.AssetSpecificTerms),
            request.EffectiveFrom,
            ToProvenance(request.SourceSystem, request.UpdatedBy, request.SourceRecordId, request.Reason, request.EffectiveFrom));

    public static AmendTerms ToAmendCommand(AmendSecurityTermsRequest request, SecurityProjectionRecord current)
        => new(
            SecurityId.NewSecurityId(request.SecurityId),
            request.ExpectedVersion,
            request.CommonTerms is JsonElement common ? FSharpOption<CommonTerms>.Some(ToCommonTerms(common)) : FSharpOption<CommonTerms>.None,
            request.AssetSpecificTermsPatch is JsonElement assetSpecific
                ? FSharpOption<SecurityKind>.Some(ToSecurityKind(current.AssetClass, assetSpecific))
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
            projection.Identifiers,
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
                ["schemaVersion"] = 2,
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

    private static Identifier ToIdentifier(SecurityIdentifierDto identifier)
        => new(
            ToIdentifierKind(identifier.Kind, identifier.Provider),
            identifier.Value,
            identifier.IsPrimary,
            identifier.ValidFrom,
            ToOption(identifier.ValidTo));

    private static IdentifierKind ToIdentifierKind(SecurityIdentifierKind kind, string? provider)
        => kind switch
        {
            SecurityIdentifierKind.Ticker => IdentifierKind.Ticker,
            SecurityIdentifierKind.Isin => IdentifierKind.Isin,
            SecurityIdentifierKind.Cusip => IdentifierKind.Cusip,
            SecurityIdentifierKind.Sedol => IdentifierKind.Sedol,
            SecurityIdentifierKind.Figi => IdentifierKind.Figi,
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
        => new(
            Enum.Parse<SecurityIdentifierKind>(identifier.Kind, ignoreCase: true),
            identifier.Value,
            identifier.IsPrimary,
            identifier.ValidFrom,
            identifier.ValidTo.HasValue ? identifier.ValidTo.Value : null,
            string.IsNullOrWhiteSpace(identifier.Provider) ? null : identifier.Provider);

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

    private static SecurityKind ToSecurityKind(string assetClass, JsonElement json)
    {
        EnsureSupportedAssetSchemaVersion(assetClass, json);

        return assetClass switch
        {
            "Equity" => SecurityKind.NewEquity(new EquityTerms(
                ToOption(GetOptionalString(json, "shareClass")),
                ToVotingRightsCatOption(GetOptionalString(json, "votingRightsCat")),
                ToEquityClassificationOption(json))),
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
            "Bond" => SecurityKind.NewBond(ToBondTerms(json)),
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
            "Swap" => SecurityKind.NewSwap(new SwapTerms(
                GetRequiredDateOnly(json, "effectiveDate"),
                GetRequiredDateOnly(json, "maturityDate"),
                ToFSharpList(GetRequiredArray(json, "legs").EnumerateArray().Select(ToSwapLeg)))),
            "DirectLoan" => SecurityKind.NewDirectLoan(new DirectLoanTerms(
                GetRequiredString(json, "borrower"),
                ToOption(GetOptionalDateOnly(json, "maturity")),
                ToFSharpList(GetRequiredArray(json, "covenants").EnumerateArray().Select(ToCovenant)))),
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
            _ => throw new InvalidOperationException($"Unsupported asset class '{assetClass}'.")
        };
    }

    private static BondSubclass ParseBondSubclass(string? subclass) => subclass switch
    {
        "Sovereign"          => BondSubclass.Sovereign,
        "Municipal"          => BondSubclass.Municipal,
        "Agency"             => BondSubclass.Agency,
        "Convertible"        => BondSubclass.Convertible,
        "InflationLinked"    => BondSubclass.InflationLinked,
        "FloatingRate"       => BondSubclass.FloatingRate,
        "AssetBacked"        => BondSubclass.AssetBacked,
        "MortgageBacked"     => BondSubclass.MortgageBacked,
        "AgencyMbs"          => BondSubclass.AgencyMbs,
        "CommercialMbs"      => BondSubclass.CommercialMbs,
        "Cmo"                => BondSubclass.Cmo,
        "Clo"                => BondSubclass.Clo,
        "Cdo"                => BondSubclass.Cdo,
        "PrincipalOnly"      => BondSubclass.PrincipalOnly,
        "InterestOnly"       => BondSubclass.InterestOnly,
        "InverseInterestOnly"=> BondSubclass.InverseInterestOnly,
        "Corporate"          => BondSubclass.Corporate,
        null or ""           => BondSubclass.Corporate,
        var other            => BondSubclass.NewOther(other)
    };

    private static BondTerms ToBondTerms(JsonElement json)
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
            ParseBondSubclass(GetOptionalString(json, "subclass")));
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

    private static FSharpOption<DateOnly> ToOption(DateOnly? value)
        => value.HasValue ? FSharpOption<DateOnly>.Some(value.Value) : FSharpOption<DateOnly>.None;

    private static FSharpOption<DateTimeOffset> ToOption(DateTimeOffset? value)
        => value.HasValue ? FSharpOption<DateTimeOffset>.Some(value.Value) : FSharpOption<DateTimeOffset>.None;

    private static JsonElement ParseJson(string json)
        => JsonDocument.Parse(json).RootElement.Clone();

    private static void EnsureSupportedAssetSchemaVersion(string assetClass, JsonElement json)
    {
        var schemaVersion = GetOptionalInt(json, "schemaVersion") ?? 1;
        if (schemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported schemaVersion '{schemaVersion}' for asset class '{assetClass}'.");
        }
    }

    private static JsonElement GetRequiredArray(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidOperationException($"Missing required array '{propertyName}'.");

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
            "Corporate"     => FSharpOption<BondSubclass>.Some(BondSubclass.Corporate),
            "Government"    => FSharpOption<BondSubclass>.Some(BondSubclass.Sovereign),
            "Sovereign"     => FSharpOption<BondSubclass>.Some(BondSubclass.Sovereign),
            "Municipal"     => FSharpOption<BondSubclass>.Some(BondSubclass.Municipal),
            "Convertible"   => FSharpOption<BondSubclass>.Some(BondSubclass.Convertible),
            "AssetBacked"   => FSharpOption<BondSubclass>.Some(BondSubclass.AssetBacked),
            "MortgageBacked"=> FSharpOption<BondSubclass>.Some(BondSubclass.MortgageBacked),
            not null        => FSharpOption<BondSubclass>.Some(BondSubclass.NewOther(raw)),
            null            => FSharpOption<BondSubclass>.None
        };

    private static FSharpOption<VotingRightsCat> ToVotingRightsCatOption(string? raw)
        => raw switch
        {
            "FullVoting"    => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.FullVoting),
            "LimitedVoting" => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.LimitedVoting),
            "NonVoting"     => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.NonVoting),
            "DualClass"     => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.DualClass),
            "SuperVoting"   => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.SuperVoting),
            not null        => FSharpOption<VotingRightsCat>.Some(VotingRightsCat.NewOtherVotingRights(raw)),
            null            => FSharpOption<VotingRightsCat>.None
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

    private static FSharpOption<EquityClassification> ToEquityClassificationOption(JsonElement json)
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
            "Other" => FSharpOption<EquityClassification>.Some(
                EquityClassification.NewOther(GetRequiredString(json, "otherClassification"))),
            null => FSharpOption<EquityClassification>.None,
            _ => throw new InvalidOperationException($"Unsupported equity classification '{raw}'.")
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
