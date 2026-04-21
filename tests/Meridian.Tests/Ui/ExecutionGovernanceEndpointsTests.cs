using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Services;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ExecutionGovernanceEndpointsTests
{
    [Fact]
    public async Task ControlsEndpoints_UpdateCircuitBreakerAndExposeAuditTrail()
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

        var response = await client.PostAsync(
            "/api/execution/controls/circuit-breaker",
            JsonContent(new { isOpen = true, reason = "manual halt" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

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
            entry.Actor == "ops");
    }

    [Fact]
    public async Task ControlsEndpoints_CreateAndClearManualOverride_UpdatesControlsAndAuditTrail()
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
        client.DefaultRequestHeaders.Add("X-Meridian-Actor", "ops-supervisor");

        var createResponse = await client.PostAsync(
            "/api/execution/controls/manual-overrides",
            JsonContent(new
            {
                kind = ExecutionManualOverrideKinds.AllowLivePromotion,
                reason = "promotion approval window",
                strategyId = "strat-42"
            }));

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var createdOverride = createJson.RootElement
            .GetProperty("manualOverrides")
            .EnumerateArray()
            .Single();

        var overrideId = createdOverride.GetProperty("overrideId").GetString();
        overrideId.Should().NotBeNullOrWhiteSpace();
        createdOverride.GetProperty("kind").GetString().Should().Be(ExecutionManualOverrideKinds.AllowLivePromotion);
        createdOverride.GetProperty("strategyId").GetString().Should().Be("strat-42");

        var controlsAfterCreate = await client.GetAsync("/api/execution/controls");
        controlsAfterCreate.StatusCode.Should().Be(HttpStatusCode.OK);
        using var controlsAfterCreateJson = JsonDocument.Parse(await controlsAfterCreate.Content.ReadAsStringAsync());
        controlsAfterCreateJson.RootElement
            .GetProperty("manualOverrides")
            .EnumerateArray()
            .Should()
            .Contain(entry => entry.GetProperty("overrideId").GetString() == overrideId);

        var clearResponse = await client.PostAsync(
            $"/api/execution/controls/manual-overrides/{overrideId}/clear",
            JsonContent(new { reason = "window closed" }));
        clearResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var clearJson = JsonDocument.Parse(await clearResponse.Content.ReadAsStringAsync());
        clearJson.RootElement.GetProperty("manualOverrides").GetArrayLength().Should().Be(0);

        var controlsAfterClear = await client.GetAsync("/api/execution/controls");
        controlsAfterClear.StatusCode.Should().Be(HttpStatusCode.OK);
        using var controlsAfterClearJson = JsonDocument.Parse(await controlsAfterClear.Content.ReadAsStringAsync());
        controlsAfterClearJson.RootElement.GetProperty("manualOverrides").GetArrayLength().Should().Be(0);

        var auditResponse = await client.GetAsync("/api/execution/audit?take=20");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditEntries = JsonSerializer.Deserialize<ExecutionAuditEntry[]>(
            await auditResponse.Content.ReadAsStringAsync(),
            JsonOptions());

        auditEntries.Should().NotBeNull();
        auditEntries!.Should().Contain(entry =>
            entry.Action == "ManualOverrideCreated" &&
            entry.Actor == "ops-supervisor");
        auditEntries.Should().Contain(entry =>
            entry.Action == "ManualOverrideCleared" &&
            entry.Actor == "ops-supervisor");
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
            services.AddSingleton(sp => new BrokerageConfiguration
            {
                Gateway = "alpaca",
                LiveExecutionEnabled = true,
                MaxPositionSize = 100m
            });
            services.AddSingleton(sp => new AlpacaBrokerageGateway(
                sp.GetRequiredService<IHttpClientFactory>(),
                new Meridian.Application.Config.AlpacaOptions(KeyId: "test-key", SecretKey: "test-secret"),
                NullLogger<AlpacaBrokerageGateway>.Instance));
            services.AddBrokerageGateway("alpaca", sp => sp.GetRequiredService<AlpacaBrokerageGateway>());
            services.AddBrokerageExecution(config =>
            {
                config.Gateway = "alpaca";
                config.LiveExecutionEnabled = true;
                config.MaxPositionSize = 100m;
            });
        });

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
                strategyId = "strategy-live"
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
            services.AddSingleton(sp => new BrokerageConfiguration
            {
                Gateway = "robinhood",
                LiveExecutionEnabled = true,
                MaxPositionSize = 100m
            });
            services.AddSingleton(sp => new RobinhoodBrokerageGateway(
                sp.GetRequiredService<IHttpClientFactory>(),
                NullLogger<RobinhoodBrokerageGateway>.Instance,
                accessToken: "test-token"));
            services.AddHostedBrokerageGateways();
            services.AddBrokerageExecution(config =>
            {
                config.Gateway = "robinhood";
                config.LiveExecutionEnabled = true;
                config.MaxPositionSize = 100m;
            });
        });

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
                strategyId = "strategy-live"
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

    private static async Task<WebApplication> CreateAppAsync(Action<IServiceCollection> configureServices)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        configureServices(builder.Services);

        var app = builder.Build();
        app.MapExecutionEndpoints(JsonOptions());
        await app.StartAsync();
        return app;
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
}
