using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Validation;

/// <summary>Semantic validation contributed by the Deposit asset module.</summary>
internal static class DepositAssetValidationModule
{
    internal static SecurityAssetValidationContribution Contribution { get; } = new("Deposit", Create);

    private static ISecurityAssetClassValidator Create(ISecurityAssetProfileCatalog assetProfileCatalog)
        => new JsonAssetClassValidator(
            "Deposit",
            // Deposit interestRate, Repo repoRate and Bond spreadBps are SIGNED in
            // SecurityRateFieldUnits (negative-rate deposits, repo specials, negative spreads), so
            // no non-negative rule governs them here; the rate table's bounds apply at consumption.
            // SignedRateValidatorParityTests keeps the two declarations from contradicting again.
            []);
}
