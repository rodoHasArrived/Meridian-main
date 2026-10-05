using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

[Collection("Sequential")]
public sealed class BrokeragePortfolioStateTests
{
    [Fact]
    public async Task BrokerObservation_PreservesFractionalPositionsAndRemainsReadableThroughFillHandoff()
    {
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var snapshot = new BrokeragePortfolioSnapshotDto(
            new("alpaca", "paper-account", "Paper account", "active", "USD", now),
            new(950m, 1000m, 1800m, "USD"),
            [new("AAPL", 0.5m, 80m, 100m, 50m, 10m, "us_equity", Currency: "USD")],
            now, new("alpaca", "paper-account", now, "USD", "active", BrokerageMarginRegime.RegulationT,
                950m, 1000m, 1800m), IsComplete: true);
        var gateway = Substitute.For<IBrokerageGateway, IBrokeragePortfolioSync>();
        gateway.GatewayId.Returns("alpaca");
        gateway.IsConnected.Returns(true);
        gateway.CheckHealthAsync(Arg.Any<CancellationToken>()).Returns(BrokerHealthStatus.Healthy());
        gateway.GetOpenOrdersAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<BrokerOrder>());
        ((IBrokeragePortfolioSync)gateway).GetPortfolioSnapshotAsync("paper-account", Arg.Any<CancellationToken>())
            .Returns(snapshot);
        var oms = Substitute.For<IOrderManager>();
        oms.GetOpenOrders().Returns(Array.Empty<OrderState>());
        oms.GetExposureReservingOrders().Returns(Array.Empty<OrderState>());
        oms.GetCompletedOrders(Arg.Any<int>()).Returns(Array.Empty<OrderState>());
        var synchronization = new LiveBrokeragePortfolioSyncService(() => gateway, () => oms,
            new BrokerageExecutionReconciliationService(NullLogger<BrokerageExecutionReconciliationService>.Instance));
        var portfolio = new BrokeragePortfolioState(synchronization);
        synchronization.GetStatus(accountId).IsReady.Should().BeFalse();

        (await synchronization.SynchronizeAsync(accountId, "paper-account")).IsReady.Should().BeTrue();

        portfolio.Cash.Should().Be(950m);
        portfolio.BuyingPower.Should().Be(1800m);
        portfolio.PortfolioValue.Should().Be(1000m);
        portfolio.Positions["AAPL"].ExactQuantity.Should().Be(0.5m);
        portfolio.Positions["AAPL"].OwnerQuantities[accountId.ToString("D")].Should().Be(0.5m);
        portfolio.GetPosition("AAPL").Quantity.Should().Be(0.5m);
        portfolio.GetPosition("AAPL").MarketPrice.Should().Be(100m);
        portfolio.UnrealisedPnl.Should().Be(10m);

        // A local fill dirties readiness. Reading the host portfolio during accounting
        // handoff must retain the broker observation without applying the fill twice.
        oms.GetCompletedOrders(Arg.Any<int>()).Returns([new OrderState
        {
            OrderId = "filled-after-sync", Symbol = "AAPL", Side = OrderSide.Buy,
            Type = Meridian.Execution.Sdk.OrderType.Limit,
            Quantity = 1m, FilledQuantity = 1m, Status = Meridian.Execution.Sdk.OrderStatus.Filled, FundAccountId = accountId
        }]);
        synchronization.GetStatus(accountId).IsReady.Should().BeFalse();
        portfolio.Cash.Should().Be(950m);
        portfolio.GetPosition("AAPL").Quantity.Should().Be(0.5m);
        portfolio.GetPortfolioValue().Should().Be(1000m);
    }

    [Fact]
    public async Task UiServer_LiveAlpaca_ComposesBrokerObservationAndDurableOmsWithoutEnablingOrders()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-host", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new
        {
            DataRoot = Path.Combine(root, "data"),
            DataSource = "Synthetic",
            Symbols = new[] { new { Symbol = "SPY", SubscribeTrades = true, Currency = "USD" } },
            Backfill = new { Enabled = false },
            Execution = new { Brokerage = new { Gateway = "alpaca", LiveExecutionEnabled = true } }
        }));
        var names = new[] { "MERIDIAN_USE_INMEMORY_GOVERNANCE", "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT" };
        var previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(names[0], "true");
            Environment.SetEnvironmentVariable(names[1], Environments.Development);
            Environment.SetEnvironmentVariable(names[2], Environments.Development);
            await using var server = new UiServer(configPath, port: 0);
            var app = (WebApplication)typeof(UiServer).GetField("_app", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(server)!;

            var portfolio = app.Services.GetRequiredService<IPortfolioState>();
            portfolio.Should().BeOfType<BrokeragePortfolioState>();
            app.Services.GetRequiredService<IPositionTracker>().Should().BeSameAs(portfolio);
            var oms = app.Services.GetRequiredService<IOrderManager>().Should().BeOfType<OrderManagementSystem>().Subject;
            var recoveryStore = app.Services.GetRequiredService<FileBrokerageOrderRecoveryStore>();
            typeof(OrderManagementSystem).GetField("_recoveryStore", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(oms).Should().BeSameAs(recoveryStore);
            recoveryStore.GatewayId.Should().Be("alpaca");
            app.Services.GetRequiredService<LiveBrokeragePortfolioSyncService>()
                .GetStatus(Guid.NewGuid()).IsReady.Should().BeFalse();
        }
        finally
        {
            foreach (var name in names)
                Environment.SetEnvironmentVariable(name, previous[name]);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
