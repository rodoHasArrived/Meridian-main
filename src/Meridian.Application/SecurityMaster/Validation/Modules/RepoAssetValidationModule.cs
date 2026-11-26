using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the Repo asset module.</summary>
internal static class RepoAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("Repo", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "Repo",
            [
                DateOrderRule.Optional("startDate", "endDate", "SM_REPO_DATE_RANGE_INVALID", "Repo date range is invalid", "Repo start date must be on or before end date."),
                FieldRule.OptionalNonNegative("repoRate", "SM_REPO_RATE_INVALID", "Repo rate is invalid", "Repo rates must be zero or greater."),
                FieldRule.OptionalNonNegative("haircut", "SM_REPO_HAIRCUT_INVALID", "Repo haircut is invalid", "Repo haircuts must be zero or greater.")
            ]);
}
