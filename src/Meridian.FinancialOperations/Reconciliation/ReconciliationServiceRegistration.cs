using Meridian.Domain.Reconciliation;
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

        AddSharedServices(services);
        services.TryAddSingleton<IStatementReconciliationCheckpointStore>(sp =>
            new FileStatementReconciliationCheckpointStore(
                sp.GetRequiredService<StorageOptions>().RootPath,
                sp.GetService<ILogger<FileStatementReconciliationCheckpointStore>>()));
        services.TryAddSingleton<ICanonicalStatementStore>(sp => new JsonCanonicalStatementStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IReconciliationCaseStore>(sp => new JsonReconciliationCaseStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IReconciliationBreakStore>(sp => new JsonReconciliationBreakStore(sp.GetRequiredService<StorageOptions>().RootPath));
        AddBrokerStatementServices(services);
        return services;
    }

    public static IServiceCollection AddStatementReconciliationServices(this IServiceCollection services, string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        AddSharedServices(services);
        services.TryAddSingleton<IStatementReconciliationCheckpointStore>(sp =>
            new FileStatementReconciliationCheckpointStore(
                dataRoot,
                sp.GetService<ILogger<FileStatementReconciliationCheckpointStore>>()));
        services.TryAddSingleton<ICanonicalStatementStore>(_ => new JsonCanonicalStatementStore(dataRoot));
        services.TryAddSingleton<IReconciliationCaseStore>(_ => new JsonReconciliationCaseStore(dataRoot));
        services.TryAddSingleton<IReconciliationBreakStore>(_ => new JsonReconciliationBreakStore(dataRoot));
        AddBrokerStatementServices(services);
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
        services.TryAddSingleton<StatementReconciliationService>();
        services.TryAddSingleton<StatementReconciliationContextAdapter>();
        services.TryAddSingleton<IStatementReconciliationValidationService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.TryAddSingleton<IDataIntegrationIngestionService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.TryAddSingleton<IReconciliationCaseIntakeService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.TryAddSingleton<IStatementRunWorkflowService, StatementRunWorkflowService>();
        services.TryAddSingleton<StatementReconciliationOrchestrator>();
    }
}
