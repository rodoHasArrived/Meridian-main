using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>A class-owned semantic validator factory; schema validation remains shared.</summary>
internal sealed record SecurityAssetValidationContribution(
    string AssetClass,
    Func<ISecurityAssetProfileCatalog, ISecurityAssetClassValidator> CreateValidator);

/// <summary>
/// Explicit, closed-world composition of class validation modules. Registration order is retained
/// for compatibility; class facts remain owned by the canonical catalog and each module's rules.
/// </summary>
internal static class SecurityAssetValidationModules
{
    internal static IReadOnlyList<SecurityAssetValidationContribution> All { get; } =
        Array.AsReadOnly<SecurityAssetValidationContribution>(
    [
        EquityAssetValidationModule.Contribution,
        OptionAssetValidationModule.Contribution,
        FutureAssetValidationModule.Contribution,
        BondAssetValidationModule.Contribution,
        FxSpotAssetValidationModule.Contribution,
        DepositAssetValidationModule.Contribution,
        MoneyMarketFundAssetValidationModule.Contribution,
        CertificateOfDepositAssetValidationModule.Contribution,
        CommercialPaperAssetValidationModule.Contribution,
        TreasuryBillAssetValidationModule.Contribution,
        RepoAssetValidationModule.Contribution,
        CashSweepAssetValidationModule.Contribution,
        OtherSecurityAssetValidationModule.Contribution,
        SwapAssetValidationModule.Contribution,
        DirectLoanAssetValidationModule.Contribution,
        StructuredCreditAssetValidationModule.Contribution,
        PrivateFundInterestAssetValidationModule.Contribution,
        PrivateCompanyEquityAssetValidationModule.Contribution,
        RealEstateHoldingAssetValidationModule.Contribution,
        CommitmentGuaranteeAssetValidationModule.Contribution,
        CommodityAssetValidationModule.Contribution,
        CryptoCurrencyAssetValidationModule.Contribution,
        CfdAssetValidationModule.Contribution,
        WarrantAssetValidationModule.Contribution,
        FxForwardAssetValidationModule.Contribution,
        NonDeliverableForwardAssetValidationModule.Contribution,
        CreditDefaultSwapAssetValidationModule.Contribution,
        SwaptionAssetValidationModule.Contribution,
        CapFloorAssetValidationModule.Contribution,
        StructuredNoteAssetValidationModule.Contribution,
        InvestmentFundAssetValidationModule.Contribution,
        CustomAssetAssetValidationModule.Contribution,
    ]);

    internal static IReadOnlyList<ISecurityAssetClassValidator> Compose(
        IEnumerable<SecurityAssetValidationContribution> contributions,
        ISecurityAssetProfileCatalog assetProfileCatalog)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(assetProfileCatalog);

        var canonicalClasses = SecurityAssetClassCatalog.AssetClasses.ToHashSet(StringComparer.Ordinal);
        var registeredClasses = new HashSet<string>(StringComparer.Ordinal);
        var validators = new List<ISecurityAssetClassValidator>();
        foreach (var contribution in contributions)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            if (!canonicalClasses.Contains(contribution.AssetClass))
                throw new InvalidOperationException($"Validation module names non-canonical asset class '{contribution.AssetClass}'.");
            if (!registeredClasses.Add(contribution.AssetClass))
                throw new InvalidOperationException($"More than one validation module names asset class '{contribution.AssetClass}'.");
            if (contribution.CreateValidator is null)
                throw new InvalidOperationException($"Validation module '{contribution.AssetClass}' has no validator factory.");

            var validator = contribution.CreateValidator(assetProfileCatalog);
            if (validator is null || !string.Equals(validator.AssetClass, contribution.AssetClass, StringComparison.Ordinal))
                throw new InvalidOperationException($"Validation module '{contribution.AssetClass}' returned a validator for a different asset class.");
            validators.Add(validator);
        }

        var missingClasses = canonicalClasses.Except(registeredClasses).Order(StringComparer.Ordinal).ToArray();
        if (missingClasses.Length != 0)
            throw new InvalidOperationException($"Missing validation modules for: {string.Join(", ", missingClasses)}.");

        return validators.AsReadOnly();
    }
}
