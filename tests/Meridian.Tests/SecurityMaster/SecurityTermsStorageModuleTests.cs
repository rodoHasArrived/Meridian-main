using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.SecurityMaster.AssetPacks;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Composition guards and independent pre-module fan-out pins for storage contributions.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityTermsStorageModuleTests
{
    [Fact]
    public void Modules_CoverEveryCanonicalClassWithTheExistingStorageDisposition()
    {
        SecurityTermsStorageModules.Validate(SecurityTermsStorageModules.All).Should().BeEmpty();
        SecurityTermsStorageModules.All.Select(static contribution => contribution.AssetClass)
            .Should().BeEquivalentTo(SecurityAssetClassCatalog.AssetClasses);

        var specialized = SecurityTermsStorageModules.All
            .Where(static contribution => contribution.ProjectionKind == SecurityTermsProjectionKind.Specialized)
            .ToArray();
        specialized.Select(static contribution => contribution.AssetClass)
            .Should().BeEquivalentTo(new[] { "Option", "CustomAsset" });
        specialized.Should().OnlyContain(static contribution =>
            contribution.Descriptor == null && contribution.SpecializedWriter != null
            && !string.IsNullOrWhiteSpace(contribution.DispositionReason));
        specialized.Select(static contribution => contribution.AssetClass)
            .Should().BeEquivalentTo(PostgresSecurityMasterStore.SpecializedProjectionAssetClasses);
    }

    [Fact]
    public void DeclarativeFanOutOrder_MatchesTheRetainedPreModuleRegistry()
    {
        // Captured from the registry before extracting the modules. Order matters when a class
        // change clears a previous writer's rows and publishes a new writer's rows in one mutation.
        SecurityTermsProjectionRegistry.AssetClasses.Should().Equal(
            "DirectLoan", "StructuredCredit", "CryptoCurrency", "Deposit", "MoneyMarketFund",
            "CertificateOfDeposit", "Equity", "FxSpot", "Future", "Swap", "Commodity", "Bond",
            "CommercialPaper", "TreasuryBill", "Repo", "CashSweep", "OtherSecurity", "Cfd",
            "Warrant", "FxForward", "NonDeliverableForward", "CreditDefaultSwap", "Swaption",
            "CapFloor", "StructuredNote", "InvestmentFund", "PrivateFundInterest",
            "PrivateCompanyEquity", "RealEstateHolding", "CommitmentGuarantee");
    }

    [Fact]
    public void Validate_RejectsADuplicateContribution()
    {
        var contributions = SecurityTermsStorageModules.All
            .Append(DepositStorageModule.Contribution).ToArray();

        SecurityTermsStorageModules.Validate(contributions)
            .Should().Contain("'Deposit' has more than one storage contribution.");
    }

    [Fact]
    public void Validate_RejectsAMissingContribution()
    {
        var contributions = SecurityTermsStorageModules.All
            .Where(static contribution => contribution.AssetClass != "Bond").ToArray();

        SecurityTermsStorageModules.Validate(contributions)
            .Should().Contain("'Bond' has no storage contribution.");
    }

    [Fact]
    public void Validate_RejectsAnUnknownOrAliasedClassIdentity()
    {
        var contributions = SecurityTermsStorageModules.All
            .Append(new("CorporateBond", SecurityTermsProjectionKind.Specialized, DispositionReason: "Probe"))
            .Append(new("NotAnAssetClass", SecurityTermsProjectionKind.Specialized, DispositionReason: "Probe"))
            .ToArray();

        var issues = SecurityTermsStorageModules.Validate(contributions);

        issues.Should().Contain("'CorporateBond' is not a canonical catalog asset class.");
        issues.Should().Contain("'NotAnAssetClass' is not a canonical catalog asset class.");
    }

    [Fact]
    public void Validate_RejectsADeclarativeContributionWithoutItsOwnDescriptor()
    {
        var contributions = Replace(DepositStorageModule.Contribution with { Descriptor = null });

        SecurityTermsStorageModules.Validate(contributions)
            .Should().Contain("'Deposit' requires its own declarative projection descriptor.");

        contributions = Replace(DepositStorageModule.Contribution with
        {
            Descriptor = BondStorageModule.Contribution.Descriptor
        });
        SecurityTermsStorageModules.Validate(contributions)
            .Should().Contain("'Deposit' requires its own declarative projection descriptor.");
    }

    [Fact]
    public void Validate_RejectsAChangedDispositionOrAnUnexplainedSpecializedWriter()
    {
        var contributions = Replace(OptionStorageModule.Contribution with
        {
            ProjectionKind = SecurityTermsProjectionKind.Declarative
        });

        SecurityTermsStorageModules.Validate(contributions)
            .Should().Contain("'Option' storage disposition differs from its catalog projection kind.");

        contributions = Replace(CustomAssetStorageModule.Contribution with { DispositionReason = null });
        SecurityTermsStorageModules.Validate(contributions).Should().Contain(
            "'CustomAsset' requires an explicit non-declarative disposition reason and no declarative descriptor.");
    }

    [Fact]
    public void Validate_RejectsAMissingSpecializedWriterOrDuplicateWriterAuthority()
    {
        var contributions = Replace(OptionStorageModule.Contribution with { SpecializedWriter = null });

        SecurityTermsStorageModules.Validate(contributions)
            .Should().Contain("'Option' requires its specialized projection writer.");

        contributions = Replace(DepositStorageModule.Contribution with
        {
            SpecializedWriter = OptionStorageModule.Contribution.SpecializedWriter
        });
        SecurityTermsStorageModules.Validate(contributions)
            .Should().Contain("'Deposit' cannot register both declarative and specialized writers.");
    }

    private static IReadOnlyList<SecurityTermsStorageContribution> Replace(SecurityTermsStorageContribution replacement)
        => SecurityTermsStorageModules.All
            .Select(contribution => contribution.AssetClass == replacement.AssetClass ? replacement : contribution)
            .ToArray();
}
