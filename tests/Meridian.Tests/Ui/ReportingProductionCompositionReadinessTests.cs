using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

/// <summary>
/// Composition evidence for the durable reporting deployment gate against the real host graph:
/// the exact-instance requirements that <see cref="ReportingDeploymentReadinessService"/> enforces
/// (one statement workflow composed with the one durable authority, and one reconciliation
/// break-queue authority shared by every casework consumer) must hold on the service provider the
/// production workstation host actually builds, not only on hand-assembled test collections.
/// </summary>
[Collection("Sequential")]
public sealed class ReportingProductionCompositionReadinessTests
{
    private const string UnreachableReportingConnectionString =
        "Host=127.0.0.1;Port=1;Database=meridian;Username=meridian;Password=meridian;Timeout=1";

    [Fact]
    public async Task UiServer_DurableReportingComposition_SharesExactStatementAndCaseworkAuthorities()
    {
        using var quiet = new Meridian.Tests.Application.Composition.ProductionEnvironmentQuietScope();
        using var aspnet = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "ASPNETCORE_ENVIRONMENT",
            Environments.Development);
        using var dotnet = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "DOTNET_ENVIRONMENT",
            Environments.Development);
        using var governance = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_USE_INMEMORY_GOVERNANCE",
            "true");
        using var unified = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable,
            null);
        using var reporting = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_REPORTING_CONNECTION_STRING",
            UnreachableReportingConnectionString);
        using var ledger = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_LEDGER_CONNECTION_STRING",
            null);
        var root = CreateTempRoot();
        var configPath = WriteMinimalConfig(root);

        try
        {
            await using var server = new UiServer(configPath, port: 0);
            var services = GetServerApp(server).Services;

            var authority = services.GetRequiredService<IStatementReconciliationReportAuthorityStore>();
            authority.Should().BeOfType<PostgresStatementReconciliationReportAuthorityStore>(
                "a configured reporting connection string must produce the durable statement authority");
            authority.IsDurableAuthority.Should().BeTrue();

            var workflow = services.GetService<StatementReconciliationReportWorkflowService>();
            workflow.Should().NotBeNull(
                "the durable composition must register the statement workflow over the shared authority");
            workflow!.IsDurablyComposedWith(authority).Should().BeTrue(
                "the workflow must hold the exact authority instance the deployment gate resolves");

            var breakQueue = services.GetRequiredService<IReconciliationBreakQueueRepository>();
            breakQueue.Should().BeAssignableTo<IReconciliationBreakQueueAuthorityProbe>(
                "the reporting migration startup fails closed on a break queue that cannot verify durable state");

            var caseworkHandoff = services.GetRequiredService<IStatementReconciliationCaseworkHandoffService>()
                .Should().BeOfType<StatementReconciliationCaseworkHandoffService>().Subject;
            caseworkHandoff.BreakQueueAuthority.Should().BeSameAs(breakQueue);

            var operationsBridge = services.GetRequiredService<IOperationsContinuityReconciliationBridge>()
                .Should().BeOfType<OperationsContinuityReconciliationBridge>().Subject;
            operationsBridge.BreakQueueAuthority.Should().BeSameAs(breakQueue);

            var closeBridge = services.GetRequiredService<Meridian.FinancialOperations.AccountingClose.IAccountingClosePostingWorkbench>()
                .Should().BeOfType<AccountingClosePostingWorkbenchBridge>().Subject;
            closeBridge.BreakQueueAuthority.Should().BeSameAs(breakQueue);

            var evidenceSource = services.GetRequiredService<IReportingReconciliationEvidenceSource>()
                .Should().BeOfType<ReportingReconciliationEvidenceSource>().Subject;
            evidenceSource.BreakQueueAuthority.Should().BeSameAs(breakQueue);

            // The gate itself must run over this graph and fail closed on the unreachable database
            // rather than on composition gaps that would persist after the database comes up.
            var capability = services.GetRequiredService<IReportingDeploymentReadinessService>().Evaluate();
            capability.IsReady.Should().BeFalse();
            capability.BlockingReasons.Should().Contain(
                "The PostgreSQL reporting authority is unreachable.");
        }
        finally
        {
            CleanupTempRoot(root);
        }
    }

    [Fact]
    public void Evaluate_ServiceResolutionFailure_NamesTheServiceAndCauseInBlockingReasons()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IReconciliationBreakQueueRepository>(
            _ => throw new InvalidOperationException("break-queue factory exploded"));
        using var provider = services.BuildServiceProvider();
        var readiness = new ReportingDeploymentReadinessService(provider);

        var capability = readiness.Evaluate();

        capability.IsReady.Should().BeFalse();
        capability.BlockingReasons.Should().Contain(reason =>
            reason.Contains(nameof(IReconciliationBreakQueueRepository))
            && reason.Contains("break-queue factory exploded"));
        readiness.GetScheduleWorkerCycleBlockingReasons().Should().Contain(reason =>
            reason.Contains("break-queue factory exploded"));
    }

    private static WebApplication GetServerApp(UiServer server)
    {
        var field = typeof(UiServer).GetField("_app", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        var app = field!.GetValue(server) as WebApplication;
        app.Should().NotBeNull();
        return app!;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "reporting-production-composition",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CleanupTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            try
            { Directory.Delete(root, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static string WriteMinimalConfig(string root)
    {
        var config = new
        {
            DataRoot = Path.Combine(root, "data"),
            Compress = false,
            DataSource = "Synthetic",
            ApiHost = new
            {
                Urls = new[] { "http://127.0.0.1:0" },
                ServeWorkstationAssets = false
            },
            Symbols = new[]
            {
                new
                {
                    Symbol = "SPY",
                    SubscribeTrades = true,
                    SubscribeDepth = false,
                    DepthLevels = 10,
                    SecurityType = "STK",
                    Exchange = "SMART",
                    Currency = "USD"
                }
            },
            Storage = new
            {
                NamingConvention = "BySymbol",
                DatePartition = "Daily",
                IncludeProvider = false
            },
            Backfill = new
            {
                Enabled = false,
                Provider = "stooq",
                Symbols = new[] { "SPY" }
            }
        };

        var configPath = Path.Combine(root, "appsettings.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        return configPath;
    }
}
