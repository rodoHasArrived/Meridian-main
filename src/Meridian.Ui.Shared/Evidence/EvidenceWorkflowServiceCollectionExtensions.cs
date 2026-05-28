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
        services.TryAddSingleton<IEvidenceArtifactStore>(sp =>
            new FileEvidenceArtifactStore(
                FileEvidenceArtifactStore.ResolveDataRoot(sp),
                sp.GetRequiredService<ILogger<FileEvidenceArtifactStore>>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, StrategyRunEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, TradingReadinessEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, ReconciliationEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, ReportPackEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, ProviderTrustEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, ExportEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, SecurityMasterConflictEvidenceContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, OperationsApprovalEvidenceContributor>());
        return services;
    }
}
