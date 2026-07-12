using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Verifies point-in-time (transaction-time) reads over the security-master event stream:
/// as-of queries replay the stream up to the requested timestamp instead of returning the
/// current projection.
/// </summary>
public sealed class SecurityMasterQueryServiceAsOfTests
{
    private static readonly DateTimeOffset T0 = new(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetByIdAsOfAsync_ReturnsTermsAsRecordedAtThatTime()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        eventStore.LoadAsync(securityId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            MakeEnvelope(securityId, version: 1, timestamp: T0, displayName: "Acme Corp"),
            MakeEnvelope(securityId, version: 2, timestamp: T0.AddDays(5), displayName: "Acme Corporation")
        });
        var service = CreateService(eventStore, currentProjection: null);

        var asOfBetween = await service.GetByIdAsOfAsync(securityId, T0.AddDays(2));
        var asOfLatest = await service.GetByIdAsOfAsync(securityId, T0.AddDays(10));

        asOfBetween.Should().NotBeNull();
        asOfBetween!.DisplayName.Should().Be("Acme Corp");
        asOfLatest.Should().NotBeNull();
        asOfLatest!.DisplayName.Should().Be("Acme Corporation");
    }

    [Fact]
    public async Task GetByIdAsOfAsync_ReturnsNull_BeforeFirstRecordedEvent()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        eventStore.LoadAsync(securityId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            MakeEnvelope(securityId, version: 1, timestamp: T0, displayName: "Acme Corp")
        });
        var service = CreateService(eventStore, currentProjection: null);

        var detail = await service.GetByIdAsOfAsync(securityId, T0.AddDays(-1));

        detail.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsOfAsync_FallsBackToCurrentProjection_WhenSecurityHasNoEventHistory()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        eventStore.LoadAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityMasterEventEnvelope>());
        var current = MakeProjection(securityId, "Projection Only Corp");
        var service = CreateService(eventStore, current);

        var detail = await service.GetByIdAsOfAsync(securityId, T0);

        detail.Should().NotBeNull();
        detail!.DisplayName.Should().Be("Projection Only Corp");
    }

    [Fact]
    public async Task GetByIdentifierAsync_WithAsOf_ReturnsAsOfTerms_NotCurrentProjection()
    {
        // Acceptance criterion for point-in-time reads: an identifier lookup with an explicit
        // asOf must not resolve the ID historically and then hand back today's terms.
        var securityId = Guid.NewGuid();
        var current = MakeProjection(securityId, "Acme Corporation");
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        eventStore.LoadAsync(securityId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            MakeEnvelope(securityId, version: 1, timestamp: T0, displayName: "Acme Corp"),
            MakeEnvelope(securityId, version: 2, timestamp: T0.AddDays(5), displayName: "Acme Corporation")
        });
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetByIdentifierAsync(
                Arg.Any<SecurityIdentifierKind>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(current);
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var service = new SecurityMasterQueryService(
            eventStore, store, new SecurityMasterAggregateRebuilder(eventStore, snapshotStore));

        var historical = await service.GetByIdentifierAsync(
            SecurityIdentifierKind.Ticker, "ACME", null, asOfUtc: T0.AddDays(2));
        var currentDetail = await service.GetByIdentifierAsync(
            SecurityIdentifierKind.Ticker, "ACME", null);

        historical.Should().NotBeNull();
        historical!.DisplayName.Should().Be("Acme Corp");
        currentDetail.Should().NotBeNull();
        currentDetail!.DisplayName.Should().Be("Acme Corporation");
    }

    [Fact]
    public async Task GetByIdentifierAsync_WithAsOf_FallsBackToStableIdentity_WhenIdentifierRowPostdatesAsOf()
    {
        // The CUSIP identity was backfilled after the security already existed, so its recorded
        // ValidFrom postdates the requested as-of even though the security was live then. The
        // point-in-time lookup should still resolve the (stable) identity and return the terms as
        // recorded at that time, instead of returning null.
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        eventStore.LoadAsync(securityId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            MakeEnvelope(securityId, version: 1, timestamp: T0, displayName: "Acme Corp")
        });

        var record = MakeProjection(securityId, "Acme Corporation") with
        {
            Identifiers = new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "ACME", true, T0.AddDays(-10), null, null),
                new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "037833100", false, T0.AddDays(10), null, null)
            }
        };
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SecurityProjectionRecord>>(new[] { record }));
        // Leaving store.GetByIdentifierAsync unconfigured makes the exact-match passes return null,
        // forcing the in-memory universe scan and then the as-of identity fallback.
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var service = new SecurityMasterQueryService(
            eventStore, store, new SecurityMasterAggregateRebuilder(eventStore, snapshotStore));

        var historical = await service.GetByIdentifierAsync(
            SecurityIdentifierKind.Cusip, "037833100", null, asOfUtc: T0.AddDays(2));

        historical.Should().NotBeNull();
        historical!.SecurityId.Should().Be(securityId);
        historical.DisplayName.Should().Be("Acme Corp");
    }

    private static SecurityMasterQueryService CreateService(
        ISecurityMasterEventStore eventStore,
        SecurityProjectionRecord? currentProjection)
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(currentProjection);
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        return new SecurityMasterQueryService(
            eventStore, store, new SecurityMasterAggregateRebuilder(eventStore, snapshotStore));
    }

    private static SecurityMasterEventEnvelope MakeEnvelope(
        Guid securityId, long version, DateTimeOffset timestamp, string displayName)
        => new(
            GlobalSequence: version,
            SecurityId: securityId,
            StreamVersion: version,
            EventType: "SecAmended",
            EventTimestamp: timestamp,
            Actor: "test",
            CorrelationId: null,
            CausationId: null,
            Payload: JsonSerializer.SerializeToElement(
                MakeProjection(securityId, displayName, version),
                Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord),
            Metadata: JsonSerializer.SerializeToElement(new { }));

    private static SecurityProjectionRecord MakeProjection(
        Guid securityId, string displayName, long version = 7)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: displayName,
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "ACME",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName,
                currency = "USD",
                exchange = "XNYS"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                shareClass = "Common",
                classification = "Common"
            }),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                updatedBy = "codex",
                asOf = T0
            }),
            Version: version,
            EffectiveFrom: T0.AddDays(-10),
            EffectiveTo: null,
            Identifiers: new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "ACME", true, T0.AddDays(-10), null, null)
            },
            Aliases: Array.Empty<SecurityAliasDto>());
}
