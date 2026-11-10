using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.Ui;
using Xunit.Abstractions;

namespace Meridian.Tests.Integration;

/// <summary>
/// Opt-in, read-only checks of the production Alpaca reference-data adapters against the real
/// paper Trading API. They exist because fixtures can only restate what the
/// adapter already assumes about the wire contract: the host a paper key authenticates against,
/// the parameters an endpoint insists on, and whether a number arrives as a string.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "LiveProvider")]
[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class AlpacaPaperSandboxReferenceDataTests(ITestOutputHelper output)
{
    [AlpacaPaperSandboxFact]
    public async Task SymbolSearch_ResolvesEquityAndCryptoAssetsOnThePaperHost()
    {
        var (key, secret) = AlpacaPaperSandbox.RequirePaperCredentials();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var clients = new AlpacaPaperSandboxHttpClientFactory(AlpacaPaperSandboxAccess.ReadOnly);
        using var httpClient = clients.CreateClient("alpaca-symbol-search");
        using var provider = new AlpacaSymbolSearchProvider(key, secret, httpClient, useSandbox: true);

        var equities = await provider.SearchAsync("AAPL", limit: 5, ct: timeout.Token);
        var apple = await provider.GetDetailsAsync(new SymbolId("AAPL"), timeout.Token);
        var cryptos = await provider.SearchAsync("BTC", limit: 50, assetType: "crypto", ct: timeout.Token);
        var bitcoin = await provider.GetDetailsAsync(new SymbolId("BTC/USD"), timeout.Token);

        equities.Should().Contain(result => result.Symbol == "AAPL" && result.AssetType == "Stock",
            "a paper key pair must be able to search the paper Trading API's asset list");
        (apple is not null).Should().BeTrue("asset details must resolve on the paper host");
        apple!.TradingEligibility!.IsTradable.Should().BeTrue();
        apple.TradingEligibility.MaintenanceMarginRequirement.Should().BeGreaterThan(0m);

        cryptos.Should().Contain(result => result.Symbol == "BTC/USD" && result.AssetType == "Crypto",
            "crypto assets carry string-encoded increments that must not discard the list");
        (bitcoin is not null).Should().BeTrue("the slash in a crypto symbol is part of the asset path");
        bitcoin!.TradingEligibility!.MinimumOrderSize.Should().BeGreaterThan(0m);
        bitcoin.TradingEligibility.MinimumTradeIncrement.Should().BeGreaterThan(0m);
        bitcoin.TradingEligibility.PriceIncrement.Should().BeGreaterThan(0m);

        output.WriteLine("Symbol search: equity matches={0}, crypto matches={1}; requests={2}.",
            equities.Count, cryptos.Count, clients.RequestCount);
    }

    [AlpacaPaperSandboxFact]
    public async Task OptionContracts_ListExpirationsBeyondTheDefaultWindowAndTheirStrikes()
    {
        var (key, secret) = AlpacaPaperSandbox.RequirePaperCredentials();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var clients = new AlpacaPaperSandboxHttpClientFactory(AlpacaPaperSandboxAccess.ReadOnly);
        using var httpClient = clients.CreateClient("alpaca-options");
        var provider = new AlpacaOptionsChainProvider(clients, key, secret, httpClient, useSandbox: true);

        var expirations = await provider.GetExpirationsAsync("AAPL", timeout.Token);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        expirations.Should().NotBeEmpty("an optionable underlying lists contracts on the paper Trading API");
        expirations.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        expirations.Should().OnlyContain(expiration => expiration >= today.AddDays(-1));
        expirations[^1].Should().BeAfter(today.AddDays(180),
            "the endpoint stops at the upcoming weekend unless the horizon is stated");

        var strikes = await provider.GetStrikesAsync("AAPL", expirations[0], timeout.Token);

        strikes.Should().NotBeEmpty("strike_price arrives as a JSON string and must still be read");
        strikes.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        strikes.Should().OnlyContain(strike => strike > 0m);

        output.WriteLine("Option contracts: expirations={0} (nearest {1:yyyy-MM-dd}, furthest {2:yyyy-MM-dd}), strikes at nearest={3}; requests={4}.",
            expirations.Count, expirations[0], expirations[^1], strikes.Count, clients.RequestCount);
    }
}
