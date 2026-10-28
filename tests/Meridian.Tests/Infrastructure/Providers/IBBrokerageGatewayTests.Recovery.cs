using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class IBBrokerageGatewayTests
{
    private static readonly OrderState RecoveryOriginal = new()
    {
        OrderId = "client-1",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 100m,
        FilledQuantity = 2m,
        AverageFillPrice = 99m,
        Status = OrderStatus.PartiallyFilled
    };

    [Theory]
    [InlineData(20, 200, ExecutionReportType.Modified)]
    [InlineData(10, 100, ExecutionReportType.ModificationRejected)]
    public async Task AmendmentRecovery_AfterRestart_SelectsOutcomeFromEchoedTermsAndRestoresContext(
        int echoedQuantity, int echoedLimit, ExecutionReportType expected)
    {
        var cancelled = new List<int>();
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                c.RaiseOpenOrder(new IBOpenOrderUpdate(7001, "AAPL", "STK", "BUY", "LMT", echoedQuantity, 0m,
                    echoedLimit, null, "Submitted", "client-1", "DU123456", null, null, null, DateTimeOffset.UtcNow));
                c.RaiseOrderStatus(new IBOrderStatusUpdate(7001, "Submitted", 5m, echoedQuantity - 5m, 99.4d, 100d,
                    1L, 1, null, null, DateTimeOffset.UtcNow));
                c.RaiseOpenOrdersCompleted();
            },
            OnCancelOrder = (c, id) =>
            {
                cancelled.Add(id);
                c.RaiseOrderStatus(new IBOrderStatusUpdate(id, "Cancelled", 5m, 0m, 99.4d, 0d, 1L, 1, null, null,
                    DateTimeOffset.UtcNow));
            }
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();
        var amendment = new PendingOrderAmendment(RecoveryOriginal,
            RecoveryOriginal with { Quantity = 20m, LimitPrice = 200m }, "7001");

        var recovered = await ((IBrokerageOrderAmendmentRecoveryGateway)sut)
            .GetAmendmentForRecoveryAsync("client-1", RecoveryOriginal, amendment);

        recovered.Should().NotBeNull();
        recovered!.ReportType.Should().Be(expected);
        recovered.OrderId.Should().Be("client-1");
        recovered.GatewayOrderId.Should().Be("7001");
        recovered.OrderQuantity.Should().Be(echoedQuantity);
        recovered.OrderLimitPrice.Should().Be(echoedLimit);
        recovered.HasOrderTerms.Should().BeTrue();
        recovered.FilledQuantity.Should().Be(5m);
        recovered.FillPrice.Should().Be(99.4m);
        recovered.FillPriceIsCumulativeAverage.Should().BeTrue();

        var amend = () => sut.ModifyOrderAsync("client-1", new OrderModification { NewQuantity = 30m });
        await amend.Should().ThrowAsync<InvalidOperationException>("guessed time-in-force must never be resubmitted");

        var cancel = await sut.CancelOrderAsync("client-1");
        cancelled.Should().Equal(7001);
        cancel.OrderStatus.Should().Be(OrderStatus.Cancelled);
    }

    [Theory]
    [InlineData(0)]   // a completed order is absent from the open-order snapshot
    [InlineData(2)]   // two working orders claim the same reference
    public async Task OrderRecovery_AbsentOrAmbiguousEvidence_StaysUnresolved(int matches)
    {
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                for (var i = 0; i < matches; i++)
                    c.RaiseOpenOrder(new IBOpenOrderUpdate(7001 + i, "AAPL", "STK", "BUY", "LMT", 10m, 0m, 100d, null,
                        "Submitted", "client-1", "DU123456", null, null, null, DateTimeOffset.UtcNow));
                c.RaiseOpenOrdersCompleted();
            }
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        var recovered = await ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1");

        recovered.Should().BeNull("absence or ambiguity is never proof of an outcome");
    }

    [Fact]
    public async Task OrderRecovery_FillsWithoutPrice_StayUnresolved()
    {
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                c.RaiseOpenOrder(new IBOpenOrderUpdate(7001, "AAPL", "STK", "BUY", "LMT", 10m, 0m, 100d, null,
                    "Submitted", "client-1", "DU123456", null, null, null, DateTimeOffset.UtcNow));
                c.RaiseOrderStatus(new IBOrderStatusUpdate(7001, "Submitted", 3m, 7m, 0d, 0d, 1L, 1, null, null,
                    DateTimeOffset.UtcNow));
                c.RaiseOpenOrdersCompleted();
            }
        };
        await using var sut = CreateSut(client);
        await sut.ConnectAsync();

        (await ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1")).Should().BeNull();
    }

    [Fact]
    public async Task OrderRecovery_ConnectionChangesDuringLookup_RefusesEvidence()
    {
        IBBrokerageGateway? gateway = null;
        var client = new FakeIbBrokerageClient
        {
            OnRequestNextValidId = c => c.RaiseNextValidId(9001),
            OnRequestOpenOrders = c =>
            {
                c.RaiseOpenOrder(new IBOpenOrderUpdate(7001, "AAPL", "STK", "BUY", "LMT", 10m, 0m, 100d, null,
                    "Submitted", "client-1", "DU123456", null, null, null, DateTimeOffset.UtcNow));
                gateway!.DisconnectAsync().GetAwaiter().GetResult();
                gateway.ConnectAsync().GetAwaiter().GetResult();
                c.RaiseOpenOrdersCompleted();
            }
        };
        await using var sut = gateway = CreateSut(client);
        await sut.ConnectAsync();

        var recovery = () => ((IBrokerageOrderRecoveryGateway)sut).GetOrderForRecoveryAsync("client-1");

        await recovery.Should().ThrowAsync<InvalidOperationException>().WithMessage("*connection changed*");
    }
}
