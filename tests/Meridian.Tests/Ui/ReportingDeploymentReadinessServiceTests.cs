using FluentAssertions;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class ReportingDeploymentReadinessServiceTests
{
    [Theory]
    [InlineData("governance", "reporting_restatement_requests")]
    [InlineData("artifacts", "reporting_artifact_packages")]
    [InlineData("reconciliation-evidence", "reporting_reconciliation_evidence_v2")]
    [InlineData("runs", "reporting_run_snapshots")]
    [InlineData("scheduling", "reporting_schedule_snapshots")]
    [InlineData("delivery", "reporting_delivery_receipts")]
    [InlineData("migrations", "reporting_schema_migrations")]
    public void HasRequiredSchema_MissingComponentTable_ShouldFailClosed(
        string componentId,
        string missingTable)
    {
        var probe = CompleteProbe() with
        {
            MissingTables = [missingTable]
        };

        ReportingDeploymentReadinessService.HasRequiredSchema(probe, componentId)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("governance", "trg_reporting_governance_audit_immutable")]
    [InlineData("artifacts", "trg_reporting_artifact_audit_append_guard")]
    [InlineData("reconciliation-evidence", "reporting_reconciliation_evidence_v2_append")]
    [InlineData(
        "delivery",
        PostgresReportingDeploymentProbe.AccessGrantArtifactConsumptionTriggerName)]
    [InlineData("delivery", "trg_reporting_delivery_receipts_immutable")]
    public void HasRequiredSchema_MissingImmutableControlTrigger_ShouldFailClosed(
        string componentId,
        string missingTrigger)
    {
        var probe = CompleteProbe() with
        {
            MissingTriggers = [missingTrigger]
        };

        probe.IsComplete.Should().BeFalse();
        ReportingDeploymentReadinessService.HasRequiredSchema(probe, componentId)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("runs", "reporting_run_create_claims.tenant_id")]
    [InlineData("runs", "reporting_run_create_claims.claimed_at_utc")]
    [InlineData("runs", "reporting_run_create_claims.lease_version")]
    [InlineData("scheduling", "reporting_schedule_snapshots.due_at_utc")]
    [InlineData("scheduling", "reporting_schedule_snapshots.lease_owner")]
    [InlineData("scheduling", "reporting_schedule_snapshots.lease_expires_at_utc")]
    [InlineData("scheduling", "reporting_schedule_snapshots.lease_version")]
    [InlineData("delivery", "reporting_access_grants.consumed_artifact_ids")]
    public void HasRequiredSchema_MissingOperationalAuthorityColumn_ShouldFailClosed(
        string componentId,
        string missingColumn)
    {
        var probe = CompleteProbe() with
        {
            MissingColumns = [missingColumn]
        };

        probe.IsComplete.Should().BeFalse();
        ReportingDeploymentReadinessService.HasRequiredSchema(probe, componentId)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("governance", "reporting_governed_runs(tenant_id,run_id)")]
    [InlineData("artifacts", "reporting_artifact_blobs(tenant_id,content_hash_sha256)")]
    [InlineData("reconciliation-evidence", "reporting_reconciliation_evidence_v2(tenant_id,receipt_key_sha256)")]
    [InlineData("runs", "reporting_run_snapshots(tenant_id,run_id_key)")]
    [InlineData("runs", "reporting_run_create_claims(tenant_id,run_id_key)")]
    [InlineData("scheduling", "reporting_schedule_snapshots(tenant_id,company_id,schedule_id_key)")]
    [InlineData("delivery", "reporting_delivery_jobs(idempotency_key)")]
    [InlineData("delivery", "reporting_delivery_jobs(access_grant_id) where access_grant_id IS NOT NULL")]
    [InlineData("migrations", "reporting_schema_migrations(filename)")]
    public void HasRequiredSchema_MissingUniqueAuthorityKey_ShouldFailClosed(
        string componentId,
        string missingUniqueKey)
    {
        var probe = CompleteProbe() with
        {
            MissingUniqueKeys = [missingUniqueKey]
        };

        probe.IsComplete.Should().BeFalse();
        ReportingDeploymentReadinessService.HasRequiredSchema(probe, componentId)
            .Should().BeFalse();
    }

    [Fact]
    public void HasRequiredSchema_MissingConsumedArtifactConstraint_ShouldFailClosed()
    {
        var probe = CompleteProbe() with
        {
            MissingConstraints =
            [
                "reporting_access_grants.ck_reporting_access_grant_consumed_artifacts"
            ]
        };

        probe.IsComplete.Should().BeFalse();
        ReportingDeploymentReadinessService.HasRequiredSchema(probe, "delivery")
            .Should().BeFalse();
    }

    [Fact]
    public void HasRequiredSchema_MissingApplicationCompatibilityMarker_ShouldBlockDelivery()
    {
        var probe = CompleteProbe() with
        {
            MissingCompatibilityMarkers =
            [
                PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionCompatibilityMarker
            ],
            VerifiedCompatibilityMarkers = []
        };

        probe.IsComplete.Should().BeFalse();
        ReportingDeploymentReadinessService.HasRequiredSchema(probe, "delivery")
            .Should().BeFalse();
    }

    [Fact]
    public void Evaluate_MissingConstraint_ShouldReportConstraintAuthorityFailure()
    {
        var probe = Substitute.For<IReportingDeploymentProbe>();
        probe.Probe().Returns(CompleteProbe() with
        {
            MissingConstraints =
            [
                "reporting_access_grants.ck_reporting_access_grant_consumed_artifacts"
            ]
        });
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        using var provider = services.BuildServiceProvider();

        var capability = new ReportingDeploymentReadinessService(provider).Evaluate();

        capability.Components
            .Single(static component => component.ComponentId == "migrations")
            .Summary.Should().Be(
                "The PostgreSQL reporting schema is missing required database constraints.");
    }

    [Fact]
    public void Evaluate_MissingApplicationCompatibilityMarker_ShouldReportVersionIncompatibility()
    {
        var probe = Substitute.For<IReportingDeploymentProbe>();
        probe.Probe().Returns(CompleteProbe() with
        {
            MissingCompatibilityMarkers =
            [
                PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionCompatibilityMarker
            ],
            VerifiedCompatibilityMarkers = []
        });
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        using var provider = services.BuildServiceProvider();

        var capability = new ReportingDeploymentReadinessService(provider).Evaluate();

        capability.DurableDelivery.Should().BeFalse();
        capability.IsReady.Should().BeFalse();
        capability.Components
            .Single(static component =>
                component.ComponentId == "application-schema-compatibility")
            .Summary.Should().Contain("incompatible")
            .And.Contain("migration 012")
            .And.Contain("delivery remains blocked");
        capability.Components
            .Single(static component => component.ComponentId == "delivery")
            .Summary.Should().Contain("incompatible with this application version");
        capability.Components
            .Single(static component => component.ComponentId == "migrations")
            .Summary.Should().Contain("migration 012's exact")
            .And.Contain("absent or mismatched");
    }

    [Fact]
    public void Evaluate_MissingBinaryMigrationAsset_ShouldNotClaimDatabaseIsUnreachable()
    {
        var probe = Substitute.For<IReportingDeploymentProbe>();
        probe.Probe().Returns(new ReportingDeploymentProbeResult(
            IsReachable: false,
            MissingTables: [],
            MissingTriggers: [],
            FailureCode:
                PostgresReportingDeploymentProbe
                    .BinaryMigrationAssetUnavailableFailureCode)
        {
            MissingCompatibilityMarkers =
            [
                PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionCompatibilityMarker
            ]
        });
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        using var provider = services.BuildServiceProvider();

        var capability = new ReportingDeploymentReadinessService(provider).Evaluate();

        capability.Components
            .Single(static component => component.ComponentId == "migrations")
            .Summary.Should().Be(
                "The deployed application is missing the migration 012 asset required to verify database compatibility.");
        capability.Components
            .Single(static component =>
                component.ComponentId == "application-schema-compatibility")
            .Summary.Should().Contain("migration asset is unavailable")
            .And.NotContain("PostgreSQL reporting authority is unreachable");
        capability.Components
            .Single(static component => component.ComponentId == "delivery")
            .Summary.Should().Contain("cannot verify its migration 012 compatibility asset")
            .And.NotContain("PostgreSQL reporting authority is unreachable");
    }

    [Fact]
    public void Evaluate_MissingMigrationLedger_ShouldReportReachableIncompleteSchema()
    {
        var probe = Substitute.For<IReportingDeploymentProbe>();
        probe.Probe().Returns(
            PostgresReportingDeploymentProbe.CreateFailureResult(
                isReachable: true,
                PostgresReportingDeploymentProbe.SchemaIncompleteFailureCode));
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        using var provider = services.BuildServiceProvider();

        var capability = new ReportingDeploymentReadinessService(provider).Evaluate();

        capability.Components
            .Single(static component => component.ComponentId == "migrations")
            .Summary.Should().Be(
                "The PostgreSQL reporting schema or migration ledger is incomplete.");
        capability.Components
            .Single(static component =>
                component.ComponentId == "application-schema-compatibility")
            .Summary.Should().Contain("schema or migration ledger is incomplete")
            .And.NotContain("authority is unreachable");
    }

    [Theory]
    [InlineData("governance")]
    [InlineData("artifacts")]
    [InlineData("reconciliation-evidence")]
    [InlineData("runs")]
    [InlineData("scheduling")]
    [InlineData("delivery")]
    [InlineData("migrations")]
    public void HasRequiredSchema_CompleteAuthority_ShouldPassComponent(string componentId)
    {
        ReportingDeploymentReadinessService.HasRequiredSchema(CompleteProbe(), componentId)
            .Should().BeTrue();
    }

    [Fact]
    public void DeliveryAndSchedulingWorkerReadiness_AreIndependent()
    {
        var schedule = new ReportingScheduleWorkerReadinessState();
        var delivery = new ReportingDeliveryWorkerReadinessState();
        var services = new ServiceCollection();
        services.AddSingleton(schedule);
        services.AddSingleton(delivery);
        services.AddSingleton(ReportingScheduleWorkerOptions.Default);
        services.AddSingleton(SecureReportingDistributionOptions.Default with
        {
            WorkerId = "delivery-worker-test",
            WorkerPollInterval = TimeSpan.FromSeconds(1)
        });
        using var provider = services.BuildServiceProvider();
        var readiness = new ReportingDeploymentReadinessService(provider);

        schedule.MarkReady();
        var scheduleOnly = readiness.Evaluate();

        scheduleOnly.Components
            .Single(static component => component.ComponentId == "scheduling-worker")
            .IsReady.Should().BeTrue();
        scheduleOnly.Components
            .Single(static component => component.ComponentId == "delivery-worker")
            .IsReady.Should().BeFalse();

        schedule.MarkNotReady();
        delivery.MarkReady();
        var deliveryOnly = readiness.Evaluate();

        deliveryOnly.Components
            .Single(static component => component.ComponentId == "scheduling-worker")
            .IsReady.Should().BeFalse();
        deliveryOnly.Components
            .Single(static component => component.ComponentId == "delivery-worker")
            .IsReady.Should().BeTrue();
    }

    [Fact]
    public void WorkerReadiness_RequiresRecentSuccessfulCycleAndClearsOnFailure()
    {
        var completedAt = new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);
        var schedule = new ReportingScheduleWorkerReadinessState();
        var delivery = new ReportingDeliveryWorkerReadinessState();

        schedule.MarkReady(completedAt);
        delivery.MarkReady(completedAt);

        schedule.IsHealthy(completedAt.AddMinutes(2), TimeSpan.FromMinutes(3))
            .Should().BeTrue();
        delivery.IsHealthy(completedAt.AddMinutes(2), TimeSpan.FromMinutes(3))
            .Should().BeTrue();
        schedule.IsHealthy(completedAt.AddMinutes(4), TimeSpan.FromMinutes(3))
            .Should().BeFalse("a stale worker heartbeat is not deployment readiness");
        delivery.IsHealthy(completedAt.AddMinutes(4), TimeSpan.FromMinutes(3))
            .Should().BeFalse("a stale worker heartbeat is not deployment readiness");

        schedule.MarkCycleFailed();
        delivery.MarkCycleFailed();

        schedule.IsReady.Should().BeFalse();
        delivery.IsReady.Should().BeFalse();
        schedule.ConsecutiveFailures.Should().Be(1);
        delivery.ConsecutiveFailures.Should().Be(1);

        schedule.MarkReady(completedAt.AddMinutes(5));
        delivery.MarkReady(completedAt.AddMinutes(5));

        schedule.ConsecutiveFailures.Should().Be(0);
        delivery.ConsecutiveFailures.Should().Be(0);
        schedule.IsHealthy(completedAt.AddMinutes(6), TimeSpan.FromMinutes(3))
            .Should().BeTrue();
        delivery.IsHealthy(completedAt.AddMinutes(6), TimeSpan.FromMinutes(3))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ScheduleWorker_FirstCycleFailureStaysUnreadyAndLaterSuccessRecovers()
    {
        var readiness = new ReportingScheduleWorkerReadinessState();
        var failed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task<ReportingScheduleWorkerBatchResult> RunDueAsync(
            DateTimeOffset _,
            CancellationToken __)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                failed.TrySetResult();
                throw new InvalidOperationException("simulated schedule-store outage");
            }

            recovered.TrySetResult();
            return Task.FromResult(new ReportingScheduleWorkerBatchResult(default!, []));
        }

        using var worker = new ReportingScheduleHostedService(
            RunDueAsync,
            TimeProvider.System,
            NullLogger<ReportingScheduleHostedService>.Instance,
            new ReportingScheduleWorkerOptions(TimeSpan.FromMilliseconds(10)),
            readiness);
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(startup.Token);
        try
        {
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => readiness.ConsecutiveFailures == 1);
            readiness.IsReady.Should().BeFalse(
                "entering the worker loop is not proof of one successful schedule cycle");

            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => readiness.IsReady);
            readiness.ConsecutiveFailures.Should().Be(0);
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(shutdown.Token);
        }

        readiness.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task DeliveryWorker_FirstCycleFailureStaysUnreadyAndLaterSuccessRecovers()
    {
        var readiness = new ReportingDeliveryWorkerReadinessState();
        var failed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task DispatchAsync(string _, CancellationToken __)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                failed.TrySetResult();
                throw new InvalidOperationException("simulated delivery-store outage");
            }

            recovered.TrySetResult();
            return Task.CompletedTask;
        }

        var scheduleService = new ReportingScheduleService(
            Substitute.For<IReportingOrchestrationService>(),
            (Meridian.Reporting.IReportingScheduleStore?)null);
        using var worker = new ReportingSecureDistributionHostedService(
            scheduleService,
            static (_, _, _) => Task.FromResult("unused-job"),
            NullLogger<ReportingSecureDistributionHostedService>.Instance,
            readiness: readiness,
            dispatchDueAsync: DispatchAsync,
            options: SecureReportingDistributionOptions.Default with
            {
                WorkerPollInterval = TimeSpan.FromMilliseconds(10)
            });
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(startup.Token);
        try
        {
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => readiness.ConsecutiveFailures == 1);
            readiness.IsReady.Should().BeFalse(
                "entering the worker loop is not proof of one successful delivery cycle");

            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => readiness.IsReady);
            readiness.ConsecutiveFailures.Should().Be(0);
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(shutdown.Token);
        }

        readiness.IsReady.Should().BeFalse();
    }

    [Fact]
    public void RecipientTransportInfrastructure_MustCoverEveryConfiguredBinding()
    {
        var resolver = Substitute.For<IReportingRecipientDestinationResolver>();
        resolver.IsConfigured.Returns(true);
        resolver.ConfiguredTransportIds.Returns(["http-relay"]);
        var transportReadiness = Substitute.For<IReportingTransportInfrastructureReadiness>();
        transportReadiness.GetTransportInfrastructureCapabilities().Returns(
        [
            new SecureReportingTransportCapability(
                "http-relay",
                "HTTP notification relay",
                "ExternalNotification",
                IsExternal: true,
                RequiresDestination: false,
                UsesGovernedRecipientScope: true,
                IssuesAccessGrant: true,
                SupportsProviderReceipts: true,
                IsConfigured: false,
                IsInfrastructureReady: false,
                InfrastructureDisabledReasonCode: "ADAPTER_NOT_CONFIGURED",
                IsReady: false,
                DisabledReasonCode: "ADAPTER_NOT_CONFIGURED")
        ]);
        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        services.AddSingleton(transportReadiness);
        using var provider = services.BuildServiceProvider();
        var readiness = new ReportingDeploymentReadinessService(provider);

        var unavailable = readiness.Evaluate();

        unavailable.Components
            .Single(static component => component.ComponentId == "recipient-transports")
            .IsReady.Should().BeFalse();
        unavailable.DurableDelivery.Should().BeFalse();

        transportReadiness.GetTransportInfrastructureCapabilities().Returns(
        [
            new SecureReportingTransportCapability(
                "http-relay",
                "HTTP notification relay",
                "ExternalNotification",
                IsExternal: true,
                RequiresDestination: false,
                UsesGovernedRecipientScope: true,
                IssuesAccessGrant: true,
                SupportsProviderReceipts: true,
                IsConfigured: true,
                IsInfrastructureReady: true,
                InfrastructureDisabledReasonCode: null,
                IsReady: true,
                DisabledReasonCode: null)
        ]);

        readiness.Evaluate().Components
            .Single(static component => component.ComponentId == "recipient-transports")
            .IsReady.Should().BeTrue();
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("delivery-worker-test", 0.1)]
    [InlineData("delivery-worker-test", 301)]
    public void DeliveryWorkerReadiness_InvalidOptionsFailClosed(
        string workerId,
        double pollSeconds)
    {
        var delivery = new ReportingDeliveryWorkerReadinessState();
        delivery.MarkReady();
        var services = new ServiceCollection();
        services.AddSingleton(delivery);
        services.AddSingleton(SecureReportingDistributionOptions.Default with
        {
            WorkerId = workerId,
            WorkerPollInterval = TimeSpan.FromSeconds(pollSeconds)
        });
        using var provider = services.BuildServiceProvider();

        var capability = new ReportingDeploymentReadinessService(provider).Evaluate();

        capability.Components
            .Single(static component => component.ComponentId == "delivery-worker")
            .IsReady.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_FileBackedRunAndScheduleStores_ShouldFailDurabilityBoundary()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Meridian.Tests",
            "reporting-readiness",
            Guid.NewGuid().ToString("N"));

        try
        {
            var probe = Substitute.For<IReportingDeploymentProbe>();
            probe.Probe().Returns(CompleteProbe());
            var services = new ServiceCollection();
            services.AddSingleton(probe);
            services.AddSingleton<IReportingRunStore>(
                new FileReportingRunStore(
                    new ReportingRunStoreOptions(Path.Combine(root, "runs")),
                    NullLogger<FileReportingRunStore>.Instance));
            services.AddSingleton<Meridian.Reporting.IReportingScheduleStore>(
                new FileReportingScheduleStore(
                    new ReportingScheduleStoreOptions(Path.Combine(root, "schedules.json")),
                    NullLogger<FileReportingScheduleStore>.Instance));
            using var provider = services.BuildServiceProvider();

            var capability = new ReportingDeploymentReadinessService(provider).Evaluate();

            capability.DurableRuns.Should().BeFalse(
                "a file-backed run store is not the canonical PostgreSQL reporting authority");
            capability.DurableScheduling.Should().BeFalse(
                "a file-backed schedule store is not the canonical PostgreSQL reporting authority");
            capability.IsReady.Should().BeFalse();
            capability.Components
                .Single(static component => component.ComponentId == "runs")
                .IsReady.Should().BeFalse();
            capability.Components
                .Single(static component => component.ComponentId == "scheduling")
                .IsReady.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ReportingDeploymentProbeResult CompleteProbe() => new(
        IsReachable: true,
        MissingTables: [],
        MissingTriggers: [],
        FailureCode: null)
    {
        VerifiedCompatibilityMarkers =
        [
            PostgresReportingDeploymentProbe
                .AccessGrantArtifactConsumptionCompatibilityMarker
        ]
    };

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }
}
