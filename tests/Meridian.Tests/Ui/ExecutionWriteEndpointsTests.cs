using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Contract tests for Trading write-action endpoints introduced in Sprint 2.
/// Verifies that cancel-order, cancel-all, and close-position return structured
/// <see cref="TradingActionResult"/> payloads for success and rejection paths.
/// </summary>
public sealed class ExecutionWriteEndpointsTests
{
    // ------------------------------------------------------------------ //
    //  POST /api/execution/orders/{orderId}/cancel                        //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task CancelOrder_WhenOmsNotRegistered_Returns503()
    {
        await using var app = await CreateAppAsync(services =>
        {
            // Intentionally do NOT register IOrderManager — simulates no OMS
        });

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

        // The OMS returns Success=false for unknown orders; endpoint maps this to 400
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
        await using var app = await CreateAppAsync(services =>
        {
            // No OMS registered
        });

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
    //  POST /api/execution/positions/{symbol}/close                       //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task ClosePosition_WhenServicesNotRegistered_Returns503()
    {
        await using var app = await CreateAppAsync(services =>
        {
            // No execution services
        });

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

        // No position for AAPL → endpoint returns 400 with Rejected status
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Rejected");
        result.ActionId.Should().NotBeNullOrEmpty();
        result.Message.Should().Contain("AAPL");
    }

    // ------------------------------------------------------------------ //
    //  Helpers                                                            //
    // ------------------------------------------------------------------ //

    private static void RegisterMinimalOms(IServiceCollection services)
    {
        services.AddSingleton<IExecutionGateway, PaperTradingGateway>();
        services.AddSingleton<IOrderManager, OrderManagementSystem>();
        services.AddSingleton<Meridian.Execution.Models.IPortfolioState, EmptyPortfolioState>();
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

        await app.StartAsync();
        return app;
    }

    private static async Task<TradingActionResult> ReadActionResultAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        var result = JsonSerializer.Deserialize<TradingActionResult>(json, opts);
        result.Should().NotBeNull($"expected a TradingActionResult in response body, but got: {json}");
        return result!;
    }
}

/// <summary>Empty portfolio state used in test fixtures where no positions exist.</summary>
file sealed class EmptyPortfolioState : Meridian.Execution.Models.IPortfolioState
{
    public decimal Cash => 100_000m;
    public decimal PortfolioValue => 100_000m;
    public decimal UnrealisedPnl => 0m;
    public decimal RealisedPnl => 0m;
    public IReadOnlyDictionary<string, Meridian.Execution.Models.ExecutionPosition> Positions =>
        new Dictionary<string, Meridian.Execution.Models.ExecutionPosition>();
}

