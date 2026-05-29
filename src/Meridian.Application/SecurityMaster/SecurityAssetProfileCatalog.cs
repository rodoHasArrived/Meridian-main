using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityAssetProfileCatalog
{
    IReadOnlyList<SecurityAssetProfileDefinitionDto> GetProfiles();
    bool TryGetProfile(string profileId, int version, out SecurityAssetProfileDefinitionDto profile);
    bool TryGetLatestApprovedProfile(string profileId, out SecurityAssetProfileDefinitionDto profile);
}

public sealed class StaticSecurityAssetProfileCatalog : ISecurityAssetProfileCatalog
{
    private readonly IReadOnlyList<SecurityAssetProfileDefinitionDto> _profiles;

    public StaticSecurityAssetProfileCatalog(IEnumerable<SecurityAssetProfileDefinitionDto> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _profiles = profiles
            .OrderBy(static profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static profile => profile.Version)
            .ToArray();
    }

    public IReadOnlyList<SecurityAssetProfileDefinitionDto> GetProfiles()
        => _profiles;

    public bool TryGetProfile(string profileId, int version, out SecurityAssetProfileDefinitionDto profile)
    {
        profile = _profiles.FirstOrDefault(candidate =>
            candidate.Version == version
            && string.Equals(candidate.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))!;
        return profile is not null;
    }

    public bool TryGetLatestApprovedProfile(string profileId, out SecurityAssetProfileDefinitionDto profile)
    {
        profile = _profiles
            .Where(candidate =>
                candidate.Status == SecurityAssetProfileStatusDto.Approved
                && string.Equals(candidate.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static candidate => candidate.Version)
            .FirstOrDefault()!;
        return profile is not null;
    }

    public static StaticSecurityAssetProfileCatalog CreateDefault()
        => new(SecurityAssetProfileSeeds.Create());
}

internal static class SecurityAssetProfileSeeds
{
    private static readonly DateOnly EffectiveFrom = new(2026, 5, 29);
    private static readonly DateTimeOffset ApprovedAtUtc = new(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<SecurityAssetProfileDefinitionDto> Create()
        =>
        [
            Profile(
                "structured-credit-io-po",
                "Structured Credit IO/PO",
                "StructuredCredit",
                "IO/PO Strip",
                [
                    RequiredText("tranche", "Tranche", projected: true, searchable: true),
                    RequiredText("poolId", "Pool ID", projected: true, searchable: true),
                    RequiredDecimal("currentFactor", "Current factor", 0m, 1m, projected: true),
                    RequiredDecimal("originalFace", "Original face", 0.01m, null, projected: true),
                    RequiredText("couponOrIndex", "Coupon/index", projected: true, searchable: true),
                    RequiredText("factorSchedule", "Factor schedule", projected: true, searchable: true),
                    RequiredEnum("collateralType", "Collateral type", ["MBS", "CMBS", "ABS", "CLO", "CDO", "Other"], projected: true, searchable: true)
                ],
                [
                    new(SecurityIdentifierKind.Cusip, true, "Structured-credit strips should carry CUSIP coverage when available."),
                    new(SecurityIdentifierKind.InternalCode, true, "Private or modeled strips need a Meridian-stable internal code.")
                ],
                ["Modeled", "Active", "Amortizing", "Matured", "Retired"],
                [SecurityAssetProfileAccountingImpactHintDto.Valuation, SecurityAssetProfileAccountingImpactHintDto.FactorSchedule, SecurityAssetProfileAccountingImpactHintDto.IncomeAccrual]),
            Profile(
                "real-estate-holding",
                "Real Estate Holding",
                "RealAssets",
                "Property Interest",
                [
                    RequiredEnum("propertyType", "Property type", ["Office", "Industrial", "Retail", "Multifamily", "Hospitality", "Land", "Other"], projected: true, searchable: true),
                    RequiredText("addressOrMarket", "Address/market", projected: true, searchable: true),
                    RequiredDecimal("ownershipPercent", "Ownership %", 0m, 100m, projected: true),
                    RequiredDecimal("appraisalValue", "Appraisal value", 0m, null, projected: true),
                    RequiredDate("valuationDate", "Valuation date", projected: true),
                    OptionalText("debtStack", "Debt stack", projected: true, searchable: true),
                    RequiredText("sponsor", "Sponsor", projected: true, searchable: true)
                ],
                [new(SecurityIdentifierKind.InternalCode, true, "Real-estate holdings require an internal code for close and report-pack coverage.")],
                ["Diligence", "Owned", "UnderReview", "Sold", "Retired"],
                [SecurityAssetProfileAccountingImpactHintDto.Valuation, SecurityAssetProfileAccountingImpactHintDto.OwnershipPercentage, SecurityAssetProfileAccountingImpactHintDto.LedgerClassification]),
            Profile(
                "private-fund-interest",
                "Private Fund Interest",
                "PrivateFunds",
                "Limited Partner Interest",
                [
                    RequiredText("gpSponsor", "GP/sponsor", projected: true, searchable: true),
                    RequiredText("strategy", "Strategy", projected: true, searchable: true),
                    RequiredInteger("vintage", "Vintage", 1900, 2200, projected: true),
                    RequiredDecimal("commitment", "Commitment", 0m, null, projected: true),
                    RequiredDecimal("fundedAmount", "Funded amount", 0m, null, projected: true),
                    RequiredDecimal("unfundedAmount", "Unfunded amount", 0m, null, projected: true),
                    RequiredDate("navDate", "NAV date", projected: true),
                    OptionalText("lockup", "Lockup", projected: true, searchable: true)
                ],
                [new(SecurityIdentifierKind.InternalCode, true, "Private-fund interests usually rely on Meridian internal identity.")],
                ["Committed", "Funding", "Active", "Harvesting", "Liquidated"],
                [SecurityAssetProfileAccountingImpactHintDto.CommitmentAccounting, SecurityAssetProfileAccountingImpactHintDto.NavBasedValuation, SecurityAssetProfileAccountingImpactHintDto.LedgerClassification]),
            Profile(
                "private-company-equity",
                "Private Company Equity",
                "PrivateEquity",
                "Private Company Shares",
                [
                    RequiredText("issuer", "Issuer", projected: true, searchable: true),
                    RequiredText("shareClass", "Share class", projected: true, searchable: true),
                    RequiredText("round", "Round", projected: true, searchable: true),
                    RequiredDecimal("ownershipPercent", "Ownership %", 0m, 100m, projected: true),
                    RequiredDecimal("costBasis", "Cost basis", 0m, null, projected: true),
                    RequiredDecimal("latestValuation", "Latest valuation", 0m, null, projected: true),
                    OptionalText("transferRestrictions", "Transfer restrictions", projected: true, searchable: true)
                ],
                [
                    new(SecurityIdentifierKind.InternalCode, true, "Private-company equity requires a stable internal code."),
                    new(SecurityIdentifierKind.Lei, false, "LEI coverage improves issuer governance when available.")
                ],
                ["Held", "Marked", "Restricted", "Exited", "Retired"],
                [SecurityAssetProfileAccountingImpactHintDto.Valuation, SecurityAssetProfileAccountingImpactHintDto.OwnershipPercentage, SecurityAssetProfileAccountingImpactHintDto.LedgerClassification]),
            Profile(
                "co-invest-spv",
                "Co-invest/SPV",
                "PrivateFunds",
                "Co-investment Vehicle",
                [
                    RequiredText("vehicle", "Vehicle", projected: true, searchable: true),
                    RequiredText("underlyingCompanyOrSecurity", "Underlying company/security", projected: true, searchable: true),
                    RequiredText("sponsor", "Sponsor", projected: true, searchable: true),
                    RequiredDecimal("commitment", "Commitment", 0m, null, projected: true),
                    RequiredText("economics", "Economics", projected: true, searchable: true),
                    RequiredEnum("reportingCadence", "Reporting cadence", ["Monthly", "Quarterly", "SemiAnnual", "Annual", "AdHoc"], projected: true, searchable: true)
                ],
                [new(SecurityIdentifierKind.InternalCode, true, "SPV and co-investment vehicles require a stable internal code.")],
                ["Committed", "Active", "Monitoring", "Exited", "Retired"],
                [SecurityAssetProfileAccountingImpactHintDto.CommitmentAccounting, SecurityAssetProfileAccountingImpactHintDto.Valuation, SecurityAssetProfileAccountingImpactHintDto.LedgerClassification])
        ];

    private static SecurityAssetProfileDefinitionDto Profile(
        string profileId,
        string name,
        string category,
        string subType,
        IReadOnlyList<SecurityAssetProfileFieldDefinitionDto> fields,
        IReadOnlyList<SecurityAssetProfileIdentifierPreferenceDto> identifierPreferences,
        IReadOnlyList<string> lifecycleStates,
        IReadOnlyList<SecurityAssetProfileAccountingImpactHintDto> accountingImpactHints)
        => new(
            profileId,
            1,
            name,
            category,
            subType,
            SecurityAssetProfileStatusDto.Approved,
            fields,
            identifierPreferences,
            lifecycleStates,
            accountingImpactHints,
            [],
            EffectiveFrom,
            null,
            "Meridian",
            ApprovedAtUtc,
            "Seeded starter template for governed custom asset profile validation.");

    private static SecurityAssetProfileFieldDefinitionDto RequiredText(string key, string label, bool projected, bool searchable = false)
        => Field(key, label, SecurityAssetProfileFieldTypeDto.Text, true, [], null, null, projected, searchable);

    private static SecurityAssetProfileFieldDefinitionDto OptionalText(string key, string label, bool projected, bool searchable = false)
        => Field(key, label, SecurityAssetProfileFieldTypeDto.Text, false, [], null, null, projected, searchable);

    private static SecurityAssetProfileFieldDefinitionDto RequiredDate(string key, string label, bool projected)
        => Field(key, label, SecurityAssetProfileFieldTypeDto.Date, true, [], null, null, projected, searchable: false);

    private static SecurityAssetProfileFieldDefinitionDto RequiredInteger(string key, string label, decimal? minValue, decimal? maxValue, bool projected)
        => Field(key, label, SecurityAssetProfileFieldTypeDto.Integer, true, [], minValue, maxValue, projected, searchable: false);

    private static SecurityAssetProfileFieldDefinitionDto RequiredDecimal(string key, string label, decimal? minValue, decimal? maxValue, bool projected)
        => Field(key, label, SecurityAssetProfileFieldTypeDto.Decimal, true, [], minValue, maxValue, projected, searchable: false);

    private static SecurityAssetProfileFieldDefinitionDto RequiredEnum(
        string key,
        string label,
        IReadOnlyList<string> allowedValues,
        bool projected,
        bool searchable = false)
        => Field(key, label, SecurityAssetProfileFieldTypeDto.Enum, true, allowedValues, null, null, projected, searchable);

    private static SecurityAssetProfileFieldDefinitionDto Field(
        string key,
        string label,
        SecurityAssetProfileFieldTypeDto fieldType,
        bool isRequired,
        IReadOnlyList<string> allowedValues,
        decimal? minValue,
        decimal? maxValue,
        bool projected,
        bool searchable)
        => new(key, label, fieldType, isRequired, allowedValues, null, minValue, maxValue, projected, searchable);
}
