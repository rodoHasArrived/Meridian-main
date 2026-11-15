using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The provider asset-class code table: its load-time invariants and its exact-match lookups.
/// </summary>
public sealed class ProviderAssetClassCodeCatalogTests
{
    [Fact]
    public void Table_Loads_AndNamesOnlyCanonicalCatalogClasses()
    {
        ProviderAssetClassCodeCatalog.All.Should().NotBeEmpty();

        foreach (var entry in ProviderAssetClassCodeCatalog.All)
        {
            SecurityAssetClassCatalog.TryGet(entry.CatalogAssetClass, out var descriptor).Should().BeTrue(entry.Code);
            descriptor!.AssetClass.Should().Be(entry.CatalogAssetClass, "the table names canonical classes, not aliases");
            ProviderAssetClassCodeProviders.All.Should().Contain(entry.Provider);
            entry.Roles.Should().NotBe(ProviderAssetClassCodeRoles.None);
        }
    }

    [Fact]
    public void Table_DeclaresNoSpellingTwicePerProvider()
    {
        foreach (var group in ProviderAssetClassCodeCatalog.All.GroupBy(static entry => entry.Provider))
        {
            group.SelectMany(static entry => entry.InputAliases.Prepend(entry.Code))
                .Select(static spelling => spelling.ToLowerInvariant())
                .Should().OnlyHaveUniqueItems($"provider {group.Key} must resolve every spelling to one code");
        }
    }

    [Fact]
    public void EveryProvider_HasCodes()
    {
        foreach (var provider in ProviderAssetClassCodeProviders.All)
        {
            ProviderAssetClassCodeCatalog.ForProvider(provider).Should().NotBeEmpty(provider);
        }
    }

    [Fact]
    public void AlpacaConstants_AreTheAlpacaCodes()
    {
        ProviderAssetClassCodeCatalog.ForProvider(ProviderAssetClassCodeProviders.Alpaca)
            .Select(static entry => entry.Code)
            .Should().BeEquivalentTo(
            [
                AlpacaAssetClassCodes.UsEquity,
                AlpacaAssetClassCodes.UsOption,
                AlpacaAssetClassCodes.Treasury,
                AlpacaAssetClassCodes.Corporate,
                AlpacaAssetClassCodes.Crypto
            ]);
    }

    [Fact]
    public void ResolveLabel_DoesNotCoverTheVendorCodes_WhichIsWhyTheTableExists()
    {
        SecurityAssetClassCatalog.TryResolveLabel("crypto", out _).Should().BeFalse();
        SecurityAssetClassCatalog.TryResolveLabel("treasury", out _).Should().BeFalse();
        SecurityAssetClassCatalog.TryResolveLabel("corporate", out _).Should().BeFalse();

        ProviderAssetClassCodeCatalog.TryResolveExact(ProviderAssetClassCodeProviders.Alpaca, "crypto", out var crypto).Should().BeTrue();
        crypto!.CatalogAssetClass.Should().Be(SecurityAssetClassNames.CryptoCurrency);
        ProviderAssetClassCodeCatalog.TryResolveExact(ProviderAssetClassCodeProviders.Alpaca, "treasury", out var treasury).Should().BeTrue();
        treasury!.CatalogAssetClass.Should().Be(SecurityAssetClassNames.Bond);
    }

    [Theory]
    [InlineData("us_option", true)]
    [InlineData("US_OPTION", false)]
    [InlineData(" us_option", false)]
    [InlineData("option", false)]
    [InlineData(null, false)]
    public void TryResolveExact_IsOrdinalAndUntrimmed_AndIgnoresAliases(string? code, bool expected)
        => ProviderAssetClassCodeCatalog.IsExactCode(ProviderAssetClassCodeProviders.Alpaca, code, SecurityAssetClassNames.Option)
            .Should().Be(expected);

    [Theory]
    [InlineData("option", true)]
    [InlineData("OPTION", true)]
    [InlineData(" option", false)]
    [InlineData("us_option", false)]
    [InlineData("options", false)]
    [InlineData(null, false)]
    public void IsCodeIgnoreCase_IgnoresCaseButNotWhitespaceOrAliases(string? code, bool expected)
        => ProviderAssetClassCodeCatalog.IsCodeIgnoreCase(
                code,
                SecurityAssetClassNames.Option,
                ProviderAssetClassCodeProviders.Robinhood,
                ProviderAssetClassCodeProviders.Meridian)
            .Should().Be(expected);

    [Fact]
    public void InteractiveBrokersReportCodes_TranslateEverySecTypeToTheMeridianVocabulary()
    {
        ProviderAssetClassCodeCatalog.InteractiveBrokersReportCodes.Should().BeEquivalentTo(
            new Dictionary<string, string>
            {
                ["STK"] = "equity",
                ["ETF"] = "equity",
                ["OPT"] = "option",
                ["FUT"] = "futures",
                ["FOP"] = "option",
                ["CASH"] = "forex",
                ["BOND"] = "bond",
                ["GOVT"] = "bond"
            });
        ProviderAssetClassCodeCatalog.InteractiveBrokersReportCodes["stk"].Should().Be("equity", "secType matches ignoring case");
    }
}
