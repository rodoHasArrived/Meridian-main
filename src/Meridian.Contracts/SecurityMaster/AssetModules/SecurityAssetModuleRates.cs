namespace Meridian.Contracts.SecurityMaster;

/// <summary>Constructors for the existing declared rate semantics; class modules own the rows.</summary>
internal static class SecurityAssetModuleRates
{
    internal const string LegacyCashFlow = "SecurityMasterCashFlowService";
    internal const string Preview = "ContractualCashFlowPreviewService";
    internal const string AccountingReaders = "SecurityMasterAccountingEventService";
    internal const string Obligations = "AssetObligationProjectionService";
    internal const string CouponReaders = LegacyCashFlow + ", " + Preview + ", " + AccountingReaders;

    // Generous annual-rate bounds: a fraction of at most 1 (100% a year). Rates declared Percent above
    // that are refused instead of posting an implausible amount.
    internal const decimal AnnualRateMax = 1m;
    internal const decimal NegativeRateFloor = -1m;

    internal static SecurityRateFieldDeclaration Coupon(string assetClass, string key, string meaning, string consumedBy = CouponReaders)
        => new(assetClass, key, meaning, DefaultUnit: null, HonorsRecordUnit: true, 0m, AnnualRateMax, AllowsNegative: false, consumedBy);

    internal static SecurityRateFieldDeclaration SignedRate(string assetClass, string key, string meaning, RateUnit? defaultUnit = null, string consumedBy = "")
        => new(assetClass, key, meaning, defaultUnit, HonorsRecordUnit: true, NegativeRateFloor, AnnualRateMax, AllowsNegative: true, consumedBy);

    internal static SecurityRateFieldDeclaration Bps(string assetClass, string key, string meaning, bool allowsNegative, string consumedBy = "")
        => new(assetClass, key, meaning, RateUnit.BasisPoints, HonorsRecordUnit: false,
            allowsNegative ? NegativeRateFloor : 0m, AnnualRateMax, allowsNegative, consumedBy);

    internal static SecurityRateFieldDeclaration Ratio(string assetClass, string key, string meaning, decimal? max)
        => new(assetClass, key, meaning, DefaultUnit: null, HonorsRecordUnit: false, 0m, max, AllowsNegative: false);

}
