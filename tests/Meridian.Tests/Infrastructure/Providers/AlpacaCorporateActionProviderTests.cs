using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class AlpacaCorporateActionProviderTests
{
    [Fact]
    public async Task FetchAsync_WithDividendAndSplitAnnouncements_ReturnsCombinedCommands()
    {
        var securityId = Guid.NewGuid();
        using var handler = new StubHttpMessageHandler(request =>
        {
            var payload = request.RequestUri?.Query.Contains("ca_types=dividend", StringComparison.Ordinal) == true
                ? BuildAnnouncementsPayload(new
                {
                    ca_type = "dividend",
                    ca_sub_type = "cash",
                    symbol = "AAPL",
                    ex_date = "2024-03-01",
                    record_date = "2024-03-04",
                    payable_date = "2024-03-15",
                    cash = 0.25m,
                    currency = "USD",
                })
                : BuildAnnouncementsPayload(new
                {
                    ca_type = "split",
                    ca_sub_type = "forward",
                    symbol = "AAPL",
                    ex_date = "2024-06-10",
                    old_rate = 1m,
                    new_rate = 4m,
                });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        });

        var provider = CreateSut(handler);

        var results = await provider.FetchAsync("AAPL", securityId, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Select(result => result.ActionType).Should().Equal("Dividend", "Split");
        results.Select(result => result.SourceProvider).Should().OnlyContain(providerId => providerId == "alpaca");
        results[0].Amount.Should().Be(0.25m);
        results[1].SplitFromFactor.Should().Be(1m);
        results[1].SplitToFactor.Should().Be(4m);
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task FetchAsync_WithoutCredentials_ReturnsEmptyWithoutCreatingClient()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ALPACA_KEY_ID"] = string.Empty,
                ["ALPACA_SECRET_KEY"] = string.Empty,
            })
            .Build();
        var provider = new AlpacaCorporateActionProvider(
            factory,
            configuration,
            NullLogger<AlpacaCorporateActionProvider>.Instance);

        var results = await provider.FetchAsync("AAPL", Guid.NewGuid(), CancellationToken.None);

        results.Should().BeEmpty();
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    private static AlpacaCorporateActionProvider CreateSut(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var client = new HttpClient(handler);
        factory.CreateClient("alpaca-corp-actions").Returns(client);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ALPACA_KEY_ID"] = "test-key",
                ["ALPACA_SECRET_KEY"] = "test-secret",
            })
            .Build();

        return new AlpacaCorporateActionProvider(
            factory,
            configuration,
            NullLogger<AlpacaCorporateActionProvider>.Instance);
    }

    private static string BuildAnnouncementsPayload(object announcement)
    {
        return JsonSerializer.Serialize(new[] { announcement });
    }
}
