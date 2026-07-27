using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Entities.FundStructure;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Reporting;
using Meridian.Storage;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Storage.Reporting;
using Meridian.TestSupport;
using Meridian.Tests.Application.Composition;
using Meridian.Tests.Storage.FundAccounts;
using Meridian.Tests.Storage.Reporting;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Ui;

[Trait("Category", "Integration")]
[Collection("Sequential")]
public sealed class ReportingDeploymentReadinessPostgresIntegrationTests
{
    [ReportingDatabaseFact]
    public async Task ProductionReportingGraph_ClientDocumentsFailClosedUntilSourceMigrationsComplete()
    {
        await using var database =
            await PostgresTestServer.CreateAsync("MERIDIAN_REPORTING_CONNECTION_STRING");

        var reportingSchema = PostgresTestSchema.NewSchemaName("reporting_readiness");
        var reportingOptions = new ReportingArtifactStoreOptions
        {
            ConnectionString = database.ConnectionString,
            Schema = reportingSchema
        };
        var ledgerOptions = new LedgerJournalStoreOptions
        {
            ConnectionString = database.ConnectionString,
            SchemaName = PostgresTestSchema.NewSchemaName("reporting_source_ledger"),
            RequireGovernedPostingCommand = true,
            RequireExpectedVersion = true
        };
        var fundAccountOptions = new FundAccountStoreOptions
        {
            ConnectionString = database.ConnectionString,
            Schema = PostgresTestSchema.NewSchemaName("reporting_source_accounts")
        };
        var fundStructureOptions = new FundStructureStoreOptions
        {
            ConnectionString = database.ConnectionString,
            Schema = PostgresTestSchema.NewSchemaName("reporting_source_structure")
        };

        using var environment = new EnvironmentVariableScope("ASPNETCORE_ENVIRONMENT", "Production");
        using var unified = new EnvironmentVariableScope(
            MeridianDatabaseEnvironment.UnifiedVariable,
            null);
        using var reporting = new EnvironmentVariableScope(
            "MERIDIAN_REPORTING_CONNECTION_STRING",
            database.ConnectionString);
        using var reportingSchemaVariable = new EnvironmentVariableScope(
            "MERIDIAN_REPORTING_SCHEMA",
            reportingSchema);
        using var ledger = new EnvironmentVariableScope(
            "MERIDIAN_LEDGER_CONNECTION_STRING",
            database.ConnectionString);
        using var ledgerSchema = new EnvironmentVariableScope(
            "MERIDIAN_LEDGER_SCHEMA",
            ledgerOptions.SchemaName);
        using var fundAccounts = new EnvironmentVariableScope(
            "MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING",
            database.ConnectionString);
        using var fundAccountsSchema = new EnvironmentVariableScope(
            "MERIDIAN_FUND_ACCOUNTS_SCHEMA",
            fundAccountOptions.Schema);
        using var fundStructure = new EnvironmentVariableScope(
            "MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING",
            database.ConnectionString);
        using var fundStructureSchema = new EnvironmentVariableScope(
            "MERIDIAN_FUND_STRUCTURE_SCHEMA",
            fundStructureOptions.Schema);
        using var destinations = new EnvironmentVariableScope(
            "MERIDIAN_REPORTING_RECIPIENT_DESTINATIONS_JSON",
            """
            [
              {
                "tenantId": "tenant-readiness",
                "companyId": "company-readiness",
                "principalId": "client-readiness",
                "transportId": "secure-portal",
                "destination": "client-readiness"
              }
            ]
            """);

        var root = Path.Combine(
            Path.GetTempPath(),
            "Meridian.Tests",
            "reporting-readiness",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        File.WriteAllText(configPath, "{}");

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production
            });
            builder.WebHost.UseTestServer();
            builder.Services.DeclareMeridianDeploymentPosture(
                MeridianDeploymentPosture.ProductionApi);
            builder.Services.AddSingleton(new Meridian.Application.UI.ConfigStore(configPath));
            RegisterPostgresLedgerPresentationSources(
                builder.Services,
                ledgerOptions,
                fundAccountOptions,
                fundStructureOptions);
            builder.Services.AddWorkstationSharedServices();

            // The test drives the real cancellable migration startup seam itself and avoids
            // starting unrelated workstation polling loops.
            builder.Services.RemoveAll<IHostedService>();

            await using var app = builder.Build();
            await app.Services
                .GetRequiredService<IReportingMigrationStartup>()
                .EnsureReadyAsync();

            var sourceMigrationReceipt = app.Services
                .GetRequiredService<DatabaseMigrationReadinessReceipt>();
            sourceMigrationReceipt.LedgerReady.Should().BeFalse();
            sourceMigrationReceipt.FundAccountsReady.Should().BeFalse();
            sourceMigrationReceipt.FundStructureReady.Should().BeFalse();

            var beforeSourceMigrations = app.Services
                .GetRequiredService<IReportingDeploymentReadinessService>()
                .Evaluate();
            beforeSourceMigrations.MigrationsManaged.Should().BeTrue(
                "the reporting schema has completed its own migration startup");
            beforeSourceMigrations.ClientDocumentsConfigured.Should().BeFalse(
                "registered PostgreSQL source types are not sufficient without completed source-schema migrations");
            beforeSourceMigrations.IsReady.Should().BeFalse();
            beforeSourceMigrations.Components
                .Single(static component => component.Id == "client-documents")
                .IsReady.Should().BeFalse();

            await LedgerStartup.EnsureDatabaseReadyAsync(app.Services);
            await FundAccountsStartup.EnsureDatabaseReadyAsync(app.Services);
            await FundStructureStartup.EnsureDatabaseReadyAsync(app.Services);

            sourceMigrationReceipt.LedgerReady.Should().BeTrue();
            sourceMigrationReceipt.FundAccountsReady.Should().BeTrue();
            sourceMigrationReceipt.FundStructureReady.Should().BeTrue();

            var capability = app.Services
                .GetRequiredService<IReportingDeploymentReadinessService>()
                .Evaluate();

            capability.IsReady.Should().BeTrue(
                string.Join("; ", capability.BlockingReasons));
            capability.DurableGovernance.Should().BeTrue();
            capability.DurableArtifacts.Should().BeTrue();
            capability.DurableReconciliationEvidence.Should().BeTrue();
            capability.DurableRuns.Should().BeTrue();
            capability.DurableScheduling.Should().BeTrue();
            capability.DurableDelivery.Should().BeTrue();
            capability.RecipientDestinationsConfigured.Should().BeTrue();
            capability.ClientDocumentsConfigured.Should().BeTrue();
            capability.MigrationsManaged.Should().BeTrue();
            capability.Components.Should().OnlyContain(static component => component.IsReady);

            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "readiness-test";
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] =
                    UserPermission.ViewReporting;
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] =
                    "tenant-readiness";
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] =
                    "company-readiness";
                await next();
            });
            app.MapWorkstationEndpoints(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await app.StartAsync();

            using var response = await app.GetTestClient()
                .GetAsync("/api/workstation/reporting");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var payload = await response.Content
                .ReadFromJsonAsync<WorkstationReportingPayload>();
            payload.Should().NotBeNull();
            payload!.DeploymentCapability.Should().NotBeNull();
            payload.DeploymentCapability!.IsReady.Should().BeTrue();
            payload.DeploymentCapability.Components
                .Should()
                .OnlyContain(static component => component.IsReady);
        }
        finally
        {
            if (database.UsesExternalConnection)
            {
                await new ReportingMigrationRunner(reportingOptions)
                    .ResetSchemaAsync();
                await FundAccountDatabaseFixture.DropSchemaAsync(
                    database.ConnectionString,
                    ledgerOptions.SchemaName);
                await FundAccountDatabaseFixture.DropSchemaAsync(
                    database.ConnectionString,
                    fundAccountOptions.Schema);
                await FundAccountDatabaseFixture.DropSchemaAsync(
                    database.ConnectionString,
                    fundStructureOptions.Schema);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    private static void RegisterPostgresLedgerPresentationSources(
        IServiceCollection services,
        LedgerJournalStoreOptions ledgerOptions,
        FundAccountStoreOptions fundAccountOptions,
        FundStructureStoreOptions fundStructureOptions)
    {
        services.AddSingleton<DatabaseMigrationReadinessReceipt>();
        services.AddSingleton(ledgerOptions);
        services.AddSingleton<LedgerMigrationRunner>();
        services.AddSingleton<ILedgerJournalStore>(
            new PostgresLedgerJournalStore(ledgerOptions));
        services.AddSingleton<IFundProfileTenancyRegistry>(
            new PostgresFundProfileTenancyRegistry(ledgerOptions));

        var accountStore = new PostgresFundAccountStore(fundAccountOptions);
        var accounts = new PostgresFundAccountService(
            accountStore);
        services.AddSingleton(fundAccountOptions);
        services.AddSingleton<IFundAccountStore>(accountStore);
        services.AddSingleton<IFundAccountService>(accounts);
        var structureStore = new PostgresFundStructureStore(fundStructureOptions);
        services.AddSingleton(fundStructureOptions);
        services.AddSingleton<IFundStructureStore>(structureStore);
        services.AddSingleton<IFundStructureService>(
            new PostgresFundStructureService(
                structureStore,
                accounts,
                new FundStructurePolicyService()));
    }
}
