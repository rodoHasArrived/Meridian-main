using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ExecutionGovernanceEndpointsTests
{
    private const string ApprovedLiveRunId = "run-live";
    private static readonly Guid LiveFundAccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");

    [Fact]
    public async Task ControlsEndpoints_UpdateCircuitBreakerAndExposeAuditTrail()
    {
        var tempRoot = CreateTempRoot();
        var oms = Substitute.For<IOrderManager>();
        oms.GetOpenOrders().Returns(Array.Empty<OrderState>());

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")));
            services.AddSingleton(new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")));
            services.AddSingleton<ExecutionAuditTrailService>();
            services.AddSingleton<ExecutionOperatorControlService>();
            services.AddSingleton(oms);
        });

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Meridian-Actor", "ops");

        var response = await client.PostAsync(
            "/api/execution/controls/circuit-breaker",
            JsonContent(new { isOpen = true, reason = "manual halt" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await oms.Received(1).CancelAllAsync(Arg.Any<CancellationToken>());

        var controlsResponse = await client.GetAsync("/api/execution/controls");
        controlsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var controlsJson = JsonDocument.Parse(await controlsResponse.Content.ReadAsStringAsync());
        controlsJson.RootElement.GetProperty("circuitBreaker").GetProperty("isOpen").GetBoolean().Should().BeTrue();

        var auditResponse = await client.GetAsync("/api/execution/audit?take=10");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditEntries = JsonSerializer.Deserialize<ExecutionAuditEntry[]>(
            await auditResponse.Content.ReadAsStringAsync(),
            JsonOptions());

        auditEntries.Should().NotBeNull();
        auditEntries!.Should().Contain(entry =>
            entry.Action == "CircuitBreakerOpened" &&
            entry.Actor == "ops-user");
        auditEntries.Should().Contain(entry =>
            entry.Action == "CircuitBreakerCancelAll" &&
            entry.Outcome == "Completed",
            because: "opening the breaker is the kill switch and must sweep the open book with an audited outcome");
    }

    [Fact]
    public async Task ControlsEndpoints_UpdatePositionLimitsAndExposeAuditTrail()
    {
        var tempRoot = CreateTempRoot();

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")));
            services.AddSingleton(new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")));
            services.AddSingleton<ExecutionAuditTrailService>();
            services.AddSingleton<ExecutionOperatorControlService>();
        });

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Meridian-Actor", "ops");

        var defaultResponse = await client.PostAsync(
            "/api/execution/controls/position-limits/default",
            JsonContent(new { maxPositionSize = 75m, reason = "desk risk cap" }));
        defaultResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var symbolResponse = await client.PostAsync(
            "/api/execution/controls/position-limits/aapl",
            JsonContent(new { maxPositionSize = 10m, reason = "event risk cap" }));
        symbolResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var controlsResponse = await client.GetAsync("/api/execution/controls");
        controlsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var controls = JsonSerializer.Deserialize<ExecutionControlSnapshot>(
            await controlsResponse.Content.ReadAsStringAsync(),
            JsonOptions());

        controls.Should().NotBeNull();
        controls!.DefaultMaxPositionSize.Should().Be(75m);
        controls.SymbolPositionLimits.Should().ContainKey("AAPL").WhoseValue.Should().Be(10m);

        var auditResponse = await client.GetAsync("/api/execution/audit?take=10");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditEntries = JsonSerializer.Deserialize<ExecutionAuditEntry[]>(
            await auditResponse.Content.ReadAsStringAsync(),
            JsonOptions());

        auditEntries.Should().NotBeNull();
        auditEntries!.Should().Contain(entry =>
            entry.Action == "DefaultPositionLimitUpdated" &&
            entry.Actor == "ops-user" &&
            entry.Metadata != null &&
            entry.Metadata["limit"] == "75");
        auditEntries.Should().Contain(entry =>
            entry.Action == "SymbolPositionLimitUpdated" &&
            entry.Actor == "ops-user" &&
            entry.Symbol == "AAPL" &&
            entry.Metadata != null &&
            entry.Metadata["limit"] == "10");
    }

    [Fact]
    public async Task AuditSearchEndpoint_ReturnsSharedCrossObjectTimeline()
    {
        var tempRoot = CreateTempRoot();

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")));
            services.AddSingleton<ExecutionAuditTrailService>();
            services.AddSingleton<AuditTrailExplorerService>();
        });

        await app.Services.GetRequiredService<ExecutionAuditTrailService>().RecordAsync(new ExecutionAuditEntry(
            AuditId: "audit-promotion-approved",
            Category: "Promotion",
            Action: "LivePromotionApproved",
            Outcome: "Approved",
            OccurredAt: DateTimeOffset.Parse("2026-05-28T18:00:00Z"),
            Actor: "approver",
            RunId: "run-live",
            CorrelationId: "corr-promotion",
            Message: "Approved for live route"));
        await app.Services.GetRequiredService<ExecutionAuditTrailService>().RecordAsync(new ExecutionAuditEntry(
            AuditId: "audit-order-submitted",
            Category: "Order",
            Action: "OrderSubmitted",
            Outcome: "Accepted",
            OccurredAt: DateTimeOffset.Parse("2026-05-28T18:05:00Z"),
            Actor: "trader",
            OrderId: "order-live-1",
            RunId: "run-live",
            Symbol: "AAPL",
            CorrelationId: "corr-order",
            Metadata: new Dictionary<string, string> { ["gateway"] = "alpaca" }));

        var response = await app.GetTestClient().GetAsync("/api/execution/audit/search?searchText=alpaca&runId=run-live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = JsonSerializer.Deserialize<AuditTrailExplorerResultDto>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions());

        result.Should().NotBeNull();
        result!.TotalMatched.Should().Be(1);
        result.Entries.Should().ContainSingle(entry =>
            entry.AuditId == "audit-order-submitted" &&
            entry.ObjectKind == "Order" &&
            entry.ObjectId == "order-live-1" &&
            entry.RelatedObjectIds!.Contains("run-live") &&
            entry.ActionLedgerSource == "ExecutionAuditTrail" &&
            entry.ActionLedgerSequence == 2 &&
            entry.CurrentActionHash != null &&
            entry.CurrentActionHash.Length == 64 &&
            entry.ActionLedgerStatus == "WalRetained");
    }

    [Fact]
    public async Task ControlsEndpoints_CreateAndClearManualOverride_PreserveAuditMetadata()
    {
        var tempRoot = CreateTempRoot();

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")));
            services.AddSingleton(new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")));
            services.AddSingleton<ExecutionAuditTrailService>();
            services.AddSingleton<ExecutionOperatorControlService>();
        });

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Meridian-Actor", "ops");

        var createResponse = await client.PostAsync(
            "/api/execution/controls/manual-overrides",
            JsonContent(new
            {
                kind = ExecutionManualOverrideKinds.AllowLivePromotion,
                reason = "Risk review completed",
                strategyId = "strategy-live",
                runId = "run-123",
                correlationId = "corr-override-create"
            }));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdOverride = JsonSerializer.Deserialize<ExecutionManualOverride>(
            await createResponse.Content.ReadAsStringAsync(),
            JsonOptions());
        createdOverride.Should().NotBeNull();

        var clearResponse = await client.PostAsync(
            $"/api/execution/controls/manual-overrides/{createdOverride!.OverrideId}/clear",
            JsonContent(new
            {
                reason = "Approval window closed",
                correlationId = "corr-override-clear"
            }));

        clearResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var controlsResponse = await client.GetAsync("/api/execution/controls");
        controlsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var controls = JsonSerializer.Deserialize<ExecutionControlSnapshot>(
            await controlsResponse.Content.ReadAsStringAsync(),
            JsonOptions());
        controls.Should().NotBeNull();
        controls!.ManualOverrides.Should().BeEmpty();
        var auditResponse = await client.GetAsync("/api/execution/audit?take=10");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditEntries = JsonSerializer.Deserialize<ExecutionAuditEntry[]>(
            await auditResponse.Content.ReadAsStringAsync(),
            JsonOptions());

        auditEntries.Should().NotBeNull();
        auditEntries!.Should().Contain(entry =>
            entry.Action == "ManualOverrideCreated" &&
            entry.Actor == "ops-user" &&
            entry.RunId == "run-123" &&
            entry.CorrelationId == "corr-override-create");
        auditEntries.Should().Contain(entry =>
            entry.Action == "ManualOverrideCleared" &&
            entry.Actor == "ops-user" &&
            entry.RunId == "run-123" &&
            entry.CorrelationId == "corr-override-clear");
    }

    [Fact]
    public async Task ControlsEndpoints_ManualOverrideMutations_ForbidReadOnlyPermissions()
    {
        var tempRoot = CreateTempRoot();

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")));
            services.AddSingleton(new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")));
            services.AddSingleton<ExecutionAuditTrailService>();
            services.AddSingleton<ExecutionOperatorControlService>();
        }, RolePermissions.For(UserRole.ReadOnly));

        var client = app.GetTestClient();
        var createResponse = await client.PostAsync(
            "/api/execution/controls/manual-overrides",
            JsonContent(new
            {
                kind = ExecutionManualOverrideKinds.AllowLivePromotion,
                reason = "Risk review completed",
                strategyId = "strategy-live",
                runId = "run-123"
            }));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam()
    {
        var tempRoot = CreateTempRoot();
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildOrderResponse() },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildAccountResponse() }
        });

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(new SequentialStubHandler(responses)));
            services.AddSingleton(new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")));
            services.AddSingleton(new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")));
            services.AddSingleton<ExecutionAuditTrailService>();
            services.AddSingleton<ExecutionOperatorControlService>();
            services.AddSingleton<IPortfolioState, EmptyPortfolioState>();
            services.AddSingleton<ILiveOrderReadinessGate>(_ => new ApprovedLiveOrderReadinessGate(ApprovedLiveRunId));
            RegisterFundAccountScope(services, LiveFundAccountId);
            services.AddSingleton(sp => new AlpacaBrokerageGateway(
                sp.GetRequiredService<IHttpClientFactory>(),
                new Meridian.Core.Config.AlpacaOptions(KeyId: "test-key", SecretKey: "test-secret"),
                NullLogger<AlpacaBrokerageGateway>.Instance));
            services.AddBrokerageGateway("alpaca", sp => sp.GetRequiredService<AlpacaBrokerageGateway>());
            services.AddBrokerageExecution(config => ConfigureReadyBrokerage(config, "alpaca"));
        });

        await app.Services.GetRequiredService<AlpacaBrokerageGateway>().ConnectAsync();

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Meridian-Actor", "ops");

        var submitResponse = await client.PostAsync(
            "/api/execution/orders/submit",
            JsonContent(new
            {
                symbol = "AAPL",
                side = 0,
                type = 0,
                timeInForce = 0,
                quantity = 1,
                strategyId = "strategy-live",
                fundAccountId = LiveFundAccountId,
                metadata = new Dictionary<string, string>
                {
                    ["runId"] = ApprovedLiveRunId
                }
            }));

        submitResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var submitJson = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync());
        submitJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        var healthResponse = await client.GetAsync("/api/execution/health");
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var healthJson = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync());
        healthJson.RootElement.GetProperty("brokerName").GetString().Should().Be("Alpaca Markets");
        healthJson.RootElement.GetProperty("selectedGatewayId").GetString().Should().Be("alpaca");

        var auditResponse = await client.GetAsync("/api/execution/audit?take=10");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditEntries = JsonSerializer.Deserialize<ExecutionAuditEntry[]>(
            await auditResponse.Content.ReadAsStringAsync(),
            JsonOptions());

        auditEntries.Should().NotBeNull();
        auditEntries!.Should().Contain(entry =>
            entry.Action == "OrderSubmitted" &&
            entry.BrokerName == "alpaca" &&
            entry.Symbol == "AAPL");
    }

    [Fact]
    public async Task RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam()
    {
        var tempRoot = CreateTempRoot();
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildRobinhoodAccountListResponse() },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildRobinhoodInstrumentListResponse() },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildRobinhoodAccountListResponse() },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildRobinhoodOrderResponse() },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = BuildRobinhoodAccountListResponse() }
        });

        await using var app = await CreateAppAsync(services =>
        {
            services.AddLogging();
            services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(new SequentialStubHandler(responses)));
            services.AddSingleton(new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")));
            services.AddSingleton(new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")));
            services.AddSingleton<ExecutionAuditTrailService>();
            services.AddSingleton<ExecutionOperatorControlService>();
            services.AddSingleton<IPortfolioState, EmptyPortfolioState>();
            services.AddSingleton<ILiveOrderReadinessGate>(_ => new ApprovedLiveOrderReadinessGate(ApprovedLiveRunId));
            RegisterFundAccountScope(services, LiveFundAccountId);
            services.AddSingleton(sp => new RobinhoodBrokerageGateway(
                sp.GetRequiredService<IHttpClientFactory>(),
                NullLogger<RobinhoodBrokerageGateway>.Instance,
                accessToken: "test-token"));
            services.AddHostedBrokerageGateways();
            services.AddBrokerageExecution(config => ConfigureReadyBrokerage(config, "robinhood"));
        });

        await app.Services.GetRequiredService<RobinhoodBrokerageGateway>().ConnectAsync();

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Meridian-Actor", "ops");

        var submitResponse = await client.PostAsync(
            "/api/execution/orders/submit",
            JsonContent(new
            {
                symbol = "AAPL",
                side = 0,
                type = 0,
                timeInForce = 0,
                quantity = 1,
                strategyId = "strategy-live",
                fundAccountId = LiveFundAccountId,
                metadata = new Dictionary<string, string>
                {
                    ["runId"] = ApprovedLiveRunId
                }
            }));

        submitResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var submitJson = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync());
        submitJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        var healthResponse = await client.GetAsync("/api/execution/health");
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var healthJson = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync());
        healthJson.RootElement.GetProperty("brokerName").GetString().Should().Be("Robinhood (unofficial)");
        healthJson.RootElement.GetProperty("selectedGatewayId").GetString().Should().Be("robinhood");

        var auditResponse = await client.GetAsync("/api/execution/audit?take=10");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditEntries = JsonSerializer.Deserialize<ExecutionAuditEntry[]>(
            await auditResponse.Content.ReadAsStringAsync(),
            JsonOptions());

        auditEntries.Should().NotBeNull();
        auditEntries!.Should().Contain(entry =>
            entry.Action == "OrderSubmitted" &&
            entry.BrokerName == "robinhood" &&
            entry.Symbol == "AAPL");
    }

    [Fact]
    public async Task SubmitOrder_WithoutManageOrdersPermission_ReturnsForbidden()
    {
        var tempRoot = CreateTempRoot();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")));
            services.AddSingleton(new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")));
            services.AddSingleton<ExecutionAuditTrailService>();
            services.AddSingleton<ExecutionOperatorControlService>();
            services.AddSingleton<IPortfolioState, EmptyPortfolioState>();
            services.AddSingleton<IExecutionGateway, PaperTradingGateway>();
            services.AddSingleton<IOrderManager>(sp => new OrderManagementSystem(
                sp.GetRequiredService<IExecutionGateway>(),
                NullLogger<OrderManagementSystem>.Instance));
        }, permissions: UserPermission.ExecuteTrades);

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/submit", JsonContent(new
        {
            symbol = "AAPL",
            side = 0,
            type = 0,
            timeInForce = 0,
            quantity = 1
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SubmitOrder_WithRestrictedBrokerRoutingMetadataAlias_ReturnsForbidden()
    {
        var tempRoot = CreateTempRoot();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")));
            services.AddSingleton(new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")));
            services.AddSingleton<ExecutionAuditTrailService>();
            services.AddSingleton<ExecutionOperatorControlService>();
            services.AddSingleton<IPortfolioState, EmptyPortfolioState>();
            services.AddSingleton<IExecutionGateway, PaperTradingGateway>();
            services.AddSingleton<IOrderManager>(sp => new OrderManagementSystem(
                sp.GetRequiredService<IExecutionGateway>(),
                NullLogger<OrderManagementSystem>.Instance));
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/submit", JsonContent(new
        {
            symbol = "AAPL",
            side = 0,
            type = 0,
            timeInForce = 0,
            quantity = 1,
            metadata = new Dictionary<string, string>
            {
                ["brokerAccountId"] = "acct-123"
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var result = JsonSerializer.Deserialize<OrderResult>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions());
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitOrder_WithFixedIncomeRoutingMetadata_IsRejected()
    {
        var tempRoot = CreateTempRoot();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")));
            services.AddSingleton(new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")));
            services.AddSingleton<ExecutionAuditTrailService>();
            services.AddSingleton<ExecutionOperatorControlService>();
            services.AddSingleton<IPortfolioState, EmptyPortfolioState>();
            services.AddSingleton<IExecutionGateway, PaperTradingGateway>();
            services.AddSingleton<IOrderManager>(sp => new OrderManagementSystem(
                sp.GetRequiredService<IExecutionGateway>(),
                NullLogger<OrderManagementSystem>.Instance));
        });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/submit", JsonContent(new
        {
            symbol = "AAPL",
            side = 0,
            type = 0,
            timeInForce = 0,
            quantity = 1,
            metadata = new Dictionary<string, string>
            {
                ["asset_class"] = "treasury",
                ["broker_account_id"] = "acct-123"
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var result = JsonSerializer.Deserialize<OrderResult>(
            await response.Content.ReadAsStringAsync(),
            JsonOptions());
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
    }

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection> configureServices,
        UserPermission permissions = UserPermission.ExecuteTrades | UserPermission.ManageOrders)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        configureServices(builder.Services);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "ops-user";
            await next(context);
        });
        app.MapExecutionEndpoints(JsonOptions());
        await app.StartAsync();
        return app;
    }

    private static void ConfigureReadyBrokerage(BrokerageConfiguration config, string gatewayId)
    {
        config.Gateway = gatewayId;
        config.LiveExecutionEnabled = true;
        config.MaxPositionSize = 100m;
        config.ReadOnlyPhaseEnabled = true;
        config.PaperTradingPhaseEnabled = true;
        config.ProductionRoutingPhaseEnabled = true;
        config.ReadOnlyVerificationPassed = true;
        config.PaperLifecycleTestsPassed = true;
        config.ReplayEvidencePassed = true;
        config.BrokerFlows[gatewayId] = new BrokerFlowFlags
        {
            ReadOnlyDataEnabled = true,
            PaperOrderFlowEnabled = true,
            ProductionOrderRoutingEnabled = true
        };
        config.ValidationGates = new BrokerValidationGateOptions
        {
            RequireValidationArtifactsForOrderPlacement = false
        };
    }

    private static void RegisterFundAccountScope(IServiceCollection services, Guid fundAccountId)
    {
        services.AddSingleton<IAccountQueryService>(new StubAccountQueryService(fundAccountId));
        services.AddSingleton<IScopedAuthorizationService>(
            new StubScopedAuthorizationService(fundAccountId));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static StringContent BuildAccountResponse() =>
        JsonContent(new
        {
            account_number = "ACC-123",
            equity = "100000.00",
            cash = "80000.00",
            buying_power = "160000.00",
            currency = "USD",
            status = "active"
        });

    private static StringContent BuildOrderResponse() =>
        JsonContent(new
        {
            id = "alpaca-order-1",
            client_order_id = "client-order-1",
            symbol = "AAPL",
            side = "buy",
            type = "market",
            qty = "1",
            filled_qty = "0",
            status = "accepted",
            created_at = "2026-04-05T14:30:00Z"
        });

    private static StringContent BuildRobinhoodAccountListResponse() =>
        JsonContent(new
        {
            results = new[]
            {
                new
                {
                    url = "https://api.robinhood.com/accounts/ACC-123/",
                    account_number = "ACC-123",
                    equity = "100000.00",
                    cash = "80000.00",
                    buying_power = "160000.00",
                    deactivated = false
                }
            }
        });

    private static StringContent BuildRobinhoodInstrumentListResponse() =>
        JsonContent(new
        {
            results = new[]
            {
                new
                {
                    url = "https://api.robinhood.com/instruments/AAPL/",
                    symbol = "AAPL"
                }
            }
        });

    private static StringContent BuildRobinhoodOrderResponse() =>
        JsonContent(new
        {
            id = "robinhood-order-1",
            ref_id = "client-order-1",
            symbol = "AAPL",
            side = "buy",
            type = "market",
            time_in_force = "gfd",
            quantity = "1",
            state = "confirmed",
            created_at = "2026-04-05T14:30:00Z"
        });

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false);
    }

    private sealed class SequentialStubHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                responses.Count > 0
                    ? responses.Dequeue()
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(new { }) });
        }
    }

    private sealed class EmptyPortfolioState : IPortfolioState
    {
        public decimal Cash => 100_000m;
        public decimal PortfolioValue => 100_000m;
        public decimal UnrealisedPnl => 0m;
        public decimal RealisedPnl => 0m;
        public IReadOnlyDictionary<string, IPosition> Positions { get; } =
            new Dictionary<string, IPosition>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ApprovedLiveOrderReadinessGate(string approvedRunId) : ILiveOrderReadinessGate
    {
        public Task<LiveOrderReadinessDecision> EvaluateAsync(
            LiveOrderReadinessRequest request,
            CancellationToken ct = default)
        {
            var decision = string.Equals(request.RunId, approvedRunId, StringComparison.Ordinal)
                ? LiveOrderReadinessDecision.Approved($"audit://live/{request.RunId}")
                : LiveOrderReadinessDecision.Rejected($"Run {request.RunId} is not approved for live order routing.");

            return Task.FromResult(decision);
        }
    }

    private sealed class StubScopedAuthorizationService(Guid allowedAccountId) : IScopedAuthorizationService
    {
        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
            string actor,
            UserPermission requiredPermission,
            AccessScopeKindDto scopeKind,
            Guid? scopeId,
            UserPermission globalPermissions,
            CancellationToken ct = default)
        {
            var allowed = scopeKind == AccessScopeKindDto.Account
                && scopeId == allowedAccountId
                && globalPermissions.HasFlag(requiredPermission);

            return Task.FromResult(new ScopedAuthorizationDecisionDto(
                allowed,
                actor,
                requiredPermission,
                scopeKind,
                scopeId,
                allowed ? "Matched test account scope." : "No matching test account scope."));
        }
    }

    private sealed class StubAccountQueryService(Guid accountId) : IAccountQueryService
    {
        private readonly AccountSummaryDto _account = new(
            accountId,
            AccountTypeDto.Brokerage,
            EntityId: null,
            FundId: null,
            SleeveId: null,
            VehicleId: null,
            AccountCode: "TEST-BROKERAGE",
            DisplayName: "Test Brokerage Account",
            BaseCurrency: "USD",
            Institution: "Test Broker",
            IsActive: true,
            EffectiveFrom: DateTimeOffset.UtcNow,
            EffectiveTo: null,
            PortfolioId: null,
            LedgerReference: null,
            StrategyId: null,
            RunId: null);

        public Task<AccountSummaryDto?> GetAccountAsync(
            Guid requestedAccountId,
            CancellationToken ct = default)
            => Task.FromResult(requestedAccountId == _account.AccountId ? _account : null);

        public Task<IReadOnlyList<AccountSummaryDto>> ListAccountsAsync(
            AccountTypeDto? accountType,
            bool? isActive,
            string? currency,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccountSummaryDto>>([_account]);

        public Task<FundAccountsDto> GetFundAccountsAsync(Guid fundId, CancellationToken ct = default)
            => Task.FromResult(new FundAccountsDto(fundId, [], [], [_account], []));

        public Task<IReadOnlyList<AccountSettlementInstructionView>> ListSettlementInstructionsAsync(
            Guid? accountId = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccountSettlementInstructionView>>([]);

        public Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceTimelineAsync(
            Guid accountId,
            DateOnly? fromDate = null,
            DateOnly? toDate = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccountBalanceSnapshotDto>>([]);

        public Task<AccountBalanceSnapshotDto?> GetLatestBalanceSnapshotAsync(
            Guid accountId,
            CancellationToken ct = default)
            => Task.FromResult<AccountBalanceSnapshotDto?>(null);

        public Task<IReadOnlyList<AccountOpenBreakView>> ListOpenBreaksAsync(
            Guid? accountId = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccountOpenBreakView>>([]);

        public Task<IReadOnlyList<CustodianPositionLineDto>> GetCustodianPositionsAsync(
            Guid accountId,
            DateOnly asOfDate,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CustodianPositionLineDto>>([]);

        public Task<IReadOnlyList<BankStatementLineDto>> GetBankStatementLinesAsync(
            Guid accountId,
            DateOnly? fromDate = null,
            DateOnly? toDate = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BankStatementLineDto>>([]);

        public Task<IReadOnlyList<AccountReconciliationRunDto>> GetReconciliationRunsAsync(
            Guid accountId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccountReconciliationRunDto>>([]);

        public Task<IReadOnlyList<AccountReconciliationResultDto>> GetReconciliationResultsAsync(
            Guid reconciliationRunId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccountReconciliationResultDto>>([]);

        public Task<IReadOnlyList<AccountSyncHistoryEntryDto>> GetSyncHistoryAsync(
            Guid accountId,
            string? capability = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccountSyncHistoryEntryDto>>([]);

        public Task<AccountSyncHistoryEntryDto?> GetLatestSyncHistoryAsync(
            Guid accountId,
            string? capability = null,
            CancellationToken ct = default)
            => Task.FromResult<AccountSyncHistoryEntryDto?>(null);

        public Task<AccountReadinessSnapshotDto?> GetReadinessAsync(
            Guid accountId,
            CancellationToken ct = default)
            => Task.FromResult<AccountReadinessSnapshotDto?>(null);

        public Task<IReadOnlyList<MarginSnapshotDto>> GetMarginSnapshotsAsync(
            Guid accountId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MarginSnapshotDto>>([]);

        public Task<MarginSnapshotDto?> GetLatestMarginSnapshotAsync(
            Guid accountId,
            CancellationToken ct = default)
            => Task.FromResult<MarginSnapshotDto?>(null);
    }
}
