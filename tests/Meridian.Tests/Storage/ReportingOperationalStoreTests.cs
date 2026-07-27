using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Meridian.Tests.Storage.Reporting;

[Trait("Category", "Integration")]
public sealed class ReportingOperationalStoreTests : IClassFixture<ReportingArtifactDatabaseFixture>
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly ReportingArtifactDatabaseFixture _database;

    public ReportingOperationalStoreTests(ReportingArtifactDatabaseFixture database)
    {
        _database = database;
    }

    [ReportingDatabaseFact]
    public async Task RunStore_IsTenantScopedAndExactRetriesAreIdempotent()
    {
        var store = new PostgresReportingRunStore(_database.Options);
        var runId = $"shared-run-{Guid.NewGuid():N}";
        var tenantA = $"tenant-a-{Guid.NewGuid():N}";
        var tenantB = $"tenant-b-{Guid.NewGuid():N}";
        var manifestA = BuildManifest(runId, tenantA, "company-a");
        var auditA = new[]
        {
            new ReportingRunAuditEntry(runId, FixedNow, "Created", "operator-a", "first save")
        };

        await store.SaveAsync(manifestA, auditA);
        var firstStoredAt = await ReadRunStoredAtAsync(tenantA, runId);
        await Task.Delay(20);
        await store.SaveAsync(manifestA, auditA);
        var retryStoredAt = await ReadRunStoredAtAsync(tenantA, runId);
        await store.SaveAsync(
            BuildManifest(runId, tenantB, "company-b"),
            [new ReportingRunAuditEntry(runId, FixedNow, "Created", "operator-b", "other tenant")]);

        retryStoredAt.Should().Be(firstStoredAt, "an exact retry must not rewrite retained state");
        (await CountRunRowsAsync(runId)).Should().Be(2);
        store.GetManifest(tenantA, runId)!.OperationalScope!.TenantId.Should().Be(tenantA);
        store.GetManifest(tenantB, runId)!.OperationalScope!.TenantId.Should().Be(tenantB);
        store.GetManifest(runId).Should().BeNull("an unscoped lookup must fail closed when the id is ambiguous");
        store.GetAudit(tenantA, runId).Should().ContainSingle(entry => entry.Actor == "operator-a");
    }

    [ReportingDatabaseFact]
    public async Task RunStore_CompanyListingScopesBeforeTheTenantLimit()
    {
        var store = new PostgresReportingRunStore(_database.Options);
        var targetTenant = $"z-target-tenant-{Guid.NewGuid():N}";
        var targetRunId = $"target-run-{Guid.NewGuid():N}";
        var foreignRunPrefix = $"foreign-run-{Guid.NewGuid():N}-";

        await store.SaveAsync(
            BuildManifest(targetRunId, targetTenant, "company-target"),
            []);

        try
        {
            await InsertForeignRunRowsAsync(
                targetTenant,
                "company-other",
                foreignRunPrefix,
                rowCount: 201);

            var retained = store.ListRuns(
                targetTenant,
                "company-target",
                10);

            retained.Should().ContainSingle();
            retained[0].Manifest.RunId.Should().Be(targetRunId);
            retained[0].Manifest.OperationalScope!.TenantId.Should().Be(targetTenant);
        }
        finally
        {
            await DeleteRunTenantAsync(targetTenant);
        }
    }

    [ReportingDatabaseFact]
    public async Task RunStore_StaleReplicaCannotOverwriteReleasedStateOrAudit()
    {
        var firstStore = new PostgresReportingRunStore(_database.Options);
        var staleStore = new PostgresReportingRunStore(_database.Options);
        var tenantId = $"tenant-cas-{Guid.NewGuid():N}";
        var runId = $"run-cas-{Guid.NewGuid():N}";
        var manifest = BuildManifest(runId, tenantId, "company-cas");
        var createdAudit = new[]
        {
            new ReportingRunAuditEntry(
                runId,
                FixedNow,
                "RunGenerated",
                "creator",
                "draft")
        };

        try
        {
            await firstStore.SaveAsync(manifest, createdAudit);
            var firstRevision = firstStore.GetRevision(tenantId, runId)!;
            var staleRevision = staleStore.GetRevision(tenantId, runId)!;
            firstRevision.Should().Be(
                ReportingRunStoreRevision.Compute(manifest, createdAudit),
                "revision hashing must be stable across PostgreSQL JSONB key reordering");

            var released = manifest with { Status = ReportingRunStatus.Released };
            var releasedAudit = createdAudit
                .Append(new ReportingRunAuditEntry(
                    runId,
                    FixedNow.AddMinutes(1),
                    "ApprovalTransition",
                    "operations",
                    "Approved->Released"))
                .ToArray();
            await firstStore.SaveAsync(
                released,
                releasedAudit,
                firstRevision);

            var staleWrite = () => staleStore.SaveAsync(
                manifest with { Status = ReportingRunStatus.InReview },
                createdAudit,
                staleRevision);
            await staleWrite.Should().ThrowAsync<ReportingRunConcurrencyException>();

            firstStore.GetManifest(tenantId, runId)!.Status
                .Should()
                .Be(ReportingRunStatus.Released);
            firstStore.GetAudit(tenantId, runId)
                .Should()
                .Equal(releasedAudit);
        }
        finally
        {
            await DeleteRunAsync(tenantId, runId);
        }
    }

    [ReportingDatabaseFact]
    public async Task RunStore_FailsClosedWhenRetainedManifestJsonIsModified()
    {
        var store = new PostgresReportingRunStore(_database.Options);
        var tenantId = $"tenant-corrupt-{Guid.NewGuid():N}";
        var runId = $"run-corrupt-{Guid.NewGuid():N}";
        await store.SaveAsync(BuildManifest(runId, tenantId, "company-corrupt"), []);

        try
        {
            await ExecuteAsync(
                $$"""
                update {{QualifiedRunTable}}
                set manifest_payload = jsonb_set(
                    manifest_payload,
                    '{templateId}',
                    to_jsonb('tampered-template'::text))
                where tenant_id = @tenant_id
                  and run_id_key = @identity_key;
                """,
                tenantId,
                runId.ToLowerInvariant());

            var read = () => store.GetManifest(tenantId, runId);

            read.Should().Throw<ReportingOperationalStateCorruptionException>()
                .WithMessage("*integrity digest*");
        }
        finally
        {
            await DeleteRunAsync(tenantId, runId);
        }
    }

    [ReportingDatabaseFact]
    public async Task RunStore_CreateClaim_IsExclusiveAndFencesExpiredOwners()
    {
        var firstStore = new PostgresReportingRunStore(_database.Options);
        var secondStore = new PostgresReportingRunStore(_database.Options);
        var tenantId = $"tenant-run-claim-{Guid.NewGuid():N}";
        var runId = $"run-claim-{Guid.NewGuid():N}";
        var manifest = BuildManifest(runId, tenantId, "company-run-claim");
        var audit = new[]
        {
            new ReportingRunAuditEntry(
                runId,
                FixedNow,
                "RunGenerated",
                "claim-owner-b",
                "durable create")
        };

        try
        {
            var firstClaim = await firstStore.TryClaimCreateAsync(
                tenantId,
                runId,
                "claim-owner-a",
                FixedNow,
                TimeSpan.FromMinutes(5));
            var blockedClaim = await secondStore.TryClaimCreateAsync(
                tenantId,
                runId,
                "claim-owner-b",
                FixedNow,
                TimeSpan.FromMinutes(5));

            firstClaim.Status.Should().Be(ReportingRunCreateClaimStatus.Acquired);
            firstClaim.LeaseVersion.Should().BePositive();
            blockedClaim.Status.Should().Be(
                ReportingRunCreateClaimStatus.LeasedByAnotherOwner);
            (await firstStore.RenewCreateClaimAsync(
                    tenantId,
                    runId,
                    "claim-owner-a",
                    firstClaim.LeaseVersion,
                    TimeSpan.FromMinutes(5)))
                .Should().BeTrue();

            await ExpireRunCreateClaimAsync(tenantId, runId);
            var takeoverClaim = await secondStore.TryClaimCreateAsync(
                tenantId,
                runId,
                "claim-owner-b",
                FixedNow.AddMinutes(6),
                TimeSpan.FromMinutes(5));

            takeoverClaim.Status.Should().Be(ReportingRunCreateClaimStatus.Acquired);
            takeoverClaim.LeaseVersion.Should().BeGreaterThan(firstClaim.LeaseVersion);
            (await firstStore.RenewCreateClaimAsync(
                    tenantId,
                    runId,
                    "claim-owner-a",
                    firstClaim.LeaseVersion,
                    TimeSpan.FromMinutes(5)))
                .Should().BeFalse();
            (await secondStore.RenewCreateClaimAsync(
                    tenantId,
                    runId,
                    "claim-owner-b",
                    takeoverClaim.LeaseVersion,
                    TimeSpan.FromMinutes(5)))
                .Should().BeTrue();

            var staleCompletion = () => firstStore.SaveClaimedCreateAsync(
                manifest,
                audit,
                "claim-owner-a",
                firstClaim.LeaseVersion);
            await staleCompletion.Should().ThrowAsync<ReportingRunCreateClaimException>();

            await firstStore.ReleaseCreateClaimAsync(
                tenantId,
                runId,
                "claim-owner-a",
                firstClaim.LeaseVersion);
            await secondStore.SaveClaimedCreateAsync(
                manifest,
                audit,
                "claim-owner-b",
                takeoverClaim.LeaseVersion);

            secondStore.GetManifest(tenantId, runId).Should().BeEquivalentTo(manifest);
            (await CountRunCreateClaimsAsync(tenantId, runId)).Should().Be(0);
        }
        finally
        {
            await DeleteRunAsync(tenantId, runId);
            await DeleteRunCreateClaimAsync(tenantId, runId);
        }
    }

    [ReportingDatabaseFact]
    public async Task ScheduleStore_RoundTripsScopedDuplicateIdsAndExactSnapshotRetries()
    {
        var store = new PostgresReportingScheduleStore(_database.Options);
        var scheduleId = $"monthly-close-{Guid.NewGuid():N}";
        var tenantA = $"tenant-a-{Guid.NewGuid():N}";
        var tenantB = $"tenant-b-{Guid.NewGuid():N}";
        var schedules = new[]
        {
            BuildSchedule(scheduleId, tenantA, "company-shared", "operator-a"),
            BuildSchedule(scheduleId, tenantB, "company-shared", "operator-b")
        };

        store.Save(schedules);
        var firstStoredAt = await ReadScheduleStoredAtAsync(
            tenantA,
            "company-shared",
            scheduleId);
        await Task.Delay(20);
        store.Save(schedules);
        var retryStoredAt = await ReadScheduleStoredAtAsync(
            tenantA,
            "company-shared",
            scheduleId);
        var retained = store.Load().Where(schedule => schedule.ScheduleId == scheduleId).ToArray();

        retryStoredAt.Should().Be(firstStoredAt, "an exact snapshot retry must not rewrite rows");
        retained.Should().HaveCount(2);
        retained.Select(schedule => schedule.TenantId).Should().BeEquivalentTo(tenantA, tenantB);
        (await CountScheduleRowsAsync(scheduleId)).Should().Be(2);
    }

    [ReportingDatabaseFact]
    public async Task ScheduleStore_LegacyLoadSaveSnapshotRemovesAbsentRows()
    {
        var store = new PostgresReportingScheduleStore(_database.Options);
        var inspector = new PostgresReportingScheduleStore(_database.Options);
        var tenantId = $"tenant-legacy-save-{Guid.NewGuid():N}";
        var companyId = $"company-legacy-save-{Guid.NewGuid():N}";
        var firstId = $"legacy-first-{Guid.NewGuid():N}";
        var secondId = $"legacy-second-{Guid.NewGuid():N}";
        var first = BuildSchedule(
            firstId,
            tenantId,
            companyId,
            "operator-a");
        var second = BuildSchedule(
            secondId,
            tenantId,
            companyId,
            "operator-a");

        try
        {
            store.Upsert(first);
            store.Upsert(second);

            var loaded = store.Load();
            store.Save(loaded
                .Where(schedule =>
                    schedule.TenantId != tenantId
                    || schedule.CompanyId != companyId
                    || schedule.ScheduleId != secondId)
                .ToArray());

            inspector.Load()
                .Where(schedule =>
                    schedule.TenantId == tenantId
                    && schedule.CompanyId == companyId)
                .Should()
                .ContainSingle(schedule => schedule.ScheduleId == firstId);
        }
        finally
        {
            await DeleteScheduleAsync(tenantId, companyId, firstId);
            await DeleteScheduleAsync(tenantId, companyId, secondId);
        }
    }

    [ReportingDatabaseFact]
    public async Task ScheduleStore_LegacySaveEmptyClearsEstablishedScope()
    {
        var store = new PostgresReportingScheduleStore(_database.Options);
        var inspector = new PostgresReportingScheduleStore(_database.Options);
        var tenantId = $"tenant-legacy-clear-{Guid.NewGuid():N}";
        var companyId = $"company-legacy-clear-{Guid.NewGuid():N}";
        var firstId = $"legacy-clear-first-{Guid.NewGuid():N}";
        var secondId = $"legacy-clear-second-{Guid.NewGuid():N}";
        var first = BuildSchedule(
            firstId,
            tenantId,
            companyId,
            "operator-a");
        var second = BuildSchedule(
            secondId,
            tenantId,
            companyId,
            "operator-a");

        try
        {
            store.Upsert(first);
            store.Upsert(second);
            store.Save([first, second]);

            store.Save([]);

            inspector.Load()
                .Should()
                .NotContain(schedule =>
                    schedule.TenantId == tenantId
                    && schedule.CompanyId == companyId);
        }
        finally
        {
            await DeleteScheduleAsync(tenantId, companyId, firstId);
            await DeleteScheduleAsync(tenantId, companyId, secondId);
        }
    }

    [ReportingDatabaseFact]
    public async Task ScheduleStore_ScopedUpsertsCannotDeleteConcurrentRows()
    {
        var firstStore = new PostgresReportingScheduleStore(_database.Options);
        var secondStore = new PostgresReportingScheduleStore(_database.Options);
        var scheduleId = $"concurrent-close-{Guid.NewGuid():N}";
        var tenantA = $"tenant-a-{Guid.NewGuid():N}";
        var tenantB = $"tenant-b-{Guid.NewGuid():N}";
        const string companyId = "company-shared";
        var scheduleA = BuildSchedule(scheduleId, tenantA, companyId, "operator-a");
        var scheduleB = BuildSchedule(scheduleId, tenantB, companyId, "operator-b");

        try
        {
            firstStore.Upsert(scheduleA);
            var retainedA = firstStore.Load()
                .Single(schedule => schedule.TenantId == tenantA
                    && schedule.ScheduleId == scheduleId);
            secondStore.Upsert(scheduleB);

            firstStore.Upsert(
                retainedA with
                {
                    State = ReportingScheduleStateDto.Paused,
                    UpdatedAtUtc = FixedNow.AddMinutes(1)
                },
                retainedA.UpdatedAtUtc);

            var retained = firstStore.Load()
                .Where(schedule => schedule.ScheduleId == scheduleId)
                .ToArray();
            retained.Should().HaveCount(2);
            retained.Should().ContainSingle(schedule =>
                schedule.TenantId == tenantA
                && schedule.State == ReportingScheduleStateDto.Paused);
            retained.Should().ContainSingle(schedule =>
                schedule.TenantId == tenantB
                && schedule.State == ReportingScheduleStateDto.Draft);

            firstStore.Delete(
                    tenantA,
                    companyId,
                    scheduleId,
                    FixedNow.AddMinutes(1))
                .Should()
                .BeTrue();
            firstStore.Delete(
                    tenantA,
                    companyId,
                    scheduleId,
                    FixedNow.AddMinutes(1))
                .Should()
                .BeFalse();
            firstStore.Load()
                .Where(schedule => schedule.ScheduleId == scheduleId)
                .Should()
                .ContainSingle(schedule => schedule.TenantId == tenantB);
        }
        finally
        {
            await DeleteScheduleAsync(tenantA, companyId, scheduleId);
            await DeleteScheduleAsync(tenantB, companyId, scheduleId);
        }
    }

    [ReportingDatabaseFact]
    public async Task ScheduleStore_SameRowUsesUpdatedAtCompareAndSwap()
    {
        var firstStore = new PostgresReportingScheduleStore(_database.Options);
        var staleStore = new PostgresReportingScheduleStore(_database.Options);
        var scheduleId = $"schedule-cas-{Guid.NewGuid():N}";
        var tenantId = $"tenant-cas-{Guid.NewGuid():N}";
        const string companyId = "company-cas";
        var original = BuildSchedule(
            scheduleId,
            tenantId,
            companyId,
            "operator-a");

        try
        {
            firstStore.Upsert(original);
            var firstRevision = firstStore.Load()
                .Single(schedule =>
                    schedule.TenantId == tenantId
                    && schedule.ScheduleId == scheduleId);
            var staleRevision = staleStore.Load()
                .Single(schedule =>
                    schedule.TenantId == tenantId
                    && schedule.ScheduleId == scheduleId);
            var retained = firstRevision with
            {
                State = ReportingScheduleStateDto.Paused,
                UpdatedAtUtc = FixedNow.AddMinutes(1)
            };
            firstStore.Upsert(retained, firstRevision.UpdatedAtUtc);

            var staleWrite = () => staleStore.Upsert(
                staleRevision with
                {
                    State = ReportingScheduleStateDto.Disabled,
                    UpdatedAtUtc = FixedNow.AddMinutes(2)
                },
                staleRevision.UpdatedAtUtc);
            staleWrite.Should().Throw<ReportingScheduleConcurrencyException>();
            var staleDelete = () => staleStore.Delete(
                tenantId,
                companyId,
                scheduleId,
                staleRevision.UpdatedAtUtc);
            staleDelete.Should().Throw<ReportingScheduleConcurrencyException>();

            firstStore.Load()
                .Should()
                .ContainSingle(schedule =>
                    schedule.TenantId == tenantId
                    && schedule.ScheduleId == scheduleId
                    && schedule.State == ReportingScheduleStateDto.Paused);
            firstStore.Delete(
                    tenantId,
                    companyId,
                    scheduleId,
                    retained.UpdatedAtUtc)
                .Should()
                .BeTrue();
        }
        finally
        {
            await DeleteScheduleAsync(tenantId, companyId, scheduleId);
        }
    }

    [ReportingDatabaseFact]
    public async Task ScheduleStore_ParallelSameRowWriters_ReturnOneExplicitConflict()
    {
        var firstStore = new PostgresReportingScheduleStore(_database.Options);
        var secondStore = new PostgresReportingScheduleStore(_database.Options);
        var scheduleId = $"schedule-parallel-cas-{Guid.NewGuid():N}";
        var tenantId = $"tenant-parallel-cas-{Guid.NewGuid():N}";
        const string companyId = "company-parallel-cas";
        var original = BuildSchedule(
            scheduleId,
            tenantId,
            companyId,
            "operator-a");

        try
        {
            firstStore.Upsert(original);
            using var start = new Barrier(participantCount: 2);
            var first = Task.Run(() =>
            {
                start.SignalAndWait();
                firstStore.Upsert(
                    original with
                    {
                        State = ReportingScheduleStateDto.Paused,
                        UpdatedAtUtc = FixedNow.AddMinutes(1)
                    },
                    original.UpdatedAtUtc);
            });
            var second = Task.Run(() =>
            {
                start.SignalAndWait();
                secondStore.Upsert(
                    original with
                    {
                        State = ReportingScheduleStateDto.Disabled,
                        UpdatedAtUtc = FixedNow.AddMinutes(1)
                    },
                    original.UpdatedAtUtc);
            });

            var outcomes = await Task.WhenAll(
                CaptureExceptionAsync(first),
                CaptureExceptionAsync(second));

            outcomes.Count(static exception => exception is null).Should().Be(1);
            outcomes.Should().ContainSingle(
                exception => exception is ReportingScheduleConcurrencyException);
            outcomes.Should().NotContain(
                exception => exception is PostgresException);
        }
        finally
        {
            await DeleteScheduleAsync(tenantId, companyId, scheduleId);
        }
    }

    [ReportingDatabaseFact]
    public async Task ScheduleStore_FailsClosedWhenRetainedScheduleJsonIsModified()
    {
        var store = new PostgresReportingScheduleStore(_database.Options);
        var tenantId = $"tenant-corrupt-{Guid.NewGuid():N}";
        var companyId = $"company-corrupt-{Guid.NewGuid():N}";
        var scheduleId = $"schedule-corrupt-{Guid.NewGuid():N}";
        store.Save([BuildSchedule(scheduleId, tenantId, companyId, "operator-corrupt")]);

        try
        {
            await ExecuteAsync(
                $$"""
                update {{QualifiedScheduleTable}}
                set schedule_payload = jsonb_set(
                    schedule_payload,
                    '{description}',
                    to_jsonb('tampered-description'::text))
                where tenant_id = @tenant_id
                  and company_id = @company_id
                  and schedule_id_key = @identity_key;
                """,
                tenantId,
                scheduleId.ToLowerInvariant(),
                companyId);

            var read = () => store.Load();

            read.Should().Throw<ReportingOperationalStateCorruptionException>()
                .WithMessage("*integrity digest*");
        }
        finally
        {
            await DeleteScheduleAsync(tenantId, companyId, scheduleId);
        }
    }

    [ReportingDatabaseFact]
    public async Task ScheduleStore_ExecutionLease_IsExclusiveRenewableAndFenced()
    {
        var firstStore = new PostgresReportingScheduleStore(_database.Options);
        var secondStore = new PostgresReportingScheduleStore(_database.Options);
        var tenantId = $"tenant-schedule-lease-{Guid.NewGuid():N}";
        var companyId = $"company-schedule-lease-{Guid.NewGuid():N}";
        var scheduleId = $"schedule-lease-{Guid.NewGuid():N}";
        var schedule = BuildSchedule(
            scheduleId,
            tenantId,
            companyId,
            "lease-operator");

        try
        {
            firstStore.Upsert(schedule);
            var firstLease = firstStore.TryClaimExecution(
                schedule,
                "lease-owner-a",
                FixedNow,
                TimeSpan.FromMinutes(5));
            var blockedLease = secondStore.TryClaimExecution(
                schedule,
                "lease-owner-b",
                FixedNow,
                TimeSpan.FromMinutes(5));

            firstLease.Should().NotBeNull();
            blockedLease.Should().BeNull();

            await ExpireScheduleExecutionLeaseAsync(
                tenantId,
                companyId,
                scheduleId);
            var takeoverLease = secondStore.TryClaimExecution(
                schedule,
                "lease-owner-b",
                FixedNow.AddMinutes(6),
                TimeSpan.FromMinutes(5));

            takeoverLease.Should().NotBeNull();
            var retainedFirstLease = firstLease!;
            var retainedTakeoverLease = takeoverLease!;
            retainedTakeoverLease.LeaseVersion.Should()
                .BeGreaterThan(retainedFirstLease.LeaseVersion);
            var staleCompletion = () => firstStore.UpsertClaimedExecution(
                schedule with
                {
                    RunCount = 1,
                    UpdatedAtUtc = FixedNow.AddMinutes(1)
                },
                schedule.UpdatedAtUtc,
                retainedFirstLease);
            staleCompletion.Should().Throw<ReportingScheduleExecutionLeaseException>();
            secondStore.Load().Should().ContainSingle(candidate =>
                candidate.TenantId == tenantId
                && candidate.CompanyId == companyId
                && candidate.ScheduleId == scheduleId
                && candidate.RunCount == 0);
            firstStore.RenewExecutionLease(
                    schedule,
                    retainedFirstLease,
                    FixedNow.AddMinutes(6),
                    TimeSpan.FromMinutes(5))
                .Should()
                .BeNull();

            firstStore.ReleaseExecutionLease(
                tenantId,
                companyId,
                scheduleId,
                retainedFirstLease);
            firstStore.TryClaimExecution(
                    schedule,
                    "lease-owner-c",
                    FixedNow.AddMinutes(6),
                    TimeSpan.FromMinutes(5))
                .Should()
                .BeNull("a stale release must not clear the takeover owner's lease");

            var renewed = secondStore.RenewExecutionLease(
                schedule,
                retainedTakeoverLease,
                FixedNow.AddMinutes(6),
                TimeSpan.FromMinutes(5));
            renewed.Should().NotBeNull();
            var retainedRenewed = renewed!;
            retainedRenewed.LeaseVersion.Should().Be(retainedTakeoverLease.LeaseVersion);

            secondStore.ReleaseExecutionLease(
                tenantId,
                companyId,
                scheduleId,
                retainedRenewed);
            var reacquired = firstStore.TryClaimExecution(
                schedule,
                "lease-owner-c",
                FixedNow.AddMinutes(6),
                TimeSpan.FromMinutes(5));
            reacquired.Should().NotBeNull();
            reacquired!.LeaseVersion.Should()
                .BeGreaterThan(retainedTakeoverLease.LeaseVersion);
        }
        finally
        {
            await DeleteScheduleAsync(tenantId, companyId, scheduleId);
        }
    }

    [ReportingDatabaseFact]
    public async Task MigrationRunner_AppliesOperationalStateMigration()
    {
        (await _database.HasMigrationAsync("010_reporting_operational_state.sql")).Should().BeTrue();
    }

    [ReportingDatabaseFact]
    public async Task ReleaseConsistencyGate_SerializesTheSamePeriodAcrossStoreInstances()
    {
        var periodId = Guid.NewGuid().ToString("D");
        var firstGate = new PostgresReportingReleaseConsistencyGate(_database.Options);
        var secondGate = new PostgresReportingReleaseConsistencyGate(_database.Options);
        await using var firstLease = await firstGate.AcquireAsync(periodId);

        var blockedAcquire = secondGate.AcquireAsync(periodId).AsTask();
        await Task.Delay(100);
        blockedAcquire.IsCompleted.Should().BeFalse(
            "the first PostgreSQL session still owns the period-scoped advisory lock");

        await firstLease.DisposeAsync();
        await using var secondLease = await blockedAcquire.WaitAsync(TimeSpan.FromSeconds(5));
        secondLease.Should().NotBeNull();
    }

    private static ReportingOutputManifest BuildManifest(
        string runId,
        string tenantId,
        string companyId)
    {
        var scope = new ReportingOperationalScope(
            tenantId,
            $"organization-{tenantId}",
            companyId,
            $"fund-{tenantId}",
            "primary-book",
            "44444444-4444-4444-4444-444444444444");
        var templateParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["z-last"] = "second",
            ["a-first"] = "first"
        };
        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto(scope.FundId!),
            scope.PeriodId,
            new DateOnly(2026, 7, 25),
            new ReportingLedgerBookSelectionDto(LedgerBookCode: scope.BookId),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Draft,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: false,
            templateParameters);
        var parametersJson = ReportingCanonicalParameterSerializer.Serialize(parameters);
        var parametersHash = ReportingCanonicalParameterSerializer.ComputeHash(parameters);
        var sourceHash = new string('b', 64);
        var sourceId = $"source-{runId}";
        var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["account"] = "cash",
                ["amount"] = "100.00"
            });
        var snapshot = new ReportingCertifiedSnapshotScope(
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            scope.FundId,
            scope.BookId,
            scope.PeriodId,
            $"snapshot-{runId}",
            new string('0', 64),
            $"reconciliation-{runId}",
            FixedNow,
            sourceId,
            sourceHash,
            new string('c', 64),
            parametersJson,
            parametersHash);
        var manifest = new ReportingOutputManifest(
            runId,
            "investor-monthly-statement",
            new DateOnly(2026, 7, 25),
            ReportingRunStatus.Draft,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray<string>.Empty,
            1,
            ReportingRunTrigger.AdHoc,
            ResolvedTemplate: new VersionedReportTemplateIdDto("investor-monthly-statement", 1),
            ResolvedParameters: parameters,
            Readiness: new ReportingRunReadinessDto(
                $"readiness-{runId}",
                FixedNow.AddMinutes(-1),
                new VersionedReportTemplateIdDto("investor-monthly-statement", 1),
                parameters,
                ReportingRunReadinessStatusDto.Ready,
                CanGenerateDraft: true,
                CanGenerateFinal: false,
                Checks:
                [
                    new ReportingRunReadinessCheckDto(
                        "source",
                        "Source",
                        ReportingRunReadinessStatusDto.Ready,
                        "Durable source is ready.",
                        IssueCount: 0,
                        BlocksDraft: true,
                        BlocksFinal: true,
                        EvidenceReferences: ["source:ready"])
                ],
                BlockingReasons: [],
                EvidenceHash: new string('d', 64)),
            OperationalScope: scope,
            ImmutableAccessScope: new ReportingAccessScope(
                "company-reporting",
                "1",
                ReportingGovernanceAccessMode.CompanyWide,
                OwnerPrincipalId: null,
                AllowOwnerAccess: false,
                Principals: [],
                PolicyHash: new string('a', 64)),
            CertifiedSnapshot: snapshot,
            AuthoritativeSource: new ReportingAuthoritativeSourceCheckpoint(
                "durable-ledger",
                sourceId,
                scope.TenantId,
                scope.OrganizationId,
                scope.CompanyId,
                scope.FundId!,
                scope.BookId,
                scope.PeriodId,
                "Gaap",
                new DateOnly(2026, 7, 25),
                FixedNow.AddMinutes(-2),
                HighestGlobalSequence: 1,
                JournalEntryCount: 1,
                LedgerLineCount: rows.Length,
                CheckpointId: sourceId,
                CheckpointHash: sourceHash,
                CapturedAtUtc: FixedNow.AddMinutes(-2),
                EvidenceIds: [$"reporting-source-checkpoint:{sourceId}:{sourceHash}"]),
            CertifiedDatasetRows: rows);
        return manifest with
        {
            CertifiedSnapshot = snapshot with
            {
                SnapshotHash =
                    ReportingCertifiedManifestValidation.ComputeSnapshotHash(manifest)
            }
        };
    }

    private static ReportingScheduleRecordDto BuildSchedule(
        string scheduleId,
        string tenantId,
        string companyId,
        string requestedBy) =>
        new(
            ScheduleId: scheduleId,
            TemplateId: "investor-monthly-statement",
            CronExpression: "0 8 1 * *",
            NextAsOfDate: new DateOnly(2026, 7, 31),
            DueAtUtc: FixedNow.AddDays(1),
            MaxRetries: 3,
            RequestedBy: requestedBy,
            State: ReportingScheduleStateDto.Draft,
            CreatedAtUtc: FixedNow,
            UpdatedAtUtc: FixedNow,
            Description: "Monthly close package",
            TenantId: tenantId,
            CompanyId: companyId);

    private async Task<DateTimeOffset> ReadRunStoredAtAsync(string tenantId, string runId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select updated_at_utc
            from {QualifiedRunTable}
            where tenant_id = @tenant_id
              and run_id_key = @identity_key;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        command.Parameters.AddWithValue("identity_key", NpgsqlDbType.Text, runId.ToLowerInvariant());
        return new DateTimeOffset(
            DateTime.SpecifyKind(
                (DateTime)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException()),
                DateTimeKind.Utc));
    }

    private async Task<DateTimeOffset> ReadScheduleStoredAtAsync(
        string tenantId,
        string companyId,
        string scheduleId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select stored_at_utc
            from {QualifiedScheduleTable}
            where tenant_id = @tenant_id
              and company_id = @company_id
              and schedule_id_key = @identity_key;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, companyId);
        command.Parameters.AddWithValue(
            "identity_key",
            NpgsqlDbType.Text,
            scheduleId.ToLowerInvariant());
        return new DateTimeOffset(
            DateTime.SpecifyKind(
                (DateTime)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException()),
                DateTimeKind.Utc));
    }

    private async Task<long> CountRunRowsAsync(string runId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select count(*) from {QualifiedRunTable} where run_id_key = @identity_key;";
        command.Parameters.AddWithValue("identity_key", NpgsqlDbType.Text, runId.ToLowerInvariant());
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<long> CountScheduleRowsAsync(string scheduleId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select count(*) from {QualifiedScheduleTable} where schedule_id_key = @identity_key;";
        command.Parameters.AddWithValue(
            "identity_key",
            NpgsqlDbType.Text,
            scheduleId.ToLowerInvariant());
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<long> CountRunCreateClaimsAsync(
        string tenantId,
        string runId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select count(*)
            from {QualifiedRunClaimTable}
            where tenant_id = @tenant_id
              and run_id_key = @identity_key;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        command.Parameters.AddWithValue(
            "identity_key",
            NpgsqlDbType.Text,
            runId.ToLowerInvariant());
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private Task ExpireRunCreateClaimAsync(string tenantId, string runId) =>
        ExecuteAsync(
            $"""
            update {QualifiedRunClaimTable}
            set claimed_at_utc = current_timestamp - interval '2 seconds',
                lease_expires_at_utc = current_timestamp - interval '1 second'
            where tenant_id = @tenant_id
              and run_id_key = @identity_key;
            """,
            tenantId,
            runId.ToLowerInvariant());

    private Task DeleteRunCreateClaimAsync(string tenantId, string runId) =>
        ExecuteAsync(
            $"""
            delete from {QualifiedRunClaimTable}
            where tenant_id = @tenant_id
              and run_id_key = @identity_key;
            """,
            tenantId,
            runId.ToLowerInvariant());

    private Task ExpireScheduleExecutionLeaseAsync(
        string tenantId,
        string companyId,
        string scheduleId) =>
        ExecuteAsync(
            $"""
            update {QualifiedScheduleTable}
            set lease_expires_at_utc = current_timestamp - interval '1 second'
            where tenant_id = @tenant_id
              and company_id = @company_id
              and schedule_id_key = @identity_key;
            """,
            tenantId,
            scheduleId.ToLowerInvariant(),
            companyId);

    private async Task InsertForeignRunRowsAsync(
        string tenantId,
        string companyId,
        string runIdPrefix,
        int rowCount)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {QualifiedRunTable} (
                tenant_id,
                run_id,
                run_id_key,
                manifest_payload,
                audit_payload,
                updated_at_utc,
                certified_dataset_hash_sha256,
                manifest_hash_sha256,
                audit_hash_sha256,
                state_hash_sha256)
            select @tenant_id,
                   @run_id_prefix || ordinal::text,
                   lower(@run_id_prefix || ordinal::text),
                    jsonb_build_object(
                        'runId', @run_id_prefix || ordinal::text,
                        'operationalScope', jsonb_build_object(
                            'tenantId', @tenant_id,
                            'companyId', @company_id)),
                   '[]'::jsonb,
                   now() + interval '1 day',
                   repeat('0', 64),
                   repeat('0', 64),
                   repeat('0', 64),
                   repeat('0', 64)
            from generate_series(1, @row_count) as ordinal;
            """;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, companyId);
        command.Parameters.AddWithValue("run_id_prefix", NpgsqlDbType.Text, runIdPrefix);
        command.Parameters.AddWithValue("row_count", NpgsqlDbType.Integer, rowCount);
        await command.ExecuteNonQueryAsync();
    }

    private Task DeleteRunAsync(string tenantId, string runId) =>
        ExecuteAsync(
            $"""
            delete from {QualifiedRunTable}
            where tenant_id = @tenant_id
              and run_id_key = @identity_key;
            """,
            tenantId,
            runId.ToLowerInvariant());

    private async Task DeleteRunTenantAsync(string tenantId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"delete from {QualifiedRunTable} where tenant_id = @tenant_id;";
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        await command.ExecuteNonQueryAsync();
    }

    private Task DeleteScheduleAsync(string tenantId, string companyId, string scheduleId) =>
        ExecuteAsync(
            $"""
            delete from {QualifiedScheduleTable}
            where tenant_id = @tenant_id
              and company_id = @company_id
              and schedule_id_key = @identity_key;
            """,
            tenantId,
            scheduleId.ToLowerInvariant(),
            companyId);

    private async Task ExecuteAsync(
        string sql,
        string tenantId,
        string identityKey,
        string? companyId = null)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
        command.Parameters.AddWithValue("identity_key", NpgsqlDbType.Text, identityKey);
        if (companyId is not null)
        {
            command.Parameters.AddWithValue("company_id", NpgsqlDbType.Text, companyId);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_database.Options.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<Exception?> CaptureExceptionAsync(Task operation)
    {
        try
        {
            await operation;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private string QualifiedRunTable =>
        $"\"{_database.Options.Schema}\".\"reporting_run_snapshots\"";

    private string QualifiedRunClaimTable =>
        $"\"{_database.Options.Schema}\".\"reporting_run_create_claims\"";

    private string QualifiedScheduleTable =>
        $"\"{_database.Options.Schema}\".\"reporting_schedule_snapshots\"";
}
