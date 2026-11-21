using System.Globalization;
using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Execution.Models;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData("0.5", 1, "Long", "10", "210")]
    [InlineData("-0.5", 1, "Short", "-10", "210")]
    [InlineData("2", 100, "Long", "4000", "84000")]
    public async Task PortfolioHoldings_UsesExactQuantityAndDerivativeMultiplierAcrossWorkspaces(
        string quantity, int multiplier, string side, string expectedPnl, string expectedExposure)
    {
        var exactQuantity = decimal.Parse(quantity, CultureInfo.InvariantCulture);
        var publisher = new Meridian.Tests.TestHelpers.TestMarketEventPublisher();
        var quotes = new QuoteCollector(publisher);
        quotes.OnQuote(new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow, Symbol: "MSFT", BidPrice: 419.50m, BidSize: 200,
            AskPrice: 420.50m, AskSize: 200, StreamId: "TEST", Venue: "TEST", Source: "TEST"));
        var position = new ExecutionPosition("MSFT", (long)exactQuantity, 400m, 0m, 0m)
        {
            ExactQuantity = exactQuantity,
            ContractMultiplier = multiplier
        };
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
            services.AddSingleton<IPortfolioState>(new LiveMarkTestPortfolioState(position));
            services.AddSingleton(quotes);
        }, currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();
        using var portfolio = await ReadJsonAsync(client, "/api/workstation/portfolio");
        using var trading = await ReadJsonAsync(client, "/api/workstation/trading");
        var holding = portfolio.RootElement.GetProperty("positions")[0];
        var tradingPosition = trading.RootElement.GetProperty("positions")[0];

        holding.GetProperty("quantity").GetString().Should().Be(Math.Abs(exactQuantity).ToString(CultureInfo.InvariantCulture));
        holding.GetProperty("side").GetString().Should().Be(side);
        decimal.Parse(holding.GetProperty("unrealizedPnlExact").GetString()!, CultureInfo.InvariantCulture)
            .Should().Be(decimal.Parse(expectedPnl, CultureInfo.InvariantCulture));
        decimal.Parse(holding.GetProperty("exposureExact").GetString()!, CultureInfo.InvariantCulture)
            .Should().Be(decimal.Parse(expectedExposure, CultureInfo.InvariantCulture));
        foreach (var field in new[] { "quantity", "side", "unrealizedPnlExact", "exposureExact" })
            holding.GetProperty(field).GetString().Should().Be(tradingPosition.GetProperty(field).GetString());
        portfolio.RootElement.GetProperty("risk").GetProperty("grossExposure").GetString()
            .Should().Be(trading.RootElement.GetProperty("risk").GetProperty("grossExposure").GetString());
        portfolio.RootElement.GetProperty("risk").GetProperty("netExposure").GetString()
            .Should().Be(trading.RootElement.GetProperty("risk").GetProperty("netExposure").GetString());
    }
}
