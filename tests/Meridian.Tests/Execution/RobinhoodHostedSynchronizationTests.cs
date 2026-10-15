using System.Net;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Execution.Sdk;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Execution;

public sealed class RobinhoodHostedSynchronizationTests
{
    [Theory]
    [InlineData("accounts", false)]
    [InlineData("portfolio", false)]
    [InlineData("activity", false)]
    [InlineData("accounts", true)]
    [InlineData("portfolio", true)]
    [InlineData("activity", true)]
    public async Task AlreadyResolvedSync_ObservesOAuthConnectRotationAndRevocation(
        string capability, bool hasStartupToken)
    {
        var hostConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ROBINHOOD_BROKERAGE_ACCOUNTS_ENDPOINT"] = "https://bridge.example.test/accounts",
            ["ROBINHOOD_BROKERAGE_PORTFOLIO_ENDPOINT_TEMPLATE"] = "https://bridge.example.test/portfolio/{accountId}",
            ["ROBINHOOD_BROKERAGE_ACTIVITY_ENDPOINT_TEMPLATE"] = "https://bridge.example.test/activity/{accountId}",
            ["ROBINHOOD_BROKERAGE_AUTHORIZATION_ENDPOINT"] = "https://bridge.example.test/authorize",
            ["ROBINHOOD_BROKERAGE_TOKEN_ENDPOINT"] = "https://bridge.example.test/token",
            ["ROBINHOOD_BROKERAGE_CLIENT_ID"] = "test-client",
            ["ROBINHOOD_BROKERAGE_REDIRECT_URI"] = "https://meridian.example.test/callback",
            ["ROBINHOOD_BROKERAGE_ACCESS_TOKEN"] = hasStartupToken ? "startup-token" : null
        }).Build();
        var configuration = new CompositionConfiguration(hostConfiguration);
        var handler = new OAuthAndSyncHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<IHttpClientFactory>(new HttpClientFactory(handler));
        services.AddSingleton(BrokerageConnectionOptions.RobinhoodFromConfiguration(configuration));
        services.AddSingleton<BrokerageConnectionService>();
        services.AddHostedBrokerageGateways();
        await using var provider = services.BuildServiceProvider();

        // Resolve each singleton before OAuth changes the startup credential state.
        var accounts = provider.GetServices<IBrokerageAccountCatalog>().Single(x => x.ProviderId == "robinhood");
        var portfolios = provider.GetServices<IBrokeragePortfolioSync>().Single(x => x.ProviderId == "robinhood");
        var activity = provider.GetServices<IBrokerageActivitySync>().Single(x => x.ProviderId == "robinhood");
        Func<Task> read = capability switch
        {
            "accounts" => async () => { await accounts.GetAccountsAsync(); }
            ,
            "portfolio" => async () => { await portfolios.GetPortfolioSnapshotAsync("account-1"); }
            ,
            _ => async () => { await activity.GetActivitySnapshotAsync("account-1"); }
        };
        if (hasStartupToken)
        {
            await read();
            handler.SyncTokens.Should().Equal("Bearer startup-token");
        }
        else
        {
            await read.Should().ThrowAsync<InvalidOperationException>();
            handler.SyncTokens.Should().BeEmpty();
        }

        var oauth = provider.GetRequiredService<BrokerageConnectionService>();
        for (var rotation = 1; rotation <= 2; rotation++)
        {
            await oauth.StartConnectionAsync();
            var status = await oauth.CompleteCallbackAsync("authorization-code",
                configuration["ROBINHOOD_BROKERAGE_OAUTH_STATE"], error: null);
            status.IsConnected.Should().BeTrue();
            await read();
        }

        string[] expectedTokens = hasStartupToken
            ? ["Bearer startup-token", "Bearer token-1", "Bearer token-2"]
            : ["Bearer token-1", "Bearer token-2"];
        handler.SyncTokens.Should().Equal(expectedTokens);
        await oauth.RevokeAsync();
        await read.Should().ThrowAsync<InvalidOperationException>();
        handler.SyncTokens.Should().HaveCount(hasStartupToken ? 3 : 2,
            "revocation must prevent stale-token requests");
    }

    private sealed class HttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class OAuthAndSyncHandler : HttpMessageHandler
    {
        private int _tokenExchanges;
        public List<string> SyncTokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string json;
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                json = $$"""{"access_token":"token-{{++_tokenExchanges}}","expires_in":3600}""";
            }
            else
            {
                request.Method.Should().Be(HttpMethod.Get);
                SyncTokens.Add(request.Headers.Authorization!.ToString());
                json = request.RequestUri.AbsolutePath == "/accounts" ? "[]" : "{}";
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
