namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// The declared semantics of one stored rate field: what the number means, the unit it is in when the
/// record does not say, the range it may take once converted to a fraction, and whether a negative
/// value is a supported economic fact.
/// </summary>
/// <param name="AssetClass">The canonical catalog asset class.</param>
/// <param name="FieldKey">
/// The schema key of the field (<see cref="SecurityAssetTermsSchema"/>); element fields are written as a
/// dotted path, for example <c>legs.fixedRate</c>.
/// </param>
/// <param name="Meaning">What the number is: an annual coupon, a spread, a strike, a recovery rate.</param>
/// <param name="DefaultUnit">
/// The unit the field declares when the record declares none, or <see langword="null"/> when no default
/// is declared: legacy values are then <see cref="RateUnitResolution.Undeclared"/> and are flagged, not
/// converted. Fields whose stored data is known to be written both ways deliberately declare none.
/// </param>
/// <param name="HonorsRecordUnit">
/// Whether a unit the record itself declares (<c>scheduleConventions.couponRateUnit</c> or a leg's rate
/// unit) governs this field. Basis-point fields are basis points by name and ignore it.
/// </param>
/// <param name="MinFraction">The lowest permitted value once converted to a fraction, if bounded.</param>
/// <param name="MaxFraction">The highest permitted value once converted to a fraction, if bounded.</param>
/// <param name="AllowsNegative">Whether a negative value is a supported economic fact for the field.</param>
/// <param name="ConsumedBy">The calculation readers that read the field through <see cref="RateUnitNormalizer"/>; empty for inventory only.</param>
public sealed record SecurityRateFieldDeclaration(
    string AssetClass,
    string FieldKey,
    string Meaning,
    RateUnit? DefaultUnit,
    bool HonorsRecordUnit,
    decimal? MinFraction,
    decimal? MaxFraction,
    bool AllowsNegative,
    string ConsumedBy = "");

/// <summary>
/// The rate-field unit table (security-master review D10, remediation slice 4, decision D3). Every rate
/// field in the flat terms of <c>SecurityMaster.fs</c> / <c>SecurityTermModules.fs</c> is inventoried here
/// with its declared default unit, range and negative-rate policy. <see cref="RateUnitNormalizer"/> reads
/// it; nothing infers a unit from a value's magnitude.
/// <para>
/// The defaults are <b>proposed, pending domain-owner review (D3)</b>. Only two kinds of field declare a
/// default: basis-point fields (basis points by name) and the Deposit and CashSweep rates, which match
/// the catalog's <see cref="SecurityAssetClassDescriptor.QuotesRatesAsFractions"/> convention. Every
/// other rate field declares none, because stored data writes it both ways (4.25 and 0.0425); a legacy
/// value on those fields is <see cref="RateUnitResolution.Undeclared"/> until the record declares a unit.
/// </para>
/// <para>
/// FX forward rates (<c>FxForward.forwardRate</c>, <c>NonDeliverableForward.forwardRate</c>) are prices,
/// not percentage rates, and are deliberately not in the table.
/// </para>
/// </summary>
public static class SecurityRateFieldUnits
{
    private const string LegacyCashFlow = "SecurityMasterCashFlowService";
    private const string Preview = "ContractualCashFlowPreviewService";
    private const string Accounting = "SecurityMasterAccountingEventService";
    private const string Obligations = "AssetObligationProjectionService";
    private const string CouponReaders = LegacyCashFlow + ", " + Preview + ", " + Accounting;

    // Generous annual-rate bounds: a fraction of at most 1 (100% a year). Rates declared Percent above
    // that are refused instead of posting an implausible amount.
    private const decimal AnnualRateMax = 1m;
    private const decimal NegativeRateFloor = -1m;

    private static SecurityRateFieldDeclaration Coupon(string assetClass, string key, string meaning, string consumedBy = CouponReaders)
        => new(assetClass, key, meaning, DefaultUnit: null, HonorsRecordUnit: true, 0m, AnnualRateMax, AllowsNegative: false, consumedBy);

    private static SecurityRateFieldDeclaration SignedRate(string assetClass, string key, string meaning, RateUnit? defaultUnit = null, string consumedBy = "")
        => new(assetClass, key, meaning, defaultUnit, HonorsRecordUnit: true, NegativeRateFloor, AnnualRateMax, AllowsNegative: true, consumedBy);

    private static SecurityRateFieldDeclaration Bps(string assetClass, string key, string meaning, bool allowsNegative, string consumedBy = "")
        => new(assetClass, key, meaning, RateUnit.BasisPoints, HonorsRecordUnit: false,
            allowsNegative ? NegativeRateFloor : 0m, AnnualRateMax, allowsNegative, consumedBy);

    private static SecurityRateFieldDeclaration Ratio(string assetClass, string key, string meaning, decimal? max)
        => new(assetClass, key, meaning, DefaultUnit: null, HonorsRecordUnit: false, 0m, max, AllowsNegative: false);

    /// <summary>Every declared rate field, in catalog order.</summary>
    public static IReadOnlyList<SecurityRateFieldDeclaration> All { get; } =
    [
        // Equity: preferred dividend (nested preferredTerms block).
        Ratio("Equity", "preferredTerms.dividendRate", "Annual preferred dividend rate", AnnualRateMax),

        // Bond coupon structure. The coupon, cap, floor, step rates and floating fixings follow the
        // record's couponRateUnit; there is no default because stored bonds write both 4.25 and 0.0425.
        Coupon("Bond", "couponRate", "Annual fixed coupon rate"),
        SignedRate("Bond", "capRate", "Floating coupon cap", consumedBy: LegacyCashFlow + ", " + Preview),
        SignedRate("Bond", "floorRate", "Floating coupon floor", consumedBy: LegacyCashFlow + ", " + Preview),
        Coupon("Bond", "stepSchedule.rate", "Step-coupon rate from its effective date", Preview),
        SignedRate("Bond", "currentIndexRate", "Observed floating index fixing", consumedBy: LegacyCashFlow),
        SignedRate("Bond", "resetSchedule.fixingRate", "Observed floating index fixing on a reset date", consumedBy: LegacyCashFlow + ", " + Preview),
        Bps("Bond", "spreadBps", "Floating coupon spread over the index", allowsNegative: true, LegacyCashFlow + ", " + Preview),
        Bps("Bond", "callSchedule.makeWholeSpreadBps", "Make-whole call spread over the benchmark", allowsNegative: false),

        // Cash and money-market rates. Deposit and CashSweep quote rates as fractions (catalog
        // QuotesRatesAsFractions); a negative deposit rate is a supported economic fact.
        SignedRate("Deposit", "interestRate", "Annual deposit interest rate", RateUnit.Fraction, CouponReaders + ", " + Obligations),
        Coupon("CertificateOfDeposit", "couponRate", "Annual CD coupon rate"),
        Coupon("CommercialPaper", "discountRate", "Discount yield (not a coupon; accretion only)", ""),
        Coupon("TreasuryBill", "discountRate", "Discount yield (not a coupon; accretion only)", ""),
        SignedRate("Repo", "repoRate", "Annual repo financing rate", consumedBy: CouponReaders),
        Ratio("Repo", "haircut", "Collateral haircut", 1m),
        SignedRate("CashSweep", "yieldRate", "Sweep vehicle yield", RateUnit.Fraction),

        // Swap legs: the leg's own couponRateUnit governs fixed rates and fixings.
        SignedRate("Swap", "legs.fixedRate", "Fixed-leg annual rate", consumedBy: LegacyCashFlow + ", " + Preview),
        SignedRate("Swap", "legs.currentIndexRate", "Observed floating index fixing", consumedBy: LegacyCashFlow),
        SignedRate("Swap", "legs.resetSchedule.fixingRate", "Observed floating index fixing on a reset date", consumedBy: LegacyCashFlow + ", " + Preview),
        Bps("Swap", "legs.spreadBps", "Floating-leg spread over the index", allowsNegative: true, LegacyCashFlow + ", " + Preview),

        // Direct loans and structured credit.
        Bps("DirectLoan", "spreadBps", "Loan spread over the reference index", allowsNegative: true),
        Coupon("DirectLoan", "currentCouponRate", "Current all-in loan coupon"),
        Coupon("StructuredCredit", "couponOrIndex", "Numeric tranche coupon (an index name is not a rate)"),
        Coupon("StructuredCredit", "weightedAverageCoupon", "Collateral weighted-average coupon", Accounting),
        Ratio("StructuredCredit", "attachmentPoint", "Loss attachment point", 1m),
        Ratio("StructuredCredit", "detachmentPoint", "Loss detachment point", 1m),

        // Guarantees and OTC derivatives.
        Ratio("CommitmentGuarantee", "feeRate", "Commitment or guarantee fee rate", AnnualRateMax),
        Bps("CreditDefaultSwap", "fixedCouponBps", "CDS running coupon", allowsNegative: false),
        Ratio("CreditDefaultSwap", "recoveryRateAssumption", "Assumed recovery rate", 1m),
        SignedRate("Swaption", "strikeRate", "Underlying swap fixed-rate strike"),
        SignedRate("CapFloor", "capStrike", "Cap strike rate"),
        SignedRate("CapFloor", "floorStrike", "Floor strike rate"),

        // Structured notes: participation and caps are leverage ratios, not annual rates.
        Ratio("StructuredNote", "principalProtectionPct", "Protected share of principal", null),
        Ratio("StructuredNote", "participationRate", "Participation in the underlying's return", null),
        Ratio("StructuredNote", "returnCap", "Cap on the participating return", null),
        Coupon("StructuredNote", "couponRate", "Annual note coupon rate", "")
    ];

    private static readonly IReadOnlyDictionary<(string AssetClass, string FieldKey), SecurityRateFieldDeclaration> ByKey =
        All.ToDictionary(static field => (field.AssetClass, field.FieldKey), RateFieldKeyComparer.Instance);

    /// <summary>
    /// The declaration of <paramref name="fieldKey"/> on <paramref name="assetClass"/> (canonical name or
    /// registered alias), or <see langword="null"/> when the field is not a catalogued rate field.
    /// </summary>
    public static SecurityRateFieldDeclaration? Find(string? assetClass, string fieldKey)
    {
        ArgumentNullException.ThrowIfNull(fieldKey);
        var canonical = SecurityAssetClassCatalog.GetOrDefault(assetClass).AssetClass;
        return ByKey.TryGetValue((canonical, fieldKey), out var field) ? field : null;
    }

    /// <summary>
    /// The flat key that carries the scalar annual coupon for <paramref name="assetClass"/>, matching the
    /// class-specific coupon key <c>StructuredCashFlowTermsResolver</c> reads first.
    /// </summary>
    public static string CouponFieldKey(string? assetClass)
        => SecurityAssetClassCatalog.GetOrDefault(assetClass).AssetClass switch
        {
            "Deposit" => "interestRate",
            "Repo" => "repoRate",
            "DirectLoan" => "currentCouponRate",
            "StructuredCredit" => "couponOrIndex",
            "TreasuryBill" or "CommercialPaper" => "discountRate",
            _ => "couponRate"
        };

    private sealed class RateFieldKeyComparer : IEqualityComparer<(string AssetClass, string FieldKey)>
    {
        public static readonly RateFieldKeyComparer Instance = new();

        public bool Equals((string AssetClass, string FieldKey) x, (string AssetClass, string FieldKey) y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.AssetClass, y.AssetClass)
               && StringComparer.OrdinalIgnoreCase.Equals(x.FieldKey, y.FieldKey);

        public int GetHashCode((string AssetClass, string FieldKey) obj)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.AssetClass), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FieldKey));
    }
}
