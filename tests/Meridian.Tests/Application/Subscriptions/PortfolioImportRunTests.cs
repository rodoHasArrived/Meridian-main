using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.Core.Subscriptions.Models;
using Meridian.Ledger;

namespace Meridian.Tests.Application.Subscriptions;

public sealed class PortfolioImportRunTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-import-runs-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _draftId = Guid.NewGuid();

    [Fact]
    public async Task PartialOpeningFailure_ResumesRetainedPopulationAfterRestartWithoutRefetch()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            calls++;
            return request.RequestUri!.AbsolutePath.EndsWith("positions", StringComparison.Ordinal)
                ? """[{"symbol":"AAPL","qty":"2","avg_entry_price":"100","asset_class":"us_equity"},{"symbol":"MSFT","qty":"3","avg_entry_price":"50","asset_class":"us_equity"}]"""
                : """{"account_number":"test-account","cash":"25"}""";
        }));
        var queue = new Queue { Fail = true, Id = _draftId };
        var service = Service(client, queue);
        var request = Request() with { Options = new(ExcludeSymbols: ["MSFT"], CreateWatchlist: true, BrokerAccountId: "test-account") };
        var first = await service.RunAsync(request);
        first.Status.Should().Be(PortfolioImportRunStatus.Partial);
        first.Subscriptions!.ImportedSymbols.Should().Equal("AAPL");
        first.Snapshot!.Positions.Should().HaveCount(2, "opening balances include positions excluded from subscriptions");
        queue.Fail = false;
        using var offline = new HttpClient(new Handler(_ => throw new InvalidOperationException("No refetch permitted")));
        var restarted = Service(offline, queue);
        var second = await restarted.RunAsync(request);
        second.Status.Should().Be(PortfolioImportRunStatus.Succeeded);
        second.OpeningJournalEntryId.Should().Be(_draftId);
        calls.Should().Be(2);
        var watchlists = await new WatchlistService(Configuration()).GetAllWatchlistsAsync();
        watchlists.Should().ContainSingle().Which.Symbols.Should().Equal("AAPL");
        var draft = PortfolioOpeningBalanceProjector.Build(second);
        draft.IsBalanced.Should().BeTrue();
        draft.Lines.Where(l => l.account.Name == "Securities").Sum(l => l.debit).Should().Be(350m);
        draft.Lines.Single(l => l.account.Name == "Capital Account").credit.Should().Be(375m);
        (await restarted.RunAsync(request)).Should().BeEquivalentTo(second);
        queue.Calls.Should().Be(2);
    }

    [Fact]
    public async Task OpeningWithoutRetainedEntity_RefusesBeforeFetchingOrApplyingSubscriptions()
    {
        using var client = new HttpClient(new Handler(_ => throw new InvalidOperationException("The provider must not be read before entity validation.")));
        var request = Request();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(client, new Queue { Id = _draftId })
            .RunAsync(request with { Scope = request.Scope with { EntityId = null } }));
    }

    [Fact]
    public async Task ReboundRunCommandOrCorruptSnapshot_IsRejected()
    {
        using var client = Client();
        var service = Service(client, new Queue { Id = _draftId });
        var request = Request();
        await service.RunAsync(request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(request with { Broker = "ib" }));
        var path = Directory.GetFiles(Path.Combine(_root, "data", "portfolio-imports"), "*.json", SearchOption.AllDirectories).Single();
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["Snapshot"]!["CashBalance"] = 9999m;
        await File.WriteAllTextAsync(path, json.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(request));
    }

    [Fact]
    public async Task LostSubscriptionReceipt_RecoversOriginalPlanAndWatchlist()
    {
        using var client = Client();
        var service = Service(client, new Queue { Id = _draftId });
        var request = Request() with { Options = new(CreateWatchlist: true, BrokerAccountId: "test-account") };
        await service.RunAsync(request);
        var path = Directory.GetFiles(Path.Combine(_root, "data", "portfolio-imports"), "*.json", SearchOption.AllDirectories).Single();
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["Status"] = (int)PortfolioImportRunStatus.Partial;
        json["Subscriptions"] = null;
        json["WatchlistId"] = null;
        await File.WriteAllTextAsync(path, json.ToJsonString());
        var resumed = await Service(client, new Queue { Id = _draftId }).RunAsync(request);
        resumed.Subscriptions!.ImportedSymbols.Should().Equal("AAPL");
        (await new WatchlistService(Configuration()).GetAllWatchlistsAsync()).Should().ContainSingle().Which.Symbols.Should().Equal("AAPL");
    }

    [Theory]
    [InlineData("OPT", "long", 1)]
    [InlineData("us_equity", "short", 1)]
    public void Opening_RejectsUnknownPriceConventionsAndInconsistentDirection(string assetClass, string side, decimal quantity)
    {
        var request = Request();
        var snapshot = new PortfolioSummary("alpaca", "test-account", null, 25m, null,
            [new("AAPL", quantity, null, 100m, null, assetClass, null, "USD", side)], DateTimeOffset.UtcNow);
        var run = new PortfolioImportRun(request, "command", PortfolioImportRunStatus.Partial, snapshot.RetrievedAt,
            snapshot.RetrievedAt, snapshot, PortfolioImportRunService.SnapshotHash(snapshot));
        Assert.Throws<InvalidOperationException>(() => PortfolioOpeningBalanceProjector.Build(run));
    }

    [Fact]
    public void Opening_RefusesMissingCashOrCostAndBalancesShortLiabilities()
    {
        var request = Request();
        var snapshot = new PortfolioSummary("alpaca", "test-account", null, 225m, null,
            [new("AAPL", -2m, null, 100m, null, "us_equity", null, "USD", "short")], DateTimeOffset.UtcNow);
        PortfolioImportRun Run(PortfolioSummary source) => new(request, "command", PortfolioImportRunStatus.Partial,
            source.RetrievedAt, source.RetrievedAt, source, PortfolioImportRunService.SnapshotHash(source));
        var draft = PortfolioOpeningBalanceProjector.Build(Run(snapshot));
        draft.IsBalanced.Should().BeTrue();
        draft.Lines.Single(l => l.account.Name == "Short Securities Payable").credit.Should().Be(200m);
        draft.Lines.Single(l => l.account.Name == "Capital Account").credit.Should().Be(25m);
        Assert.Throws<InvalidOperationException>(() => PortfolioOpeningBalanceProjector.Build(Run(snapshot with { CashBalance = null })));
        Assert.Throws<InvalidOperationException>(() => PortfolioOpeningBalanceProjector.Build(Run(snapshot with
        { Positions = [snapshot.Positions[0] with { AverageCost = null }] })));
    }

    private PortfolioImportRunRequest Request() => new(Guid.NewGuid(), "alpaca", new(BrokerAccountId: "test-account"),
        new("tenant-a", "company-a", Guid.NewGuid().ToString("D"), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow), "operator-a", Guid.NewGuid()), PrepareOpeningBalances: true);

    private ConfigStore Configuration()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appsettings.json");
        if (!File.Exists(path))
            File.WriteAllText(path, JsonSerializer.Serialize(new AppConfig(DataRoot: Path.Combine(_root, "data"))
            { Alpaca = new AlpacaOptions("synthetic-key", "synthetic-secret") }));
        return new ConfigStore(path);
    }

    private PortfolioImportRunService Service(HttpClient client, Queue queue)
    {
        var config = Configuration();
        var watchlists = new WatchlistService(config);
        return new(config, new PortfolioImportService(config, watchlists, client), watchlists, queue);
    }

    private static HttpClient Client() => new(new Handler(request => request.RequestUri!.AbsolutePath.EndsWith("positions", StringComparison.Ordinal)
        ? """[{"symbol":"AAPL","qty":"2","avg_entry_price":"100","asset_class":"us_equity"}]"""
        : """{"account_number":"test-account","cash":"25"}"""));

    private sealed class Queue : IPortfolioOpeningBalanceQueue
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public Guid Id { get; init; }
        public Task<Guid> QueueAsync(PortfolioImportRun run, CancellationToken ct = default)
        {
            Calls++;
            return Fail ? Task.FromException<Guid>(new IOException("Synthetic interruption before draft receipt")) : Task.FromResult(Id);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request)) });
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
