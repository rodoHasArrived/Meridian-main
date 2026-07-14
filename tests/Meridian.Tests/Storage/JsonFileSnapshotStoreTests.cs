using System.Text.Json;
using FluentAssertions;
using Meridian.Storage.Store;

namespace Meridian.Tests.Storage;

public sealed class JsonFileSnapshotStoreTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _directory = Directory.CreateTempSubdirectory("json-snapshot-store-tests").FullName;

    private string SnapshotPath => Path.Combine(_directory, "snapshot.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed record CounterSnapshot(int Value);

    private sealed class CounterStore(string path) : JsonFileSnapshotStore<CounterSnapshot>(path, JsonOptions)
    {
        protected override CounterSnapshot CreateEmptySnapshot() => new(0);

        public Task<int> GetAsync(CancellationToken ct = default)
            => ReadSnapshotAsync(static snapshot => snapshot.Value, ct);

        public Task<int> IncrementAsync(CancellationToken ct = default)
            => UpdateSnapshotAsync(static snapshot =>
            {
                var next = snapshot with { Value = snapshot.Value + 1 };
                return (next, next.Value);
            }, ct);
    }

    private sealed class RecoveringCounterStore(string path) : JsonFileSnapshotStore<CounterSnapshot>(path, JsonOptions)
    {
        public bool Recovered { get; private set; }

        protected override CounterSnapshot CreateEmptySnapshot() => new(0);

        protected override CounterSnapshot HandleCorruptSnapshot(JsonException exception)
        {
            Recovered = true;
            return CreateEmptySnapshot();
        }

        public Task<int> GetAsync() => ReadSnapshotAsync(static snapshot => snapshot.Value);
    }

    [Fact]
    public async Task Read_ReturnsEmptySnapshotWhenFileDoesNotExist()
    {
        var store = new CounterStore(SnapshotPath);

        (await store.GetAsync()).Should().Be(0);
        File.Exists(SnapshotPath).Should().BeFalse("reads must not create the snapshot file");
    }

    [Fact]
    public async Task Update_PersistsSnapshotAcrossStoreInstances()
    {
        var store = new CounterStore(SnapshotPath);
        await store.IncrementAsync();
        await store.IncrementAsync();

        var reloaded = new CounterStore(SnapshotPath);

        (await reloaded.GetAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Update_ReturnsTheResultProducedByTheUpdate()
    {
        var store = new CounterStore(SnapshotPath);

        (await store.IncrementAsync()).Should().Be(1);
        (await store.IncrementAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CorruptSnapshot_PropagatesByDefault()
    {
        await File.WriteAllTextAsync(SnapshotPath, "{ not json");
        var store = new CounterStore(SnapshotPath);

        var act = () => store.GetAsync();

        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task CorruptSnapshot_UsesRecoveryHookWhenOverridden()
    {
        await File.WriteAllTextAsync(SnapshotPath, "{ not json");
        var store = new RecoveringCounterStore(SnapshotPath);

        (await store.GetAsync()).Should().Be(0);
        store.Recovered.Should().BeTrue();
    }

    [Fact]
    public async Task NullDocument_YieldsEmptySnapshot()
    {
        await File.WriteAllTextAsync(SnapshotPath, "null");
        var store = new CounterStore(SnapshotPath);

        (await store.GetAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentUpdates_AreSerializedByTheGate()
    {
        var store = new CounterStore(SnapshotPath);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.IncrementAsync()));

        (await store.GetAsync()).Should().Be(20);
    }

    [Fact]
    public async Task Update_LeavesNoTemporaryFilesBehind()
    {
        var store = new CounterStore(SnapshotPath);
        await store.IncrementAsync();

        Directory.EnumerateFiles(_directory).Should().ContainSingle()
            .Which.Should().EndWith("snapshot.json");
    }
}
