using Meridian.Application.Commands;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.Storage;
using Meridian.Storage.Services;
using Serilog;

namespace Meridian.Application.Composition.Startup;

internal sealed record CommandDispatchPlan(CommandDispatcher Dispatcher);

internal static class CommandDispatchPlanner
{
    public static CommandDispatchPlan Create(
        AppConfig cfg,
        string cfgPath,
        ILogger log,
        ConfigurationService configService)
    {
        var store = new ConfigStore(cfgPath);
        var dataRoot = store.GetDataRoot(cfg);
        var symbolService = new SymbolManagementService(store, dataRoot, log);
        var storageSearchService = new StorageSearchService(
            cfg.Storage?.ToStorageOptions(dataRoot, cfg.Compress ?? false)
            ?? new StorageOptions { RootPath = dataRoot });

        var runbookStore = new Meridian.Application.Runbooks.JsonRunbookStore(dataRoot);
        var runbookExecutor = new Meridian.Application.Runbooks.RunbookExecutor();

        return new CommandDispatchPlan(new CommandDispatcher(
            new HelpCommand(),
            new ConfigCommands(configService, log),
            new DiagnosticsCommands(cfg, cfgPath, configService, log),
            new SchemaCheckCommand(cfg, log),
            new SimulationCommands(new ExecutionSimulationOrchestrator(new HistoricalDataQueryService(dataRoot))),
            new SymbolCommands(symbolService, log),
            new ValidateConfigCommand(configService, cfgPath, log),
            new DryRunCommand(cfg, configService, log),
            new SelfTestCommand(log),
            new LedgerCliCommand(),
            new PackageCommands(cfg, log),
            new EtlCommands(cfgPath, log),
            new StatementImportCommands(dataRoot, log),
            new StatementCommands(),
            new ConfigPresetCommand(new AutoConfigurationService(), log),
            new QueryCommand(new HistoricalDataQueryService(dataRoot), log),
            new CatalogCommand(storageSearchService, log),
            new GenerateLoaderCommand(dataRoot, log),
            new RunbookCommands(runbookStore, runbookExecutor),
            new WalRepairCommand(cfg, log),
            new ProviderCalibrationCommand(dataRoot, log),
            // Security Master ingest: importService is null until the full host configures Postgres.
            new SecurityMasterCommands(importService: null, log)));
    }
}
