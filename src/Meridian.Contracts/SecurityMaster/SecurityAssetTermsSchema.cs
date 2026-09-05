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
/// The designated vocabulary member that absorbs a value outside the declared vocabulary:
/// <c>classification = "Other"</c> pairs with a <see cref="LabelKey"/> (<c>otherClassification</c>)
/// carrying the raw label verbatim, so an unrecognized value survives the codec loop under a
/// canonical discriminant instead of vanishing.
/// <para>The escape is lossless only while the record carries none of the
/// <see cref="DependentKeys"/>. Those are payload the IN-vocabulary cases own — an equity's
/// <c>preferredTerms</c> hangs off <c>classification = "Preferred"</c> — and the escape decode has
/// nowhere to reattach them, so it would silently delete them.</para>
/// </summary>
public sealed record SecurityAssetTermVocabularyEscape(
    string Value,
    string LabelKey,
    IReadOnlyList<string> DependentKeys);

/// <summary>
/// One declarative field in the per-asset-class asset-specific-terms contract: the canonical JSON
/// key, its value type, whether the serialize side always emits it, any legacy flat key aliases
/// a tolerant reader should also accept, and — for discriminant strings — the closed vocabulary its
/// value must come from.
/// </summary>
/// <remarks>
/// What the codec does with a value OUTSIDE the vocabulary decides whether an already-stored odd
/// value can be re-serialized safely, which is a different question from whether a write may assert
/// one. A discriminant is exactly one of three things, and every guard that re-serializes a record
/// has to tell them apart:
/// <list type="number">
/// <item><see cref="CarriesUndeclaredValueVerbatim"/> — the domain holds the value as a plain string
/// and writes it straight back (an option's <c>putCall</c>). The vocabulary constrains what a write
/// may assert; re-serializing an odd value already in the row loses nothing.</item>
/// <item><see cref="Escape"/> — the value is routed through a canonical escape member that preserves
/// the raw label in a companion key, lossless unless the escape's dependent blocks are present.</item>
/// <item>Neither — the value is DROPPED. An unknown <c>exerciseStyle</c> reads as None; an unknown
/// <c>couponType</c> collapses to <c>Fixed</c> and takes the structure-specific fields with it.
/// Re-serializing is always destructive.</item>
/// </list>
/// </remarks>
public sealed record SecurityAssetTermField(
    string Key,
    SecurityAssetTermFieldType Type,
    bool Required,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> AllowedValues,
    SecurityAssetTermVocabularyEscape? Escape = null,
    bool CarriesUndeclaredValueVerbatim = false)
{
    /// <summary>
    /// True when the field carries a CLOSED vocabulary: its value selects a domain case, and a value
    /// outside <see cref="AllowedValues"/> cannot be decoded back into the one it names. Fields whose
    /// domain type has an open "other" case (a bond <c>subclass</c>, a <c>paymentFrequency</c>) are
    /// deliberately not discriminants — they round-trip any label losslessly, so constraining them
    /// would reject values the domain accepts today.
    /// </summary>
    public bool IsDiscriminant => AllowedValues.Count > 0;

    /// <summary>
    /// True when <paramref name="value"/> is one of the declared vocabulary members, or the field
    /// declares no vocabulary at all. The comparison is ORDINAL and case-sensitive because the
    /// codecs are: the serializer emits one exact spelling per case and the deserializer switches on
    /// it, so <c>"fixed"</c> is as undecodable as <c>"Variable"</c>.
    /// </summary>
    public bool Allows(string? value)
        => !IsDiscriminant
           || (value is not null && AllowedValues.Contains(value, StringComparer.Ordinal));

    /// <summary>
    /// The shared rejection reason for a value outside this field's vocabulary, naming the declared
    /// members and the escape when one exists. Both the operator edit surface and the write-mode
    /// codec reject through this text so a rejected value reads the same wherever it is caught.
    /// </summary>
    public string DescribeUndeclaredValue(string assetClass, string? value)
    {
        var reason =
            $"Value '{value}' is not a declared '{Key}' value for asset class '{assetClass}'. " +
            $"Declared values: {string.Join(", ", AllowedValues)}.";

        return Escape is null
            ? reason
            : reason + $" Use '{Escape.Value}' with '{Escape.LabelKey}' to carry a value outside the vocabulary.";
    }

    /// <summary>A field the serialize side always emits (a non-optional domain field).</summary>
    public static SecurityAssetTermField Req(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => new(key, type, Required: true, aliases, AllowedValues: []);

    /// <summary>A field emitted only when its optional domain value is present.</summary>
    public static SecurityAssetTermField Opt(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => new(key, type, Required: false, aliases, AllowedValues: []);

    /// <summary>A required discriminant string constrained to a closed vocabulary.</summary>
    public static SecurityAssetTermField ReqOneOf(string key, params string[] allowedValues)
        => new(key, SecurityAssetTermFieldType.String, Required: true, Aliases: [], AllowedValues: allowedValues);

    /// <summary>An optional discriminant string constrained to a closed vocabulary.</summary>
    public static SecurityAssetTermField OptOneOf(string key, params string[] allowedValues)
        => new(key, SecurityAssetTermFieldType.String, Required: false, Aliases: [], AllowedValues: allowedValues);

    /// <summary>
    /// Declares the vocabulary member that absorbs an out-of-vocabulary label, the key carrying that
    /// raw label, and the keys the escape decode cannot reattach. See
    /// <see cref="SecurityAssetTermVocabularyEscape"/>.
    /// </summary>
    public SecurityAssetTermField WithEscape(string value, string labelKey, params string[] dependentKeys)
        => this with { Escape = new SecurityAssetTermVocabularyEscape(value, labelKey, dependentKeys) };

    /// <summary>
    /// Declares that the codec carries an out-of-vocabulary value VERBATIM in this same key, so the
    /// vocabulary is a write-time constraint only and never a reason to refuse re-serializing a
    /// record that already holds one. See <see cref="CarriesUndeclaredValueVerbatim"/>.
    /// </summary>
    public SecurityAssetTermField WithVerbatimUndeclaredValue()
        => this with { CarriesUndeclaredValueVerbatim = true };
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
/// <para>A field declared with <c>ReqOneOf</c>/<c>OptOneOf</c> carries a CLOSED vocabulary that the
/// write-mode codec and the operator edit surface enforce. Only the string fields whose domain type
/// genuinely cannot round-trip an unlisted value get one: <c>classification</c>, <c>putCall</c>,
/// <c>exerciseStyle</c>, and <c>couponType</c>. Deliberately NOT vocabularies are the labels whose
/// domain type has an open "other" case that preserves the raw string — <c>votingRightsCat</c>
/// (<c>OtherVotingRights</c>), <c>subclass</c> (<c>BondSubclass.Other</c>), <c>paymentFrequency</c>
/// (<c>OtherFrequency</c>), and <c>distributionPolicy</c> (<c>OtherDistribution</c>) — plus the
/// free-text labels the domain carries verbatim (<c>dayCount</c>, <c>settlementType</c>,
/// <c>seniority</c>, <c>depositType</c>, and the rest). Constraining those would reject values that
/// persist and read back losslessly today.</para>
/// </remarks>
public static class SecurityAssetTermsSchema
{
    private static SecurityAssetTermField Req(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => SecurityAssetTermField.Req(key, type, aliases);

    private static SecurityAssetTermField Opt(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => SecurityAssetTermField.Opt(key, type, aliases);

    private static SecurityAssetTermField ReqOneOf(string key, params string[] allowedValues)
        => SecurityAssetTermField.ReqOneOf(key, allowedValues);

    private static SecurityAssetTermField OptOneOf(string key, params string[] allowedValues)
        => SecurityAssetTermField.OptOneOf(key, allowedValues);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SecurityAssetTermField>> FieldsByAssetClass =
        new Dictionary<string, IReadOnlyList<SecurityAssetTermField>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Equity"] =
            [
                Opt("shareClass", SecurityAssetTermFieldType.String),
                Opt("votingRightsCat", SecurityAssetTermFieldType.String),
                OptOneOf("classification", "Common", "Preferred", "Convertible", "ConvertiblePreferred", "Other")
                    .WithEscape("Other", "otherClassification", "preferredTerms", "convertibleTerms"),
                // Raw label carried alongside classification="Other"; the discriminant stays a
                // closed vocabulary while free-text classifications round-trip losslessly.
                Opt("otherClassification", SecurityAssetTermFieldType.String),
                Opt("preferredTerms", SecurityAssetTermFieldType.Object),
                Opt("convertibleTerms", SecurityAssetTermFieldType.Object)
            ],
            ["Option"] =
            [
                Req("underlyingId", SecurityAssetTermFieldType.Guid),
                // The F# option command rejects anything but Put/Call; declaring the vocabulary moves
                // the same rejection onto the edit surface and the write-mode codec. OptionTerms
                // holds it as a plain string, so an odd value already in a row re-serializes intact
                // — the vocabulary constrains new writes, it does not make old rows unwritable.
                ReqOneOf("putCall", "Put", "Call").WithVerbatimUndeclaredValue(),
                Req("strike", SecurityAssetTermFieldType.Decimal),
                Req("expiry", SecurityAssetTermFieldType.Date),
                Req("multiplier", SecurityAssetTermFieldType.Decimal),
                Opt("optChainId", SecurityAssetTermFieldType.String),
                // ExerciseStyle has no "other" case: an unrecognized style decodes to None, so the
                // value would be silently dropped rather than preserved.
                OptOneOf("exerciseStyle", "American", "European", "Bermudan"),
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
                // serializer; the legacy nested "coupon" object shape is read as a fallback.
                // couponType selects the BondCouponStructure case and has no escape: an
                // unrecognized value collapses to Fixed, dropping the label AND every field the
                // named structure owns (floatingIndex, the step schedule, the inflation block).
                OptOneOf("couponType", "Fixed", "Floating", "ZeroCoupon", "Step", "InflationLinked"),
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

    /// <summary>The declared fields for <paramref name="assetClass"/>, or an empty list when none is declared.</summary>
    public static IReadOnlyList<SecurityAssetTermField> Fields(string assetClass)
        => FieldsByAssetClass.TryGetValue(assetClass, out var fields) ? fields : [];

    /// <summary>
    /// The declared CLOSED-vocabulary fields for <paramref name="assetClass"/> — the discriminants
    /// whose value selects a domain case. Callers that must enforce or audit the vocabularies walk
    /// this instead of hard-coding a field list per asset class.
    /// </summary>
    public static IEnumerable<SecurityAssetTermField> DiscriminantFields(string assetClass)
        => Fields(assetClass).Where(static field => field.IsDiscriminant);

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
