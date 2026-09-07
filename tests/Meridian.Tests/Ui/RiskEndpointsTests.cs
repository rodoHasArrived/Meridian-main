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
using Meridian.Risk;
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
    public async Task RiskRuleStatus_WithoutTradeRead_IsForbidden()
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

        // Rule status carries aggregate gross exposure across every registered portfolio
        // and violation reasons that can name traded symbols. A session with no trade-read
        // authority must not be able to recover the book from the guardrail surface.
        foreach (var route in new[] { "/api/risk/rules", "/api/risk/rules/GrossExposure/status" })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Add("X-Test-Permissions", nameof(UserPermission.ViewReporting));

            (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "{0} exposes book data", route);
        }
    }

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
        parkedResponse.StatusCode.Should().Be(
            HttpStatusCode.Accepted,
            "a parked order is not a rejection: nothing routed, but a live queue entry can still execute it, "
            + "and a client that saw 400 would tell the operator the submission failed");
        var parkedResult = JsonSerializer.Deserialize<OrderResult>(await parkedResponse.Content.ReadAsStringAsync(), JsonOptions());
        parkedResult!.ErrorMessage.Should().Contain("governed approval");
        parkedResult.RequiresApproval.Should().BeTrue("a parked escalation is a typed outcome, not a plain rejection");
        parkedResult.EscalationId.Should().NotBeNullOrWhiteSpace();

        // The unscoped rules endpoint reports that orders are parked, never which ones: it
        // runs no fund-scope or order-management permission check, so symbol, side, and
        // size stay behind /api/risk/escalations.
        var parkedRules = JsonSerializer.Deserialize<RiskRuleStatusDto[]>(
            await (await client.GetAsync("/api/risk/rules")).Content.ReadAsStringAsync(), JsonOptions());
        var notionalStatus = parkedRules!.Single(rule => rule.RuleName == "OrderNotional");
        notionalStatus.RecentViolations.Should().ContainSingle()
            .Which.Should().Contain("1 order(s) parked");
        string.Join(" ", notionalStatus.RecentViolations).Should().NotContain("AAPL")
            .And.NotContain("200", "the parked order's size is not readable from an unscoped surface");

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

        // A governed decision without a rationale is refused before any queue state changes:
        // the audit record would otherwise fall back to the original risk-breach reason and
        // read as though the operator supplied evidence they never did.
        var unreasonedDeny = await client.PostAsync(
            $"/api/risk/escalations/{escalationId}/deny", JsonContent(new { }));
        unreasonedDeny.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Denying an already-resolved escalation is refused (the queue only denies pending entries).
        var denyAfter = await client.PostAsync(
            $"/api/risk/escalations/{escalationId}/deny", JsonContent(new { reason = "too late" }));
        denyAfter.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RiskEscalations_ChainedApprovalRetainsOriginalSubmitterForSegregationOfDuties()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<PaperTradingPortfolio>(_ => new PaperTradingPortfolio(100_000m));
            services.AddSingleton<IPortfolioState>(sp => sp.GetRequiredService<PaperTradingPortfolio>());
            services.AddSingleton<IExecutionGateway>(_ => new Meridian.Execution.PaperTradingGateway(
                NullLogger<Meridian.Execution.PaperTradingGateway>.Instance,
                options: new Meridian.Execution.Adapters.PaperTradingGatewayOptions { AllowScaffoldMarketFills = true }));
            services.AddSingleton(new RiskEscalationQueueService(
                NullLogger<RiskEscalationQueueService>.Instance,
                options: new RiskEscalationQueueOptions(
                    Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"escalations-{Guid.NewGuid():N}", "escalations.json"))));
            services.AddSingleton<IRiskValidator>(sp => new Meridian.Risk.CompositeRiskValidator(
                [
                    new EscalatingRule("order-notional", "band A"),
                    new EscalatingRule("desk-review", "band B")
                ],
                NullLogger<Meridian.Risk.CompositeRiskValidator>.Instance,
                escalationQueue: sp.GetRequiredService<RiskEscalationQueueService>()));
            services.AddSingleton<IOrderManager>(sp => new OrderManagementSystem(
                sp.GetRequiredService<IExecutionGateway>(),
                NullLogger<OrderManagementSystem>.Instance,
                riskValidator: sp.GetRequiredService<IRiskValidator>(),
                portfolioState: sp.GetRequiredService<PaperTradingPortfolio>()));
        }, includeExecutionEndpoints: true);

        var client = app.GetTestClient();
        var submit = new HttpRequestMessage(HttpMethod.Post, "/api/execution/orders/submit")
        {
            Content = JsonContent(new
            {
                symbol = "AAPL",
                side = 0,
                type = 1,
                timeInForce = 0,
                quantity = 10,
                limitPrice = 100m
            })
        };
        submit.Headers.Add("X-Test-User", "submitter");

        var parkedResponse = await client.SendAsync(submit);
        parkedResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var parked = JsonSerializer.Deserialize<OrderResult>(
            await parkedResponse.Content.ReadAsStringAsync(), JsonOptions());

        var approveFirst = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/risk/escalations/{parked!.EscalationId}/approve")
        {
            Content = JsonContent(new { reason = "first independent review" })
        };
        approveFirst.Headers.Add("X-Test-User", "risk-desk");
        var firstApprovalResponse = await client.SendAsync(approveFirst);
        firstApprovalResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstApproval = JsonSerializer.Deserialize<RiskEscalationApprovalResponse>(
            await firstApprovalResponse.Content.ReadAsStringAsync(), JsonOptions());
        firstApproval!.ReleaseResult!.RequiresApproval.Should().BeTrue("the second rule still requires its own decision");

        var queue = app.Services.GetRequiredService<RiskEscalationQueueService>();
        var second = queue.TryGet(firstApproval.ReleaseResult.EscalationId!);
        second!.Actor.Should().Be("submitter", "an approver cannot replace the submitting actor during a chained release");

        var selfApproveSecond = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/risk/escalations/{second.EscalationId}/approve")
        {
            Content = JsonContent(new { reason = "submitter must not approve a later exception" })
        };
        selfApproveSecond.Headers.Add("X-Test-User", "submitter");

        (await client.SendAsync(selfApproveSecond)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

    // ── Cross-fund violation detail is not readable by a scope-limited operator ──────────

    /// <summary>
    /// Seeds one position-limit rejection whose reason names the symbol and the size, which is the
    /// disclosure the rules routes leaked: they gate on the global ViewTrades bit only, so an
    /// operator entitled to part of the book read every account's traded symbols, quantities, and
    /// entered prices out of RecentViolations.
    /// </summary>
    private static async Task<WebApplication> CreateRedactionAppAsync(
        IScopedAccessAssignmentService? scopedAccess)
    {
        var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IPositionTracker, StaticPositionTracker>();
            services.AddSingleton(new ExecutionOperatorControlOptions(Path.Combine(Path.GetTempPath(), $"execution-controls-{Guid.NewGuid():N}")));
            services.AddSingleton<ExecutionOperatorControlService>();
            services.AddSingleton(new ExecutionAuditTrailOptions(Path.Combine(Path.GetTempPath(), $"risk-audit-{Guid.NewGuid():N}")));
            services.AddSingleton<ExecutionAuditTrailService>();
            services.AddSingleton<RiskRuleRuntimeService>();
            services.AddSingleton(new RiskRuleRuntimeOptions(Path.Combine(Path.GetTempPath(), $"risk-rules-{Guid.NewGuid():N}.json")));
            if (scopedAccess is not null)
            {
                services.AddSingleton(scopedAccess);
            }
        });

        await app.Services.GetRequiredService<ExecutionAuditTrailService>().RecordAsync(
            category: "Risk",
            action: "OrderRejected",
            outcome: "Rejected",
            actor: "risk-operator",
            symbol: RedactionLeakSymbol,
            reason: $"Position limit breached for {RedactionLeakSymbol}: {RedactionLeakQuantity} at {RedactionLeakPrice}.");

        return app;
    }

    private const string RedactionLeakSymbol = "AAPL";
    private const string RedactionLeakQuantity = "4200";
    private const string RedactionLeakPrice = "187.55";

    private static async Task<string> ReadRulesBodyAsync(WebApplication app, string route, string? permissions = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        if (permissions is not null)
        {
            request.Headers.Add("X-Test-Permissions", permissions);
        }

        var response = await app.GetTestClient().SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "{0} must remain readable", route);
        return await response.Content.ReadAsStringAsync();
    }

    [Theory]
    [InlineData("/api/risk/rules")]
    [InlineData("/api/risk/rules/PositionLimit/status")]
    public async Task RiskEndpoints_ScopeLimitedOperator_CannotReadViolationDetail(string route)
    {
        await using var app = await CreateRedactionAppAsync(
            new StubScopedAccessAssignmentService(throwOnQuery: false, AccessScopeKindDto.Account));

        var body = await ReadRulesBodyAsync(app, route);

        body.Should().NotContain(RedactionLeakQuantity, "an account-scoped operator must not read another account's order size")
            .And.NotContain(RedactionLeakPrice, "nor the price it was entered at");
        body.Should().Contain("violation(s)", "the rule must still read as explained, not as a breach with no cause");
    }

    [Fact]
    public async Task RiskEndpoints_ScopeLimitedOperator_StillSeesRuleHealth()
    {
        await using var app = await CreateRedactionAppAsync(
            new StubScopedAccessAssignmentService(throwOnQuery: false, AccessScopeKindDto.Fund));

        var body = await ReadRulesBodyAsync(app, "/api/risk/rules");
        var rules = JsonSerializer.Deserialize<RiskRuleStatusDto[]>(body, JsonOptions());
        var positionLimit = rules!.Single(rule => rule.RuleName == "PositionLimit");

        positionLimit.IsBreached.Should().BeTrue("redaction hides the detail, never the fact that a rule is breached");
        positionLimit.State.Should().Be("Constrained");
        positionLimit.RecentViolations.Should().ContainSingle()
            .Which.Should().NotContain(RedactionLeakSymbol);
    }

    [Fact]
    public async Task RiskEndpoints_UnrestrictedOperator_ReadsFullViolationDetail()
    {
        // A principal holding only global assignments is entitled to the host-wide view, so
        // redacting it would be pure signal loss rather than a confidentiality gain.
        await using var globallyScoped = await CreateRedactionAppAsync(
            new StubScopedAccessAssignmentService(throwOnQuery: false, AccessScopeKindDto.Global));
        (await ReadRulesBodyAsync(globallyScoped, "/api/risk/rules"))
            .Should().Contain(RedactionLeakSymbol);

        // Likewise a composition with no scoped-access directory at all: nobody in it is
        // scope-limited, so there is no narrower view to fall back to.
        await using var unscoped = await CreateRedactionAppAsync(scopedAccess: null);
        (await ReadRulesBodyAsync(unscoped, "/api/risk/rules"))
            .Should().Contain(RedactionLeakSymbol);
    }

    [Fact]
    public async Task RiskEndpoints_AdminMaintenance_BypassesRedaction()
    {
        await using var app = await CreateRedactionAppAsync(
            new StubScopedAccessAssignmentService(throwOnQuery: false, AccessScopeKindDto.Account));

        var permissions = $"{nameof(UserPermission.ViewTrades)}, {nameof(UserPermission.AdminMaintenance)}";
        (await ReadRulesBodyAsync(app, "/api/risk/rules", permissions))
            .Should().Contain(RedactionLeakSymbol, "the same administrative bypass the sibling routes in this file use");
    }

    [Fact]
    public async Task RiskEndpoints_ScopeLookupFailure_FailsClosed()
    {
        await using var app = await CreateRedactionAppAsync(
            new StubScopedAccessAssignmentService(throwOnQuery: true));

        // Being unable to establish that the caller is unrestricted is not a reason to hand them
        // the whole book.
        (await ReadRulesBodyAsync(app, "/api/risk/rules"))
            .Should().NotContain(RedactionLeakQuantity);
    }

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
                    // Every role that can manage orders also reads trades — see
                    // RolePermissions: Admin and TradeDesk both carry the pair. A
                    // ManageOrders-only principal matches no real role, and defaulting to
                    // one hid the fact that rule status is now a trade read.
                    : UserPermission.ManageOrders | UserPermission.ViewTrades;
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

    private sealed class EscalatingRule(string ruleName, string reason) : IRiskRule
    {
        public string RuleName => ruleName;

        public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(RiskValidationResult.Escalated(reason));
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

    /// <summary>
    /// Stands in for the scoped-access directory so a test can say whether the calling operator's
    /// entitlement is bounded to part of the book. <paramref name="scopeKinds"/> empty means an
    /// unrestricted principal; a throwing instance covers the fail-closed path.
    /// </summary>
    private sealed class StubScopedAccessAssignmentService(
        bool throwOnQuery,
        params AccessScopeKindDto[] scopeKinds) : IScopedAccessAssignmentService
    {
        public Task<IReadOnlyList<UserAccessAssignmentDto>> QueryAsync(
            UserAccessAssignmentQueryDto query,
            CancellationToken ct = default)
        {
            if (throwOnQuery)
            {
                throw new InvalidOperationException("Scoped access directory unavailable.");
            }

            IReadOnlyList<UserAccessAssignmentDto> assignments = scopeKinds
                .Select(kind => new UserAccessAssignmentDto(
                    AssignmentId: Guid.NewGuid(),
                    PrincipalId: query.PrincipalId ?? "risk-operator",
                    PrincipalKind: AccessPrincipalKindDto.User,
                    ScopeKind: kind,
                    ScopeId: kind == AccessScopeKindDto.Global ? null : Guid.NewGuid(),
                    Role: "TradeDesk",
                    RoleProfileName: null,
                    PermissionNames: new[] { nameof(UserPermission.ViewTrades) },
                    PermissionMask: (long)UserPermission.ViewTrades,
                    EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-1),
                    EffectiveTo: null,
                    GrantedBy: "test",
                    Rationale: "test",
                    CorrelationId: Guid.NewGuid().ToString("N"),
                    Version: 1,
                    CreatedAtUtc: DateTimeOffset.UtcNow.AddDays(-1),
                    UpdatedAtUtc: DateTimeOffset.UtcNow.AddDays(-1)))
                .ToArray();

            return Task.FromResult(assignments);
        }

        public Task<UserAccessAssignmentMutationResultDto> CreateAsync(
            UserAccessAssignmentCreateRequestDto request,
            string actor,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<UserAccessAssignmentMutationResultDto> RevokeAsync(
            UserAccessAssignmentRevokeRequestDto request,
            string actor,
            CancellationToken ct = default) => throw new NotSupportedException();
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
