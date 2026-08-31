using FluentAssertions;
using Meridian.Contracts.Lifecycle;
using Meridian.Storage.Runtime;
using Xunit;

namespace Meridian.Tests.Storage.Runtime;

public sealed class JsonLifecycleReceiptStoreTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "meridian-lifecycle-receipt-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteAndReadHostReceiptAsync_RoundTripsThroughAtomicStore()
    {
        var store = new JsonLifecycleReceiptStore(new LifecycleReceiptStoreOptions
        {
            DataRoot = _dataRoot
        });
        var receipt = CreateReceipt("session-a", "operation-a");

        await store.WriteHostReceiptAsync(receipt);
        var reloaded = await store.ReadLatestHostReceiptAsync();

        reloaded.Should().BeEquivalentTo(receipt);
        File.Exists(Path.Combine(
            _dataRoot,
            "runtime",
            "lifecycle",
            "receipts",
            "host-session-a.json")).Should().BeTrue();
    }

    [Fact]
    public async Task WriteHostReceiptAsync_PrunesOnlyReceiptsBeyondRetention()
    {
        var store = new JsonLifecycleReceiptStore(new LifecycleReceiptStoreOptions
        {
            DataRoot = _dataRoot,
            RetainedReceiptCount = 2
        });

        await store.WriteHostReceiptAsync(CreateReceipt("session-a", "operation-a"));
        await Task.Delay(20);
        await store.WriteHostReceiptAsync(CreateReceipt("session-b", "operation-b"));
        await Task.Delay(20);
        await store.WriteHostReceiptAsync(CreateReceipt("session-c", "operation-c"));

        var receiptFiles = Directory.GetFiles(
            Path.Combine(_dataRoot, "runtime", "lifecycle", "receipts"),
            "host-*.json");
        receiptFiles.Should().HaveCount(2);
        receiptFiles.Should().Contain(path => path.EndsWith("host-session-c.json", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    private static LifecycleShutdownReceiptDto CreateReceipt(string sessionId, string operationId)
        => new()
        {
            SessionId = sessionId,
            OperationId = operationId,
            Reason = LifecycleShutdownReason.Operator,
            Outcome = LifecycleShutdownOutcome.Succeeded,
            StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
            CompletedAtUtc = DateTimeOffset.UtcNow,
            ForcedTermination = false
        };
}
