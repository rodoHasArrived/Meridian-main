using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Alpaca;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class AlpacaActivityStatementConnectorTests : IDisposable
{
    private readonly string _root = StatementConnectorTestData.CreateTempRoot("mdc_alpaca_connector");
    private readonly StatementMappingProfileCatalog _catalog;

    public AlpacaActivityStatementConnectorTests()
    {
        _catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
    }

    private AlpacaActivityStatementConnector CreateConnector(
        IBrokerageActivitySync? activitySync = null,
        IBrokeragePortfolioSync? portfolioSync = null)
        => new(
            _catalog,
            activitySync is null ? [] : [activitySync],
            portfolioSync is null ? [] : [portfolioSync]);

    [Fact]
    public async Task Parse_CombinedSnapshot_YieldsTransactionsPositionsCashAndDividends()
    {
        var connector = CreateConnector();
        var document = new StatementSourceDocument(
            "alpaca-combined-snapshot.json",
            StatementConnectorTestData.ReadFixture("alpaca-combined-snapshot.json"));

        connector.CanHandle(document).Should().BeTrue();
        var result = await connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.ProfileId.Should().Be(StatementBuiltInProfiles.AlpacaActivityV1ProfileId);
        result.Records.Should().HaveCount(8);
        result.Records.Should().OnlyContain(record => record.Account == "PA3ALPACA01");

        var byKind = result.Records.GroupBy(record => record.Kind).ToDictionary(group => group.Key, group => group.Count());
        byKind.Should().BeEquivalentTo(new Dictionary<StatementRecordKind, int>
        {
            // Fills plus the CSD wire deposit: cash activities are ledger movements and reconcile in
            // the transaction lane; only the portfolio snapshot's balance is a genuine cash balance.
            [StatementRecordKind.Transaction] = 3,
            [StatementRecordKind.CashBalance] = 1,
            [StatementRecordKind.Fee] = 1,
            [StatementRecordKind.Dividend] = 1,
            [StatementRecordKind.Position] = 2
        });

        var buyFill = result.Records.Single(record => record.ExternalTransactionId == "fill-1001");
        buyFill.Quantity.Should().Be(100m);
        buyFill.CashAmount.Should().Be(-18725m, "buys consume cash");
        buyFill.FeesCommission.Should().Be(1.05m);
        buyFill.TradeDate.Should().Be(new DateOnly(2026, 6, 2));

        var sellFill = result.Records.Single(record => record.ExternalTransactionId == "fill-1002");
        sellFill.Quantity.Should().Be(-50m, "sells are signed negative");
        sellFill.CashAmount.Should().Be(20605m);

        var dividend = result.Records.Single(record => record.Kind == StatementRecordKind.Dividend);
        dividend.Symbol.Should().Be("AAPL");
        dividend.CashAmount.Should().Be(24.00m);
        dividend.TradeDate.Should().Be(new DateOnly(2026, 6, 10));

        var deposit = result.Records.Single(record => record.ExternalTransactionId == "cash-3001");
        deposit.Kind.Should().Be(StatementRecordKind.Transaction, "a wire deposit is a movement with its transaction id intact");
        deposit.CashAmount.Should().Be(50000.00m);

        var balance = result.Records.Single(record => record.Kind == StatementRecordKind.CashBalance);
        balance.ExternalTransactionId.Should().BeNull();
        balance.CashAmount.Should().Be(31247.93m);

        var positions = result.Records.Where(record => record.Kind == StatementRecordKind.Position).ToArray();
        positions.Select(position => position.Symbol).Should().BeEquivalentTo("AAPL", "TLT");
        positions.Single(position => position.Symbol == "AAPL").CashAmount.Should().Be(19010.00m);
    }

    [Fact]
    public async Task Parse_SnapshotAccountDiffersFromAuthorizedDocumentAccount_FailsClosed()
    {
        var connector = CreateConnector();
        var document = new StatementSourceDocument(
            "alpaca-combined-snapshot.json",
            StatementConnectorTestData.ReadFixture("alpaca-combined-snapshot.json"),
            ExternalAccountId: "OTHER-ACCOUNT");

        var result = await connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty();
        result.Issues.Should().ContainSingle(issue => issue.Code == "ACCOUNT_SCOPE_MISMATCH");
    }

    [Fact]
    public void Descriptor_WithoutGateways_DegradesToFileOnly()
    {
        CreateConnector().Descriptor.SupportsRemoteFetch.Should().BeFalse();
        CreateConnector(new FakeActivitySync()).Descriptor.SupportsRemoteFetch.Should().BeTrue();
    }

    [Fact]
    public async Task Fetch_WithoutGateways_Throws()
    {
        var act = () => CreateConnector().FetchAsync(new StatementFetchRequest("alpaca-activity", "PA3ALPACA01"));

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task FetchThenParse_RoundTripsBothDatasets()
    {
        var connector = CreateConnector(new FakeActivitySync(), new FakePortfolioSync());

        var document = await connector.FetchAsync(new StatementFetchRequest("alpaca-activity", "PA3ALPACA01"));
        document.FileName.Should().StartWith("alpaca-activity-PA3ALPACA01-");

        var result = await connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.Records.Should().HaveCount(3);
        result.Records.Select(record => record.Kind).Should().BeEquivalentTo(
        [
            StatementRecordKind.Transaction,
            StatementRecordKind.Position,
            StatementRecordKind.CashBalance
        ]);
    }

    [Fact]
    public async Task Fetch_ActivityDatasetOnly_SkipsPortfolio()
    {
        var connector = CreateConnector(new FakeActivitySync(), new FakePortfolioSync());

        var document = await connector.FetchAsync(
            new StatementFetchRequest("alpaca-activity", "PA3ALPACA01", Datasets: StatementFetchDatasets.Activity));
        var result = await connector.ParseAsync(document);

        result.Records.Should().ContainSingle().Which.Kind.Should().Be(StatementRecordKind.Transaction);
    }

    [Fact]
    public async Task Fetch_ActivitySnapshotAccountDiffersFromRequestedAccount_FailsBeforeRetention()
    {
        var connector = CreateConnector(new MismatchedActivitySync());

        var act = () => connector.FetchAsync(new StatementFetchRequest(
            "alpaca-activity",
            "PA3ALPACA01",
            Datasets: StatementFetchDatasets.Activity));

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*activity snapshot account*requested external account*");
    }

    [Fact]
    public async Task Fetch_BoundedPeriod_ForwardsExactHalfOpenWindow()
    {
        var activitySync = new RecordingBoundedActivitySync();
        var connector = CreateConnector(activitySync);
        var since = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var untilExclusive = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        await connector.FetchAsync(new StatementFetchRequest(
            "alpaca-activity",
            "PA3ALPACA01",
            Since: since,
            Datasets: StatementFetchDatasets.Activity,
            UntilExclusive: untilExclusive));

        activitySync.LastSince.Should().Be(since);
        activitySync.LastUntilExclusive.Should().Be(untilExclusive);
    }

    [Fact]
    public async Task Fetch_BoundedPeriod_WhenProviderCannotEnforceUpperBound_FailsClosed()
    {
        var connector = CreateConnector(new FakeActivitySync());

        var act = () => connector.FetchAsync(new StatementFetchRequest(
            "alpaca-activity",
            "PA3ALPACA01",
            Since: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            Datasets: StatementFetchDatasets.Activity,
            UntilExclusive: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*exact upper time bound*");
    }

    [Fact]
    public async Task Fetch_BoundedPeriod_WithCurrentPortfolioDataset_FailsClosed()
    {
        var connector = CreateConnector(new RecordingBoundedActivitySync(), new FakePortfolioSync());

        var act = () => connector.FetchAsync(new StatementFetchRequest(
            "alpaca-activity",
            "PA3ALPACA01",
            Since: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            UntilExclusive: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*historical portfolio snapshot*");
    }

    private sealed class FakeActivitySync : IBrokerageActivitySync
    {
        public string ProviderId => "alpaca";

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
            => Task.FromResult(new BrokerageActivitySnapshotDto(
                ProviderId,
                externalAccountId,
                new DateTimeOffset(2026, 6, 30, 21, 0, 0, TimeSpan.Zero),
                Orders: [],
                Fills:
                [
                    new BrokerageFillSnapshotDto(
                        "fake-fill-1",
                        "fake-order-1",
                        "AAPL",
                        OrderSide.Buy,
                        10m,
                        187.25m,
                        new DateTimeOffset(2026, 6, 2, 14, 30, 0, TimeSpan.Zero),
                        Commission: 0.55m)
                ],
                CashTransactions: []));
    }

    private sealed class RecordingBoundedActivitySync : IBrokerageActivitySync
    {
        private readonly FakeActivitySync _inner = new();

        public string ProviderId => _inner.ProviderId;

        public DateTimeOffset? LastSince { get; private set; }

        public DateTimeOffset? LastUntilExclusive { get; private set; }

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
            => _inner.GetActivitySnapshotAsync(externalAccountId, since, ct);

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since,
            DateTimeOffset? untilExclusive,
            CancellationToken ct = default)
        {
            LastSince = since;
            LastUntilExclusive = untilExclusive;
            return _inner.GetActivitySnapshotAsync(externalAccountId, since, ct);
        }
    }

    private sealed class MismatchedActivitySync : IBrokerageActivitySync
    {
        public string ProviderId => "alpaca";

        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
            string externalAccountId,
            DateTimeOffset? since = null,
            CancellationToken ct = default)
            => Task.FromResult(new BrokerageActivitySnapshotDto(
                ProviderId,
                "CREDENTIAL-ACCOUNT",
                new DateTimeOffset(2026, 6, 30, 21, 0, 0, TimeSpan.Zero),
                Orders: [],
                Fills: [],
                CashTransactions: []));
    }

    private sealed class FakePortfolioSync : IBrokeragePortfolioSync
    {
        public string ProviderId => "alpaca";

        public Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(
            string externalAccountId,
            CancellationToken ct = default)
        {
            var retrievedAt = new DateTimeOffset(2026, 6, 30, 21, 0, 0, TimeSpan.Zero);
            return Task.FromResult(new BrokeragePortfolioSnapshotDto(
                new BrokerageExternalAccountDto("alpaca", externalAccountId, "Fake", "ACTIVE", "USD", retrievedAt),
                new BrokerageBalanceSnapshotDto(1000m, 2000m, 2000m, "USD"),
                [
                    new BrokeragePositionSnapshotDto("AAPL", 10m, 187.25m, 190.10m, 1901.00m, 28.50m, "us_equity")
                ],
                retrievedAt));
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
