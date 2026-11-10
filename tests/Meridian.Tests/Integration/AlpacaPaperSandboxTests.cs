using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Alpaca;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Risk.Rules;
using Meridian.Strategies.Services;
using Meridian.Tests.Ui;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Meridian.Tests.Integration;

/// <summary>
/// Opt-in, read-only certification against Alpaca's real paper Trading API. A transport guard
/// refuses every mutation, redirect and non-paper host before credentials leave the process.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "LiveProvider")]
[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class AlpacaPaperSandboxTests(ITestOutputHelper output)
{
    [AlpacaPaperSandboxFact]
    public async Task AccountSnapshot_ReconciliationAndRisk_UseRealPaperBrokerEvidence()
    {
        var key = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.KeyIdName);
        var secret = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.SecretKeyName);
        var environment = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.TradingEnvironmentName);
        // Assert presence only: test failures must never print a credential value.
        string.IsNullOrWhiteSpace(key).Should().BeFalse("ALPACA_KEY_ID must be configured for the opted-in sandbox run");
        string.IsNullOrWhiteSpace(secret).Should().BeFalse("ALPACA_SECRET_KEY must be configured for the opted-in sandbox run");
        string.Equals(environment, "paper", StringComparison.Ordinal).Should().BeTrue(
            "ALPACA_TRADING_ENVIRONMENT must explicitly be paper; live or implicit defaults are refused");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var clients = new PaperReadOnlyHttpClientFactory();
        await using var gateway = new AlpacaBrokerageGateway(clients,
            new AlpacaOptions(KeyId: key!, SecretKey: secret!, UseSandbox: true),
            NullLogger<AlpacaBrokerageGateway>.Instance);
        // This smoke deliberately omits the trade-update socket: it certifies the REST
        // portfolio/reconciliation lane and cannot place orders or claim stream recovery proof.
        await gateway.ConnectAsync(timeout.Token);
        var accounts = await gateway.GetAccountsAsync(timeout.Token);
        accounts.Count.Should().Be(1, "Alpaca Trading API credentials identify one account");
        var externalAccountId = accounts[0].AccountId;
        var localAccountId = Guid.NewGuid();

        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);
        await oms.StartAsync();
        var reconciliation = new BrokerageExecutionReconciliationService(
            NullLogger<BrokerageExecutionReconciliationService>.Instance);
        var sync = new LiveBrokeragePortfolioSyncService(() => gateway, () => oms, reconciliation);
        sync.GetStatus(localAccountId).IsReady.Should().BeFalse("a fresh runtime starts without trusted broker evidence");

        LiveBrokeragePortfolioStatus? status = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            status = await sync.SynchronizeAsync(localAccountId, externalAccountId, timeout.Token);
            if (status.IsReady || status.Reconciliation?.Breaks.Count > 0)
                break;
            if (attempt < 2)
                await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }

        var observed = status!;
        output.WriteLine("Paper REST snapshot: complete={0}, fresh={1}, consistent={2}, reconciled={3}, ready={4}; requests={5}.",
            observed.IsComplete, observed.IsFresh, observed.IsConsistent, observed.IsReconciled, observed.IsReady, clients.RequestCount);
        foreach (var reason in observed.BlockingReasons)
            output.WriteLine("Blocking reason: {0}", reason);

        (observed.Snapshot is not null).Should().BeTrue("the broker must provide portfolio evidence");
        observed.Snapshot!.IsComplete.Should().BeTrue("missing provider values cannot be certified as zero");
        observed.IsFresh.Should().BeTrue();
        observed.Snapshot.Balance.Currency.Should().Be("USD");
        (observed.Snapshot.AccountSnapshot is not null).Should().BeTrue();
        (observed.Reconciliation is not null).Should().BeTrue("production order reconciliation must run");

        var exposure = new AggregatePortfolioExposureProvider(new AggregatePortfolioService(new PortfolioRegistry()),
            orderManagerAccessor: () => oms, livePortfolioAccessor: () => sync);
        var riskSnapshot = exposure.GetSnapshot(localAccountId);
        (riskSnapshot.Cash == observed.Snapshot.Balance.Cash).Should().BeTrue();
        (riskSnapshot.BuyingPower == observed.Snapshot.Balance.BuyingPower).Should().BeTrue();
        (riskSnapshot.GrossExposure == observed.Snapshot.Positions.Sum(position => Math.Abs(position.MarketValue))).Should().BeTrue();
        exposure.GetSnapshot(Guid.NewGuid()).IsComplete.Should().BeFalse("another local account cannot borrow this evidence");

        if (observed.Reconciliation!.Breaks.Count > 0)
        {
            observed.IsReady.Should().BeFalse("a fresh OMS must not silently adopt unrelated paper orders");
            riskSnapshot.IsComplete.Should().BeFalse();
            output.WriteLine("Unmatched sandbox orders remain blocked. Use a dedicated paper account with no open orders to certify the ready path.");
        }
        else
        {
            observed.IsReady.Should().BeTrue("stable complete account evidence and a clean broker book must reconcile; check reported blockers");
            riskSnapshot.IsComplete.Should().BeTrue();
        }

        var restartedSync = new LiveBrokeragePortfolioSyncService(() => gateway, () => oms, reconciliation);
        restartedSync.GetStatus(localAccountId).IsReady.Should().BeFalse("restart requires new synchronization");
        await gateway.DisconnectAsync(timeout.Token);
        var decision = await new BrokeragePortfolioStateRule(exposure).EvaluateAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 1m,
            LimitPrice = 1m,
            FundAccountId = localAccountId
        }, timeout.Token);
        decision.IsApproved.Should().BeFalse("disconnect revokes the previously captured account state");
        decision.Code.Should().Be("BROKER_PORTFOLIO_BLOCKED");
        output.WriteLine("Validated account isolation, restart blocking, and disconnect risk blocking. No orders were submitted, changed, or cancelled.");
    }

    [AlpacaPaperSandboxFact]
    public async Task StatementFetchAndConnectionVerification_UseRealPaperBrokerEvidence()
    {
        var key = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.KeyIdName);
        var secret = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.SecretKeyName);
        // Assert presence only: test failures must never print a credential value.
        string.IsNullOrWhiteSpace(key).Should().BeFalse("ALPACA_KEY_ID must be configured for the opted-in sandbox run");
        string.IsNullOrWhiteSpace(secret).Should().BeFalse("ALPACA_SECRET_KEY must be configured for the opted-in sandbox run");
        string.Equals(Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.TradingEnvironmentName), "paper", StringComparison.Ordinal)
            .Should().BeTrue("ALPACA_TRADING_ENVIRONMENT must explicitly be paper; live or implicit defaults are refused");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var clients = new PaperReadOnlyHttpClientFactory(allowActivities: true);
        await using var gateway = new AlpacaBrokerageGateway(clients,
            new AlpacaOptions(KeyId: key!, SecretKey: secret!, UseSandbox: true),
            NullLogger<AlpacaBrokerageGateway>.Instance);
        await gateway.ConnectAsync(timeout.Token);
        var externalAccountId = (await gateway.GetAccountsAsync(timeout.Token)).Single().AccountId;
        (await gateway.CheckHealthAsync(timeout.Token)).IsHealthy.Should().BeTrue();

        // Local scratch only: mapping profiles and the verified-credential record never leave it.
        var scratch = Path.Combine(Path.GetTempPath(), $"meridian-alpaca-paper-statement-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        try
        {
            var connector = new AlpacaActivityStatementConnector(
                new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(scratch)), [gateway], [gateway]);
            connector.Descriptor.SupportsRemoteFetch.Should().BeTrue();

            // The fetched broker snapshot goes through the same parse path as an imported file.
            var document = await connector.FetchAsync(
                new StatementFetchRequest(AlpacaActivityStatementConnector.ConnectorId, externalAccountId), timeout.Token);
            connector.CanHandle(document).Should().BeTrue();
            var statement = await connector.ParseAsync(document, timeout.Token);
            statement.Issues.Should().BeEmpty("a complete broker snapshot must parse without diagnostics");
            statement.AccountSnapshots.Should().ContainSingle();
            statement.Records.Should().ContainSingle(record => record.Kind == StatementRecordKind.CashBalance)
                .Which.Currency.Should().Be("USD");
            var fills = statement.Records.Where(record => record.ProviderActivityCode == "FILL").ToArray();
            fills.Should().OnlyContain(fill => fill.Kind == StatementRecordKind.Transaction && fill.Currency == "USD"
                && fill.Quantity != 0m && fill.Price > 0m && fill.CashAmount == -fill.Quantity * fill.Price
                && !string.IsNullOrWhiteSpace(fill.OrderId) && !string.IsNullOrWhiteSpace(fill.ExternalTransactionId));
            output.WriteLine("Statement snapshot: {0} records, {1} fills, {2} issues.",
                statement.Records.Count, fills.Length, statement.Issues.Count);

            // A bounded period is activity only, and every record must fall inside the half-open window.
            var until = DateTimeOffset.UtcNow;
            var since = until.AddDays(-7);
            var bounded = await connector.ParseAsync(await connector.FetchAsync(new StatementFetchRequest(
                AlpacaActivityStatementConnector.ConnectorId, externalAccountId, since,
                Datasets: StatementFetchDatasets.Activity, UntilExclusive: until), timeout.Token), timeout.Token);
            bounded.Issues.Should().BeEmpty();
            bounded.Records.Should().OnlyContain(record => record.Kind == StatementRecordKind.Transaction
                && record.TradeDate >= DateOnly.FromDateTime(since.UtcDateTime) && record.TradeDate <= DateOnly.FromDateTime(until.UtcDateTime));
            var historicalPositions = await Record.ExceptionAsync(() => connector.FetchAsync(new StatementFetchRequest(
                AlpacaActivityStatementConnector.ConnectorId, externalAccountId, since, UntilExclusive: until), timeout.Token));
            historicalPositions.Should().BeOfType<NotSupportedException>("Alpaca has no portfolio snapshot as of a past instant");
            var foreignAccount = await Record.ExceptionAsync(() => connector.FetchAsync(
                new StatementFetchRequest(AlpacaActivityStatementConnector.ConnectorId, "another-account"), timeout.Token));
            foreignAccount.Should().BeOfType<InvalidOperationException>("credentials for one account must not serve another");

            var connection = new AlpacaBrokerageConnectionService(
                NullLogger<AlpacaBrokerageConnectionService>.Instance, clients, new FileProviderCredentialStore(scratch));
            (await connection.GetStatusAsync(timeout.Token)).IsConfigured.Should().BeFalse();
            var verified = await connection.ConnectAsync(new AlpacaBrokerageConnectionRequestDto(key, secret, "paper"), timeout.Token);
            verified.State.Should().Be(BrokerageConnectionStateDto.Connected);
            verified.Environment.Should().Be("paper");
            verified.LastError.Should().BeNullOrEmpty();
            string.IsNullOrWhiteSpace(verified.ExternalAccountId).Should().BeFalse("verification must record the broker account it reached");
            (await connection.RevokeAsync(timeout.Token)).IsConfigured.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }

        output.WriteLine("Validated statement fetch and parse, period bounds, account scoping, and connection verification with {0} GET requests.",
            clients.RequestCount);
    }

    private sealed class PaperReadOnlyHttpClientFactory(bool allowActivities = false) : IHttpClientFactory, IDisposable
    {
        private readonly PaperReadOnlyHandler _handler = new(allowActivities);
        public int RequestCount => _handler.RequestCount;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        public void Dispose() => _handler.Dispose();
    }

    private sealed class PaperReadOnlyHandler : DelegatingHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);

        private readonly bool _allowActivities;

        public PaperReadOnlyHandler(bool allowActivities) : base(new HttpClientHandler { AllowAutoRedirect = false }) =>
            _allowActivities = allowActivities;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (request.Method != HttpMethod.Get || uri is null || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uri.Host, "paper-api.alpaca.markets", StringComparison.OrdinalIgnoreCase)
                || uri.Port != 443
                || uri.AbsolutePath is not ("/v2/account" or "/v2/positions" or "/v2/orders")
                    && !(_allowActivities && uri.AbsolutePath == "/v2/account/activities"))
                throw new InvalidOperationException("Sandbox certification permits only paper Trading API account, positions, open-order, and account-activity GET requests.");
            Interlocked.Increment(ref _requestCount);
            return base.SendAsync(request, cancellationToken);
        }
    }
}

public sealed class AlpacaPaperSandboxFactAttribute : FactAttribute
{
    public AlpacaPaperSandboxFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MERIDIAN_RUN_ALPACA_PAPER_SANDBOX"), "1", StringComparison.Ordinal))
            Skip = "Real paper-broker validation is opt-in: set MERIDIAN_RUN_ALPACA_PAPER_SANDBOX=1 and the documented paper credentials.";
    }
}
