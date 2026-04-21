using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Meridian.Contracts.Api;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Backtesting.Sdk;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ExecutionServices = Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Contract tests for execution write-action and blotter endpoints, including
/// named operator scenarios that guard against paper-session replay drift and
/// promotion decisions that are not visibly auditable.
/// </summary>
public sealed class ExecutionWriteEndpointsTests
{
    [Fact]
    public async Task GetBlotterPositions_WhenServicesNotRegistered_Returns503()
    {
        await using var app = await CreateAppAsync(_ => { });

        var client = app.GetTestClient();
        var response = await client.GetAsync(UiApiRoutes.ExecutionBlotterPositions);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetBlotterPositions_WithPaperPositions_ReturnsPaperSnapshotWithoutDemoRows()
    {
        await using var app = await CreateAppAsync(services =>
            RegisterMinimalOms(
                services,
                new ExecutionPosition("AAPL", 10, 180m, 25m, 0m)));

        var client = app.GetTestClient();
        var response = await client.GetAsync(UiApiRoutes.ExecutionBlotterPositions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await ReadAsync<ExecutionBlotterSnapshotResponse>(response);

        using (new AssertionScope())
        {
            snapshot.IsBrokerBacked.Should().BeFalse();
            snapshot.IsLive.Should().BeFalse();
            snapshot.Source.Should().Be("Paper Trading");
            snapshot.StatusMessage.Should().Contain("paper position");
            snapshot.Positions.Should().ContainSingle();
            snapshot.Positions[0].PositionKey.Should().Be("AAPL");
            snapshot.Positions[0].AssetClass.Should().Be("equity");
            snapshot.Positions[0].ProductDescription.Should().Be("AAPL");
        }
    }

    // ------------------------------------------------------------------ //
    //  POST /api/execution/orders/{orderId}/cancel                        //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task CancelOrder_WhenOmsNotRegistered_Returns503()
    {
        await using var app = await CreateAppAsync(_ => { });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/ord-001/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task CancelOrder_WithUnknownOrderId_ReturnsRejectedActionResult()
    {
        await using var app = await CreateAppAsync(RegisterMinimalOms);

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/NONEXISTENT/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Rejected");
        result.ActionId.Should().NotBeNullOrEmpty();
        result.OccurredAt.Should().NotBe(default);
    }

    // ------------------------------------------------------------------ //
    //  POST /api/execution/orders/cancel-all                              //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task CancelAllOrders_WhenOmsNotRegistered_Returns503()
    {
        await using var app = await CreateAppAsync(_ => { });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/cancel-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task CancelAllOrders_WhenNoOpenOrders_ReturnsCompletedActionResult()
    {
        await using var app = await CreateAppAsync(RegisterMinimalOms);

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/orders/cancel-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Completed");
        result.ActionId.Should().NotBeNullOrEmpty();
        result.Message.Should().Contain("0");
    }

    // ------------------------------------------------------------------ //
    //  POST /api/execution/positions/*                                    //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task ClosePosition_WhenServicesNotRegistered_Returns503()
    {
        await using var app = await CreateAppAsync(_ => { });

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/positions/AAPL/close", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ClosePosition_WhenSymbolHasNoPosition_ReturnsRejectedActionResult()
    {
        await using var app = await CreateAppAsync(RegisterMinimalOms);

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/positions/AAPL/close", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Rejected");
        result.ActionId.Should().NotBeNullOrEmpty();
        result.Message.Should().Contain("AAPL");
    }

    [Fact]
    public async Task ClosePositionByKey_WithPaperPosition_SubmitsOrder()
    {
        await using var app = await CreateAppAsync(services =>
            RegisterMinimalOms(
                services,
                new ExecutionPosition("AAPL", 5, 180m, 10m, 0m)));

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionPositionActionClose,
            JsonContent(new ExecutionPositionActionRequest("AAPL")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Accepted");
        result.Message.Should().Contain("AAPL");
    }

    [Fact]
    public async Task ClosePosition_WhenBrokerSnapshotHasMultipleMatches_ReturnsAmbiguousResult()
    {
        var gateway = new RecordingBrokerageGateway(
            CreateRobinhoodOptionPosition("opt-1"),
            CreateRobinhoodOptionPosition("opt-2", expiration: new DateOnly(2026, 6, 19), strike: 185m));

        await using var app = await CreateAppAsync(services => RegisterBrokerageOms(services, gateway));

        var client = app.GetTestClient();
        var response = await client.PostAsync("/api/execution/positions/AAPL/close", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Rejected");
        result.Message.Should().Contain("Use the keyed position action endpoint");
        gateway.SubmittedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ClosePositionByKey_WithBrokerOptionPosition_PassesRobinhoodOptionMetadata()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-close"));

        await using var app = await CreateAppAsync(services => RegisterBrokerageOms(services, gateway));

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionPositionActionClose,
            JsonContent(new ExecutionPositionActionRequest("opt-close", Quantity: 1m)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Accepted");

        var request = gateway.SubmittedRequests.Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            request.Symbol.Should().Be("AAPL");
            request.Side.Should().Be(OrderSide.Sell);
            request.Quantity.Should().Be(1m);
            request.Metadata.Should().NotBeNull();
            request.Metadata!["asset_class"].Should().Be("option");
            request.Metadata["option_instrument_url"].Should().Be("https://api.robinhood.com/options/instruments/opt-close/");
            request.Metadata["position_effect"].Should().Be("close");
            request.Metadata["positionKey"].Should().Be("opt-close");
            request.Metadata["positionSource"].Should().Be("Robinhood (test)");
        }
    }

    [Fact]
    public async Task UpsizePositionByKey_WithBrokerOptionPosition_UsesOpenPositionEffect()
    {
        var gateway = new RecordingBrokerageGateway(CreateRobinhoodOptionPosition("opt-upsize"));

        await using var app = await CreateAppAsync(services => RegisterBrokerageOms(services, gateway));

        var client = app.GetTestClient();
        var response = await client.PostAsync(
            UiApiRoutes.ExecutionPositionActionUpsize,
            JsonContent(new ExecutionPositionActionRequest("opt-upsize", Quantity: 2m)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Accepted");

        var request = gateway.SubmittedRequests.Should().ContainSingle().Subject;
        request.Side.Should().Be(OrderSide.Buy);
        request.Quantity.Should().Be(2m);
        request.Metadata.Should().NotBeNull();
        request.Metadata!["position_effect"].Should().Be("open");
        request.Metadata["option_instrument_url"].Should().Be("https://api.robinhood.com/options/instruments/opt-upsize/");
    }

    [Fact]
    public async Task PaperSessionLifecycleEndpoints_PreserveSymbolsAndExposeReplayContinuityAudit()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(PaperSessionLifecycleEndpoints_PreserveSymbolsAndExposeReplayContinuityAudit));
        await using var app = await CreateAppAsync(services => RegisterSessionServices(services, artifacts.RootPath));

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Meridian-Actor", "ops-session");

        var createResponse = await client.PostAsync(
            UiApiRoutes.ExecutionSessionCreate,
            JsonContent(new CreatePaperSessionRequest(
                StrategyId: "strat-session",
                StrategyName: "Session Strategy",
                InitialCash: 125_000m,
                Symbols: ["AAPL", "MSFT"])));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var summary = await ReadAsync<ExecutionServices.PaperSessionSummaryDto>(createResponse);

        var persistence = app.Services.GetRequiredService<ExecutionServices.PaperSessionPersistenceService>();
        await persistence.RecordFillAsync(summary.SessionId, CreateFill("AAPL", 5m, 200m));

        var detailResponse = await client.GetAsync(
            UiApiRoutes.ExecutionSessionById.Replace("{sessionId}", summary.SessionId, StringComparison.Ordinal));
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await ReadAsync<ExecutionServices.PaperSessionDetailDto>(detailResponse);
        detail.Symbols.Should().Equal("AAPL", "MSFT");

        var closeResponse = await client.PostAsync(
            UiApiRoutes.ExecutionSessionClose.Replace("{sessionId}", summary.SessionId, StringComparison.Ordinal),
            content: null);
        closeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var closeResult = await ReadActionResultAsync(closeResponse);
        closeResult.Status.Should().Be("Completed");
        closeResult.AuditId.Should().NotBeNullOrWhiteSpace();

        var replayResponse = await client.GetAsync(
            UiApiRoutes.ExecutionSessionReplay.Replace("{sessionId}", summary.SessionId, StringComparison.Ordinal));
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayVerification = await ReadAsync<ExecutionServices.PaperSessionReplayVerificationDto>(replayResponse);

        using (new AssertionScope())
        {
            replayVerification.Summary.SessionId.Should().Be(summary.SessionId);
            replayVerification.Symbols.Should().Equal("AAPL", "MSFT");
            replayVerification.ReplaySource.Should().Be("DurableFillLog");
            replayVerification.IsConsistent.Should().BeTrue();
            replayVerification.MismatchReasons.Should().BeEmpty();
            replayVerification.CurrentPortfolio.Should().NotBeNull();
            replayVerification.ReplayPortfolio.Cash.Should().Be(124_000m);
        }

        var auditResponse = await client.GetAsync(UiApiRoutes.ExecutionAudit);
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var audits = await ReadAsync<ExecutionServices.ExecutionAuditEntry[]>(auditResponse);
        audits.Should().Contain(entry => entry.Action == "CreatePaperSession" && entry.Actor == "ops-session" && entry.Metadata!["sessionId"] == summary.SessionId);
        audits.Should().Contain(entry => entry.Action == "ClosePaperSession" && entry.Actor == "ops-session" && entry.Metadata!["sessionId"] == summary.SessionId);
        audits.Should().Contain(entry => entry.Action == "ReplayPaperSession" && entry.Actor == "ops-session" && entry.Metadata!["sessionId"] == summary.SessionId);
    }

    [Fact]
    public async Task Scenario_SessionCloseReplayAndPromotionReview_BacktestToPaperFlowRemainsContinuousAndAuditable()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(Scenario_SessionCloseReplayAndPromotionReview_BacktestToPaperFlowRemainsContinuousAndAuditable));
        await using var app = await CreateAppAsync(services =>
        {
            RegisterSessionServices(services, artifacts.RootPath);
            RegisterPromotionServices(services);
        });

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Meridian-Actor", "ops-promoter");

        var createSessionResponse = await client.PostAsync(
            UiApiRoutes.ExecutionSessionCreate,
            JsonContent(new CreatePaperSessionRequest(
                StrategyId: "strat-wave2",
                StrategyName: "Wave2 Continuity",
                InitialCash: 100_000m,
                Symbols: ["AAPL"])));
        createSessionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await ReadAsync<ExecutionServices.PaperSessionSummaryDto>(createSessionResponse);

        var persistence = app.Services.GetRequiredService<ExecutionServices.PaperSessionPersistenceService>();
        await persistence.RecordFillAsync(session.SessionId, CreateFill("AAPL", quantity: 10m, fillPrice: 101m));

        var replayResponse = await client.GetAsync(
            UiApiRoutes.ExecutionSessionReplay.Replace("{sessionId}", session.SessionId, StringComparison.Ordinal));
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await ReadAsync<ExecutionServices.PaperSessionReplayVerificationDto>(replayResponse);

        var evaluateResponse = await client.GetAsync("/api/promotion/evaluate/run-backtest-01");
        evaluateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var evaluation = await ReadAsync<PromotionEvaluationResult>(evaluateResponse);

        var approveResponse = await client.PostAsync(
            "/api/promotion/approve",
            JsonContent(new PromotionApprovalRequest(
                RunId: "run-backtest-01",
                ReviewNotes: "Replay is consistent with durable fill log.",
                ApprovedBy: "ops-promoter",
                ApprovalReason: "Replay source and session continuity verified.")));
        approveResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var approval = await ReadAsync<PromotionDecisionResult>(approveResponse);

        var historyResponse = await client.GetAsync("/api/promotion/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await ReadAsync<StrategyPromotionRecord[]>(historyResponse);

        var auditResponse = await client.GetAsync(UiApiRoutes.ExecutionAudit);
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var audits = await ReadAsync<ExecutionServices.ExecutionAuditEntry[]>(auditResponse);

        using (new AssertionScope())
        {
            replay.IsConsistent.Should().BeTrue();
            replay.ReplaySource.Should().Be("DurableFillLog");
            replay.MismatchReasons.Should().BeEmpty();
            replay.ReplayPortfolio.Cash.Should().Be(98_990m);

            evaluation.IsEligible.Should().BeTrue();
            evaluation.SourceMode.Should().Be(RunType.Backtest);
            evaluation.TargetMode.Should().Be(RunType.Paper);
            evaluation.RequiresHumanApproval.Should().BeFalse();

            approval.Success.Should().BeTrue();
            approval.NewRunId.Should().NotBeNullOrWhiteSpace();
            approval.ApprovedBy.Should().Be("ops-promoter");
            approval.PromotionId.Should().NotBeNullOrWhiteSpace();

            history.Should().ContainSingle(record =>
                record.StrategyId == "strat-wave2" &&
                record.SourceRunType == RunType.Backtest &&
                record.TargetRunType == RunType.Paper &&
                record.ApprovedBy == "ops-promoter");

            audits.Should().Contain(entry =>
                entry.Action == "ReplayPaperSession" &&
                entry.Actor == "ops-promoter" &&
                entry.Outcome == "Completed" &&
                entry.Metadata is not null &&
                entry.Metadata.TryGetValue("replaySource", out var source) &&
                string.Equals(source, "DurableFillLog", StringComparison.Ordinal));

            audits.Should().Contain(entry =>
                entry.Action == "PromotionApproved" &&
                entry.Actor == "ops-promoter" &&
                entry.RunId == "run-backtest-01" &&
                entry.Outcome == "Approved" &&
                entry.CorrelationId == approval.PromotionId &&
                entry.Message == "Replay source and session continuity verified.");
        }
    }

    [Fact]
    public async Task Scenario_RiskTriggeredPromotionRejection_DecisionRemainsVisibleWithBlockingRationale()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(Scenario_RiskTriggeredPromotionRejection_DecisionRemainsVisibleWithBlockingRationale));
        await using var app = await CreateAppAsync(services =>
        {
            RegisterSessionServices(services, artifacts.RootPath);
            RegisterPromotionServices(services, runId: "run-backtest-risk-blocked", sharpeRatio: 0.12d, maxDrawdownPercent: 0.42m, totalReturn: -0.08m);
        });

        var client = app.GetTestClient();

        var evaluateResponse = await client.GetAsync("/api/promotion/evaluate/run-backtest-risk-blocked");
        evaluateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var evaluation = await ReadAsync<PromotionEvaluationResult>(evaluateResponse);

        var rejectResponse = await client.PostAsync(
            "/api/promotion/reject",
            JsonContent(new PromotionRejectionRequest(
                RunId: "run-backtest-risk-blocked",
                Reason: "Max drawdown exceeded cockpit guardrail and return is negative.")));
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejection = await ReadAsync<PromotionDecisionResult>(rejectResponse);

        using (new AssertionScope())
        {
            evaluation.IsEligible.Should().BeFalse();
            evaluation.Ready.Should().BeTrue();
            evaluation.TargetMode.Should().Be(RunType.Paper);
            evaluation.Reason.Should().NotBeNullOrWhiteSpace();
            evaluation.BlockingReasons.Should().NotBeNull();
            evaluation.BlockingReasons!.Should().Contain(reason =>
                reason.Contains("Sharpe", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("drawdown", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("return", StringComparison.OrdinalIgnoreCase));

            rejection.Success.Should().BeTrue();
            rejection.NewRunId.Should().BeNull();
            rejection.Reason.Should().Contain("Promotion rejected");
            rejection.Reason.Should().Contain("drawdown", StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------------------ //
    //  Helpers                                                            //
    // ------------------------------------------------------------------ //

    private static void RegisterMinimalOms(IServiceCollection services) =>
        RegisterMinimalOms(services, Array.Empty<ExecutionPosition>());

    private static void RegisterMinimalOms(IServiceCollection services, params ExecutionPosition[] positions)
    {
        services.AddSingleton<IExecutionGateway, PaperTradingGateway>();
        services.AddSingleton<IOrderManager>(sp =>
            new OrderManagementSystem(
                sp.GetRequiredService<IExecutionGateway>(),
                NullLogger<OrderManagementSystem>.Instance));
        services.AddSingleton<Meridian.Execution.Models.IPortfolioState>(new StaticPortfolioState(positions));
    }

    private static void RegisterBrokerageOms(IServiceCollection services, RecordingBrokerageGateway gateway)
    {
        services.AddSingleton(gateway);
        services.AddSingleton<IExecutionGateway>(sp => sp.GetRequiredService<RecordingBrokerageGateway>());
        services.AddSingleton<IOrderManager>(sp =>
            new OrderManagementSystem(
                sp.GetRequiredService<IExecutionGateway>(),
                NullLogger<OrderManagementSystem>.Instance));
    }

    private static void RegisterSessionServices(IServiceCollection services, string rootPath)
    {
        services.AddSingleton(_ => new ExecutionServices.ExecutionAuditTrailService(
            new ExecutionServices.ExecutionAuditTrailOptions(Path.Combine(rootPath, "audit")),
            NullLogger<ExecutionServices.ExecutionAuditTrailService>.Instance));
        services.AddSingleton<ExecutionServices.IPaperSessionStore>(_ => new ExecutionServices.JsonlFilePaperSessionStore(
            Path.Combine(rootPath, "sessions"),
            NullLogger<ExecutionServices.JsonlFilePaperSessionStore>.Instance));
        services.AddSingleton<ExecutionServices.PaperSessionPersistenceService>(sp => new ExecutionServices.PaperSessionPersistenceService(
            NullLogger<ExecutionServices.PaperSessionPersistenceService>.Instance,
            sp.GetRequiredService<ExecutionServices.IPaperSessionStore>()));
    }

    private static void RegisterPromotionServices(
        IServiceCollection services,
        string runId = "run-backtest-01",
        double sharpeRatio = 1.20d,
        decimal maxDrawdownPercent = 0.08m,
        decimal totalReturn = 0.16m)
    {
        var strategyRepository = new StrategyRunStore();
        strategyRepository
            .RecordRunAsync(CreateCompletedBacktestRun(
                runId: runId,
                strategyId: "strat-wave2",
                strategyName: "Wave2 Continuity",
                sharpeRatio: sharpeRatio,
                maxDrawdownPercent: maxDrawdownPercent,
                totalReturn: totalReturn))
            .GetAwaiter()
            .GetResult();

        services.AddSingleton(strategyRepository);
        services.AddSingleton<BacktestToLivePromoter>();
        services.AddSingleton<PromotionService>(sp => new PromotionService(
            sp.GetRequiredService<StrategyRunStore>(),
            sp.GetRequiredService<BacktestToLivePromoter>(),
            NullLogger<PromotionService>.Instance,
            auditTrail: sp.GetRequiredService<ExecutionServices.ExecutionAuditTrailService>()));
    }

    private static async Task<WebApplication> CreateAppAsync(Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapExecutionEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        app.MapPromotionEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private static async Task<TradingActionResult> ReadActionResultAsync(HttpResponseMessage response) =>
        await ReadAsync<TradingActionResult>(response);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        var result = JsonSerializer.Deserialize<T>(json, opts);
        result.Should().NotBeNull($"expected a {typeof(T).Name} in response body, but got: {json}");
        return result!;
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static ExecutionReport CreateFill(string symbol, decimal quantity, decimal fillPrice) => new()
    {
        OrderId = $"fill-{Guid.NewGuid():N}",
        ReportType = ExecutionReportType.Fill,
        Symbol = symbol,
        Side = OrderSide.Buy,
        OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
        OrderQuantity = quantity,
        FilledQuantity = quantity,
        FillPrice = fillPrice,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static Meridian.Strategies.Models.StrategyRunEntry CreateCompletedBacktestRun(
        string runId,
        string strategyId,
        string strategyName,
        double sharpeRatio,
        decimal maxDrawdownPercent,
        decimal totalReturn)
    {
        var now = DateTimeOffset.UtcNow;
        var request = new BacktestRequest(
            StrategyId: strategyId,
            StrategyName: strategyName,
            StartDate: now.AddDays(-10).UtcDateTime,
            EndDate: now.AddDays(-1).UtcDateTime,
            InitialCapital: 100_000m,
            BenchmarkSymbol: "SPY");

        var snapshot = new PortfolioSnapshot(
            Timestamp: now.AddDays(-1),
            Cash: 108_000m,
            MarginBalance: 0m,
            LongMarketValue: 8_000m,
            ShortMarketValue: 0m,
            TotalEquity: 116_000m,
            DailyReturn: 0.01m,
            Positions: new Dictionary<string, Position>(),
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
            DayCashFlows: []);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: 116_000m,
            GrossPnl: 16_000m,
            NetPnl: 15_200m,
            TotalReturn: totalReturn,
            AnnualizedReturn: 0.20m,
            SharpeRatio: sharpeRatio,
            SortinoRatio: 1.8d,
            CalmarRatio: 1.1d,
            MaxDrawdown: 8_400m,
            MaxDrawdownPercent: maxDrawdownPercent,
            MaxDrawdownRecoveryDays: 7,
            ProfitFactor: 1.7d,
            WinRate: 0.58d,
            TotalTrades: 32,
            WinningTrades: 18,
            LosingTrades: 14,
            TotalCommissions: 750m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0.14d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(["AAPL"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new global::Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromMinutes(7),
            TotalEventsProcessed: 1_200);

        return new Meridian.Strategies.Models.StrategyRunEntry(
            RunId: runId,
            StrategyId: strategyId,
            StrategyName: strategyName,
            RunType: RunType.Backtest,
            StartedAt: now.AddDays(-10),
            EndedAt: now.AddDays(-1),
            Metrics: result,
            PortfolioId: $"{strategyId}-backtest-portfolio",
            LedgerReference: $"{strategyId}-backtest-ledger",
            Engine: "MeridianNative");
    }

    private static BrokerPosition CreateRobinhoodOptionPosition(
        string positionId,
        DateOnly? expiration = null,
        decimal strike = 180m) =>
        new()
        {
            PositionId = positionId,
            Symbol = "AAPL",
            UnderlyingSymbol = "AAPL",
            Description = $"AAPL {(expiration ?? new DateOnly(2026, 5, 15)):yyyy-MM-dd} {strike}C",
            Quantity = 1m,
            AverageEntryPrice = 2.10m,
            MarketPrice = 2.45m,
            MarketValue = 245m,
            UnrealizedPnl = 35m,
            AssetClass = "option",
            Expiration = expiration ?? new DateOnly(2026, 5, 15),
            Strike = strike,
            Right = "call",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["asset_class"] = "option",
                ["option_instrument_url"] = $"https://api.robinhood.com/options/instruments/{positionId}/",
                ["underlying_symbol"] = "AAPL",
                ["right"] = "call",
                ["expiration"] = (expiration ?? new DateOnly(2026, 5, 15)).ToString("yyyy-MM-dd"),
                ["strike"] = strike.ToString("G29")
            }
        };
}

file sealed class StaticPortfolioState(params ExecutionPosition[] positions) : Meridian.Execution.Models.IPortfolioState
{
    public decimal Cash => 100_000m;
    public decimal PortfolioValue => 100_000m;
    public decimal UnrealisedPnl { get; } = positions.Sum(position => position.UnrealisedPnl);
    public decimal RealisedPnl { get; } = positions.Sum(position => position.RealisedPnl);
    public IReadOnlyDictionary<string, Meridian.Execution.Sdk.IPosition> Positions { get; } = positions.ToDictionary(
        position => position.Symbol,
        position => (Meridian.Execution.Sdk.IPosition)position,
        StringComparer.OrdinalIgnoreCase);
}

sealed class RecordingBrokerageGateway(params BrokerPosition[] positions) : IBrokerageGateway
{
    private readonly IReadOnlyList<BrokerPosition> _positions = positions;

    public List<OrderRequest> SubmittedRequests { get; } = new();

    public string GatewayId => "robinhood";
    public string BrokerDisplayName => "Robinhood (test)";
    public bool IsConnected { get; private set; }
    public BrokerageCapabilities BrokerageCapabilities { get; } = BrokerageCapabilities.UsEquity();

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        SubmittedRequests.Add(request);

        return Task.FromResult(new ExecutionReport
        {
            OrderId = request.ClientOrderId ?? $"test-{SubmittedRequests.Count}",
            ClientOrderId = request.ClientOrderId,
            GatewayOrderId = $"gw-{SubmittedRequests.Count}",
            ReportType = ExecutionReportType.Fill,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
            OrderQuantity = request.Quantity,
            FilledQuantity = request.Quantity,
            FillPrice = 1m,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
        Task.FromResult(new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Cancelled,
            Symbol = string.Empty,
            Side = OrderSide.Buy,
            OrderStatus = Meridian.Execution.Sdk.OrderStatus.Cancelled,
            Timestamp = DateTimeOffset.UtcNow
        });

    public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) =>
        Task.FromResult(new ExecutionReport
        {
            OrderId = orderId,
            ReportType = ExecutionReportType.Modified,
            Symbol = string.Empty,
            Side = OrderSide.Buy,
            OrderStatus = Meridian.Execution.Sdk.OrderStatus.Accepted,
            Timestamp = DateTimeOffset.UtcNow
        });

    public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) =>
        Task.FromResult(new AccountInfo
        {
            AccountId = "acct-1",
            Cash = 100_000m,
            Equity = 100_000m,
            BuyingPower = 100_000m
        });

    public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) =>
        Task.FromResult(_positions);

    public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BrokerOrder>>(Array.Empty<BrokerOrder>());

    public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default) =>
        Task.FromResult(BrokerHealthStatus.Healthy("ok"));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file sealed class TestArtifactDirectory : IDisposable
{
    private TestArtifactDirectory(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public static TestArtifactDirectory Create(string scenarioName)
    {
        var sanitizedName = new string(scenarioName
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        var rootPath = Path.Combine(
            AppContext.BaseDirectory,
            "test-artifacts",
            sanitizedName,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        return new TestArtifactDirectory(rootPath);
    }

    public void Dispose()
    {
        if (!Directory.Exists(RootPath))
        {
            return;
        }

        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for test artifacts.
        }
    }
}
