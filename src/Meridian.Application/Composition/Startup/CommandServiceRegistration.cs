using Meridian.Application.Commands;
using Meridian.Application.Config;
using Meridian.DataIntegration.Historical;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Application.Services;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.Domain.Reconciliation;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;

namespace Meridian.Application.Composition.Startup;

internal static class CommandServiceRegistration
{
    public static IServiceCollection AddCommandDispatchServices(
        this IServiceCollection services,
        AppConfig cfg,
        string cfgPath,
        ILogger log,
        ConfigurationService configService)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(cfg);
        ArgumentException.ThrowIfNullOrWhiteSpace(cfgPath);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(configService);

        var configStore = new ConfigStore(cfgPath);
        var dataRoot = configStore.GetDataRoot(cfg);
        var storageOptions = cfg.Storage?.ToStorageOptions(dataRoot, cfg.Compress ?? false)
            ?? new StorageOptions { RootPath = dataRoot };

        services.TryAddSingleton(cfg);
        services.TryAddSingleton(log);
        services.TryAddSingleton(configService);
        services.TryAddSingleton(configStore);
        services.TryAddSingleton(storageOptions);
        services.TryAddSingleton(new CommandServicePaths(cfgPath, dataRoot));
        services.TryAddSingleton<CommandDispatchLifetimeProbe>();
        services.TryAddSingleton(sp => new SymbolManagementService(
            sp.GetRequiredService<ConfigStore>(),
            sp.GetRequiredService<CommandServicePaths>().DataRoot,
            sp.GetRequiredService<ILogger>()));
        services.TryAddSingleton(sp => new StorageSearchService(sp.GetRequiredService<StorageOptions>()));
        services.TryAddSingleton(sp => new HistoricalDataQueryService(sp.GetRequiredService<CommandServicePaths>().DataRoot));
        services.TryAddSingleton<AutoConfigurationService>();
        services.TryAddSingleton(sp => new Meridian.Workflow.Runbooks.JsonRunbookStore(sp.GetRequiredService<CommandServicePaths>().DataRoot));
        services.TryAddSingleton<Meridian.Workflow.Runbooks.RunbookExecutor>();
        services.AddStatementReconciliationServices(dataRoot);

        services.AddSingleton<ICliCommand, HelpCommand>();
        services.AddSingleton<ICliCommand>(sp => new ConfigCommands(
            sp.GetRequiredService<ConfigurationService>(),
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new DiagnosticsCommands(
            sp.GetRequiredService<AppConfig>(),
            sp.GetRequiredService<CommandServicePaths>().ConfigPath,
            sp.GetRequiredService<ConfigurationService>(),
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new SchemaCheckCommand(
            sp.GetRequiredService<AppConfig>(),
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new SimulationCommands(
            new ExecutionSimulationOrchestrator(sp.GetRequiredService<HistoricalDataQueryService>())));
        services.AddSingleton<ICliCommand>(sp => new SymbolCommands(
            sp.GetRequiredService<SymbolManagementService>(),
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new ValidateConfigCommand(
            sp.GetRequiredService<ConfigurationService>(),
            sp.GetRequiredService<CommandServicePaths>().ConfigPath,
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new DryRunCommand(
            sp.GetRequiredService<AppConfig>(),
            sp.GetRequiredService<ConfigurationService>(),
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new SelfTestCommand(sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand, LedgerCliCommand>();
        services.AddSingleton<ICliCommand>(sp => new PackageCommands(
            sp.GetRequiredService<AppConfig>(),
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new EtlCommands(
            sp.GetRequiredService<CommandServicePaths>().ConfigPath,
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new StatementImportCommands(
            sp.GetRequiredService<IBrokerStatementService>(),
            sp.GetRequiredService<IStatementRunWorkflowService>(),
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new StatementCommands(
            sp.GetRequiredService<IStatementReconciliationValidationService>(),
            sp.GetRequiredService<IDataIntegrationIngestionService>(),
            sp.GetRequiredService<IReconciliationCaseIntakeService>(),
            sp.GetRequiredService<IStatementReconciliationCheckpointStore>()));
        services.AddSingleton<ICliCommand>(sp => new ConfigPresetCommand(
            sp.GetRequiredService<AutoConfigurationService>(),
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new QueryCommand(
            sp.GetRequiredService<HistoricalDataQueryService>(),
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new CatalogCommand(
            sp.GetRequiredService<StorageSearchService>(),
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new GenerateLoaderCommand(
            sp.GetRequiredService<CommandServicePaths>().DataRoot,
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new RunbookCommands(
            sp.GetRequiredService<Meridian.Workflow.Runbooks.JsonRunbookStore>(),
            sp.GetRequiredService<Meridian.Workflow.Runbooks.RunbookExecutor>()));
        services.AddSingleton<ICliCommand>(sp => new WalRepairCommand(
            sp.GetRequiredService<AppConfig>(),
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new ProviderCalibrationCommand(
            sp.GetRequiredService<CommandServicePaths>().DataRoot,
            sp.GetRequiredService<ILogger>()));
        services.AddSingleton<ICliCommand>(sp => new SecurityMasterCommands(
            importService: null,
            sp.GetRequiredService<ILogger>()));
        services.TryAddSingleton(sp => new CommandDispatcher(sp.GetServices<ICliCommand>().ToArray()));
        return services;
    }
}

internal sealed record CommandServicePaths(string ConfigPath, string DataRoot);

internal sealed class CommandDispatchLifetimeProbe : IDisposable
{
    public void Dispose()
    {
        CommandDispatchLifetimeDiagnostics.OnDisposed?.Invoke();
    }
}

internal static class CommandDispatchLifetimeDiagnostics
{
    public static Action? OnDisposed { get; set; }
}
