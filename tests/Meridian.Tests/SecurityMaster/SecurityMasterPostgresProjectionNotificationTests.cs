using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Proves cross-node projection-cache coherence end to end against PostgreSQL: a projection write
/// committed by one store instance ("node A") reaches a second node's listener and cache, and a
/// write whose transaction rolls back after queuing its notification announces nothing.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityMasterPostgresProjectionNotificationTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterPostgresProjectionNotificationTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task CommittedWriteOnOneNode_RefreshesAnotherNodesCache()
    {
        await using var nodeB = await ListeningNode.StartAsync(_fixture.Options);
        var storeA = new PostgresSecurityMasterStore(_fixture.Options, new SecurityMasterNodeIdentity(Guid.NewGuid()));
        var securityId = Guid.NewGuid();
        var ticker = UniqueTicker();

        var created = nodeB.WaitForAsync(securityId, SecurityProjectionChangeOutcome.Refreshed);
        await storeA.UpsertProjectionAsync(CreateProjection(securityId, ticker, "Created On A", 1));
        await created.WaitAsync(DeliveryTimeout);

        nodeB.Cache.Get(securityId)!.DisplayName.Should().Be("Created On A");

        var amended = nodeB.WaitForAsync(securityId, SecurityProjectionChangeOutcome.Refreshed);
        await storeA.UpsertProjectionAsync(CreateProjection(securityId, ticker, "Amended On A", 2));
        await amended.WaitAsync(DeliveryTimeout);

        var cached = nodeB.Cache.Get(securityId);
        cached.Should().NotBeNull();
        cached!.Version.Should().Be(2);
        cached.DisplayName.Should().Be("Amended On A");
    }

    [SecurityMasterDatabaseFact]
    public async Task OwnNodesCommittedWrite_IsIgnoredByItsListener()
    {
        await using var nodeB = await ListeningNode.StartAsync(_fixture.Options);
        var securityId = Guid.NewGuid();

        var ignored = nodeB.WaitForAsync(securityId, SecurityProjectionChangeOutcome.IgnoredOwnNode);
        await nodeB.Store.UpsertProjectionAsync(CreateProjection(securityId, UniqueTicker(), "Written On B", 1));
        await ignored.WaitAsync(DeliveryTimeout);

        nodeB.Cache.Get(securityId).Should().BeNull("the writing node's own service path upserts its cache, not the listener");
    }

    [SecurityMasterDatabaseFact]
    public async Task RolledBackWrite_ProducesNoNotification()
    {
        await using var nodeB = await ListeningNode.StartAsync(_fixture.Options);
        var storeA = new PostgresSecurityMasterStore(_fixture.Options, new SecurityMasterNodeIdentity(Guid.NewGuid()));
        var rolledBackId = Guid.NewGuid();

        // The batch path queues its per-security pg_notify and only then saves the checkpoint; a
        // NUL byte is not storable in a PostgreSQL text column, so the checkpoint write fails and
        // the whole transaction — the queued notification included — rolls back.
        var write = () => storeA.PersistProjectionBatchAsync(
            "bad\0checkpoint",
            1,
            new[] { CreateProjection(rolledBackId, UniqueTicker(), "Rolled Back", 1) });
        await write.Should().ThrowAsync<Exception>();

        (await storeA.GetProjectionAsync(rolledBackId)).Should().BeNull("the write must actually have rolled back");

        // Notifications are delivered in commit order, so once a later committed write has been
        // handled, any notification from the rolled-back transaction would already have arrived.
        var sentinelId = Guid.NewGuid();
        var sentinel = nodeB.WaitForAsync(sentinelId, SecurityProjectionChangeOutcome.Refreshed);
        await storeA.UpsertProjectionAsync(CreateProjection(sentinelId, UniqueTicker(), "Committed", 1));
        await sentinel.WaitAsync(DeliveryTimeout);

        nodeB.HandledPayloads.Should().NotContain(payload => payload.Contains(rolledBackId.ToString("N"), StringComparison.Ordinal));
        nodeB.Cache.Get(rolledBackId).Should().BeNull();
    }

    [SecurityMasterDatabaseFact]
    public async Task ListenerReconnect_ResynchronizesChangesMissedWhileDisconnected()
    {
        var options = _fixture.Options;
        // A one-second reconnect delay keeps the missed commit well inside the disconnected window,
        // so the change can only arrive through the post-reconnect resync, not a notification.
        await using var nodeB = await ListeningNode.StartAsync(options, preload: true, reconnectDelay: TimeSpan.FromSeconds(1));
        var storeA = new PostgresSecurityMasterStore(options, new SecurityMasterNodeIdentity(Guid.NewGuid()));
        var missedId = Guid.NewGuid();

        // Terminate the listener's backend, then commit while it is disconnected: the notification
        // goes nowhere, so only the post-reconnect resync can bring the change in.
        var relistened = nodeB.WaitForNextListenAsync();
        await TerminateListenerBackendsAsync(options);
        await storeA.UpsertProjectionAsync(CreateProjection(missedId, UniqueTicker(), "Missed", 1));
        await relistened.WaitAsync(DeliveryTimeout);

        nodeB.Cache.Get(missedId).Should().NotBeNull();
        nodeB.Cache.Get(missedId)!.DisplayName.Should().Be("Missed");
        nodeB.HandledPayloads.Should().NotContain(
            payload => payload.Contains(missedId.ToString("N"), StringComparison.Ordinal),
            "the change was committed while the listener was disconnected");
    }

    private static async Task TerminateListenerBackendsAsync(SecurityMasterOptions options)
    {
        var channel = SecurityProjectionChangeNotification.ChannelFor(options.Schema);
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select count(pg_terminate_backend(pid))
            from pg_stat_activity
            where pid <> pg_backend_pid() and query ilike @pattern;
            """;
        command.Parameters.AddWithValue("pattern", $"%LISTEN \"{channel}\"%");
        var terminated = (long)(await command.ExecuteScalarAsync())!;
        terminated.Should().BeGreaterThan(0, "the listener's dedicated connection must have been found");
    }

    private static string UniqueTicker() => "N" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private static SecurityProjectionRecord CreateProjection(Guid securityId, string ticker, string displayName, long version)
        => new(
            securityId,
            "Equity",
            SecurityStatusDto.Active,
            displayName,
            "USD",
            "Ticker",
            ticker,
            JsonSerializer.SerializeToElement(new
            {
                displayName,
                currency = "USD",
                exchange = "XNYS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            JsonSerializer.SerializeToElement(new
            {
                shareClass = "Common"
            }),
            JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                asOf = DateTimeOffset.UtcNow,
                updatedBy = "codex"
            }),
            version,
            DateTimeOffset.UtcNow.AddDays(-1),
            null,
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
            },
            Array.Empty<SecurityAliasDto>());

    /// <summary>A second Security Master node: its own identity, store, cache, and running listener.</summary>
    private sealed class ListeningNode : IAsyncDisposable
    {
        private readonly SecurityMasterProjectionChangeListener _listener;
        private readonly ConcurrentBag<string> _handled = new();
        private readonly ConcurrentQueue<(string Payload, SecurityProjectionChangeOutcome Outcome, TaskCompletionSource Signal)> _waiters = new();
        private TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ListeningNode(SecurityMasterOptions options, bool preload, TimeSpan reconnectDelay)
        {
            var nodeOptions = new SecurityMasterOptions
            {
                ConnectionString = options.ConnectionString,
                Schema = options.Schema,
                PreloadProjectionCache = preload
            };
            var identity = new SecurityMasterNodeIdentity(Guid.NewGuid());
            Store = new PostgresSecurityMasterStore(nodeOptions, identity);
            var eventStore = new PostgresSecurityMasterEventStore(nodeOptions, NullLogger<PostgresSecurityMasterEventStore>.Instance);
            var snapshotStore = new PostgresSecurityMasterSnapshotStore(nodeOptions);
            var projectionService = new SecurityMasterProjectionService(
                Store,
                Cache,
                new SecurityMasterAggregateRebuilder(eventStore, snapshotStore),
                NullLogger<SecurityMasterProjectionService>.Instance);
            var handler = new SecurityMasterProjectionChangeHandler(
                projectionService,
                Cache,
                identity,
                nodeOptions,
                NullLogger<SecurityMasterProjectionChangeHandler>.Instance);
            _listener = new SecurityMasterProjectionChangeListener(
                nodeOptions,
                handler,
                NullLogger<SecurityMasterProjectionChangeListener>.Instance)
            {
                InitialReconnectDelay = reconnectDelay,
                MaxReconnectDelay = reconnectDelay
            };
            _listener.Listening += () => Volatile.Read(ref _listening).TrySetResult();
            _listener.NotificationHandled += OnHandled;
        }

        public SecurityMasterProjectionCache Cache { get; } = new();

        public PostgresSecurityMasterStore Store { get; }

        public IReadOnlyCollection<string> HandledPayloads => _handled.ToArray();

        public static async Task<ListeningNode> StartAsync(
            SecurityMasterOptions options,
            bool preload = false,
            TimeSpan? reconnectDelay = null)
        {
            var node = new ListeningNode(options, preload, reconnectDelay ?? TimeSpan.FromMilliseconds(100));
            var listening = node.WaitForNextListenAsync();
            await node._listener.StartAsync(CancellationToken.None);
            await listening.WaitAsync(DeliveryTimeout);
            return node;
        }

        public Task WaitForNextListenAsync()
        {
            var next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _listening, next);
            return next.Task;
        }

        public Task WaitForAsync(Guid securityId, SecurityProjectionChangeOutcome outcome)
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue((securityId.ToString("N"), outcome, signal));
            return signal.Task;
        }

        private void OnHandled(string payload, SecurityProjectionChangeOutcome outcome)
        {
            _handled.Add(payload);
            foreach (var waiter in _waiters)
            {
                if (waiter.Outcome == outcome && payload.Contains(waiter.Payload, StringComparison.Ordinal))
                {
                    waiter.Signal.TrySetResult();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _listener.StopAsync(CancellationToken.None);
            _listener.Dispose();
        }
    }
}
