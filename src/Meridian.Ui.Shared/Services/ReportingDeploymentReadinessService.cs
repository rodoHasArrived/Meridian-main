using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Strategies.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Evaluates whether the one canonical reporting authority is fully durable and operable.
/// </summary>
public interface IReportingDeploymentReadinessService
{
    ReportingDeploymentCapabilityDto Evaluate();
}

/// <summary>
/// Process-local receipt that the hosted migration prerequisite completed successfully.
/// Merely registering a migration runner is not evidence that migrations ran.
/// </summary>
public sealed class ReportingMigrationReadinessState
{
    private int _ready;

    public bool IsReady => Volatile.Read(ref _ready) == 1;

    internal void MarkReady() => Volatile.Write(ref _ready, 1);

    internal void MarkNotReady() => Volatile.Write(ref _ready, 0);
}

/// <summary>
/// Process-local receipt that the canonical reconciliation queue was fully reloaded and passed its
/// integrity checks during reporting startup.
/// </summary>
public sealed class ReconciliationCaseworkAuthorityReadinessState
{
    private int _ready;

    public bool IsReady => Volatile.Read(ref _ready) == 1;

    internal void MarkReady() => Volatile.Write(ref _ready, 1);

    internal void MarkNotReady() => Volatile.Write(ref _ready, 0);
}

/// <summary>
/// Process-local liveness receipt for the server-owned reporting schedule worker. Readiness is
/// earned only by a successful cycle and is cleared by a cycle-level failure or worker stop.
/// </summary>
public sealed class ReportingScheduleWorkerReadinessState
{
    private int _ready;
    private int _consecutiveFailures;
    private long _lastSuccessfulCycleUtcTicks;

    public bool IsReady => Volatile.Read(ref _ready) == 1;

    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    public DateTimeOffset? LastSuccessfulCycleUtc =>
        ReadTimestamp(ref _lastSuccessfulCycleUtcTicks);

    public bool IsHealthy(DateTimeOffset nowUtc, TimeSpan maximumHeartbeatAge) =>
        WorkerReadinessState.IsHealthy(
            IsReady,
            ConsecutiveFailures,
            LastSuccessfulCycleUtc,
            nowUtc,
            maximumHeartbeatAge);

    internal void MarkReady(DateTimeOffset? completedAtUtc = null) =>
        WorkerReadinessState.MarkReady(
            ref _ready,
            ref _consecutiveFailures,
            ref _lastSuccessfulCycleUtcTicks,
            completedAtUtc);

    internal void MarkCycleFailed()
    {
        Interlocked.Increment(ref _consecutiveFailures);
        Volatile.Write(ref _ready, 0);
    }

    internal void MarkNotReady() => Volatile.Write(ref _ready, 0);

    private static DateTimeOffset? ReadTimestamp(ref long ticks)
    {
        var value = Interlocked.Read(ref ticks);
        return value == 0 ? null : new DateTimeOffset(value, TimeSpan.Zero);
    }
}

/// <summary>
/// Process-local liveness receipt for the server-owned secure reporting distribution worker.
/// Readiness is earned only by a successful cycle and is cleared by a cycle-level failure or stop.
/// </summary>
public sealed class ReportingDeliveryWorkerReadinessState
{
    private int _ready;
    private int _consecutiveFailures;
    private long _lastSuccessfulCycleUtcTicks;

    public bool IsReady => Volatile.Read(ref _ready) == 1;

    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    public DateTimeOffset? LastSuccessfulCycleUtc =>
        ReadTimestamp(ref _lastSuccessfulCycleUtcTicks);

    public bool IsHealthy(DateTimeOffset nowUtc, TimeSpan maximumHeartbeatAge) =>
        WorkerReadinessState.IsHealthy(
            IsReady,
            ConsecutiveFailures,
            LastSuccessfulCycleUtc,
            nowUtc,
            maximumHeartbeatAge);

    internal void MarkReady(DateTimeOffset? completedAtUtc = null) =>
        WorkerReadinessState.MarkReady(
            ref _ready,
            ref _consecutiveFailures,
            ref _lastSuccessfulCycleUtcTicks,
            completedAtUtc);

    internal void MarkCycleFailed()
    {
        Interlocked.Increment(ref _consecutiveFailures);
        Volatile.Write(ref _ready, 0);
    }

    internal void MarkNotReady() => Volatile.Write(ref _ready, 0);

    private static DateTimeOffset? ReadTimestamp(ref long ticks)
    {
        var value = Interlocked.Read(ref ticks);
        return value == 0 ? null : new DateTimeOffset(value, TimeSpan.Zero);
    }
}

internal static class WorkerReadinessState
{
    internal static void MarkReady(
        ref int ready,
        ref int consecutiveFailures,
        ref long lastSuccessfulCycleUtcTicks,
        DateTimeOffset? completedAtUtc)
    {
        var completed = (completedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        Interlocked.Exchange(ref lastSuccessfulCycleUtcTicks, completed.Ticks);
        Volatile.Write(ref consecutiveFailures, 0);
        Volatile.Write(ref ready, 1);
    }

    internal static bool IsHealthy(
        bool isReady,
        int consecutiveFailures,
        DateTimeOffset? lastSuccessfulCycleUtc,
        DateTimeOffset nowUtc,
        TimeSpan maximumHeartbeatAge)
    {
        if (!isReady
            || consecutiveFailures != 0
            || lastSuccessfulCycleUtc is null
            || maximumHeartbeatAge <= TimeSpan.Zero)
        {
            return false;
        }

        var age = nowUtc.ToUniversalTime() - lastSuccessfulCycleUtc.Value;
        return age >= TimeSpan.Zero && age <= maximumHeartbeatAge;
    }
}

/// <summary>
/// Read-only deployment gate shared by the reporting capability endpoint and runtime readiness.
/// The gate deliberately inspects the resolved persistence graph rather than treating a connection
/// string alone as proof that governance, artifacts, schedules, and delivery are durable.
/// </summary>
public sealed class ReportingDeploymentReadinessService(
    IServiceProvider services) : IReportingDeploymentReadinessService
{
    private static readonly IReadOnlyDictionary<string, ReportingDeploymentSchemaRequirement>
        SchemaRequirements =
            new Dictionary<string, ReportingDeploymentSchemaRequirement>(StringComparer.Ordinal)
            {
                ["governance"] = new(
                    [
                        "reporting_governed_runs",
                        "reporting_restatement_requests",
                        "reporting_governance_audit"
                    ],
                    [
                        "trg_reporting_governed_runs_guard",
                        "trg_reporting_restatement_requests_guard",
                        "trg_reporting_governance_audit_append",
                        "trg_reporting_governance_audit_immutable"
                    ],
                    UniqueKeys:
                    [
                        "reporting_governed_runs(tenant_id,run_id)",
                        "reporting_governed_runs(tenant_id,series_id,revision)",
                        "reporting_restatement_requests(tenant_id,request_id)",
                        "reporting_governance_audit(tenant_id,aggregate_kind,aggregate_id,aggregate_version)",
                        "reporting_governance_audit(tenant_id,event_id)"
                    ]),
                ["artifacts"] = new(
                    [
                        "reporting_artifact_blobs",
                        "reporting_artifact_packages",
                        "reporting_artifact_catalog",
                        "reporting_artifact_audit_chain_head",
                        "reporting_artifact_audit"
                    ],
                    [
                        "trg_reporting_artifact_blobs_immutable",
                        "trg_reporting_artifact_audit_append_guard",
                        "trg_reporting_artifact_packages_immutable",
                        "trg_reporting_artifact_catalog_immutable",
                        "trg_reporting_artifact_audit_immutable"
                    ],
                    UniqueKeys:
                    [
                        "reporting_artifact_blobs(tenant_id,content_hash_sha256)",
                        "reporting_artifact_packages(tenant_id,package_id)",
                        "reporting_artifact_catalog(tenant_id,package_id,artifact_id)",
                        "reporting_artifact_audit_chain_head(chain_id)",
                        "reporting_artifact_audit(sequence)",
                        "reporting_artifact_audit(event_id)"
                    ]),
                ["reconciliation-evidence"] = new(
                    [
                        "reporting_reconciliation_evidence",
                        "reporting_reconciliation_evidence_v2"
                    ],
                    [
                        "reporting_reconciliation_evidence_immutable",
                        "reporting_reconciliation_evidence_v2_append"
                    ],
                    UniqueKeys:
                    [
                        "reporting_reconciliation_evidence(tenant_id,receipt_key_sha256)",
                        "reporting_reconciliation_evidence(tenant_id,reconciliation_checkpoint_id,reconciliation_checkpoint_hash)",
                        "reporting_reconciliation_evidence_v2(tenant_id,receipt_key_sha256)",
                        "reporting_reconciliation_evidence_v2(tenant_id,reconciliation_checkpoint_id,reconciliation_checkpoint_hash)"
                    ]),
                ["runs"] = new(
                    [
                        "reporting_run_snapshots",
                        "reporting_run_create_claims"
                    ],
                    [],
                    [
                        "reporting_run_create_claims.tenant_id",
                        "reporting_run_create_claims.run_id",
                        "reporting_run_create_claims.run_id_key",
                        "reporting_run_create_claims.lease_owner",
                        "reporting_run_create_claims.claimed_at_utc",
                        "reporting_run_create_claims.lease_expires_at_utc",
                        "reporting_run_create_claims.lease_version"
                    ],
                    [
                        "reporting_run_snapshots(tenant_id,run_id_key)",
                        "reporting_run_create_claims(tenant_id,run_id_key)"
                    ]),
                ["scheduling"] = new(
                    ["reporting_schedule_snapshots"],
                    [],
                    [
                        "reporting_schedule_snapshots.due_at_utc",
                        "reporting_schedule_snapshots.lease_owner",
                        "reporting_schedule_snapshots.lease_expires_at_utc",
                        "reporting_schedule_snapshots.lease_version"
                    ],
                    ["reporting_schedule_snapshots(tenant_id,company_id,schedule_id_key)"]),
                ["delivery"] = new(
                    [
                        "reporting_access_grants",
                        "reporting_delivery_jobs",
                        "reporting_delivery_receipts"
                    ],
                    [
                        PostgresReportingDeploymentProbe
                            .AccessGrantArtifactConsumptionTriggerName,
                        "trg_reporting_delivery_jobs_guard",
                        "trg_reporting_delivery_receipts_immutable"
                    ],
                    Columns:
                    [
                        "reporting_access_grants.consumed_artifact_ids"
                    ],
                    Constraints:
                    [
                        "reporting_access_grants.ck_reporting_access_grant_consumed_artifacts"
                    ],
                    CompatibilityMarkers:
                    [
                        PostgresReportingDeploymentProbe
                            .AccessGrantArtifactConsumptionCompatibilityMarker
                    ],
                    UniqueKeys:
                    [
                        "reporting_access_grants(grant_id)",
                        "reporting_delivery_jobs(job_id)",
                        "reporting_delivery_jobs(idempotency_key)",
                        "reporting_delivery_jobs(job_id,tenant_id)",
                        "reporting_delivery_jobs(access_grant_id) where access_grant_id IS NOT NULL",
                        "reporting_delivery_receipts(job_id,receipt_id)"
                    ]),
                ["migrations"] = new(
                    ["reporting_schema_migrations"],
                    [],
                    [
                        "reporting_schema_migrations.filename",
                        "reporting_schema_migrations.checksum"
                    ],
                    ["reporting_schema_migrations(filename)"])
            };

    private readonly IServiceProvider _services =
        services ?? throw new ArgumentNullException(nameof(services));

    public ReportingDeploymentCapabilityDto Evaluate()
    {
        var persistenceProbe = Resolve<IReportingDeploymentProbe>()?.Probe();
        var persistenceReady = persistenceProbe?.IsComplete == true;
        var applicationVersionCompatible =
            persistenceProbe?.HasCompatibilityMarker(
                PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionCompatibilityMarker) == true;
        var durableGovernanceStore =
            HasRequiredSchema(persistenceProbe, "governance")
            && IsImplementation<IReportingGovernanceRepository, PostgresReportingGovernanceRepository>()
            && Resolve<ReportingGovernanceService>() is not null
            && Resolve<IReportingGovernanceEndpointCoordinator>() is not null;
        var releaseConsistency =
            IsImplementation<
                IReportingReleaseConsistencyGate,
                PostgresReportingReleaseConsistencyGate>();
        var durableGovernance =
            durableGovernanceStore
            && releaseConsistency;
        var durableArtifacts =
            HasRequiredSchema(persistenceProbe, "artifacts")
            && IsImplementation<IReportingArtifactStore, PostgresReportingArtifactStore>()
            && IsImplementation<IReportingArtifactCatalog, PostgresReportingArtifactCatalog>()
            && IsImplementation<IReportingArtifactAuditStore, PostgresReportingArtifactAuditStore>()
            && Resolve<ReportingArtifactVaultService>() is not null;
        var durableReconciliationEvidenceStore =
            HasRequiredSchema(persistenceProbe, "reconciliation-evidence")
            && IsImplementation<
                IReportingReconciliationEvidenceStore,
                PostgresReportingReconciliationEvidenceStore>()
            && IsImplementation<
                IReportingReconciliationEvidenceRetentionStore,
                PostgresReportingReconciliationEvidenceStore>()
            && Resolve<ReportingReconciliationEvidenceRetentionService>() is not null;
        var breakQueue = Resolve<IReconciliationBreakQueueRepository>();
        var caseworkHandoff =
            Resolve<IStatementReconciliationCaseworkHandoffService>()
                as StatementReconciliationCaseworkHandoffService;
        var operationsBridge =
            Resolve<IOperationsContinuityReconciliationBridge>()
                as OperationsContinuityReconciliationBridge;
        var closeBridge =
            Resolve<IAccountingClosePostingWorkbench>()
                as AccountingClosePostingWorkbenchBridge;
        var finalEvidenceSource =
            Resolve<IReportingReconciliationEvidenceSource>()
                as ReportingReconciliationEvidenceSource;
        var reconciliationCaseworkAuthority =
            breakQueue is IReconciliationBreakQueueAuthorityProbe
            && Resolve<ReconciliationCaseworkAuthorityReadinessState>()?.IsReady == true
            && ReferenceEquals(caseworkHandoff?.BreakQueueAuthority, breakQueue)
            && ReferenceEquals(operationsBridge?.BreakQueueAuthority, breakQueue)
            && ReferenceEquals(closeBridge?.BreakQueueAuthority, breakQueue)
            && ReferenceEquals(finalEvidenceSource?.BreakQueueAuthority, breakQueue);
        var durableReconciliationEvidence =
            durableReconciliationEvidenceStore
            && reconciliationCaseworkAuthority;
        var durableRuns =
            HasRequiredSchema(persistenceProbe, "runs")
            && IsImplementation<IReportingRunStore, PostgresReportingRunStore>();
        var durableScheduling =
            HasRequiredSchema(persistenceProbe, "scheduling")
            && IsImplementation<Meridian.Reporting.IReportingScheduleStore, PostgresReportingScheduleStore>();
        var nowUtc = (Resolve<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        var scheduleWorkerOptions = Resolve<ReportingScheduleWorkerOptions>();
        var schedulingWorkerConfigured =
            scheduleWorkerOptions is not null
            && scheduleWorkerOptions.PollInterval >= TimeSpan.FromMilliseconds(250)
            && scheduleWorkerOptions.PollInterval <= TimeSpan.FromMinutes(5)
            && Resolve<ReportingScheduleWorkerReadinessState>()?.IsHealthy(
                nowUtc,
                WorkerHeartbeatMaximumAge(scheduleWorkerOptions.PollInterval)) == true;
        var distributionApplication =
            Resolve<ReportingSecureDistributionApplicationService>();
        var durableDeliveryStore =
            HasRequiredSchema(persistenceProbe, "delivery")
            && IsImplementation<IReportingAccessGrantStore, PostgresReportingAccessGrantStore>()
            && IsImplementation<IReportingDeliveryStore, PostgresReportingDeliveryStore>()
            && Resolve<IReportingDeliveryStore>()
                is IReportingDeliveryGrantDownloadCommitter
            && distributionApplication is not null;
        var distributionOptions = Resolve<SecureReportingDistributionOptions>();
        var deliveryWorkerConfigured =
            distributionOptions is not null
            && !string.IsNullOrWhiteSpace(distributionOptions.WorkerId)
            && distributionOptions.WorkerPollInterval >= TimeSpan.FromMilliseconds(250)
            && distributionOptions.WorkerPollInterval <= TimeSpan.FromMinutes(5)
            && Resolve<ReportingDeliveryWorkerReadinessState>()?.IsHealthy(
                nowUtc,
                WorkerHeartbeatMaximumAge(distributionOptions.WorkerPollInterval)) == true;
        var destinationResolver = Resolve<IReportingRecipientDestinationResolver>();
        var recipientDestinationsConfigured = destinationResolver?.IsConfigured == true;
        var configuredRecipientTransports = destinationResolver?.ConfiguredTransportIds?
            .Where(static transportId => !string.IsNullOrWhiteSpace(transportId))
            .Select(static transportId => transportId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        var transportCapabilities = Resolve<IReportingTransportInfrastructureReadiness>()?
            .GetTransportInfrastructureCapabilities();
        var recipientTransportInfrastructureReady =
            recipientDestinationsConfigured
            && configuredRecipientTransports.Length > 0
            && configuredRecipientTransports.All(transportId =>
                transportCapabilities?.SingleOrDefault(capability =>
                    string.Equals(
                        capability.TransportId,
                        transportId,
                        StringComparison.OrdinalIgnoreCase))
                    is { IsInfrastructureReady: true });
        var durableDelivery =
            durableDeliveryStore
            && recipientTransportInfrastructureReady;
        var clientDocumentsConfigured =
            IsImplementation<IReportingPrimaryDocumentRenderer, DocumentsReportingPrimaryDocumentRenderer>()
            && Resolve<LedgerClientReportExportService>()?.HasClientGradeRenderer == true
            && IsImplementation<IReportingCertifiedArtifactProducer,
                DeterministicReportingCertifiedArtifactProducer>()
            && Resolve<IReportingCertifiedLedgerPresentationSource>()
                is ServiceProviderReportingAuthoritativeSource { IsConfigured: true };
        var migrationsManaged =
            persistenceReady
            && Resolve<ReportingMigrationRunner>() is not null
            && Resolve<ReportingMigrationReadinessState>()?.IsReady == true;

        var components = new[]
        {
            Component(
                "governance",
                "Governance",
                durableGovernanceStore,
                "PostgreSQL governed-run lifecycle and maker-checker audit are configured.",
                "Durable PostgreSQL reporting governance is not configured."),
            Component(
                "release-consistency",
                "Release consistency",
                releaseConsistency,
                "Final release uses the PostgreSQL accounting-period consistency gate.",
                "The PostgreSQL final-release/accounting-period consistency gate is not configured."),
            Component(
                "artifacts",
                "Artifact vault",
                durableArtifacts,
                "PostgreSQL artifact bytes, catalog, and access audit are configured.",
                "Durable reporting artifact bytes, catalog, and audit are not fully configured."),
            Component(
                "reconciliation-evidence",
                "Reconciliation evidence",
                durableReconciliationEvidenceStore,
                "PostgreSQL close and reconciliation evidence, including immutable controls, is configured.",
                "Durable close and reconciliation evidence or its immutable database controls are not fully configured."),
            Component(
                "reconciliation-casework",
                "Reconciliation casework authority",
                reconciliationCaseworkAuthority,
                "The canonical reconciliation queue, casework handoff, Operations bridge, and final-certification evidence source are configured.",
                "The reconciliation casework authority required by hard close and final reporting is not fully configured."),
            Component(
                "runs",
                "Run history",
                durableRuns,
                "PostgreSQL certified run manifests and run audit are configured.",
                "Certified reporting runs are not backed by the PostgreSQL authority."),
            Component(
                "scheduling",
                "Scheduling",
                durableScheduling,
                "PostgreSQL reporting schedules are configured.",
                "Reporting schedules are not backed by the PostgreSQL authority."),
            Component(
                "scheduling-worker",
                "Scheduling worker",
                schedulingWorkerConfigured,
                "The server-owned reporting schedule worker is running with a positive poll interval.",
                "The server-owned reporting schedule worker is missing, stopped, or has invalid configuration."),
            Component(
                "delivery",
                "Delivery",
                durableDelivery,
                "PostgreSQL access grants, delivery jobs, immutable receipts, atomic download accounting, and every recipient transport are configured.",
                ResolveDeliveryBlocker(
                    persistenceProbe,
                    applicationVersionCompatible,
                    durableDeliveryStore,
                    recipientTransportInfrastructureReady)),
            Component(
                "application-schema-compatibility",
                "Application/schema compatibility",
                applicationVersionCompatible,
                "This application and the PostgreSQL reporting schema agree on migration 012 access-grant artifact consumption.",
                ResolveApplicationCompatibilityBlocker(persistenceProbe)),
            Component(
                "delivery-worker",
                "Delivery worker",
                deliveryWorkerConfigured,
                "The server-owned secure distribution worker is running with valid worker options.",
                "The server-owned secure distribution worker is missing, stopped, or has invalid configuration."),
            Component(
                "recipient-destinations",
                "Recipient destinations",
                recipientDestinationsConfigured,
                "Exact-scope reporting recipient destinations are configured.",
                "No exact-scope reporting recipient destination directory is configured."),
            Component(
                "recipient-transports",
                "Recipient transport infrastructure",
                recipientTransportInfrastructureReady,
                "Every transport referenced by the recipient directory is infrastructure-ready.",
                "One or more recipient-directory transports has no ready delivery adapter or required infrastructure."),
            Component(
                "client-documents",
                "Client documents",
                clientDocumentsConfigured,
                "The canonical client PDF/XLSX renderer and durable ledger presentation source are configured.",
                "The canonical client-document renderer or durable ledger presentation source is not configured."),
            Component(
                "migrations",
                "Reporting migrations",
                migrationsManaged,
                "Checksummed reporting migrations are managed as a startup prerequisite.",
                ResolveMigrationBlocker(persistenceProbe))
        };
        var blockers = components
            .Where(static component => !component.IsReady)
            .Select(static component => component.Summary)
            .ToArray();

        return new ReportingDeploymentCapabilityDto(
            IsReady: blockers.Length == 0,
            DurableGovernance: durableGovernance,
            DurableArtifacts: durableArtifacts,
            DurableReconciliationEvidence: durableReconciliationEvidence,
            DurableRuns: durableRuns,
            DurableScheduling: durableScheduling,
            DurableDelivery: durableDelivery,
            RecipientDestinationsConfigured: recipientDestinationsConfigured,
            ClientDocumentsConfigured: clientDocumentsConfigured,
            MigrationsManaged: migrationsManaged,
            Components: components,
            BlockingReasons: blockers);
    }

    private static ReportingDeploymentComponentDto Component(
        string id,
        string displayName,
        bool isReady,
        string readySummary,
        string blockedSummary) =>
        new(id, displayName, isReady, isReady ? readySummary : blockedSummary);

    private static string ResolveApplicationCompatibilityBlocker(
        ReportingDeploymentProbeResult? probe)
    {
        if (probe?.FailureCode
            == PostgresReportingDeploymentProbe.BinaryMigrationAssetUnavailableFailureCode)
        {
            return "This application cannot verify migration 012 because its deployed migration asset is unavailable; delivery remains blocked.";
        }

        if (probe?.FailureCode
            == PostgresReportingDeploymentProbe.SchemaIncompleteFailureCode)
        {
            return "Application/schema compatibility cannot be verified because the PostgreSQL reporting schema or migration ledger is incomplete; delivery remains blocked.";
        }

        return probe is { IsReachable: false }
            ? "Application/schema compatibility cannot be verified while the PostgreSQL reporting authority is unreachable."
            : "This application version is incompatible with the PostgreSQL reporting schema: the exact migration 012 access-grant artifact-consumption marker is absent or mismatched; delivery remains blocked.";
    }

    private static string ResolveDeliveryBlocker(
        ReportingDeploymentProbeResult? probe,
        bool applicationVersionCompatible,
        bool durableDeliveryStore,
        bool recipientTransportInfrastructureReady)
    {
        if (!durableDeliveryStore)
        {
            if (applicationVersionCompatible)
            {
                return "Durable reporting distribution, receipts, or atomic download accounting are not fully configured.";
            }

            if (probe?.FailureCode
                == PostgresReportingDeploymentProbe.BinaryMigrationAssetUnavailableFailureCode)
            {
                return "Delivery is blocked because this application cannot verify its migration 012 compatibility asset.";
            }

            if (probe?.FailureCode
                == PostgresReportingDeploymentProbe.SchemaIncompleteFailureCode)
            {
                return "Delivery is blocked because the PostgreSQL reporting schema or migration ledger is incomplete.";
            }

            return probe is { IsReachable: false }
                ? "Delivery is blocked because application/schema compatibility cannot be verified while the PostgreSQL reporting authority is unreachable."
                : "Delivery is blocked because the PostgreSQL access-grant schema is incompatible with this application version.";
        }

        return recipientTransportInfrastructureReady
            ? "Durable reporting delivery is not fully configured."
            : "Delivery is blocked because one or more recipient-directory transports has no ready adapter or required infrastructure.";
    }

    private static TimeSpan WorkerHeartbeatMaximumAge(TimeSpan pollInterval) =>
        TimeSpan.FromTicks(checked((pollInterval.Ticks * 3) + TimeSpan.FromSeconds(10).Ticks));

    private static string ResolveMigrationBlocker(
        ReportingDeploymentProbeResult? probe)
    {
        if (probe?.FailureCode
            == PostgresReportingDeploymentProbe.BinaryMigrationAssetUnavailableFailureCode)
        {
            return "The deployed application is missing the migration 012 asset required to verify database compatibility.";
        }

        if (probe?.FailureCode
            == PostgresReportingDeploymentProbe.SchemaIncompleteFailureCode)
        {
            return "The PostgreSQL reporting schema or migration ledger is incomplete.";
        }

        if (probe is { IsReachable: false })
        {
            return "The PostgreSQL reporting authority is unreachable.";
        }

        if (probe is null
            || !probe.HasCompatibilityMarker(
                PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionCompatibilityMarker))
        {
            return "The PostgreSQL reporting schema is incompatible with this application version because migration 012's exact access-grant artifact-consumption marker is absent or mismatched.";
        }

        if (probe.MissingTriggers.Count > 0)
        {
            return "The PostgreSQL reporting schema is missing required immutable-control triggers.";
        }

        if (probe.MissingConstraints.Count > 0)
        {
            return "The PostgreSQL reporting schema is missing required database constraints.";
        }

        if (probe.MissingColumns.Count > 0)
        {
            return "The PostgreSQL reporting schema is missing required operational-authority columns.";
        }

        if (probe.MissingUniqueKeys.Count > 0)
        {
            return "The PostgreSQL reporting schema is missing required durable identity keys.";
        }

        return probe.MissingTables.Count > 0
            ? "The PostgreSQL reporting schema is incomplete."
            : "Reporting migrations have not completed for this process.";
    }

    internal static bool HasRequiredSchema(
        ReportingDeploymentProbeResult? probe,
        string componentId)
    {
        if (probe is null
            || !probe.IsReachable
            || !SchemaRequirements.TryGetValue(componentId, out var requirement))
        {
            return false;
        }

        return requirement.Tables.All(probe.HasTable)
               && requirement.Triggers.All(probe.HasTrigger)
               && (requirement.Columns?.All(column =>
               {
                   var separator = column.IndexOf('.');
                   return separator > 0
                          && probe.HasColumn(
                              column[..separator],
                              column[(separator + 1)..]);
               }) ?? true)
               && (requirement.UniqueKeys?.All(probe.HasUniqueKey) ?? true)
               && (requirement.Constraints?.All(probe.HasConstraint) ?? true)
               && (requirement.CompatibilityMarkers?.All(probe.HasCompatibilityMarker) ?? true);
    }

    private T? Resolve<T>()
        where T : class
    {
        try
        {
            return _services.GetService<T>();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private bool IsImplementation<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
        => Resolve<TService>() is TImplementation;

    private sealed record ReportingDeploymentSchemaRequirement(
        IReadOnlyList<string> Tables,
        IReadOnlyList<string> Triggers,
        IReadOnlyList<string>? Columns = null,
        IReadOnlyList<string>? UniqueKeys = null,
        IReadOnlyList<string>? Constraints = null,
        IReadOnlyList<string>? CompatibilityMarkers = null);
}
