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
    public async Task CreateAsync_WhenProjectionWritten_RecordsConflictsAfterUpsert()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
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
            NullLogger<SecurityMasterService>.Instance,
            conflictService);

        await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Conflict Recording Security",
                currency = "USD"
            }),
            JsonSerializer.SerializeToElement(new
            {
                shareClass = "Common"
            }),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "CRSEC", true, DateTimeOffset.UtcNow.AddDays(-1), null, "polygon")
            },
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "create"));

        Received.InOrder(() =>
        {
            store.UpsertProjectionAsync(
                Arg.Is<SecurityProjectionRecord>(projection => projection.SecurityId == securityId),
                Arg.Any<CancellationToken>());
            conflictService.RecordConflictsForProjectionAsync(
                Arg.Is<SecurityProjectionRecord>(projection =>
                    projection.SecurityId == securityId &&
                    projection.PrimaryIdentifierValue == "CRSEC"),
                Arg.Any<CancellationToken>());
        });
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
    public async Task DeactivateAsync_UnknownEquityClassificationWithNestedTerms_RefusesTheWrite()
    {
        // Read tolerance degrades an unrecognized classification to Other(raw), which re-serializes
        // WITHOUT the nested preferred/convertible blocks — so a write from this node would silently
        // delete structure it did not understand. The guard must refuse before any event append.
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

        var projection = CreateProjection(securityId, "Equity", SecurityStatusDto.Active, "Tracking Stock", 2) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                classification = "TrackingStock",
                preferredTerms = new { dividendType = "Cumulative" }
            })
        };
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(projection);

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);

        await service.Invoking(s => s.DeactivateAsync(new DeactivateSecurityRequest(
                securityId,
                2,
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "deactivate")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*classification 'TrackingStock'*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_BondWithUndeclaredCouponType_RefusesTheWrite()
    {
        // couponType is the trichotomy's third case — no verbatim carry, no escape — so a stored
        // "floating" reads as Fixed(0) and re-serializing it would drop the floating index and
        // spread. A deactivation re-serializes the whole kind, so it is refused like an amend.
        var securityId = Guid.NewGuid();
        var (eventStore, service) = CreateBondCouponHarness(securityId, "floating");

        await service.Invoking(s => s.DeactivateAsync(new DeactivateSecurityRequest(
                securityId,
                2,
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "deactivate")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*couponType 'floating'*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_BondWithUndeclaredCouponTypeAndNoAssetTermsPatch_RefusesTheWrite()
    {
        // An amendment that touches only common terms (a rename, a ticker change, a backfill) still
        // re-serializes the kind, so it completes the same rewrite as a deactivate.
        var securityId = Guid.NewGuid();
        var (eventStore, service) = CreateBondCouponHarness(securityId, "floating");

        await service.Invoking(s => s.AmendTermsAsync(BondAmendRequest(securityId, assetSpecificTermsPatch: null)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*couponType 'floating'*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_BondCouponTypePatchRepeatingTheUndeclaredValue_StillRefusesTheWrite()
    {
        // The exit is not a loophole: a patch that re-asserts the same undecodable value would be
        // decoded to the same Fixed fallback, completing the rewrite by another route.
        var securityId = Guid.NewGuid();
        var (eventStore, service) = CreateBondCouponHarness(securityId, "floating");

        await service.Invoking(s => s.AmendTermsAsync(BondAmendRequest(
                securityId,
                JsonSerializer.SerializeToElement(new
                {
                    maturity = "2035-06-15",
                    couponType = "floating",
                    floatingIndex = "SOFR",
                    isCallable = false,
                    subclass = "FloatingRate"
                }))))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*couponType 'floating'*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_BondCouponTypePatchNamingADeclaredValue_RepairsTheRecord()
    {
        // The governed exit the refusal instructs. Without it the refusal is a dead end: unlike an
        // unrecognized asset class, an undeclared couponType names no other node that could apply
        // the change, so the row would be permanently unamendable AND undeactivatable. The patch
        // replaces the kind wholesale, so the misread coupon is never re-serialized.
        var securityId = Guid.NewGuid();
        var (eventStore, service) = CreateBondCouponHarness(securityId, "floating");

        var detail = await service.AmendTermsAsync(BondAmendRequest(
            securityId,
            JsonSerializer.SerializeToElement(new
            {
                maturity = "2035-06-15",
                couponType = "Floating",
                floatingIndex = "SOFR",
                spreadBps = 185m,
                isCallable = false,
                subclass = "FloatingRate"
            })));

        detail.AssetSpecificTerms.GetProperty("couponType").GetString().Should().Be("Floating");
        detail.AssetSpecificTerms.GetProperty("floatingIndex").GetString().Should().Be("SOFR");
        await eventStore.Received(1).AppendAsync(
            securityId,
            2,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_BondWithDeclaredCouponType_IsNotRefused()
    {
        // The guard must not touch the ordinary bond lifecycle — a canonical discriminant, and an
        // absent couponType keeping its documented Fixed default, both still write.
        foreach (var couponType in new string?[] { "Floating", null })
        {
            var securityId = Guid.NewGuid();
            var (eventStore, service) = CreateBondCouponHarness(securityId, couponType);

            await service.DeactivateAsync(new DeactivateSecurityRequest(
                securityId,
                2,
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "deactivate"));

            await eventStore.Received(1).AppendAsync(
                securityId,
                2,
                Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task DeactivateAsync_BondWithOnlyALegacyNestedCoupon_RefusesTheWrite()
    {
        // The vocabulary walk inspects the FLAT couponType the serializer writes, so a coupon
        // carried in the legacy nested object slips past it — while being lossy in exactly the same
        // way: ToBondTerms has no nested-coupon fallback, reads the row as a fixed coupon, and
        // re-serializes it flat, dropping the nested index and spread. The projection store reads
        // this shape deliberately for externally-authored payloads, so such rows are expected.
        var securityId = Guid.NewGuid();
        var (eventStore, service) = CreateNestedCouponBondHarness(securityId, includeFlatCouponType: false);

        await service.Invoking(s => s.DeactivateAsync(new DeactivateSecurityRequest(
                securityId,
                2,
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "deactivate")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*a legacy nested 'coupon' object but no couponType*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_LegacyNestedCouponPatchNamingADeclaredCouponType_RepairsTheRecord()
    {
        // Same repair exit: the patch replaces the kind wholesale, so the nested object is not
        // re-serialized away — it is superseded by flat coupon fields the codec actually reads.
        var securityId = Guid.NewGuid();
        var (eventStore, service) = CreateNestedCouponBondHarness(securityId, includeFlatCouponType: false);

        var detail = await service.AmendTermsAsync(BondAmendRequest(
            securityId,
            JsonSerializer.SerializeToElement(new
            {
                maturity = "2035-06-15",
                couponType = "Floating",
                floatingIndex = "SOFR",
                spreadBps = 125m,
                isCallable = false,
                subclass = "FloatingRate"
            })));

        detail.AssetSpecificTerms.GetProperty("couponType").GetString().Should().Be("Floating");
        detail.AssetSpecificTerms.GetProperty("floatingIndex").GetString().Should().Be("SOFR");
        await eventStore.Received(1).AppendAsync(
            securityId,
            2,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_BondWithFlatCouponTypeAlongsideANestedCoupon_IsNotRefused()
    {
        // When both shapes are present the flat keys are authoritative and already describe the
        // structure — the same precedence the projection store applies — so the vestigial nested
        // object carries nothing the record would lose, and the lifecycle must not be blocked.
        var securityId = Guid.NewGuid();
        var (eventStore, service) = CreateNestedCouponBondHarness(securityId, includeFlatCouponType: true);

        await service.DeactivateAsync(new DeactivateSecurityRequest(
            securityId,
            2,
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "deactivate"));

        await eventStore.Received(1).AppendAsync(
            securityId,
            2,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_BondWithOrphanedFlatCouponStructure_RefusesTheWrite()
    {
        // The same defect one step wider: no couponType at all, but a populated floatingIndex and
        // spread. The codec reads the record as a fixed coupon and re-serializes it that way,
        // orphaning the structure — a very plausible vendor payload.
        var securityId = Guid.NewGuid();
        var (eventStore, service) = CreateBondHarness(securityId, new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["maturity"] = "2035-06-15",
            ["floatingIndex"] = "SOFR",
            ["spreadBps"] = 125m,
            ["isCallable"] = false,
            ["subclass"] = "FloatingRate"
        });

        await service.Invoking(s => s.DeactivateAsync(new DeactivateSecurityRequest(
                securityId,
                2,
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "deactivate")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*floatingIndex and spreadBps*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_BondWithNoCouponTypeAndEmptyCouponCompanions_IsNotRefused()
    {
        // The false-positive filter that makes the orphan check safe, exercised on the only shape
        // that reaches it — couponType absent, so the guard actually walks the companions. They are
        // null/[] here, which is exactly what the canonical serializer emits for a genuine fixed
        // coupon, so a presence-only test would refuse the most ordinary bond there is.
        var securityId = Guid.NewGuid();
        var (eventStore, service) = CreateBondHarness(securityId, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["maturity"] = "2035-06-15",
            ["couponRate"] = 4.25m,
            ["floatingIndex"] = null,
            ["spreadBps"] = null,
            ["capRate"] = null,
            ["floorRate"] = null,
            ["inflationIndex"] = null,
            ["stepSchedule"] = Array.Empty<object>(),
            ["isCallable"] = false,
            ["subclass"] = "Corporate"
        });

        await service.DeactivateAsync(new DeactivateSecurityRequest(
            securityId,
            2,
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "deactivate"));

        await eventStore.Received(1).AppendAsync(
            securityId,
            2,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A stored bond whose asset-specific terms are exactly <paramref name="terms"/>.</summary>
    private static (ISecurityMasterEventStore EventStore, SecurityMasterService Service) CreateBondHarness(
        Guid securityId,
        object terms)
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
            ResolveInactiveByDefault = true
        };
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);

        var projection = CreateProjection(securityId, "Bond", SecurityStatusDto.Active, "Bond under guard", 2) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(terms)
        };
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(projection);

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);

        return (eventStore, service);
    }

    /// <summary>
    /// A stored bond whose coupon lives in the legacy nested <c>coupon</c> object, optionally
    /// alongside the flat <c>couponType</c> the canonical serializer writes.
    /// </summary>
    private static (ISecurityMasterEventStore EventStore, SecurityMasterService Service) CreateNestedCouponBondHarness(
        Guid securityId,
        bool includeFlatCouponType)
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
            ResolveInactiveByDefault = true
        };
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);

        var terms = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["maturity"] = "2035-06-15",
            ["coupon"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["kind"] = "Floating",
                ["index"] = "SOFR",
                ["spreadBps"] = 125m
            },
            ["isCallable"] = false,
            ["subclass"] = "FloatingRate"
        };
        if (includeFlatCouponType)
        {
            terms["couponType"] = "Floating";
            terms["floatingIndex"] = "SOFR";
        }

        var projection = CreateProjection(securityId, "Bond", SecurityStatusDto.Active, "External floater", 2) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(terms)
        };
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(projection);

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);

        return (eventStore, service);
    }

    private static AmendSecurityTermsRequest BondAmendRequest(Guid securityId, JsonElement? assetSpecificTermsPatch)
        => new(
            SecurityId: securityId,
            ExpectedVersion: 2,
            CommonTerms: null,
            AssetSpecificTermsPatch: assetSpecificTermsPatch,
            IdentifiersToAdd: Array.Empty<SecurityIdentifierDto>(),
            IdentifiersToExpire: Array.Empty<SecurityIdentifierDto>(),
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "test",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "amend");

    /// <summary>
    /// A stored floating-rate bond whose <c>couponType</c> is <paramref name="couponType"/>, or one
    /// with no <c>couponType</c> key at all when it is <see langword="null"/>.
    /// </summary>
    private static (ISecurityMasterEventStore EventStore, SecurityMasterService Service) CreateBondCouponHarness(
        Guid securityId,
        string? couponType)
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
            ResolveInactiveByDefault = true
        };
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);

        var terms = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["maturity"] = "2035-06-15",
            ["floatingIndex"] = "SOFR",
            ["spreadBps"] = 185m,
            ["isCallable"] = false,
            ["subclass"] = "FloatingRate"
        };
        if (couponType is not null)
        {
            terms["couponType"] = couponType;
        }

        var projection = CreateProjection(securityId, "Bond", SecurityStatusDto.Active, "Vendor floater", 2) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(terms)
        };
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(projection);

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);

        return (eventStore, service);
    }

    [Fact]
    public async Task DeactivateAsync_LegacyCustomAssetWithoutProfileEnvelope_RefusesTheWrite()
    {
        // A CustomAsset row that predates the profile envelope reads through the OtherSecurity
        // salvage path even though "CustomAsset" is in the catalog; re-serializing that fallback
        // would drop the record's unmodeled custom fields, so the write must be refused.
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

        var projection = CreateProjection(securityId, "CustomAsset", SecurityStatusDto.Active, "Legacy Custom", 2) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                category = "Litigation Finance",
                bespokeField = "unmodeled"
            })
        };
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(projection);

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);

        await service.Invoking(s => s.DeactivateAsync(new DeactivateSecurityRequest(
                securityId,
                2,
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "deactivate")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*without a profile envelope*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_LegacyCustomAssetWithFullEnvelopePatch_MigratesOntoTheProfile()
    {
        // The envelope-less guard's refusal message instructs a governed migration — this IS that
        // migration: an amendment whose patch carries a complete profile envelope replaces the
        // terms wholesale, so the lossy OtherSecurity fallback is never persisted and the legacy
        // record is not permanently unamendable.
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

        var previous = CreateProjection(securityId, "CustomAsset", SecurityStatusDto.Active, "Legacy Custom", 2) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                category = "Litigation Finance",
                bespokeField = "unmodeled"
            }),
            Provenance = JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "provA",
                asOf = DateTimeOffset.UtcNow.AddDays(-1),
                updatedBy = "feed"
            }),
            Identifiers = new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "LEGACY-MIG", true, DateTimeOffset.UtcNow.AddDays(-30), null, null)
            }
        };
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(previous);

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        // A patch WITHOUT an envelope stays refused — the record still reads through the salvage path.
        await service.Invoking(s => s.AmendTermsAsync(new AmendSecurityTermsRequest(
                securityId,
                2,
                CommonTerms: null,
                AssetSpecificTermsPatch: JsonSerializer.SerializeToElement(new { category = "Litigation Finance" }),
                IdentifiersToAdd: [],
                IdentifiersToExpire: [],
                EffectiveFrom: DateTimeOffset.UtcNow,
                SourceSystem: "provA",
                UpdatedBy: "codex",
                SourceRecordId: null,
                Reason: "amend")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*without a profile envelope*");

        // The full-envelope migration amendment persists and reclassifies.
        var detail = await service.AmendTermsAsync(new AmendSecurityTermsRequest(
            securityId,
            2,
            CommonTerms: null,
            AssetSpecificTermsPatch: JsonSerializer.SerializeToElement(new
            {
                customProfileId = "private-fund-interest",
                profileVersion = 1,
                profileFields = new
                {
                    gpSponsor = "Meridian Growth Partners",
                    strategy = "Buyout",
                    vintage = 2024,
                    commitment = 5_000_000m,
                    fundedAmount = 0m,
                    unfundedAmount = 5_000_000m,
                    navDate = "2026-06-30"
                },
                profileApproval = new
                {
                    approvedBy = "Meridian",
                    approvedAtUtc = "2026-05-29T00:00:00Z",
                    approvalReference = "MERIDIAN-SEED-APPROVAL"
                }
            }),
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "provA",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "migrate legacy custom asset onto a profile"));

        detail.AssetClass.Should().Be("PrivateFundInterest",
            "the migration amendment resolves exactly as the identical create would");
        await eventStore.Received(1).AppendAsync(
            securityId,
            2,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_CustomAssetWithUnknownProfile_RefusesTheCanonicalWrite()
    {
        // The F# domain command validates the envelope's shape but cannot see the profile catalog;
        // without the catalog check an unknown or unapproved profile persists canonically and is
        // only discovered by a later validation read.
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
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        await service.Invoking(s => s.CreateAsync(new CreateSecurityRequest(
                securityId,
                "CustomAsset",
                JsonSerializer.SerializeToElement(new { displayName = "Unknown Profile Asset", currency = "USD" }),
                JsonSerializer.SerializeToElement(new
                {
                    customProfileId = "not-a-registered-profile",
                    profileVersion = 1,
                    profileFields = new { anything = "x" }
                }),
                new[]
                {
                    new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "CUST-UNKNOWN", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
                },
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "create")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*fails catalog validation*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_CustomAssetWithoutProfileEnvelope_RefusesTheWriteInsteadOfSalvaging()
    {
        // The OtherSecurity salvage in the kind mapping is READ tolerance for legacy rows. A
        // create request naming CustomAsset without a customProfileId must fail the command:
        // silently re-typing it to OtherSecurity would skip every CustomAsset profile invariant
        // and persist a record the operator never asked for.
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
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        await service.Invoking(s => s.CreateAsync(new CreateSecurityRequest(
                securityId,
                "CustomAsset",
                JsonSerializer.SerializeToElement(new { displayName = "Envelope-less Custom Asset", currency = "USD" }),
                JsonSerializer.SerializeToElement(new
                {
                    category = "Litigation Finance",
                    subType = "Case Portfolio"
                }),
                new[]
                {
                    new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "CUST-NO-ENVELOPE", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
                },
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "create")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*customProfileId*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ReclassifiedProfileBackedRecord_ValidatesProfileWithoutOtherSecurityRules()
    {
        // A recognized profile reclassifies the projection to its resolved asset class
        // (private-fund-interest → PrivateFundInterest). Profile validation must run on its own —
        // applying the OtherSecurity composite would reject this valid payload solely because it
        // omits the outer `category` field, which the CustomAsset schema declares OPTIONAL.
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
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        var detail = await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "CustomAsset",
            JsonSerializer.SerializeToElement(new { displayName = "Meridian Growth Fund III LP", currency = "USD" }),
            JsonSerializer.SerializeToElement(new
            {
                customProfileId = "private-fund-interest",
                profileVersion = 1,
                profileFields = new
                {
                    gpSponsor = "Meridian Growth Partners",
                    strategy = "Buyout",
                    vintage = 2024,
                    commitment = 5_000_000m,
                    fundedAmount = 2_000_000m,
                    unfundedAmount = 3_000_000m,
                    navDate = "2026-06-30"
                },
                profileApproval = new
                {
                    approvedBy = "Meridian",
                    approvedAtUtc = "2026-05-29T00:00:00Z",
                    approvalReference = "MERIDIAN-SEED-APPROVAL"
                }
            }),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "PFI-III", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
            },
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "create"));

        detail.SecurityId.Should().Be(securityId);
        await eventStore.Received(1).AppendAsync(
            securityId,
            0,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_BondWithMalformedPrincipalScheduleContainer_Rejects()
    {
        // A principalSchedule that is present but not an array must fail the write instead of
        // reading as absent — silently deleting the submitted schedule would persist a snapshot
        // that projects the bond as a bullet.
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

        await service.Invoking(s => s.CreateAsync(new CreateSecurityRequest(
                securityId,
                "Bond",
                JsonSerializer.SerializeToElement(new { displayName = "Malformed Sinker", currency = "USD" }),
                JsonSerializer.SerializeToElement(new
                {
                    maturity = "2031-06-15",
                    couponRate = 0.05m,
                    principalSchedule = "not-an-array"
                }),
                new[]
                {
                    new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "BOND-MALF", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
                },
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "create")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'principalSchedule' must be a JSON array*");
    }

    [Fact]
    public async Task AmendTermsAsync_FieldConflictRecordingFails_RefusesTheAmendment()
    {
        // Once the event and projection persist, the previous source value is overwritten and the
        // cross-source disagreement cannot be reconstructed — so a conflict-store failure must fail
        // the amend BEFORE anything persists, not be swallowed after.
        var (securityId, eventStore, service, conflictService, _) = BuildAmendHarness(
            conflictRecordingFails: true);

        await service.Invoking(s => s.AmendTermsAsync(BuildConflictingAmend(securityId)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*conflict store down*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
        await conflictService.Received(1).RecordFieldConflictsAsync(
            Arg.Any<SecurityProjectionRecord>(),
            Arg.Any<SecurityProjectionRecord>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_ConflictedField_RetiresStaleResolutionProvenance()
    {
        // A field that just changed hands invalidates the prior ConflictResolution attribution —
        // the recorded winner no longer supplied the current value.
        var (securityId, eventStore, service, _, fieldProvenance) = BuildAmendHarness(
            conflictRecordingFails: false);

        await service.AmendTermsAsync(BuildConflictingAmend(securityId));

        await eventStore.Received(1).AppendAsync(
            securityId,
            2,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
        await fieldProvenance.Received(1).RemoveAsync(
            securityId,
            "EconomicTerms.couponRate",
            SecurityFieldProvenanceOrigins.ConflictResolution,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_ProfileBackedRecordWithoutEnvelopeInPatch_RefusesTheAmendment()
    {
        // A profile-backed record's asset terms ARE the envelope. Restoring the previous envelope
        // over an ordinary patch would append an event and advance the version while silently
        // discarding every requested value — refuse instead.
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

        var previous = CreateProjection(securityId, "CustomAsset", SecurityStatusDto.Active, "Profile-backed asset", 2) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                customProfileId = "structured-credit-io-po",
                profileVersion = 1,
                profileFields = new { }
            }),
            Provenance = JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "provA",
                asOf = DateTimeOffset.UtcNow.AddDays(-1),
                updatedBy = "feed"
            })
        };
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(previous);

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance);

        await service.Invoking(s => s.AmendTermsAsync(new AmendSecurityTermsRequest(
                securityId,
                2,
                CommonTerms: null,
                AssetSpecificTermsPatch: JsonSerializer.SerializeToElement(new { category = "Litigation Finance" }),
                IdentifiersToAdd: [],
                IdentifiersToExpire: [],
                EffectiveFrom: DateTimeOffset.UtcNow,
                SourceSystem: "provB",
                UpdatedBy: "codex",
                SourceRecordId: null,
                Reason: "amend")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*profile-backed*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_WinningSourceAmendsItsOwnValue_StillRetiresStaleResolutionProvenance()
    {
        // The previous winner amending its OWN value opens no cross-source conflict, but the old
        // ConflictResolution attribution is stale all the same — it selected an earlier value.
        var (securityId, eventStore, service, conflictService, fieldProvenance) = BuildAmendHarness(
            conflictRecordingFails: false);

        await service.AmendTermsAsync(BuildConflictingAmend(securityId) with { SourceSystem = "provA" });

        await conflictService.DidNotReceive().RecordFieldConflictsAsync(
            Arg.Any<SecurityProjectionRecord>(),
            Arg.Any<SecurityProjectionRecord>(),
            Arg.Any<CancellationToken>());
        await eventStore.Received(1).AppendAsync(
            securityId,
            2,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
        await fieldProvenance.Received(1).RemoveAsync(
            securityId,
            "EconomicTerms.couponRate",
            SecurityFieldProvenanceOrigins.ConflictResolution,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_DelayedLowVersionAttribution_DoesNotSuppressTheRealConflict()
    {
        // Cross-origin incumbent precedence follows the attributed projection VERSION, not the
        // callback's wall-clock recording time. A delayed v2 CanonicalWrite row (provA) recorded
        // AFTER a conflict resolution that validated v3 (winner provB) must not resurrect provA as
        // the incumbent: provA's next amendment would then look like same-source versioning and
        // the pre-check would silently suppress the real provA-vs-provB disagreement.
        var (securityId, _, service, conflictService, fieldProvenance) = BuildAmendHarness(
            conflictRecordingFails: false);
        fieldProvenance.GetAsync(securityId, Arg.Any<CancellationToken>()).Returns(
        [
            new SecurityFieldProvenanceRecord(
                securityId, "EconomicTerms.couponRate", "provA",
                AsOf: null, UpdatedBy: "feed", Confidence: null,
                Origin: SecurityFieldProvenanceOrigins.CanonicalWrite,
                OriginReference: "version:2", RecordedAt: DateTimeOffset.UtcNow,
                SourceVersion: 2),
            new SecurityFieldProvenanceRecord(
                securityId, "EconomicTerms.couponRate", "provB",
                AsOf: null, UpdatedBy: "operator", Confidence: null,
                Origin: SecurityFieldProvenanceOrigins.ConflictResolution,
                OriginReference: Guid.NewGuid().ToString("D"), RecordedAt: DateTimeOffset.UtcNow.AddHours(-1),
                SourceVersion: 3),
        ]);

        await service.AmendTermsAsync(BuildConflictingAmend(securityId) with { SourceSystem = "provA" });

        await conflictService.Received(1).RecordFieldConflictsAsync(
            Arg.Any<SecurityProjectionRecord>(),
            Arg.Any<SecurityProjectionRecord>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_ConcurrentAmendment_WaitsForThePriorAttributionWrite()
    {
        // Amendment C committing its projection and pausing before its attribution write must not
        // let amendment B read the PREVIOUS incumbent's field row: B would durably record a
        // conflict pairing the old source with C's value — a mispairing source-version ordering
        // cannot repair once persisted. Same-security amendments serialize from the conflict
        // pre-check through the attribution write.
        var (securityId, _, service, _, fieldProvenance) = BuildAmendHarness(conflictRecordingFails: false);
        var attributionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAttribution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstAttribution = true;
        fieldProvenance.UpsertAsync(Arg.Any<SecurityFieldProvenanceRecord>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (firstAttribution)
                {
                    firstAttribution = false;
                    attributionEntered.TrySetResult();
                    await releaseAttribution.Task;
                }
            });

        var firstAmend = service.AmendTermsAsync(BuildConflictingAmend(securityId));
        await attributionEntered.Task;

        var secondAmend = service.AmendTermsAsync(BuildConflictingAmend(securityId));
        await Task.Delay(100);
        secondAmend.IsCompleted.Should().BeFalse(
            "a concurrent same-security amendment must wait until the prior amendment's attribution write lands");

        releaseAttribution.TrySetResult();
        await Task.WhenAll(firstAmend, secondAmend);
    }

    [Fact]
    public async Task AmendTermsAsync_AttributionReadFails_StillInvokesDurableConflictRecording()
    {
        // When the per-field attribution read FAILS, the pre-check's same-source shortcut is not
        // authoritative: the last record writer may be changing a field another provider supplied,
        // and skipping the durable conflict service would silently omit that disagreement. The
        // durable service performs its own attribution read, so it must always be invoked after a
        // failed pre-check read.
        var (securityId, eventStore, service, conflictService, fieldProvenance) = BuildAmendHarness(
            conflictRecordingFails: false);
        fieldProvenance.GetAsync(securityId, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<SecurityFieldProvenanceRecord>>>(
                static _ => throw new InvalidOperationException("provenance store down"));

        await service.AmendTermsAsync(BuildConflictingAmend(securityId) with { SourceSystem = "provA" });

        await conflictService.Received(1).RecordFieldConflictsAsync(
            Arg.Any<SecurityProjectionRecord>(),
            Arg.Any<SecurityProjectionRecord>(),
            Arg.Any<CancellationToken>());
        await eventStore.Received(1).AppendAsync(
            securityId,
            2,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_ChangedGovernedField_RecordsCanonicalWriteAttribution()
    {
        // Record-level provenance flips on every amendment; the per-field CanonicalWrite row is
        // what lets conflict detection name the source that actually supplied the changed field.
        var (securityId, _, service, _, fieldProvenance) = BuildAmendHarness(
            conflictRecordingFails: false);

        await service.AmendTermsAsync(BuildConflictingAmend(securityId));

        await fieldProvenance.Received(1).UpsertAsync(
            Arg.Is<SecurityFieldProvenanceRecord>(record =>
                record.SecurityId == securityId
                && record.FieldPath == "EconomicTerms.couponRate"
                && record.SourceSystem == "provB"
                && record.Origin == SecurityFieldProvenanceOrigins.CanonicalWrite),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_AttributionCanceled_StillReturnsTheAmendedRecord()
    {
        // By the time attribution runs, the canonical event append and projection upsert have
        // COMMITTED — the amend has succeeded. A request token canceled during the best-effort
        // lineage write must not surface as a canceled amend: the caller would retry with the
        // original expected version, fail concurrency, and be unable to repair lineage the
        // invalidation fallback already handles.
        var (securityId, _, service, _, fieldProvenance) = BuildAmendHarness(
            conflictRecordingFails: false);
        fieldProvenance.UpsertAsync(Arg.Any<SecurityFieldProvenanceRecord>(), Arg.Any<CancellationToken>())
            .Returns<Task>(static _ => throw new OperationCanceledException("request aborted"));

        var amended = await service.AmendTermsAsync(BuildConflictingAmend(securityId));

        amended.Should().NotBeNull(
            "cancellation during the best-effort attribution write must not hide the committed amendment");
    }

    [Fact]
    public async Task AmendTermsAsync_SnapshotCanceled_StillReturnsTheAmendedRecord()
    {
        // The snapshot write runs AFTER the event append and projection upsert have committed. A
        // request token canceled mid-amend must not surface a canceled amendment (the retry would
        // fail concurrency on the advanced version) nor skip the cache/registry updates that
        // follow the snapshot.
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        snapshotStore.SaveAsync(Arg.Any<SecuritySnapshotRecord>(), Arg.Any<CancellationToken>())
            .Returns(static call =>
            {
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        var (securityId, _, service, _, fieldProvenance) = BuildAmendHarness(
            conflictRecordingFails: false, snapshotStore: snapshotStore, snapshotIntervalVersions: 1);

        // Cancel at the stale-attribution retirement step: it runs immediately AFTER the durable
        // projection upsert, so every later post-commit step observes a canceled request token.
        using var cts = new CancellationTokenSource();
        fieldProvenance.RemoveAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            });

        var amended = await service.AmendTermsAsync(BuildConflictingAmend(securityId), cts.Token);

        amended.Should().NotBeNull(
            "cancellation arriving after the canonical writes committed must not hide the amendment behind the snapshot write");
    }

    [Fact]
    public async Task AmendTermsAsync_ReconcileSweepCanceled_StillReturnsTheAmendedRecord()
    {
        // The post-persist open-conflict reconciliation sweep is best-effort and runs after the
        // canonical writes committed: a request token canceled during the sweep must not surface
        // as a canceled amendment (the retry would fail concurrency) nor skip the remaining
        // post-commit steps.
        var (securityId, _, service, conflictService, _) = BuildAmendHarness(
            conflictRecordingFails: false);
        conflictService.ReconcileOpenFieldConflictsAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>())
            .Returns<Task>(static _ => throw new OperationCanceledException("request aborted"));

        var amended = await service.AmendTermsAsync(BuildConflictingAmend(securityId));

        amended.Should().NotBeNull(
            "cancellation during the best-effort reconciliation sweep must not hide the committed amendment");
    }

    [Fact]
    public async Task CreateAsync_GovernedFields_SeedCanonicalWriteAttribution()
    {
        // Creation is the first canonical write of every governed field the record supplies, so
        // per-field attribution must exist from version 1 — not only after the first amendment.
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var fieldProvenance = Substitute.For<ISecurityFieldProvenanceStore>();
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
            NullLogger<SecurityMasterService>.Instance,
            fieldProvenance: fieldProvenance);

        await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "Bond",
            JsonSerializer.SerializeToElement(new { displayName = "Attributed Bond", currency = "USD" }),
            JsonSerializer.SerializeToElement(new
            {
                maturity = "2031-06-15",
                couponRate = 4.25m,
                par = 1000m
            }),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "BOND-ATTR", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
            },
            DateTimeOffset.UtcNow,
            "provA",
            "codex",
            null,
            "create"));

        await fieldProvenance.Received(1).UpsertAsync(
            Arg.Is<SecurityFieldProvenanceRecord>(record =>
                record.SecurityId == securityId
                && record.FieldPath == "EconomicTerms.couponRate"
                && record.SourceSystem == "provA"
                && record.Origin == SecurityFieldProvenanceOrigins.CanonicalWrite),
            Arg.Any<CancellationToken>());
        await fieldProvenance.Received(1).UpsertAsync(
            Arg.Is<SecurityFieldProvenanceRecord>(record =>
                record.FieldPath == "CommonTerms.currency"
                && record.Origin == SecurityFieldProvenanceOrigins.CanonicalWrite),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ProfileMetadataContradictsResolvedClass_RefusesTheWrite()
    {
        // The profile is the identity of a profile-backed record: a registered profile id decides
        // the resolved class by itself, and envelope metadata naming a DIFFERENT class's keyword is
        // a contradiction to surface, not a signal to honor.
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
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        await service.Invoking(s => s.CreateAsync(new CreateSecurityRequest(
                securityId,
                "CustomAsset",
                JsonSerializer.SerializeToElement(new { displayName = "Mislabeled Fund Interest", currency = "USD" }),
                JsonSerializer.SerializeToElement(new
                {
                    customProfileId = "private-fund-interest",
                    profileVersion = 1,
                    category = "MBS",
                    profileFields = new
                    {
                        gpSponsor = "Meridian Growth Partners",
                        strategy = "Buyout",
                        vintage = 2024,
                        commitment = 5_000_000m,
                        fundedAmount = 2_000_000m,
                        unfundedAmount = 3_000_000m,
                        navDate = "2026-06-30"
                    }
                }),
                new[]
                {
                    new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "PFI-MIS", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
                },
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "create")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*belongs to 'StructuredCredit'*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_UnmappedProfileWithSpoofedClassMetadata_StaysCustomAsset()
    {
        // Field names and classification metadata are caller-controlled: a payload pinned to the
        // approved co-invest-spv profile that ALSO carries structured-credit field names and
        // category = "StructuredCredit" satisfies the co-invest profile's own rules, so it must
        // stay a CustomAsset — reclassifying on the spoofed shape would hand it StructuredCredit
        // validators and projection behavior its profile never granted.
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
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        var detail = await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "CustomAsset",
            JsonSerializer.SerializeToElement(new { displayName = "Spoofed SPV", currency = "USD" }),
            JsonSerializer.SerializeToElement(new
            {
                customProfileId = "co-invest-spv",
                profileVersion = 1,
                category = "StructuredCredit",
                profileFields = new
                {
                    vehicle = "Meridian Co-Invest I",
                    underlyingCompanyOrSecurity = "Acme Holdings",
                    sponsor = "Meridian Growth Partners",
                    commitment = 1_000_000m,
                    economics = "1/10 over 8",
                    reportingCadence = "Quarterly",
                    tranche = "A-1",
                    collateralType = "CLO",
                    originalFace = 1_000_000m,
                    couponOrIndex = "SOFR+250"
                },
                profileApproval = new
                {
                    approvedBy = "Meridian",
                    approvedAtUtc = "2026-05-29T00:00:00Z",
                    approvalReference = "MERIDIAN-SEED-APPROVAL"
                }
            }),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "SPV-SPOOF", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
            },
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "create"));

        detail.AssetClass.Should().Be("CustomAsset",
            "an unmapped profile id must never reclassify, whatever field names or metadata the envelope carries");
    }

    [Fact]
    public async Task CreateAsync_ReclassifiedRecordViolatingKindInvariants_RefusesTheWrite()
    {
        // The pinned profile's field ranges can be LOOSER than the resolved first-class kind's
        // domain rules (the profile allows commitment >= 0; PrivateFundInterest requires > 0).
        // Reclassification must re-run the resolved kind's invariants or the payload persists
        // under a class whose rules it never satisfied.
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
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        await service.Invoking(s => s.CreateAsync(new CreateSecurityRequest(
                securityId,
                "CustomAsset",
                JsonSerializer.SerializeToElement(new { displayName = "Zero Commitment Fund", currency = "USD" }),
                JsonSerializer.SerializeToElement(new
                {
                    customProfileId = "private-fund-interest",
                    profileVersion = 1,
                    profileFields = new
                    {
                        gpSponsor = "Meridian Growth Partners",
                        strategy = "Buyout",
                        vintage = 2024,
                        commitment = 0m,
                        fundedAmount = 0m,
                        unfundedAmount = 0m,
                        navDate = "2026-06-30"
                    },
                    profileApproval = new
                    {
                        approvedBy = "Meridian",
                        approvedAtUtc = "2026-05-29T00:00:00Z",
                        approvalReference = "MERIDIAN-SEED-APPROVAL"
                    }
                }),
                new[]
                {
                    new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "PFI-ZERO", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
                },
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "create")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*domain invariants*private_fund_commitment_invalid*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_FutureDatedProfileBackedRecord_ValidatesIdentifiersAtEffectiveTime()
    {
        // Catalog validation runs at the write's effective time, not the wall clock: a forward-dated
        // create whose required identifier becomes valid ON its EffectiveFrom is legitimate, and
        // evaluating coverage at "now" would refuse it.
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
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(30);
        var detail = await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "CustomAsset",
            JsonSerializer.SerializeToElement(new { displayName = "Forward-Dated Fund Interest", currency = "USD" }),
            JsonSerializer.SerializeToElement(new
            {
                customProfileId = "private-fund-interest",
                profileVersion = 1,
                profileFields = new
                {
                    gpSponsor = "Meridian Growth Partners",
                    strategy = "Buyout",
                    vintage = 2026,
                    commitment = 5_000_000m,
                    fundedAmount = 0m,
                    unfundedAmount = 5_000_000m,
                    navDate = "2026-06-30"
                },
                profileApproval = new
                {
                    approvedBy = "Meridian",
                    approvedAtUtc = "2026-05-29T00:00:00Z",
                    approvalReference = "MERIDIAN-SEED-APPROVAL"
                }
            }),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "PFI-FWD", true, effectiveFrom, null, null)
            },
            effectiveFrom,
            "test",
            "codex",
            null,
            "create"));

        detail.SecurityId.Should().Be(securityId);
        await eventStore.Received(1).AppendAsync(
            securityId,
            0,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_BackdatedWriteOutsideProfileEffectiveWindow_RefusesTheWrite()
    {
        // Approved status alone is not enough: the pinned version's effective window must cover
        // the write's effective date, or a backdated write pins a version that was not yet in
        // force (and a current write could pin an expired one).
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
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        // The seeded profiles become effective 2026-05-29; this write is dated before that.
        var effectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await service.Invoking(s => s.CreateAsync(new CreateSecurityRequest(
                securityId,
                "CustomAsset",
                JsonSerializer.SerializeToElement(new { displayName = "Backdated Fund Interest", currency = "USD" }),
                JsonSerializer.SerializeToElement(new
                {
                    customProfileId = "private-fund-interest",
                    profileVersion = 1,
                    profileFields = new
                    {
                        gpSponsor = "Meridian Growth Partners",
                        strategy = "Buyout",
                        vintage = 2024,
                        commitment = 5_000_000m,
                        fundedAmount = 0m,
                        unfundedAmount = 5_000_000m,
                        navDate = "2026-06-30"
                    },
                    profileApproval = new
                    {
                        approvedBy = "Meridian",
                        approvedAtUtc = "2026-05-29T00:00:00Z",
                        approvalReference = "MERIDIAN-SEED-APPROVAL"
                    }
                }),
                new[]
                {
                    new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "PFI-BACK", true, effectiveFrom, null, null)
                },
                effectiveFrom,
                "test",
                "codex",
                null,
                "create")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SM_CUSTOM_PROFILE_VERSION_NOT_EFFECTIVE*");

        await eventStore.DidNotReceive().AppendAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ApprovalMetadataContradictingCatalog_RefusesTheWrite()
    {
        // profileApproval persists as the record's immutable audit trail: caller-supplied values
        // contradicting the governed catalog's approval facts would corrupt that trail from the
        // first write.
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
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        await service.Invoking(s => s.CreateAsync(new CreateSecurityRequest(
                securityId,
                "CustomAsset",
                JsonSerializer.SerializeToElement(new { displayName = "Forged Approval Fund", currency = "USD" }),
                JsonSerializer.SerializeToElement(new
                {
                    customProfileId = "private-fund-interest",
                    profileVersion = 1,
                    profileFields = new
                    {
                        gpSponsor = "Meridian Growth Partners",
                        strategy = "Buyout",
                        vintage = 2024,
                        commitment = 5_000_000m,
                        fundedAmount = 0m,
                        unfundedAmount = 5_000_000m,
                        navDate = "2026-06-30"
                    },
                    profileApproval = new
                    {
                        approvedBy = "rogue-actor",
                        approvedAtUtc = "2026-05-29T00:00:00Z",
                        approvalReference = "FORGED-REFERENCE"
                    }
                }),
                new[]
                {
                    new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "PFI-FORGE", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
                },
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "create")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SM_CUSTOM_PROFILE_APPROVAL_METADATA_MISMATCH*");

        // A forged approval REFERENCE alone — approver and timestamp copied from the catalog —
        // must also refuse: the reference is part of the immutable approval evidence and the
        // catalog now retains its own to compare.
        await service.Invoking(s => s.CreateAsync(new CreateSecurityRequest(
                Guid.NewGuid(),
                "CustomAsset",
                JsonSerializer.SerializeToElement(new { displayName = "Forged Reference Fund", currency = "USD" }),
                JsonSerializer.SerializeToElement(new
                {
                    customProfileId = "private-fund-interest",
                    profileVersion = 1,
                    profileFields = new
                    {
                        gpSponsor = "Meridian Growth Partners",
                        strategy = "Buyout",
                        vintage = 2024,
                        commitment = 5_000_000m,
                        fundedAmount = 0m,
                        unfundedAmount = 5_000_000m,
                        navDate = "2026-06-30"
                    },
                    profileApproval = new
                    {
                        approvedBy = "Meridian",
                        approvedAtUtc = "2026-05-29T00:00:00Z",
                        approvalReference = "FORGED-REFERENCE"
                    }
                }),
                new[]
                {
                    new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "PFI-FORGE-REF", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
                },
                DateTimeOffset.UtcNow,
                "test",
                "codex",
                null,
                "create")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SM_CUSTOM_PROFILE_APPROVAL_METADATA_MISMATCH*");
    }

    [Fact]
    public async Task AmendTermsAsync_EnvelopeRepinnedToReclassifyingProfile_ResolvesTheSubmittedClass()
    {
        // The SUBMITTED envelope decides an amendment's resolved class: repinning an unmapped
        // CustomAsset (co-invest-spv) to private-fund-interest must resolve to PrivateFundInterest
        // exactly as the identical create would — persisting the new envelope while silently
        // keeping CustomAsset would skip the resolved class's validators and routing.
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

        var previous = CreateProjection(securityId, "CustomAsset", SecurityStatusDto.Active, "SPV becoming fund interest", 2) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                customProfileId = "co-invest-spv",
                profileVersion = 1,
                profileFields = new
                {
                    vehicle = "Meridian Co-Invest I",
                    underlyingCompanyOrSecurity = "Acme Holdings",
                    sponsor = "Meridian Growth Partners",
                    commitment = 1_000_000m,
                    economics = "1/10 over 8",
                    reportingCadence = "Quarterly"
                },
                profileApproval = new
                {
                    approvedBy = "Meridian",
                    approvedAtUtc = "2026-05-29T00:00:00Z",
                    approvalReference = "MERIDIAN-SEED-APPROVAL"
                }
            }),
            Provenance = JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "provA",
                asOf = DateTimeOffset.UtcNow.AddDays(-1),
                updatedBy = "feed"
            }),
            Identifiers = new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "SPV-REPIN", true, DateTimeOffset.UtcNow.AddDays(-30), null, null)
            }
        };
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(previous);

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        var detail = await service.AmendTermsAsync(new AmendSecurityTermsRequest(
            securityId,
            2,
            CommonTerms: null,
            AssetSpecificTermsPatch: JsonSerializer.SerializeToElement(new
            {
                customProfileId = "private-fund-interest",
                profileVersion = 1,
                profileFields = new
                {
                    gpSponsor = "Meridian Growth Partners",
                    strategy = "Buyout",
                    vintage = 2024,
                    commitment = 5_000_000m,
                    fundedAmount = 0m,
                    unfundedAmount = 5_000_000m,
                    navDate = "2026-06-30"
                },
                profileApproval = new
                {
                    approvedBy = "Meridian",
                    approvedAtUtc = "2026-05-29T00:00:00Z",
                    approvalReference = "MERIDIAN-SEED-APPROVAL"
                }
            }),
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "provA",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "repin to fund interest"));

        detail.AssetClass.Should().Be("PrivateFundInterest",
            "the submitted envelope resolves the class exactly as the identical create would");
        await eventStore.Received(1).AppendAsync(
            securityId,
            2,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AmendTermsAsync_FirstClassRecordRepinnedToDifferentProfile_ParsesThePatchUnderTheSubmittedClass()
    {
        // The submitted envelope's profile decides which kind parses the patch: repinning a
        // PrivateFundInterest record to structured-credit-io-po must parse the new envelope as
        // StructuredCredit — parsing it through the OLD class would demand gpSponsor and the other
        // fund fields from a structured-credit envelope and refuse a legitimate repin.
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

        var previous = CreateProjection(securityId, "PrivateFundInterest", SecurityStatusDto.Active, "Fund becoming strip", 2) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                customProfileId = "private-fund-interest",
                profileVersion = 1,
                profileFields = new
                {
                    gpSponsor = "Meridian Growth Partners",
                    strategy = "Buyout",
                    vintage = 2024,
                    commitment = 5_000_000m,
                    fundedAmount = 0m,
                    unfundedAmount = 5_000_000m,
                    navDate = "2026-06-30"
                },
                profileApproval = new
                {
                    approvedBy = "Meridian",
                    approvedAtUtc = "2026-05-29T00:00:00Z",
                    approvalReference = "MERIDIAN-SEED-APPROVAL"
                }
            }),
            Provenance = JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "provA",
                asOf = DateTimeOffset.UtcNow.AddDays(-1),
                updatedBy = "feed"
            }),
            Identifiers = new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "88160R1014", true, DateTimeOffset.UtcNow.AddDays(-30), null, null),
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "PFI-TO-SC", false, DateTimeOffset.UtcNow.AddDays(-30), null, null)
            }
        };
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(previous);

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        var detail = await service.AmendTermsAsync(new AmendSecurityTermsRequest(
            securityId,
            2,
            CommonTerms: null,
            AssetSpecificTermsPatch: JsonSerializer.SerializeToElement(new
            {
                customProfileId = "structured-credit-io-po",
                profileVersion = 1,
                profileFields = new
                {
                    tranche = "A-1",
                    poolId = "POOL-1",
                    currentFactor = 0.8m,
                    originalFace = 1_000_000m,
                    couponOrIndex = "SOFR+250",
                    factorSchedule = "trustee-report",
                    collateralType = "CLO"
                },
                profileApproval = new
                {
                    approvedBy = "Meridian",
                    approvedAtUtc = "2026-05-29T00:00:00Z",
                    approvalReference = "MERIDIAN-SEED-APPROVAL"
                }
            }),
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "provA",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "repin to structured credit"));

        detail.AssetClass.Should().Be("StructuredCredit",
            "the submitted envelope's profile decides the class and the kind that parses the patch");
    }

    [Fact]
    public async Task AmendTermsAsync_FirstClassRecordRepinnedToUnmappedProfile_ReturnsToCustomAsset()
    {
        // An unmapped registered profile resolves to CustomAsset: repinning a PrivateFundInterest
        // record to co-invest-spv must return it to CustomAsset instead of keeping the old class
        // via the stored projection's resolution.
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

        var previous = CreateProjection(securityId, "PrivateFundInterest", SecurityStatusDto.Active, "Fund becoming SPV", 2) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                customProfileId = "private-fund-interest",
                profileVersion = 1,
                profileFields = new
                {
                    gpSponsor = "Meridian Growth Partners",
                    strategy = "Buyout",
                    vintage = 2024,
                    commitment = 5_000_000m,
                    fundedAmount = 0m,
                    unfundedAmount = 5_000_000m,
                    navDate = "2026-06-30"
                },
                profileApproval = new
                {
                    approvedBy = "Meridian",
                    approvedAtUtc = "2026-05-29T00:00:00Z",
                    approvalReference = "MERIDIAN-SEED-APPROVAL"
                }
            }),
            Provenance = JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "provA",
                asOf = DateTimeOffset.UtcNow.AddDays(-1),
                updatedBy = "feed"
            }),
            Identifiers = new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "PFI-TO-SPV", true, DateTimeOffset.UtcNow.AddDays(-30), null, null)
            }
        };
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(previous);

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance,
            assetProfileCatalog: Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        var detail = await service.AmendTermsAsync(new AmendSecurityTermsRequest(
            securityId,
            2,
            CommonTerms: null,
            AssetSpecificTermsPatch: JsonSerializer.SerializeToElement(new
            {
                customProfileId = "co-invest-spv",
                profileVersion = 1,
                profileFields = new
                {
                    vehicle = "Meridian Co-Invest II",
                    underlyingCompanyOrSecurity = "Acme Holdings",
                    sponsor = "Meridian Growth Partners",
                    commitment = 1_000_000m,
                    economics = "1/10 over 8",
                    reportingCadence = "Quarterly"
                },
                profileApproval = new
                {
                    approvedBy = "Meridian",
                    approvedAtUtc = "2026-05-29T00:00:00Z",
                    approvalReference = "MERIDIAN-SEED-APPROVAL"
                }
            }),
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "provA",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "repin to co-invest SPV"));

        detail.AssetClass.Should().Be("CustomAsset",
            "an unmapped registered profile returns the record to CustomAsset");
    }

    private static (Guid SecurityId,
        ISecurityMasterEventStore EventStore,
        SecurityMasterService Service,
        ISecurityMasterConflictService ConflictService,
        ISecurityFieldProvenanceStore FieldProvenance) BuildAmendHarness(
        bool conflictRecordingFails,
        ISecurityMasterSnapshotStore? snapshotStore = null,
        int snapshotIntervalVersions = 50)
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        snapshotStore ??= Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        var fieldProvenance = Substitute.For<ISecurityFieldProvenanceStore>();
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = snapshotIntervalVersions,
            ResolveInactiveByDefault = true
        };
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);

        // The current golden copy: provider A's bond with a 4.25 coupon.
        var previous = CreateProjection(securityId, "Bond", SecurityStatusDto.Active, "Cross-source bond", 2) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                maturity = "2031-06-15",
                couponRate = 4.25m,
                par = 1000m
            }),
            Provenance = JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "provA",
                asOf = DateTimeOffset.UtcNow.AddDays(-1),
                updatedBy = "feed"
            })
        };
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(previous);

        if (conflictRecordingFails)
        {
            conflictService
                .RecordFieldConflictsAsync(
                    Arg.Any<SecurityProjectionRecord>(),
                    Arg.Any<SecurityProjectionRecord>(),
                    Arg.Any<CancellationToken>())
                .Returns<Task>(static _ => throw new InvalidOperationException("conflict store down"));
        }

        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            options,
            NullLogger<SecurityMasterService>.Instance,
            conflictService,
            fieldProvenance: fieldProvenance);
        return (securityId, eventStore, service, conflictService, fieldProvenance);
    }

    private static AmendSecurityTermsRequest BuildConflictingAmend(Guid securityId)
        => new(
            securityId,
            2,
            CommonTerms: null,
            AssetSpecificTermsPatch: JsonSerializer.SerializeToElement(new
            {
                maturity = "2031-06-15",
                couponRate = 4.50m,
                par = 1000m
            }),
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "provB",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "amend");

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
