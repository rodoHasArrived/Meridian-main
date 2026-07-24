using FluentAssertions;
using Meridian.ProviderSdk;
using Meridian.Storage.Store;

namespace Meridian.Tests.Storage;

public sealed class JsonFileIBDataResultStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-ib-results", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UpsertAndRestart_RetainsCompleteLineageAndUsesStableOrdering()
    {
        var store = new JsonFileIBDataResultStore(new IBDataResultStoreOptions { DataRoot = _root });
        await store.UpsertAsync(Result("two", 2, "MSFT", DateTimeOffset.UnixEpoch.AddMinutes(2)));
        await store.UpsertAsync(Result("one", 1, "AAPL", DateTimeOffset.UnixEpoch.AddMinutes(1)));
        await store.UpsertAsync(Result("one", 1, "AAPL", DateTimeOffset.UnixEpoch.AddMinutes(3), status: ProviderDataRequestStatus.Completed));

        var restarted = new JsonFileIBDataResultStore(new IBDataResultStoreOptions { DataRoot = _root });
        var values = await restarted.QueryAsync(new IBDataResultQuery(Capability: "scanner", CapturedFrom: DateTimeOffset.UnixEpoch, Limit: 10));

        values.Select(x => x.ResultIdentity).Should().Equal("two", "one");
        values.Should().HaveCount(2);
        values.Last().LifecycleStatus.Should().Be(ProviderDataRequestStatus.Completed);
        values.Last().Lineage.Should().Be(Result("one", 1, "AAPL", DateTimeOffset.UnixEpoch).Lineage);
        values.Last().NormalizedPayload.Should().Be("{\"kind\":\"scanner\"}");
    }

    [Fact]
    public async Task Query_BoundsByRequestSymbolAccountAndTime()
    {
        var store = new JsonFileIBDataResultStore(new IBDataResultStoreOptions { DataRoot = _root });
        await store.UpsertAsync(Result("a", 1, "AAPL", DateTimeOffset.UnixEpoch, account: "DU1"));
        await store.UpsertAsync(Result("b", 2, "MSFT", DateTimeOffset.UnixEpoch.AddMinutes(1), account: "DU2"));

        var values = await store.QueryAsync(new IBDataResultQuery(RequestIdentity: "2", Symbol: "MSFT", AccountId: "DU2", CapturedFrom: DateTimeOffset.UnixEpoch.AddSeconds(30), Limit: 1));

        values.Should().ContainSingle().Which.ResultIdentity.Should().Be("b");
    }

    private static IBDataResult Result(string identity, int requestId, string symbol, DateTimeOffset capturedAt, string? account = null, ProviderDataRequestStatus status = ProviderDataRequestStatus.Streaming)
    {
        var lineage = new IBDataLineage(requestId, "scanner", symbol, "NASDAQ", "26", "0.01", "TOP", IBMarketDataAvailability.Delayed, true, "streaming", DateTimeOffset.UnixEpoch);
        return new IBDataResult(identity, "interactive-brokers", "scanner", requestId.ToString(), "TOP", symbol, account, capturedAt, status, "{\"kind\":\"scanner\"}", lineage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
