using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class FileBrokerageOrderRecoveryStoreTests
{
    [Theory]
    [InlineData(OrderStatus.Filled, 10, "broker-1")]
    [InlineData(OrderStatus.Rejected, 0, null)]
    public void TryCompact_SafeTerminalOrder_RetainsPermanentIdentityAcrossRestart(
        OrderStatus status, int filledQuantity, string? brokerOrderId)
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        var retained = Order(status, filledQuantity, brokerOrderId);
        store.Save(retained);

        store.TryCompact("ord-1").Should().BeTrue();
        store.Load().Should().BeEmpty();
        store.LoadTombstones().Should().ContainSingle().Which.Should().Be(
            new BrokerageOrderTombstone("ord-1", status, 10m, filledQuantity));

        var reopened = directory.CreateStore();
        reopened.ScopeIdentity.Should().Be("alpaca:paper:account-1");
        reopened.Load().Should().BeEmpty();
        reopened.LoadTombstones().Should().Equal(store.LoadTombstones());
        reopened.TryCompact("ord-1").Should().BeFalse();
        var overwrite = () => reopened.Save(retained);
        overwrite.Should().Throw<InvalidOperationException>();
        var reopenRecovery = () => reopened.RequireRecovery("ord-1");
        reopenRecovery.Should().Throw<InvalidOperationException>();
        directory.CreateStore().LoadTombstones().Should().Equal(store.LoadTombstones());
    }

    [Theory]
    [InlineData(OrderStatus.PendingNew, 0, null, true)]
    [InlineData(OrderStatus.Accepted, 0, "broker-1", false)]
    [InlineData(OrderStatus.PartiallyFilled, 4, "broker-1", false)]
    [InlineData(OrderStatus.Cancelled, 0, "broker-1", false)]
    [InlineData(OrderStatus.Expired, 0, "broker-1", false)]
    [InlineData(OrderStatus.Rejected, 0, "broker-1", false)]
    [InlineData(OrderStatus.Rejected, 4, null, false)]
    [InlineData(OrderStatus.Filled, 4, "broker-1", false)]
    [InlineData(OrderStatus.Filled, 10, "broker-1", true)]
    [InlineData(OrderStatus.Rejected, 0, null, true)]
    public void TryCompact_UncertainOrPotentialLateFillState_PreservesFullEvidence(
        OrderStatus status, int filledQuantity, string? brokerOrderId, bool requiresRecovery)
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        var retained = Order(status, filledQuantity, brokerOrderId) with { RequiresRecovery = requiresRecovery };
        store.Save(retained);

        store.TryCompact("ord-1").Should().BeFalse();

        store.Load().Should().ContainSingle().Which.Should().Be(retained);
        store.LoadTombstones().Should().BeEmpty();
        directory.CreateStore().Load().Should().ContainSingle().Which.Should().BeEquivalentTo(retained);
    }

    [Fact]
    public void Save_AnotherOrder_PreservesCompactedIdentity()
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        store.Save(Order(OrderStatus.Filled, 10, "broker-1"));
        store.TryCompact("ord-1").Should().BeTrue();
        var working = Order(OrderStatus.Accepted, 0, "broker-2");
        store.Save(working with { State = working.State with { OrderId = "ord-2" } });

        var reopened = directory.CreateStore();

        reopened.Load().Should().ContainSingle(order => order.State.OrderId == "ord-2");
        reopened.LoadTombstones().Should().ContainSingle(order => order.OrderId == "ord-1");
    }

    [Fact]
    public void TryCompact_RecoveryRequiredAfterCandidateSelection_RechecksCurrentState()
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        store.Save(Order(OrderStatus.Filled, 10, "broker-1"));
        store.Load().Should().ContainSingle(order => !order.RequiresRecovery);
        store.RequireRecovery("ord-1");

        store.TryCompact("ord-1").Should().BeFalse();

        store.Load().Should().ContainSingle(order => order.RequiresRecovery);
        store.LoadTombstones().Should().BeEmpty();
    }

    [Fact]
    public void TryCompact_AtomicWriteFails_LeavesFullStateInMemory()
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        store.Save(Order(OrderStatus.Filled, 10, "broker-1"));
        var original = File.ReadAllText(directory.Path);
        File.Delete(directory.Path);
        Directory.CreateDirectory(directory.Path);

        var compact = () => store.TryCompact("ord-1");

        var failure = compact.Should().Throw<Exception>().Which;
        (failure is IOException or UnauthorizedAccessException).Should().BeTrue();
        store.Load().Should().ContainSingle();
        store.LoadTombstones().Should().BeEmpty();
        Directory.Delete(directory.Path);
        File.WriteAllText(directory.Path, original);
        directory.CreateStore().Load().Should().ContainSingle();
    }

    [Fact]
    public void VersionOneSnapshot_LoadsAndUpgradesOnNextWrite()
    {
        using var directory = new StoreDirectory();
        var original = directory.CreateStore();
        original.Save(Order(OrderStatus.Accepted, 0, "broker-1"));
        var legacy = JsonNode.Parse(File.ReadAllText(directory.Path))!.AsObject();
        legacy["version"] = 1;
        legacy.Remove("tombstones");
        File.WriteAllText(directory.Path, legacy.ToJsonString());

        var store = directory.CreateStore();
        store.Load().Should().ContainSingle();
        store.LoadTombstones().Should().BeEmpty();
        store.RequireRecovery("ord-1");

        var upgraded = JsonNode.Parse(File.ReadAllText(directory.Path))!;
        upgraded["version"]!.GetValue<int>().Should().Be(2);
        upgraded["tombstones"]!.AsArray().Should().BeEmpty();
        directory.CreateStore().Load().Should().ContainSingle(order => order.RequiresRecovery);
    }

    [Theory]
    [InlineData("blank-id")]
    [InlineData("zero-quantity")]
    [InlineData("negative-fill")]
    [InlineData("excess-fill")]
    [InlineData("partial-filled-status")]
    [InlineData("rejected-with-fill")]
    [InlineData("nonterminal-status")]
    [InlineData("undefined-status")]
    [InlineData("duplicate")]
    [InlineData("overlap")]
    [InlineData("null-entry")]
    [InlineData("missing-collection")]
    public void InvalidTombstones_FailStartupClosed(string invalidity)
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        store.Save(Order(OrderStatus.Filled, 10, "broker-1"));
        var fullOrder = JsonNode.Parse(File.ReadAllText(directory.Path))!["orders"]![0]!.DeepClone();
        store.TryCompact("ord-1").Should().BeTrue();
        var snapshot = JsonNode.Parse(File.ReadAllText(directory.Path))!.AsObject();
        var tombstones = snapshot["tombstones"]!.AsArray();
        var tombstone = tombstones[0]!.AsObject();
        switch (invalidity)
        {
            case "blank-id":
                tombstone["orderId"] = " ";
                break;
            case "zero-quantity":
                tombstone["quantity"] = 0;
                break;
            case "negative-fill":
                tombstone["filledQuantity"] = -1;
                break;
            case "excess-fill":
                tombstone["filledQuantity"] = 11;
                break;
            case "partial-filled-status":
                tombstone["filledQuantity"] = 4;
                break;
            case "rejected-with-fill":
                tombstone["status"] = "Rejected";
                break;
            case "nonterminal-status":
                tombstone["status"] = "Accepted";
                break;
            case "undefined-status":
                tombstone["status"] = 12345;
                break;
            case "duplicate":
                tombstones.Add(tombstone.DeepClone());
                break;
            case "overlap":
                snapshot["orders"]!.AsArray().Add(fullOrder);
                break;
            case "null-entry":
                tombstones.Add((JsonNode?)null);
                break;
            case "missing-collection":
                snapshot.Remove("tombstones");
                break;
            default:
                throw new InvalidOperationException();
        }
        File.WriteAllText(directory.Path, snapshot.ToJsonString());

        var reopen = () => new FileBrokerageOrderRecoveryStore(directory.Path, "alpaca");

        reopen.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void TombstonesWithoutAccountScope_CannotBindToAnotherAccount()
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        store.Save(Order(OrderStatus.Filled, 10, "broker-1"));
        store.TryCompact("ord-1").Should().BeTrue();
        var snapshot = JsonNode.Parse(File.ReadAllText(directory.Path))!.AsObject();
        snapshot.Remove("scopeIdentity");
        File.WriteAllText(directory.Path, snapshot.ToJsonString());
        var reopened = new FileBrokerageOrderRecoveryStore(directory.Path, "alpaca");

        var bind = () => reopened.BindScope("alpaca:paper:another-account");

        bind.Should().Throw<InvalidOperationException>();
    }

    private static RetainedBrokerageOrder Order(OrderStatus status, decimal filledQuantity, string? brokerOrderId) => new(
        new OrderState
        {
            OrderId = "ord-1",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            FilledQuantity = filledQuantity,
            Status = status,
            FundAccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        }, brokerOrderId, RequiresRecovery: false);

    private sealed class StoreDirectory : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"meridian-order-tombstones-{Guid.NewGuid():N}");

        public string Path => System.IO.Path.Combine(_directory, "orders.json");

        public FileBrokerageOrderRecoveryStore CreateStore()
        {
            var store = new FileBrokerageOrderRecoveryStore(Path, "alpaca");
            store.BindScope("alpaca:paper:account-1");
            return store;
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
