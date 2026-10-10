using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Unit coverage for the cross-node projection-cache notification seam: the wire payload and the
/// <see cref="SecurityMasterProjectionChangeHandler"/> decisions the listener delegates to.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterProjectionChangeNotificationTests
{
    private static readonly Guid LocalNode = Guid.NewGuid();
    private static readonly Guid RemoteNode = Guid.NewGuid();

    private readonly ISecurityMasterStore _store = Substitute.For<ISecurityMasterStore>();
    private readonly ISecurityMasterEventStore _eventStore = Substitute.For<ISecurityMasterEventStore>();
    private readonly ISecurityMasterSnapshotStore _snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
    private readonly SecurityMasterProjectionCache _cache = new();
    private readonly RecordingLogger<SecurityMasterProjectionChangeHandler> _logger = new();

    public SecurityMasterProjectionChangeNotificationTests()
    {
        // No event history: the rebuilder falls back to the durable projection row, so the store
        // stub alone decides what a re-read returns.
        _eventStore.LoadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityMasterEventEnvelope>());
        _snapshotStore.LoadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((SecuritySnapshotRecord?)null);
    }

    [Fact]
    public void Payload_RoundTripsAndStaysUnderTheNotifyLimit()
    {
        var securityId = Guid.NewGuid();
        var notification = SecurityProjectionChangeNotification.ForSecurity(RemoteNode, securityId, long.MaxValue);

        var payload = notification.ToPayload();

        payload.Length.Should().BeLessThan(8000);
        SecurityProjectionChangeNotification.TryParse(payload, out var parsed).Should().BeTrue();
        parsed.Should().Be(notification);

        var resync = SecurityProjectionChangeNotification.ForResync(RemoteNode);
        SecurityProjectionChangeNotification.TryParse(resync.ToPayload(), out var parsedResync).Should().BeTrue();
        parsedResync.Should().Be(resync);
    }

    [Theory]
    [InlineData("security_master")]
    [InlineData("sm_test_0123456789abcdef0123456789abcdef")]
    [InlineData("A_Very_Long_Schema_Name_That_Exceeds_The_PostgreSQL_Identifier_Limit_By_A_Lot")]
    public void ChannelFor_IsALowercaseIdentifierWithinTheLimit(string schema)
    {
        var channel = SecurityProjectionChangeNotification.ChannelFor(schema);

        channel.Length.Should().BeLessThanOrEqualTo(63);
        channel.Should().MatchRegex("^[a-z0-9_]+$");
        SecurityProjectionChangeNotification.ChannelFor(schema + "x").Should().NotBe(channel);
    }

    [Fact]
    public async Task OtherNodeNotification_RefreshesTheCachedEntryFromTheDurableStore()
    {
        var securityId = Guid.NewGuid();
        _cache.Upsert(CreateProjection(securityId, "Before", 1));
        _store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "After", 2));

        var outcome = await CreateHandler().HandleAsync(
            SecurityProjectionChangeNotification.ForSecurity(RemoteNode, securityId, 2).ToPayload());

        outcome.Should().Be(SecurityProjectionChangeOutcome.Refreshed);
        var cached = _cache.Get(securityId);
        cached.Should().NotBeNull();
        cached!.Version.Should().Be(2);
        cached.DisplayName.Should().Be("After");
    }

    [Fact]
    public async Task OtherNodeNotification_ForAnUncachedSecurity_InstallsIt()
    {
        var securityId = Guid.NewGuid();
        _store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "Created Elsewhere", 1));

        var outcome = await CreateHandler().HandleAsync(
            SecurityProjectionChangeNotification.ForSecurity(RemoteNode, securityId, 1).ToPayload());

        outcome.Should().Be(SecurityProjectionChangeOutcome.Refreshed);
        _cache.Get(securityId)!.DisplayName.Should().Be("Created Elsewhere");
    }

    [Fact]
    public async Task OwnNodeNotification_IsIgnoredWithoutADurableRead()
    {
        var securityId = Guid.NewGuid();
        _cache.Upsert(CreateProjection(securityId, "Before", 1));
        _store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "After", 2));

        var outcome = await CreateHandler().HandleAsync(
            SecurityProjectionChangeNotification.ForSecurity(LocalNode, securityId, 2).ToPayload());

        outcome.Should().Be(SecurityProjectionChangeOutcome.IgnoredOwnNode);
        _cache.Get(securityId)!.Version.Should().Be(1);
        await _store.DidNotReceive().GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OlderVersionNotification_DoesNotDowngradeTheCachedEntry()
    {
        var securityId = Guid.NewGuid();
        _cache.Upsert(CreateProjection(securityId, "Newest", 3));
        _store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "Stale", 2));

        var outcome = await CreateHandler().HandleAsync(
            SecurityProjectionChangeNotification.ForSecurity(RemoteNode, securityId, 2).ToPayload());

        outcome.Should().Be(SecurityProjectionChangeOutcome.AlreadyCurrent);
        _cache.Get(securityId)!.DisplayName.Should().Be("Newest");
        _cache.Get(securityId)!.Version.Should().Be(3);
    }

    [Fact]
    public async Task EqualVersionNotification_RereadsChangedContent()
    {
        // Alias writes and projection replacements change cached content without moving the
        // event-stream version, so an equal version must not short-circuit the reread.
        var securityId = Guid.NewGuid();
        _cache.Upsert(CreateProjection(securityId, "Before alias correction", 2));
        _store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "After alias correction", 2));

        var outcome = await CreateHandler().HandleAsync(
            SecurityProjectionChangeNotification.ForSecurity(RemoteNode, securityId, 2).ToPayload());

        outcome.Should().Be(SecurityProjectionChangeOutcome.Refreshed);
        _cache.Get(securityId)!.DisplayName.Should().Be("After alias correction");
    }

    [Fact]
    public async Task DurableReadOlderThanTheCache_DoesNotDowngradeTheCachedEntry()
    {
        // The notification claims a newer version, but the durable read returns an older row (the
        // cache was meanwhile advanced by a local write): the cache's version guard must win.
        var securityId = Guid.NewGuid();
        _cache.Upsert(CreateProjection(securityId, "Newest", 3));
        _store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(securityId, "Stale", 2));

        var outcome = await CreateHandler().HandleAsync(
            SecurityProjectionChangeNotification.ForSecurity(RemoteNode, securityId, 5).ToPayload());

        outcome.Should().Be(SecurityProjectionChangeOutcome.KeptNewer);
        _cache.Get(securityId)!.DisplayName.Should().Be("Newest");
        _cache.Get(securityId)!.Version.Should().Be(3);
    }

    [Fact]
    public async Task NotificationForASecurityGoneFromTheDurableStore_EvictsIt()
    {
        var securityId = Guid.NewGuid();
        _cache.Upsert(CreateProjection(securityId, "Purged", 1));
        _store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns((SecurityProjectionRecord?)null);

        var outcome = await CreateHandler().HandleAsync(
            SecurityProjectionChangeNotification.ForSecurity(RemoteNode, securityId, 2).ToPayload());

        outcome.Should().Be(SecurityProjectionChangeOutcome.Evicted);
        _cache.Get(securityId).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a notification")]
    [InlineData("v1|not-a-guid|s|00000000000000000000000000000001|1")]
    [InlineData("v2|0123456789abcdef0123456789abcdef|s|0123456789abcdef0123456789abcdef|1")]
    [InlineData("v1|0123456789abcdef0123456789abcdef|s|0123456789abcdef0123456789abcdef|-1")]
    [InlineData("v1|0123456789abcdef0123456789abcdef|s|0123456789abcdef0123456789abcdef")]
    [InlineData("v1|0123456789abcdef0123456789abcdef|x")]
    public async Task MalformedPayload_IsLoggedAndIgnored(string? payload)
    {
        var securityId = Guid.NewGuid();
        _cache.Upsert(CreateProjection(securityId, "Untouched", 1));

        var outcome = await CreateHandler().HandleAsync(payload);

        outcome.Should().Be(SecurityProjectionChangeOutcome.Malformed);
        _logger.Entries.Should().ContainSingle(entry => entry.LogLevel == LogLevel.Warning
            && entry.Message.Contains("malformed", StringComparison.OrdinalIgnoreCase));
        _cache.Get(securityId)!.DisplayName.Should().Be("Untouched");
        await _store.DidNotReceive().GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DurableReadFailure_IsLoggedAndReportedWithoutThrowing()
    {
        var securityId = Guid.NewGuid();
        _store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns<Task<SecurityProjectionRecord?>>(_ => throw new InvalidOperationException("database unavailable"));

        var outcome = await CreateHandler().HandleAsync(
            SecurityProjectionChangeNotification.ForSecurity(RemoteNode, securityId, 1).ToPayload());

        outcome.Should().Be(SecurityProjectionChangeOutcome.Failed);
        _logger.Entries.Should().Contain(entry => entry.LogLevel == LogLevel.Warning && entry.Exception != null);
    }

    [Fact]
    public async Task ResyncNotification_RewarmsAPreloadedCache()
    {
        var stale = Guid.NewGuid();
        var current = Guid.NewGuid();
        _cache.Upsert(CreateProjection(stale, "Gone", 1));
        _store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { CreateProjection(current, "Rebuilt", 4) });

        var outcome = await CreateHandler(preload: true).HandleAsync(
            SecurityProjectionChangeNotification.ForResync(RemoteNode).ToPayload());

        outcome.Should().Be(SecurityProjectionChangeOutcome.Resynchronized);
        _cache.Get(stale).Should().BeNull();
        _cache.Get(current)!.Version.Should().Be(4);
    }

    [Fact]
    public async Task Resynchronize_WithoutPreload_RefreshesOnlyCachedSecurities()
    {
        var cachedId = Guid.NewGuid();
        _cache.Upsert(CreateProjection(cachedId, "Before", 1));
        _store.GetProjectionAsync(cachedId, Arg.Any<CancellationToken>())
            .Returns(CreateProjection(cachedId, "After", 2));

        await CreateHandler(preload: false).ResynchronizeAsync();

        _cache.Count.Should().Be(1);
        _cache.Get(cachedId)!.DisplayName.Should().Be("After");
        await _store.DidNotReceive().LoadAllAsync(Arg.Any<CancellationToken>());
    }

    private SecurityMasterProjectionChangeHandler CreateHandler(bool preload = false)
    {
        var options = new SecurityMasterOptions { PreloadProjectionCache = preload };
        var projectionService = new SecurityMasterProjectionService(
            _store,
            _cache,
            new SecurityMasterAggregateRebuilder(_eventStore, _snapshotStore),
            NullLogger<SecurityMasterProjectionService>.Instance);
        return new SecurityMasterProjectionChangeHandler(
            projectionService,
            _cache,
            new SecurityMasterNodeIdentity(LocalNode),
            options,
            _logger);
    }

    internal static SecurityProjectionRecord CreateProjection(Guid securityId, string displayName, long version)
        => new(
            securityId,
            "Equity",
            SecurityStatusDto.Active,
            displayName,
            "USD",
            "Ticker",
            "ACME",
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
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "ACME", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
            },
            Array.Empty<SecurityAliasDto>());
}
