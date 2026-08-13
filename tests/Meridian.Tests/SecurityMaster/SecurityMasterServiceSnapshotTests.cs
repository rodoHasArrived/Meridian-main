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
        ISecurityFieldProvenanceStore FieldProvenance) BuildAmendHarness(bool conflictRecordingFails)
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var conflictService = Substitute.For<ISecurityMasterConflictService>();
        var fieldProvenance = Substitute.For<ISecurityFieldProvenanceStore>();
        var options = new SecurityMasterOptions
        {
            SnapshotIntervalVersions = 50,
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
