using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Auth;
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
            new StrategyDesignRunBacktestRequest(BuildValidDocument()),
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
            new StrategyDesignRunBacktestRequest(BuildValidDocument()),
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
            new StrategyDesignRunBacktestRequest(BuildValidDocument(), new Dictionary<string, string> { ["from"] = "2026-01-01" }),
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
    }

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection>? configureServices = null,
        UserPermission currentUserPermissions = UserPermission.ManageStrategies)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IStrategyRepository, StrategyRunStore>();
        builder.Services.AddSingleton<StrategyDesignService>();
        builder.Services.AddSingleton<IStrategyDesignRepository>(_ => new JsonlStrategyDesignRepository(
            Path.Combine(Path.GetTempPath(), "meridian-tests", "strategy-designer-endpoints", Guid.NewGuid().ToString("N")),
            NullLogger<JsonlStrategyDesignRepository>.Instance));
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "strategy-operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = currentUserPermissions;
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

    private sealed class CapturingScriptRunner : IScriptRunner
    {
        public string LastSource { get; private set; } = string.Empty;

        public Task<ScriptRunResult> RunAsync(
            string source,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken ct = default)
        {
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
                CapturedBacktests: [],
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
