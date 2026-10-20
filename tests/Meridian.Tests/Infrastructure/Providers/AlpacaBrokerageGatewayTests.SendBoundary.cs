using System.Net;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class AlpacaBrokerageGatewayTests
{
    [Fact]
    public async Task OmsAmendment_CancelledBeforeThePatch_IsNotSentAndLeavesTheOrderUnblocked()
    {
        using var stop = new CancellationTokenSource();
        var cancelOnRead = false;
        var patches = 0;
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v2/account")
                return new(HttpStatusCode.OK) { Content = BuildAccountResponse() };
            if (request.Method == HttpMethod.Patch)
            {
                patches++;
                return new(HttpStatusCode.OK)
                {
                    Content = BuildOrderResponse("candidate", "pending_new",
                        clientOrderId: "candidate-client", qty: "20", replaces: "original", limitPrice: "150")
                };
            }
            if (request.Method == HttpMethod.Get && cancelOnRead)
            {
                cancelOnRead = false;
                stop.Cancel(); // the caller gives up while the pre-PATCH read is in flight
            }
            return new(HttpStatusCode.OK)
            {
                Content = BuildOrderResponse("original", "accepted", clientOrderId: "local", qty: "10", limitPrice: "150")
            };
        });
        await using var gateway = CreateSut(handler);
        await gateway.ConnectAsync();
        await using var oms = new OrderManagementSystem(new SimulatedAlpaca(gateway), NullLogger<OrderManagementSystem>.Instance,
            portfolioState: new PaperTradingPortfolio(100_000m));
        await oms.StartAsync();
        (await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = "local",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        })).Success.Should().BeTrue();
        cancelOnRead = true;

        var cancelled = await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m }, stop.Token);

        cancelled.Success.Should().BeFalse();
        patches.Should().Be(0);
        oms.GetExposureReservingOrders().Single().Quantity.Should().Be(10m, "the unsent amendment released its reservation");
        await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m });
        patches.Should().Be(1, "neither the OMS nor the gateway retained the unsent attempt");
    }
}
