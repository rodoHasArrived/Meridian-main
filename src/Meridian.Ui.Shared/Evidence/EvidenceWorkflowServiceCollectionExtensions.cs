using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Evidence;

public static class EvidenceWorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddEvidenceWorkflowFabric(this IServiceCollection services)
    {
        services.TryAddSingleton<EvidenceTemplateRegistry>();
        services.TryAddSingleton<EvidenceSubjectResolver>();
        services.TryAddSingleton<EvidencePacketValidationService>();
        services.TryAddSingleton<EvidenceGraphService>();
        services.TryAddSingleton<IEvidenceDocumentExtractor, ManualEvidenceDocumentExtractor>();
        services.TryAddSingleton<IEvidenceArtifactStore>(sp =>
            new FileEvidenceArtifactStore(
                FileEvidenceArtifactStore.ResolveDataRoot(sp),
                sp.GetRequiredService<ILogger<FileEvidenceArtifactStore>>()));
        services.TryAddSingleton(sp => new StatementImportEvidenceBridge(
            sp.GetRequiredService<IEvidenceArtifactStore>(),
            FileEvidenceArtifactStore.ResolveDataRoot(sp)));
        services.TryAddSingleton<IStatementImportEvidenceRetainer>(sp =>
            sp.GetRequiredService<StatementImportEvidenceBridge>());
        services.TryAddSingleton(sp => new StatementReconciliationReportWorkflowService(
            sp.GetRequiredService<Meridian.FinancialOperations.Reconciliation.Connectors.IStatementImportCommitService>(),
            sp.GetRequiredService<IStatementImportEvidenceRetainer>(),
            sp.GetRequiredService<Meridian.FinancialOperations.Reconciliation.IStatementRunWorkflowService>(),
            FileEvidenceArtifactStore.ResolveDataRoot(sp),
            sp.GetService<ILogger<StatementReconciliationReportWorkflowService>>()));
#pragma warning disable CS0618 // Preserve injection compatibility while the operation is renamed.
        services.TryAddSingleton(sp => new StatementToReportWorkflowService(
            sp.GetRequiredService<StatementReconciliationReportWorkflowService>()));
#pragma warning restore CS0618

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, StrategyRunEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, TradingReadinessEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, ReconciliationEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, ReportPackEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, ReportPackDeliveryEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, ProviderTrustEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, ExportEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, SecurityMasterConflictEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, OperationsApprovalEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, PrivateCapitalFundEventEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, PaymentIntentEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, EvidenceVaultEvidenceContributor>());
        return services;
    }
}
