using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
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
    }

    [Fact]
    public async Task RiskAndExecutionIntegration_PositionLimitRejectsOversizedOrder()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(new RiskRuleRuntimeOptions(Path.Combine(Path.GetTempPath(), $"risk-rules-{Guid.NewGuid():N}.json")));
            services.AddSingleton<PaperTradingPortfolio>(_ => new PaperTradingPortfolio(100_000m));
            services.AddSingleton<IPortfolioState>(sp => sp.GetRequiredService<PaperTradingPortfolio>());
            services.AddSingleton<IPositionTracker, StaticPositionTracker>();
            services.AddSingleton<IExecutionGateway>(_ => new Meridian.Execution.PaperTradingGateway(NullLogger<Meridian.Execution.PaperTradingGateway>.Instance));
            services.AddSingleton<OperatorRiskRuleService>();
            services.AddSingleton<IRiskValidator>(sp => sp.GetRequiredService<OperatorRiskRuleService>());
            services.AddSingleton<IOrderManager>(sp =>
                new OrderManagementSystem(
                    sp.GetRequiredService<IExecutionGateway>(),
                    NullLogger<OrderManagementSystem>.Instance,
                    riskValidator: sp.GetRequiredService<IRiskValidator>(),
                    portfolioState: sp.GetRequiredService<PaperTradingPortfolio>()));
        }, includeExecutionEndpoints: true);

        var client = app.GetTestClient();

        var riskRules = app.Services.GetRequiredService<OperatorRiskRuleService>();
        await riskRules.UpdateRuleConfigAsync(
            "PositionLimit",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["maxPositionSize"] = "1"
            },
            updatedBy: "test",
            reason: "tighten limits");

        var submitResponse = await client.PostAsync(
            "/api/execution/orders/submit",
            JsonContent(new
            {
                symbol = "AAPL",
                side = 0,
                type = 0,
                timeInForce = 0,
                quantity = 2,
                strategyId = "risk-check"
            }));

        submitResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = JsonSerializer.Deserialize<OrderResult>(await submitResponse.Content.ReadAsStringAsync(), JsonOptions());
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Position limit exceeded");
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
}
