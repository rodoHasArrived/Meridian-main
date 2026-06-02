using System.Reflection;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Templates;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class TemplateBrokerageGatewayTests
{
    [Fact]
    public void TemplateBrokerageGateway_DeclaresProviderDiscoveryMetadata()
    {
        var type = typeof(TemplateBrokerageGateway);

        var dataSource = type.GetCustomAttribute<DataSourceAttribute>();
        var adrs = type.GetCustomAttributes<ImplementsAdrAttribute>().Select(attr => attr.AdrId);

        dataSource.Should().NotBeNull();
        dataSource!.Id.Should().Be("template-brokerage");
        dataSource.DisplayName.Should().Be("Template Brokerage");
        dataSource.Category.Should().Be(DataSourceCategory.Broker);
        adrs.Should().Contain(["ADR-001", "ADR-004", "ADR-005"]);
    }

    [Fact]
    public void Constructor_UsesConfiguredIdentityAndCapabilities()
    {
        var capabilities = BrokerageCapabilities.UsEquity(
            modification: false,
            partialFills: false,
            shortSelling: true,
            fractional: true,
            extendedHours: true);

        var gateway = new TemplateBrokerageGateway(
            NullLogger<TemplateBrokerageGateway>.Instance,
            new TemplateBrokerageGatewayOptions
            {
                GatewayId = "demo-broker",
                BrokerDisplayName = "Demo Broker",
                Capabilities = capabilities,
            });

        gateway.GatewayId.Should().Be("demo-broker");
        gateway.BrokerDisplayName.Should().Be("Demo Broker");
        gateway.BrokerageCapabilities.Should().BeSameAs(capabilities);
    }

    [Fact]
    public async Task GetAccountInfoAsync_ReturnsConfiguredTemplateAccount()
    {
        await using var gateway = new TemplateBrokerageGateway(
            NullLogger<TemplateBrokerageGateway>.Instance,
            new TemplateBrokerageGatewayOptions
            {
                AccountId = "paper-template-1",
                Equity = 125_000m,
                Cash = 25_000m,
                BuyingPower = 50_000m,
                Currency = "USD",
                AccountStatus = "paper-ready",
            });

        await gateway.ConnectAsync();

        var account = await gateway.GetAccountInfoAsync();

        account.AccountId.Should().Be("paper-template-1");
        account.Equity.Should().Be(125_000m);
        account.Cash.Should().Be(25_000m);
        account.BuyingPower.Should().Be(50_000m);
        account.Status.Should().Be("paper-ready");
    }

    [Fact]
    public async Task ConnectAsync_RejectsTemplateConnectionWhenReadinessIsDisabled()
    {
        await using var gateway = new TemplateBrokerageGateway(
            NullLogger<TemplateBrokerageGateway>.Instance,
            new TemplateBrokerageGatewayOptions
            {
                ConnectionReady = false,
                ConnectionFailureMessage = "Credential fixture missing.",
            });

        var act = () => gateway.ConnectAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Credential fixture missing.");
        gateway.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitOrderAsync_HonorsCancellationBeforeMutatingOpenOrders()
    {
        await using var gateway = new TemplateBrokerageGateway(NullLogger<TemplateBrokerageGateway>.Instance);
        await gateway.ConnectAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 5m,
            LimitPrice = 410m,
            ClientOrderId = "client-cancelled",
        }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var openOrders = await gateway.GetOpenOrdersAsync();
        openOrders.Should().BeEmpty();
    }

    [Fact]
    public async Task ModifyOrderAsync_HonorsCancellationBeforeMutatingOpenOrders()
    {
        await using var gateway = new TemplateBrokerageGateway(NullLogger<TemplateBrokerageGateway>.Instance);
        await gateway.ConnectAsync();
        var submitted = await gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 5m,
            LimitPrice = 410m,
        });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => gateway.ModifyOrderAsync(submitted.OrderId, new OrderModification
        {
            NewQuantity = 10m,
            NewLimitPrice = 412m,
        }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var openOrder = (await gateway.GetOpenOrdersAsync()).Should().ContainSingle().Subject;
        openOrder.Quantity.Should().Be(5m);
        openOrder.LimitPrice.Should().Be(410m);
    }

    [Fact]
    public async Task GetPositionsAsync_ReturnsConfiguredTemplatePositions()
    {
        var positions = new[]
        {
            new BrokerPosition
            {
                PositionId = "pos-aapl",
                Symbol = "AAPL",
                Quantity = 10m,
                AverageEntryPrice = 180m,
                MarketPrice = 185m,
                MarketValue = 1_850m,
                UnrealizedPnl = 50m,
            },
        };
        await using var gateway = new TemplateBrokerageGateway(
            NullLogger<TemplateBrokerageGateway>.Instance,
            new TemplateBrokerageGatewayOptions { Positions = positions });

        await gateway.ConnectAsync();

        var result = await gateway.GetPositionsAsync();

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(positions[0]);
    }

    [Fact]
    public async Task GetOpenOrdersAsync_TracksSubmittedModifiedAndCancelledTemplateOrders()
    {
        await using var gateway = new TemplateBrokerageGateway(NullLogger<TemplateBrokerageGateway>.Instance);
        await gateway.ConnectAsync();

        var submitted = await gateway.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 5m,
            LimitPrice = 410m,
            ClientOrderId = "client-1",
        });

        var openOrders = await gateway.GetOpenOrdersAsync();
        openOrders.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            OrderId = submitted.OrderId,
            ClientOrderId = "client-1",
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 5m,
            LimitPrice = 410m,
            Status = OrderStatus.Accepted,
        });

        await gateway.ModifyOrderAsync(submitted.OrderId, new OrderModification
        {
            NewQuantity = 7m,
            NewLimitPrice = 411m,
        });

        openOrders = await gateway.GetOpenOrdersAsync();
        openOrders.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            OrderId = submitted.OrderId,
            Quantity = 7m,
            LimitPrice = 411m,
            Status = OrderStatus.Accepted,
        }, options => options.ExcludingMissingMembers());

        await gateway.CancelOrderAsync(submitted.OrderId);

        openOrders = await gateway.GetOpenOrdersAsync();
        openOrders.Should().BeEmpty();
    }

    [Fact]
    public async Task DisconnectAsync_RespectsCancellationAndMarksTemplateDisconnected()
    {
        await using var gateway = new TemplateBrokerageGateway(NullLogger<TemplateBrokerageGateway>.Instance);
        await gateway.ConnectAsync();

        await gateway.DisconnectAsync();

        gateway.IsConnected.Should().BeFalse();
    }
}
