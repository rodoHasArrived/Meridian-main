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
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Contract tests for execution write-action and blotter endpoints.
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
<<<<<<< HEAD
    public decimal UnrealisedPnl { get; } = positions.Sum(position => position.UnrealisedPnl);
    public decimal RealisedPnl { get; } = positions.Sum(position => position.RealisedPnl);
    public IReadOnlyDictionary<string, Meridian.Execution.Sdk.IPosition> Positions { get; } = positions.ToDictionary(
        position => position.Symbol,
        position => (Meridian.Execution.Sdk.IPosition)position,
        StringComparer.OrdinalIgnoreCase);
=======
    public decimal UnrealisedPnl => 0m;
    public decimal RealisedPnl => 0m;
    public IReadOnlyDictionary<string, IPosition> Positions =>
        new Dictionary<string, IPosition>();
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
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
