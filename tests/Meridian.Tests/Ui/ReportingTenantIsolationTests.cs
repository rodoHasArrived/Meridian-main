using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class ReportingTenantIsolationTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ReportingGovernanceAccessMode.Restricted, ReportingAccessPrincipalKind.Group, "reporting-reviewers")]
    [InlineData(ReportingGovernanceAccessMode.CompanyWide, ReportingAccessPrincipalKind.Company, "company-shared")]
    public void ScheduledRecipientResolution_PreservesTypedGroupAndCompany(
        ReportingGovernanceAccessMode mode,
        ReportingAccessPrincipalKind kind,
        string principalId)
    {
        var access = new ReportingAccessScope(
            "policy-a",
            "1",
            mode,
            OwnerPrincipalId: null,
            AllowOwnerAccess: false,
            [new ReportingAccessPrincipalScope(kind, principalId)],
            new string('a', 64));

        ReportingScheduleService.ResolveScheduledRecipientPrincipal(access, principalId)
            .Should().Be(new ReportingAccessPrincipalScope(kind, principalId));
    }

    [Fact]
    public void ReportingSchedules_SameIdRemainIsolatedByTenantAndCompany_EvenForAdmin()
    {
        var service = CreateScheduleService();
        var tenantA = Scope("operator-a", "tenant-a", "company-shared");
        var tenantBAdmin = Scope("admin-b", "tenant-b", "company-shared", isAdmin: true);
        var companyBAdmin = Scope("admin-company-b", "tenant-a", "company-b", isAdmin: true);

        var scheduleA = service.Upsert(BuildSchedule("monthly-close", "operator-a"), tenantA);

        scheduleA.TenantId.Should().Be("tenant-a");
        scheduleA.CompanyId.Should().Be("company-shared");
        service.ListSchedules(tenantA).Should().ContainSingle();
        service.ListSchedules(tenantBAdmin).Should().BeEmpty();
        service.ListSchedules(companyBAdmin).Should().BeEmpty();
        service.ListSchedules().Should().BeEmpty("unscoped compatibility reads cannot enumerate tenant schedules");
        var crossTenantMutation = () => service.SetState(
            "monthly-close",
            ReportingScheduleStateDto.Paused,
            tenantBAdmin);
        crossTenantMutation.Should().Throw<KeyNotFoundException>();
        var crossCompanyMutation = () => service.SetState(
            "monthly-close",
            ReportingScheduleStateDto.Paused,
            companyBAdmin);
        crossCompanyMutation.Should().Throw<KeyNotFoundException>();

        var scheduleB = service.Upsert(BuildSchedule("monthly-close", "admin-b"), tenantBAdmin);
        service.SetState("monthly-close", ReportingScheduleStateDto.Paused, tenantBAdmin);

        scheduleB.TenantId.Should().Be("tenant-b");
        service.ListSchedules(tenantA).Should().ContainSingle(schedule =>
            schedule.ScheduleId == "monthly-close"
            && schedule.State == ReportingScheduleStateDto.Active
            && schedule.TenantId == "tenant-a");
        service.ListSchedules(tenantBAdmin).Should().ContainSingle(schedule =>
            schedule.ScheduleId == "monthly-close"
            && schedule.State == ReportingScheduleStateDto.Paused
            && schedule.TenantId == "tenant-b");
    }

    [Fact]
    public void ReportingSchedules_RejectSpoofedActorAndCaseVariantTenantScope()
    {
        var service = CreateScheduleService();
        var tenantA = Scope("operator-a", "tenant-a", "company-a");
        service.Upsert(BuildSchedule("daily-pack", "operator-a"), tenantA);

        var spoofedActor = () => service.Upsert(
            BuildSchedule("spoofed", "attacker"),
            tenantA);
        var caseVariantTenant = Scope("operator-a", "TENANT-A", "company-a", isAdmin: true);
        var caseVariantRead = service.ListSchedules(caseVariantTenant);

        spoofedActor.Should().Throw<UnauthorizedAccessException>();
        caseVariantRead.Should().BeEmpty("tenant identifiers are exact immutable authority keys");
    }

    [Fact]
    public void ReportingScheduleStore_CorruptOrDuplicateScopedStateFailsClosed()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "reporting-schedules.json");
        File.WriteAllText(path, "{\"schedules\":[null]}");
        var store = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(path),
            NullLogger<FileReportingScheduleStore>.Instance);

        var read = () => store.Load();

        read.Should().Throw<ReportingStateCorruptionException>();
    }

    [Fact]
    public void ScheduledReleaseHandoff_IsDurableScopedAndIdempotentlyCompletedAfterQueueSuccess()
    {
        var handoff = BuildReleaseHandoff();
        var schedule = BuildHandoffSchedule([handoff]);
        var store = new StubReportingScheduleStore([schedule]);
        var service = CreateScheduleService(store);
        var tenantA = Scope("operator-a", "tenant-a", "company-shared");
        var tenantBAdmin = Scope("admin-b", "tenant-b", "company-shared", isAdmin: true);

        service.GetPendingReleaseHandoffs("run-a", tenantA).Should().ContainSingle();
        service.GetPendingReleaseHandoffs("run-a", tenantBAdmin).Should().BeEmpty();

        var completed = service.MarkReleaseHandoffEnqueuedForWorker(
            handoff.HandoffId,
            "tenant-a",
            "company-shared",
            "delivery-job-a",
            FixedNow.AddMinutes(1));
        var repeated = service.MarkReleaseHandoffEnqueuedForWorker(
            handoff.HandoffId,
            "tenant-a",
            "company-shared",
            "delivery-job-a",
            FixedNow.AddMinutes(2));
        var conflictingCompletion = () => service.MarkReleaseHandoffEnqueuedForWorker(
            handoff.HandoffId,
            "tenant-a",
            "company-shared",
            "delivery-job-b",
            FixedNow.AddMinutes(3));

        completed.State.Should().Be(ReportingScheduledReleaseHandoffStateDto.Enqueued);
        completed.EnqueuedDeliveryJobId.Should().Be("delivery-job-a");
        repeated.Should().Be(completed);
        conflictingCompletion.Should().Throw<InvalidOperationException>();
        service.GetPendingReleaseHandoffs("run-a", tenantA).Should().BeEmpty();
        store.Saved.Should().ContainSingle(saved =>
            saved.ReleaseDeliveryHandoffs!.Single().EnqueuedDeliveryJobId == "delivery-job-a");
    }

    [Fact]
    public async Task HostedBridge_UnreleasedHandoffCreatesNoJob_ThenReleasedArtifactQueuesExactlyOnce()
    {
        var handoff = BuildReleaseHandoff();
        var store = new StubReportingScheduleStore([BuildHandoffSchedule([handoff])]);
        var scheduleService = CreateScheduleService(store);
        var queue = new RecordingScheduledHandoffQueue();
        var worker = new ReportingSecureDistributionHostedService(
            scheduleService,
            queue.QueueAsync,
            NullLogger<ReportingSecureDistributionHostedService>.Instance);

        var unreleased = await worker.EnqueueReleasedScheduleHandoffsOnceAsync(CancellationToken.None);

        unreleased.Attempted.Should().Be(1);
        unreleased.Enqueued.Should().Be(0);
        unreleased.Failed.Should().Be(1);
        queue.CreatedJobCount.Should().Be(0);
        scheduleService.ListPendingReleaseHandoffsForWorker().Should().ContainSingle();

        queue.Release("run-a", "run-a.pdf");
        var released = await worker.EnqueueReleasedScheduleHandoffsOnceAsync(CancellationToken.None);
        var repeated = await worker.EnqueueReleasedScheduleHandoffsOnceAsync(CancellationToken.None);

        released.Enqueued.Should().Be(1);
        repeated.Attempted.Should().Be(0);
        queue.CreatedJobCount.Should().Be(1);
        queue.Attempts.Should().HaveCount(2, "one attempt was release-blocked and one queued after release");
        var queued = queue.Attempts[^1].Command;
        queued.ArtifactIds.Should().Equal("run-a.pdf");
        queued.Destination.Should().BeEmpty();
        queue.Attempts[^1].Authority.TenantId.Should().Be("tenant-a");
        queue.Attempts[^1].Authority.CompanyId.Should().Be("company-shared");
        scheduleService.ListPendingReleaseHandoffsForWorker().Should().BeEmpty();
    }

    [Fact]
    public async Task HostedBridge_RestartAfterQueueBeforeMark_ReusesJobAndMarksHandoffOnce()
    {
        var handoff = BuildReleaseHandoff();
        var store = new StubReportingScheduleStore([BuildHandoffSchedule([handoff])]);
        var queue = new RecordingScheduledHandoffQueue();
        queue.Release("run-a", "run-a.pdf");
        var firstService = CreateScheduleService(store);
        var firstWorker = new ReportingSecureDistributionHostedService(
            firstService,
            queue.QueueAsync,
            NullLogger<ReportingSecureDistributionHostedService>.Instance);
        store.FailingSaveCount = 1;

        var interrupted = await firstWorker.EnqueueReleasedScheduleHandoffsOnceAsync(CancellationToken.None);

        interrupted.Enqueued.Should().Be(0);
        interrupted.Failed.Should().Be(1);
        queue.CreatedJobCount.Should().Be(1, "the durable queue write happens before handoff completion");
        firstService.ListPendingReleaseHandoffsForWorker().Should().ContainSingle();

        var restartedService = CreateScheduleService(store);
        var restartedWorker = new ReportingSecureDistributionHostedService(
            restartedService,
            queue.QueueAsync,
            NullLogger<ReportingSecureDistributionHostedService>.Instance);
        var recovered = await restartedWorker.EnqueueReleasedScheduleHandoffsOnceAsync(CancellationToken.None);

        recovered.Enqueued.Should().Be(1);
        queue.Attempts.Should().HaveCount(2);
        queue.CreatedJobCount.Should().Be(1, "distribution idempotency returns the job created before the crash");
        var completed = store.Saved.Single().ReleaseDeliveryHandoffs.Should().ContainSingle().Subject;
        completed.State.Should().Be(ReportingScheduledReleaseHandoffStateDto.Enqueued);
        completed.EnqueuedDeliveryJobId.Should().Be(queue.Jobs[handoff.DistributionId]);
    }

    [Theory]
    [InlineData(ReportingAccessPrincipalKind.Group)]
    [InlineData(ReportingAccessPrincipalKind.Company)]
    public async Task HostedBridge_RestartPreservesTypedRecipientWithoutSyntheticGroups(
        ReportingAccessPrincipalKind recipientKind)
    {
        var recipientId = recipientKind == ReportingAccessPrincipalKind.Company
            ? "company-shared"
            : "reporting-reviewers";
        var handoff = BuildReleaseHandoff(
            recipientPrincipalId: recipientId,
            recipientPrincipalKind: recipientKind);
        var store = new StubReportingScheduleStore([BuildHandoffSchedule([handoff])]);
        var queue = new RecordingScheduledHandoffQueue();
        queue.Release("run-a", "run-a.pdf");
        var firstService = CreateScheduleService(store);
        var firstWorker = new ReportingSecureDistributionHostedService(
            firstService,
            queue.QueueAsync,
            NullLogger<ReportingSecureDistributionHostedService>.Instance);
        store.FailingSaveCount = 1;

        (await firstWorker.EnqueueReleasedScheduleHandoffsOnceAsync(CancellationToken.None))
            .Failed.Should().Be(1);
        var restartedWorker = new ReportingSecureDistributionHostedService(
            CreateScheduleService(store),
            queue.QueueAsync,
            NullLogger<ReportingSecureDistributionHostedService>.Instance);
        (await restartedWorker.EnqueueReleasedScheduleHandoffsOnceAsync(CancellationToken.None))
            .Enqueued.Should().Be(1);

        queue.CreatedJobCount.Should().Be(1);
        queue.Attempts.Should().HaveCount(2).And.OnlyContain(attempt =>
            attempt.Command.RecipientPrincipalKind == recipientKind
            && attempt.Command.RecipientPrincipalId == recipientId
            && attempt.Authority.PrincipalIds.IsEmpty
            && attempt.Authority.DelegatedPrincipal == new ReportingAccessPrincipalScope(
                recipientKind,
                recipientId));
        store.Saved.Single().ReleaseDeliveryHandoffs!.Single().RecipientPrincipalKind
            .Should().Be(recipientKind.ToString());
    }

    [Theory]
    [InlineData(ReportAccessPrincipalKindDto.User, "operator-a")]
    [InlineData(ReportAccessPrincipalKindDto.Group, "reporting-team")]
    [InlineData(ReportAccessPrincipalKindDto.Company, "company-shared")]
    public async Task CompanyWideSchedule_ExplicitTypedRecipientSurvivesUpsertRestartHandoffAndQueue(
        ReportAccessPrincipalKindDto recipientKind,
        string recipientId)
    {
        var reportingKind = recipientKind switch
        {
            ReportAccessPrincipalKindDto.User => ReportingAccessPrincipalKind.User,
            ReportAccessPrincipalKindDto.Group => ReportingAccessPrincipalKind.Group,
            ReportAccessPrincipalKindDto.Company => ReportingAccessPrincipalKind.Company,
            _ => throw new ArgumentOutOfRangeException(nameof(recipientKind))
        };
        var destinationResolver = new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a",
                "company-shared",
                recipientId,
                "http-relay",
                $"{recipientKind.ToString().ToLowerInvariant()}-{recipientId}@example.test",
                reportingKind)
        ]);
        var store = new StubReportingScheduleStore([]);
        var scope = Scope("operator-a", "tenant-a", "company-shared");
        var service = CreateScheduleService(store, destinationResolver);
        var request = BuildSchedule("typed-company-wide", "operator-a") with
        {
            DeliveryTargets =
            [
                new ReportingScheduleDeliveryTargetDto(
                    "fund-operations",
                    [GovernanceReportArtifactFormatDto.Pdf],
                    ReportPackDeliveryModeDto.EmailLink,
                    RecipientPrincipalId: recipientId,
                    RecipientPrincipalKind: recipientKind)
            ]
        };

        var saved = await service.UpsertAsync(request, scope, CancellationToken.None);
        var restarted = CreateScheduleService(store, destinationResolver);
        var reloaded = restarted.ListSchedules(scope).Should().ContainSingle().Subject;

        saved.DeliveryTargets.Should().ContainSingle(target =>
            target.RecipientPrincipalId == recipientId
            && target.RecipientPrincipalKind == recipientKind);
        reloaded.DeliveryTargets.Should().BeEquivalentTo(saved.DeliveryTargets);
        ReportingScheduleService.HasValidDeliveryTargetsSnapshot(reloaded).Should().BeTrue();

        var manifest = BuildRunSnapshot("run-a", "tenant-a", "company-shared").Manifest with
        {
            Trigger = ReportingRunTrigger.Scheduled,
            ScheduleId = reloaded.ScheduleId,
            ResolvedParameters = BuildExactRunParameters()
        };
        var handoffs = ReportingScheduleService.BuildReleaseDeliveryHandoffs(reloaded, manifest);
        var handoff = handoffs.Should().ContainSingle().Subject;
        handoff.RecipientPrincipalId.Should().Be(recipientId);
        handoff.RecipientPrincipalKind.Should().Be(reportingKind.ToString());
        store.Save([reloaded with { ReleaseDeliveryHandoffs = handoffs }]);

        var queue = new RecordingScheduledHandoffQueue();
        queue.Release(manifest.RunId, $"{manifest.RunId}.pdf");
        var worker = new ReportingSecureDistributionHostedService(
            CreateScheduleService(store, destinationResolver),
            queue.QueueAsync,
            NullLogger<ReportingSecureDistributionHostedService>.Instance);
        (await worker.EnqueueReleasedScheduleHandoffsOnceAsync(CancellationToken.None))
            .Enqueued.Should().Be(1);

        queue.Attempts.Should().ContainSingle(attempt =>
            attempt.Command.RecipientPrincipalId == recipientId
            && attempt.Command.RecipientPrincipalKind == reportingKind
            && attempt.Authority.DelegatedPrincipal == new ReportingAccessPrincipalScope(
                reportingKind,
                recipientId));
    }

    [Fact]
    public async Task ScheduleTargetChange_CancelsPendingHandoffAcrossRestartAndRetainsEnqueuedAudit()
    {
        var root = CreateTempDirectory();
        var schedulePath = Path.Combine(root, "reporting-schedules.json");
        var store = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(schedulePath),
            NullLogger<FileReportingScheduleStore>.Instance);
        var scope = Scope("operator-a", "tenant-a", "company-shared");
        var initialService = CreateScheduleService(store);
        var initialRequest = BuildSchedule("target-revision", "operator-a") with
        {
            DeliveryTargets =
            [
                new ReportingScheduleDeliveryTargetDto(
                    "fund-operations",
                    [GovernanceReportArtifactFormatDto.Pdf],
                    ReportPackDeliveryModeDto.SecurePortal,
                    RecipientPrincipalId: "reporting-team",
                    RecipientPrincipalKind: ReportAccessPrincipalKindDto.Group)
            ]
        };
        var saved = await initialService.UpsertAsync(initialRequest, scope, CancellationToken.None);
        var pendingManifest = BuildRunSnapshot("pending-run", "tenant-a", "company-shared").Manifest with
        {
            Trigger = ReportingRunTrigger.Scheduled,
            ScheduleId = saved.ScheduleId,
            ResolvedParameters = BuildExactRunParameters()
        };
        var enqueuedManifest = pendingManifest with { RunId = "enqueued-run" };
        var pending = ReportingScheduleService
            .BuildReleaseDeliveryHandoffs(saved, pendingManifest)
            .Should().ContainSingle().Subject;
        var enqueuedDeclaration = ReportingScheduleService
            .BuildReleaseDeliveryHandoffs(saved, enqueuedManifest)
            .Should().ContainSingle().Subject;
        var enqueued = enqueuedDeclaration with
        {
            State = ReportingScheduledReleaseHandoffStateDto.Enqueued,
            EnqueuedDeliveryJobId = "delivery-job-retained",
            EnqueuedAtUtc = FixedNow
        };
        store.Save([saved with { ReleaseDeliveryHandoffs = [pending, enqueued] }]);

        var editingService = CreateScheduleService(store);
        var updated = await editingService.UpsertAsync(
            initialRequest with
            {
                DeliveryTargets =
                [
                    new ReportingScheduleDeliveryTargetDto(
                        "fund-operations",
                        [GovernanceReportArtifactFormatDto.Pdf],
                        ReportPackDeliveryModeDto.SecurePortal,
                        RecipientPrincipalId: "operator-a",
                        RecipientPrincipalKind: ReportAccessPrincipalKindDto.User)
                ]
            },
            scope,
            CancellationToken.None);

        updated.ReleaseDeliveryHandoffs.Should().ContainSingle(handoff =>
            handoff.HandoffId == enqueued.HandoffId
            && handoff.State == ReportingScheduledReleaseHandoffStateDto.Enqueued);
        updated.ReleaseDeliveryHandoffs.Should().NotContain(handoff =>
            handoff.HandoffId == pending.HandoffId);

        var reloaded = CreateScheduleService(store);
        var queue = new RecordingScheduledHandoffQueue();
        var worker = new ReportingSecureDistributionHostedService(
            reloaded,
            queue.QueueAsync,
            NullLogger<ReportingSecureDistributionHostedService>.Instance);
        var bridge = await worker.EnqueueReleasedScheduleHandoffsOnceAsync(CancellationToken.None);

        bridge.Attempted.Should().Be(0);
        queue.Attempts.Should().BeEmpty(
            "a pending declaration from the replaced target snapshot must never queue after restart");
        reloaded.ListSchedules(scope).Should().ContainSingle(schedule =>
            schedule.ReleaseDeliveryHandoffs != null
            && schedule.ReleaseDeliveryHandoffs.Count == 1
            && schedule.ReleaseDeliveryHandoffs[0].HandoffId == enqueued.HandoffId);

        // Defense in depth: even if an obsolete declaration is reintroduced by a stale in-memory
        // writer, worker discovery and the final mark must reject its superseded target authority.
        var staleService = CreateScheduleService(new StubReportingScheduleStore(
        [
            updated with { ReleaseDeliveryHandoffs = [pending, enqueued] }
        ]));
        var staleWorker = new ReportingSecureDistributionHostedService(
            staleService,
            queue.QueueAsync,
            NullLogger<ReportingSecureDistributionHostedService>.Instance);

        (await staleWorker.EnqueueReleasedScheduleHandoffsOnceAsync(CancellationToken.None))
            .Attempted.Should().Be(0);
        var staleMark = () => staleService.MarkReleaseHandoffEnqueuedForWorker(
            pending.HandoffId,
            pending.TenantId,
            pending.CompanyId,
            "delivery-job-stale",
            FixedNow.AddMinutes(1));
        staleMark.Should().Throw<InvalidOperationException>()
            .WithMessage("*no longer bound*");
    }

    [Fact]
    public async Task ScheduleDeliveryMode_IsMaterializedAndHandoffNeverReResolvesMutablePolicy()
    {
        var root = CreateTempDirectory();
        var schedulePath = Path.Combine(root, "reporting-schedules.json");
        var store = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(schedulePath),
            NullLogger<FileReportingScheduleStore>.Instance);
        var scope = Scope("operator-a", "tenant-a", "company-shared");
        var service = CreateScheduleService(store);
        var saved = await service.UpsertAsync(
            BuildSchedule("immutable-delivery-mode", "operator-a") with
            {
                DeliveryTargets =
                [
                    new ReportingScheduleDeliveryTargetDto(
                        "fund-operations",
                        [GovernanceReportArtifactFormatDto.Pdf],
                        DeliveryMode: null,
                        RecipientPrincipalId: "operator-a",
                        RecipientPrincipalKind: ReportAccessPrincipalKindDto.User)
                ]
            },
            scope,
            CancellationToken.None);
        var reloaded = CreateScheduleService(store)
            .ListSchedules(scope)
            .Should().ContainSingle().Subject;
        var materialized = reloaded.DeliveryTargets.Should().ContainSingle().Subject;

        materialized.DeliveryMode.Should().Be(ReportPackDeliveryModeDto.SecurePortal);
        ReportingScheduleService.HasValidDeliveryTargetsSnapshot(reloaded).Should().BeTrue();

        // An identifier no longer present in the live catalog models a policy that was retired or
        // changed after this immutable snapshot was approved. Handoff creation must use the
        // retained effective mode and cannot reinterpret it as external egress.
        var retainedAfterCatalogChange = materialized with
        {
            DistributionId = "retired-fund-operations-policy",
            DeliveryMode = ReportPackDeliveryModeDto.InternalRoute
        };
        var retainedSchedule = reloaded with
        {
            DeliveryTargets = [retainedAfterCatalogChange],
            DeliveryTargetsSnapshotHash = ReportingScheduleService.ComputeDeliveryTargetsSnapshotHash(
                [retainedAfterCatalogChange])
        };
        var manifest = BuildRunSnapshot("mode-freeze-run", "tenant-a", "company-shared").Manifest with
        {
            Trigger = ReportingRunTrigger.Scheduled,
            ScheduleId = reloaded.ScheduleId,
            ResolvedParameters = BuildExactRunParameters()
        };

        var handoff = ReportingScheduleService
            .BuildReleaseDeliveryHandoffs(retainedSchedule, manifest)
            .Should().ContainSingle().Subject;

        handoff.TargetDistributionId.Should().Be("retired-fund-operations-policy");
        handoff.TransportId.Should().Be("secure-portal");
        handoff.GrantLifetimeSeconds.Should().BeNull();
        handoff.GrantMaxUses.Should().BeNull();
    }

    [Fact]
    public async Task CompanyWideSchedule_RejectsMissingExplicitRecipientBeforePersistence()
    {
        var store = new StubReportingScheduleStore([]);
        var service = CreateScheduleService(
            store,
            new RejectingReportingRecipientDestinationResolver());
        var request = BuildSchedule("missing-company-wide-recipient", "operator-a") with
        {
            DeliveryTargets =
            [
                new ReportingScheduleDeliveryTargetDto(
                    "fund-operations",
                    [GovernanceReportArtifactFormatDto.Pdf],
                    ReportPackDeliveryModeDto.SecurePortal)
            ]
        };

        var upsert = () => service.UpsertAsync(
            request,
            Scope("operator-a", "tenant-a", "company-shared"),
            CancellationToken.None);

        await upsert.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*explicit typed recipient*");
        store.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task ExternalSchedule_RejectsUnconfiguredRecipientDirectoryBeforePersistence()
    {
        var store = new StubReportingScheduleStore([]);
        var service = CreateScheduleService(
            store,
            new RejectingReportingRecipientDestinationResolver());
        var request = BuildSchedule("missing-recipient-directory", "operator-a") with
        {
            DeliveryTargets =
            [
                new ReportingScheduleDeliveryTargetDto(
                    "fund-operations",
                    [GovernanceReportArtifactFormatDto.Pdf],
                    ReportPackDeliveryModeDto.EmailLink,
                    RecipientPrincipalId: "operator-a",
                    RecipientPrincipalKind: ReportAccessPrincipalKindDto.User)
            ]
        };

        var upsert = () => service.UpsertAsync(
            request,
            Scope("operator-a", "tenant-a", "company-shared"),
            CancellationToken.None);

        await upsert.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*recipient destination directory*");
        store.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task RestrictedSchedule_RejectsAmbiguousLegacyRecipientInferenceBeforePersistence()
    {
        var scope = Scope("operator-a", "tenant-a", "company-shared");
        var registry = new ReportTemplateRegistryService();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "ambiguous-recipient-pack",
                "Ambiguous recipient pack",
                ["summary"],
                [],
                Rationale: "Prove typed recipient namespaces cannot be inferred.",
                AccessPolicy: new ReportAccessPolicyDto(
                    ReportAccessModeDto.Restricted,
                    OwnerPrincipalId: null,
                    Principals:
                    [
                        new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.User, "operator-a"),
                        new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.User, "shared-id"),
                        new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "shared-id")
                    ],
                    CompanyId: "company-shared",
                    AllowOwnerAccess: false)),
            "operator-a",
            "company-shared",
            tenantId: "tenant-a");
        registry.Submit(draft.Definition.TemplateId, "operator-a", "Ready for review.", scope);
        registry.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Approved.", "ambiguous-template-approval"),
            "reviewer-a",
            Scope("reviewer-a", "tenant-a", "company-shared", isAdmin: true));
        var catalog = new GovernedReportingTemplateCatalog(
            new DefaultReportingTemplateCatalog(),
            registry);
        var orchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => FixedNow);
        var store = new StubReportingScheduleStore([]);
        var service = new ReportingScheduleService(
            orchestration,
            store,
            governedTemplateCatalog: catalog);
        var request = BuildSchedule("ambiguous-recipient", "operator-a") with
        {
            TemplateId = draft.Definition.TemplateId.Name,
            Template = draft.Definition.TemplateId,
            DeliveryTargets =
            [
                new ReportingScheduleDeliveryTargetDto(
                    "fund-operations",
                    [GovernanceReportArtifactFormatDto.Pdf],
                    ReportPackDeliveryModeDto.SecurePortal,
                    RecipientPrincipalId: "shared-id")
            ]
        };

        var upsert = () => service.UpsertAsync(request, scope, CancellationToken.None);

        await upsert.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*identifier appears in multiple principal namespaces*");
        store.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task HostedBridge_PagedPoisonHandoffs_DoNotStarveLaterValidReleasedWork()
    {
        var queue = new RecordingScheduledHandoffQueue();
        var poison = Enumerable.Range(0, 101)
            .Select(index => BuildReleaseHandoff(
                index.ToString("x64", System.Globalization.CultureInfo.InvariantCulture),
                $"poison-run-{index}",
                $"poison-run-{index}.wrong.pdf",
                FixedNow.AddSeconds(index)))
            .ToArray();
        foreach (var handoff in poison)
        {
            queue.Release(handoff.RunId, $"{handoff.RunId}.pdf");
        }
        var valid = BuildReleaseHandoff(
            new string('f', 64),
            "valid-run",
            "valid-run.pdf",
            FixedNow.AddSeconds(102));
        queue.Release(valid.RunId, "valid-run.pdf");
        var store = new StubReportingScheduleStore(
            [BuildHandoffSchedule(poison.Append(valid).ToArray())]);
        var scheduleService = CreateScheduleService(store);
        var worker = new ReportingSecureDistributionHostedService(
            scheduleService,
            queue.QueueAsync,
            NullLogger<ReportingSecureDistributionHostedService>.Instance,
            handoffBatchSize: 100);

        var firstPage = await worker.EnqueueReleasedScheduleHandoffsOnceAsync(CancellationToken.None);
        var secondPage = await worker.EnqueueReleasedScheduleHandoffsOnceAsync(CancellationToken.None);

        firstPage.Attempted.Should().Be(100);
        firstPage.Enqueued.Should().Be(0);
        firstPage.Failed.Should().Be(100);
        secondPage.Attempted.Should().Be(2);
        secondPage.Enqueued.Should().Be(1);
        secondPage.Failed.Should().Be(1, "the remaining poison handoff cannot abort the valid item behind it");
        queue.CreatedJobCount.Should().Be(1);
        queue.Jobs.Should().ContainKey(valid.DistributionId);
        var retained = store.Saved.Single().ReleaseDeliveryHandoffs!;
        retained.Single(item => item.HandoffId == valid.HandoffId).State
            .Should().Be(ReportingScheduledReleaseHandoffStateDto.Enqueued);
        retained.Count(item => item.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease)
            .Should().Be(101);
    }

    /// <summary>
    /// Guards the month-end scheduled reporting scenario where automation may prepare an exact
    /// certified draft, but an independent reviewer and delivery operator must retain control of
    /// approval, release, and distribution after a process restart.
    /// </summary>
    [Fact]
    public async Task Scenario_MonthEndScheduledReport_CertifiesDraftAndQueuesOnlyAfterIndependentRelease()
    {
        var root = CreateTempDirectory();
        var schedulePath = Path.Combine(root, "reporting-schedules.json");
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var ownerScope = Scope("operator-a", "tenant-a", "company-a");
        var registry = new ReportTemplateRegistryService();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "private-month-end-pack",
                "Private month-end pack",
                ["summary"],
                [],
                Rationale: "Recurring controller-owned month-end reporting.",
                AccessPolicy: new ReportAccessPolicyDto(
                    ReportAccessModeDto.Private,
                    OwnerPrincipalId: "operator-a",
                    Principals:
                    [
                        new ReportAccessPrincipalDto(
                            ReportAccessPrincipalKindDto.User,
                            "delegate-a"),
                        new ReportAccessPrincipalDto(
                            ReportAccessPrincipalKindDto.User,
                            "approver-a"),
                        new ReportAccessPrincipalDto(
                            ReportAccessPrincipalKindDto.User,
                            "delivery-a")
                    ],
                    CompanyId: "company-a")),
            "operator-a",
            "company-a",
            tenantId: "tenant-a");
        registry.Submit(draft.Definition.TemplateId, "operator-a", "Ready for independent review.", ownerScope);
        registry.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Approved private schedule template.", "template-approval-a"),
            "template-reviewer-a",
            Scope("template-reviewer-a", "tenant-a", "company-a", isAdmin: true));

        var catalog = new GovernedReportingTemplateCatalog(
            new DefaultReportingTemplateCatalog(),
            registry);
        var orchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => FixedNow,
            runStore);
        var source = new ExactScheduleAuthoritativeSource();
        var certification = new ReportingRunCertificationService(
            source,
            new ExactScheduleReconciliationSource());
        var readiness = new ReportingRunReadinessService(
            catalog,
            catalog,
            new ReadyScheduleDependencyEvaluator(),
            () => FixedNow);
        var governance = new RecordingScheduleGovernanceCoordinator(orchestration);
        var scheduleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(schedulePath),
            NullLogger<FileReportingScheduleStore>.Instance);
        var service = new ReportingScheduleService(
            orchestration,
            scheduleStore,
            governedTemplateCatalog: catalog,
            readinessService: readiness,
            certificationService: certification,
            governanceCoordinator: governance);
        var parameters = BuildExactRunParameters();
        var request = new ReportingScheduleUpsertRequestDto(
            "private-month-end",
            draft.Definition.TemplateId.Name,
            "0 8 1 * *",
            parameters.AsOfDate,
            FixedNow.AddMinutes(-1),
            2,
            "operator-a",
            "Certified month-end schedule.",
            DeliveryTargets:
            [
                new ReportingScheduleDeliveryTargetDto(
                    "fund-operations",
                    [GovernanceReportArtifactFormatDto.Pdf],
                    ReportPackDeliveryModeDto.SecurePortal,
                    RecipientPrincipalId: "operator-a",
                    RecipientPrincipalKind: ReportAccessPrincipalKindDto.User)
            ],
            Template: draft.Definition.TemplateId,
            RunParameters: parameters);

        var privateDelegate = () => service.Upsert(
            request with { ScheduleId = "private-delegate", RequestedBy = "delegate-a" },
            Scope("delegate-a", "tenant-a", "company-a"));
        privateDelegate.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*immutable owner*");
        var unavailableFormat = () => service.Upsert(
            request with
            {
                ScheduleId = "private-unavailable-format",
                DeliveryTargets =
                [
                    new ReportingScheduleDeliveryTargetDto(
                        "fund-operations",
                        [GovernanceReportArtifactFormatDto.Csv],
                        ReportPackDeliveryModeDto.SecurePortal,
                        RecipientPrincipalId: "operator-a",
                        RecipientPrincipalKind: ReportAccessPrincipalKindDto.User)
                ]
            },
            ownerScope);
        unavailableFormat.Should().Throw<InvalidDataException>()
            .WithMessage("*exact primary output is Pdf*");

        var retainedSchedule = service.Upsert(request, ownerScope);
        var incompleteScope = Scope("operator-b", "tenant-b", "company-b");
        var incompleteDraft = service.Upsert(
            new ReportingScheduleUpsertRequestDto(
                "a-incomplete-schedule",
                "investor-monthly-statement",
                "0 7 1 * *",
                parameters.AsOfDate,
                FixedNow.AddMinutes(-2),
                2,
                "operator-b",
                "Legacy incomplete schedule used to prove worker isolation.",
                State: ReportingScheduleStateDto.Draft),
            incompleteScope);
        var invalidActivation = () => service.SetState(
            incompleteDraft.ScheduleId,
            ReportingScheduleStateDto.Active,
            incompleteScope);
        invalidActivation.Should().Throw<InvalidDataException>()
            .WithMessage("*immutable run-parameter snapshot*");

        var result = await service.RunDueAsync(FixedNow, CancellationToken.None);

        var runResult = result.Runs.Should().ContainSingle().Subject;
        governance.CurrentRun.Should().NotBeNull();
        var governed = governance.CurrentRun!;
        governed.RunId.Should().Be(runResult.Run.RunId);
        governed.ExecutionState.Should().Be(GovernedReportingExecutionState.Succeeded);
        governed.GovernanceState.Should().Be(GovernedReportingState.Draft);
        governed.Scope.TenantId.Should().Be("tenant-a");
        governed.Scope.CompanyId.Should().Be("company-a");
        governed.CreationAuthority.ActorId.Should().Be(retainedSchedule.RequestedBy);
        governed.CreationAuthority.Permissions.Should().Contain(ReportingGovernancePermission.CreateRun);
        governed.CreationAuthority.Permissions.Should().NotContain(ReportingGovernancePermission.ApproveRun);
        governed.CreationAuthority.Permissions.Should().NotContain(ReportingGovernancePermission.ReleaseRun);
        governance.LastCaller.Should().NotBeNull();
        governance.LastCaller!.Origin.Should().Be(ReportingCommandOrigin.ServicePrincipal);
        governance.LastCaller.TenantId.Should().Be("tenant-a");
        governance.LastCaller.CompanyId.Should().Be("company-a");
        governance.LastCaller.Permissions.Should().Be(UserPermission.ManageReporting);
        source.LastParameters.Should().BeEquivalentTo(parameters);
        source.LastAccessContext!.ActorPrincipalId.Should().Be("operator-a");
        source.LastAccessContext.TenantId.Should().Be("tenant-a");
        source.LastAccessContext.CompanyId.Should().Be("company-a");
        source.LastAccessContext.HasGlobalOverride.Should().BeFalse();
        var manifest = runStore.GetManifest(governed.RunId);
        manifest.Should().NotBeNull();
        manifest!.CertifiedSnapshot.Should().NotBeNull();
        manifest.AuthoritativeSource.Should().NotBeNull();
        manifest.ResolvedParameters.Should().BeEquivalentTo(parameters);
        var csvSelection = ReportingScheduleService.ResolveScheduledArtifactSelection(
            new ReportingScheduleDeliveryTargetDto(
                "fund-operations",
                [GovernanceReportArtifactFormatDto.Csv]),
            manifest with
            {
                RunId = "scheduled-csv-run",
                ResolvedParameters = parameters with { OutputFormat = ReportingOutputFormatDto.Csv }
            });
        csvSelection.ArtifactIds.Should().Equal("scheduled-csv-run.csv");
        csvSelection.ArtifactIds.Should().NotContain(artifactId =>
            artifactId.Contains("certified-source", StringComparison.Ordinal)
            || artifactId.Contains("supporting-schedules", StringComparison.Ordinal));
        var jsonSelection = ReportingScheduleService.ResolveScheduledArtifactSelection(
            new ReportingScheduleDeliveryTargetDto(
                "fund-operations",
                [GovernanceReportArtifactFormatDto.Json]),
            manifest with
            {
                RunId = "scheduled-json-run",
                ResolvedParameters = parameters with { OutputFormat = ReportingOutputFormatDto.EvidenceVault }
            });
        jsonSelection.ArtifactIds.Should().Equal("scheduled-json-run.evidence-vault.json");
        jsonSelection.ArtifactIds.Should().NotContain(artifactId =>
            artifactId.Contains("manifest", StringComparison.Ordinal)
            || artifactId.Contains("evidence-appendix", StringComparison.Ordinal));

        var interruptedSchedulePath = Path.Combine(root, "interrupted-reporting-schedules.json");
        var interruptedStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(interruptedSchedulePath),
            NullLogger<FileReportingScheduleStore>.Instance);
        interruptedStore.Save(
        [
            incompleteDraft with { State = ReportingScheduleStateDto.Active },
            retainedSchedule
        ]);
        var restartedOrchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => FixedNow,
            runStore);
        var restartedGovernance = new RecordingScheduleGovernanceCoordinator(restartedOrchestration);
        var recoveredService = new ReportingScheduleService(
            restartedOrchestration,
            new FileReportingScheduleStore(
                new ReportingScheduleStoreOptions(interruptedSchedulePath),
                NullLogger<FileReportingScheduleStore>.Instance),
            governedTemplateCatalog: catalog,
            readinessService: readiness,
            certificationService: certification,
            governanceCoordinator: restartedGovernance);
        var recovered = await recoveredService.RunDueForWorkerAsync(FixedNow, CancellationToken.None);
        recovered.Result.Runs.Should().ContainSingle(run => run.Run.RunId == governed.RunId);
        recovered.Failures.Should().ContainSingle(failure =>
            failure.ScheduleId == incompleteDraft.ScheduleId
            && failure.TenantId == "tenant-b"
            && failure.CompanyId == "company-b"
            && failure.ErrorType == nameof(InvalidDataException)
            && failure.FailureRecordingErrorType == null);
        runStore.ListRuns(10).Should().ContainSingle(snapshot => snapshot.Manifest.RunId == governed.RunId);
        var retainedFailure = recoveredService.ListSchedules(incompleteScope)
            .Should().ContainSingle().Subject;
        retainedFailure.State.Should().Be(ReportingScheduleStateDto.Active);
        retainedFailure.DueAtUtc.Should().BeOnOrBefore(FixedNow);
        retainedFailure.LastRunId.Should().BeNull();
        retainedFailure.RunCount.Should().Be(0);

        var secondRestart = new ReportingScheduleService(
            restartedOrchestration,
            new FileReportingScheduleStore(
                new ReportingScheduleStoreOptions(interruptedSchedulePath),
                NullLogger<FileReportingScheduleStore>.Instance),
            governedTemplateCatalog: catalog,
            readinessService: readiness,
            certificationService: certification,
            governanceCoordinator: restartedGovernance);
        var retried = await secondRestart.RunDueForWorkerAsync(FixedNow, CancellationToken.None);
        retried.Result.Runs.Should().BeEmpty();
        retried.Failures.Should().ContainSingle(failure =>
            failure.ScheduleId == incompleteDraft.ScheduleId
            && failure.ErrorType == nameof(InvalidDataException));
        runStore.ListRuns(10).Should().ContainSingle(snapshot => snapshot.Manifest.RunId == governed.RunId);

        var reloaded = new ReportingScheduleService(
            orchestration,
            new FileReportingScheduleStore(
                new ReportingScheduleStoreOptions(schedulePath),
                NullLogger<FileReportingScheduleStore>.Instance),
            governedTemplateCatalog: catalog,
            readinessService: readiness,
            certificationService: certification,
            governanceCoordinator: governance);
        var handoff = reloaded.GetPendingReleaseHandoffs(governed.RunId, ownerScope)
            .Should().ContainSingle().Subject;
        handoff.State.Should().Be(ReportingScheduledReleaseHandoffStateDto.PendingRelease);
        handoff.HandoffId.Should().HaveLength(64);
        handoff.DistributionId.Should().Be($"scheduled:{handoff.HandoffId}");
        handoff.RequestedFormats.Should().Equal(GovernanceReportArtifactFormatDto.Pdf);
        handoff.ArtifactIds.Should().Equal($"{governed.RunId}.pdf");
        handoff.Destination.Should().BeEmpty(
            "the recipient directory owns external destination resolution after release");

        var sameActorApproval = () => governance.ApproveAsync(
            governed.RunId,
            governed.Version,
            "Self approval is not permitted.",
            GovernanceCaller("operator-a", UserPermission.ApproveReporting),
            CancellationToken.None);
        await sameActorApproval.Should().ThrowAsync<ReportingGovernanceAuthorizationException>()
            .WithMessage("*preparer*");
        AssertHandoffStillPending(reloaded, governed.RunId, ownerScope);

        governed = await governance.ValidateAsync(
            governed.RunId,
            governed.Version,
            GovernanceCaller("operator-a", UserPermission.ManageReporting),
            CancellationToken.None);
        governed.GovernanceState.Should().Be(GovernedReportingState.Validated);
        AssertHandoffStillPending(reloaded, governed.RunId, ownerScope);
        governed = await governance.SubmitAsync(
            governed.RunId,
            governed.Version,
            GovernanceCaller("operator-a", UserPermission.ManageReporting),
            CancellationToken.None);
        governed.GovernanceState.Should().Be(GovernedReportingState.InReview);
        AssertHandoffStillPending(reloaded, governed.RunId, ownerScope);
        governed = await governance.ApproveAsync(
            governed.RunId,
            governed.Version,
            "Independent controller approval.",
            GovernanceCaller("approver-a", UserPermission.ApproveReporting),
            CancellationToken.None);
        governed.GovernanceState.Should().Be(GovernedReportingState.Approved);
        AssertHandoffStillPending(reloaded, governed.RunId, ownerScope);
        governed = await governance.ReleaseAsync(
            governed.RunId,
            governed.Version,
            GovernanceCaller("delivery-a", UserPermission.DeliverReporting),
            CancellationToken.None);
        governed.GovernanceState.Should().Be(GovernedReportingState.Released);
        AssertHandoffStillPending(reloaded, governed.RunId, ownerScope);

        var enqueued = reloaded.MarkReleaseHandoffEnqueuedForWorker(
            handoff.HandoffId,
            "tenant-a",
            "company-a",
            "delivery-job-a",
            FixedNow.AddMinutes(1));
        var repeated = reloaded.MarkReleaseHandoffEnqueuedForWorker(
            handoff.HandoffId,
            "tenant-a",
            "company-a",
            "delivery-job-a",
            FixedNow.AddMinutes(2));
        enqueued.State.Should().Be(ReportingScheduledReleaseHandoffStateDto.Enqueued);
        repeated.Should().Be(enqueued);
        reloaded.GetPendingReleaseHandoffs(governed.RunId, ownerScope).Should().BeEmpty();
    }

    [Fact]
    public void StarterKitStore_SameCompanyIdAcrossTenantsKeepsIndependentState()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "reporting-starter-kit.json");
        var store = new FileReportingStarterKitStore(
            new ReportingStarterKitStoreOptions(path),
            NullLogger<FileReportingStarterKitStore>.Instance);
        var stateA = BuildStarterKitState("kit-a", "operator-a");
        var stateB = BuildStarterKitState("kit-b", "operator-b");

        store.Save("tenant-a", "company-shared", stateA);
        store.Save("tenant-b", "company-shared", stateB);

        store.Load("tenant-a", "company-shared").Should().BeEquivalentTo(stateA);
        store.Load("tenant-b", "company-shared").Should().BeEquivalentTo(stateB);
        store.Load("TENANT-A", "company-shared").Should().BeNull(
            "tenant identifiers are exact immutable authority keys");
    }

    [Fact]
    public void StarterKitStore_LegacyUnscopedOrCorruptStateFailsClosed()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "reporting-starter-kit.json");
        File.WriteAllText(path, "{\"state\":{\"isProvisioned\":true}}");
        var store = new FileReportingStarterKitStore(
            new ReportingStarterKitStoreOptions(path),
            NullLogger<FileReportingStarterKitStore>.Instance);

        var read = () => store.Load("tenant-a", "company-a");

        read.Should().Throw<ReportingStateCorruptionException>();
    }

    [Fact]
    public void TemplateRegistry_BindsImmutableTenantOwnership_AndAdminCannotCrossTenant()
    {
        var registry = new ReportTemplateRegistryService();
        var tenantA = Scope("author-a", "tenant-a", "company-shared");
        var tenantBAdmin = Scope("admin-b", "tenant-b", "company-shared", isAdmin: true);
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "tenant-owned-pack",
                "Tenant owned pack",
                ["summary"],
                []),
            "author-a",
            tenantA.CompanyId,
            tenantA.GroupPrincipalIds,
            tenantA.TenantId);

        draft.TenantId.Should().Be("tenant-a");
        draft.CompanyId.Should().Be("company-shared");
        registry.List(tenantA).Should().Contain(record =>
            record.Definition.TemplateId == draft.Definition.TemplateId);
        registry.List(tenantBAdmin).Should().NotContain(record =>
            record.Definition.TemplateId == draft.Definition.TemplateId);
        registry.Get(draft.Definition.TemplateId, tenantBAdmin).Should().BeNull();

        var crossTenantSubmit = () => registry.Submit(
            draft.Definition.TemplateId,
            "admin-b",
            "attempted cross-tenant transition",
            tenantBAdmin);
        crossTenantSubmit.Should().Throw<KeyNotFoundException>();

        var tenantBDraft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "tenant-owned-pack",
                "Tenant B owned pack",
                ["summary"],
                []),
            "admin-b",
            tenantBAdmin.CompanyId,
            tenantBAdmin.GroupPrincipalIds,
            tenantBAdmin.TenantId);

        tenantBDraft.Definition.TemplateId.Should().Be(draft.Definition.TemplateId,
            "template identity is tenant-scoped rather than allocated from another tenant's version sequence");
        registry.List(tenantA).Should().ContainSingle(record =>
            !record.IsBuiltIn
            && record.CreatedBy == "author-a"
            && record.Definition.TemplateId == draft.Definition.TemplateId);
        registry.List(tenantBAdmin).Should().ContainSingle(record =>
            !record.IsBuiltIn
            && record.CreatedBy == "admin-b"
            && record.Definition.TemplateId == tenantBDraft.Definition.TemplateId);
    }

    [Fact]
    public async Task ReportWriterGridRead_CrossTenantAdminIsDeniedBeforeArtifactLookup()
    {
        var catalog = new DefaultReportingTemplateCatalog();
        var orchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => FixedNow);
        var manifest = await orchestration.ExecuteAsync(
            new ReportingJobContract(
                "tenant-a-grid",
                "investor-monthly-statement",
                new DateOnly(2026, 7, 15),
                ReportingRunTrigger.AdHoc,
                0,
                "operator-a",
                FixedNow,
                OperationalScope: new ReportingOperationalScope(
                    "tenant-a",
                    "organization-a",
                    "company-shared",
                    FundId: null,
                    BookId: "book-a",
                    PeriodId: "2026-07"),
                ImmutableAccessScope: new ReportingAccessScope(
                    "policy-a",
                    "1",
                    ReportingGovernanceAccessMode.CompanyWide,
                    OwnerPrincipalId: null,
                    AllowOwnerAccess: false,
                    Principals: ImmutableArray<ReportingAccessPrincipalScope>.Empty,
                    PolicyHash: "policy-hash-a")),
            CancellationToken.None);
        var service = new ReportWriterGridArtifactService(orchestration);
        var tenantBAdmin = Scope("admin-b", "tenant-b", "company-shared", isAdmin: true);

        var read = () => service.GetGrid(manifest.RunId, "any-grid", tenantBAdmin);

        read.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*another tenant or company*");
    }

    [Fact]
    public void WorkstationReportingRead_AdminSeesOnlyImmutableTenantRunScope()
    {
        var store = new StubReportingRunStore(
        [
            BuildRunSnapshot("run-a", "tenant-a", "company-shared"),
            BuildRunSnapshot("run-b", "tenant-b", "company-shared")
        ]);
        var service = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            store);

        var payload = service.BuildPayload(
            Scope("admin-a", "tenant-a", "company-shared", isAdmin: true));

        payload.RecentRuns.Should().ContainSingle(run => run.RunId == "run-a");
        payload.RecentRuns.Should().NotContain(run => run.RunId == "run-b");
    }

    private static ReportingScheduleService CreateScheduleService(
        IReportingScheduleStore? store = null,
        IReportingRecipientDestinationResolver? destinationResolver = null)
    {
        var fallback = new DefaultReportingTemplateCatalog();
        var registry = new ReportTemplateRegistryService();
        var governed = new GovernedReportingTemplateCatalog(fallback, registry);
        var orchestration = new ReportingOrchestrationService(
            governed,
            new DeterministicReportingSectionRenderer(),
            () => FixedNow);
        return new ReportingScheduleService(
            orchestration,
            store,
            governedTemplateCatalog: governed,
            destinationResolver: destinationResolver);
    }

    private static ReportingRunParametersDto BuildExactRunParameters() =>
        new(
            new ReportingRunScopeDto("fund-a"),
            "2026-07",
            new DateOnly(2026, 7, 31),
            new ReportingLedgerBookSelectionDto(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "primary"),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Final,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true);

    private static ReportingGovernanceCallerContext GovernanceCaller(
        string actor,
        UserPermission permissions) =>
        new(
            actor,
            "tenant-a",
            "company-a",
            permissions,
            ReportingCommandOrigin.HumanOperator,
            $"test:{actor}:{permissions}",
            ImmutableArray.Create(actor));

    private static void AssertHandoffStillPending(
        ReportingScheduleService service,
        string runId,
        ReportAccessQueryContext accessContext) =>
        service.GetPendingReleaseHandoffs(runId, accessContext)
            .Should().ContainSingle(handoff =>
                handoff.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease);

    private static ReportingScheduleUpsertRequestDto BuildSchedule(
        string scheduleId,
        string actor) =>
        new(
            scheduleId,
            "investor-monthly-statement",
            "0 8 1 * *",
            new DateOnly(2026, 7, 31),
            FixedNow.AddDays(1),
            1,
            actor,
            RunParameters: BuildExactRunParameters());

    private static ReportAccessQueryContext Scope(
        string actor,
        string tenantId,
        string companyId,
        bool isAdmin = false) =>
        new(
            ActorPrincipalId: actor,
            GroupPrincipalIds: ["reporting-team"],
            CompanyId: companyId,
            HasGlobalOverride: isAdmin,
            TenantId: tenantId,
            RequireBoundScope: true);

    private static ReportingStarterKitStateDto BuildStarterKitState(
        string kitId,
        string actor) =>
        new(
            IsProvisioned: true,
            SelectedKitId: kitId,
            Archetype: "test",
            EnabledTemplateIds: ["investor-monthly-statement"],
            DefaultLayoutId: "default",
            DefaultPeriod: "CurrentMonth",
            SeedScheduleIds: ["monthly-close"],
            ProvisionedAtUtc: FixedNow,
            ProvisionedBy: actor);

    private static ReportingScheduledReleaseHandoffDto BuildReleaseHandoff(
        string? handoffId = null,
        string runId = "run-a",
        string? artifactId = null,
        DateTimeOffset? createdAtUtc = null,
        string recipientPrincipalId = "recipient-a",
        ReportingAccessPrincipalKind recipientPrincipalKind = ReportingAccessPrincipalKind.User)
    {
        var resolvedHandoffId = handoffId ?? new string('a', 64);
        var resolvedArtifactId = artifactId ?? $"{runId}.pdf";
        return new(
            HandoffId: resolvedHandoffId,
            TenantId: "tenant-a",
            CompanyId: "company-shared",
            ScheduleId: "monthly-close",
            RunId: runId,
            TemplateId: "investor-monthly-statement",
            DistributionId: $"scheduled:{resolvedHandoffId}",
            TargetDistributionId: $"fund-operations-{resolvedHandoffId[..8]}",
            TransportId: "secure-portal",
            RecipientPrincipalId: recipientPrincipalId,
            Destination: string.Empty,
            Subject: "Released report",
            Body: "A governed report is available.",
            RequestedFormats: [GovernanceReportArtifactFormatDto.Pdf],
            ArtifactIds: [resolvedArtifactId],
            GrantLifetimeSeconds: null,
            GrantMaxUses: null,
            MaxAttempts: 3,
            CreatedAtUtc: createdAtUtc ?? FixedNow,
            RecipientPrincipalKind: recipientPrincipalKind.ToString());
    }

    private static ReportingScheduleRecordDto BuildHandoffSchedule(
        IReadOnlyList<ReportingScheduledReleaseHandoffDto> handoffs)
    {
        var accessPolicy = new ReportAccessPolicyDto(
            ReportAccessModeDto.CompanyWide,
            CompanyId: "company-shared");
        var targets = handoffs
            .Select(handoff => new ReportingScheduleDeliveryTargetDto(
                handoff.TargetDistributionId,
                handoff.RequestedFormats,
                string.Equals(handoff.TransportId, "http-relay", StringComparison.Ordinal)
                    ? ReportPackDeliveryModeDto.EmailLink
                    : ReportPackDeliveryModeDto.SecurePortal,
                RecipientPrincipalId: handoff.RecipientPrincipalId,
                RecipientPrincipalKind: Enum.Parse<ReportingAccessPrincipalKind>(
                    handoff.RecipientPrincipalKind!,
                    ignoreCase: false) switch
                {
                    ReportingAccessPrincipalKind.User => ReportAccessPrincipalKindDto.User,
                    ReportingAccessPrincipalKind.Group => ReportAccessPrincipalKindDto.Group,
                    ReportingAccessPrincipalKind.Company => ReportAccessPrincipalKindDto.Company,
                    _ => throw new InvalidDataException("Unknown scheduled handoff recipient kind.")
                }))
            .ToArray();
        var targetSnapshotHash = ReportingScheduleService.ComputeDeliveryTargetsSnapshotHash(targets);
        var boundHandoffs = handoffs
            .Select(handoff => handoff with { DeliveryTargetsSnapshotHash = targetSnapshotHash })
            .ToArray();
        return new ReportingScheduleRecordDto(
            "monthly-close",
            "investor-monthly-statement",
            "0 8 1 * *",
            new DateOnly(2026, 7, 31),
            FixedNow.AddDays(1),
            2,
            "operator-a",
            ReportingScheduleStateDto.Active,
            FixedNow,
            FixedNow,
            RunParameters: BuildExactRunParameters(),
            TenantId: "tenant-a",
            CompanyId: "company-shared",
            AccessPolicySnapshot: accessPolicy,
            DeliveryTargets: targets,
            ReleaseDeliveryHandoffs: boundHandoffs,
            AccessPolicySnapshotHash: ReportingScheduleService.ComputeAccessPolicySnapshotHash(accessPolicy),
            DeliveryTargetsSnapshotHash: targetSnapshotHash);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "Meridian.Tests",
            nameof(ReportingTenantIsolationTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static ReportingRunSnapshot BuildRunSnapshot(
        string runId,
        string tenantId,
        string companyId)
    {
        var manifest = new ReportingOutputManifest(
            runId,
            "investor-monthly-statement",
            new DateOnly(2026, 7, 15),
            ReportingRunStatus.Draft,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray<string>.Empty,
            1,
            ReportingRunTrigger.AdHoc,
            OperationalScope: new ReportingOperationalScope(
                tenantId,
                $"organization-{tenantId}",
                companyId,
                FundId: null,
                BookId: "book-a",
                PeriodId: "2026-07"),
            ImmutableAccessScope: new ReportingAccessScope(
                $"policy-{tenantId}",
                "1",
                ReportingGovernanceAccessMode.CompanyWide,
                OwnerPrincipalId: null,
                AllowOwnerAccess: false,
                Principals: ImmutableArray<ReportingAccessPrincipalScope>.Empty,
                PolicyHash: $"policy-hash-{tenantId}"));
        return new ReportingRunSnapshot(manifest, [], FixedNow);
    }

    private sealed class StubReportingRunStore(IReadOnlyList<ReportingRunSnapshot> runs)
        : IReportingRunStore
    {
        public IReadOnlyList<ReportingRunSnapshot> ListRuns(int limit = 25) =>
            runs.Take(limit).ToArray();

        public ReportingOutputManifest? GetManifest(string runId) =>
            runs.FirstOrDefault(run => string.Equals(
                run.Manifest.RunId,
                runId,
                StringComparison.Ordinal))?.Manifest;

        public IReadOnlyList<ReportingRunAuditEntry> GetAudit(string runId) =>
            runs.FirstOrDefault(run => string.Equals(
                run.Manifest.RunId,
                runId,
                StringComparison.Ordinal))?.AuditTrail ?? [];

        public Task SaveAsync(
            ReportingOutputManifest manifest,
            IReadOnlyList<ReportingRunAuditEntry> auditTrail,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StubReportingScheduleStore(IReadOnlyList<ReportingScheduleRecordDto> schedules)
        : IReportingScheduleStore
    {
        public IReadOnlyList<ReportingScheduleRecordDto> Saved { get; private set; } = schedules;
        public int FailingSaveCount { get; set; }

        public IReadOnlyList<ReportingScheduleRecordDto> Load() => Saved;

        public void Save(IReadOnlyList<ReportingScheduleRecordDto> updated)
        {
            if (FailingSaveCount > 0)
            {
                FailingSaveCount--;
                throw new IOException("Simulated durable schedule-store interruption.");
            }

            Saved = updated.ToArray();
        }
    }

    private sealed class RecordingScheduledHandoffQueue
    {
        private readonly Dictionary<string, HashSet<string>> _releasedArtifacts =
            new(StringComparer.Ordinal);

        public List<(
            SecureReportingDeliveryQueueCommand Command,
            ReportingDistributionAuthority Authority)> Attempts { get; } = [];

        public Dictionary<string, string> Jobs { get; } = new(StringComparer.Ordinal);

        public int CreatedJobCount { get; private set; }

        public void Release(string runId, params string[] artifactIds) =>
            _releasedArtifacts[runId] = artifactIds.ToHashSet(StringComparer.Ordinal);

        public Task<string> QueueAsync(
            SecureReportingDeliveryQueueCommand command,
            ReportingDistributionAuthority authority,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts.Add((command, authority));
            if (!string.Equals(authority.TenantId, "tenant-a", StringComparison.Ordinal)
                || !string.Equals(authority.CompanyId, "company-shared", StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("Scheduled bridge authority crossed scope.");
            }
            if (!string.IsNullOrEmpty(command.Destination))
            {
                throw new InvalidDataException("External destination assertions must be server resolved.");
            }
            if (!_releasedArtifacts.TryGetValue(command.RunId, out var released))
            {
                throw new InvalidOperationException("The governed run is not released.");
            }
            if (command.ArtifactIds is not { Count: > 0 }
                || command.ArtifactIds.Any(artifactId => !released.Contains(artifactId)))
            {
                throw new InvalidDataException("The handoff selects an artifact outside the release receipt.");
            }
            if (!Jobs.TryGetValue(command.DistributionId, out var jobId))
            {
                jobId = $"job:{command.DistributionId}";
                Jobs.Add(command.DistributionId, jobId);
                CreatedJobCount++;
            }

            return Task.FromResult(jobId);
        }
    }

    private sealed class ReadyScheduleDependencyEvaluator : IReportingRunReadinessDependencyEvaluator
    {
        public Task<IReadOnlyList<ReportingRunReadinessCheckDto>> EvaluateAsync(
            ReportingRunRequestDto request,
            ReportingTemplateMetadata template,
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext? accessContext,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<ReportingRunReadinessCheckDto> checks =
            [
                new ReportingRunReadinessCheckDto(
                    "month-end-close",
                    "Month-end close evidence",
                    ReportingRunReadinessStatusDto.Ready,
                    "Exact ledger, close, and evidence dependencies are ready.",
                    0,
                    BlocksDraft: false,
                    BlocksFinal: false,
                    EvidenceReferences: ["close-evidence:tenant-a:company-a:2026-07"])
            ];
            return Task.FromResult(checks);
        }
    }

    private sealed class ExactScheduleAuthoritativeSource : IReportingAuthoritativeSource
    {
        public ReportingRunParametersDto? LastParameters { get; private set; }
        public ReportAccessQueryContext? LastAccessContext { get; private set; }

        public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastParameters = parameters;
            LastAccessContext = accessContext;
            var checkpointId = "ledger-checkpoint-month-end-a";
            var checkpointHash = new string('b', 64);
            var checkpoint = new ReportingAuthoritativeSourceCheckpoint(
                "ledger-journal",
                "ledger-book:11111111-1111-1111-1111-111111111111",
                accessContext.TenantId!,
                "organization-a",
                accessContext.CompanyId,
                parameters.Scope.FundProfileId,
                parameters.LedgerBook.LedgerBookId!.Value.ToString("D"),
                parameters.PeriodId,
                parameters.AccountingBasis.ToString(),
                parameters.AsOfDate,
                new DateTimeOffset(parameters.AsOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero),
                42,
                0,
                0,
                checkpointId,
                checkpointHash,
                FixedNow,
                ImmutableArray.Create(
                    $"reporting-source-checkpoint:{checkpointId}:{checkpointHash}"));
            return ValueTask.FromResult(new ReportingAuthoritativeSourceCapture(
                checkpoint,
                ImmutableArray<IReadOnlyDictionary<string, string>>.Empty));
        }
    }

    private sealed class ExactScheduleReconciliationSource : IReportingReconciliationEvidenceSource
    {
        public ValueTask<ReportingReconciliationEvidenceReceipt> ResolveAsync(
            ReportingRunParametersDto parameters,
            ReportingAuthoritativeSourceCheckpoint source,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checkpointId = "reconciliation-month-end-a";
            var checkpointHash = new string('c', 64);
            return ValueTask.FromResult(new ReportingReconciliationEvidenceReceipt(
                source.TenantId,
                source.OrganizationId,
                source.CompanyId,
                source.FundId,
                source.LedgerBookId,
                source.AccountingPeriodId,
                source.AccountingBasis,
                source.AsOfDate,
                source.CheckpointId,
                source.CheckpointHash,
                checkpointId,
                checkpointHash,
                FixedNow,
                HasOpenBreaks: false,
                EvidenceIds: ImmutableArray.Create(
                    $"reconciliation-checkpoint:{checkpointId}:{checkpointHash}")));
        }
    }

    private sealed class RecordingScheduleGovernanceCoordinator(IReportingOrchestrationService orchestration)
        : IReportingGovernanceEndpointCoordinator
    {
        public ReportingGovernanceCallerContext? LastCaller { get; private set; }
        public GovernedReportingRun? CurrentRun { get; private set; }

        public Task<GovernedReportingRun> CreateFromCompletedCertifiedManifestAsync(
            string manifestRunId,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = orchestration.GetManifest(manifestRunId)
                ?? throw new KeyNotFoundException("manifest not found");
            if (!caller.Permissions.HasFlag(UserPermission.ManageReporting))
            {
                throw new ReportingGovernanceAuthorizationException("Manage reporting is required.");
            }

            LastCaller = caller;
            var authority = BuildAuthority(caller);
            CurrentRun = new GovernedReportingRun(
                manifest.RunId,
                manifest.RunSeriesId ?? manifest.RunId,
                1,
                manifest.ResolvedTemplate!.Name,
                manifest.ResolvedTemplate.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                manifest.OperationalScope!,
                manifest.ImmutableAccessScope!,
                manifest.CertifiedSnapshot!,
                authority,
                FixedNow,
                RestatementOfRunId: null,
                ExecutionState: GovernedReportingExecutionState.Succeeded,
                GovernanceState: GovernedReportingState.Draft,
                Version: 3,
                Readiness: null,
                Approval: null,
                Release: null,
                AuditTrail: ImmutableArray<ReportingGovernanceAuditEntry>.Empty);
            return Task.FromResult(CurrentRun!);
        }

        public Task<GovernedReportingRun> GetAsync(
            string runId,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(RequireRun(runId));
        }

        public Task<IReadOnlyList<GovernedReportingRun>> ListAsync(
            string seriesId,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<GovernedReportingRun> result = CurrentRun is not null
                && string.Equals(CurrentRun.SeriesId, seriesId, StringComparison.Ordinal)
                    ? [CurrentRun!]
                    : [];
            return Task.FromResult(result);
        }

        public Task<GovernedReportingRun> ValidateAsync(
            string runId,
            long expectedVersion,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            TransitionAsync(
                runId,
                expectedVersion,
                caller,
                UserPermission.ManageReporting,
                GovernedReportingState.Draft,
                GovernedReportingState.Validated,
                cancellationToken);

        public Task<GovernedReportingRun> SubmitAsync(
            string runId,
            long expectedVersion,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            TransitionAsync(
                runId,
                expectedVersion,
                caller,
                UserPermission.ManageReporting,
                GovernedReportingState.Validated,
                GovernedReportingState.InReview,
                cancellationToken);

        public Task<GovernedReportingRun> ApproveAsync(
            string runId,
            long expectedVersion,
            string decisionNote,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = RequireRun(runId);
            if (string.Equals(run.CreationAuthority.ActorId, caller.ActorId, StringComparison.Ordinal))
            {
                throw new ReportingGovernanceAuthorizationException(
                    "The run preparer cannot approve the same reporting revision.");
            }

            return TransitionAsync(
                runId,
                expectedVersion,
                caller,
                UserPermission.ApproveReporting,
                GovernedReportingState.InReview,
                GovernedReportingState.Approved,
                cancellationToken);
        }

        public Task<GovernedReportingRun> ReleaseAsync(
            string runId,
            long expectedVersion,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            TransitionAsync(
                runId,
                expectedVersion,
                caller,
                UserPermission.DeliverReporting,
                GovernedReportingState.Approved,
                GovernedReportingState.Released,
                cancellationToken);

        public Task<ReportingRestatementRequest> RequestRestatementAsync(
            string predecessorRunId,
            long expectedPredecessorVersion,
            string reason,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ReportingRestatementApprovalResult> ApproveRestatementAsync(
            string requestId,
            long expectedRequestVersion,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private Task<GovernedReportingRun> TransitionAsync(
            string runId,
            long expectedVersion,
            ReportingGovernanceCallerContext caller,
            UserPermission requiredPermission,
            GovernedReportingState requiredState,
            GovernedReportingState targetState,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = RequireRun(runId);
            if (!caller.Permissions.HasFlag(requiredPermission))
            {
                throw new ReportingGovernanceAuthorizationException(
                    $"The caller lacks {requiredPermission}.");
            }
            var hasImmutableAccess = run.Access.Mode switch
            {
                ReportingGovernanceAccessMode.CompanyWide => true,
                ReportingGovernanceAccessMode.Private =>
                    (run.Access.AllowOwnerAccess
                        && string.Equals(run.Access.OwnerPrincipalId, caller.ActorId, StringComparison.OrdinalIgnoreCase))
                    || run.Access.Principals.Any(principal =>
                        principal.Kind == ReportingAccessPrincipalKind.User
                        && caller.Matches(principal)),
                ReportingGovernanceAccessMode.Restricted =>
                    (run.Access.AllowOwnerAccess
                        && string.Equals(run.Access.OwnerPrincipalId, caller.ActorId, StringComparison.OrdinalIgnoreCase))
                    || run.Access.Principals.Any(caller.Matches),
                _ => false
            };
            if (!hasImmutableAccess)
            {
                throw new ReportingGovernanceAuthorizationException(
                    "The caller is outside the immutable reporting audience.");
            }
            if (run.Version != expectedVersion || run.GovernanceState != requiredState)
            {
                throw new ReportingGovernanceConcurrencyException(
                    $"Expected {requiredState} at version {expectedVersion}.");
            }
            if (!string.Equals(run.Scope.TenantId, caller.TenantId, StringComparison.Ordinal)
                || !string.Equals(run.Scope.CompanyId, caller.CompanyId, StringComparison.Ordinal))
            {
                throw new ReportingGovernanceAuthorizationException(
                    "The caller is outside the reporting tenant/company scope.");
            }

            CurrentRun = run with
            {
                GovernanceState = targetState,
                Version = run.Version + 1
            };
            return Task.FromResult(CurrentRun!);
        }

        private GovernedReportingRun RequireRun(string runId) =>
            CurrentRun is not null
            && string.Equals(CurrentRun.RunId, runId, StringComparison.Ordinal)
                ? CurrentRun
                : throw new ReportingGovernanceNotFoundException("Reporting run was not found.");

        private static ReportingAuthorityScope BuildAuthority(ReportingGovernanceCallerContext caller)
        {
            var permissions = ImmutableArray.CreateBuilder<ReportingGovernancePermission>();
            if (caller.Permissions.HasFlag(UserPermission.ManageReporting))
            {
                permissions.Add(ReportingGovernancePermission.CreateRun);
                permissions.Add(ReportingGovernancePermission.ExecuteRun);
                permissions.Add(ReportingGovernancePermission.ValidateRun);
                permissions.Add(ReportingGovernancePermission.SubmitRun);
                permissions.Add(ReportingGovernancePermission.RequestRestatement);
            }
            if (caller.Permissions.HasFlag(UserPermission.ApproveReporting))
            {
                permissions.Add(ReportingGovernancePermission.ApproveRun);
                permissions.Add(ReportingGovernancePermission.ApproveRestatement);
            }
            if (caller.Permissions.HasFlag(UserPermission.DeliverReporting))
            {
                permissions.Add(ReportingGovernancePermission.ReleaseRun);
            }

            return new ReportingAuthorityScope(
                caller.ActorId,
                caller.TenantId,
                "organization-a",
                caller.CompanyId,
                permissions.ToImmutable(),
                caller.Origin,
                caller.CorrelationId,
                caller.PrincipalIds);
        }
    }
}
