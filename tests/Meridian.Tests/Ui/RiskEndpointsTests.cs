using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class RiskEndpointsTests
{
    [Fact]
    public async Task RiskEndpoints_ListStatusAndConfigLifecycle_WorkAsExpected()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IPositionTracker, StaticPositionTracker>();
            services.AddSingleton(new ExecutionOperatorControlOptions(Path.Combine(Path.GetTempPath(), $"execution-controls-{Guid.NewGuid():N}")));
            services.AddSingleton<ExecutionOperatorControlService>();
            services.AddSingleton<RiskRuleRuntimeService>();
            services.AddSingleton(new RiskRuleRuntimeOptions(Path.Combine(Path.GetTempPath(), $"risk-rules-{Guid.NewGuid():N}.json")));
        });

        var client = app.GetTestClient();

        var rulesResponse = await client.GetAsync("/api/risk/rules");
        rulesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rules = JsonSerializer.Deserialize<RiskRuleStatusDto[]>(await rulesResponse.Content.ReadAsStringAsync(), JsonOptions());
        rules.Should().NotBeNull();
        rules!.Should().Contain(rule => rule.RuleName == "PositionLimit");
        rules.Should().Contain(rule => rule.RuleName == "DrawdownCircuitBreaker");
        rules.Should().Contain(rule => rule.RuleName == "OrderRateThrottle");
        rules.Should().Contain(rule => rule.RuleName == "GrossExposure");
        rules.Should().Contain(rule => rule.RuleName == "SymbolConcentration");
        rules.Should().Contain(rule => rule.RuleName == "OrderNotional");
        rules.Should().OnlyContain(rule => !string.IsNullOrWhiteSpace(rule.Severity), "each guardrail declares its enforced severity outcome");

        var updateResponse = await client.PutAsync(
            "/api/risk/rules/PositionLimit/config",
            JsonContent(new
            {
                defaultMaxPositionSize = 50m,
                reason = "Operator tuning"
            }));

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var configResponse = await client.GetAsync("/api/risk/rules/PositionLimit/config");
        configResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var config = JsonSerializer.Deserialize<RiskRuleConfigDto>(await configResponse.Content.ReadAsStringAsync(), JsonOptions());
        config.Should().NotBeNull();
        config!.DefaultMaxPositionSize.Should().Be(50m);

        // Portfolio-aware rule config lifecycle: tune the notional bands, read them back.
        var notionalUpdate = await client.PutAsync(
            "/api/risk/rules/OrderNotional/config",
            JsonContent(new
            {
                maxOrderNotional = 250_000m,
                escalateOrderNotional = 50_000m,
                reason = "Escalation band setup"
            }));
        notionalUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        var notionalConfig = JsonSerializer.Deserialize<RiskRuleConfigDto>(
            await (await client.GetAsync("/api/risk/rules/OrderNotional/config")).Content.ReadAsStringAsync(),
            JsonOptions());
        notionalConfig!.MaxOrderNotional.Should().Be(250_000m);
        notionalConfig.EscalateOrderNotional.Should().Be(50_000m);

        // An inverted band (escalate ≥ reject ceiling) is rejected.
        var invertedBand = await client.PutAsync(
            "/api/risk/rules/OrderNotional/config",
            JsonContent(new { escalateOrderNotional = 400_000m }));
        invertedBand.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var concentrationUpdate = await client.PutAsync(
            "/api/risk/rules/SymbolConcentration/config",
            JsonContent(new { maxSymbolConcentrationPercent = 25m }));
        concentrationUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        var grossUpdate = await client.PutAsync(
            "/api/risk/rules/GrossExposure/config",
            JsonContent(new { maxGrossExposure = 500_000m }));
        grossUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RiskEscalations_ApproveReleasesParkedOrderThroughRiskGate()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new RiskRuleRuntimeOptions(Path.Combine(Path.GetTempPath(), $"risk-rules-{Guid.NewGuid():N}.json")));
            services.AddSingleton<PaperTradingPortfolio>(_ => new PaperTradingPortfolio(100_000m));
            services.AddSingleton<IPortfolioState>(sp => sp.GetRequiredService<PaperTradingPortfolio>());
            services.AddSingleton<IExecutionGateway>(_ => new Meridian.Execution.PaperTradingGateway(
                NullLogger<Meridian.Execution.PaperTradingGateway>.Instance,
                options: new Meridian.Execution.Adapters.PaperTradingGatewayOptions { AllowScaffoldMarketFills = true }));
            services.AddSingleton<RiskRuleRuntimeService>();
            services.AddSingleton(new RiskEscalationQueueService(
                NullLogger<RiskEscalationQueueService>.Instance,
                options: new RiskEscalationQueueOptions(
                    Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"escalations-{Guid.NewGuid():N}", "escalations.json"))));
            services.AddSingleton<Meridian.Risk.IPortfolioExposureProvider>(new StaticExposureProvider());
            services.AddSingleton<IRiskValidator>(sp =>
            {
                var runtime = sp.GetRequiredService<RiskRuleRuntimeService>();
                return new Meridian.Risk.CompositeRiskValidator(
                    [
                        new Meridian.Risk.Rules.OrderNotionalRule(
                            sp.GetRequiredService<Meridian.Risk.IPortfolioExposureProvider>(),
                            () => runtime.MaxOrderNotional,
                            () => runtime.EscalateOrderNotional,
                            NullLogger<Meridian.Risk.Rules.OrderNotionalRule>.Instance),
                    ],
                    NullLogger<Meridian.Risk.CompositeRiskValidator>.Instance,
                    escalationQueue: sp.GetRequiredService<RiskEscalationQueueService>());
            });
            services.AddSingleton<IOrderManager>(sp =>
                new OrderManagementSystem(
                    sp.GetRequiredService<IExecutionGateway>(),
                    NullLogger<OrderManagementSystem>.Instance,
                    riskValidator: sp.GetRequiredService<IRiskValidator>(),
                    portfolioState: sp.GetRequiredService<PaperTradingPortfolio>()));
        }, includeExecutionEndpoints: true);

        var client = app.GetTestClient();

        var riskRules = app.Services.GetRequiredService<RiskRuleRuntimeService>();
        await riskRules.UpdateConfigAsync(
            "OrderNotional",
            new RiskRuleConfigUpdateRequest(MaxOrderNotional: 100_000m, EscalateOrderNotional: 10_000m, Reason: "band"),
            actor: "test");

        // 200 × 100 = 20k notional lands in the governed-approval band: parked, not routed.
        var parkedResponse = await client.PostAsync(
            "/api/execution/orders/submit",
            JsonContent(new
            {
                symbol = "AAPL",
                side = 0,
                type = 1,
                timeInForce = 0,
                quantity = 200,
                limitPrice = 100m,
                strategyId = "escalation-check"
            }));
        parkedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var parkedResult = JsonSerializer.Deserialize<OrderResult>(await parkedResponse.Content.ReadAsStringAsync(), JsonOptions());
        parkedResult!.ErrorMessage.Should().Contain("governed approval");
        parkedResult.RequiresApproval.Should().BeTrue("a parked escalation is a typed outcome, not a plain rejection");
        parkedResult.EscalationId.Should().NotBeNullOrWhiteSpace();

        var escalationsResponse = await client.GetAsync("/api/risk/escalations");
        escalationsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var escalations = JsonSerializer.Deserialize<RiskEscalationDto[]>(
            await escalationsResponse.Content.ReadAsStringAsync(), JsonOptions());
        escalations.Should().ContainSingle(entry => entry.Status == "PendingApproval");
        var escalationId = escalations![0].EscalationId;

        // Segregation of duties: the submitting operator cannot approve their own escalation.
        var selfApprove = await client.PostAsync(
            $"/api/risk/escalations/{escalationId}/approve",
            JsonContent(new { reason = "self-serve" }));
        selfApprove.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Approve with release as a distinct operator: the order goes back through the risk gate and routes.
        var approveRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/risk/escalations/{escalationId}/approve")
        {
            Content = JsonContent(new { reason = "cleared with the desk" })
        };
        approveRequest.Headers.Add("X-Test-User", "risk-desk-supervisor");
        var approveResponse = await client.SendAsync(approveRequest);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approval = JsonSerializer.Deserialize<RiskEscalationApprovalResponse>(
            await approveResponse.Content.ReadAsStringAsync(), JsonOptions());
        approval!.Escalation.Status.Should().Be("Released");
        approval.ReleaseResult.Should().NotBeNull();
        approval.ReleaseResult!.Success.Should().BeTrue("the consumed approval releases the order past the escalation band");

        // Denying an already-resolved escalation is refused (the queue only denies pending entries).
        var denyAfter = await client.PostAsync($"/api/risk/escalations/{escalationId}/deny", JsonContent(new { }));
        denyAfter.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RiskAndExecutionIntegration_RegisteredValidatorEnforcesOrderRateThrottle()
    {
        // Proves the consolidation: the RiskRuleRuntimeService that powers the dashboard supplies
        // the live thresholds to Meridian.Risk's CompositeRiskValidator — the registered
        // IRiskValidator the OMS invokes — so a guardrail that reports on the dashboard actually
        // gates orders through the visible risk library.
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new RiskRuleRuntimeOptions(Path.Combine(Path.GetTempPath(), $"risk-rules-{Guid.NewGuid():N}.json")));
            services.AddSingleton<PaperTradingPortfolio>(_ => new PaperTradingPortfolio(100_000m));
            services.AddSingleton<IPortfolioState>(sp => sp.GetRequiredService<PaperTradingPortfolio>());
            services.AddSingleton<IExecutionGateway>(_ => new Meridian.Execution.PaperTradingGateway(
                NullLogger<Meridian.Execution.PaperTradingGateway>.Instance,
                options: new Meridian.Execution.Adapters.PaperTradingGatewayOptions { AllowScaffoldMarketFills = true }));
            services.AddSingleton<RiskRuleRuntimeService>();
            services.AddSingleton<IRiskValidator>(sp =>
            {
                var runtime = sp.GetRequiredService<RiskRuleRuntimeService>();
                return new Meridian.Risk.CompositeRiskValidator(
                    [
                        new DrawdownGuardrailRule(runtime),
                        new Meridian.Risk.Rules.OrderRateThrottle(
                            () => runtime.MaxOrdersPerMinute,
                            NullLogger<Meridian.Risk.Rules.OrderRateThrottle>.Instance),
                    ],
                    NullLogger<Meridian.Risk.CompositeRiskValidator>.Instance);
            });
            services.AddSingleton<IOrderManager>(sp =>
                new OrderManagementSystem(
                    sp.GetRequiredService<IExecutionGateway>(),
                    NullLogger<OrderManagementSystem>.Instance,
                    riskValidator: sp.GetRequiredService<IRiskValidator>(),
                    portfolioState: sp.GetRequiredService<PaperTradingPortfolio>()));
        }, includeExecutionEndpoints: true);

        var client = app.GetTestClient();

        var riskRules = app.Services.GetRequiredService<RiskRuleRuntimeService>();
        await riskRules.UpdateConfigAsync(
            "OrderRateThrottle",
            new RiskRuleConfigUpdateRequest(MaxOrdersPerMinute: 1, Reason: "tighten throttle"),
            actor: "test");

        // First order passes the throttle (0 < 1) and is enqueued.
        var firstResponse = await client.PostAsync(
            "/api/execution/orders/submit",
            JsonContent(new
            {
                symbol = "AAPL",
                side = 0,
                type = 0,
                timeInForce = 0,
                quantity = 1,
                strategyId = "risk-check"
            }));
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second order within the same minute trips the throttle at the registered validator.
        var secondResponse = await client.PostAsync(
            "/api/execution/orders/submit",
            JsonContent(new
            {
                symbol = "AAPL",
                side = 0,
                type = 0,
                timeInForce = 0,
                quantity = 1,
                strategyId = "risk-check"
            }));

        secondResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = JsonSerializer.Deserialize<OrderResult>(await secondResponse.Content.ReadAsStringAsync(), JsonOptions());
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Order rate limit");
    }

    [Fact]
    public async Task SubmitOrder_WithFundAccountId_RequiresScopedManageOrdersAccess()
    {
        var deniedAccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var allowedAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new RiskRuleRuntimeOptions(Path.Combine(Path.GetTempPath(), $"risk-rules-{Guid.NewGuid():N}.json")));
            services.AddSingleton<PaperTradingPortfolio>(_ => new PaperTradingPortfolio(100_000m));
            services.AddSingleton<IPortfolioState>(sp => sp.GetRequiredService<PaperTradingPortfolio>());
            services.AddSingleton<IPositionTracker, StaticPositionTracker>();
            services.AddSingleton<IExecutionGateway>(_ => new Meridian.Execution.PaperTradingGateway(
                NullLogger<Meridian.Execution.PaperTradingGateway>.Instance,
                options: new Meridian.Execution.Adapters.PaperTradingGatewayOptions { AllowScaffoldMarketFills = true }));
            services.AddSingleton<RiskRuleRuntimeService>();
            services.AddSingleton<IRiskValidator>(sp =>
            {
                var runtime = sp.GetRequiredService<RiskRuleRuntimeService>();
                return new Meridian.Risk.CompositeRiskValidator(
                    [
                        new DrawdownGuardrailRule(runtime),
                        new Meridian.Risk.Rules.OrderRateThrottle(
                            () => runtime.MaxOrdersPerMinute,
                            NullLogger<Meridian.Risk.Rules.OrderRateThrottle>.Instance),
                    ],
                    NullLogger<Meridian.Risk.CompositeRiskValidator>.Instance);
            });
            services.AddSingleton<IScopedAuthorizationService>(_ => new AccountScopedAuthorizationService(allowedAccountId));
            services.AddSingleton<IAccountQueryService>(_ => new StubAccountQueryService(allowedAccountId));
            services.AddSingleton<IOrderManager>(sp =>
                new OrderManagementSystem(
                    sp.GetRequiredService<IExecutionGateway>(),
                    NullLogger<OrderManagementSystem>.Instance,
                    riskValidator: sp.GetRequiredService<IRiskValidator>(),
                    portfolioState: sp.GetRequiredService<PaperTradingPortfolio>()));
        }, includeExecutionEndpoints: true);

        var client = app.GetTestClient();

        var deniedResponse = await client.PostAsync(
            "/api/execution/orders/submit",
            JsonContent(new
            {
                symbol = "AAPL",
                side = 0,
                type = 0,
                timeInForce = 0,
                quantity = 1,
                strategyId = "account-scope-check",
                fundAccountId = deniedAccountId
            }));

        deniedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var allowedResponse = await client.PostAsync(
            "/api/execution/orders/submit",
            JsonContent(new
            {
                symbol = "AAPL",
                side = 0,
                type = 0,
                timeInForce = 0,
                quantity = 1,
                strategyId = "account-scope-check",
                fundAccountId = allowedAccountId
            }));

        allowedResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task RiskEscalations_List_RequiresPermissionAndFiltersByFundScope()
    {
        var deniedAccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var allowedAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new RiskEscalationQueueService(
                NullLogger<RiskEscalationQueueService>.Instance,
                options: new RiskEscalationQueueOptions(
                    Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"escalations-{Guid.NewGuid():N}", "escalations.json"))));
            services.AddSingleton<IScopedAuthorizationService>(_ => new AccountScopedAuthorizationService(allowedAccountId));
        });

        var queue = app.Services.GetRequiredService<RiskEscalationQueueService>();
        queue.Park(CreateParkedOrder(), "unscoped entry");
        queue.Park(CreateParkedOrder(allowedAccountId), "allowed-fund entry");
        queue.Park(CreateParkedOrder(deniedAccountId), "denied-fund entry");

        var client = app.GetTestClient();

        // Without order-management permission the queue is not visible at all.
        var noPermission = new HttpRequestMessage(HttpMethod.Get, "/api/risk/escalations");
        noPermission.Headers.Add("X-Test-Permissions", "None");
        (await client.SendAsync(noPermission)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A scoped caller sees unscoped entries plus funds within their account authority;
        // parked orders for other funds are filtered out of the listing.
        var scopedResponse = await client.GetAsync("/api/risk/escalations");
        scopedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var scopedEntries = JsonSerializer.Deserialize<RiskEscalationDto[]>(
            await scopedResponse.Content.ReadAsStringAsync(), JsonOptions());
        scopedEntries!.Select(entry => entry.Reason).Should()
            .Contain("unscoped entry").And
            .Contain("allowed-fund entry").And
            .NotContain("denied-fund entry");

        // AdminMaintenance bypasses fund scoping and sees the full queue.
        var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/api/risk/escalations");
        adminRequest.Headers.Add("X-Test-Permissions", "ManageOrders, AdminMaintenance");
        var adminResponse = await client.SendAsync(adminRequest);
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminEntries = JsonSerializer.Deserialize<RiskEscalationDto[]>(
            await adminResponse.Content.ReadAsStringAsync(), JsonOptions());
        adminEntries!.Should().HaveCount(3);
    }

    private static OrderRequest CreateParkedOrder(Guid? fundAccountId = null) => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 100m,
        LimitPrice = 250m,
        FundAccountId = fundAccountId,
    };

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection> configureServices,
        bool includeExecutionEndpoints = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        configureServices(builder.Services);

        var app = builder.Build();
        app.Use((context, next) =>
        {
            var user = context.Request.Headers.TryGetValue("X-Test-User", out var configured) &&
                !string.IsNullOrWhiteSpace(configured)
                    ? configured.ToString()
                    : "risk-operator";
            var permissions = context.Request.Headers.TryGetValue("X-Test-Permissions", out var configuredPermissions) &&
                Enum.TryParse<UserPermission>(configuredPermissions.ToString(), out var parsedPermissions)
                    ? parsedPermissions
                    : UserPermission.ManageOrders;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = user;
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "risk-test-company";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "risk-test-tenant";
            return next();
        });
        app.MapRiskEndpoints(JsonOptions());
        if (includeExecutionEndpoints)
        {
            app.MapExecutionEndpoints(JsonOptions());
        }

        await app.StartAsync();
        return app;
    }

    private static StringContent JsonContent(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class StaticExposureProvider : Meridian.Risk.IPortfolioExposureProvider
    {
        public Meridian.Risk.PortfolioExposureSnapshot GetSnapshot() => new(
            GrossExposure: 0m,
            NetExposure: 0m,
            PortfolioValue: 100_000m,
            SymbolExposures: new Dictionary<string, Meridian.Risk.SymbolExposure>(StringComparer.OrdinalIgnoreCase),
            AsOf: DateTimeOffset.UtcNow);
    }

    private sealed class StaticPositionTracker : IPositionTracker
    {
        public PositionState GetPosition(string symbol) => new()
        {
            Symbol = symbol,
            Quantity = 0m,
            AverageCostBasis = 100m,
            MarketPrice = 100m,
            LastUpdated = DateTimeOffset.UtcNow
        };

        public IReadOnlyDictionary<string, PositionState> GetAllPositions() =>
            new Dictionary<string, PositionState>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = new()
                {
                    Symbol = "AAPL",
                    Quantity = 0m,
                    AverageCostBasis = 100m,
                    MarketPrice = 100m,
                    LastUpdated = DateTimeOffset.UtcNow
                }
            };

        public decimal GetPortfolioValue() => 100_000m;

        public decimal GetCash() => 100_000m;

        public decimal GetUnrealizedPnl() => 0m;

        public decimal GetRealizedPnl() => 0m;
    }

    private sealed class AccountScopedAuthorizationService(Guid allowedAccountId) : IScopedAuthorizationService
    {
        public Task<ScopedAuthorizationDecisionDto> AuthorizeAsync(
            string actor,
            UserPermission requiredPermission,
            AccessScopeKindDto scopeKind,
            Guid? scopeId,
            UserPermission globalPermissions,
            CancellationToken ct = default)
        {
            var allowed = requiredPermission == UserPermission.ManageOrders &&
                scopeKind == AccessScopeKindDto.Account &&
                scopeId == allowedAccountId;

            return Task.FromResult(new ScopedAuthorizationDecisionDto(
                allowed,
                actor,
                requiredPermission,
                scopeKind,
                scopeId,
                allowed ? "Scoped test grant." : "Scoped test denial."));
        }
    }
}
