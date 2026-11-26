using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Meridian.FinancialOperations.AccountingSystem;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class AccountingSystemIntegrationStateStoreTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task IndependentInstances_MergePersistedRowsAndRefreshWithoutLosingOtherWriters()
    {
        using var directory = new StateDirectory();
        var first = new AccountingSystemIntegrationStateStore(directory.Path);
        var second = new AccountingSystemIntegrationStateStore(directory.Path + System.IO.Path.DirectorySeparatorChar);
        var firstRows = new ConcurrentDictionary<string, string>();
        var secondRows = new ConcurrentDictionary<string, string>();
        await first.RetainAsync("mapping-profiles", firstRows, "first", "mapping-a", Options, default);
        await second.RetainAsync("mapping-profiles", secondRows, "second", "mapping-b", Options, default);
        first.Refresh("mapping-profiles", firstRows, Options);
        firstRows.Should().BeEquivalentTo(new Dictionary<string, string> { ["first"] = "mapping-a", ["second"] = "mapping-b" });
        var restarted = new AccountingSystemIntegrationStateStore(directory.Path);
        restarted.Load<string>("mapping-profiles", Options).Should().BeEquivalentTo(firstRows);
    }

    [Fact]
    public async Task StaleUpdate_RefusesToOverwriteAnotherInstancesRetainedRecord()
    {
        using var directory = new StateDirectory();
        var first = new AccountingSystemIntegrationStateStore(directory.Path);
        var firstRows = new ConcurrentDictionary<string, string>();
        await first.RetainAsync("mapping-profiles", firstRows, "profile", "v1", Options, default);
        var second = new AccountingSystemIntegrationStateStore(directory.Path);
        var staleRows = new ConcurrentDictionary<string, string>(second.Load<string>("mapping-profiles", Options));
        await first.RetainAsync("mapping-profiles", firstRows, "profile", "v2", Options, default);
        var stale = () => second.RetainAsync("mapping-profiles", staleRows, "profile", "stale-v3", Options, default);
        await stale.Should().ThrowAsync<InvalidOperationException>().WithMessage("*state changed*reload*");
        second.Load<string>("mapping-profiles", Options)["profile"].Should().Be("v2");
        staleRows["profile"].Should().Be("v1", "a refused write must not publish unretained state");
    }

    [Fact]
    public async Task ConcurrentEquivalentDirectoryWriters_OnlyOneObservedRevisionCanWin()
    {
        using var directory = new StateDirectory();
        var first = new AccountingSystemIntegrationStateStore(directory.Path);
        var firstRows = new ConcurrentDictionary<string, string>();
        await first.RetainAsync("mapping-profiles", firstRows, "profile", "v1", Options, default);
        var second = new AccountingSystemIntegrationStateStore(directory.Path + System.IO.Path.DirectorySeparatorChar);
        var secondRows = new ConcurrentDictionary<string, string>(second.Load<string>("mapping-profiles", Options));
        var expected = AccountingSystemIntegrationStateStore.Capture(firstRows, "profile", Options);
        async Task<bool> Attempt(AccountingSystemIntegrationStateStore store, ConcurrentDictionary<string, string> rows, string value)
        {
            try
            { await store.RetainAsync("mapping-profiles", rows, "profile", value, Options, default, expected); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Attempt(first, firstRows, "a")), Task.Run(() => Attempt(second, secondRows, "b")));
        results.Should().ContainSingle(value => value);
        first.Load<string>("mapping-profiles", Options)["profile"].Should().BeOneOf("a", "b");
    }

    [Fact]
    public async Task ExplicitReviewedRow_RemainsTheExpectedStateAfterCacheRefresh()
    {
        using var directory = new StateDirectory();
        var store = new AccountingSystemIntegrationStateStore(directory.Path);
        var rows = new ConcurrentDictionary<string, string>();
        await store.RetainAsync("export-packages", rows, "package", "ready", Options, default);
        var reviewed = AccountingSystemIntegrationStateStore.Capture(rows, "package", Options);
        var another = new AccountingSystemIntegrationStateStore(directory.Path);
        var anotherRows = new ConcurrentDictionary<string, string>(another.Load<string>("export-packages", Options));
        await another.RetainAsync("export-packages", anotherRows, "package", "certified-a", Options, default);
        store.Refresh("export-packages", rows, Options);
        var staleCertification = () => store.RetainAsync("export-packages", rows, "package", "certified-b", Options, default, reviewed);
        await staleCertification.Should().ThrowAsync<InvalidOperationException>();
        store.Load<string>("export-packages", Options)["package"].Should().Be("certified-a");
    }

    [Fact]
    public async Task ChangedDependency_RefusesPublicationWithoutCreatingAPackage()
    {
        using var directory = new StateDirectory();
        var store = new AccountingSystemIntegrationStateStore(directory.Path);
        var mappings = new ConcurrentDictionary<string, string>();
        var packages = new ConcurrentDictionary<string, string>();
        await store.RetainAsync("mapping-profiles", mappings, "profile", "reviewed", Options, default);
        var expected = AccountingSystemIntegrationStateStore.Capture(mappings, "profile", Options);
        await store.RetainAsync("mapping-profiles", mappings, "profile", "changed", Options, default);
        var prepare = () => store.RetainAsync("export-packages", packages, "package", "approved", Options, default,
            dependencies: [new("mapping-profiles", "profile", expected)]);
        await prepare.Should().ThrowAsync<InvalidOperationException>().WithMessage("*dependency changed*");
        store.Load<string>("export-packages", Options).Should().BeEmpty();
        packages.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingRegisteredCollection_AfterRestartFailsClosed()
    {
        using var directory = new StateDirectory();
        var store = new AccountingSystemIntegrationStateStore(directory.Path);
        await store.RetainAsync("latest-imports", new ConcurrentDictionary<string, string>(), "import", "retained", Options, default);
        File.Delete(System.IO.Path.Combine(directory.Path, "latest-imports.json"));
        var restart = () => new AccountingSystemIntegrationStateStore(directory.Path).Load<string>("latest-imports", Options);
        restart.Should().Throw<InvalidOperationException>().WithMessage("*latest-imports*missing*restore*");
    }

    [Fact]
    public async Task MissingRegistrationReceipt_AfterSuccessfulRetentionCannotBeReadoptedAsNew()
    {
        using var directory = new StateDirectory();
        var store = new AccountingSystemIntegrationStateStore(directory.Path);
        await store.RetainAsync("export-packages", new ConcurrentDictionary<string, string>(), "package", "retained", Options, default);
        File.Delete(System.IO.Path.Combine(directory.Path, "registered-collections.json"));
        var read = () => new AccountingSystemIntegrationStateStore(directory.Path).Load<string>("export-packages", Options);
        read.Should().Throw<InvalidOperationException>().WithMessage("*registration*missing*restore*");
    }

    [Fact]
    public void ExistingCollection_IsAdoptedWithoutRewritingItsRecordsAndLaterLossIsRefused()
    {
        using var directory = new StateDirectory();
        var path = System.IO.Path.Combine(directory.Path, "mapping-profiles.json");
        const string legacyBytes = "{ \"legacy-profile\" : \"reviewed-v1\" }";
        File.WriteAllText(path, legacyBytes);
        var store = new AccountingSystemIntegrationStateStore(directory.Path);
        store.Load<string>("mapping-profiles", Options)["legacy-profile"].Should().Be("reviewed-v1");
        File.ReadAllText(path).Should().Be(legacyBytes);
        File.Delete(path);
        var load = () => new AccountingSystemIntegrationStateStore(directory.Path).Load<string>("mapping-profiles", Options);
        load.Should().Throw<InvalidOperationException>().WithMessage("*missing*restore*");
    }

    [Theory]
    [InlineData("registered-collections.json")]
    [InlineData("registration-established")]
    [InlineData("export-packages.json")]
    public async Task CorruptAuthority_IsRefusedRatherThanReset(string filename)
    {
        using var directory = new StateDirectory();
        var store = new AccountingSystemIntegrationStateStore(directory.Path);
        await store.RetainAsync("export-packages", new ConcurrentDictionary<string, string>(), "package", "retained", Options, default);
        File.WriteAllText(System.IO.Path.Combine(directory.Path, filename), "{ corrupt");
        var read = () => new AccountingSystemIntegrationStateStore(directory.Path).Load<string>("export-packages", Options);
        read.Should().Throw<InvalidOperationException>().WithMessage("*cannot be read*restore*");
    }

    private sealed class StateDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "meridian-gl-state-" + Guid.NewGuid().ToString("N"));
        public StateDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
