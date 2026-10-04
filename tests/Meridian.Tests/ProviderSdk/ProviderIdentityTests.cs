using FluentAssertions;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.ProviderSdk;

public sealed class ProviderIdentityTests
{
    [Theory]
    [InlineData(" ib ", "ibkr")]
    [InlineData("INTERACTIVE-BROKERS", "ibkr")]
    [InlineData("InteractiveBrokers", "ibkr")]
    [InlineData("NasdaqDataLink", "nasdaq")]
    [InlineData("nasdaq-symbols", "nasdaq")]
    [InlineData("nyse-streaming", "nyse")]
    [InlineData("robinhood-live", "robinhood")]
    [InlineData("alpaca-options", "alpaca")]
    [InlineData("twelve_data", "twelvedata")]
    [InlineData("YahooFinance", "yahoo")]
    public void NormalizeId_AcceptedAliasesResolveToCanonicalFamily(string alias, string expected)
    {
        ProviderIdentity.NormalizeId(alias).Should().Be(expected);
        ProviderIdentity.EqualsId(alias, expected).Should().BeTrue();
        ProviderCredentialCatalog.NormalizeProviderId(alias).Should().Be(expected);
    }

    [Fact]
    public void AllAliases_ResolveDirectlyAndNormalizationIsIdempotent()
    {
        foreach (var (alias, canonical) in ProviderIdentity.Aliases)
        {
            ProviderIdentity.NormalizeId($" {alias.ToUpperInvariant()} ").Should().Be(canonical);
            ProviderIdentity.NormalizeId(canonical).Should().Be(canonical);
        }

        foreach (var canonical in ProviderIdentity.CanonicalFamilyIds)
            ProviderIdentity.NormalizeId(canonical).Should().Be(canonical);
    }

    [Theory]
    [InlineData(" Vendor-Options ", "vendor-options")]
    [InlineData("IB-SIM", "ib-sim")]
    [InlineData("ib-flex-web-service", "ib-flex")]
    public void NormalizeId_PreservesPluginAndDistinctIntegrationIdentities(string input, string expected)
    {
        ProviderIdentity.NormalizeId(input).Should().Be(expected);
        ProviderIdentity.EqualsId(input, "ibkr").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NormalizeId_RejectsMissingIdentity(string? input)
    {
        var normalize = () => ProviderIdentity.NormalizeId(input!);
        normalize.Should().Throw<ArgumentException>();
        ProviderIdentity.EqualsId(input, "ibkr").Should().BeFalse();
    }

    [Fact]
    public void CredentialCatalog_UsesCanonicalIdsForEveryDeclaredAdapter()
    {
        ProviderCredentialCatalog.All.Should().OnlyContain(entry =>
            ProviderIdentity.NormalizeId(entry.ProviderId) == entry.ProviderId);
        ProviderCredentialCatalog.Find("interactive-brokers")!.ProviderId.Should().Be("ibkr");
        ProviderCredentialCatalog.Find("nasdaqdatalink")!.ProviderId.Should().Be("nasdaq");
    }
}
