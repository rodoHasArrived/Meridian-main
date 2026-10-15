using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class SecurityMasterCrossNodeConsistencyTests(SecurityMasterDatabaseFixture fixture)
    : IClassFixture<SecurityMasterDatabaseFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [SecurityMasterDatabaseFact]
    public async Task FailedRefresh_RecoversDespiteContinuousNotificationsAndAnotherFailedResync()
    {
        var id = Guid.NewGuid();
        var clock = new NotificationClock();
        var storeA = new PostgresSecurityMasterStore(fixture.Options);
        var before = SecurityMasterProjectionChangeNotificationTests.CreateProjection(id, "Before", 1);
        await storeA.UpsertProjectionAsync(before);
        var failRead = 0;
        var failResync = 0;
        var resyncAttempts = 0;
        await using var nodeB = await Node.StartAsync(fixture.Options, clock, store =>
        {
            var reader = Substitute.For<ISecurityMasterStore>();
            reader.GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (call.Arg<Guid>() == id && Interlocked.Exchange(ref failRead, 0) == 1)
                    throw new IOException("injected transient durable-read failure");
                return store.GetProjectionAsync(call.Arg<Guid>(), call.Arg<CancellationToken>());
            });
            reader.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(call =>
            {
                Interlocked.Increment(ref resyncAttempts);
                if (Interlocked.Exchange(ref failResync, 0) == 1)
                    throw new IOException("injected transient resync failure");
                return store.LoadAllAsync(call.Arg<CancellationToken>());
            });
            return reader;
        });

        Interlocked.Exchange(ref failRead, 1);
        Interlocked.Exchange(ref failResync, 1);
        var failed = nodeB.Expect(id, SecurityProjectionChangeOutcome.Failed);
        await storeA.UpsertProjectionAsync(before with { DisplayName = "Recovered", Version = 2 });
        await failed.WaitAsync(Timeout);
        nodeB.Cache.Get(id)!.Version.Should().Be(1);

        // Every wait receives a real PG notification. Advance only monotonic time: recovery
        // must happen before a real 30-second idle timeout and without notifying the target again.
        await using var publisher = new NpgsqlConnection(fixture.Options.ConnectionString);
        await publisher.OpenAsync();
        for (var i = 0; i < 18; i++)
        {
            var sentinel = Guid.NewGuid();
            var handled = nodeB.Expect(sentinel, SecurityProjectionChangeOutcome.IgnoredOwnNode);
            clock.Advance(TimeSpan.FromSeconds(10));
            await using var command = new NpgsqlCommand("select pg_notify(@channel, @payload)", publisher);
            command.Parameters.AddWithValue("channel", SecurityProjectionChangeNotification.ChannelFor(fixture.Options.Schema));
            command.Parameters.AddWithValue("payload",
                SecurityProjectionChangeNotification.ForSecurity(nodeB.Identity.NodeId, sentinel, 1).ToPayload());
            await command.ExecuteNonQueryAsync();
            await handled.WaitAsync(Timeout);
        }

        // A final notification provides a barrier after any scheduled resync in the prior loop.
        nodeB.Cache.Get(id)!.DisplayName.Should().Be("Recovered");
        nodeB.Cache.Get(id)!.Version.Should().Be(2);
        Volatile.Read(ref resyncAttempts).Should().Be(3, "startup, failed retry, successful retry only");
    }

    [SecurityMasterDatabaseFact]
    public async Task AliasCorrection_RemovesSupersededInboundAndOutboundMappings_PreservingOtherAuthority()
    {
        await using var nodeB = await Node.StartAsync(fixture.Options);
        var storeA = new PostgresSecurityMasterStore(fixture.Options);
        var id = Guid.NewGuid();
        var ticker = "C" + id.ToString("N")[..10].ToUpperInvariant();
        var oldGeneric = ticker + ".OLD";
        var oldProvider = ticker + ".O";
        var effective = DateTimeOffset.UtcNow.AddDays(-1);
        var generic = new SecurityAliasDto(Guid.NewGuid(), id, "Ticker", oldGeneric, null,
            SecurityAliasScope.Operations, null, "creator", effective, effective, null, true);
        var provider = generic with { AliasId = Guid.NewGuid(), AliasValue = oldProvider, Provider = "reuters" };
        var projection = SecurityMasterProjectionChangeNotificationTests.CreateProjection(id, "Aliased", 1) with
        {
            PrimaryIdentifierValue = ticker,
            Identifiers = [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, effective, null, null)],
            Aliases = [generic, provider]
        };
        var created = nodeB.Expect(id, SecurityProjectionChangeOutcome.Refreshed);
        await storeA.UpsertProjectionAsync(projection);
        await created.WaitAsync(Timeout);
        nodeB.Registry.ResolveToCanonical(oldGeneric).Should().Be(ticker);
        nodeB.Registry.TryResolve(oldProvider, "reuters").Should().Be(ticker);
        nodeB.Registry.GetProviderSymbol(ticker, "reuters").Should().Be(oldProvider);

        var otherId = Guid.NewGuid();
        await nodeB.Registry.RegisterAsync(new CanonicalSymbolDefinition
        {
            Canonical = ticker,
            SecurityId = id,
            AliasDefinitions = [new("CURATED-" + ticker, SymbolMappingSources.Operator)],
            ProviderSymbols = new Dictionary<string, ProviderSymbolDefinition>
            {
                ["ib"] = new("OP-" + ticker, SymbolMappingSources.Operator, true)
            }
        });
        await nodeB.Registry.RegisterAsync(new CanonicalSymbolDefinition
        {
            Canonical = "OTHER-" + ticker,
            SecurityId = otherId,
            AliasDefinitions = [new(oldProvider, SymbolMappingSources.OpenFigi, "other-provider")]
        });

        var correctedGeneric = nodeB.Expect(id, SecurityProjectionChangeOutcome.Refreshed);
        await storeA.UpsertAliasAsync(generic with { AliasValue = ticker + ".NEW" });
        await correctedGeneric.WaitAsync(Timeout);
        var correctedProvider = nodeB.Expect(id, SecurityProjectionChangeOutcome.Refreshed);
        await storeA.UpsertAliasAsync(provider with { AliasValue = ticker + ".OQ" });
        await correctedProvider.WaitAsync(Timeout);

        nodeB.Cache.Get(id)!.Version.Should().Be(1, "alias correction retains the event version");
        nodeB.Registry.ResolveToCanonical(oldGeneric).Should().BeNull();
        // Generic fallback can still find OTHER's independently owned spelling; it must never
        // route the superseded alias to this security or retain its outbound Reuters mapping.
        nodeB.Registry.TryResolve(oldProvider, "reuters").Should().NotBe(ticker);
        nodeB.Registry.TryResolve(ticker + ".OQ", "reuters").Should().Be(ticker);
        nodeB.Registry.ResolveToCanonical(ticker + ".NEW").Should().Be(ticker);
        nodeB.Registry.GetProviderSymbol(ticker, "reuters").Should().Be(ticker + ".OQ");
        nodeB.Registry.TryResolve(oldProvider, "other-provider").Should().Be("OTHER-" + ticker);
        nodeB.Registry.ResolveToCanonical("CURATED-" + ticker).Should().Be(ticker);
        nodeB.Registry.GetProviderSymbol(ticker, "ib").Should().Be("OP-" + ticker);

        var disabled = nodeB.Expect(id, SecurityProjectionChangeOutcome.Refreshed);
        await storeA.UpsertAliasAsync(provider with { AliasValue = ticker + ".OQ", IsEnabled = false });
        await disabled.WaitAsync(Timeout);
        nodeB.Registry.TryResolve(ticker + ".OQ", "reuters").Should().BeNull();
        nodeB.Registry.GetProviderSymbol(ticker, "reuters").Should().BeNull();

        var reloadedStore = new SymbolRegistryService(nodeB.Directory);
        await reloadedStore.InitializeAsync();
        var reloaded = new CanonicalSymbolRegistry(reloadedStore);
        reloaded.ResolveToCanonical(oldGeneric).Should().BeNull();
        reloaded.GetProviderSymbol(ticker, "reuters").Should().BeNull();
        reloaded.GetProviderSymbol(ticker, "ib").Should().Be("OP-" + ticker);
        reloaded.TryResolve(oldProvider, "other-provider").Should().Be("OTHER-" + ticker);
    }

    private sealed class NotificationClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _timestamp, duration.Ticks);
    }

    private sealed class Node : IAsyncDisposable
    {
        private readonly SecurityMasterProjectionChangeListener _listener;
        private readonly ConcurrentDictionary<(Guid, SecurityProjectionChangeOutcome), TaskCompletionSource> _waiters = new();
        private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "sm-consistency-" + Guid.NewGuid().ToString("N"));
        public SecurityMasterNodeIdentity Identity { get; } = SecurityMasterNodeIdentity.CreateNew();
        public SecurityMasterProjectionCache Cache { get; } = new();
        public CanonicalSymbolRegistry Registry { get; }

        private Node(SecurityMasterOptions options, TimeProvider clock, Func<PostgresSecurityMasterStore, ISecurityMasterStore>? decorate)
        {
            var nodeOptions = new SecurityMasterOptions
            {
                ConnectionString = options.ConnectionString,
                Schema = options.Schema,
                PreloadProjectionCache = true,
                ProjectionCacheRefreshMinutes = 0
            };
            var store = new PostgresSecurityMasterStore(nodeOptions, Identity);
            Registry = new CanonicalSymbolRegistry(new SymbolRegistryService(Directory));
            var seed = new SecurityMasterCanonicalSymbolSeedService(Cache, Registry,
                NullLogger<SecurityMasterCanonicalSymbolSeedService>.Instance);
            var projection = new SecurityMasterProjectionService(decorate?.Invoke(store) ?? store, Cache,
                new SecurityMasterAggregateRebuilder(
                    new PostgresSecurityMasterEventStore(nodeOptions, NullLogger<PostgresSecurityMasterEventStore>.Instance),
                    new PostgresSecurityMasterSnapshotStore(nodeOptions)),
                NullLogger<SecurityMasterProjectionService>.Instance);
            var handler = new SecurityMasterProjectionChangeHandler(projection, Cache, Identity, nodeOptions,
                NullLogger<SecurityMasterProjectionChangeHandler>.Instance, seed);
            _listener = new SecurityMasterProjectionChangeListener(nodeOptions, handler,
                NullLogger<SecurityMasterProjectionChangeListener>.Instance)
            { Clock = clock };
            _listener.Listening += () => _listening.TrySetResult();
            _listener.NotificationHandled += (payload, outcome) =>
            {
                if (SecurityProjectionChangeNotification.TryParse(payload, out var change) && change is not null
                    && _waiters.TryRemove((change.SecurityId, outcome), out var signal))
                    signal.TrySetResult();
            };
        }

        public static async Task<Node> StartAsync(SecurityMasterOptions options, TimeProvider? clock = null,
            Func<PostgresSecurityMasterStore, ISecurityMasterStore>? decorate = null)
        {
            var node = new Node(options, clock ?? TimeProvider.System, decorate);
            await node._listener.StartAsync(CancellationToken.None);
            await node._listening.Task.WaitAsync(Timeout);
            return node;
        }

        public Task Expect(Guid id, SecurityProjectionChangeOutcome outcome)
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters[(id, outcome)] = signal;
            return signal.Task;
        }

        public async ValueTask DisposeAsync()
        {
            await _listener.StopAsync(CancellationToken.None);
            _listener.Dispose();
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
