using FluentAssertions;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.Ui;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Pins the order of authority by which Trading API reference lookups choose the paper or live
/// host. Serialized with the other tests that change the Alpaca environment variables.
/// </summary>
[Trait("Category", "Unit")]
[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class AlpacaTradingApiTests : IDisposable
{
    private const string Variable = "ALPACA_TRADING_ENVIRONMENT";
    private const string Paper = "https://paper-api.alpaca.markets";
    private const string Live = "https://api.alpaca.markets";

    private readonly string? _original = Environment.GetEnvironmentVariable(Variable);

    public void Dispose() => Environment.SetEnvironmentVariable(Variable, _original);

    [Theory]
    [InlineData(true, "live", "AKLIVEKEY", Paper)]
    [InlineData(false, "paper", "PKPAPERKEY", Live)]
    public void AnExplicitEnvironment_OutranksTheVariableAndTheKey(bool useSandbox, string variable, string keyId, string expected)
    {
        Environment.SetEnvironmentVariable(Variable, variable);

        AlpacaTradingApi.ResolveBaseUrl(useSandbox, keyId).Should().Be(expected);
    }

    [Theory]
    [InlineData("paper", "AKLIVEKEY", Paper)]
    [InlineData("live", "PKPAPERKEY", Live)]
    [InlineData("https://paper-api.alpaca.markets/v2", "AKLIVEKEY", Paper)]
    public void TheVariable_WhenSet_OutranksTheKey(string variable, string keyId, string expected)
    {
        Environment.SetEnvironmentVariable(Variable, variable);

        AlpacaTradingApi.ResolveBaseUrl(null, keyId).Should().Be(expected);
    }

    [Theory]
    // Nothing selected an environment: a paper key goes where it authenticates...
    [InlineData("PKTESTPAPERKEY0000000000", Paper)]
    [InlineData("  PKPADDED", Paper)]
    // ...and everything else stays on the live host these lookups always used.
    [InlineData("AKTESTLIVEKEY00000000000", Live)]
    [InlineData("pklowercase", Live)]
    [InlineData("", Live)]
    [InlineData(null, Live)]
    public void WithNothingSelected_APaperKeyGoesToPaperAndAnyOtherKeyStaysLive(string? keyId, string expected)
    {
        Environment.SetEnvironmentVariable(Variable, null);

        AlpacaTradingApi.ResolveBaseUrl(null, keyId).Should().Be(expected);
    }

    [Fact]
    public void DescribeEnvironment_NamesTheHostFamily()
    {
        AlpacaTradingApi.DescribeEnvironment(Paper).Should().Be("paper");
        AlpacaTradingApi.DescribeEnvironment(Live).Should().Be("live");
    }
}
