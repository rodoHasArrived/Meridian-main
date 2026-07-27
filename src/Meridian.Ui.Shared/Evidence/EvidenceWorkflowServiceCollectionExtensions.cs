using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Evidence;

public static class EvidenceWorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddEvidenceWorkflowFabric(
        this IServiceCollection services,
        bool isProductionComposition = false)
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
        var hasKnownDurableStatementAuthority =
            HasKnownDurableStatementAuthority(services);

        if (!isProductionComposition)
        {
            services.TryAddSingleton<IStatementReconciliationReportAuthorityStore>(sp =>
                new FileStatementReconciliationReportAuthorityStore(
                    FileEvidenceArtifactStore.ResolveDataRoot(sp)));
            services.TryAddSingleton(sp => new StatementImportEvidenceBridge(
                sp.GetRequiredService<IEvidenceArtifactStore>(),
                FileEvidenceArtifactStore.ResolveDataRoot(sp)));
        }

        if (!isProductionComposition
            || hasKnownDurableStatementAuthority)
        {
            services.TryAddSingleton<IStatementImportEvidenceRetainer>(sp =>
                ResolveStatementImportEvidenceRetainer(
                    sp,
                    isProductionComposition));
            services.TryAddSingleton(sp => new StatementReconciliationReportWorkflowService(
                sp.GetRequiredService<Meridian.FinancialOperations.Reconciliation.Connectors.IStatementImportCommitService>(),
                sp.GetRequiredService<IStatementImportEvidenceRetainer>(),
                sp.GetRequiredService<Meridian.FinancialOperations.Reconciliation.IStatementRunWorkflowService>(),
                FileEvidenceArtifactStore.ResolveDataRoot(sp),
                isProductionComposition
                    ? RequireDurableStatementAuthority(sp)
                    : sp.GetRequiredService<IStatementReconciliationReportAuthorityStore>(),
                sp.GetService<ILogger<StatementReconciliationReportWorkflowService>>(),
                sp.GetService<Meridian.Strategies.Services.IReconciliationBreakQueueRepository>(),
                sp.GetRequiredService<Meridian.Ui.Shared.Services.IStatementReconciliationIntakeAuthority>()));
            services.TryAddSingleton<IStatementFetchIngestionAuthority>(sp =>
                new StatementReconciliationReportFetchIngestionAuthority(
                    sp.GetRequiredService<StatementReconciliationReportWorkflowService>(),
                    sp.GetRequiredService<IStatementReconciliationIntakeAuthority>()));
#pragma warning disable CS0618 // Preserve injection compatibility while the operation is renamed.
            services.TryAddSingleton(sp => new StatementToReportWorkflowService(
                sp.GetRequiredService<StatementReconciliationReportWorkflowService>()));
#pragma warning restore CS0618
        }

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

    private static IStatementImportEvidenceRetainer ResolveStatementImportEvidenceRetainer(
        IServiceProvider serviceProvider,
        bool isProductionComposition)
    {
        var authority =
            serviceProvider.GetRequiredService<IStatementReconciliationReportAuthorityStore>();
        if (authority.IsDurableAuthority)
        {
            return new ReportingStatementImportEvidenceRetainer(
                authority,
                FileEvidenceArtifactStore.ResolveDataRoot(serviceProvider));
        }

        if (isProductionComposition)
        {
            throw new InvalidOperationException(
                "Production statement reconciliation composition requires a shared durable authority.");
        }

        return serviceProvider.GetRequiredService<StatementImportEvidenceBridge>();
    }

    private static IStatementReconciliationReportAuthorityStore RequireDurableStatementAuthority(
        IServiceProvider serviceProvider)
    {
        var authority = serviceProvider
            .GetRequiredService<IStatementReconciliationReportAuthorityStore>();
        if (!authority.IsDurableAuthority)
        {
            throw new InvalidOperationException(
                "Production statement reconciliation composition requires a shared durable authority.");
        }

        return authority;
    }

    private static bool HasKnownDurableStatementAuthority(IServiceCollection services) =>
        services.Any(static descriptor =>
            descriptor.ServiceType
            == typeof(DurableStatementReconciliationAuthorityRegistration));
}

/// <summary>
/// Internal composition marker added only when Workstation owns the production PostgreSQL
/// statement-authority registration. An arbitrary interface descriptor is not evidence of
/// durability.
/// </summary>
internal sealed class DurableStatementReconciliationAuthorityRegistration;
