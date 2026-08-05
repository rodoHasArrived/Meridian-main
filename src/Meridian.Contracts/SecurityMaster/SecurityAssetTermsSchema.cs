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
    /// <summary>
    /// For <see cref="SecurityAssetTermFieldType.Array"/> and <see cref="SecurityAssetTermFieldType.Object"/>
    /// fields, the declared shape of each element. Empty means the inner shape is undeclared.
    /// <para>
    /// Leaving nested shapes undeclared is what let the structured-credit factor schedule and the
    /// swap leg drift: both were type-correct at the top level (a string, an array) while their
    /// contents disagreed with every consumer. A field whose elements carry economics — schedules,
    /// legs, instalments — should declare them here so the codec tests can measure the serializer
    /// against the contract rather than against another codec.
    /// </para>
    /// </summary>
    public IReadOnlyList<SecurityAssetTermField> ElementFields { get; init; } = [];

    /// <summary>A field the serialize side always emits (a non-optional domain field).</summary>
    public static SecurityAssetTermField Req(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => new(key, type, Required: true, aliases);

    /// <summary>A field emitted only when its optional domain value is present.</summary>
    public static SecurityAssetTermField Opt(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => new(key, type, Required: false, aliases);

    /// <summary>Declares the per-element shape of an <c>Array</c> or <c>Object</c> field.</summary>
    public SecurityAssetTermField WithElements(params SecurityAssetTermField[] elementFields)
        => this with { ElementFields = elementFields };
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
                // serializer; the legacy nested "coupon" object shape is read as a fallback.
                Opt("couponType", SecurityAssetTermFieldType.String),
                Opt("couponRate", SecurityAssetTermFieldType.Decimal),
                Opt("floatingIndex", SecurityAssetTermFieldType.String),
                Opt("spreadBps", SecurityAssetTermFieldType.Decimal),
                Opt("capRate", SecurityAssetTermFieldType.Decimal),
                Opt("floorRate", SecurityAssetTermFieldType.Decimal),
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
                Opt("mandatoryPutDate", SecurityAssetTermFieldType.Date)
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
                Opt("profileApproval", SecurityAssetTermFieldType.Object)
            ],
            ["Swap"] =
            [
                Req("effectiveDate", SecurityAssetTermFieldType.Date),
                Req("maturityDate", SecurityAssetTermFieldType.Date),
                // Per-leg economics, not a rate label. Without notional, payment frequency, and day
                // count a leg cannot be projected into a cash flow; the read-side
                // StructuredCashFlowLeg has always modelled all three.
                Req("legs", SecurityAssetTermFieldType.Array).WithElements(
                    Opt("legId", SecurityAssetTermFieldType.String, "id", "name"),
                    Req("legType", SecurityAssetTermFieldType.String, "rateType", "type"),
                    Req("currency", SecurityAssetTermFieldType.String),
                    Opt("direction", SecurityAssetTermFieldType.String, "payReceive", "payOrReceive", "side"),
                    Opt("index", SecurityAssetTermFieldType.String, "indexName", "referenceIndex"),
                    Opt("fixedRate", SecurityAssetTermFieldType.Decimal),
                    Opt("spreadBps", SecurityAssetTermFieldType.Decimal),
                    Opt("currentIndexRate", SecurityAssetTermFieldType.Decimal, "lastFixing", "currentRate", "indexRate"),
                    Opt("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount", "faceAmount", "principal"),
                    Opt("paymentFrequency", SecurityAssetTermFieldType.String, "frequency"),
                    Opt("dayCount", SecurityAssetTermFieldType.String, "dayCountConvention", "dayCountBasis"),
                    Req("exchangesPrincipal", SecurityAssetTermFieldType.Boolean, "principalExchange", "notionalExchange"))
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
                Req("covenants", SecurityAssetTermFieldType.Array).WithElements(
                    Req("covenantType", SecurityAssetTermFieldType.String),
                    Req("threshold", SecurityAssetTermFieldType.String),
                    Opt("notes", SecurityAssetTermFieldType.String)),
                Req("principalSchedule", SecurityAssetTermFieldType.Array).WithElements(
                    Req("paymentDate", SecurityAssetTermFieldType.Date),
                    Req("amount", SecurityAssetTermFieldType.Decimal))
            ],
            ["StructuredCredit"] =
            [
                Req("tranche", SecurityAssetTermFieldType.String),
                Opt("poolId", SecurityAssetTermFieldType.String),
                Req("collateralType", SecurityAssetTermFieldType.String),
                Req("originalFace", SecurityAssetTermFieldType.Decimal),
                Opt("currentFactor", SecurityAssetTermFieldType.Decimal),
                Req("couponOrIndex", SecurityAssetTermFieldType.String),
                // Array of {asOfDate, factor} rows. Was declared String, matching a serializer that
                // wrote free text — which the cash-flow resolver and the accounting-event adapter
                // both skip, because both accept only an array. Factor-based tranches therefore
                // restated face off the scalar currentFactor for their whole life while
                // RequiresFactorSchedule blocked paydown accounting on a condition the write path
                // could not satisfy. The legacy free text now lands in factorScheduleNote.
                Req("factorSchedule", SecurityAssetTermFieldType.Array).WithElements(
                    Req("asOfDate", SecurityAssetTermFieldType.Date, "factorDate", "effectiveDate", "date"),
                    Req("factor", SecurityAssetTermFieldType.Decimal, "currentFactor")),
                Opt("factorScheduleNote", SecurityAssetTermFieldType.String)
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
