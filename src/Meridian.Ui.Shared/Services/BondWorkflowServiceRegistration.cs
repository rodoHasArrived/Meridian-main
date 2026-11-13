using Meridian.Application.Integrations;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.FixedIncome;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Storage;
using Meridian.Storage.FixedIncome;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Ui.Shared.Services;

internal static class BondWorkflowServiceRegistration
{
    internal static void AddBondWorkflowServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IBondMarketDataStore>(sp =>
            new FileBondMarketDataStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IBondMarketDataService, BondMarketDataService>();
        services.TryAddSingleton<IBondProviderResponseStore>(sp =>
            new FileBondProviderResponseStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<OpenFigiClient>();
        services.TryAddSingleton<FredHistoricalDataProvider>();
        services.TryAddSingleton<BondProviderIntakeService>();
        // The delegate reads the current AsyncLocal request on every call; no tenant is captured in a singleton.
        services.TryAddSingleton<BondMarketWorkflowObservationSource>(sp => new(
            sp.GetRequiredService<IBondMarketDataService>(),
            () => sp.GetRequiredService<IWorkstationTenantContextAccessor>().GetRequired().TenantId!));
        services.TryAddSingleton<IBondWorkflowObservationSource>(sp => sp.GetRequiredService<BondMarketWorkflowObservationSource>());
        services.TryAddSingleton<IBondWorkflowQuoteSource>(sp => sp.GetRequiredService<BondMarketWorkflowObservationSource>());
        services.TryAddSingleton<IBondWorkflowService, BondWorkflowService>();
        services.TryAddSingleton<IAccountBondHoldingsSource, AccountBondHoldingsSource>();
        services.TryAddSingleton<BondHoldingImportService>();
        services.TryAddSingleton<IBondMonitoringStore>(sp => new BondMonitoringFileStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IBondMonitoringService, BondMonitoringService>();
        services.TryAddSingleton<IAccountBondIncomePlanningService, AccountBondIncomePlanningService>();
        services.TryAddSingleton<IBondComparisonService, BondComparisonService>();
        services.TryAddSingleton<IBondPortfolioRiskService, BondPortfolioRiskService>();
        services.TryAddSingleton<IBondScreeningService, BondScreeningService>();
        services.TryAddSingleton<IBondPerformanceTransactionPopulationStore>(sp =>
            new FileBondPerformanceTransactionStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<BondPerformanceTransactionSource>();
        services.TryAddSingleton<IBondPerformanceTransactionSource>(sp => sp.GetRequiredService<BondPerformanceTransactionSource>());
        services.TryAddSingleton<IBondPerformanceSourceVerifier, BondPerformanceSourceVerifier>();
        services.TryAddSingleton<IBondCouponRecognitionEvidenceVerifier, BondCouponRecognitionEvidenceVerifier>();
        services.TryAddSingleton<IBondIncomePreparationEvidenceStore>(sp =>
            new FileBondIncomePreparationEvidenceStore(Path.Combine(sp.GetRequiredService<StorageOptions>().RootPath,
                "bond-income", "prepared-evidence")));
        services.TryAddSingleton<IBondPerformanceService, BondPerformanceService>();
        services.TryAddSingleton<BondWorkspaceSnapshotStore>(sp => new(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<BondWorkflowConnectionReadinessService>();
        services.TryAddSingleton<BondCollectionCaseworkProjection>();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ILedgerJournalStore))
            && services.Any(descriptor => descriptor.ServiceType == typeof(ISecurityMasterStore))
            && services.Any(descriptor => descriptor.ServiceType == typeof(Meridian.Storage.AssetOperations.IInstrumentPositionProjectionStore))
            && services.Any(descriptor => descriptor.ServiceType == typeof(IAccountingPolicyService)))
        {
            services.TryAddSingleton<IBondIncomeService, BondIncomeService>();
            services.TryAddSingleton<BondIncomePreparationService>(sp =>
                sp.GetService<IAssetAccountingEventSpineService>() is { } spine
                    ? new BondIncomePreparationService(sp.GetRequiredService<IBondIncomeService>(),
                        sp.GetRequiredService<ILedgerJournalStore>(),
                        sp.GetRequiredService<Meridian.Storage.AssetOperations.IInstrumentPositionProjectionStore>(),
                        sp.GetRequiredService<IAccountingPolicyService>(), spine,
                        sp.GetRequiredService<IBondIncomePreparationEvidenceStore>()) : null!);
        }
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IBondCouponRecognitionStore))
            && services.Any(descriptor => descriptor.ServiceType == typeof(IBondIncomeService)))
            services.TryAddSingleton<IBondCouponRecognitionService, BondCouponRecognitionService>();
    }
}
