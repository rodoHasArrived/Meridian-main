using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
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
                        "trg_reporting_access_grants_guard",
                        "trg_reporting_delivery_jobs_guard",
                        "trg_reporting_delivery_receipts_immutable"
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
        var durableGovernance =
            HasRequiredSchema(persistenceProbe, "governance")
            && IsImplementation<IReportingGovernanceRepository, PostgresReportingGovernanceRepository>()
            && Resolve<ReportingGovernanceService>() is not null
            && Resolve<IReportingGovernanceEndpointCoordinator>() is not null;
        var durableArtifacts =
            HasRequiredSchema(persistenceProbe, "artifacts")
            && IsImplementation<IReportingArtifactStore, PostgresReportingArtifactStore>()
            && IsImplementation<IReportingArtifactCatalog, PostgresReportingArtifactCatalog>()
            && IsImplementation<IReportingArtifactAuditStore, PostgresReportingArtifactAuditStore>()
            && Resolve<ReportingArtifactVaultService>() is not null;
        var durableReconciliationEvidence =
            HasRequiredSchema(persistenceProbe, "reconciliation-evidence")
            && IsImplementation<
                IReportingReconciliationEvidenceStore,
                PostgresReportingReconciliationEvidenceStore>()
            && IsImplementation<
                IReportingReconciliationEvidenceRetentionStore,
                PostgresReportingReconciliationEvidenceStore>()
            && Resolve<ReportingReconciliationEvidenceRetentionService>() is not null;
        var durableRuns =
            HasRequiredSchema(persistenceProbe, "runs")
            && IsImplementation<IReportingRunStore, PostgresReportingRunStore>();
        var durableScheduling =
            HasRequiredSchema(persistenceProbe, "scheduling")
            && IsImplementation<Meridian.Reporting.IReportingScheduleStore, PostgresReportingScheduleStore>();
        var durableDelivery =
            HasRequiredSchema(persistenceProbe, "delivery")
            && IsImplementation<IReportingAccessGrantStore, PostgresReportingAccessGrantStore>()
            && IsImplementation<IReportingDeliveryStore, PostgresReportingDeliveryStore>()
            && Resolve<ReportingSecureDistributionApplicationService>() is not null;
        var recipientDestinationsConfigured =
            Resolve<IReportingRecipientDestinationResolver>()?.IsConfigured == true;
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
                durableGovernance,
                "PostgreSQL governed-run lifecycle and maker-checker audit are configured.",
                "Durable PostgreSQL reporting governance is not configured."),
            Component(
                "artifacts",
                "Artifact vault",
                durableArtifacts,
                "PostgreSQL artifact bytes, catalog, and access audit are configured.",
                "Durable reporting artifact bytes, catalog, and audit are not fully configured."),
            Component(
                "reconciliation-evidence",
                "Reconciliation evidence",
                durableReconciliationEvidence,
                "PostgreSQL close and reconciliation evidence, including immutable controls, is configured.",
                "Durable close and reconciliation evidence or its immutable database controls are not fully configured."),
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
                "delivery",
                "Delivery",
                durableDelivery,
                "PostgreSQL access grants, delivery jobs, and immutable receipts are configured.",
                "Durable reporting distribution and receipts are not fully configured."),
            Component(
                "recipient-destinations",
                "Recipient destinations",
                recipientDestinationsConfigured,
                "Exact-scope reporting recipient destinations are configured.",
                "No exact-scope reporting recipient destination directory is configured."),
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
                persistenceProbe is { IsReachable: false }
                    ? "The PostgreSQL reporting authority is unreachable."
                    : persistenceProbe is { MissingTriggers.Count: > 0 }
                        ? "The PostgreSQL reporting schema is missing required immutable-control triggers."
                        : persistenceProbe is { MissingColumns.Count: > 0 }
                            ? "The PostgreSQL reporting schema is missing required operational-authority columns."
                            : persistenceProbe is { MissingUniqueKeys.Count: > 0 }
                                ? "The PostgreSQL reporting schema is missing required durable identity keys."
                                : persistenceProbe is { MissingTables.Count: > 0 }
                                    ? "The PostgreSQL reporting schema is incomplete."
                                    : "Reporting migrations have not completed for this process.")
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
               && (requirement.UniqueKeys?.All(probe.HasUniqueKey) ?? true);
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
        IReadOnlyList<string>? UniqueKeys = null);
}
