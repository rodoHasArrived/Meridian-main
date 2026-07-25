using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Commands;
using Meridian.Application.Composition.Startup;
using Meridian.Application.Composition.Startup.ModeRunners;
using Meridian.Application.Composition.Startup.StartupModels;
using Meridian.Application.Config;
using Meridian.Application.Reconciliation;
using Meridian.Core.Config;
using Meridian.Application.Services;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Platform.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Xunit;

namespace Meridian.Tests.Application.Composition.Startup.ModeRunners;

[Collection("Sequential")]
public sealed class CommandModeRunnerTests
{
    [Fact]
    public async Task TryRunAsync_StatementImportCommand_UsesPlannerServicesAndDisposesProvider()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-command-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var dataRoot = Path.Combine(root, "data");
        var configPath = Path.Combine(root, "appsettings.json");
        var statementPath = Path.Combine(root, "statement.csv");
        await File.WriteAllTextAsync(configPath, CreateMinimalConfig(dataRoot));
        await File.WriteAllLinesAsync(statementPath,
        [
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
            "FUND-1,SPY,10,500,0,position,2026-05-28",
            "FUND-1,,0,0,2500.25,cash,2026-05-28",
            "FUND-1,MSFT,1,15.75,0,fee,2026-05-28"
        ]);

        var log = Logger.None;
        await using var configService = new ConfigurationService(log);
        using var lifecycle = ApplicationLifecycleCoordinator.Create(log);
        var disposed = false;
        var originalOut = Console.Out;
        CommandDispatchLifetimeDiagnostics.OnDisposed = () => disposed = true;

        try
        {
            string[] args =
            [
                "--statement-import",
                "--statement-broker", "custodian",
                "--statement-source-institution", "Sample Custodian",
                "--statement-fund-account-id", "fund-account-1",
                "--statement-external-account-id", "FUND-1",
                "--statement-mapping-profile-id", "canonical-csv-v1",
                "--statement-tolerance-profile-id", "statement-default",
                "--statement-imported-by", "ops-user",
                "--statement-source-path", statementPath,
                "--statement-date", "2026-05-31",
                "--statement-period-start", "2026-05-01",
                "--statement-period-end", "2026-05-31"
            ];

            var ctx = new StartupContext
            {
                CliArgs = CliArguments.Parse(args),
                ConfigPath = configPath,
                Config = configService.LoadAndPrepareConfig(configPath),
                Deployment = DeploymentContext.ForCommand("statement-import", configPath),
                ConfigurationService = configService,
                DashboardServerFactory = static (_, _, _) => throw new NotSupportedException("Dashboard server should not be created for one-shot command tests."),
                Lifecycle = lifecycle,
                Log = log,
                CancellationToken = CancellationToken.None
            };

            using var writer = new StringWriter();
            Console.SetOut(writer);

            var runner = new CommandModeRunner(log);
            var exitCode = await runner.TryRunAsync(ctx, CancellationToken.None);

            exitCode.Should().Be(0);
            disposed.Should().BeTrue();
            writer.ToString().Should().Contain("imported=");

            var importStore = new JsonCanonicalStatementStore(dataRoot);
            var breakStore = new JsonReconciliationBreakStore(dataRoot);
            var caseStore = new JsonReconciliationCaseStore(dataRoot);

            var imports = await importStore.ListImportsAsync();
            imports.Should().ContainSingle();
            imports[0].Broker.Should().Be("custodian");

            // The retained internal-book provider is wired into the CLI graph, but this run's
            // fund-account id ("fund-account-1") is an operator label rather than a Meridian
            // fund-account GUID, so the provider fails closed to an empty book and all three statement
            // rows (position, cash, fee) reconcile to unmatched breaks.
            var breaks = await breakStore.ListOpenAsync();
            breaks.Should().HaveCount(3);
            breaks.Should().OnlyContain(item => item.ImportId == imports[0].ImportId);

            var cases = await caseStore.ListAsync();
            cases.Should().HaveCount(3);
            cases.Should().OnlyContain(item => item.ImportId == imports[0].ImportId);
        }
        finally
        {
            CommandDispatchLifetimeDiagnostics.OnDisposed = null;
            Console.SetOut(originalOut);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AddCommandDispatchServices_WiresRetainedInternalReconciliationProvider()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-cli-recon-wiring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var dataRoot = Path.Combine(root, "data");
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, CreateMinimalConfig(dataRoot));

        var log = Logger.None;
        await using var configService = new ConfigurationService(log);
        var cfg = configService.LoadAndPrepareConfig(configPath);

        try
        {
            var services = new ServiceCollection();
            services.AddCommandDispatchServices(cfg, configPath, log, configService);
            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<IInternalReconciliationPopulationProvider>()
                .Should().BeOfType<RetainedInternalReconciliationPopulationProvider>(
                    "the CLI statement-import graph must reconcile against retained records, not the fail-closed empty book");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateMinimalConfig(string dataRoot)
    {
        var config = new
        {
            DataRoot = dataRoot,
            Compress = false,
            Storage = new
            {
                NamingConvention = "BySymbol",
                DatePartition = "Daily",
                IncludeProvider = false
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }
}
