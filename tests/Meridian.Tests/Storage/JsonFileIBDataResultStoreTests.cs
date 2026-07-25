using System.Text.Json;
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
        var values = await restarted.QueryAsync(new IBDataResultQuery(
            "tenant-a",
            "company-a",
            Capability: "scanner",
            CapturedFrom: DateTimeOffset.UnixEpoch,
            Limit: 10));

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

        var values = await store.QueryAsync(new IBDataResultQuery(
            "tenant-a",
            "company-a",
            RequestIdentity: "2",
            Symbol: "MSFT",
            AccountId: "DU2",
            CapturedFrom: DateTimeOffset.UnixEpoch.AddSeconds(30),
            Limit: 1));

        values.Should().ContainSingle().Which.ResultIdentity.Should().Be("b");
    }

    [Fact]
    public async Task Scenario_RestartWithMatchingResultIdentities_PreservesTenantAndCompanyIsolation()
    {
        var store = new JsonFileIBDataResultStore(new IBDataResultStoreOptions { DataRoot = _root });
        await store.UpsertAsync(Result(
            "shared-result",
            1,
            "ALPHA",
            DateTimeOffset.UnixEpoch,
            tenantId: "tenant-shared",
            companyId: "company-alpha"));
        await store.UpsertAsync(Result(
            "shared-result",
            1,
            "BETA",
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            tenantId: "tenant-shared",
            companyId: "company-beta"));

        var restarted = new JsonFileIBDataResultStore(new IBDataResultStoreOptions { DataRoot = _root });
        var alpha = await restarted.QueryAsync(new IBDataResultQuery("tenant-shared", "company-alpha"));
        var beta = await restarted.QueryAsync(new IBDataResultQuery("tenant-shared", "company-beta"));

        alpha.Should().ContainSingle().Which.Symbol.Should().Be("ALPHA");
        beta.Should().ContainSingle().Which.Symbol.Should().Be("BETA");
        alpha.Should().NotContain(result => result.CompanyId == "company-beta");
        beta.Should().NotContain(result => result.CompanyId == "company-alpha");
    }

    [Fact]
    public async Task Query_WithoutTenantOrCompanyScope_FailsClosed()
    {
        var store = new JsonFileIBDataResultStore(new IBDataResultStoreOptions { DataRoot = _root });
        await store.UpsertAsync(Result("a", 1, "AAPL", DateTimeOffset.UnixEpoch));

        var withoutTenant = () => store.QueryAsync(new IBDataResultQuery("", "company-a")).AsTask();
        var withoutCompany = () => store.QueryAsync(new IBDataResultQuery("tenant-a", "")).AsTask();

        await withoutTenant.Should().ThrowAsync<ArgumentException>();
        await withoutCompany.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Restart_WithUnscopedLegacyRow_ExcludesItFromEveryScopedQuery()
    {
        var path = Path.Combine(
            _root,
            "provider-results",
            "interactive-brokers",
            "results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var unscoped = Result("legacy", 1, "AAPL", DateTimeOffset.UnixEpoch) with
        {
            TenantId = null!,
            CompanyId = null!
        };
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
                new[] { unscoped },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var restarted = new JsonFileIBDataResultStore(new IBDataResultStoreOptions { DataRoot = _root });
        var values = await restarted.QueryAsync(new IBDataResultQuery("tenant-a", "company-a"));

        values.Should().BeEmpty("legacy rows without authoritative ownership must fail closed");
    }

    private static IBDataResult Result(
        string identity,
        int requestId,
        string symbol,
        DateTimeOffset capturedAt,
        string? account = null,
        ProviderDataRequestStatus status = ProviderDataRequestStatus.Streaming,
        string tenantId = "tenant-a",
        string companyId = "company-a")
    {
        var lineage = new IBDataLineage(requestId, "scanner", symbol, "NASDAQ", "26", "0.01", "TOP", IBMarketDataAvailability.Delayed, true, "streaming", DateTimeOffset.UnixEpoch);
        return new IBDataResult(
            tenantId,
            companyId,
            identity,
            "interactive-brokers",
            "scanner",
            requestId.ToString(),
            "TOP",
            symbol,
            account,
            capturedAt,
            status,
            "{\"kind\":\"scanner\"}",
            lineage);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
