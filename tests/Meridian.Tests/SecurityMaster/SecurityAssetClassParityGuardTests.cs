using FluentAssertions;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Commit-time parity guards between the Security Master asset-class catalog and the registries that
/// govern per-class behaviour.
/// <para>
/// The catalog was already locked to the F# <c>AssetClassRegistry</c>, the terms schema, and the
/// relational projections. These add the two directions that were unguarded: the VALIDATOR registry
/// (a catalog class with no validator raises Error-severity <c>SM_ASSET_CLASS_UNSUPPORTED</c> for
/// every record of that class, which governed run, ledger and report-pack use gate on) and the ASSET
/// PACK registry in both directions (a pack claiming a class the domain cannot represent publishes
/// coverage the system does not have, straight into the operational readiness report).
/// </para>
/// </summary>
public sealed class SecurityAssetClassParityGuardTests
{
    [Fact]
    public void ValidatorRegistry_CoversExactlyTheCatalogAssetClasses()
    {
        AssetClassValidatorRegistry.CreateDefault().SupportedAssetClasses
            .Should().BeEquivalentTo(
                SecurityAssetClassCatalog.AssetClasses,
                "every catalog asset class needs a validator — without one, SecurityValidationService "
                + "raises Error-severity SM_ASSET_CLASS_UNSUPPORTED for every record of the class");
    }

    [Fact]
    public void AssetPackRegistry_ClaimsOnlyAssetClassesTheCatalogModels()
    {
        SecurityAssetPackRegistry.All
            .SelectMany(static pack => pack.AssetClasses.Select(assetClass => (pack.PackId, assetClass)))
            .Should().OnlyContain(
                claim => SecurityAssetClassCatalog.AssetClasses.Contains(claim.assetClass),
                "a pack that claims a class with no SecurityKind arm, terms schema or validator "
                + "reports coverage the Security Master does not have");
    }

    [Fact]
    public void AssetPackRegistry_PlansOnlyAssetClassesTheCatalogDoesNotModel()
    {
        SecurityAssetPackRegistry.All
            .SelectMany(static pack => pack.PlannedAssetClasses.Select(assetClass => (pack.PackId, assetClass)))
            .Should().OnlyContain(
                planned => !SecurityAssetClassCatalog.AssetClasses.Contains(planned.assetClass),
                "a class the domain now models is present coverage and belongs in AssetClasses");
    }

    [Fact]
    public void AssetPackRegistry_PlannedClassesRetainTheRoadmapVocabulary()
    {
        // The claimed set was trimmed to real classes; the business vocabulary it used to carry is
        // retained rather than dropped, so the readiness surface can still say what is coming.
        SecurityAssetPackRegistry.All
            .SelectMany(static pack => pack.PlannedAssetClasses)
            .Should().Contain(new[] { "Cash", "BankAccount", "Mortgage", "Forward", "ExchangeTradedFund" });
    }

    [Fact]
    public void ValidateDescriptor_RejectsAPackClaimingAClassTheCatalogDoesNotModel()
    {
        var candidate = SecurityAssetPackRegistry.CreateCandidateDescriptor(
            "candidate-pack",
            "Candidate pack",
            ["Equity", "TokenizedCarbonCredit"],
            ["Purchase", "Sale"],
            ["MarketPrice"],
            ["trade"],
            AssetPackAutomationDepth.WideCapture);

        var validation = SecurityAssetPackRegistry.ValidateDescriptor(candidate);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().Contain(issue =>
            issue.Code == "asset-pack.asset-class-not-in-catalog" &&
            issue.Message.Contains("TokenizedCarbonCredit"));
    }

    [Fact]
    public void ValidateDescriptor_RejectsAPlannedClassTheCatalogAlreadyModels()
    {
        var candidate = SecurityAssetPackRegistry.CreateCandidateDescriptor(
            "candidate-pack",
            "Candidate pack",
            ["Equity"],
            ["Purchase", "Sale"],
            ["MarketPrice"],
            ["trade"],
            AssetPackAutomationDepth.WideCapture,
            plannedAssetClasses: ["Bond"]);

        var validation = SecurityAssetPackRegistry.ValidateDescriptor(candidate);

        validation.IsValid.Should().BeFalse();
        validation.Issues.Should().Contain(issue =>
            issue.Code == "asset-pack.planned-asset-class-already-modeled" &&
            issue.Message.Contains("Bond"));
    }

    [Fact]
    public void SupportsIdentifierOnlyImport_MatchesExactlyTheClassesWithNoRequiredTerms()
    {
        // The capability is only honest while it agrees with the terms schema: a class with a
        // REQUIRED asset-specific term cannot be created from identity columns without inventing
        // that term. This keeps the flag from drifting away from the contract it describes.
        var classesWithNoRequiredTerms = SecurityAssetClassCatalog.AssetClasses
            .Where(static assetClass => !SecurityAssetTermsSchema.Fields(assetClass).Any(static field => field.Required))
            .ToArray();

        SecurityAssetClassCatalog.IdentifierOnlyImportableAssetClasses
            .Should().BeEquivalentTo(classesWithNoRequiredTerms);
    }
}
