namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// The JSON value shape a Security Master asset-specific-term field takes in the flat
/// (<see cref="AssetSpecificTermsSchema.Legacy"/>) payload. This is the codec-level type, distinct
/// from the operator-facing <see cref="SecurityAssetProfileFieldTypeDto"/> used by custom profiles.
/// </summary>
public enum SecurityAssetTermFieldType
{
    /// <summary>A JSON string.</summary>
    String,

    /// <summary>A JSON number read as <see cref="decimal"/>.</summary>
    Decimal,

    /// <summary>A JSON number read as <see cref="int"/>.</summary>
    Integer,

    /// <summary>A JSON boolean.</summary>
    Boolean,

    /// <summary>A JSON string parsed as a <see cref="System.DateOnly"/> (or an ISO timestamp).</summary>
    Date,

    /// <summary>A JSON string parsed as a <see cref="System.Guid"/>.</summary>
    Guid,

    /// <summary>A JSON array.</summary>
    Array,

    /// <summary>A nested JSON object.</summary>
    Object
}

/// <summary>
/// One declarative field in the per-asset-class asset-specific-terms contract: the canonical JSON
/// key, its value type, whether the serialize side always emits it, and any legacy flat key aliases
/// a tolerant reader should also accept.
/// </summary>
public sealed record SecurityAssetTermField(
    string Key,
    SecurityAssetTermFieldType Type,
    bool Required,
    IReadOnlyList<string> Aliases)
{
    /// <summary>A field the serialize side always emits (a non-optional domain field).</summary>
    public static SecurityAssetTermField Req(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => new(key, type, Required: true, aliases);

    /// <summary>A field emitted only when its optional domain value is present.</summary>
    public static SecurityAssetTermField Opt(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => new(key, type, Required: false, aliases);
}

/// <summary>
/// The single declarative source of truth for the flat, per-asset-class asset-specific-terms field
/// contract. Historically the same field/type table was hand-maintained three times — the F# serialize
/// side (<c>Interop.SecurityMaster.assetSpecificTermsJson</c>), the C# deserialize side
/// (<c>SecurityMasterMapping.ToSecurityKind</c>), and the relational projection decoders in the Postgres
/// store — which let them silently drift (e.g. the projection store reading a nested <c>coupon</c> object
/// the serializer never wrote, so bond coupon columns landed null). This table names each class's fields
/// once so those codec surfaces can be validated against a single contract instead of against each other.
/// It mirrors the data-driven pattern already proven by <c>AssetClassValidatorRegistry</c>.
/// </summary>
/// <remarks>
/// Keys and types are taken from the authoritative serialize contract (the F# <c>SecurityKind</c>
/// term records). Fields carrying nested/collection shapes are typed <see cref="SecurityAssetTermFieldType.Array"/>
/// or <see cref="SecurityAssetTermFieldType.Object"/>; their inner shapes are not enumerated here.
/// </remarks>
public static class SecurityAssetTermsSchema
{
    private static SecurityAssetTermField Req(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => SecurityAssetTermField.Req(key, type, aliases);

    private static SecurityAssetTermField Opt(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => SecurityAssetTermField.Opt(key, type, aliases);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SecurityAssetTermField>> FieldsByAssetClass =
        new Dictionary<string, IReadOnlyList<SecurityAssetTermField>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Equity"] =
            [
                Opt("shareClass", SecurityAssetTermFieldType.String),
                Opt("votingRightsCat", SecurityAssetTermFieldType.String),
                Opt("classification", SecurityAssetTermFieldType.String),
                // Raw label carried alongside classification="Other"; the discriminant stays a
                // closed vocabulary while free-text classifications round-trip losslessly.
                Opt("otherClassification", SecurityAssetTermFieldType.String),
                Opt("preferredTerms", SecurityAssetTermFieldType.Object),
                Opt("convertibleTerms", SecurityAssetTermFieldType.Object)
            ],
            ["Option"] =
            [
                Req("underlyingId", SecurityAssetTermFieldType.Guid),
                Req("putCall", SecurityAssetTermFieldType.String),
                Req("strike", SecurityAssetTermFieldType.Decimal),
                Req("expiry", SecurityAssetTermFieldType.Date),
                Req("multiplier", SecurityAssetTermFieldType.Decimal),
                Opt("optChainId", SecurityAssetTermFieldType.String),
                Opt("exerciseStyle", SecurityAssetTermFieldType.String),
                Opt("settlementType", SecurityAssetTermFieldType.String),
                Req("isAdjusted", SecurityAssetTermFieldType.Boolean),
                Opt("lastTradingDt", SecurityAssetTermFieldType.Date)
            ],
            ["Future"] =
            [
                Req("rootSymbol", SecurityAssetTermFieldType.String),
                Req("contractMonth", SecurityAssetTermFieldType.String),
                Req("expiry", SecurityAssetTermFieldType.Date),
                Req("multiplier", SecurityAssetTermFieldType.Decimal),
                Opt("lastTradingDt", SecurityAssetTermFieldType.Date),
                Opt("firstNoticeDt", SecurityAssetTermFieldType.Date),
                Opt("deliveryMonthDt", SecurityAssetTermFieldType.Date),
                Opt("settlementType", SecurityAssetTermFieldType.String),
                Opt("deliveryLocationCode", SecurityAssetTermFieldType.String),
                Req("isRollTarget", SecurityAssetTermFieldType.Boolean),
                Opt("rollWindowDays", SecurityAssetTermFieldType.Integer)
            ],
            ["Bond"] =
            [
                Req("maturity", SecurityAssetTermFieldType.Date),
                Opt("issueDate", SecurityAssetTermFieldType.Date),
                // couponType/couponRate/floatingIndex/spreadBps/dayCount are emitted flat by the
                // serializer; only the projection store and the v2 upcaster read a nested "coupon".
                Opt("couponType", SecurityAssetTermFieldType.String),
                Opt("couponRate", SecurityAssetTermFieldType.Decimal),
                Opt("floatingIndex", SecurityAssetTermFieldType.String),
                Opt("spreadBps", SecurityAssetTermFieldType.Decimal),
                Opt("capRate", SecurityAssetTermFieldType.Decimal),
                Opt("floorRate", SecurityAssetTermFieldType.Decimal),
                // Step-coupon schedule ({effectiveDate, rate}); empty for non-step coupons. The
                // inflation fields carry linker indexation (couponType = "InflationLinked"); null
                // otherwise. These are what make StepRate/FixedToFloat/InflationLinked subclasses
                // computable rather than labels.
                Opt("stepSchedule", SecurityAssetTermFieldType.Array),
                Opt("inflationIndex", SecurityAssetTermFieldType.String),
                Opt("inflationBaseIndexValue", SecurityAssetTermFieldType.Decimal),
                Opt("inflationIndexRatio", SecurityAssetTermFieldType.Decimal),
                Opt("dayCount", SecurityAssetTermFieldType.String, "dayCountConvention"),
                Req("isCallable", SecurityAssetTermFieldType.Boolean),
                Opt("callDate", SecurityAssetTermFieldType.Date),
                Opt("issuerName", SecurityAssetTermFieldType.String),
                Opt("seniority", SecurityAssetTermFieldType.String),
                Req("subclass", SecurityAssetTermFieldType.String),
                Opt("par", SecurityAssetTermFieldType.Decimal),
                Opt("paymentFrequency", SecurityAssetTermFieldType.String),
                Opt("legalFinalMaturity", SecurityAssetTermFieldType.Date),
                Opt("preRefundDate", SecurityAssetTermFieldType.Date),
                Opt("mandatoryPutDate", SecurityAssetTermFieldType.Date),
                // Contractual principal instalments ({paymentDate, amount}) for sinking-fund and
                // other scheduled-principal subclasses; empty for bullet bonds.
                Opt("principalSchedule", SecurityAssetTermFieldType.Array)
            ],
            ["FxSpot"] =
            [
                Req("baseCurrency", SecurityAssetTermFieldType.String),
                Req("quoteCurrency", SecurityAssetTermFieldType.String)
            ],
            ["Deposit"] =
            [
                Req("depositType", SecurityAssetTermFieldType.String),
                Req("institutionName", SecurityAssetTermFieldType.String),
                Opt("maturity", SecurityAssetTermFieldType.Date),
                Opt("interestRate", SecurityAssetTermFieldType.Decimal),
                Opt("dayCount", SecurityAssetTermFieldType.String),
                Req("isCallable", SecurityAssetTermFieldType.Boolean)
            ],
            ["MoneyMarketFund"] =
            [
                Opt("fundFamily", SecurityAssetTermFieldType.String),
                Req("sweepEligible", SecurityAssetTermFieldType.Boolean),
                Opt("weightedAverageMaturityDays", SecurityAssetTermFieldType.Integer),
                Req("liquidityFeeEligible", SecurityAssetTermFieldType.Boolean)
            ],
            ["CertificateOfDeposit"] =
            [
                Req("issuerName", SecurityAssetTermFieldType.String),
                Req("maturity", SecurityAssetTermFieldType.Date),
                Opt("couponRate", SecurityAssetTermFieldType.Decimal),
                Opt("callableDate", SecurityAssetTermFieldType.Date),
                Opt("dayCount", SecurityAssetTermFieldType.String)
            ],
            ["CommercialPaper"] =
            [
                Req("issuerName", SecurityAssetTermFieldType.String),
                Req("maturity", SecurityAssetTermFieldType.Date),
                Opt("discountRate", SecurityAssetTermFieldType.Decimal),
                Opt("dayCount", SecurityAssetTermFieldType.String),
                Req("isAssetBacked", SecurityAssetTermFieldType.Boolean)
            ],
            ["TreasuryBill"] =
            [
                Req("maturity", SecurityAssetTermFieldType.Date),
                Opt("auctionDate", SecurityAssetTermFieldType.Date),
                Opt("cusip", SecurityAssetTermFieldType.String),
                Opt("discountRate", SecurityAssetTermFieldType.Decimal)
            ],
            ["Repo"] =
            [
                Req("counterparty", SecurityAssetTermFieldType.String),
                Req("startDate", SecurityAssetTermFieldType.Date),
                Req("endDate", SecurityAssetTermFieldType.Date),
                Opt("repoRate", SecurityAssetTermFieldType.Decimal),
                Opt("collateralType", SecurityAssetTermFieldType.String),
                Opt("haircut", SecurityAssetTermFieldType.Decimal)
            ],
            ["CashSweep"] =
            [
                Req("programName", SecurityAssetTermFieldType.String),
                Req("sweepVehicleType", SecurityAssetTermFieldType.String),
                Opt("sweepFrequency", SecurityAssetTermFieldType.String),
                Opt("targetAccountType", SecurityAssetTermFieldType.String),
                Opt("yieldRate", SecurityAssetTermFieldType.Decimal)
            ],
            ["OtherSecurity"] =
            [
                Req("category", SecurityAssetTermFieldType.String),
                Opt("subType", SecurityAssetTermFieldType.String),
                Opt("maturity", SecurityAssetTermFieldType.Date),
                Opt("issuerName", SecurityAssetTermFieldType.String),
                Opt("settlementType", SecurityAssetTermFieldType.String)
            ],
            ["CustomAsset"] =
            [
                // Profile-backed custom assets carry the profile envelope; the class-specific fields
                // are dynamic and live under profileFields, governed by the approved profile version.
                Req("customProfileId", SecurityAssetTermFieldType.String),
                Req("profileVersion", SecurityAssetTermFieldType.Integer),
                Req("profileFields", SecurityAssetTermFieldType.Object),
                Opt("profileApproval", SecurityAssetTermFieldType.Object),
                // The workstation create path also stamps a display category/sub-type and evidence
                // links onto the document; they are part of the real persisted contract.
                Opt("category", SecurityAssetTermFieldType.String),
                Opt("subType", SecurityAssetTermFieldType.String),
                Opt("evidenceLinks", SecurityAssetTermFieldType.Array)
            ],
            ["Swap"] =
            [
                Req("effectiveDate", SecurityAssetTermFieldType.Date),
                Req("maturityDate", SecurityAssetTermFieldType.Date),
                Req("legs", SecurityAssetTermFieldType.Array)
            ],
            ["DirectLoan"] =
            [
                Req("borrower", SecurityAssetTermFieldType.String),
                Opt("maturity", SecurityAssetTermFieldType.Date),
                Opt("referenceIndex", SecurityAssetTermFieldType.String),
                Opt("spreadBps", SecurityAssetTermFieldType.Decimal),
                Opt("currentCouponRate", SecurityAssetTermFieldType.Decimal),
                Opt("resetFrequency", SecurityAssetTermFieldType.String),
                Opt("pricingSource", SecurityAssetTermFieldType.String),
                Req("covenants", SecurityAssetTermFieldType.Array),
                Req("principalSchedule", SecurityAssetTermFieldType.Array)
            ],
            ["StructuredCredit"] =
            [
                Req("tranche", SecurityAssetTermFieldType.String),
                Opt("poolId", SecurityAssetTermFieldType.String),
                Req("collateralType", SecurityAssetTermFieldType.String),
                Req("originalFace", SecurityAssetTermFieldType.Decimal),
                Opt("currentFactor", SecurityAssetTermFieldType.Decimal),
                Req("couponOrIndex", SecurityAssetTermFieldType.String),
                Opt("factorSchedule", SecurityAssetTermFieldType.String),
                // Typed, dated factor points ({asOfDate, factor}) consumed by the structured
                // cash-flow resolver's FactorAsOf lookup; factorSchedule stays the free-text
                // legacy reference.
                Opt("factorScheduleEntries", SecurityAssetTermFieldType.Array),
                // Legal final maturity of the tranche: the anchor date for calculated cash-flow
                // projection — without it the factor schedule has no production effect.
                Opt("maturity", SecurityAssetTermFieldType.Date)
            ],
            ["PrivateFundInterest"] =
            [
                Req("gpSponsor", SecurityAssetTermFieldType.String),
                Req("strategy", SecurityAssetTermFieldType.String),
                Req("vintage", SecurityAssetTermFieldType.Integer),
                Req("commitment", SecurityAssetTermFieldType.Decimal),
                Opt("fundedAmount", SecurityAssetTermFieldType.Decimal),
                Opt("unfundedAmount", SecurityAssetTermFieldType.Decimal),
                Req("navDate", SecurityAssetTermFieldType.Date),
                Opt("lockup", SecurityAssetTermFieldType.String)
            ],
            ["PrivateCompanyEquity"] =
            [
                Req("issuer", SecurityAssetTermFieldType.String),
                Req("shareClass", SecurityAssetTermFieldType.String),
                Req("round", SecurityAssetTermFieldType.String),
                Opt("ownershipPercent", SecurityAssetTermFieldType.Decimal),
                Req("costBasis", SecurityAssetTermFieldType.Decimal),
                Opt("latestValuation", SecurityAssetTermFieldType.Decimal),
                Opt("transferRestrictions", SecurityAssetTermFieldType.String)
            ],
            ["RealEstateHolding"] =
            [
                Req("propertyType", SecurityAssetTermFieldType.String),
                Req("addressOrMarket", SecurityAssetTermFieldType.String),
                Req("ownershipPercent", SecurityAssetTermFieldType.Decimal),
                Req("appraisalValue", SecurityAssetTermFieldType.Decimal),
                Req("valuationDate", SecurityAssetTermFieldType.Date),
                Opt("debtStack", SecurityAssetTermFieldType.String),
                Opt("sponsor", SecurityAssetTermFieldType.String)
            ],
            ["CommitmentGuarantee"] =
            [
                Req("counterparty", SecurityAssetTermFieldType.String),
                Opt("beneficiary", SecurityAssetTermFieldType.String),
                Req("committedAmount", SecurityAssetTermFieldType.Decimal),
                Opt("unfundedAmount", SecurityAssetTermFieldType.Decimal),
                Req("effectiveDate", SecurityAssetTermFieldType.Date),
                Opt("expiryDate", SecurityAssetTermFieldType.Date),
                Opt("feeRate", SecurityAssetTermFieldType.Decimal),
                Opt("collateral", SecurityAssetTermFieldType.String),
                Req("covenants", SecurityAssetTermFieldType.Array)
            ],
            ["Commodity"] =
            [
                Req("commodityType", SecurityAssetTermFieldType.String),
                Opt("denomination", SecurityAssetTermFieldType.String),
                Opt("contractSize", SecurityAssetTermFieldType.Decimal)
            ],
            ["CryptoCurrency"] =
            [
                Req("baseCurrency", SecurityAssetTermFieldType.String),
                Req("quoteCurrency", SecurityAssetTermFieldType.String),
                Opt("network", SecurityAssetTermFieldType.String)
            ],
            ["Cfd"] =
            [
                Req("underlyingAssetClass", SecurityAssetTermFieldType.String),
                Opt("underlyingDescription", SecurityAssetTermFieldType.String),
                Opt("leverage", SecurityAssetTermFieldType.Decimal)
            ],
            ["Warrant"] =
            [
                Req("underlyingId", SecurityAssetTermFieldType.Guid),
                Req("warrantType", SecurityAssetTermFieldType.String),
                Opt("strike", SecurityAssetTermFieldType.Decimal),
                Opt("expiry", SecurityAssetTermFieldType.Date),
                Opt("multiplier", SecurityAssetTermFieldType.Decimal)
            ],
            ["InvestmentFund"] =
            [
                Opt("fundType", SecurityAssetTermFieldType.String),
                Opt("fundFamily", SecurityAssetTermFieldType.String),
                Opt("navCurrency", SecurityAssetTermFieldType.String),
                Opt("distributionPolicy", SecurityAssetTermFieldType.String),
                Opt("isStableNav", SecurityAssetTermFieldType.Boolean),
                Opt("pricingSource", SecurityAssetTermFieldType.String)
            ]
        };

    /// <summary>The asset classes with a declared terms schema.</summary>
    public static IReadOnlyCollection<string> AssetClasses { get; } = FieldsByAssetClass.Keys.ToArray();

    /// <summary>
    /// The closed value vocabularies for declared term fields whose codec maps the string onto a
    /// closed switch or discriminated union with NO raw-carrying case, so a value outside the set
    /// cannot survive a deserialize/re-serialize pass — it is silently replaced by a different,
    /// canonical value and the original is gone.
    /// <para>Membership is EXACT-CASE, matching the ordinal switches in
    /// <c>SecurityMasterMapping</c>. <c>"floating"</c> is not a near-miss those readers forgive; it
    /// is a value they rewrite to <c>Fixed</c>. Accepting it here and persisting it verbatim (the
    /// field-edit path stages the operator's value unchanged) would stage exactly the corruption
    /// this table exists to stop, so the vocabulary must not be matched case-insensitively.</para>
    /// <para>Fields whose union carries the raw label — <c>subclass</c>, <c>paymentFrequency</c>,
    /// <c>distributionPolicy</c>, <c>votingRightsCat</c> — round-trip losslessly through their
    /// <c>Other</c> case and are deliberately absent: constraining them would reject the free
    /// vendor taxonomy those cases exist to preserve. Plain-string fields (<c>putCall</c>,
    /// <c>settlementType</c>, <c>depositType</c>, <c>collateralType</c>, <c>warrantType</c>,
    /// <c>fundType</c>, <c>sweepVehicleType</c>) are carried verbatim into the domain and are
    /// likewise not codec-constrained; where they need a governed vocabulary it is a validation
    /// rule (<c>AssetClassValidatorRegistry</c>), not a round-trip requirement.</para>
    /// <para>Equity <c>classification</c> is listed for the operator-facing edit surface only. Its
    /// write refusal lives in <c>SecurityMasterMapping.ToEquityClassificationOption</c>, which pairs
    /// the discriminant with the <c>otherClassification</c> rule this flat table cannot express.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> ClosedVocabulariesByAssetClass =
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Equity"] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["classification"] = ["Common", "Preferred", "Convertible", "ConvertiblePreferred", "Other"]
            },
            ["Option"] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["exerciseStyle"] = ["American", "European", "Bermudan"]
            },
            ["Bond"] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["couponType"] = ["Fixed", "Floating", "ZeroCoupon", "Step", "InflationLinked"]
            }
        };

    /// <summary>
    /// Tries to resolve the closed value vocabulary for a declared term field. The key is matched
    /// case-insensitively (as elsewhere in this table); the VALUES it yields are exact-case and must
    /// be compared ordinally.
    /// </summary>
    public static bool TryGetAllowedValues(string assetClass, string key, out IReadOnlyList<string> allowedValues)
    {
        if (ClosedVocabulariesByAssetClass.TryGetValue(assetClass, out var vocabularies)
            && vocabularies.TryGetValue(key, out var values))
        {
            allowedValues = values;
            return true;
        }

        allowedValues = [];
        return false;
    }

    /// <summary>
    /// The closed value vocabulary for <paramref name="key"/> in <paramref name="assetClass"/>, or
    /// an empty list when the field accepts free text.
    /// </summary>
    public static IReadOnlyList<string> AllowedValues(string assetClass, string key)
        => TryGetAllowedValues(assetClass, key, out var allowedValues) ? allowedValues : [];

    /// <summary>
    /// True when <paramref name="value"/> is acceptable for <paramref name="key"/>: either the field
    /// has no closed vocabulary, or the value is an exact-case member of it. A null or blank value
    /// is acceptable — it asserts no discriminant.
    /// </summary>
    public static bool IsAllowedValue(string assetClass, string key, string? value)
        => string.IsNullOrWhiteSpace(value)
            || !TryGetAllowedValues(assetClass, key, out var allowedValues)
            || allowedValues.Contains(value, StringComparer.Ordinal);

    /// <summary>The declared fields for <paramref name="assetClass"/>, or an empty list when none is declared.</summary>
    public static IReadOnlyList<SecurityAssetTermField> Fields(string assetClass)
        => FieldsByAssetClass.TryGetValue(assetClass, out var fields) ? fields : [];

    /// <summary>Tries to resolve the declared fields for <paramref name="assetClass"/>.</summary>
    public static bool TryGetFields(string assetClass, out IReadOnlyList<SecurityAssetTermField> fields)
        => FieldsByAssetClass.TryGetValue(assetClass, out fields!);

    /// <summary>The declared field for <paramref name="key"/> in <paramref name="assetClass"/>, or <see langword="null"/>.</summary>
    public static SecurityAssetTermField? Field(string assetClass, string key)
    {
        if (!FieldsByAssetClass.TryGetValue(assetClass, out var fields))
        {
            return null;
        }

        foreach (var field in fields)
        {
            if (string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        return null;
    }
}
