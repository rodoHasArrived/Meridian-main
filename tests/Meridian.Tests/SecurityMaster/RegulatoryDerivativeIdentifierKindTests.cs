using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Regulatory derivatives-reporting identifiers: UTI (ISO 23897), UPI (ISO 4914) and the Markit RED
/// entity (RED6) and pair (RED9) codes. All values are synthetic except the published ANNA DSB UPI
/// example used to pin the check-character algorithm.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RegulatoryDerivativeIdentifierKindTests
{
    // Synthetic LEI with a valid ISO 17442 MOD 97-10 check ("MERIDIANSYNTH00001" + "83").
    private const string SyntheticLei = "MERIDIANSYNTH0000183";

    [Theory]
    [InlineData(SyntheticLei + "TRADE20261105SWAP0001", true)]
    [InlineData(SyntheticLei + "1", true)]
    [InlineData(" meridiansynth0000183-trade.0001 ", true)]
    [InlineData(SyntheticLei + "XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", true)] // 52 characters
    [InlineData(SyntheticLei + "XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", false)] // 53 characters
    [InlineData(SyntheticLei, false)] // LEI alone has no unique value
    [InlineData("MERIDIANSYNTH0000184TRADE0001", false)] // LEI prefix fails MOD 97
    [InlineData("TRADE0001", false)]
    public void Uti_RequiresAValidLeiPrefixAndAtMost52Characters(string value, bool valid)
    {
        SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.Uti, value, out var message)
            .Should().Be(valid, message);
    }

    [Theory]
    [InlineData("QZRBG6ZTKS42", true)] // Published ANNA DSB example.
    [InlineData("qz-rbg6-ztks42", true)]
    [InlineData("QZ1BCD2FGH31", true)] // Synthetic, valid MOD 31,30 check character.
    [InlineData("QZRBG6ZTKS43", false)] // Wrong check character.
    [InlineData("QZRBG6ZTKS4", false)] // 11 characters.
    [InlineData("XZRBG6ZTKS42", false)] // Wrong prefix.
    [InlineData("QZRAG6ZTKS42", false)] // Vowel.
    [InlineData("QZRYG6ZTKS42", false)] // Y is not in the UPI alphabet.
    public void Upi_RequiresQzPrefixUpiAlphabetAndCheckCharacter(string value, bool valid)
    {
        SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.Upi, value, out var message)
            .Should().Be(valid, message);
    }

    [Theory]
    [InlineData(SecurityIdentifierKind.MarkitRedEntity, "2H6677", true)]
    [InlineData(SecurityIdentifierKind.MarkitRedEntity, "2h66-77", true)]
    [InlineData(SecurityIdentifierKind.MarkitRedEntity, "2H667", false)]
    [InlineData(SecurityIdentifierKind.MarkitRedEntity, "2H6677A", false)]
    [InlineData(SecurityIdentifierKind.MarkitRedEntity, "2H6677AA2", false)]
    [InlineData(SecurityIdentifierKind.MarkitRedPair, "2H6677AA2", true)]
    [InlineData(SecurityIdentifierKind.MarkitRedPair, "2H6677", false)]
    [InlineData(SecurityIdentifierKind.MarkitRedPair, "2H6677AA", false)]
    [InlineData(SecurityIdentifierKind.MarkitRedPair, "2H6677AA2B", false)]
    [InlineData(SecurityIdentifierKind.MarkitRedPair, "2H6677ÉA2", false)]
    public void MarkitRed_EntityIsSixAndPairIsNineAlphanumericCharacters(SecurityIdentifierKind kind, string value, bool valid)
    {
        SecurityIdentifierNormalizer.TryValidateFormat(kind, value, out var message).Should().Be(valid, message);
    }

    [Theory]
    [InlineData(SecurityIdentifierKind.Uti, " meridiansynth0000183-trade.0001 ", "MERIDIANSYNTH0000183TRADE0001")]
    [InlineData(SecurityIdentifierKind.Upi, "qz-rbg6 ztks42", "QZRBG6ZTKS42")]
    [InlineData(SecurityIdentifierKind.MarkitRedEntity, "2h-6677", "2H6677")]
    [InlineData(SecurityIdentifierKind.MarkitRedPair, "2h6677 aa2", "2H6677AA2")]
    public void RegulatoryIdentifiers_NormalizeToTheirCompactUpperCaseForm(SecurityIdentifierKind kind, string raw, string expected)
    {
        SecurityIdentifierNormalizer.NormalizeValue(kind, raw).Should().Be(expected);
    }

    [Fact]
    public void SharedReferenceKinds_AreExcludedFromAmbiguityPairing_WhileUtiStillPairs()
    {
        foreach (var kind in new[] { SecurityIdentifierKind.Upi, SecurityIdentifierKind.MarkitRedEntity, SecurityIdentifierKind.MarkitRedPair })
        {
            SecurityIdentifierNormalizer.IsSharedReferenceKind(kind).Should().BeTrue($"{kind} is shared reference data");
            SecurityIdentifierNormalizer.IsExcludedFromAmbiguityPairing(kind).Should().BeTrue($"{kind} is shared by many contracts");
        }

        SecurityIdentifierNormalizer.IsSharedReferenceKind(SecurityIdentifierKind.Uti).Should().BeFalse();
        SecurityIdentifierNormalizer.IsExcludedFromAmbiguityPairing(SecurityIdentifierKind.Uti)
            .Should().BeFalse("a UTI names exactly one trade, so two securities claiming it is a real ambiguity");
    }

    private static readonly string[] OtcDerivativeClasses =
        ["Swap", "CreditDefaultSwap", "Swaption", "CapFloor", "FxForward", "NonDeliverableForward", "Cfd"];

    [Fact]
    public void AssetClassMatrix_PinsWhichClassesMayCarryEachRegulatoryKind()
    {
        SecurityAssetClassCatalog.GetPermittedAssetClasses(SecurityIdentifierKind.Uti)
            .Should().BeEquivalentTo(OtcDerivativeClasses);
        SecurityAssetClassCatalog.GetPermittedAssetClasses(SecurityIdentifierKind.Upi)
            .Should().BeEquivalentTo(OtcDerivativeClasses);
        SecurityAssetClassCatalog.GetPermittedAssetClasses(SecurityIdentifierKind.MarkitRedEntity)
            .Should().BeEquivalentTo(["CreditDefaultSwap"]);
        SecurityAssetClassCatalog.GetPermittedAssetClasses(SecurityIdentifierKind.MarkitRedPair)
            .Should().BeEquivalentTo(["CreditDefaultSwap"]);

        foreach (var assetClass in OtcDerivativeClasses)
        {
            SecurityAssetClassCatalog.TryGet(assetClass, out _).Should().BeTrue($"{assetClass} must be a declared asset class");
            SecurityAssetClassCatalog.GetPreferredIdentifierKinds(assetClass)
                .Should().Contain([SecurityIdentifierKind.Uti, SecurityIdentifierKind.Upi]);
        }

        SecurityAssetClassCatalog.GetPreferredIdentifierKinds("CreditDefaultSwap")
            .Should().Contain([SecurityIdentifierKind.MarkitRedEntity, SecurityIdentifierKind.MarkitRedPair]);

        SecurityAssetClassCatalog.MayCarryIdentifierKind("swap", SecurityIdentifierKind.Uti).Should().BeTrue();
        SecurityAssetClassCatalog.MayCarryIdentifierKind("Equity", SecurityIdentifierKind.Uti).Should().BeFalse();
        SecurityAssetClassCatalog.MayCarryIdentifierKind("Repo", SecurityIdentifierKind.Uti).Should().BeFalse("SFTR UTIs are deferred");
        SecurityAssetClassCatalog.MayCarryIdentifierKind("Option", SecurityIdentifierKind.Upi).Should().BeFalse();
        SecurityAssetClassCatalog.MayCarryIdentifierKind("Swaption", SecurityIdentifierKind.MarkitRedPair).Should().BeFalse();
        SecurityAssetClassCatalog.MayCarryIdentifierKind(null, SecurityIdentifierKind.MarkitRedEntity).Should().BeFalse();
    }

    [Fact]
    public void UnrestrictedKinds_MayBeCarriedByEveryAssetClass()
    {
        var restricted = new[]
        {
            SecurityIdentifierKind.Uti, SecurityIdentifierKind.Upi,
            SecurityIdentifierKind.MarkitRedEntity, SecurityIdentifierKind.MarkitRedPair
        };

        foreach (var kind in Enum.GetValues<SecurityIdentifierKind>().Except(restricted))
        {
            SecurityAssetClassCatalog.IsAssetClassRestrictedIdentifierKind(kind).Should().BeFalse($"{kind} has no asset-class restriction");
            SecurityAssetClassCatalog.MayCarryIdentifierKind("Equity", kind).Should().BeTrue();
            SecurityAssetClassCatalog.MayCarryIdentifierKind("NotARealClass", kind).Should().BeTrue();
        }
    }
}
