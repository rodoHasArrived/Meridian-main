using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the DirectLoan asset module.</summary>
internal static class DirectLoanAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("DirectLoan", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "DirectLoan",
            [
            ]);
}
