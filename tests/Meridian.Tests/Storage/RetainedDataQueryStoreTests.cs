using Meridian.Storage.Query;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class RetainedDataQueryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-query-store-" + Guid.NewGuid().ToString("N"));
    private static readonly DataQueryOwner Owner = new("tenant-a", "company-a", "operator-a");
    private DataQueryOptions Options => new() { RetainedResultsRoot = Path.Combine(_root, "private"), MaxRetainedResultBytes = 4096, MaxRetainedTotalBytes = 8192 };
    private RetainedDataQueryStore Store(DataQueryOptions? options = null, TimeProvider? clock = null)
        => new(options ?? Options, Path.Combine(_root, "queryable"), clock);

    [Fact]
    public async Task Complete_PreservesExactRowsAndExecutedSqlAcrossStoreInstances()
    {
        var store = Store();
        await using var sink = await store.CreateAsync(Owner);
        const string sql = "SELECT amount, label FROM original_source ORDER BY amount";
        await sink.BeginAsync(sql, ["amount", "label"], ["DECIMAL(38,8)", "VARCHAR"], default);
        await sink.WriteRowAsync(["-123456789012345678901234567890.12345678", "=SUM(A1:A2)"], default);
        await sink.WriteRowAsync([null, "a\nquoted\" value"], default);
        var receipt = await sink.CompleteAsync(2, default);
        var retained = await Store().ReadAsync(Owner, receipt.ExecutionId);
        Assert.Equal(sql, retained.Manifest.Sql);
        Assert.Equal(2, retained.Manifest.RowCount);
        Assert.Equal("-123456789012345678901234567890.12345678", retained.Rows[0][0]);
        Assert.Null(retained.Rows[1][0]);
        Assert.Equal("a\nquoted\" value", retained.Rows[1][1]);
    }

    [Theory]
    [InlineData("tenant-b", "company-a", "operator-a")]
    [InlineData("tenant-a", "company-b", "operator-a")]
    [InlineData("tenant-a", "company-a", "operator-b")]
    public async Task Read_RefusesEveryForeignOwnerDimension(string tenant, string company, string actor)
    {
        var receipt = await Publish(Store());
        var exception = await Assert.ThrowsAsync<RetainedDataQueryException>(() => Store().ReadAsync(new(tenant, company, actor), receipt.ExecutionId));
        Assert.Equal("NotFound", exception.Code);
    }

    [Fact]
    public async Task SharedPrivateRoot_DoesNotAllowCrossStorageSourceTokenRedemption()
    {
        var options = Options;
        var receipt = await Publish(Store(options));
        var otherSource = new RetainedDataQueryStore(options, Path.Combine(_root, "other-queryable"));
        Assert.Equal("NotFound", (await Assert.ThrowsAsync<RetainedDataQueryException>(() => otherSource.ReadAsync(Owner, receipt.ExecutionId))).Code);
    }

    [Fact]
    public async Task ExpiredReceipt_IsRefusedAndCapacityIsReclaimed()
    {
        var clock = new MutableClock(new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero));
        var options = Options;
        options.RetainedResultMinutes = 1;
        var store = Store(options, clock);
        var receipt = await Publish(store);
        clock.Now = clock.Now.AddMinutes(2);
        Assert.Equal("Expired", (await Assert.ThrowsAsync<RetainedDataQueryException>(() => store.ReadAsync(Owner, receipt.ExecutionId))).Code);
        await using var next = await store.CreateAsync(Owner);
        Assert.False(File.Exists(Path.Combine(store.RootPath, receipt.ExecutionId + ".manifest.json")));
    }

    [Fact]
    public async Task PartialAndTamperedSpools_NeverYieldAnExportablePopulation()
    {
        var store = Store();
        var pending = await store.CreateAsync(Owner);
        await pending.BeginAsync("SELECT 1", ["value"], ["INTEGER"], default);
        await pending.WriteRowAsync(["1"], default);
        var pendingId = Path.GetFileNameWithoutExtension(Assert.Single(Directory.GetFiles(store.RootPath, "*.part")));
        Assert.Equal("NotFound", (await Assert.ThrowsAsync<RetainedDataQueryException>(() => store.ReadAsync(Owner, pendingId))).Code);
        await pending.DisposeAsync();
        Assert.Empty(Directory.GetFiles(store.RootPath, "*.part"));
        var receipt = await Publish(store);
        await File.AppendAllTextAsync(Path.Combine(store.RootPath, receipt.ExecutionId + ".rows"), "[\"injected\"]\n");
        Assert.Equal("Invalid", (await Assert.ThrowsAsync<RetainedDataQueryException>(() => store.ReadAsync(Owner, receipt.ExecutionId))).Code);
    }

    [Fact]
    public async Task RowQuota_FailsBeforePublishingAnyCompleteReceipt()
    {
        var options = Options;
        options.MaxRetainedRows = 1;
        var store = Store(options);
        await using (var sink = await store.CreateAsync(Owner))
        {
            await sink.BeginAsync("SELECT 1", ["value"], ["INTEGER"], default);
            await sink.WriteRowAsync(["1"], default);
            Assert.Equal("Quota", (await Assert.ThrowsAsync<RetainedDataQueryException>(() => sink.WriteRowAsync(["2"], default))).Code);
        }
        Assert.Empty(Directory.GetFiles(store.RootPath, "*.manifest.json"));
        Assert.Empty(Directory.GetFiles(store.RootPath, "*.rows"));
        Assert.Empty(Directory.GetFiles(store.RootPath, "*.reservation"));
    }

    [Fact]
    public async Task IndependentWriters_ReserveQuotaBeforeWriting()
    {
        var options = Options;
        options.MaxRetainedTotalBytes = options.MaxRetainedResultBytes;
        var first = await Store(options).CreateAsync(Owner);
        Assert.Equal("Quota", (await Assert.ThrowsAsync<RetainedDataQueryException>(() => Store(options).CreateAsync(Owner))).Code);
        await first.DisposeAsync();
        await using var second = await Store(options).CreateAsync(Owner);
    }

    [Fact]
    public async Task ByteQuotaAndCancellation_DiscardIncompleteSpools()
    {
        var options = Options;
        options.MaxRetainedResultBytes = 16;
        var store = Store(options);
        await using (var sink = await store.CreateAsync(Owner))
        {
            await sink.BeginAsync("SELECT value", ["value"], ["VARCHAR"], default);
            Assert.Equal("Quota", (await Assert.ThrowsAsync<RetainedDataQueryException>(() => sink.WriteRowAsync([new string('x', 100)], default))).Code);
        }
        await using (var sink = await store.CreateAsync(Owner))
        {
            await sink.BeginAsync("SELECT value", ["value"], ["VARCHAR"], default);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sink.WriteRowAsync(["x"], cancellation.Token));
        }
        Assert.Empty(Directory.GetFiles(store.RootPath, "*.manifest.json"));
        Assert.Empty(Directory.GetFiles(store.RootPath, "*.part"));
    }

    [Fact]
    public void QueryReadableAndAncestorSymlinkRoots_AreRefused()
    {
        var options = Options;
        var queryable = Path.Combine(_root, "queryable");
        options.RetainedResultsRoot = Path.Combine(queryable, "spool");
        Assert.Throws<RetainedDataQueryException>(() => new RetainedDataQueryStore(options, queryable));
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(queryable);
            var alias = Path.Combine(_root, "alias");
            Directory.CreateSymbolicLink(alias, queryable);
            options.RetainedResultsRoot = Path.Combine(alias, "nested", "spool");
            Assert.Throws<RetainedDataQueryException>(() => new RetainedDataQueryStore(options, queryable));
        }
    }

    [Theory]
    [InlineData("../foreign")]
    [InlineData("")]
    public async Task InvalidTokens_AreRefusedWithoutReadingPaths(string token)
        => Assert.Equal("NotFound", (await Assert.ThrowsAsync<RetainedDataQueryException>(() => Store().ReadAsync(Owner, token))).Code);

    private static async Task<DataQueryRetentionReceipt> Publish(RetainedDataQueryStore store)
    {
        await using var sink = await store.CreateAsync(Owner);
        await sink.BeginAsync("SELECT 1", ["value"], ["INTEGER"], default);
        await sink.WriteRowAsync(["1"], default);
        return await sink.CompleteAsync(1, default);
    }
    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
