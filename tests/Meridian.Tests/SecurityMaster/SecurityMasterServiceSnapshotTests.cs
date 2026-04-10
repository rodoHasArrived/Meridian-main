using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterServiceSnapshotTests
{
    [Fact]
    public async Task CreateAsync_SavesCanonicalEconomicSnapshotPayload()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
            ResolveInactiveByDefault = true
        };
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);

        await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "Deposit",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Deposit Snapshot",
                currency = "USD",
                issuerName = "Meridian Bank"
            }),
            JsonSerializer.SerializeToElement(new
            {
                depositType = "TimeDeposit",
                institutionName = "Meridian Bank",
                maturity = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
                interestRate = 0.05m,
                dayCount = "ACT/360",
                isCallable = false
            }),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "DEP-TEST", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
            },
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "create"));

        await snapshotStore.Received(1).SaveAsync(
            Arg.Is<SecuritySnapshotRecord>(snapshot => MatchesCanonicalEconomicSnapshot(snapshot, securityId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_RebuildsFromSnapshotAndTailEvents_WhenProjectionStoreIsMissing()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
            ResolveInactiveByDefault = true
        };
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);

        var snapshotProjection = CreateProjection(securityId, "Equity", SecurityStatusDto.Active, "Snapshot Name", 1);
        var tailProjection = CreateProjection(securityId, "Equity", SecurityStatusDto.Active, "Tail Name", 2);

        snapshotStore.LoadAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecuritySnapshotRecord(
                securityId,
                1,
                DateTimeOffset.UtcNow,
                JsonSerializer.SerializeToElement(snapshotProjection, Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord)));

        eventStore.LoadAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new SecurityMasterEventEnvelope(
                    null,
                    securityId,
                    1,
                    "SecurityCreated",
                    DateTimeOffset.UtcNow.AddMinutes(-2),
                    "codex",
                    null,
                    null,
                    JsonSerializer.SerializeToElement(snapshotProjection, Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord),
                    JsonSerializer.SerializeToElement(new { sourceSystem = "test" })),
                new SecurityMasterEventEnvelope(
                    null,
                    securityId,
                    2,
                    "TermsAmended",
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    "codex",
                    null,
                    null,
                    JsonSerializer.SerializeToElement(tailProjection, Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord),
                    JsonSerializer.SerializeToElement(new { sourceSystem = "test" }))
            });

        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns((SecurityProjectionRecord?)null);

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);

        await service.DeactivateAsync(new DeactivateSecurityRequest(
            securityId,
            2,
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "deactivate"));

        await snapshotStore.Received(1).LoadAsync(securityId, Arg.Any<CancellationToken>());
        await eventStore.Received(1).LoadAsync(securityId, Arg.Any<CancellationToken>());
        await eventStore.Received(1).AppendAsync(
            securityId,
            2,
            Arg.Is<IReadOnlyList<SecurityMasterEventEnvelope>>(events =>
                events.Count == 1 &&
                events[0].EventType == "SecurityDeactivated" &&
                events[0].StreamVersion == 3),
            Arg.Any<CancellationToken>());
        await snapshotStore.Received(1).SaveAsync(
            Arg.Is<SecuritySnapshotRecord>(snapshot => snapshot.SecurityId == securityId && snapshot.Version == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_RecordsConflictsWithoutFailingWhenDetectionThrows()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        var cache = new SecurityMasterProjectionCache();

        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
            ResolveInactiveByDefault = true
        };
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);

        eventStore.AppendAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        store.UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        snapshotStore.SaveAsync(Arg.Any<SecuritySnapshotRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        conflictService.RecordConflictsForProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("conflict detection failure"));

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance,
            conflictService,
            projectionCache: cache);

        var request = new CreateSecurityRequest(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Equity Conflict",
                currency = "USD",
                exchange = "XNYS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            JsonSerializer.SerializeToElement(new
            {
                shareClass = "Common"
            }),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "EQ-CONFLICT", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
            },
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "create");

        var detail = await service.CreateAsync(request);

        detail.SecurityId.Should().Be(securityId);
        cache.Get(securityId).Should().NotBeNull();
        await conflictService.Received(1).RecordConflictsForProjectionAsync(
            Arg.Is<SecurityProjectionRecord>(p => p.SecurityId == securityId),
            Arg.Any<CancellationToken>());
    }

    private static SecurityProjectionRecord CreateProjection(
        Guid securityId,
        string assetClass,
        SecurityStatusDto status,
        string displayName,
        long version)
        => new(
            securityId,
            assetClass,
            status,
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

    private static bool MatchesCanonicalEconomicSnapshot(SecuritySnapshotRecord snapshot, Guid securityId)
    {
        if (snapshot.SecurityId != securityId || snapshot.Version != 1)
        {
            return false;
        }

        if (!snapshot.Payload.TryGetProperty("classification", out var classification) ||
            !classification.TryGetProperty("assetClass", out var assetClass) ||
            !string.Equals(assetClass.GetString(), "CashEquivalent", StringComparison.Ordinal))
        {
            return false;
        }

        if (!snapshot.Payload.TryGetProperty("economicTerms", out var economicTerms) ||
            !economicTerms.TryGetProperty("schemaVersion", out var schemaVersion) ||
            schemaVersion.GetInt32() != 2)
        {
            return false;
        }

        return snapshot.Payload.TryGetProperty("legacyAssetClass", out var legacyAssetClass) &&
               string.Equals(legacyAssetClass.GetString(), "Deposit", StringComparison.Ordinal);
    }
}
