using System.Net;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Robinhood;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class RobinhoodReadOnlyBrokerageSyncAdapterTests
{
    [Fact]
    public async Task Scenario_ReadOnlyAggregator_ReturnsThreeRobinhoodAccounts()
    {
        var adapter = CreateAdapter(new Dictionary<string, string>
        {
            ["/accounts"] = """
                          [
                            {
                              "providerId": "robinhood",
                              "accountId": "rh-roth",
                              "displayName": "Robinhood Roth IRA",
                              "status": "active",
                              "currency": "USD",
                              "retrievedAt": "2026-05-07T12:00:00Z",
                              "metadata": { "accountKind": "RothIra" }
                            },
                            {
                              "providerId": "robinhood",
                              "accountId": "rh-trad",
                              "displayName": "Robinhood Traditional IRA",
                              "status": "active",
                              "currency": "USD",
                              "retrievedAt": "2026-05-07T12:00:00Z",
                              "metadata": { "accountKind": "TraditionalIra" }
                            },
                            {
                              "providerId": "robinhood",
                              "accountId": "rh-taxable",
                              "displayName": "Robinhood Brokerage",
                              "status": "active",
                              "currency": "USD",
                              "retrievedAt": "2026-05-07T12:00:00Z",
                              "metadata": { "accountKind": "TaxableBrokerage" }
                            }
                          ]
                          """
        });

        var accounts = await adapter.GetAccountsAsync();

        accounts.Should().HaveCount(3);
        accounts.Select(static account => account.AccountId).Should().Contain(["rh-roth", "rh-trad", "rh-taxable"]);
    }

    [Fact]
    public async Task Scenario_ReadOnlyAggregator_ReturnsPortfolioAndActivitySnapshots()
    {
        var adapter = CreateAdapter(new Dictionary<string, string>
        {
            ["/portfolio/rh-roth"] = """
                                    {
                                      "account": {
                                        "providerId": "robinhood",
                                        "accountId": "rh-roth",
                                        "displayName": "Robinhood Roth IRA",
                                        "status": "active",
                                        "currency": "USD",
                                        "retrievedAt": "2026-05-07T12:00:00Z"
                                      },
                                      "balance": {
                                        "cash": 1200,
                                        "equity": 18200,
                                        "buyingPower": 1200,
                                        "currency": "USD"
                                      },
                                      "positions": [
                                        {
                                          "symbol": "AAPL",
                                          "quantity": 10,
                                          "averageEntryPrice": 150,
                                          "marketPrice": 170,
                                          "marketValue": 1700,
                                          "unrealizedPnl": 200,
                                          "assetClass": "equity",
                                          "description": "Apple Inc.",
                                          "currency": "USD"
                                        }
                                      ],
                                      "retrievedAt": "2026-05-07T12:00:00Z"
                                    }
                                    """,
            ["/activity/rh-roth?since="] = """
                                           {
                                             "providerId": "robinhood",
                                             "accountId": "rh-roth",
                                             "retrievedAt": "2026-05-07T12:00:00Z",
                                             "orders": [],
                                             "fills": [],
                                             "cashTransactions": [
                                               {
                                                 "transactionId": "div-1",
                                                 "transactionType": "DIV",
                                                 "amount": 12.50,
                                                 "currency": "USD",
                                                 "postedAt": "2026-05-06T12:00:00Z",
                                                 "symbol": "AAPL",
                                                 "description": "Dividend"
                                               }
                                             ]
                                           }
                                           """
        });

        var portfolio = await adapter.GetPortfolioSnapshotAsync("rh-roth");
        var activity = await adapter.GetActivitySnapshotAsync("rh-roth");

        portfolio.Account.AccountId.Should().Be("rh-roth");
        portfolio.Balance.Equity.Should().Be(18200m);
        portfolio.Positions.Should().ContainSingle(position => position.Symbol == "AAPL");
        activity.CashTransactions.Should().ContainSingle(transaction => transaction.TransactionType == "DIV");
    }

    [Fact]
    public async Task Scenario_MissingOAuthToken_ReadOnlyAggregatorFailsWithoutCallingEndpoint()
    {
        var adapter = CreateAdapter(
            responses: [],
            accessToken: "");

        var act = async () => await adapter.GetAccountsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ROBINHOOD_BROKERAGE_ACCESS_TOKEN*");
    }

    private static RobinhoodReadOnlyBrokerageSyncAdapter CreateAdapter(
        IReadOnlyDictionary<string, string> responses,
        string accessToken = "test-token")
    {
        var options = new RobinhoodReadOnlyBrokerageOptions(
            AccessToken: accessToken,
            AccountsEndpoint: "https://aggregator.example.test/accounts",
            PortfolioEndpointTemplate: "https://aggregator.example.test/portfolio/{accountId}",
            ActivityEndpointTemplate: "https://aggregator.example.test/activity/{accountId}?since={since}");
        return new RobinhoodReadOnlyBrokerageSyncAdapter(
            new FixedHttpClientFactory(new FixedResponseHandler(responses)),
            options,
            NullLogger<RobinhoodReadOnlyBrokerageSyncAdapter>.Instance);
    }

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://aggregator.example.test")
        };
    }

    private sealed class FixedResponseHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Headers.Authorization?.Scheme.Should().Be("Bearer");
            var key = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (!responses.TryGetValue(key, out var json))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
