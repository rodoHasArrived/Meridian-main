using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// A registered asset-class alias names its catalog class all the way through the codec: the F#
/// mapping switches on canonical names, so an alias must be canonicalized before it is mapped
/// rather than refused as unknown on write or degraded to OtherSecurity on read.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityAssetClassAliasMappingTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Loan", "DirectLoan")]
    [InlineData("directloan", "DirectLoan")]
    [InlineData("MBS", "StructuredCredit")]
    [InlineData("CorporateBond", "Bond")]
    [InlineData("PrivateFund", "PrivateFundInterest")]
    [InlineData(" Equity ", "Equity")]
    public void Canonicalize_ResolvesCatalogNamesAndAliasesInAnyCasing(string submitted, string expected)
        => SecurityAssetClassCatalog.Canonicalize(submitted).Should().Be(expected);

    [Fact]
    public void Canonicalize_LeavesAnUnknownSpellingUnchanged()
        => SecurityAssetClassCatalog.Canonicalize("Equitiy").Should().Be("Equitiy");

    [Fact]
    public void CreateCommand_ForAnAliasedClass_MapsToTheCanonicalKind()
    {
        var result = SecurityMasterCommandFacade.Create(SecurityMasterMapping.ToCreateCommand(BondRequest("CorporateBond")));

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.ErrorDetails.Select(error => error.Message)));
        result.Snapshot.AssetClass.Should().Be("Bond", "the alias is the Bond class, not an unrecognized one");
    }

    [Fact]
    public void CreateCommand_ForAnUnknownClass_IsStillRefused()
    {
        var create = () => SecurityMasterMapping.ToCreateCommand(BondRequest("Equitiy"));

        create.Should().Throw<InvalidOperationException>().WithMessage("*Equitiy*");
    }

    private static CreateSecurityRequest BondRequest(string assetClass)
        => new(Guid.NewGuid(), assetClass,
            JsonSerializer.SerializeToElement(new { displayName = "Alias bond", currency = "USD" }),
            JsonSerializer.SerializeToElement(new
            {
                issueDate = "2026-01-15",
                maturity = "2027-01-15",
                couponType = "ZeroCoupon",
                par = 100m,
                paymentFrequency = "Annual",
                isCallable = false,
                subclass = "Corporate"
            }),
            [new(SecurityIdentifierKind.InternalCode, "BOND-ALIAS", true, AsOf)],
            AsOf, "tests", "tests", null, "Alias canonicalization regression");
}
