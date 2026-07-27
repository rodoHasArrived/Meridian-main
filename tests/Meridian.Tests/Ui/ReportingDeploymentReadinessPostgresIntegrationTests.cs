using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.FundStructure;
using Meridian.Application.UI;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
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
    public async Task ProductionReportingGraph_FirstStartupIsReadyAndEndpointPublishesCapability()
    {
        await using var database =
            await PostgresTestServer.CreateAsync("MERIDIAN_REPORTING_CONNECTION_STRING");

        var reportingSchema = PostgresTestSchema.NewSchemaName("reporting_readiness");
        var reportingOptions = new ReportingArtifactStoreOptions
        {
            ConnectionString = database.ConnectionString,
            Schema = reportingSchema
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
            null);
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
            builder.Services.AddSingleton(new Meridian.Ui.Shared.Services.ConfigStore(configPath));
            RegisterPostgresLedgerPresentationSources(
                builder.Services,
                database.ConnectionString);
            builder.Services.AddWorkstationSharedServices();

            // The test drives the real cancellable migration startup seam itself and avoids
            // starting unrelated workstation polling loops.
            builder.Services.RemoveAll<IHostedService>();

            await using var app = builder.Build();
            await app.Services
                .GetRequiredService<IReportingMigrationStartup>()
                .EnsureReadyAsync();

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
            }

            Directory.Delete(root, recursive: true);
        }
    }

    private static void RegisterPostgresLedgerPresentationSources(
        IServiceCollection services,
        string connectionString)
    {
        var ledgerOptions = new LedgerJournalStoreOptions
        {
            ConnectionString = connectionString,
            SchemaName = PostgresTestSchema.NewSchemaName("reporting_source_ledger"),
            RequireGovernedPostingCommand = true,
            RequireExpectedVersion = true
        };
        services.AddSingleton<ILedgerJournalStore>(
            new PostgresLedgerJournalStore(ledgerOptions));
        services.AddSingleton<IFundProfileTenancyRegistry>(
            new PostgresFundProfileTenancyRegistry(ledgerOptions));

        var accounts = new PostgresFundAccountService(
            new PostgresFundAccountStore(new FundAccountStoreOptions
            {
                ConnectionString = connectionString,
                Schema = PostgresTestSchema.NewSchemaName("reporting_source_accounts")
            }));
        services.AddSingleton<Meridian.Contracts.Services.IFundStructureService>(
            new PostgresFundStructureService(
                new PostgresFundStructureStore(new FundStructureStoreOptions
                {
                    ConnectionString = connectionString,
                    Schema = PostgresTestSchema.NewSchemaName("reporting_source_structure")
                }),
                accounts,
                new FundStructurePolicyService()));
    }
}
