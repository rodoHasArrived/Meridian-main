using System.Text.Json.Nodes;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.FixedIncome;

namespace Meridian.Tests.FixedIncome;

public sealed class BondWorkspaceSnapshotStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-bond-workspace", Guid.NewGuid().ToString("N"));
    private static readonly AccountCashForecastScopeDto Scope = new("tenant-a", "company-a", "account-a", "book-a", "USD");

    [Fact]
    public async Task RetryAndRestart_PreserveOriginalRecord_AndRejectConflictingIdentity()
    {
        var store = new BondWorkspaceSnapshotStore(_root);
        var value = new RetainedPlan("accepted-holdings", 123.456789m);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.RetainAsync(Scope, "plan", "run-1", value)));
        Assert.Equal(value, await new BondWorkspaceSnapshotStore(_root).ReadAsync<RetainedPlan>(Scope, "plan", "run-1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RetainAsync(Scope, "plan", "run-1", value with { Cash = 1m }));
    }

    [Fact]
    public async Task ExactOwnerScope_IsolatesEveryRetainedPlan()
    {
        var store = new BondWorkspaceSnapshotStore(_root);
        await store.RetainAsync(Scope, "plan", "same-id", new RetainedPlan("source", 100m));
        foreach (var other in new[] { Scope with { TenantId = "tenant-b" }, Scope with { CompanyId = "company-b" },
            Scope with { AccountId = "account-b" }, Scope with { BookId = "book-b" } })
            Assert.Null(await store.ReadAsync<RetainedPlan>(other, "plan", "same-id"));
    }

    [Fact]
    public async Task ChangedRetainedBytes_FailClosed()
    {
        var store = new BondWorkspaceSnapshotStore(_root);
        await store.RetainAsync(Scope, "plan", "run", new RetainedPlan("source", 100m));
        var path = Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories).Single();
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        envelope["Payload"] = "{\"Source\":\"substituted\",\"Cash\":1000000}";
        await File.WriteAllTextAsync(path, envelope.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync<RetainedPlan>(Scope, "plan", "run"));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    public sealed record RetainedPlan(string Source, decimal Cash);
}
