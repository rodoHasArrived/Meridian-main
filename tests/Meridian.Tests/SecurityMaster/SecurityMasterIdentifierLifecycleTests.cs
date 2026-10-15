using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityMasterIdentifierLifecycleTests
{
    [Fact]
    public async Task CommittedCreate_RequestCancellationDoesNotSkipCacheOrRegistryRetry()
    {
        using var requestCts = new CancellationTokenSource();
        using var harness = new Harness();
        harness.AfterCommit = requestCts.Cancel;
        var attempts = 0;
        harness.Writer.ReconcileSecurityMasterAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IEnumerable<CanonicalSymbolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref attempts) == 1
                ? Task.FromException<int>(new IOException("transient registry failure"))
                : Task.FromResult(1));
        CancellationToken snapshotToken = default;
        harness.Snapshots.SaveAsync(Arg.Any<SecuritySnapshotRecord>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                snapshotToken = call.Arg<CancellationToken>();
                snapshotToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        var created = await harness.Service.CreateAsync(CreateRequest(harness.SecurityId), requestCts.Token);

        requestCts.IsCancellationRequested.Should().BeTrue();
        created.SecurityId.Should().Be(harness.SecurityId);
        harness.Cache.Get(harness.SecurityId).Should().NotBeNull();
        snapshotToken.CanBeCanceled.Should().BeTrue("post-commit work is bounded by the service lifetime");
        snapshotToken.Should().NotBe(requestCts.Token);
        await EventuallyAsync(() => harness.Seed.TryGetSecurityId("ACME") == harness.SecurityId);
        attempts.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task CommittedCreate_SnapshotFailureCannotSkipLocalCoherence()
    {
        using var harness = new Harness();
        harness.Snapshots.SaveAsync(Arg.Any<SecuritySnapshotRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("snapshot unavailable")));

        await harness.Service.Invoking(service => service.CreateAsync(CreateRequest(harness.SecurityId)))
            .Should().ThrowAsync<IOException>();

        harness.Cache.Get(harness.SecurityId).Should().NotBeNull();
        await EventuallyAsync(() => harness.Seed.TryGetSecurityId("ACME") == harness.SecurityId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommittedDeactivation_WithdrawsIdentifierEvenIfLaterSnapshotFails(bool snapshotFails)
    {
        using var harness = new Harness();
        await harness.Service.CreateAsync(CreateRequest(harness.SecurityId));
        await harness.Seed.SeedAsync();
        harness.Seed.TryGetSecurityId("ACME").Should().Be(harness.SecurityId);
        using var requestCts = new CancellationTokenSource();
        harness.AfterCommit = requestCts.Cancel;
        harness.Snapshots.SaveAsync(Arg.Any<SecuritySnapshotRecord>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return snapshotFails ? Task.FromException(new IOException("snapshot unavailable")) : Task.CompletedTask;
            });
        var request = new DeactivateSecurityRequest(harness.SecurityId, 1, DateTimeOffset.UtcNow,
            "tests", "tests", null, "deactivate");

        if (snapshotFails)
        {
            await harness.Service.Invoking(service => service.DeactivateAsync(request, requestCts.Token))
                .Should().ThrowAsync<IOException>();
        }
        else
        {
            await harness.Service.DeactivateAsync(request, requestCts.Token);
        }

        requestCts.IsCancellationRequested.Should().BeTrue();
        harness.Cache.Get(harness.SecurityId)!.Status.Should().Be(SecurityStatusDto.Inactive);
        await EventuallyAsync(() => harness.Seed.TryGetSecurityId("ACME") is null);
        await harness.Writer.Received().ReconcileSecurityMasterAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(harness.SecurityId)),
            Arg.Is<IEnumerable<CanonicalSymbolDefinition>>(definitions => !definitions.Any()),
            Arg.Any<CancellationToken>());
    }

    private static CreateSecurityRequest CreateRequest(Guid securityId)
        => new(securityId, "Equity",
            JsonSerializer.SerializeToElement(new { displayName = "Acme", currency = "USD" }),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "ACME", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)],
            DateTimeOffset.UtcNow, "tests", "tests", null, "create");

    private static async Task EventuallyAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        predicate().Should().BeTrue();
    }

    private sealed class Harness : IDisposable
    {
        public Guid SecurityId { get; } = Guid.NewGuid();
        public SecurityMasterProjectionCache Cache { get; } = new();
        public ISecurityMasterSnapshotStore Snapshots { get; } = Substitute.For<ISecurityMasterSnapshotStore>();
        public ICanonicalSymbolRegistrySecurityMasterWriter Writer { get; }
        public SecurityMasterCanonicalSymbolSeedService Seed { get; }
        public SecurityMasterService Service { get; }
        public Action? AfterCommit { get; set; }

        public Harness()
        {
            var store = Substitute.For<ISecurityMasterStore>();
            SecurityProjectionRecord? persisted = null;
            store.UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                    persisted = call.Arg<SecurityProjectionRecord>();
                    AfterCommit?.Invoke();
                    return Task.CompletedTask;
                });
            store.GetProjectionAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(_ => persisted);
            var events = Substitute.For<ISecurityMasterEventStore>();
            events.LoadAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(Array.Empty<SecurityMasterEventEnvelope>());
            var registry = Substitute.For<ICanonicalSymbolRegistry, ICanonicalSymbolRegistrySecurityMasterWriter>();
            Writer = (ICanonicalSymbolRegistrySecurityMasterWriter)registry;
            Writer.ReconcileSecurityMasterAsync(Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<IEnumerable<CanonicalSymbolDefinition>>(), Arg.Any<CancellationToken>()).Returns(1);
            Seed = new SecurityMasterCanonicalSymbolSeedService(Cache, registry,
                NullLogger<SecurityMasterCanonicalSymbolSeedService>.Instance);
            Service = new SecurityMasterService(events, Snapshots, store,
                new SecurityMasterAggregateRebuilder(events, Snapshots), new SecurityMasterOptions(),
                NullLogger<SecurityMasterService>.Instance, projectionCache: Cache, seedService: Seed)
            {
                RegistryReseedRetryDelay = TimeSpan.FromMilliseconds(10)
            };
        }

        public void Dispose() => Service.Dispose();
    }
}
