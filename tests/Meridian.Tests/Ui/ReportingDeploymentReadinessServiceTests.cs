using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Evidence;
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
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_documents")]
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_document_revisions")]
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
    [InlineData(
        "statement-reconciliation-authority",
        PostgresReportingDeploymentProbe.StatementDocumentGuardTriggerName)]
    [InlineData(
        "statement-reconciliation-authority",
        PostgresReportingDeploymentProbe.StatementDocumentTruncateGuardTriggerName)]
    [InlineData(
        "statement-reconciliation-authority",
        PostgresReportingDeploymentProbe.StatementDocumentRevisionTriggerName)]
    [InlineData(
        "statement-reconciliation-authority",
        PostgresReportingDeploymentProbe.StatementRevisionAppendTriggerName)]
    [InlineData(
        "statement-reconciliation-authority",
        PostgresReportingDeploymentProbe.StatementRevisionGuardTriggerName)]
    [InlineData(
        "statement-reconciliation-authority",
        PostgresReportingDeploymentProbe.StatementRevisionTruncateGuardTriggerName)]
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
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_documents.document_version")]
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_document_revisions.previous_content_hash_sha256")]
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
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_documents(tenant_id,company_id,workflow_id,document_key)")]
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_document_revisions(tenant_id,company_id,workflow_id,document_key,document_version)")]
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

    [Theory]
    [InlineData(
        "delivery",
        "reporting_access_grants.ck_reporting_access_grant_consumed_artifacts")]
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_documents.fk_reporting_statement_document_blob")]
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_documents.ck_reporting_statement_document_key")]
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_documents.ck_reporting_statement_document_identity_utf8_bytes")]
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_document_revisions.fk_reporting_statement_revision_blob")]
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_document_revisions.fk_reporting_statement_revision_previous_blob")]
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_document_revisions.ck_reporting_statement_revision_chain")]
    [InlineData(
        "statement-reconciliation-authority",
        "reporting_statement_reconciliation_document_revisions.ck_reporting_statement_revision_identity_utf8_bytes")]
    public void HasRequiredSchema_MissingAuthorityConstraint_ShouldFailClosed(
        string componentId,
        string missingConstraint)
    {
        var probe = CompleteProbe() with
        {
            MissingConstraints = [missingConstraint]
        };

        probe.IsComplete.Should().BeFalse();
        ReportingDeploymentReadinessService.HasRequiredSchema(probe, componentId)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(
        "delivery",
        PostgresReportingDeploymentProbe.AccessGrantArtifactConsumptionCompatibilityMarker)]
    [InlineData(
        "statement-reconciliation-authority",
        PostgresReportingDeploymentProbe.StatementReconciliationAuthorityCompatibilityMarker)]
    public void HasRequiredSchema_MissingApplicationCompatibilityMarker_ShouldFailClosed(
        string componentId,
        string missingMarker)
    {
        var complete = CompleteProbe();
        var probe = CompleteProbe() with
        {
            MissingCompatibilityMarkers = [missingMarker],
            VerifiedCompatibilityMarkers = complete.VerifiedCompatibilityMarkers
                .Where(marker =>
                    !string.Equals(marker, missingMarker, StringComparison.Ordinal))
                .ToArray()
        };

        probe.IsComplete.Should().BeFalse();
        ReportingDeploymentReadinessService.HasRequiredSchema(probe, componentId)
            .Should().BeFalse();
    }

    [Fact]
    public void Evaluate_StatementAuthorityRequiresMarkerStoreAndDurablyComposedWorkflow()
    {
        var probe = Substitute.For<IReportingDeploymentProbe>();
        probe.Probe().Returns(CompleteProbe());
        var options = new ReportingArtifactStoreOptions
        {
            ConnectionString =
                "Host=localhost;Database=meridian_readiness;Username=meridian",
            Schema = "reporting"
        };
        var authority = new PostgresStatementReconciliationReportAuthorityStore(
            options,
            new PostgresReportingArtifactStore(options));
        var evidence = new ReportingStatementImportEvidenceRetainer(
            authority,
            Path.GetTempPath());
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton<IStatementReconciliationReportAuthorityStore>(authority);
        services.AddSingleton<IStatementImportEvidenceRetainer>(evidence);
        services.AddSingleton(CreateDurableStatementWorkflow(authority, evidence));
        using var provider = services.BuildServiceProvider();
        var readiness = new ReportingDeploymentReadinessService(provider);

        readiness.Evaluate().Components
            .Single(static component =>
                component.ComponentId == "statement-reconciliation-authority")
            .IsReady.Should().BeTrue();

        var complete = CompleteProbe();
        probe.Probe().Returns(complete with
        {
            MissingCompatibilityMarkers =
            [
                PostgresReportingDeploymentProbe
                    .StatementReconciliationAuthorityCompatibilityMarker
            ],
            VerifiedCompatibilityMarkers = complete.VerifiedCompatibilityMarkers
                .Where(marker =>
                    !string.Equals(
                        marker,
                        PostgresReportingDeploymentProbe
                            .StatementReconciliationAuthorityCompatibilityMarker,
                        StringComparison.Ordinal))
                .ToArray()
        });

        readiness.Evaluate().Components
            .Single(static component =>
                component.ComponentId == "statement-reconciliation-authority")
            .IsReady.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_PostgresStatementStoreWithoutDurablyComposedWorkflow_ShouldFailClosed()
    {
        var probe = Substitute.For<IReportingDeploymentProbe>();
        probe.Probe().Returns(CompleteProbe());
        var options = new ReportingArtifactStoreOptions
        {
            ConnectionString =
                "Host=localhost;Database=meridian_readiness;Username=meridian",
            Schema = "reporting"
        };
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton<IStatementReconciliationReportAuthorityStore>(
            new PostgresStatementReconciliationReportAuthorityStore(
                options,
                new PostgresReportingArtifactStore(options)));
        using var provider = services.BuildServiceProvider();

        var capability = new ReportingDeploymentReadinessService(provider).Evaluate();

        capability.Components
            .Single(static component =>
                component.ComponentId == "statement-reconciliation-authority")
            .IsReady.Should().BeFalse(
                "a durable store registration alone does not prove the workflow retains canonical run evidence through that exact authority");
    }

    [Fact]
    public void Evaluate_WorkflowUsingDifferentDurableAuthorityInstance_ShouldFailClosed()
    {
        var probe = Substitute.For<IReportingDeploymentProbe>();
        probe.Probe().Returns(CompleteProbe());
        var options = new ReportingArtifactStoreOptions
        {
            ConnectionString =
                "Host=localhost;Database=meridian_readiness;Username=meridian",
            Schema = "reporting"
        };
        var registeredAuthority = new PostgresStatementReconciliationReportAuthorityStore(
            options,
            new PostgresReportingArtifactStore(options));
        var workflowAuthority = new PostgresStatementReconciliationReportAuthorityStore(
            options,
            new PostgresReportingArtifactStore(options));
        var workflowEvidence = new ReportingStatementImportEvidenceRetainer(
            workflowAuthority,
            Path.GetTempPath());
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddSingleton<IStatementReconciliationReportAuthorityStore>(
            registeredAuthority);
        services.AddSingleton<IStatementImportEvidenceRetainer>(workflowEvidence);
        services.AddSingleton(
            CreateDurableStatementWorkflow(workflowAuthority, workflowEvidence));
        using var provider = services.BuildServiceProvider();

        var capability = new ReportingDeploymentReadinessService(provider).Evaluate();

        capability.Components
            .Single(static component =>
                component.ComponentId == "statement-reconciliation-authority")
            .IsReady.Should().BeFalse(
                "the probed store and workflow must share the exact authority instance");
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
            .Summary.Should()
            .Contain("one or more exact required migration capability markers")
            .And.Contain("reporting chain remains blocked")
            .And.NotContain("migration 012");
        capability.Components
            .Single(static component => component.ComponentId == "delivery")
            .Summary.Should().Contain("access-grant schema")
            .And.Contain("incompatible with this application version");
        capability.Components
            .Single(static component => component.ComponentId == "migrations")
            .Summary.Should()
            .Contain("one or more exact required migration capability markers")
            .And.Contain("absent or mismatched")
            .And.NotContain("migration 012");
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
                    .AccessGrantArtifactConsumptionCompatibilityMarker,
                PostgresReportingDeploymentProbe
                    .StatementReconciliationAuthorityCompatibilityMarker
            ]
        });
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        using var provider = services.BuildServiceProvider();

        var capability = new ReportingDeploymentReadinessService(provider).Evaluate();

        capability.Components
            .Single(static component => component.ComponentId == "migrations")
            .Summary.Should().Be(
                "The deployed application is missing one or more reporting migration assets required to verify database compatibility.");
        capability.Components
            .Single(static component =>
                component.ComponentId == "application-schema-compatibility")
            .Summary.Should()
            .Contain("one or more required reporting migrations")
            .And.Contain("a deployed migration asset is unavailable")
            .And.NotContain("migration 012")
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
    [InlineData("statement-reconciliation-authority")]
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
        var allowRecovery = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<ReportingScheduleWorkerBatchResult> RunDueAsync(
            DateTimeOffset _,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                failed.TrySetResult();
                throw new InvalidOperationException("simulated schedule-store outage");
            }

            await allowRecovery.Task.WaitAsync(cancellationToken);
            recovered.TrySetResult();
            return new ReportingScheduleWorkerBatchResult(default!, []);
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

            allowRecovery.TrySetResult();
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
    public async Task ScheduleWorker_FirstCycleDoesNotDependOnItsOwnHeartbeat()
    {
        var deploymentReadiness = Substitute.For<IReportingDeploymentReadinessService>();
        deploymentReadiness.Evaluate().Returns(new ReportingDeploymentCapabilityDto(
            IsReady: false,
            DurableGovernance: true,
            DurableArtifacts: true,
            DurableReconciliationEvidence: true,
            DurableRuns: true,
            DurableScheduling: true,
            DurableDelivery: true,
            RecipientDestinationsConfigured: true,
            ClientDocumentsConfigured: true,
            MigrationsManaged: true,
            Components:
            [
                new ReportingDeploymentComponentDto(
                    "scheduling-worker",
                    "Scheduling worker",
                    IsReady: false,
                    "The scheduling worker has not completed its first successful cycle.")
            ],
            BlockingReasons:
            [
                "The scheduling worker has not completed its first successful cycle."
            ]));
        deploymentReadiness.GetScheduleWorkerCycleBlockingReasons().Returns([]);
        var service = new ReportingScheduleService(
            Substitute.For<IReportingOrchestrationService>(),
            (Meridian.Reporting.IReportingScheduleStore?)null,
            deliveryService: null,
            governedTemplateCatalog: null,
            datasetSourceService: null,
            readinessService: null,
            certificationService: null,
            governanceCoordinator: null,
            destinationResolver: null,
            deploymentReadinessService: deploymentReadiness);

        var workerCycle = await service.RunDueForWorkerAsync(
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        workerCycle.Result.Runs.Should().BeEmpty();
        var publicCycle = async () =>
            await service.RunDueAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        await publicCycle.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*scheduling worker has not completed*");
    }

    [Fact]
    public async Task ScheduleWorkerCycleBlockers_InitialDeliveryBootstrapExemptsOnlyPeerLiveness()
    {
        var deliveryReadiness = new ReportingDeliveryWorkerReadinessState();
        var capability = WorkerBootstrapCapability(deliveryReady: false);
        var firstCycle = new TaskCompletionSource<ReportingScheduledHandoffBridgeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduleService = new ReportingScheduleService(
            Substitute.For<IReportingOrchestrationService>(),
            (Meridian.Reporting.IReportingScheduleStore?)null);
        using var worker = new ReportingSecureDistributionHostedService(
            scheduleService,
            static (_, _, _) => Task.FromResult("unused-job"),
            NullLogger<ReportingSecureDistributionHostedService>.Instance,
            readiness: deliveryReadiness,
            enqueueReleasedHandoffsAsync: _ => firstCycle.Task);

        deliveryReadiness.IsInitialStartInProgress.Should().BeFalse();
        ReportingDeploymentReadinessService
            .ResolveScheduleWorkerCycleBlockingReasons(capability)
            .Should().ContainSingle()
            .Which.Should().Be(DeliveryWorkerBlockedSummary);

        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(startup.Token);
        try
        {
            deliveryReadiness.IsInitialStartInProgress.Should().BeTrue(
                "StartAsync must expose the bootstrap window before the scheduler starts");
            ReportingDeploymentReadinessService
                .ResolveScheduleWorkerCycleBlockingReasons(
                    capability,
                    allowDeliveryWorkerInitialBootstrap:
                        deliveryReadiness.IsInitialStartInProgress)
                .Should().BeEmpty();
        }
        finally
        {
            firstCycle.TrySetResult(new ReportingScheduledHandoffBridgeResult(
                Attempted: 0,
                Enqueued: 0,
                AwaitingRelease: 0,
                Failed: 0,
                NextCursor: null));
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(shutdown.Token);
        }

        deliveryReadiness.IsInitialStartInProgress.Should().BeFalse();
    }

    [Fact]
    public void ScheduleWorkerCycleBlockers_StaleOrFailedDeliveryAfterStartRemainsBlocked()
    {
        var completedAt = new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.Zero);
        var staleReadiness = new ReportingDeliveryWorkerReadinessState();
        staleReadiness.MarkStarting();
        staleReadiness.MarkReady(completedAt);
        var staleCapability = WorkerBootstrapCapability(
            deliveryReady: staleReadiness.IsHealthy(
                completedAt.AddMinutes(4),
                TimeSpan.FromMinutes(3)));

        staleReadiness.IsInitialStartInProgress.Should().BeFalse();
        ReportingDeploymentReadinessService
            .ResolveScheduleWorkerCycleBlockingReasons(
                staleCapability,
                allowDeliveryWorkerInitialBootstrap:
                    staleReadiness.IsInitialStartInProgress)
            .Should().ContainSingle()
            .Which.Should().Be(DeliveryWorkerBlockedSummary);

        var failedReadiness = new ReportingDeliveryWorkerReadinessState();
        failedReadiness.MarkStarting();
        failedReadiness.MarkCycleFailed();
        failedReadiness.MarkStarting();

        failedReadiness.IsInitialStartInProgress.Should().BeFalse(
            "a worker restart must not recreate its one-time bootstrap exemption");
        ReportingDeploymentReadinessService
            .ResolveScheduleWorkerCycleBlockingReasons(
                WorkerBootstrapCapability(deliveryReady: false),
                allowDeliveryWorkerInitialBootstrap:
                    failedReadiness.IsInitialStartInProgress)
            .Should().ContainSingle()
            .Which.Should().Be(DeliveryWorkerBlockedSummary);
    }

    [Fact]
    public void ScheduleWorkerCycleBlockers_MissingDeliveryComponentFailsClosed()
    {
        var deliveryReadiness = new ReportingDeliveryWorkerReadinessState();
        deliveryReadiness.MarkStarting();

        ReportingDeploymentReadinessService
            .ResolveScheduleWorkerCycleBlockingReasons(
                WorkerBootstrapCapability(
                    deliveryReady: false,
                    includeDeliveryComponent: false),
                allowDeliveryWorkerInitialBootstrap:
                    deliveryReadiness.IsInitialStartInProgress)
            .Should().ContainSingle()
            .Which.Should().Be(
                "Reporting deployment readiness omitted the delivery-worker component.");
    }

    [Fact]
    public void ScheduleWorkerCycleBlockers_PreserveUnrepresentedAndInconsistentFailures()
    {
        var schedulingSummary =
            "The scheduling worker has not completed its first successful cycle.";
        var capability = new ReportingDeploymentCapabilityDto(
            IsReady: false,
            DurableGovernance: false,
            DurableArtifacts: true,
            DurableReconciliationEvidence: true,
            DurableRuns: true,
            DurableScheduling: true,
            DurableDelivery: true,
            RecipientDestinationsConfigured: true,
            ClientDocumentsConfigured: true,
            MigrationsManaged: true,
            Components:
            [
                new ReportingDeploymentComponentDto(
                    "scheduling-worker",
                    "Scheduling worker",
                    IsReady: false,
                    schedulingSummary),
                new ReportingDeploymentComponentDto(
                    "governance",
                    "Governance",
                    IsReady: false,
                    "Governance component is not ready."),
                new ReportingDeploymentComponentDto(
                    "delivery-worker",
                    "Delivery worker",
                    IsReady: true,
                    "The secure distribution worker is ready.")
            ],
            BlockingReasons:
            [
                schedulingSummary,
                "Durable PostgreSQL reporting governance is not configured."
            ]);

        ReportingDeploymentReadinessService
            .ResolveCapabilityBlockingReasons(capability)
            .Should().HaveCount(3);
        ReportingDeploymentReadinessService
            .ResolveScheduleWorkerCycleBlockingReasons(capability)
            .Should().BeEquivalentTo(
                "Durable PostgreSQL reporting governance is not configured.",
                "Governance component is not ready.");

        var inconsistent = capability with
        {
            Components = [],
            BlockingReasons = []
        };

        ReportingDeploymentReadinessService
            .ResolveCapabilityBlockingReasons(inconsistent)
            .Should().ContainSingle()
            .Which.Should().Contain("without a reason");
    }

    [Fact]
    public async Task ScheduleService_DurableStoreWithoutDeploymentGateFailsClosed()
    {
        var store = Substitute.For<Meridian.Reporting.IReportingScheduleStore>();
        store.IsDurableAuthority.Returns(true);
        store.Load().Returns([]);
        var service = new ReportingScheduleService(
            Substitute.For<IReportingOrchestrationService>(),
            store);

        var workerCycle = async () =>
            await service.RunDueForWorkerAsync(
                DateTimeOffset.UtcNow,
                CancellationToken.None);

        await workerCycle.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*durable reporting schedule authority requires*readiness gate*");
    }

    [Fact]
    public async Task DeliveryWorker_FirstCycleFailureStaysUnreadyAndLaterSuccessRecovers()
    {
        var readiness = new ReportingDeliveryWorkerReadinessState();
        var failed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRecovery = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task DispatchAsync(string _, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                failed.TrySetResult();
                throw new InvalidOperationException("simulated delivery-store outage");
            }

            await allowRecovery.Task.WaitAsync(cancellationToken);
            recovered.TrySetResult();
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

            allowRecovery.TrySetResult();
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
    public void DeliveryWorker_HandoffFailuresBlockReadinessButAwaitingReleaseDoesNot()
    {
        var awaitingRelease = new ReportingScheduledHandoffBridgeResult(
            Attempted: 1,
            Enqueued: 0,
            AwaitingRelease: 1,
            Failed: 0,
            NextCursor: null);
        var operationalFailure = awaitingRelease with
        {
            AwaitingRelease = 0,
            Failed = 1
        };

        var expectedWait = () =>
            ReportingSecureDistributionHostedService.EnsureHandoffCycleHealthy(
                awaitingRelease);
        var failedCycle = () =>
            ReportingSecureDistributionHostedService.EnsureHandoffCycleHealthy(
                operationalFailure);

        expectedWait.Should().NotThrow(
            "independent approval and release is an expected pending state");
        failedCycle.Should().Throw<ReportingScheduledHandoffCycleException>()
            .WithMessage("*could not persist or queue 1 scheduled handoff*");
    }

    [Fact]
    public async Task DeliveryWorker_HandoffFailureDoesNotStarveGrantReconciliationOrDispatch()
    {
        var readiness = new ReportingDeliveryWorkerReadinessState();
        var reconciled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatched = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduleService = new ReportingScheduleService(
            Substitute.For<IReportingOrchestrationService>(),
            (Meridian.Reporting.IReportingScheduleStore?)null);
        using var worker = new ReportingSecureDistributionHostedService(
            scheduleService,
            static (_, _, _) => Task.FromResult("unused-job"),
            NullLogger<ReportingSecureDistributionHostedService>.Instance,
            readiness: readiness,
            dispatchDueAsync: (_, _) =>
            {
                dispatched.TrySetResult();
                return Task.CompletedTask;
            },
            reconcileFailedGrantsAsync: _ =>
            {
                reconciled.TrySetResult();
                return Task.FromResult(0);
            },
            options: SecureReportingDistributionOptions.Default with
            {
                WorkerPollInterval = TimeSpan.FromMinutes(1)
            },
            enqueueReleasedHandoffsAsync: _ => Task.FromResult(
                new ReportingScheduledHandoffBridgeResult(
                    Attempted: 1,
                    Enqueued: 0,
                    AwaitingRelease: 0,
                    Failed: 1,
                    NextCursor: null)));
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(startup.Token);
        try
        {
            await Task.WhenAll(
                reconciled.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            await WaitUntilAsync(() => readiness.ConsecutiveFailures == 1);
            readiness.IsReady.Should().BeFalse();
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(shutdown.Token);
        }
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
                .AccessGrantArtifactConsumptionCompatibilityMarker,
            PostgresReportingDeploymentProbe
                .StatementReconciliationAuthorityCompatibilityMarker
        ]
    };

    private const string SchedulingWorkerBlockedSummary =
        "The server-owned reporting schedule worker is missing, stopped, stale, failed its latest cycle, or has invalid configuration.";

    private const string DeliveryWorkerBlockedSummary =
        "The server-owned secure distribution worker is missing, stopped, stale, failed its latest cycle, or has invalid configuration.";

    private static ReportingDeploymentCapabilityDto WorkerBootstrapCapability(
        bool deliveryReady,
        bool includeDeliveryComponent = true)
    {
        var components = new List<ReportingDeploymentComponentDto>
        {
            new(
                "scheduling-worker",
                "Scheduling worker",
                IsReady: false,
                SchedulingWorkerBlockedSummary)
        };
        var blockers = new List<string> { SchedulingWorkerBlockedSummary };
        if (includeDeliveryComponent)
        {
            components.Add(new ReportingDeploymentComponentDto(
                "delivery-worker",
                "Delivery worker",
                deliveryReady,
                deliveryReady
                    ? "The secure distribution worker is ready."
                    : DeliveryWorkerBlockedSummary));
            if (!deliveryReady)
            {
                blockers.Add(DeliveryWorkerBlockedSummary);
            }
        }

        return new ReportingDeploymentCapabilityDto(
            IsReady: false,
            DurableGovernance: true,
            DurableArtifacts: true,
            DurableReconciliationEvidence: true,
            DurableRuns: true,
            DurableScheduling: true,
            DurableDelivery: true,
            RecipientDestinationsConfigured: true,
            ClientDocumentsConfigured: true,
            MigrationsManaged: true,
            Components: components,
            BlockingReasons: blockers);
    }

    private static StatementReconciliationReportWorkflowService CreateDurableStatementWorkflow(
        IStatementReconciliationReportAuthorityStore authority,
        ReportingStatementImportEvidenceRetainer evidence) =>
        new(
            Substitute.For<IStatementImportCommitService>(),
            evidence,
            Substitute.For<IStatementRunWorkflowService>(),
            Path.GetTempPath(),
            authority,
            NullLogger<StatementReconciliationReportWorkflowService>.Instance,
            breakQueue: null,
            intakeAuthority: Substitute.For<IStatementReconciliationIntakeAuthority>());

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }
}
