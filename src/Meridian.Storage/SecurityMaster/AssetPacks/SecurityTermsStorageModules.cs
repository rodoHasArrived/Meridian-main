using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>
/// Explicit compiled storage composition. Declarative entries retain the historic fan-out order;
/// specialized entries describe the existing hand-written writers without adding a second writer.
/// </summary>
internal static class SecurityTermsStorageModules
{
    internal static IReadOnlyList<SecurityTermsStorageContribution> All { get; } = Compose(
    [
        DirectLoanStorageModule.Contribution,
        StructuredCreditStorageModule.Contribution,
        CryptoCurrencyStorageModule.Contribution,
        DepositStorageModule.Contribution,
        MoneyMarketFundStorageModule.Contribution,
        CertificateOfDepositStorageModule.Contribution,
        EquityStorageModule.Contribution,
        FxSpotStorageModule.Contribution,
        FutureStorageModule.Contribution,
        SwapStorageModule.Contribution,
        CommodityStorageModule.Contribution,
        BondStorageModule.Contribution,
        CommercialPaperStorageModule.Contribution,
        TreasuryBillStorageModule.Contribution,
        RepoStorageModule.Contribution,
        CashSweepStorageModule.Contribution,
        OtherSecurityStorageModule.Contribution,
        CfdStorageModule.Contribution,
        WarrantStorageModule.Contribution,
        FxForwardStorageModule.Contribution,
        NonDeliverableForwardStorageModule.Contribution,
        CreditDefaultSwapStorageModule.Contribution,
        SwaptionStorageModule.Contribution,
        CapFloorStorageModule.Contribution,
        StructuredNoteStorageModule.Contribution,
        InvestmentFundStorageModule.Contribution,
        PrivateFundInterestStorageModule.Contribution,
        PrivateCompanyEquityStorageModule.Contribution,
        RealEstateHoldingStorageModule.Contribution,
        CommitmentGuaranteeStorageModule.Contribution,
        OptionStorageModule.Contribution,
        CustomAssetStorageModule.Contribution
    ]);

    private static IReadOnlyList<SecurityTermsStorageContribution> Compose(
        IReadOnlyList<SecurityTermsStorageContribution> contributions)
    {
        var issues = Validate(contributions);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid Security Master storage module composition: {string.Join(" ", issues)}");
        }

        return contributions;
    }

    /// <summary>Checks module identity and disposition independently of SQL-column validation.</summary>
    internal static IReadOnlyList<string> Validate(IReadOnlyList<SecurityTermsStorageContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        var issues = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contribution in contributions)
        {
            if (!seen.Add(contribution.AssetClass))
            {
                issues.Add($"'{contribution.AssetClass}' has more than one storage contribution.");
            }

            if (!SecurityAssetClassCatalog.TryGet(contribution.AssetClass, out var descriptor)
                || !string.Equals(contribution.AssetClass, descriptor.AssetClass, StringComparison.Ordinal))
            {
                issues.Add($"'{contribution.AssetClass}' is not a canonical catalog asset class.");
                continue;
            }

            if (contribution.ProjectionKind != descriptor.ProjectionKind)
            {
                issues.Add($"'{contribution.AssetClass}' storage disposition differs from its catalog projection kind.");
            }

            if (contribution.ProjectionKind == SecurityTermsProjectionKind.Declarative)
            {
                if (contribution.Descriptor is null
                    || !string.Equals(contribution.Descriptor.AssetClass, contribution.AssetClass, StringComparison.Ordinal))
                {
                    issues.Add($"'{contribution.AssetClass}' requires its own declarative projection descriptor.");
                }

                if (contribution.SpecializedWriter is not null)
                {
                    issues.Add($"'{contribution.AssetClass}' cannot register both declarative and specialized writers.");
                }
            }
            else if (contribution.Descriptor is not null || string.IsNullOrWhiteSpace(contribution.DispositionReason))
            {
                issues.Add($"'{contribution.AssetClass}' requires an explicit non-declarative disposition reason and no declarative descriptor.");
            }

            if (contribution.ProjectionKind == SecurityTermsProjectionKind.Specialized && contribution.SpecializedWriter is null)
            {
                issues.Add($"'{contribution.AssetClass}' requires its specialized projection writer.");
            }
            else if (contribution.ProjectionKind == SecurityTermsProjectionKind.TermsJsonOnly && contribution.SpecializedWriter is not null)
            {
                issues.Add($"'{contribution.AssetClass}' terms-JSON-only disposition cannot register a projection writer.");
            }
        }

        foreach (var assetClass in SecurityAssetClassCatalog.AssetClasses)
        {
            if (!seen.Contains(assetClass))
            {
                issues.Add($"'{assetClass}' has no storage contribution.");
            }
        }

        return issues;
    }
}
