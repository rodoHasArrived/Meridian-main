using Meridian.Application.Commands;
using Meridian.Application.Reconciliation;
using Meridian.Core.Config;
using Meridian.DataIntegration.Historical;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Application.Services;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.Contracts.Domain;
using Meridian.Domain.Reconciliation;
using Meridian.Contracts.Operations;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage;
using Meridian.Storage.Operations;
using Meridian.Storage.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
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
        services.TryAddSingleton<IOperationalCaseHistoryStore>(sp =>
            new FileOperationalCaseHistoryStore(sp.GetRequiredService<CommandServicePaths>().DataRoot));
        services.TryAddSingleton<Meridian.Workflow.Workflows.FundWorkflowCommandHandler>();
        services.TryAddSingleton(sp => new Meridian.Workflow.Runbooks.RunbookExecutor(
            sp.GetRequiredService<IOperationalCaseHistoryStore>(),
            sp.GetServices<Meridian.Workflow.Runbooks.IRunbookStepHandler>()));
        services.AddStatementReconciliationServices(dataRoot);
        // Reconcile CLI statement imports against Meridian's own retained account records (positions +
        // cash) instead of the fail-closed empty book, matching the browser workstation graph. The
        // file-backed fund-account and position stores read retained governance/position data under the
        // CLI data root; a run whose FundAccountId is a Meridian fund-account GUID reconciles, while a
        // non-GUID label or missing retained data fails closed to breaks. Replace (not TryAdd) so this
        // wins over the empty default AddStatementReconciliationServices registers via TryAddSingleton.
        services.TryAddSingleton<IPositionSnapshotStore>(sp => new JsonlPositionSnapshotStore(
            sp.GetRequiredService<StorageOptions>(),
            NullLogger<JsonlPositionSnapshotStore>.Instance));
        services.TryAddSingleton<IAccountQueryService>(_ => new InMemoryFundAccountService(
            Path.Combine(dataRoot, "governance", "fund-accounts.json")));
        services.Replace(ServiceDescriptor.Singleton<IInternalReconciliationPopulationProvider, RetainedInternalReconciliationPopulationProvider>());
        // Normalize cross-currency statement lines using the operator-maintained FX rate table under the
        // data root (reconciliation/fx-rates.json), matching the workstation graph, instead of the
        // identity-only default. A missing or empty table keeps cross-currency lines failing closed.
        services.Replace(ServiceDescriptor.Singleton<IReconciliationFxRateProvider>(_ =>
            FileReconciliationFxRateProvider.Load(dataRoot)));
        // Resolve the run's selected tolerance profile from the operator-maintained profile table under
        // the data root, matching the workstation graph; an unknown id fails closed at the workflow.
        services.Replace(ServiceDescriptor.Singleton<IStatementToleranceProfileProvider>(_ =>
            FileStatementToleranceProfileProvider.Load(dataRoot)));

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
            sp.GetRequiredService<Meridian.FinancialOperations.Reconciliation.Connectors.IStatementImportCommitService>(),
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
            sp.GetRequiredService<ILogger>(),
            corporateActionIngestOrchestrator: sp.GetService<Meridian.Application.SecurityMaster.CorporateActions.CorporateActionIngestOrchestrator>(),
            securityMasterEventStore: sp.GetService<Meridian.Storage.SecurityMaster.ISecurityMasterEventStore>()));
        services.TryAddSingleton(sp => new CommandDispatcher(
            sp.GetServices<ICliCommand>(),
            sp.GetRequiredService<ILogger>()));
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
