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
    public async Task OmsAmendment_InvalidSnapshotBeforePatch_IsAnOrdinaryRejectionWithoutRecovery()
    {
        var corrupt = false;
        var patches = 0;
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v2/account")
                return new(HttpStatusCode.OK) { Content = BuildAccountResponse() };
            if (request.Method == HttpMethod.Patch)
                patches++;
            return new(HttpStatusCode.OK)
            {
                Content = BuildOrderResponse("original", corrupt ? "unrecognised" : "accepted",
                    clientOrderId: "local", qty: "10", limitPrice: "150")
            };
        });
        await using var gateway = CreateSut(handler);
        await gateway.ConnectAsync();
        await using var oms = new OrderManagementSystem(new SimulatedAlpaca(gateway), NullLogger<OrderManagementSystem>.Instance,
            portfolioState: new PaperTradingPortfolio(100_000m));
        (await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = "local",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        })).Success.Should().BeTrue();
        corrupt = true;

        var amendment = await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m });

        amendment.Success.Should().BeFalse();
        amendment.ErrorMessage.Should().Contain("consistent order identity");
        patches.Should().Be(0);
        oms.GetExposureReservingOrders().Single().Quantity.Should().Be(10m);
    }
}

public sealed partial class AlpacaBrokerageGatewayTests
{
    [Fact]
    public async Task Recovery_ReplacedPredecessorWithAcceptedForBiddingSuccessor_StaysPending()
    {
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v2/account")
                return new(HttpStatusCode.OK) { Content = BuildAccountResponse() };
            if (request.RequestUri.AbsolutePath == "/v2/orders/candidate")
                return new(HttpStatusCode.OK)
                {
                    Content = BuildOrderResponse("candidate", "accepted_for_bidding",
                        clientOrderId: "candidate-client", qty: "20", replaces: "original", limitPrice: "149")
                };
            return new(HttpStatusCode.OK)
            {
                Content = BuildOrderResponse("original", "replaced",
                    clientOrderId: "local", qty: "10", replacedBy: "candidate", limitPrice: "150")
            };
        });
        await using var gateway = CreateSut(handler);
        await gateway.ConnectAsync();

        var recovered = await gateway.GetOrderForRecoveryAsync("local");

        recovered!.ReportType.Should().Be(ExecutionReportType.ModificationPending);
    }
}
