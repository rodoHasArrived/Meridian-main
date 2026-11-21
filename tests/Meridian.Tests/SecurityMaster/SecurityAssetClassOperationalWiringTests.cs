using FluentAssertions;
using FluentAssertions.Execution;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Complements the codec, validator and projection parity guards with the operational surface:
/// a new domain kind must appear in readiness, and every alias must route evidence to one class.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetClassOperationalWiringTests
{
    [Fact]
    public async Task EveryDomainKind_HasExactlyOneRegistryAndRuntimeReadinessRow()
    {
        // Start at the domain union, rather than the registry under test: omitting a new class
        // from both the catalog and readiness must still fail this guard.
        var domainKinds = FSharpType.GetUnionCases(typeof(SecurityKind), null)
            .Select(static unionCase => unionCase.Name)
            .ToArray();
        var result = await new SecurityMasterOperationalReadinessService()
            .GetReadinessAsync(new SecurityMasterOperationalReadinessRequest());

        using var scope = new AssertionScope();
        SecurityAssetClassCatalog.AssetClasses.Should().BeEquivalentTo(domainKinds);
        SecurityAssetPackRegistry.ReadinessSpecifications.Select(static specification => specification.AssetClass)
            .Should().BeEquivalentTo(domainKinds,
                "every domain class needs readiness metadata, including classes introduced after this test");
        result.AssetClasses.Select(static row => row.AssetClass).Should().BeEquivalentTo(domainKinds,
            "readiness must publish every domain class exactly once, without a private partial list");
        result.AssetClasses.Should().OnlyContain(static row =>
            row.EvidenceRequirements.Any(static requirement => requirement.Category == "SecurityMaster") &&
            row.EvidenceRequirements.Any(static requirement => requirement.Category == "ProviderEvidence") &&
            row.EvidenceRequirements.Any(static requirement => requirement.Category == "Ledger") &&
            row.EvidenceRequirements.Any(static requirement => requirement.Category == "Reconciliation"));
        result.AssetClasses.SelectMany(static row => row.Blockers).Should().NotContain(static blocker =>
            blocker.Code.EndsWith(":catalog-missing", StringComparison.Ordinal) ||
            blocker.Code.EndsWith(":validator-missing", StringComparison.Ordinal),
            "a visible row must be backed by a real catalog definition and validator");
    }

    public static TheoryData<string, string> CatalogNamesAndAliases()
    {
        var data = new TheoryData<string, string>();
        foreach (var descriptor in SecurityAssetClassCatalog.All)
        {
            data.Add(descriptor.AssetClass, descriptor.AssetClass);
            foreach (var alias in descriptor.Aliases ?? [])
            {
                data.Add(alias, descriptor.AssetClass);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogNamesAndAliases))]
    public async Task EveryRegisteredName_FiltersToExactlyItsCanonicalReadinessRow(string name, string canonicalClass)
    {
        SecurityAssetPackRegistry.ResolveAssetClass($"  {name.ToLowerInvariant()}  ")
            .Should().Be(canonicalClass);

        var result = await new SecurityMasterOperationalReadinessService().GetReadinessAsync(
            new SecurityMasterOperationalReadinessRequest(AssetClass: name));

        result.AssetClasses.Should().ContainSingle().Which.AssetClass.Should().Be(canonicalClass);
    }

    [Theory]
    [InlineData("MBS")]
    [InlineData("CLO")]
    [InlineData("ABS")]
    [InlineData("CMBS")]
    public async Task SecuritizedEvidence_IsAssignedOnlyToStructuredCredit(string assetClassAlias)
    {
        const string retainedKind = "Unique trustee remittance evidence";
        var snapshot = new SecurityMasterOperationalEvidenceSnapshot(
            ProviderId: "trustee",
            ExternalAccountId: "structured-account",
            ReconciliationStatus: "Matched",
            ReconciliationDetailPath: null,
            EvidenceItems:
            [
                new SecurityMasterOperationalEvidenceItem(
                    EvidenceId: "structured-evidence",
                    Category: "Reconciliation",
                    EvidenceKind: retainedKind,
                    Status: "Blocked",
                    Source: "trustee-report",
                    AssetClass: assetClassAlias)
            ]);

        var result = await new SecurityMasterOperationalReadinessService().GetReadinessAsync(
            new SecurityMasterOperationalReadinessRequest(EvidenceSnapshot: snapshot));

        var rowsUsingEvidence = result.AssetClasses
            .Where(row => row.ReconciliationSignals["retainedEvidence"].Contains(retainedKind, StringComparison.Ordinal))
            .ToArray();
        rowsUsingEvidence.Should().ContainSingle().Which.AssetClass.Should().Be("StructuredCredit",
            "one securitized asset's evidence must not satisfy or block a Bond, CustomAsset or OtherSecurity row");
        rowsUsingEvidence[0].Status.Should().Be("Blocked");
    }

    [Fact]
    public async Task EveryEvidenceGatedClass_RemainsGatedWithoutAccountEvidence()
    {
        var result = await new SecurityMasterOperationalReadinessService()
            .GetReadinessAsync(new SecurityMasterOperationalReadinessRequest());

        foreach (var specification in SecurityAssetPackRegistry.ReadinessSpecifications.Where(static specification => specification.HardBlocker))
        {
            var row = result.AssetClasses.Single(row => row.AssetClass == specification.AssetClass);
            row.Status.Should().NotBe("Ready", $"{specification.AssetClass} requires account-scoped evidence");
            row.EvidenceRequirements.Should().Contain(static requirement =>
                requirement.Category == "ProviderEvidence" && requirement.Status == "ReviewRequired");
        }
    }

    [Fact]
    public async Task GenericReadinessProfiles_RemainUnderReviewEvenWithCompleteReadyEvidence()
    {
        var genericSpecifications = SecurityAssetPackRegistry.ReadinessSpecifications
            .Where(static specification => specification.RequiresOperationalProfileReview)
            .ToArray();
        genericSpecifications.Should().NotBeEmpty("the catalog includes classes whose operational integration has not been declared");
        var service = new SecurityMasterOperationalReadinessService();

        foreach (var specification in genericSpecifications)
        {
            var comprehensiveEvidence = string.Join(" ",
                specification.ProviderFeeds.Concat(specification.ReconciliationSignals).Append(specification.LedgerClassification));
            var snapshot = new SecurityMasterOperationalEvidenceSnapshot(
                ProviderId: "complete-provider",
                ExternalAccountId: "complete-account",
                ReconciliationStatus: "Matched",
                ReconciliationDetailPath: null,
                EvidenceItems:
                [
                    new SecurityMasterOperationalEvidenceItem(
                        EvidenceId: $"{specification.AssetClass}:ready-evidence",
                        Category: "ShadowBook",
                        EvidenceKind: comprehensiveEvidence,
                        Status: "Ready",
                        Source: "retained-account-evidence",
                        AssetClass: specification.AssetClass)
                ]);

            var result = await service.GetReadinessAsync(new SecurityMasterOperationalReadinessRequest(
                AssetClass: specification.AssetClass,
                EvidenceSnapshot: snapshot));

            var row = result.AssetClasses.Should().ContainSingle().Subject;
            row.EvidenceRequirements.Should().Contain(static requirement =>
                requirement.Category == "ProviderEvidence" && requirement.Status == "Ready",
                "the evidence is comprehensive, so the remaining review gate must be the undeclared integration");
            row.Status.Should().Be("ReviewRequired",
                $"{specification.AssetClass} cannot claim operational integration from generic evidence alone");
            row.Blockers.Should().Contain(static blocker => blocker.Severity == "Review");
        }
    }

    [Fact]
    public void ReclassifyingProfiles_TargetProfileCapableCanonicalClasses()
    {
        foreach (var (profileId, assetClass) in SecurityAssetPackRegistry.ProfileAssetClasses)
        {
            profileId.Should().NotBeNullOrWhiteSpace("profile mappings also cover runtime-governed profiles that are not seeded by default");
            SecurityAssetClassCatalog.TryGet(assetClass, out var descriptor).Should().BeTrue();
            descriptor!.AssetClass.Should().Be(assetClass);
            descriptor.SupportsProfileBackedTerms.Should().BeTrue();
        }

        // The lookup is derived from what each class declares, and is exactly the reviewed set.
        SecurityAssetPackRegistry.ProfileAssetClasses.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["structured-credit-io-po"] = "StructuredCredit",
            ["private-fund-interest"] = "PrivateFundInterest",
            ["private-company-equity"] = "PrivateCompanyEquity",
            ["real-estate-holding"] = "RealEstateHolding",
            ["commitment-guarantee"] = "CommitmentGuarantee"
        });
        SecurityAssetClassCatalog.All.SelectMany(static descriptor => descriptor.ReclassifyingProfileIds)
            .Should().OnlyHaveUniqueItems("a profile id resolves to one class");
    }

    [Fact]
    public void RoutingKeys_AreOwnedByEachClass_AndAreTheNameAliasesAndDeclaredLabels()
    {
        // The classification labels each class declares; these four sets were a class-name switch in
        // the pack registry and are unchanged in content.
        var declaredLabels = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["PrivateFundInterest"] = ["PrivateFunds"],
            ["PrivateCompanyEquity"] = ["PrivateEquity"],
            ["RealEstateHolding"] = ["RealEstate", "RealEstateInterest"],
            ["CommitmentGuarantee"] = ["UnfundedCommitment", "Guarantee"]
        };

        foreach (var descriptor in SecurityAssetClassCatalog.All)
        {
            descriptor.ClassificationLabels.Should().Equal(
                declaredLabels.GetValueOrDefault(descriptor.AssetClass, []), descriptor.AssetClass);
            descriptor.RoutingKeys.Should().Equal(
                [descriptor.AssetClass, .. descriptor.Aliases ?? [], .. descriptor.ClassificationLabels], descriptor.AssetClass);
            SecurityAssetClassCatalog.ClassesOwningRoutingKey(descriptor.AssetClass)
                .Should().Contain(owner => owner.Descriptor.AssetClass == descriptor.AssetClass);
        }

        // No key routes to two classes, so metadata that names a class names exactly one.
        var owners = SecurityAssetClassCatalog.All
            .SelectMany(static descriptor => descriptor.RoutingKeys.Select(key => (
                Key: new string(key.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant(), descriptor.AssetClass)))
            .GroupBy(static pair => pair.Key, static pair => pair.AssetClass)
            .Where(static group => group.Distinct().Count() > 1)
            .Select(static group => $"{group.Key}: {string.Join(", ", group.Distinct())}");
        owners.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Real Estate", "RealEstate", true)]
    [InlineData("real-estate", "RealEstate", true)]
    [InlineData("REALESTATE", "RealEstate", true)]
    [InlineData("RealEstateFund", "RealEstate", false)]
    [InlineData("Estate", "RealEstate", false)]
    [InlineData(null, "RealEstate", false)]
    public void MatchesRoutingKey_ComparesTheWholeLabel_IgnoringCaseAndPunctuation(string? value, string key, bool expected)
        => SecurityAssetClassCatalog.MatchesRoutingKey(value, key).Should().Be(expected);

    [Fact]
    public void ClassesOwningRoutingKey_ResolvesNamesAliasesAndLabels_AndNothingForUnknownText()
    {
        SecurityAssetClassCatalog.ClassesOwningRoutingKey("Private Equity").Should().ContainSingle()
            .Which.Should().Match<(SecurityAssetClassDescriptor Descriptor, string RoutingKey)>(owner =>
                owner.Descriptor.AssetClass == "PrivateCompanyEquity" && owner.RoutingKey == "PrivateEquity");
        SecurityAssetClassCatalog.ClassesOwningRoutingKey("MBS").Should().ContainSingle()
            .Which.Descriptor.AssetClass.Should().Be("StructuredCredit");
        SecurityAssetClassCatalog.ClassesOwningRoutingKey("Guarantee").Should().ContainSingle()
            .Which.Descriptor.AssetClass.Should().Be("CommitmentGuarantee");
        SecurityAssetClassCatalog.ClassesOwningRoutingKey("guaranteed note").Should().BeEmpty("a key is matched whole");
        SecurityAssetClassCatalog.ClassesOwningRoutingKey(" ").Should().BeEmpty();
    }

    [Fact]
    public void DepthTargets_AreUniqueAndBelongToClassesTheDomainModels()
    {
        var domainKinds = FSharpType.GetUnionCases(typeof(SecurityKind), null)
            .Select(static unionCase => unionCase.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (assetClass, targets) in SecurityAssetPackRegistry.ReadinessDepthTargets)
        {
            domainKinds.Should().Contain(assetClass);
            targets.Should().NotBeEmpty();
            targets.Select(static target => target.KeySuffix).Should().OnlyHaveUniqueItems(assetClass);
            targets.Select(static target => target.TargetType).Should().OnlyHaveUniqueItems(assetClass);
        }
    }
}
