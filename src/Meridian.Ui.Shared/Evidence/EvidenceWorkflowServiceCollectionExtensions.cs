using Meridian.Application.Composition;
using Meridian.Documents;
using Microsoft.Extensions.Options;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Contracts.Operations;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>
/// Registers evidence artifact storage and the services that assemble workflow evidence.
/// </summary>
public static class EvidenceWorkflowServiceCollectionExtensions
{
    /// <summary>Registers the shared evidence store and host-configured storage limits for browser and desktop hosts.</summary>
    public static IServiceCollection AddEvidenceArtifactStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Explicit per-host composition settings take precedence over the generic host
        // configuration. Direct consumers without either retain the default limits.
        services.AddOptions<EvidenceStorageQuotaOptions>()
            .Configure<IServiceProvider>((options, provider) =>
                (provider.GetService<CompositionConfiguration>()?.HostConfiguration
                    ?? provider.GetService<IConfiguration>())?
                .GetSection("EvidenceVault:StorageQuota").Bind(options));
        services.TryAddSingleton<IEvidenceArtifactStore>(sp =>
            new FileEvidenceArtifactStore(
                FileEvidenceArtifactStore.ResolveDataRoot(sp),
                sp.GetRequiredService<ILogger<FileEvidenceArtifactStore>>(),
                sp.GetRequiredService<IOptions<EvidenceStorageQuotaOptions>>().Value));
        services.TryAddSingleton<EvidenceAttestationSubjectResolver>();
        services.TryAddSingleton<ContractualCashFlowDraftAttestationSubjectResolver>();
        services.TryAddSingleton<PreparerAttestationEvidenceProjectionService>();
        services.TryAddSingleton(sp => new SinglePrincipalAttestationStore(
            FileEvidenceArtifactStore.ResolveDataRoot(sp),
            async (scope, authority, request, ct) =>
            {
                if (request.ActionId == SinglePrincipalAttestationPolicy.DraftCheck)
                {
                    if (scope.LedgerBookId is not null || !authority.CanModifyDraftTarget
                        || request.SubjectKind != ContractualCashFlowDraftAttestationSubjectResolver.SubjectKind
                        || string.IsNullOrWhiteSpace(request.SubjectId))
                        return null;
                    var parts = request.SubjectId.Split('/');
                    if (parts is not ["Security", var security, var run]
                        || !Guid.TryParseExact(security, "D", out var securityId)
                        || !Guid.TryParseExact(run, "D", out var runId))
                        return null;
                    return await sp.GetRequiredService<ContractualCashFlowDraftAttestationSubjectResolver>()
                        .ResolveAsync(securityId, runId, scope.TenantId, scope.CompanyId, authority.Actor, ct)
                        .ConfigureAwait(false);
                }
                if (scope.LedgerBookId is not null || !authority.CanModifyTarget
                    || request.ActionId != SinglePrincipalAttestationPolicy.EvidenceAcknowledgment
                    || request.SubjectKind != EvidenceAttestationSubjectResolver.SubjectKind)
                    return null;
                var segments = request.SubjectId.Split('/');
                if (segments.Length != 2 || segments.Any(string.IsNullOrWhiteSpace))
                    return null;
                var subject = await sp.GetRequiredService<EvidenceAttestationSubjectResolver>()
                    .ResolveAsync(segments[0], segments[1], scope.TenantId, scope.CompanyId, ct)
                    .ConfigureAwait(false);
                return subject is null ? null : new SinglePrincipalAttestationSubjectSnapshotDto(
                    subject.SubjectKind, subject.SubjectId, subject.Revision, subject.Hash,
                    subject.SourceArtifactId, subject.SourceHash, subject.SourceActor,
                    subject.SourceProvenance, subject.Eligible, subject.Checks,
                    subject.SourceSubjectKind, subject.SourceSubjectId, subject.SubjectSnapshotJson);
            },
            policyEnabled: (sp.GetService<CompositionConfiguration>()?.HostConfiguration
                ?? sp.GetService<IConfiguration>())?
                .GetValue<bool>("SinglePrincipalAttestation:EnableEvidenceAcknowledgments") ?? false,
            draftChecksEnabled: (sp.GetService<CompositionConfiguration>()?.HostConfiguration
                ?? sp.GetService<IConfiguration>())?
                .GetValue<bool>("SinglePrincipalAttestation:EnableDraftChecks") ?? false));
        services.TryAddSingleton<IPreparerAttestationReferenceClassifier>(sp =>
            sp.GetRequiredService<SinglePrincipalAttestationStore>());
        return services;
    }

    public static IServiceCollection AddEvidenceWorkflowFabric(
        this IServiceCollection services,
        bool isProductionComposition = false)
    {
        services.AddEvidenceArtifactStorage();
        services.TryAddSingleton<EvidenceTemplateRegistry>();
        services.TryAddSingleton<EvidenceSubjectResolver>();
        services.TryAddSingleton<EvidencePacketValidationService>();
        services.TryAddSingleton<EvidenceGraphService>();
        services.TryAddSingleton(sp => new PostedLedgerAmountProvenanceService(
            sp.GetService<Meridian.Storage.Ledger.ILedgerJournalStore>(),
            sp.GetService<Meridian.Contracts.Ledger.ILedgerBookService>(),
            sp.GetService<Meridian.Contracts.Tenancy.IFundProfileTenancyRegistry>(),
            sp.GetService<IEvidenceArtifactStore>()));
        services.TryAddSingleton(sp => new ReportLedgerAmountProvenanceService(
            sp.GetService<IReportingRunStore>(),
            sp.GetRequiredService<PostedLedgerAmountProvenanceService>()));
        services.TryAddSingleton<IEvidenceDocumentExtractor, ManualEvidenceDocumentExtractor>();
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvidenceContributor, JournalEntryEvidenceContributor>());
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
                FileEvidenceArtifactStore.ResolveDataRoot(serviceProvider),
                serviceProvider.GetRequiredService<IEvidenceArtifactStore>());
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
