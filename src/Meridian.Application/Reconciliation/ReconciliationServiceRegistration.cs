using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Application.Reconciliation;

public static class ReconciliationServiceRegistration
{
    public static IServiceCollection AddStatementReconciliationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddSharedServices(services);
        services.TryAddSingleton<ICanonicalStatementStore>(sp => new JsonCanonicalStatementStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IReconciliationCaseStore>(sp => new JsonReconciliationCaseStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IReconciliationBreakStore>(sp => new JsonReconciliationBreakStore(sp.GetRequiredService<StorageOptions>().RootPath));
        services.TryAddSingleton<IBrokerStatementService>(sp => new CsvBrokerStatementService(sp.GetRequiredService<ICanonicalStatementStore>()));
        return services;
    }

    public static IServiceCollection AddStatementReconciliationServices(this IServiceCollection services, string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        AddSharedServices(services);
        services.TryAddSingleton<ICanonicalStatementStore>(_ => new JsonCanonicalStatementStore(dataRoot));
        services.TryAddSingleton<IReconciliationCaseStore>(_ => new JsonReconciliationCaseStore(dataRoot));
        services.TryAddSingleton<IReconciliationBreakStore>(_ => new JsonReconciliationBreakStore(dataRoot));
        services.TryAddSingleton<IBrokerStatementService>(sp => new CsvBrokerStatementService(sp.GetRequiredService<ICanonicalStatementStore>()));
        return services;
    }

    private static void AddSharedServices(IServiceCollection services)
    {
        services.TryAddSingleton<StatementReconciliationService>();
        services.TryAddSingleton<StatementReconciliationContextAdapter>();
        services.TryAddSingleton<IStatementReconciliationValidationService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.TryAddSingleton<IDataIntegrationIngestionService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.TryAddSingleton<IReconciliationCaseIntakeService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.TryAddSingleton<IStatementRunWorkflowService, StatementRunWorkflowService>();
        services.TryAddSingleton<IStatementReconciliationCheckpointStore, InMemoryStatementReconciliationCheckpointStore>();
        services.TryAddSingleton<StatementReconciliationOrchestrator>();
    }
}
