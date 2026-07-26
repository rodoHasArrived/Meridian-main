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
                    ]),
                ["reconciliation-evidence"] = new(
                    [
                        "reporting_reconciliation_evidence",
                        "reporting_reconciliation_evidence_v2"
                    ],
                    [
                        "reporting_reconciliation_evidence_immutable",
                        "reporting_reconciliation_evidence_v2_append"
                    ]),
                ["runs"] = new(["reporting_run_snapshots"], []),
                ["scheduling"] = new(["reporting_schedule_snapshots"], []),
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
                    ]),
                ["migrations"] = new(["reporting_schema_migrations"], [])
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
               && requirement.Triggers.All(probe.HasTrigger);
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
        IReadOnlyList<string> Triggers);
}
