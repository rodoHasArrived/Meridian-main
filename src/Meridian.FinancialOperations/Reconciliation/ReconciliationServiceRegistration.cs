using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Alpaca;
using Meridian.FinancialOperations.Reconciliation.Connectors.Bai2;
using Meridian.FinancialOperations.Reconciliation.Connectors.Camt;
using Meridian.FinancialOperations.Reconciliation.Connectors.IbFlex;
using Meridian.FinancialOperations.Reconciliation.Connectors.Ofx;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Meridian.FinancialOperations.Reconciliation;

public static class ReconciliationServiceRegistration
{
    public static IServiceCollection AddStatementReconciliationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAccountingCalendar>(sp =>
            FileAccountingCalendar.Load(
                sp.GetRequiredService<StorageOptions>().RootPath,
                sp.GetService<ILogger<BusinessDayAccountingCalendar>>()));
        AddSharedServices(services);
        services.TryAddSingleton<IStatementReconciliationCheckpointStore>(sp =>
            new FileStatementReconciliationCheckpointStore(
                sp.GetRequiredService<StorageOptions>().RootPath,
                sp.GetService<ILogger<FileStatementReconciliationCheckpointStore>>()));
        services.TryAddSingleton<ICanonicalStatementStore>(sp => new JsonCanonicalStatementStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IReconciliationCaseStore>(sp => new JsonReconciliationCaseStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IReconciliationBreakStore>(sp => new JsonReconciliationBreakStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IStatementRunRecoveryRepository>(sp =>
            new FileStatementRunRecoveryRepository(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IStatementRunMatchArtifactStore>(sp =>
            new FileStatementRunMatchArtifactStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IStatementCaseworkCommitStore>(sp =>
            new FileStatementCaseworkCommitStore(sp.GetRequiredService<StorageOptions>().RootPath));
        AddBrokerStatementServices(services);
        AddConnectorServices(services, static sp => sp.GetRequiredService<StorageOptions>().RootPath);
        return services;
    }

    public static IServiceCollection AddStatementReconciliationServices(this IServiceCollection services, string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        services.TryAddSingleton<IAccountingCalendar>(sp =>
            FileAccountingCalendar.Load(dataRoot, sp.GetService<ILogger<BusinessDayAccountingCalendar>>()));
        AddSharedServices(services);
        services.TryAddSingleton<IStatementReconciliationCheckpointStore>(sp =>
            new FileStatementReconciliationCheckpointStore(
                dataRoot,
                sp.GetService<ILogger<FileStatementReconciliationCheckpointStore>>()));
        services.TryAddSingleton<ICanonicalStatementStore>(_ => new JsonCanonicalStatementStore(dataRoot));
        services.TryAddSingleton<IReconciliationCaseStore>(_ => new JsonReconciliationCaseStore(dataRoot));
        services.TryAddSingleton<IReconciliationBreakStore>(_ => new JsonReconciliationBreakStore(dataRoot));
        services.TryAddSingleton<IStatementRunRecoveryRepository>(_ => new FileStatementRunRecoveryRepository(dataRoot));
        services.TryAddSingleton<IStatementRunMatchArtifactStore>(_ => new FileStatementRunMatchArtifactStore(dataRoot));
        services.TryAddSingleton<IStatementCaseworkCommitStore>(_ => new FileStatementCaseworkCommitStore(dataRoot));
        AddBrokerStatementServices(services);
        AddConnectorServices(services, _ => dataRoot);
        return services;
    }

    private static void AddBrokerStatementServices(IServiceCollection services)
    {
        services.TryAddSingleton<CsvBrokerStatementService>(sp =>
            new CsvBrokerStatementService(sp.GetRequiredService<ICanonicalStatementStore>()));
        services.TryAddSingleton<IbFlexBrokerStatementService>(sp =>
            new IbFlexBrokerStatementService(sp.GetRequiredService<ICanonicalStatementStore>()));
        services.TryAddSingleton<IBrokerStatementService>(sp => new RoutingBrokerStatementService(
            sp.GetRequiredService<CsvBrokerStatementService>(),
            sp.GetRequiredService<IbFlexBrokerStatementService>()));
    }

    private static void AddSharedServices(IServiceCollection services)
    {
        // Safe, fail-closed defaults for the statement-run matcher. A deployment wires a real
        // internal-population provider (live positions/cash/ledger) and FX rate table by registering
        // its own implementations before calling AddStatementReconciliationServices, or via Replace.
        services.TryAddSingleton<IInternalReconciliationPopulationProvider>(EmptyInternalReconciliationPopulationProvider.Instance);
        services.TryAddSingleton<IReconciliationFxRateProvider>(IdentityReconciliationFxRateProvider.Instance);
        // Safety net for hosts that bypass the dataRoot-aware overloads: a weekends-only calendar.
        // The overloads above register the operator-maintained file calendar first, so it wins here.
        services.TryAddSingleton<IAccountingCalendar>(BusinessDayAccountingCalendar.Default);
        // Knows only the built-in default profile; a deployment registers a provider carrying its
        // operator profiles (first-wins/Replace) so non-default runs are matched with the selected
        // profile's thresholds instead of silently falling back to the defaults.
        services.TryAddSingleton<IStatementToleranceProfileProvider>(new InMemoryStatementToleranceProfileProvider());
        services.TryAddSingleton<StatementReconciliationService>();
        services.TryAddSingleton<StatementReconciliationContextAdapter>();
        services.TryAddSingleton<IStatementReconciliationValidationService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.TryAddSingleton<IDataIntegrationIngestionService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.TryAddSingleton<IReconciliationCaseIntakeService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        // Default internal-book source: an empty book. Deployments wire reconciliation against
        // their authoritative internal records by registering a real IInternalReconciliationBookSource
        // before this call; TryAdd keeps that override in force.
        services.TryAddSingleton<IInternalReconciliationBookSource, EmptyInternalReconciliationBookSource>();
        services.TryAddSingleton<IStatementRunWorkflowService, StatementRunWorkflowService>();
        services.TryAddSingleton<StatementReconciliationOrchestrator>();
    }

    /// <summary>
    /// Registers the statement connector library: declarative mapping-profile store/catalog,
    /// the connectors themselves, the import service, and scheduled-fetch support. The
    /// profile registry singleton flows operator profiles present at startup into the legacy
    /// reconciliation paths; the connector paths read the catalog live.
    /// </summary>
    private static void AddConnectorServices(IServiceCollection services, Func<IServiceProvider, string> resolveDataRoot)
    {
        services.TryAddSingleton<IStatementMappingProfileStore>(sp =>
            new FileStatementMappingProfileStore(
                resolveDataRoot(sp),
                sp.GetService<ILogger<FileStatementMappingProfileStore>>()));
        services.TryAddSingleton<StatementMappingProfileCatalog>();
        services.TryAddSingleton(sp => sp.GetRequiredService<StatementMappingProfileCatalog>().BuildRegistry());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatementConnector, CsvStatementConnector>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatementConnector, OfxStatementConnector>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatementConnector, IbFlexStatementConnector>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatementConnector, AlpacaActivityStatementConnector>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatementConnector, Camt053StatementConnector>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStatementConnector, Bai2StatementConnector>());
        services.TryAddSingleton<StatementConnectorRegistry>();

        services.TryAddSingleton(sp => new StatementImportService(
            sp.GetRequiredService<StatementConnectorRegistry>(),
            sp.GetRequiredService<StatementMappingProfileCatalog>(),
            sp.GetRequiredService<IStatementRunWorkflowService>(),
            resolveDataRoot(sp)));
        services.TryAddSingleton<IStatementImportCommitService>(sp =>
            sp.GetRequiredService<StatementImportService>());

        services.TryAddSingleton<IStatementFetchScheduleStore>(sp =>
            new FileStatementFetchScheduleStore(
                resolveDataRoot(sp),
                sp.GetService<ILogger<FileStatementFetchScheduleStore>>()));
        services.TryAddSingleton<StatementFetchScheduleRunner>();
    }
}
