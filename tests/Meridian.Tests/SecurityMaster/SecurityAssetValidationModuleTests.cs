using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityAssetValidationModuleTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DefaultComposition_RetainsHistoricalValidatorOrder()
    {
        // Independent pin from the registry before module extraction. Catalog/schema order differs.
        AssetClassValidatorRegistry.CreateDefault().SupportedAssetClasses.Should().Equal(
            "Equity", "Option", "Future", "Bond", "FxSpot", "Deposit", "MoneyMarketFund",
            "CertificateOfDeposit", "CommercialPaper", "TreasuryBill", "Repo", "CashSweep",
            "OtherSecurity", "Swap", "DirectLoan", "StructuredCredit", "PrivateFundInterest",
            "PrivateCompanyEquity", "RealEstateHolding", "CommitmentGuarantee", "Commodity",
            "CryptoCurrency", "Cfd", "Warrant", "FxForward", "NonDeliverableForward",
            "CreditDefaultSwap", "Swaption", "CapFloor", "StructuredNote", "InvestmentFund", "CustomAsset");
    }

    [Fact]
    public void Composition_RejectsMissingClass()
    {
        var contributions = SecurityAssetValidationModules.All.Where(static item => item.AssetClass != "Bond");

        var compose = () => SecurityAssetValidationModules.Compose(contributions, DefaultProfiles());

        compose.Should().Throw<InvalidOperationException>().WithMessage("Missing validation modules for: Bond.");
    }

    [Fact]
    public void Composition_RejectsDuplicateClass()
    {
        var contributions = SecurityAssetValidationModules.All.Concat([SecurityAssetValidationModules.All[0]]);

        var compose = () => SecurityAssetValidationModules.Compose(contributions, DefaultProfiles());

        compose.Should().Throw<InvalidOperationException>().WithMessage("*More than one*Equity*");
    }

    [Theory]
    [InlineData("TokenizedCarbonCredit")]
    [InlineData("bond")]
    public void Composition_RejectsNonCanonicalClass(string assetClass)
    {
        SecurityAssetValidationContribution[] contributions = [new(assetClass, _ => new StubValidator(assetClass))];

        var compose = () => SecurityAssetValidationModules.Compose(contributions, DefaultProfiles());

        compose.Should().Throw<InvalidOperationException>().WithMessage($"*non-canonical*'{assetClass}'*");
    }

    [Fact]
    public void Composition_RejectsFactoryReturningADifferentClass()
    {
        var contributions = SecurityAssetValidationModules.All.Select(static item => item.AssetClass == "Bond"
            ? item with { CreateValidator = _ => new StubValidator("Equity") }
            : item);

        var compose = () => SecurityAssetValidationModules.Compose(contributions, DefaultProfiles());

        compose.Should().Throw<InvalidOperationException>().WithMessage("*'Bond'*different asset class*");
    }

    [Fact]
    public void Composition_RejectsMissingFactory()
    {
        SecurityAssetValidationContribution[] contributions = [new("Bond", null!)];

        var compose = () => SecurityAssetValidationModules.Compose(contributions, DefaultProfiles());

        compose.Should().Throw<InvalidOperationException>().WithMessage("*'Bond'*no validator factory*");
    }

    [Fact]
    public void BondModule_RetainsSemanticIssueOrderAndCanonicalHomeRefusal()
    {
        var bond = DefaultAssetClassValidators.Create(DefaultProfiles()).Single(static item => item.AssetClass == "Bond");

        var issues = bond.Validate(Context("Bond", new
        {
            couponRate = -1m,
            spreadBps = -1m,
            issueDate = "2031-01-01",
            callDate = "2032-01-01",
            maturity = "2030-01-01",
            subclass = "MBS"
        }));

        issues.Select(static issue => issue.Code).Should().Equal(
            "SM_BOND_COUPON_RATE_INVALID",
            "SM_BOND_FLOATING_SPREAD_INVALID",
            "SM_BOND_ISSUE_DATE_INVALID",
            "SM_BOND_CALL_DATE_INVALID",
            "SM_BOND_SECURITIZED_SUBCLASS_NONCANONICAL");
    }

    [Fact]
    public void ProfileModule_UsesTheSuppliedCatalog()
    {
        var context = Context("CustomAsset", new { customProfileId = "structured-credit-io-po", profileVersion = 1 });
        var emptyProfiles = new StaticSecurityAssetProfileCatalog([]);
        var emptyCatalogValidator = DefaultAssetClassValidators.Create(emptyProfiles)
            .Single(static item => item.AssetClass == "CustomAsset");
        var defaultCatalogValidator = DefaultAssetClassValidators.Create(DefaultProfiles())
            .Single(static item => item.AssetClass == "CustomAsset");

        emptyCatalogValidator.Validate(context).Should().ContainSingle()
            .Which.Code.Should().Be("SM_CUSTOM_PROFILE_VERSION_UNKNOWN");
        defaultCatalogValidator.Validate(context).Should()
            .NotContain(static issue => issue.Code == "SM_CUSTOM_PROFILE_VERSION_UNKNOWN");
    }

    [Fact]
    public void CustomRegistryConstructor_RetainsExternalClassSupport()
    {
        var registry = new AssetClassValidatorRegistry([new StubValidator("ExternalClass")]);

        registry.TryGetValidator("externalclass", out var validator).Should().BeTrue();
        validator.AssetClass.Should().Be("ExternalClass");
    }

    private static ISecurityAssetProfileCatalog DefaultProfiles() => StaticSecurityAssetProfileCatalog.CreateDefault();

    private static SecurityValidationContext Context(string assetClass, object terms)
        => new(new SecurityProjectionRecord(
            Guid.Parse("b1fef22b-38d1-4e3d-8d0b-95e4d9694cb0"), assetClass, SecurityStatusDto.Active,
            "Synthetic module fixture", "USD", "Internal", "MODULE-FIXTURE",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms),
            JsonSerializer.SerializeToElement(new { }), 1, EvaluatedAt.AddYears(-1), null, [], []), EvaluatedAt);

    private sealed class StubValidator(string assetClass) : ISecurityAssetClassValidator
    {
        public string AssetClass { get; } = assetClass;
        public IReadOnlyList<SecurityValidationIssueDto> Validate(SecurityValidationContext context) => [];
    }
}
