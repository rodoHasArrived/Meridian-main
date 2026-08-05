using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.StrategyEngine;
using Meridian.Identity.Auth;
using Meridian.Contracts.Workstation;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards the Strategy Builder workstation scenario where operators validate, preview, and run
/// a governed strategy design through the existing browser workstation API.
/// </summary>
public sealed class StrategyDesignerWorkstationEndpointsTests
{
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task MapWorkstationEndpoints_StrategyDesignerCatalog_ShouldExposeTemplatesAndDisabledAmxFields()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var templates = await client.GetFromJsonAsync<StrategyDesignTemplate[]>(
            "/api/workstation/strategy/designer/templates",
            ServerJsonOptions);
        var fields = await client.GetFromJsonAsync<StrategyDesignFieldCatalogItem[]>(
            "/api/workstation/strategy/designer/field-catalog",
            ServerJsonOptions);

        templates.Should().Contain(template => template.TemplateId == "options-payoff");
        templates.Should().Contain(template =>
            template.TemplateId == "equity-momentum-breakout" &&
            template.Document.Cells.Any(cell =>
                cell.CellId == "review-packet" &&
                cell.Label == "Review packet" &&
                cell.Purpose == "control"));
        templates.Should().NotContain(template =>
            template.Document.Cells.Any(cell => cell.CellId == "governance-pack"));
        fields.Should().Contain(field =>
            field.FieldId == "AMX_PRIVATE_SCORE" &&
            !field.IsEnabled &&
            field.DisabledReason!.Contains("No Meridian canonical source", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StrategyDesignerValidate_ShouldReturnBadTransitionLoopMessages()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var document = BuildValidDocument() with
        {
            Transitions =
            [
                new("loop", "rank", "filter", "loop", "weekly rebalance")
            ]
        };

        var response = await client.PostAsJsonAsync(
            "/api/workstation/strategy/designer/validate",
            document,
            ServerJsonOptions);
        var validation = await response.Content.ReadFromJsonAsync<StrategyDesignValidationResult>(ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        validation.Should().NotBeNull();
        validation!.IsValid.Should().BeFalse();
        validation.Messages.Should().Contain(message => message.Code == "LoopGuardRequired");
        validation.Messages.Should().Contain(message => message.Code == "LoopRationaleRequired");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StrategyDesignerRunBacktest_WithoutManageStrategies_ShouldReturnForbidden()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ViewStrategies);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/strategy/designer/run-backtest",
            BuildValidRunRequest(),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StrategyDesignerRunBacktest_WithoutQuantLab_ShouldReturnServiceUnavailable()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ManageStrategies);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/strategy/designer/run-backtest",
            BuildValidRunRequest(),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StrategyDesignerRunBacktest_WithQuantLab_ShouldRecordStrategyRun()
    {
        var runner = new CapturingScriptRunner();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IScriptRunner>(runner),
            UserPermission.ManageStrategies);
        var client = app.GetTestClient();
        var store = app.Services.GetRequiredService<IStrategyRepository>();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/strategy/designer/run-backtest",
            BuildValidRunRequest(parameters: new Dictionary<string, string> { ["from"] = "2026-01-01" }),
            ServerJsonOptions);
        var result = await response.Content.ReadFromJsonAsync<StrategyDesignRunBacktestResponse>(ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.RunId.Should().NotBeNullOrWhiteSpace();
        result.PromotionCandidatePath.Should().Be($"/api/promotion/evaluate/{result.RunId}");
        result.ReviewPacketPath.Should().Be($"/api/workstation/runs/{result.RunId}/review-packet");
        runner.LastSource.Should().Contain("Strategy Builder run");

        var stored = await store.GetRunByIdAsync(result.RunId!);
        stored.Should().NotBeNull();
        stored!.StrategyId.Should().Be("designer-test");
        stored.StrategyName.Should().Be("Designer test");
        stored.RunType.Should().Be(RunType.Backtest);
        stored.Engine.Should().Be("QuantScript");
        stored.DatasetReference.Should().Be("provider-bars/equities/daily");
        stored.ParameterSet.Should().ContainKey("datasetFingerprint");
        stored.Metrics.Should().NotBeNull();
        stored.EndedAt.Should().NotBeNull();
        stored.OperatorAcceptanceCriteria.Should().ContainSingle("Operator reviewed the retained backtest evidence.");
        stored.RetainedEvidenceReferences.Should().ContainSingle("evidence://strategy-designer/designer-test");
        stored.AccountingRecordReferences.Should().ContainSingle("ledger://books/11111111-1111-1111-1111-111111111111/accounts/strategy-designer/designer-test");
        stored.ApprovalReferences.Should().ContainSingle("approval://strategy-designer/designer-test");
        stored.PaperValidationReferences.Should().ContainSingle("workflow://fund/22222222-2222-2222-2222-222222222222");
        stored.GovernedReportReferences.Should().ContainSingle("reporting-run://designer-test/manifest");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task MapWorkstationEndpoints_StrategyDesignerRunBacktest_WithoutExactlyOneCapturedBacktest_ShouldFailWithoutRecordingRun(
        int capturedBacktestCount)
    {
        var runner = new CapturingScriptRunner(capturedBacktestCount);
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IScriptRunner>(runner),
            UserPermission.ManageStrategies);
        var client = app.GetTestClient();
        var store = app.Services.GetRequiredService<IStrategyRepository>();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/strategy/designer/run-backtest",
            BuildValidRunRequest(),
            ServerJsonOptions);
        var result = await response.Content.ReadFromJsonAsync<StrategyDesignRunBacktestResponse>(ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.RunId.Should().BeNull();
        result.RuntimeError.Should().Contain($"captured {capturedBacktestCount} BacktestResult values; exactly one is required");
        runner.InvocationCount.Should().Be(1);
        (await store.QueryRunsAsync(new StrategyRunRepositoryQuery(Limit: 10))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("identity", "nonblank strategy identity")]
    [InlineData("criterion", "nonblank operator acceptance criterion")]
    [InlineData("reference", "retained evidence, accounting, approval, paper-validation, or governed-report reference")]
    [InlineData("malformed-reference", "Every retained evidence reference must be a stable absolute URI")]
    [InlineData("mismatched-reference", "Every accounting record reference must be a stable absolute URI")]
    public async Task MapWorkstationEndpoints_StrategyDesignerRunBacktest_InvalidEvidence_ShouldFailBeforeRunnerOrPersistence(
        string invalidField,
        string expectedMessage)
    {
        var runner = new CapturingScriptRunner();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IScriptRunner>(runner),
            UserPermission.ManageStrategies);
        var client = app.GetTestClient();
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var request = invalidField switch
        {
            "identity" => BuildValidRunRequest(BuildValidDocument() with { DocumentId = " " }),
            "criterion" => BuildValidRunRequest() with { OperatorAcceptanceCriteria = [" ", "\t"] },
            "reference" => BuildValidRunRequest() with
            {
                RetainedEvidenceReferences = [],
                AccountingRecordReferences = [],
                ApprovalReferences = [],
                PaperValidationReferences = [],
                GovernedReportReferences = []
            },
            "malformed-reference" => BuildValidRunRequest() with
            {
                RetainedEvidenceReferences = ["not-a-stable-reference"]
            },
            "mismatched-reference" => BuildValidRunRequest() with
            {
                AccountingRecordReferences = ["evidence://strategy-designer/designer-test"]
            },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField), invalidField, null)
        };

        var response = await client.PostAsJsonAsync(
            "/api/workstation/strategy/designer/run-backtest",
            request,
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(expectedMessage);
        runner.InvocationCount.Should().Be(0);
        (await store.QueryRunsAsync(new StrategyRunRepositoryQuery(Limit: 10))).Should().BeEmpty();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StrategyDesignerRunBacktest_WithoutRunRepository_ShouldFailBeforeRunner()
    {
        var runner = new CapturingScriptRunner();
        await using var app = await CreateAppAsync(
            services => services.AddSingleton<IScriptRunner>(runner),
            UserPermission.ManageStrategies,
            registerStrategyRepository: false);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/strategy/designer/run-backtest",
            BuildValidRunRequest(),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        runner.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StrategyEngineDefinitions_ShouldExposeCoveredCallAndDesignerDefinitions()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var definitions = await client.GetFromJsonAsync<StrategyEngineDefinition[]>(
            "/api/workstation/strategy/engine/definitions",
            ServerJsonOptions);

        definitions.Should().NotBeNull();
        definitions.Should().Contain(definition =>
            definition.StrategyId == "covered-call" &&
            definition.UiMetadata!["route"] == "/workstation/strategy/covered-call");
        definitions.Should().Contain(definition =>
            definition.StrategyId == "options-payoff" &&
            definition.Type == StrategyEngineStrategyType.VisualDesigner);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StrategyEngineValidateRun_ShouldBlockMissingDataBeforeRun()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/strategy/engine/validate-run",
            new
            {
                runRequest = new
                {
                    strategyId = "covered-call",
                    strategyVersion = "1",
                    parameters = new Dictionary<string, string>
                    {
                        ["underlying"] = "SPY",
                        ["daysToExpiration"] = "30",
                        ["targetDelta"] = "0.30",
                        ["contracts"] = "1",
                        ["maxAssignmentRisk"] = "0.25"
                    },
                    universe = new[] { "SPY" },
                    from = "2026-01-01T00:00:00Z",
                    to = "2026-02-01T00:00:00Z",
                    dataSource = "fixture",
                    mode = "Backtest"
                },
                dataAvailability = Array.Empty<StrategyEngineDataAvailability>()
            },
            ServerJsonOptions);
        var validation = await response.Content.ReadFromJsonAsync<StrategyEngineValidationResult>(ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        validation.Should().NotBeNull();
        validation!.IsValid.Should().BeFalse();
        validation.Findings.Should().Contain(finding => finding.Code == "dependency-missing");
        validation.Evidence.EvidenceRoute.Should().Be("/workstation/reporting/evidence");
    }

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection>? configureServices = null,
        UserPermission currentUserPermissions = UserPermission.ManageStrategies,
        bool registerStrategyRepository = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        if (registerStrategyRepository)
        {
            builder.Services.AddSingleton<IStrategyRepository, StrategyRunStore>();
        }
        builder.Services.AddSingleton<StrategyDesignService>();
        builder.Services.AddSingleton<StrategyEngineRegistry>();
        builder.Services.AddSingleton<StrategyEngineValidationService>();
        builder.Services.AddSingleton<IStrategyDesignRepository>(_ => new JsonlStrategyDesignRepository(
            Path.Combine(Path.GetTempPath(), "meridian-tests", "strategy-designer-endpoints", Guid.NewGuid().ToString("N")),
            NullLogger<JsonlStrategyDesignRepository>.Instance));
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "strategy-operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = currentUserPermissions;
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "strategy-test-company";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "strategy-test-tenant";
            await next();
        });
        app.MapWorkstationEndpoints(ServerJsonOptions);
        await app.StartAsync();
        return app;
    }

    private static StrategyDesignDocument BuildValidDocument()
        => new(
            "designer-test",
            "Designer test",
            "Endpoint strategy design",
            "1",
            "provider-bars/equities/daily",
            ["SPY", "QQQ"],
            [
                new("filter", "Filter universe", "visual", "filter", "PRICE > 20", ["PRICE"]),
                new("rank", "Rank universe", "formula", "rank", "MOMENTUM_63D - VOLATILITY_20D", ["MOMENTUM_63D", "VOLATILITY_20D"]),
                new("risk", "Risk guard", "governance", "risk", "VOLATILITY_20D < 0.3", ["VOLATILITY_20D"])
            ],
            [
                new("t1", "filter", "rank", "next", "filter complete"),
                new("t2", "rank", "risk", "next", "rank complete")
            ],
            null,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(-1));

    private static StrategyDesignRunBacktestRequest BuildValidRunRequest(
        StrategyDesignDocument? document = null,
        IReadOnlyDictionary<string, string>? parameters = null)
        => new(
            Document: document ?? BuildValidDocument(),
            Parameters: parameters)
        {
            OperatorAcceptanceCriteria =
            [
                " Operator reviewed the retained backtest evidence. ",
                "operator reviewed the retained backtest evidence."
            ],
            RetainedEvidenceReferences =
            [
                " evidence://strategy-designer/designer-test ",
                "evidence://strategy-designer/designer-test"
            ],
            AccountingRecordReferences = ["ledger://books/11111111-1111-1111-1111-111111111111/accounts/strategy-designer/designer-test"],
            ApprovalReferences = ["approval://strategy-designer/designer-test"],
            PaperValidationReferences = ["workflow://fund/22222222-2222-2222-2222-222222222222"],
            GovernedReportReferences = ["reporting-run://designer-test/manifest"]
        };

    private static BacktestResult BuildCapturedBacktestResult()
        => new(
            Request: new BacktestRequest(
                From: new DateOnly(2026, 1, 1),
                To: new DateOnly(2026, 1, 31),
                Symbols: ["SPY", "QQQ"],
                InitialCash: 100_000m,
                DataRoot: "provider-bars/equities/daily"),
            Universe: new HashSet<string>(["SPY", "QQQ"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [],
            CashFlows: [],
            Fills: [],
            Metrics: new BacktestMetrics(
                InitialCapital: 100_000m,
                FinalEquity: 101_000m,
                GrossPnl: 1_000m,
                NetPnl: 1_000m,
                TotalReturn: 0.01m,
                AnnualizedReturn: 0.01m,
                SharpeRatio: 1.1,
                SortinoRatio: 1.0,
                CalmarRatio: 0.9,
                MaxDrawdown: 500m,
                MaxDrawdownPercent: 0.005m,
                MaxDrawdownRecoveryDays: 1,
                ProfitFactor: 1.3,
                WinRate: 0.55,
                TotalTrades: 2,
                WinningTrades: 1,
                LosingTrades: 1,
                TotalCommissions: 5m,
                TotalMarginInterest: 0m,
                TotalShortRebates: 0m,
                Xirr: 0.01,
                SymbolAttribution: new Dictionary<string, SymbolAttribution>()),
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(1),
            TotalEventsProcessed: 10L);

    private sealed class CapturingScriptRunner : IScriptRunner
    {
        private readonly int _capturedBacktestCount;

        public CapturingScriptRunner(int capturedBacktestCount = 1)
        {
            _capturedBacktestCount = capturedBacktestCount;
        }

        public string LastSource { get; private set; } = string.Empty;

        public int InvocationCount { get; private set; }

        public Task<ScriptRunResult> RunAsync(
            string source,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken ct = default)
        {
            InvocationCount++;
            LastSource = source;
            return Task.FromResult(new ScriptRunResult(
                Success: true,
                Elapsed: TimeSpan.FromMilliseconds(10),
                CompileTime: TimeSpan.FromMilliseconds(2),
                PeakMemoryBytes: 1024,
                CompilationErrors: [],
                RuntimeDiagnostics: [],
                RuntimeError: null,
                ConsoleOutput: "strategy builder run complete",
                Metrics:
                [
                    new KeyValuePair<string, string>("Designer cells", "3"),
                    new KeyValuePair<string, string>("Mapped fields", "3")
                ],
                Plots: [],
                Trades: [],
                CapturedBacktests: Enumerable.Range(0, _capturedBacktestCount)
                    .Select(static _ => BuildCapturedBacktestResult())
                    .ToArray(),
                RuntimeParameters: []));
        }

        public Task<ScriptRunResult> ContinueWithAsync(
            string source,
            ScriptExecutionCheckpoint checkpoint,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken ct = default)
            => RunAsync(source, parameters, ct);
    }
}
