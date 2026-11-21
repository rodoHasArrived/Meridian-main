using System.Net;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// A confirmed replacement leaves the addressed order in Alpaca's <c>replaced</c> state while the
/// successor it names keeps working. Observed against the paper Trading API: cancelling the
/// original client order id was refused with HTTP 422 and the replacement stayed on the book.
/// </summary>
public sealed partial class AlpacaBrokerageGatewayTests
{
    [Fact]
    public async Task CancelOrderAsync_ConfirmedReplacement_CancelsTheLiveSuccessor()
    {
        var routes = new List<(HttpMethod Method, string PathAndQuery)>();
        var responses = new Queue<HttpResponseMessage>(
        [
            new(HttpStatusCode.OK) { Content = BuildOrderResponse("original", "replaced", clientOrderId: "client-1", replacedBy: "successor") },
            new(HttpStatusCode.OK) { Content = BuildOrderResponse("successor", "new", clientOrderId: "broker-assigned", replaces: "original") },
            new(HttpStatusCode.NoContent),
            new(HttpStatusCode.OK) { Content = BuildOrderResponse("successor", "canceled", clientOrderId: "broker-assigned", replaces: "original") },
        ]);
        await using var sut = CreateSut(new SequentialStubHandler(
            responses, request => routes.Add((request.Method, request.RequestUri!.PathAndQuery))));
        MarkConnected(sut);

        var report = await sut.CancelOrderAsync(
            new OrderCancellationIdentifier("client-1", OrderCancellationIdentifierKind.ClientOrderId));

        report.ReportType.Should().Be(ExecutionReportType.Cancelled);
        report.OrderStatus.Should().Be(OrderStatus.Cancelled);
        report.GatewayOrderId.Should().Be("successor", "the cancel must reach the order that now holds the terms");
        report.ClientOrderId.Should().Be("client-1", "the report keeps the identity the caller addressed");
        routes.Should().Equal(
            (HttpMethod.Get, "/v2/orders:by_client_order_id?client_order_id=client-1"),
            (HttpMethod.Get, "/v2/orders/successor"),
            (HttpMethod.Delete, "/v2/orders/successor"),
            (HttpMethod.Get, "/v2/orders/successor"));
    }

    [Fact]
    public async Task CancelOrderAsync_ReplacementChain_IsFollowedToItsLiveEnd()
    {
        var routes = new List<(HttpMethod Method, string PathAndQuery)>();
        var responses = new Queue<HttpResponseMessage>(
        [
            new(HttpStatusCode.OK) { Content = BuildOrderResponse("first", "replaced", replacedBy: "second") },
            new(HttpStatusCode.OK) { Content = BuildOrderResponse("second", "replaced", replaces: "first", replacedBy: "third") },
            new(HttpStatusCode.OK) { Content = BuildOrderResponse("third", "accepted", replaces: "second") },
            new(HttpStatusCode.NoContent),
            new(HttpStatusCode.OK) { Content = BuildOrderResponse("third", "canceled", replaces: "second") },
        ]);
        await using var sut = CreateSut(new SequentialStubHandler(
            responses, request => routes.Add((request.Method, request.RequestUri!.PathAndQuery))));
        MarkConnected(sut);

        var report = await sut.CancelOrderAsync("first");

        report.OrderStatus.Should().Be(OrderStatus.Cancelled);
        report.GatewayOrderId.Should().Be("third");
        routes.Where(route => route.Method == HttpMethod.Delete).Should().Equal((HttpMethod.Delete, "/v2/orders/third"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, null)]
    [InlineData(HttpStatusCode.OK, "some-other-order")]
    public async Task CancelOrderAsync_ReplacementSuccessorUnverified_StaysNonTerminalWithoutCancellingAnything(
        HttpStatusCode successorStatus, string? successorReplaces)
    {
        var routes = new List<(HttpMethod Method, string PathAndQuery)>();
        var responses = new Queue<HttpResponseMessage>(
        [
            new(HttpStatusCode.OK) { Content = BuildOrderResponse("original", "replaced", replacedBy: "successor") },
            new(successorStatus) { Content = BuildOrderResponse("successor", "new", replaces: successorReplaces) },
        ]);
        await using var sut = CreateSut(new SequentialStubHandler(
            responses, request => routes.Add((request.Method, request.RequestUri!.PathAndQuery))));
        MarkConnected(sut);

        var report = await sut.CancelOrderAsync("original");

        // The successor may still be working, so the order must stay in future cancellation sweeps.
        report.ReportType.Should().Be(ExecutionReportType.Rejected);
        report.OrderStatus.Should().Be(OrderStatus.PendingCancel);
        report.RejectReason.Should().Contain("may still be working");
        routes.Should().NotContain(route => route.Method == HttpMethod.Delete,
            "an order that is not provably the replacement must not be cancelled on a guess");
    }

    [Fact]
    public async Task CancelOrderAsync_ReplacedOrderWithoutSuccessor_KeepsTheBrokerRefusal()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            new(HttpStatusCode.OK) { Content = BuildOrderResponse("original", "replaced") },
            new(HttpStatusCode.UnprocessableEntity) { Content = BuildJson(new { code = 42210000, message = "order is already in \"replaced\" state" }) },
        ]);
        await using var sut = CreateSut(new SequentialStubHandler(responses));
        MarkConnected(sut);

        var report = await sut.CancelOrderAsync("original");

        report.ReportType.Should().Be(ExecutionReportType.Rejected);
        report.RejectReason.Should().Be("Cancel request failed with HTTP 422.");
    }
}
