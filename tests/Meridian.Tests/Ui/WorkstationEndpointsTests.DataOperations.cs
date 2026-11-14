using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.ProviderRouting;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Credentials;
using Meridian.DataIntegration.Monitoring;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using IReconciliationApiService = Meridian.Contracts.Reconciliation.IReconciliationApiService;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ReconciliationCaseSummaryDto = Meridian.Contracts.Reconciliation.ReconciliationCaseSummaryDto;
using ReconciliationQueueAccountStatusDto = Meridian.Contracts.Reconciliation.ReconciliationQueueAccountStatusDto;
using StatementImportSummaryDto = Meridian.Contracts.Reconciliation.StatementImportSummaryDto;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_DataOperationsPayload_ReportsTheTenantsScopedCredentialReadiness()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-tenant-readiness", Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(dataRoot);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new { dataRoot }));
        var vault = new FileProviderCredentialStore(dataRoot);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterConfigStores(services, configPath);
            services.AddSingleton<IProviderCredentialStore>(vault);
            services.AddSingleton(NullLogger<ProviderConnectionLifecycleService>.Instance);
            services.AddSingleton<ProviderConnectionLifecycleService>();
            services.AddSingleton<ProviderConnectionService>();
        },
            currentUserPermissions: UserPermission.ManageCredentials | UserPermission.ViewHistoricalData);
        var connections = app.Services.GetRequiredService<ProviderConnectionService>();
        await connections.UpsertForTenantAsync(new CreateProviderConnectionRequest(
            ConnectionId: "polygon-owned", ProviderFamilyId: "polygon", DisplayName: "Owned Polygon",
            ExternalAccountId: "account-owned"), "tenant-test", "default");
        var scope = await connections.GetCredentialScopeForTenantAsync("polygon-owned", "tenant-test");
        await vault.SaveScopedAsync(new ProviderCredentialSaveRequest("polygon",
            new Dictionary<string, string?> { ["ApiKey"] = "owned-key" }, scope!.Environment), scope);

        using var dataOperations = await ReadJsonAsync(app.GetTestClient(), "/api/workstation/data-operations");

        var polygon = dataOperations.RootElement.GetProperty("providers").EnumerateArray()
            .Single(provider => provider.GetProperty("providerId").GetString() == "polygon");
        var summary = polygon.GetProperty("connectionSummary").Deserialize<ProviderConnectionRowDto>(ServerJsonOptions);
        summary!.CredentialState.Should().Be(ProviderCredentialStateDto.Configured,
            "the workstation reports the same tenant readiness as the providers endpoint and Settings");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DataOperationsPayload_ShouldAggregateMultipleRoutingConnectionsByProviderFamily()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-routing-summary", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(dataRoot);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new { dataRoot }));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterConfigStores(services, configPath);
            services.AddSingleton<IProviderCredentialStore>(_ => new FileProviderCredentialStore(dataRoot));
            services.AddSingleton(NullLogger<ProviderConnectionLifecycleService>.Instance);
            services.AddSingleton<ProviderConnectionLifecycleService>();
            services.AddSingleton<ProviderConnectionService>();
            services.AddSingleton<ProviderBindingService>();
        },
            currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ManageCredentials | UserPermission.ViewHistoricalData);

        var connectionService = app.Services.GetRequiredService<ProviderConnectionService>();
        var bindingService = app.Services.GetRequiredService<ProviderBindingService>();

        // Routing summaries follow the direct /api/provider-routing reads: only connections retained
        // for the request tenant contribute. The foreign and unassigned connections must not.
        await connectionService.UpsertForTenantAsync(new CreateProviderConnectionRequest(
            ConnectionId: "alpaca-paper",
            ProviderFamilyId: "alpaca",
            DisplayName: "Alpaca Paper",
            ConnectionType: "DataVendor",
            ConnectionMode: "Paper",
            Enabled: true,
            ExternalAccountId: "account-paper",
            ProductionReady: false), "tenant-test", "paper");
        await connectionService.UpsertForTenantAsync(new CreateProviderConnectionRequest(
            ConnectionId: "alpaca-live",
            ProviderFamilyId: "alpaca",
            DisplayName: "Alpaca Live",
            ConnectionType: "DataVendor",
            ConnectionMode: "Live",
            Enabled: true,
            ExternalAccountId: "account-live",
            ProductionReady: true), "tenant-test", "live");
        await connectionService.UpsertForTenantAsync(new CreateProviderConnectionRequest(
            ConnectionId: "alpaca-foreign",
            ProviderFamilyId: "alpaca",
            DisplayName: "Foreign Alpaca",
            ConnectionType: "DataVendor",
            ConnectionMode: "Live",
            Enabled: true,
            ExternalAccountId: "account-foreign",
            ProductionReady: true), "tenant-foreign", "live");
        await connectionService.UpsertAsync(new CreateProviderConnectionRequest(
            ConnectionId: "alpaca-unassigned",
            ProviderFamilyId: "alpaca",
            DisplayName: "Unassigned Alpaca",
            ConnectionType: "DataVendor",
            ConnectionMode: "Live",
            Enabled: true,
            ProductionReady: true));

        await bindingService.UpsertAsync(new UpdateProviderBindingRequest(
            BindingId: "alpaca-paper-historical",
            Capability: nameof(ProviderCapabilityKind.HistoricalBars),
            ConnectionId: "alpaca-paper",
            FailoverConnectionIds: ["alpaca-live"]));
        await bindingService.UpsertAsync(new UpdateProviderBindingRequest(
            BindingId: "alpaca-live-realtime",
            Capability: nameof(ProviderCapabilityKind.RealtimeMarketData),
            ConnectionId: "alpaca-live"));
        await bindingService.UpsertAsync(new UpdateProviderBindingRequest(
            BindingId: "alpaca-foreign-realtime",
            Capability: nameof(ProviderCapabilityKind.RealtimeMarketData),
            ConnectionId: "alpaca-foreign",
            FailoverConnectionIds: ["alpaca-unassigned"]));
        await bindingService.UpsertAsync(new UpdateProviderBindingRequest(
            BindingId: "alpaca-unassigned-historical",
            Capability: nameof(ProviderCapabilityKind.HistoricalBars),
            ConnectionId: "alpaca-unassigned"));

        using var dataOperations = await ReadJsonAsync(client: app.GetTestClient(), "/api/workstation/data-operations");
        dataOperations.RootElement.GetRawText().Should().NotContain("alpaca-foreign").And.NotContain("alpaca-unassigned");
        var providers = dataOperations.RootElement.GetProperty("providers").EnumerateArray().ToArray();

        providers.Count(provider => provider.GetProperty("providerId").GetString() == "alpaca").Should().Be(1);

        var alpaca = providers.First(provider => provider.GetProperty("providerId").GetString() == "alpaca");
        alpaca.GetProperty("connectionSummary").ValueKind.Should().Be(JsonValueKind.Object);
        var routingSummary = alpaca.GetProperty("routingSummary");
        routingSummary.GetProperty("connectionId").GetString().Should().Be("alpaca-live");
        routingSummary.GetProperty("providerFamilyId").GetString().Should().Be("alpaca");
        routingSummary.GetProperty("productionReady").GetBoolean().Should().BeTrue();
        routingSummary.GetProperty("bindingCount").GetInt32().Should().Be(2);
        routingSummary.GetProperty("fallbackRouteCount").GetInt32().Should().Be(1);

        alpaca.GetProperty("diagnostics").EnumerateArray()
            .Should()
            .Contain(item =>
                item.GetProperty("id").GetString() == "routing-readiness" &&
                item.GetProperty("detail").GetString()!.Contains("Bindings 2; fallback routes 1; production ready True.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DataOperationsPayload_WithoutManageCredentials_ShouldNotExposeConnectionSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-metrics-no-creds", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "_status"));
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, """{"DataRoot":"."}""");

        var metrics = new ProviderMetricsStatus(
            Timestamp: new DateTimeOffset(2026, 4, 24, 17, 0, 0, TimeSpan.Zero),
            Providers:
            [
                new ProviderMetrics(
                    ProviderId: "yahoo",
                    ProviderType: "Historical bars",
                    IsConnected: true,
                    TradesReceived: 0,
                    DepthUpdatesReceived: 0,
                    QuotesReceived: 2400,
                    ConnectionAttempts: 1,
                    ConnectionFailures: 0,
                    MessagesDropped: 0,
                    ActiveSubscriptions: 4,
                    AverageLatencyMs: 42,
                    MinLatencyMs: 25,
                    MaxLatencyMs: 80,
                    DataQualityScore: 0.96,
                    ConnectionSuccessRate: 1,
                    Timestamp: new DateTimeOffset(2026, 4, 24, 16, 59, 0, TimeSpan.Zero))
            ],
            TotalProviders: 1,
            HealthyProviders: 1);
        await File.WriteAllTextAsync(
            Path.Combine(root, "_status", "providers.json"),
            JsonSerializer.Serialize(metrics, ServerJsonOptions));

        // Default test permissions do not include ManageCredentials, so connection summaries
        // must stay hidden even though real provider metrics are available.
        await using var app = await CreateAppAsync(
            services => RegisterConfigStores(services, configPath),
            currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ViewHistoricalData);
        using var dataOperations = await ReadJsonAsync(app.GetTestClient(), "/api/workstation/data-operations");
        var providers = dataOperations.RootElement.GetProperty("providers").EnumerateArray().ToArray();

        providers.Should().NotBeEmpty();
        providers.Should().OnlyContain(provider =>
            provider.GetProperty("connectionSummary").ValueKind == JsonValueKind.Null ||
            provider.GetProperty("connectionSummary").ValueKind == JsonValueKind.Undefined);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_KernelObservability_ShouldSurfaceActiveAndHistoricalAlertMetrics()
    {
        var observability = CreateRecoveredKernelObservability();
        await using var app = await CreateAppAsync(services =>
        {
            // Real read services keep the data/governance surfaces live; without them these
            // endpoints honestly return 503 instead of fabricated fallback payloads.
            RegisterRunReadServices(services);
            services.AddSingleton(observability);
        }, currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ViewHistoricalData);

        var client = app.GetTestClient();

        using var dataOperations = await ReadJsonAsync(client, "/api/workstation/data-operations");
        dataOperations.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric =>
                metric.GetProperty("id").GetString() == "kernel-critical-jumps" &&
                metric.GetProperty("value").GetString() == "0" &&
                metric.GetProperty("delta").GetString() == "1 total" &&
                metric.GetProperty("tone").GetString() == "success");

        var dataOpsKernel = dataOperations.RootElement.GetProperty("kernelObservability");
        dataOpsKernel.GetProperty("activeAlerts").GetInt32().Should().Be(0);
        dataOpsKernel.GetProperty("totalAlerts").GetInt32().Should().Be(1);
        dataOpsKernel.GetProperty("alerts").GetInt32().Should().Be(1);
        dataOpsKernel.GetProperty("determinismChecksEnabled").GetBoolean().Should().BeTrue();
        dataOpsKernel.GetProperty("domains").GetArrayLength().Should().Be(1);

        var domain = dataOpsKernel.GetProperty("domains")[0];
        domain.GetProperty("domain").GetString().Should().Be(nameof(ProviderCapabilityKind.HistoricalBars));
        domain.GetProperty("throughputPerMinute").GetDouble().Should().BeGreaterThan(0);
        domain.GetProperty("latencyMs").GetProperty("p95").GetDouble()
            .Should()
            .BeGreaterThanOrEqualTo(domain.GetProperty("latencyMs").GetProperty("p50").GetDouble());
        domain.GetProperty("drift").GetProperty("methodology").GetString().Should().Be("totalVariationDistance");
        domain.GetProperty("lastUpdatedUtc").ValueKind.Should().Be(JsonValueKind.String);

        var criticalSeverityRate = domain.GetProperty("criticalSeverityRate");
        criticalSeverityRate.GetProperty("jumpAlertActive").GetBoolean().Should().BeFalse();
        criticalSeverityRate.GetProperty("jumpAlertCount").GetInt32().Should().Be(1);
        criticalSeverityRate.GetProperty("shortWindowSamples").GetInt32().Should().Be(30);
        criticalSeverityRate.GetProperty("longWindowSamples").GetInt32().Should().Be(90);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("minimumSampleCount").GetInt32().Should().Be(20);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("minimumShortRate").GetDouble().Should().Be(0.25);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("zeroBaselineShortRate").GetDouble().Should().Be(0.35);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("relativeMultiplier").GetDouble().Should().Be(2.0);
        criticalSeverityRate.GetProperty("alertThresholds").GetProperty("absoluteIncrease").GetDouble().Should().Be(0.15);

        using var governance = await ReadJsonAsync(client, "/api/workstation/governance");
        governance.RootElement.GetProperty("metrics").EnumerateArray()
            .Should()
            .Contain(metric =>
                metric.GetProperty("id").GetString() == "kernel-critical-jumps" &&
                metric.GetProperty("value").GetString() == "0" &&
                metric.GetProperty("tone").GetString() == "success");

        var governanceKernel = governance.RootElement.GetProperty("kernelObservability");
        governanceKernel.GetProperty("activeAlerts").GetInt32().Should().Be(0);
        governanceKernel.GetProperty("totalAlerts").GetInt32().Should().Be(1);
        governanceKernel.GetProperty("domains")[0]
            .GetProperty("criticalSeverityRate")
            .GetProperty("jumpAlertCount")
            .GetInt32()
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_WithStrategyReadService_ShouldReturnTypedStrategyBriefing()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-latest",
            strategyId: "carry-1",
            strategyName: "Carry Pair",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 16, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/fx/spot",
            feedReference: "synthetic:fx"));
        await store.RecordRunAsync(BuildRun(
            runId: "run-prior",
            strategyId: "meanrev-1",
            strategyName: "Mean Reversion",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 14, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/us/equities",
            feedReference: "synthetic:equities"));

        var client = app.GetTestClient();
        var response = await client.GetAsync(UiApiRoutes.WorkstationStrategyBriefing);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var briefing = await response.Content.ReadFromJsonAsync<StrategyBriefingDto>(ServerJsonOptions);

        briefing.Should().NotBeNull();
        briefing!.Workspace.TotalRuns.Should().Be(2);
        briefing.Workspace.LatestRunId.Should().Be("run-latest");
        briefing.Workspace.HasLedgerCoverage.Should().BeTrue();
        briefing.Workspace.Summary.Should().Contain("active Strategy session(s)");
        briefing.Workspace.Summary.Should().NotContain("active research session(s)");
        briefing.InsightFeed.Widgets.Should().HaveCount(2);
        briefing.RecentRuns.Should().HaveCount(2);
        briefing.RecentRuns[0].RunId.Should().Be("run-latest");
        briefing.RecentRuns[0].DrillIn.Continuity.Should().Be("/api/workstation/runs/run-latest/continuity");
        briefing.SavedComparisons.Should().NotBeEmpty();
        briefing.Alerts.Should().NotBeEmpty();
        briefing.Watchlists.Should().BeEmpty();
    }
}
