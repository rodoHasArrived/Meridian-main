using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Guards module composition against the independent domain and retained facade contracts.</summary>
public sealed class SecurityAssetClassModuleTests
{
    [Fact]
    public void CompiledModules_CoverEveryDomainClassExactlyOnce()
    {
        var classes = SecurityAssetClassModules.All.Select(static module => module.Descriptor.AssetClass).ToArray();

        classes.Should().OnlyHaveUniqueItems();
        classes.Should().BeEquivalentTo(AssetClassRegistry.assetClasses);
        SecurityAssetClassCatalog.AssetClasses.Should().Equal(classes);
    }

    [Fact]
    public void CatalogAndSchema_UseTheOwnedContributions()
    {
        foreach (var module in SecurityAssetClassModules.All)
        {
            var assetClass = module.Descriptor.AssetClass;
            SecurityAssetClassModules.Find(assetClass.ToLowerInvariant()).Should().BeSameAs(module);
            SecurityAssetClassCatalog.GetOrDefault(assetClass).Should().BeSameAs(module.Descriptor);
            SecurityAssetTermsSchema.Fields(assetClass).Should().BeSameAs(module.Fields);
            foreach (var shape in module.ElementShapes)
                SecurityAssetTermsSchema.ElementFields(assetClass, shape.Key).Should().BeSameAs(shape.Fields);
        }
    }

    [Fact]
    public void ModuleLookup_LeavesVendorAliasResolutionWithTheCatalog()
    {
        SecurityAssetClassModules.Find("BankLoan").Should().BeNull();
        var loan = SecurityAssetClassModules.Find("DirectLoan")!;
        SecurityAssetClassCatalog.GetOrDefault("BankLoan").Should().BeSameAs(loan.Descriptor);
        SecurityAssetClassModules.Find("UnmodeledInstrument").Should().BeNull();
    }

    [Fact]
    public void RateAndGeneratedCodecFacades_UseTheOwnedContributions()
    {
        foreach (var module in SecurityAssetClassModules.All)
        {
            foreach (var declaration in module.RateFields)
                SecurityRateFieldUnits.Find(module.Descriptor.AssetClass, declaration.FieldKey).Should().BeSameAs(declaration);
            SecurityRateFieldUnits.CouponFieldKey(module.Descriptor.AssetClass).Should().Be(module.CouponRateFieldKey);

            if (module.CodecBinding is { } binding)
                SecurityAssetTermsCodecBindings.ByAssetClass[module.Descriptor.AssetClass].Should().BeSameAs(binding);
            SecurityAssetTermsSchema.ReadsProfileResolvedTerms(module.Descriptor.AssetClass)
                .Should().Be(module.CodecBinding?.ReadsResolvedTerms == true);
        }

        SecurityAssetClassModules.All.Where(static module => module.CodecBinding is null)
            .Select(static module => module.Descriptor.AssetClass).Should().Equal("CustomAsset");
        SecurityRateFieldUnits.CouponFieldKey("UnmodeledInstrument").Should().Be("couponRate");
    }

    [Fact]
    public void Composition_RejectsDuplicateAndCrossClassContributions()
    {
        var deposit = SecurityAssetClassModules.Find("Deposit")!;
        var bond = SecurityAssetClassModules.Find("Bond")!;

        var duplicate = () => SecurityAssetClassModules.Compose([deposit, deposit]);
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate*identity*");
        var foreignCodec = () => SecurityAssetClassModules.Compose([deposit with { CodecBinding = bond.CodecBinding }]);
        foreignCodec.Should().Throw<InvalidOperationException>().WithMessage("*another class's codec*");
        var foreignRates = () => SecurityAssetClassModules.Compose([deposit with { RateFields = bond.RateFields }]);
        foreignRates.Should().Throw<InvalidOperationException>().WithMessage("*another class's rate*");
    }

    [Fact]
    public void Composition_RequiresExplicitCodecAndEconomicRecoveryDisposition()
    {
        var deposit = SecurityAssetClassModules.Find("Deposit")!;
        var custom = SecurityAssetClassModules.Find("CustomAsset")!;

        var missingCodec = () => SecurityAssetClassModules.Compose([deposit with { CodecBinding = null }]);
        missingCodec.Should().Throw<InvalidOperationException>().WithMessage("*must supply*codec*economic-recovery*");
        var missingRecovery = () => SecurityAssetClassModules.Compose([deposit with { EconomicFlattenings = null }]);
        missingRecovery.Should().Throw<InvalidOperationException>().WithMessage("*must supply*codec*economic-recovery*");
        var implicitOpaque = () => SecurityAssetClassModules.Compose([custom with { UsesOpaqueProfileCodec = false }]);
        implicitOpaque.Should().Throw<InvalidOperationException>().WithMessage("*must supply*codec*economic-recovery*");
        var contradictory = () => SecurityAssetClassModules.Compose([custom with { CodecBinding = deposit.CodecBinding }]);
        contradictory.Should().Throw<InvalidOperationException>().WithMessage("*Opaque profile*cannot also supply*");
    }

    [Fact]
    public void NestedSchemaEnumeration_RetainsThePreModuleOrder()
    {
        // Public enumeration order also feeds code generation and retained schema manifests.
        // The class-owned shapes remain interleaved exactly as the original schema table exposed.
        SecurityAssetTermsSchema.ElementFieldPaths.Should().Equal(
            "Swap.legs", "Swap.legs.notionalSchedule", "Swap.legs.resetSchedule",
            "Bond.principalSchedule", "DirectLoan.principalSchedule", "Bond.stepSchedule",
            "DirectLoan.covenants", "CommitmentGuarantee.covenants", "StructuredCredit.factorScheduleEntries",
            "Swap.legs.scheduleConventions", "Bond.scheduleConventions", "Bond.bondWorkflow", "CertificateOfDeposit.scheduleConventions",
            "StructuredCredit.scheduleConventions", "Bond.callSchedule", "Bond.putSchedule",
            "CertificateOfDeposit.callSchedule", "CertificateOfDeposit.putSchedule",
            "StructuredCredit.callSchedule", "StructuredCredit.putSchedule", "Equity.convertibleTerms",
            "Equity.preferredTerms", "Equity.preferredTerms.participationTerms",
            "Equity.preferredTerms.liquidationPreference");
    }
}
