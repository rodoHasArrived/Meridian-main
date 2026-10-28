using System.Text.Json;
using System.Text.Json.Nodes;

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
/// <summary>What a term field describes.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SecurityAssetTermRole>))]
public enum SecurityAssetTermRole
{
    /// <summary>The instrument's contractual economics.</summary>
    Economic,

    /// <summary>An operational or vendor reference (pricing source, data feed) carried with the terms.</summary>
    Operational
}

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

    /// <summary>
    /// The typed convention this string field carries, or <see cref="SecurityTermConventionKind.None"/>.
    /// Unlike a closed vocabulary, a convention accepts every spelling its parser recognizes and
    /// stores the text verbatim; it rejects only values the parser cannot type.
    /// </summary>
    public SecurityTermConventionKind Convention { get; init; }

    /// <summary>Declares the typed convention this string field carries.</summary>
    public SecurityAssetTermField WithConvention(SecurityTermConventionKind convention)
        => this with { Convention = convention };

    /// <summary>
    /// Whether the field is part of the instrument's economics or an operational reference (a pricing
    /// source, a vendor feed). Operational fields are kept beside the economics for compatibility but
    /// never feed cash-flow, accounting or economic-definition math.
    /// </summary>
    public SecurityAssetTermRole Role { get; init; } = SecurityAssetTermRole.Economic;

    /// <summary>Declares the field an operational reference rather than an economic term.</summary>
    public SecurityAssetTermField AsOperational()
        => this with { Role = SecurityAssetTermRole.Operational };

    /// <summary>
    /// The role the field's value plays in the underlying graph, or <see langword="null"/> when the
    /// field names nothing the instrument depends on. <see cref="SecurityUnderlyingLinkDeriver"/>
    /// builds the derived graph from these declarations alone: a value that is a security id links
    /// to that record, any other text is recorded as an unresolved reference.
    /// </summary>
    public SecurityUnderlyingRole? UnderlyingRole { get; init; }

    /// <summary>Declares the field a reference the underlying graph links in <paramref name="role"/>.</summary>
    public SecurityAssetTermField LinksUnderlying(SecurityUnderlyingRole role)
        => this with { UnderlyingRole = role };

    /// <summary>
    /// The asset-class terms version (<see cref="SecurityAssetClassDescriptor.TermsVersion"/>) that
    /// introduced this field; 1 for every field the class carried before per-class versioning. A
    /// payload stamped with an earlier version never carries it. The committed manifests under
    /// <c>tests/fixtures/security-master/terms-versions</c> are checked against it: a field must first
    /// appear in the manifest of this version. A field above the class's write version is readable
    /// but not writable (<see cref="SecurityAssetClassDescriptor.ReadsTermsThrough"/>): a node decodes
    /// it from payloads stamped at or above this version and refuses it on every write path. A class
    /// may read several versions ahead of what it writes, each introduced by its own top-level fields
    /// (Bond: <c>scheduleConventions</c> at 2, <c>callSchedule</c> and <c>putSchedule</c> at 3). The
    /// members of a container introduced above version 1 are frozen with it: a later member needs a
    /// new container key.
    /// </summary>
    public int SinceTermsVersion { get; init; } = SecurityTermsEnvelope.DefaultAssetClassTermsVersion;

    /// <summary>
    /// Declares the terms version that introduced the field. Like <see cref="WithConvention"/> and
    /// <see cref="AsOperational"/> it is a single-member <c>with</c>, so it composes with every other
    /// modifier in any order.
    /// </summary>
    public SecurityAssetTermField Since(int termsVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(termsVersion, SecurityTermsEnvelope.DefaultAssetClassTermsVersion);
        return this with { SinceTermsVersion = termsVersion };
    }

    /// <summary>
    /// True when <paramref name="value"/> satisfies this field's convention (or it declares none).
    /// Blank values are left to the required/optional rules.
    /// </summary>
    public bool SatisfiesConvention(string? value)
        => Convention == SecurityTermConventionKind.None
           || string.IsNullOrWhiteSpace(value)
           || SecurityTermConventions.IsRecognized(Convention, value);
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
/// or <see cref="SecurityAssetTermFieldType.Object"/>. Registered inner shapes are exposed through
/// <see cref="ElementFields"/> so codec guards can check the contents as well as the container.
/// <para>A field declared with <c>ReqOneOf</c>/<c>OptOneOf</c> carries a CLOSED vocabulary that the
/// write-mode codec and the operator edit surface enforce. Only the string fields whose domain type
/// genuinely cannot round-trip an unlisted value get one: <c>classification</c>, <c>putCall</c>,
/// <c>exerciseStyle</c>, and <c>couponType</c>. Deliberately NOT vocabularies are the labels whose
/// domain type has an open "other" case that preserves the raw string — <c>votingRightsCat</c>
/// (<c>OtherVotingRights</c>), <c>subclass</c> (<c>BondSubclass.Other</c>), <c>paymentFrequency</c>
/// (<c>OtherFrequency</c>), and <c>distributionPolicy</c> (<c>OtherDistribution</c>) — plus the
/// free-text labels the domain carries verbatim (<c>seniority</c>, <c>depositType</c>, and the
/// rest). Constraining those would reject values that persist and read back losslessly today.
/// Between the two sit the typed CONVENTIONS (<c>dayCount</c>, <c>settlementType</c>,
/// <c>paymentFrequency</c>, <c>resetFrequency</c>, <c>sweepFrequency</c>): the text is still stored
/// verbatim and every stored value still reads, but a write must name a spelling the convention's
/// parser (<see cref="SecurityTermConventions"/>) can type, so no value is accepted that every
/// cash-flow and accrual consumer would then read as an unknown cadence or basis.</para>
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
                Req("underlyingId", SecurityAssetTermFieldType.Guid).LinksUnderlying(SecurityUnderlyingRole.Underlying),
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
                Opt("settlementType", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.SettlementMethod),
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
                Opt("settlementType", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.SettlementMethod),
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
                Opt("floatingIndex", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Index),
                Opt("spreadBps", SecurityAssetTermFieldType.Decimal),
                Opt("capRate", SecurityAssetTermFieldType.Decimal),
                Opt("floorRate", SecurityAssetTermFieldType.Decimal),
                // Step-coupon schedule ({effectiveDate, rate}); empty for non-step coupons. The
                // inflation fields carry linker indexation (couponType = "InflationLinked"); null
                // otherwise. These are what make StepRate/FixedToFloat/InflationLinked subclasses
                // computable rather than labels.
                Opt("stepSchedule", SecurityAssetTermFieldType.Array),
                Opt("inflationIndex", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Index),
                Opt("inflationBaseIndexValue", SecurityAssetTermFieldType.Decimal),
                Opt("inflationIndexRatio", SecurityAssetTermFieldType.Decimal),
                Opt("dayCount", SecurityAssetTermFieldType.String, "dayCountConvention").WithConvention(SecurityTermConventionKind.DayCount),
                Req("isCallable", SecurityAssetTermFieldType.Boolean),
                Opt("callDate", SecurityAssetTermFieldType.Date),
                Opt("issuerName", SecurityAssetTermFieldType.String),
                Opt("seniority", SecurityAssetTermFieldType.String),
                Req("subclass", SecurityAssetTermFieldType.String),
                Opt("par", SecurityAssetTermFieldType.Decimal),
                Opt("paymentFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.CouponFrequency),
                Opt("legalFinalMaturity", SecurityAssetTermFieldType.Date),
                Opt("preRefundDate", SecurityAssetTermFieldType.Date),
                Opt("mandatoryPutDate", SecurityAssetTermFieldType.Date),
                // Contractual principal instalments ({paymentDate, amount}) for sinking-fund and
                // other scheduled-principal subclasses; empty for bullet bonds.
                Opt("principalSchedule", SecurityAssetTermFieldType.Array),
                // How the schedule kernel builds the coupon periods (asset-class terms version 2,
                // readable before writable: ADR-024). Inner shape: ScheduleConventionFields.
                Opt("scheduleConventions", SecurityAssetTermFieldType.Object).Since(2),
                // Declared call and put schedules (asset-class terms version 3, readable before
                // writable: ADR-024). Entry shapes: CallScheduleEntryFields, PutScheduleEntryFields.
                Opt("callSchedule", SecurityAssetTermFieldType.Array).Since(3),
                Opt("putSchedule", SecurityAssetTermFieldType.Array).Since(3)
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
                Opt("dayCount", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.DayCount),
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
                Opt("dayCount", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.DayCount),
                // Asset-class terms version 2, readable before writable (ADR-024).
                Opt("scheduleConventions", SecurityAssetTermFieldType.Object).Since(2),
                // Declared call and put schedules (asset-class terms version 3, readable before
                // writable: ADR-024). Entry shapes: CallScheduleEntryFields, PutScheduleEntryFields.
                Opt("callSchedule", SecurityAssetTermFieldType.Array).Since(3),
                Opt("putSchedule", SecurityAssetTermFieldType.Array).Since(3)
            ],
            ["CommercialPaper"] =
            [
                Req("issuerName", SecurityAssetTermFieldType.String),
                Req("maturity", SecurityAssetTermFieldType.Date),
                Opt("discountRate", SecurityAssetTermFieldType.Decimal),
                Opt("dayCount", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.DayCount),
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
                Opt("sweepFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.PaymentFrequency),
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
                Req("legs", SecurityAssetTermFieldType.Array, "swapLegs", "cashFlowLegs")
            ],
            ["DirectLoan"] =
            [
                Req("borrower", SecurityAssetTermFieldType.String),
                Opt("maturity", SecurityAssetTermFieldType.Date),
                Opt("referenceIndex", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Index),
                Opt("spreadBps", SecurityAssetTermFieldType.Decimal),
                Opt("currentCouponRate", SecurityAssetTermFieldType.Decimal),
                Opt("resetFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.PaymentFrequency),
                Opt("pricingSource", SecurityAssetTermFieldType.String).AsOperational(),
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
                // A fixed coupon or a floating index name in one free-text field; the graph records
                // it as an index reference until the field is split.
                Req("couponOrIndex", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Index),
                // Free-text trustee or servicer report pointer: the evidence lineage the paydown
                // projector requires for typed factor rows. Operational, not economic: no cash-flow
                // or accounting math reads it. The dated schedule is factorScheduleEntries.
                Opt("factorSchedule", SecurityAssetTermFieldType.String).AsOperational(),
                // Typed, dated factor points ({asOfDate, factor}) consumed by the structured
                // cash-flow resolver's FactorAsOf lookup.
                Opt("factorScheduleEntries", SecurityAssetTermFieldType.Array),
                // Legal final maturity of the tranche: the anchor date for calculated cash-flow
                // projection — without it the factor schedule has no production effect.
                Opt("maturity", SecurityAssetTermFieldType.Date),
                // Typed tranche terms (asset-class terms version 2): the securitisation family,
                // capital-structure position, loss attachment/detachment, collateral-pool statistics
                // and the CLO period dates. All optional, so a version-1 payload reads unchanged;
                // tagged .Since(2) so the committed version manifests place them in version 2.
                Opt("structuredCreditKind", SecurityAssetTermFieldType.String).Since(2),
                Opt("seniority", SecurityAssetTermFieldType.String).Since(2),
                Opt("attachmentPoint", SecurityAssetTermFieldType.Decimal).Since(2),
                Opt("detachmentPoint", SecurityAssetTermFieldType.Decimal).Since(2),
                Opt("weightedAverageCoupon", SecurityAssetTermFieldType.Decimal).Since(2),
                Opt("weightedAverageMaturityMonths", SecurityAssetTermFieldType.Integer).Since(2),
                Opt("weightedAverageLoanAgeMonths", SecurityAssetTermFieldType.Integer).Since(2),
                Opt("reinvestmentPeriodEnd", SecurityAssetTermFieldType.Date).Since(2),
                Opt("nonCallPeriodEnd", SecurityAssetTermFieldType.Date).Since(2),
                // Asset-class terms version 3, readable before writable (ADR-024).
                Opt("scheduleConventions", SecurityAssetTermFieldType.Object).Since(3),
                // Declared call and put schedules (asset-class terms version 4, readable before
                // writable: ADR-024). Entry shapes: CallScheduleEntryFields, PutScheduleEntryFields.
                Opt("callSchedule", SecurityAssetTermFieldType.Array).Since(4),
                Opt("putSchedule", SecurityAssetTermFieldType.Array).Since(4)
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
                Opt("collateral", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Collateral),
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
                Opt("underlyingDescription", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Underlying),
                Opt("leverage", SecurityAssetTermFieldType.Decimal)
            ],
            ["Warrant"] =
            [
                Req("underlyingId", SecurityAssetTermFieldType.Guid).LinksUnderlying(SecurityUnderlyingRole.Underlying),
                Req("warrantType", SecurityAssetTermFieldType.String),
                Opt("strike", SecurityAssetTermFieldType.Decimal),
                Opt("expiry", SecurityAssetTermFieldType.Date),
                Opt("multiplier", SecurityAssetTermFieldType.Decimal)
            ],
            ["FxForward"] =
            [
                Req("baseCurrency", SecurityAssetTermFieldType.String),
                Req("quoteCurrency", SecurityAssetTermFieldType.String),
                Opt("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount"),
                Opt("forwardRate", SecurityAssetTermFieldType.Decimal, "rate"),
                Opt("tradeDate", SecurityAssetTermFieldType.Date),
                Req("valueDate", SecurityAssetTermFieldType.Date, "settlementDate", "maturityDate"),
                Opt("settlementType", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.SettlementMethod)
            ],
            ["NonDeliverableForward"] =
            [
                Req("baseCurrency", SecurityAssetTermFieldType.String),
                Req("quoteCurrency", SecurityAssetTermFieldType.String),
                Req("settlementCurrency", SecurityAssetTermFieldType.String),
                Opt("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount"),
                Opt("forwardRate", SecurityAssetTermFieldType.Decimal, "rate"),
                Req("fixingDate", SecurityAssetTermFieldType.Date),
                Req("valueDate", SecurityAssetTermFieldType.Date, "settlementDate", "maturityDate"),
                Opt("fixingSource", SecurityAssetTermFieldType.String).AsOperational()
            ],
            ["CreditDefaultSwap"] =
            [
                Req("referenceEntity", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.ReferenceEntity),
                Opt("referenceObligation", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.ReferenceObligation),
                ReqOneOf("protectionSide", "Buy", "Sell").WithVerbatimUndeclaredValue(),
                Req("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount"),
                Req("fixedCouponBps", SecurityAssetTermFieldType.Decimal, "couponBps", "runningSpreadBps"),
                Req("effectiveDate", SecurityAssetTermFieldType.Date),
                Req("maturityDate", SecurityAssetTermFieldType.Date, "scheduledTerminationDate"),
                Opt("paymentFrequency", SecurityAssetTermFieldType.String, "frequency").WithConvention(SecurityTermConventionKind.CouponFrequency),
                Opt("dayCount", SecurityAssetTermFieldType.String, "dayCountConvention").WithConvention(SecurityTermConventionKind.DayCount),
                Opt("seniority", SecurityAssetTermFieldType.String),
                Opt("restructuringClause", SecurityAssetTermFieldType.String, "docClause"),
                Opt("upfrontPoints", SecurityAssetTermFieldType.Decimal),
                Opt("recoveryRateAssumption", SecurityAssetTermFieldType.Decimal, "recoveryRate"),
                Opt("indexName", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Index)
            ],
            ["Swaption"] =
            [
                ReqOneOf("payerReceiver", "Payer", "Receiver").WithVerbatimUndeclaredValue(),
                OptOneOf("exerciseStyle", "European", "American", "Bermudan").WithVerbatimUndeclaredValue(),
                Req("strikeRate", SecurityAssetTermFieldType.Decimal, "strike", "fixedRate"),
                Req("expiry", SecurityAssetTermFieldType.Date, "expiryDate", "expirationDate"),
                Opt("underlyingEffectiveDate", SecurityAssetTermFieldType.Date),
                Req("underlyingMaturityDate", SecurityAssetTermFieldType.Date),
                Req("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount"),
                Opt("floatingIndex", SecurityAssetTermFieldType.String, "index", "referenceIndex").LinksUnderlying(SecurityUnderlyingRole.Index),
                Opt("fixedLegPaymentFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.CouponFrequency),
                Opt("fixedLegDayCount", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.DayCount),
                Opt("settlementType", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.SettlementMethod)
            ],
            ["CapFloor"] =
            [
                ReqOneOf("capFloorType", "Cap", "Floor", "Collar").WithVerbatimUndeclaredValue(),
                Req("referenceIndex", SecurityAssetTermFieldType.String, "index", "floatingIndex").LinksUnderlying(SecurityUnderlyingRole.Index),
                Opt("capStrike", SecurityAssetTermFieldType.Decimal, "capRate"),
                Opt("floorStrike", SecurityAssetTermFieldType.Decimal, "floorRate"),
                Req("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount"),
                Req("effectiveDate", SecurityAssetTermFieldType.Date),
                Req("maturityDate", SecurityAssetTermFieldType.Date),
                Opt("resetFrequency", SecurityAssetTermFieldType.String, "frequency").WithConvention(SecurityTermConventionKind.PaymentFrequency),
                Opt("dayCount", SecurityAssetTermFieldType.String, "dayCountConvention").WithConvention(SecurityTermConventionKind.DayCount),
                Opt("premiumAmount", SecurityAssetTermFieldType.Decimal, "premium")
            ],
            ["StructuredNote"] =
            [
                Req("issuerName", SecurityAssetTermFieldType.String, "issuer"),
                Req("underlyingDescription", SecurityAssetTermFieldType.String, "underlying", "referenceAsset").LinksUnderlying(SecurityUnderlyingRole.Underlying),
                Req("maturity", SecurityAssetTermFieldType.Date, "maturityDate"),
                Opt("issueDate", SecurityAssetTermFieldType.Date),
                Opt("denomination", SecurityAssetTermFieldType.Decimal, "par", "faceAmount"),
                Opt("principalProtectionPct", SecurityAssetTermFieldType.Decimal, "principalProtection"),
                Opt("participationRate", SecurityAssetTermFieldType.Decimal, "participation"),
                Opt("returnCap", SecurityAssetTermFieldType.Decimal, "cap"),
                Opt("couponRate", SecurityAssetTermFieldType.Decimal, "coupon"),
                Opt("paymentFrequency", SecurityAssetTermFieldType.String, "frequency").WithConvention(SecurityTermConventionKind.CouponFrequency),
                Opt("barrierLevel", SecurityAssetTermFieldType.Decimal, "barrier")
            ],
            ["InvestmentFund"] =
            [
                Opt("fundType", SecurityAssetTermFieldType.String),
                Opt("fundFamily", SecurityAssetTermFieldType.String),
                Opt("navCurrency", SecurityAssetTermFieldType.String),
                Opt("distributionPolicy", SecurityAssetTermFieldType.String),
                Opt("isStableNav", SecurityAssetTermFieldType.Boolean),
                Opt("pricingSource", SecurityAssetTermFieldType.String).AsOperational()
            ]
        };

    // The scheduleConventions block: how the contractual schedule kernel (ADR-025) builds an
    // instrument's periods. Every member is optional and Since 1 within the block, which carries the
    // class's terms version itself; a member added later needs a new readable version. It must be
    // declared above ElementFieldsByPath, which reads it while the type initializes.
    private static readonly IReadOnlyList<SecurityAssetTermField> ScheduleConventionFields =
    [
        Opt("businessDayConvention", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.BusinessDayConvention),
        Opt("accrualAdjusted", SecurityAssetTermFieldType.Boolean),
        Opt("paymentCalendars", SecurityAssetTermFieldType.Array),
        Opt("paymentLagBusinessDays", SecurityAssetTermFieldType.Integer),
        Opt("rollConvention", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.RollConvention),
        Opt("endOfMonthRule", SecurityAssetTermFieldType.Boolean),
        Opt("firstRegularDate", SecurityAssetTermFieldType.Date),
        Opt("lastRegularDate", SecurityAssetTermFieldType.Date),
        OptOneOf("direction", "Forward", "Backward").WithVerbatimUndeclaredValue(),
        Opt("stubPolicy", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.StubPolicy),
        OptOneOf("couponRateUnit", "Percent", "Fraction").WithVerbatimUndeclaredValue(),
        OptOneOf("couponCashBasis", "Level", "DayCount").WithVerbatimUndeclaredValue()
    ];

    // Notice terms shared by call and put entries (ADR-025 Applications, increment 4b): how many days
    // before the exercise date notice (or a holder's put election) is due, at the latest and the
    // earliest, and whether the counts are business or calendar days.
    private static readonly IReadOnlyList<SecurityAssetTermField> NoticeFields =
    [
        Opt("minimumNoticeDays", SecurityAssetTermFieldType.Integer),
        Opt("maximumNoticeDays", SecurityAssetTermFieldType.Integer),
        OptOneOf("noticeDayType", "Business", "Calendar").WithVerbatimUndeclaredValue()
    ];

    // One entry of a declared call schedule (asset-class terms version 3 on Bond and
    // CertificateOfDeposit, 4 on StructuredCredit; readable before writable, ADR-024). Meanings are
    // frozen in ADR-025 Applications. The date is required in meaning but optional here, because a
    // versioned container's members are untagged and optional: the reader drops an undated entry and
    // the contract step refuses it on write. The shape is frozen once readable
    // (terms-element-shapes/versioned-containers.json): a member added later needs a new container key
    // at a new readable version, so a later class-wide term belongs at the top level instead. Both
    // entry lists are declared above ElementFieldsByPath, which reads them while the type initializes.
    private static readonly IReadOnlyList<SecurityAssetTermField> CallScheduleEntryFields =
    [
        Opt("callDate", SecurityAssetTermFieldType.Date),
        Opt("callEndDate", SecurityAssetTermFieldType.Date),
        OptOneOf("exerciseStyle", "European", "American", "Bermudan").WithVerbatimUndeclaredValue(),
        Opt("exerciseFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.CouponFrequency),
        Opt("callPrice", SecurityAssetTermFieldType.Decimal),
        OptOneOf("priceBasis", "PercentOfPar", "PercentOfAccretedValue").WithVerbatimUndeclaredValue(),
        OptOneOf("priceType", "Fixed", "MakeWhole").WithVerbatimUndeclaredValue(),
        Opt("makeWholeSpreadBps", SecurityAssetTermFieldType.Decimal),
        OptOneOf("partialRedemption", "WholeOnly", "ProRata", "ByLot").WithVerbatimUndeclaredValue(),
        .. NoticeFields
    ];

    // One entry of a declared put schedule: a holder-elective put (a mandatory tender stays
    // mandatoryPutDate). The same version and freezing rules as the call entry.
    private static readonly IReadOnlyList<SecurityAssetTermField> PutScheduleEntryFields =
    [
        Opt("putDate", SecurityAssetTermFieldType.Date),
        Opt("putEndDate", SecurityAssetTermFieldType.Date),
        OptOneOf("exerciseStyle", "European", "American", "Bermudan").WithVerbatimUndeclaredValue(),
        Opt("exerciseFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.CouponFrequency),
        Opt("putPrice", SecurityAssetTermFieldType.Decimal),
        OptOneOf("priceBasis", "PercentOfPar", "PercentOfAccretedValue").WithVerbatimUndeclaredValue(),
        .. NoticeFields
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SecurityAssetTermField>> ElementFieldsByPath =
        new Dictionary<string, IReadOnlyList<SecurityAssetTermField>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Swap.legs"] =
            [
                Opt("legId", SecurityAssetTermFieldType.String, "id", "name"),
                Req("legType", SecurityAssetTermFieldType.String, "rateType", "type"),
                Req("currency", SecurityAssetTermFieldType.String),
                Opt("direction", SecurityAssetTermFieldType.String, "payReceive", "payOrReceive", "side"),
                Opt("index", SecurityAssetTermFieldType.String, "indexName", "referenceIndex").LinksUnderlying(SecurityUnderlyingRole.Index),
                Opt("fixedRate", SecurityAssetTermFieldType.Decimal, "rate", "couponRate"),
                Opt("spreadBps", SecurityAssetTermFieldType.Decimal),
                Opt("currentIndexRate", SecurityAssetTermFieldType.Decimal, "lastFixing", "currentRate", "indexRate"),
                Opt("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount", "faceAmount", "principal"),
                Opt("paymentFrequency", SecurityAssetTermFieldType.String, "frequency").WithConvention(SecurityTermConventionKind.CouponFrequency),
                Opt("dayCount", SecurityAssetTermFieldType.String, "dayCountConvention", "dayCountBasis").WithConvention(SecurityTermConventionKind.DayCount),
                Req("exchangesPrincipal", SecurityAssetTermFieldType.Boolean, "principalExchange", "notionalExchange"),
                // Dated {effectiveDate, notional} steps of an amortizing or accreting leg.
                Opt("notionalSchedule", SecurityAssetTermFieldType.Array, "amortizationSchedule"),
                // Dated {resetDate, fixingRate} resets of a floating leg, with the fixing once observed.
                Opt("resetSchedule", SecurityAssetTermFieldType.Array, "fixings"),
                // The leg's own schedule conventions (asset-class terms version 2, readable before
                // writable: ADR-024).
                Opt("scheduleConventions", SecurityAssetTermFieldType.Object).Since(2)
            ],
            ["Swap.legs.scheduleConventions"] = ScheduleConventionFields,
            ["Bond.scheduleConventions"] = ScheduleConventionFields,
            ["CertificateOfDeposit.scheduleConventions"] = ScheduleConventionFields,
            ["StructuredCredit.scheduleConventions"] = ScheduleConventionFields,
            ["Bond.callSchedule"] = CallScheduleEntryFields,
            ["Bond.putSchedule"] = PutScheduleEntryFields,
            ["CertificateOfDeposit.callSchedule"] = CallScheduleEntryFields,
            ["CertificateOfDeposit.putSchedule"] = PutScheduleEntryFields,
            ["StructuredCredit.callSchedule"] = CallScheduleEntryFields,
            ["StructuredCredit.putSchedule"] = PutScheduleEntryFields,
            // The conversion terms a convertible or convertible-preferred equity carries (the F#
            // ConvertibleTerms record): the security it converts into, and at what ratio.
            ["Equity.convertibleTerms"] =
            [
                Req("underlyingSecurityId", SecurityAssetTermFieldType.Guid).LinksUnderlying(SecurityUnderlyingRole.Underlying),
                Req("conversionRatio", SecurityAssetTermFieldType.Decimal),
                Opt("conversionPrice", SecurityAssetTermFieldType.Decimal),
                Opt("conversionStartDate", SecurityAssetTermFieldType.Date),
                Opt("conversionEndDate", SecurityAssetTermFieldType.Date)
            ]
        };

    /// <summary>
    /// The declared fields within an array element or nested object, or an empty list when no
    /// inner shape is registered. These describe the serialized shape; optional legacy input may
    /// omit fields such as a swap leg's principal-exchange flag, which defaults to false.
    /// </summary>
    public static IReadOnlyList<SecurityAssetTermField> ElementFields(string assetClass, string key)
        => ElementFieldsByPath.TryGetValue($"{assetClass}.{key}", out var fields) ? fields : [];

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

    /// <summary>
    /// The declared fields of <paramref name="assetClass"/> a node writes: those introduced at or below
    /// its write version (<see cref="SecurityAssetClassCatalog.GetTermsVersion"/>). Authoring surfaces
    /// (the asset-class registry, term blocks, the field-edit validator) offer these, never a field
    /// the node only reads.
    /// </summary>
    public static IReadOnlyList<SecurityAssetTermField> WritableFields(string assetClass)
    {
        var writes = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
        return Fields(assetClass).Where(field => field.SinceTermsVersion <= writes).ToArray();
    }

    /// <summary>
    /// The registered inner-shape paths (<c>Class.key</c>, or <c>Class.container.key</c> for a shape
    /// inside an element), so guards can check every registered shape.
    /// </summary>
    public static IReadOnlyCollection<string> ElementFieldPaths => ElementFieldsByPath.Keys.ToArray();

    /// <summary>
    /// Whether a term value is populated. An absent or null value is not, and an array or object is
    /// populated only when an element or member is; every other value is, including <c>false</c>,
    /// <c>0</c> and the empty string. Absent, null, <c>[]</c> and an all-null object therefore all mean
    /// the term is unset (ADR-024).
    /// </summary>
    public static bool IsPopulatedTermValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => false,
            JsonValueKind.Array => value.EnumerateArray().Any(IsPopulatedTermValue),
            JsonValueKind.Object => value.EnumerateObject().Any(static member => IsPopulatedTermValue(member.Value)),
            _ => true
        };

    /// <summary>
    /// The populated terms of a flat payload that were introduced above <paramref name="termsVersion"/>:
    /// top-level fields, and fields inside a declared object or array element (a swap leg's
    /// <c>legs[1].scheduleConventions</c>). Keys and aliases match case-insensitively, as the readers
    /// do. A profile-governed payload (<see cref="SecurityTermsEnvelope.IsProfileGovernedPayload"/>)
    /// has no versioned class terms and yields nothing.
    /// </summary>
    public static IReadOnlyList<(string Path, SecurityAssetTermField Field)> PopulatedTermsAbove(
        string assetClass,
        JsonElement payload,
        int termsVersion)
        => VersionedTermsAbove(assetClass, payload, termsVersion, int.MaxValue)
            .Where(static term => IsPopulatedTermValue(term.Value))
            .Select(static term => (term.Path, term.Field))
            .ToArray();

    /// <summary>
    /// The terms of a flat payload introduced above <paramref name="termsVersion"/>, keyed by canonical
    /// path (<c>scheduleConventions</c>, <c>legs[1].scheduleConventions</c>) with every null member,
    /// <c>[]</c> and <c>{}</c> removed, so two payloads that carry the same versioned content compare
    /// equal however they spelled its absence. A path more than one spelling populates is reported in
    /// <paramref name="conflictingPaths"/>. A profile-governed payload yields an empty object.
    /// </summary>
    public static JsonObject NormalizedTermsAbove(
        string assetClass,
        JsonElement payload,
        int termsVersion,
        out IReadOnlyList<string> conflictingPaths)
        => NormalizedTermsAbove(assetClass, payload, termsVersion, int.MaxValue, out conflictingPaths);

    /// <summary>
    /// <see cref="NormalizedTermsAbove(string, JsonElement, int, out IReadOnlyList{string})"/> bounded
    /// above: only the terms introduced after <paramref name="termsVersion"/> and at or before
    /// <paramref name="throughVersion"/>. The round-trip guard bounds it by a row's own stamp, because a
    /// term above the stamp is an undeclared key of that row, which no reader decodes.
    /// </summary>
    public static JsonObject NormalizedTermsAbove(
        string assetClass,
        JsonElement payload,
        int termsVersion,
        int throughVersion,
        out IReadOnlyList<string> conflictingPaths)
    {
        var normalized = new JsonObject();
        var conflicts = new List<string>();
        foreach (var term in VersionedTermsAbove(assetClass, payload, termsVersion, throughVersion))
        {
            if (NormalizeTermValue(JsonNode.Parse(term.Value.GetRawText())) is not { } value)
            {
                continue;
            }

            if (normalized.ContainsKey(term.CanonicalPath))
            {
                conflicts.Add(term.CanonicalPath);
                continue;
            }

            normalized[term.CanonicalPath] = value;
        }

        conflictingPaths = conflicts;
        return normalized;
    }

    private static IEnumerable<(string Path, string CanonicalPath, SecurityAssetTermField Field, JsonElement Value)> VersionedTermsAbove(
        string assetClass,
        JsonElement payload,
        int termsVersion,
        int throughVersion)
    {
        if (payload.ValueKind != JsonValueKind.Object || SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, payload))
        {
            yield break;
        }

        foreach (var field in Fields(assetClass))
        {
            if (field.SinceTermsVersion > throughVersion)
            {
                continue;
            }

            foreach (var property in MatchingProperties(payload, field))
            {
                if (field.SinceTermsVersion > termsVersion)
                {
                    yield return (property.Name, field.Key, field, property.Value);
                    continue;
                }

                var inner = ElementFields(assetClass, field.Key)
                    .Where(innerField => innerField.SinceTermsVersion > termsVersion && innerField.SinceTermsVersion <= throughVersion)
                    .ToArray();
                if (inner.Length == 0)
                {
                    continue;
                }

                if (field.Type == SecurityAssetTermFieldType.Object && property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var term in InnerTerms(property.Value, property.Name, field.Key, inner))
                    {
                        yield return term;
                    }
                }
                else if (field.Type == SecurityAssetTermFieldType.Array && property.Value.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var element in property.Value.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var term in InnerTerms(element, $"{property.Name}[{index}]", $"{field.Key}[{index}]", inner))
                            {
                                yield return term;
                            }
                        }

                        index++;
                    }
                }
            }
        }
    }

    private static IEnumerable<(string Path, string CanonicalPath, SecurityAssetTermField Field, JsonElement Value)> InnerTerms(
        JsonElement container,
        string path,
        string canonicalPath,
        IReadOnlyList<SecurityAssetTermField> fields)
    {
        foreach (var field in fields)
        {
            foreach (var property in MatchingProperties(container, field))
            {
                yield return ($"{path}.{property.Name}", $"{canonicalPath}.{field.Key}", field, property.Value);
            }
        }
    }

    private static IEnumerable<JsonProperty> MatchingProperties(JsonElement container, SecurityAssetTermField field)
        => container.EnumerateObject().Where(property =>
            string.Equals(property.Name, field.Key, StringComparison.OrdinalIgnoreCase)
            || field.Aliases.Any(alias => string.Equals(property.Name, alias, StringComparison.OrdinalIgnoreCase)));

    private static JsonNode? NormalizeTermValue(JsonNode? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonObject members:
                var normalizedObject = new JsonObject();
                foreach (var (key, member) in members)
                {
                    if (NormalizeTermValue(member) is { } normalizedMember)
                    {
                        normalizedObject[key] = normalizedMember;
                    }
                }

                return normalizedObject.Count == 0 ? null : normalizedObject;
            case JsonArray elements:
                var normalizedArray = new JsonArray();
                foreach (var element in elements)
                {
                    if (NormalizeTermValue(element) is { } normalizedElement)
                    {
                        normalizedArray.Add(normalizedElement);
                    }
                }

                return normalizedArray.Count == 0 ? null : normalizedArray;
            default:
                return value.GetValueKind() == JsonValueKind.Null ? null : value.DeepClone();
        }
    }
}
