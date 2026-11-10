using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Core.Config;
using Meridian.Execution.Sdk;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Alpaca;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.Ui;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Meridian.Tests.Integration;

/// <summary>
/// Opt-in, read-only checks of the production Alpaca reference-data and statement adapters
/// against the real paper Trading API. They exist because fixtures can only restate what the
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

    [AlpacaPaperSandboxFact]
    public async Task ActivityStatement_FetchedFromThePaperAccount_ParsesWithoutErrors()
    {
        var (key, secret) = AlpacaPaperSandbox.RequirePaperCredentials();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var clients = new AlpacaPaperSandboxHttpClientFactory(AlpacaPaperSandboxAccess.ReadOnly);
        await using var gateway = new AlpacaBrokerageGateway(clients,
            new AlpacaOptions(KeyId: key, SecretKey: secret, UseSandbox: true),
            NullLogger<AlpacaBrokerageGateway>.Instance);
        await gateway.ConnectAsync(timeout.Token);
        var externalAccountId = (await gateway.GetAccountsAsync(timeout.Token)).Single().AccountId;
        var profileRoot = Path.Combine(Path.GetTempPath(), $"meridian-alpaca-sandbox-{Guid.NewGuid():N}");
        try
        {
            var connector = new AlpacaActivityStatementConnector(
                new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(profileRoot)),
                [gateway],
                [gateway]);
            connector.Descriptor.SupportsRemoteFetch.Should().BeTrue();

            var document = await connector.FetchAsync(
                new StatementFetchRequest(AlpacaActivityStatementConnector.ConnectorId, externalAccountId),
                timeout.Token);
            var parsed = await connector.ParseAsync(document, timeout.Token);

            parsed.Issues.Where(issue => string.Equals(issue.Severity, StatementParseIssue.ErrorSeverity, StringComparison.OrdinalIgnoreCase))
                .Select(issue => issue.Code).Should().BeEmpty("real account activity must parse through the production statement path");
            parsed.Records.Should().Contain(record => record.Kind == StatementRecordKind.CashBalance,
                "a fetched portfolio snapshot always carries the account's cash");
            parsed.Records.Should().OnlyContain(record => !string.IsNullOrWhiteSpace(record.Account));
            (parsed.AccountSnapshots is { Count: 1 }).Should().BeTrue("the fetch reads exactly the credentialed account");
            (parsed.ActivityEvents ?? []).Should().OnlyContain(activity =>
                !string.IsNullOrWhiteSpace(activity.EventId) && !string.IsNullOrWhiteSpace(activity.ProviderCode));
            (parsed.ActivityEvents ?? []).Where(activity => activity.Subtype == BrokerageActivitySubtype.TradeFill)
                .Should().OnlyContain(fill => fill.Currency == "USD" && fill.Quantity != 0m && fill.Price > 0m && !string.IsNullOrWhiteSpace(fill.OrderId));

            // Counts and categories only: never account identifiers, balances or order ids.
            output.WriteLine("Activity statement: records={0}, activity events={1} ({2}), issues={3}; requests={4}.",
                parsed.Records.Count,
                parsed.ActivityEvents?.Count ?? 0,
                string.Join(", ", (parsed.ActivityEvents ?? [])
                    .GroupBy(activity => activity.Subtype)
                    .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
                    .Select(group => $"{group.Key}×{group.Count()}")),
                parsed.Issues.Count,
                clients.RequestCount);
        }
        finally
        {
            if (Directory.Exists(profileRoot))
                Directory.Delete(profileRoot, recursive: true);
        }
    }
}
