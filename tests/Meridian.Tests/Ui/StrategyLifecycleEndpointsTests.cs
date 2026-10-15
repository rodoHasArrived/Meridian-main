using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Live;
using Meridian.Execution.Services;
using Meridian.Identity.Auth;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Tests.Ui;

public sealed class StrategyLifecycleEndpointsTests
{
    [Theory]
    [InlineData("/api/strategies/strategy-1/pause")]
    [InlineData("/api/strategies/strategy-1/stop")]
    [InlineData("/api/strategies/strategy-1/reconcile")]
    [InlineData("/api/strategies/strategy-1/resume")]
    public async Task LifecycleMutation_WhenUserLacksManageStrategiesPermission_ReturnsForbidden(string route)
    {
        await using var app = await CreateAppAsync(UserPermission.ViewStrategies);

        var response = await app.GetTestClient().PostAsync(route, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Resume_WhenEngineIsUnavailable_ReturnsBlockedReceipt()
    {
        await using var app = await CreateAppAsync(UserPermission.ManageStrategies);

        var response = await app.GetTestClient().PostAsync("/api/strategies/strategy-1/resume", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var outcome = await response.Content.ReadFromJsonAsync<VerifiedOperationOutcome>();
        outcome!.State.Should().Be(OperationTerminalState.Blocked);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task Resume_UnknownStrategy_ReturnsNotFoundWithoutStartingCode()
    {
        var repository = new StrategyRunStore();
        var strategy = new ResumeProbeStrategy("strategy-1");
        await using var engine = CreateEngine(repository, strategy);
        await using var app = await CreateAppAsync(UserPermission.ManageStrategies, services =>
        {
            services.AddSingleton<IStrategyRepository>(repository);
            services.AddSingleton(engine);
        });

        var response = await app.GetTestClient().PostAsync("/api/strategies/missing/resume", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        strategy.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resume_RestoredPausedRun_ReturnsRetainedReceiptAndKeepsConfiguration()
    {
        var repository = new StrategyRunStore();
        var strategy = new ResumeProbeStrategy("strategy-1");
        var paused = StrategyRunEntry.StartWithEvidence(
            strategy.StrategyId, strategy.Name, RunType.Paper,
            runId: "paused-paper-run",
            parameterSet: new Dictionary<string, string> { ["symbols"] = "SPY", ["quantity"] = "7" })
            .Paused("operator") with
        {
            TerminalStatus = StrategyRunStatus.Paused,
            ParentRunId = "approved-backtest",
            InputHashSha256 = null
        };
        await repository.RecordRunAsync(paused);
        await using var engine = CreateEngine(repository, strategy);
        await engine.ResumePendingRunsAsync();
        engine.PausedRunIds.Should().Contain(paused.RunId);
        strategy.StartCalls.Should().Be(0, "restoration must wait for an explicit resume request");
        await using var app = await CreateAppAsync(UserPermission.ManageStrategies, services =>
        {
            services.AddSingleton<IStrategyRepository>(repository);
            services.AddSingleton(engine);
        });

        var response = await app.GetTestClient().PostAsync($"/api/strategies/{strategy.StrategyId}/resume", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var outcome = await response.Content.ReadFromJsonAsync<VerifiedOperationOutcome>();
        outcome!.OperationKind.Should().Be("strategy-run.Resumed");
        outcome.State.Should().Be(OperationTerminalState.Succeeded);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
        strategy.StartCalls.Should().Be(1);
        strategy.InitializeCalls.Should().Be(1);
        engine.PausedRunIds.Should().NotContain(paused.RunId);
        var resumed = await repository.GetRunByIdAsync(paused.RunId);
        resumed!.RunId.Should().Be(paused.RunId);
        resumed.ParameterSet.Should().BeEquivalentTo(paused.ParameterSet);
        resumed.ParentRunId.Should().Be(paused.ParentRunId);
        resumed.TerminalStatus.Should().BeNull();

        var duplicate = await app.GetTestClient().PostAsync($"/api/strategies/{strategy.StrategyId}/resume", content: null);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        strategy.StartCalls.Should().Be(1, "a duplicate resume cannot repeat strategy warmup");
    }

    [Fact]
    public async Task Resume_WhenWarmupFails_ReturnsRetainedFailureReceipt()
    {
        var repository = new StrategyRunStore();
        var strategy = new ResumeProbeStrategy("strategy-1", failStart: true);
        var paused = StrategyRunEntry.StartWithEvidence(
            strategy.StrategyId, strategy.Name, RunType.Paper,
            runId: "paused-failing-run",
            parameterSet: new Dictionary<string, string> { ["symbols"] = "SPY" })
            .Paused("operator") with
        { TerminalStatus = StrategyRunStatus.Paused };
        await repository.RecordRunAsync(paused);
        await using var engine = CreateEngine(repository, strategy);
        await engine.ResumePendingRunsAsync();
        await using var app = await CreateAppAsync(UserPermission.ManageStrategies, services =>
        {
            services.AddSingleton<IStrategyRepository>(repository);
            services.AddSingleton(engine);
        });

        var response = await app.GetTestClient().PostAsync($"/api/strategies/{strategy.StrategyId}/resume", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var outcome = await response.Content.ReadFromJsonAsync<VerifiedOperationOutcome>();
        outcome!.OperationKind.Should().Be("strategy-run.StartFailed");
        outcome.State.Should().Be(OperationTerminalState.Failed);
        outcome.Issues.Should().Contain(issue => issue.Message.Contains("Warmup unavailable", StringComparison.Ordinal));
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
        (await repository.GetRunByIdAsync(paused.RunId))!.LastLifecycleEvent.Should()
            .Be(StrategyRunLifecycleEventType.StartFailed);
    }

    [Fact]
    public void CreateEndpointFailureOutcome_ForBlockedPrerequisite_ReturnsValidReceipt()
    {
        var outcome = StrategyLifecycleEndpoints.CreateEndpointFailureOutcome(
            "strategy-1",
            "pause",
            OperationTerminalState.Blocked,
            "Strategy is not registered.",
            exception: null,
            externalStateMayHaveChanged: false);

        outcome.State.Should().Be(OperationTerminalState.Blocked);
        outcome.Issues.Should().ContainSingle().Which.IsBlocking.Should().BeTrue();
        outcome.Recovery.Should().ContainSingle().Which.Retryable.Should().BeTrue();
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    [Fact]
    public void CreateEndpointFailureOutcome_WhenExternalStateMayHaveChanged_RequiresReconciliation()
    {
        var outcome = StrategyLifecycleEndpoints.CreateEndpointFailureOutcome(
            "strategy-1",
            "stop",
            OperationTerminalState.Failed,
            "The stop request failed before a retained terminal receipt was returned.",
            new IOException("Persistence unavailable."),
            externalStateMayHaveChanged: true);

        outcome.State.Should().Be(OperationTerminalState.Failed);
        outcome.Recovery.Should().ContainSingle().Which.Should().Match<OperationRecoveryAction>(action =>
            action.ActionId == "reconcile-before-retry" &&
            !action.Retryable &&
            action.Guidance.Contains("Do not repeat", StringComparison.Ordinal) &&
            action.Route == "/api/strategies/strategy-1/reconcile");
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    private static async Task<WebApplication> CreateAppAsync(
        UserPermission permissions, Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddRateLimiter(options => options.AddPolicy(
            UiEndpoints.MutationRateLimitPolicy,
            _ => RateLimitPartition.GetNoLimiter("strategy-lifecycle-test")));
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapStrategyLifecycleEndpoints(new JsonSerializerOptions());
        await app.StartAsync();
        return app;
    }

    private static LiveTradingEngine CreateEngine(IStrategyRepository repository, ResumeProbeStrategy strategy)
    {
        var cache = new LiveMarketDataCache();
        return new LiveTradingEngine(
            new LiveStrategyCatalog().Register(strategy.StrategyId, _ => strategy),
            new LiveMarketEventHub(), cache, Mock.Of<IOrderGateway>(),
            Mock.Of<ExecutionSdk.IOrderManager>(), new PaperTradingPortfolio(100_000m), repository,
            lifecycleManager: new StrategyLifecycleManager(
                repository, NullLogger<StrategyLifecycleManager>.Instance));
    }

    private sealed class ResumeProbeStrategy(string strategyId, bool failStart = false) : LiveStrategyBase
    {
        public override string StrategyId => strategyId;
        public override string Name => "Resume probe";
        public int StartCalls { get; private set; }
        public int InitializeCalls { get; private set; }

        protected override Task OnStartingAsync(IExecutionContext ctx, CancellationToken ct)
        {
            StartCalls++;
            return failStart ? Task.FromException(new InvalidOperationException("Warmup unavailable.")) : Task.CompletedTask;
        }

        public override void Initialize(IBacktestContext ctx) => InitializeCalls++;
    }
}
