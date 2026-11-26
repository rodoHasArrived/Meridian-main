using Meridian.Application.SecurityMaster;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecurityQuery = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Ui.Shared.Services;

internal static class SecurityMasterOperatorServiceRegistration
{
    internal static IServiceCollection AddSecurityMasterOperatorWorkflows(this IServiceCollection services)
    {
        services.TryAddSingleton<ISecurityRateRepairStore>(provider =>
            provider.GetService<ISecurityMasterStore>() is PostgresSecurityMasterStore store
                && provider.GetService<ISecurityMasterRevisionStore>() is PostgresSecurityMasterRevisionStore
                ? new PostgresSecurityRateRepairStore(store.ImpactGuardOptions)
                : null!);
        services.TryAddSingleton<ISecurityMasterRevisionWorkflowAuthority>(provider =>
            provider.GetService<ISecurityRateRepairStore>() is { } batches
                && provider.GetService<ILedgerJournalStore>() is { } ledger
                ? new SecurityRateRepairWorkflowAuthority(provider.GetRequiredService<ISecurityMasterRevisionStore>(),
                    batches, provider.GetRequiredService<ISecurityMasterImpactScopeAccessor>(), ledger)
                : null!);
        services.TryAddScoped<ISecurityRateRepairEvidenceResolver, WorkstationRateRepairEvidenceResolver>();
        services.TryAddScoped<SecurityRateRepairService>(provider =>
            provider.GetService<ISecurityRateRepairStore>() is { } batches
                && provider.GetService<ISecurityMasterImpactPreviewService>() is { } impacts
                && provider.GetService<ILedgerJournalStore>() is { } ledger
                ? new SecurityRateRepairService(batches, provider.GetRequiredService<SecurityQuery>(),
                    provider.GetRequiredService<ISecurityMasterStore>(), provider.GetRequiredService<ISecurityMasterRevisionStore>(),
                    provider.GetRequiredService<ISecurityMasterWorkbenchCommandService>(),
                    provider.GetRequiredService<IOperationsContinuityWorkflowService>(),
                    provider.GetRequiredService<ISecurityRateRepairEvidenceResolver>(), impacts, ledger,
                    provider.GetService<ISecurityFieldProvenanceStore>())
                : null!);
        return services;
    }
}
