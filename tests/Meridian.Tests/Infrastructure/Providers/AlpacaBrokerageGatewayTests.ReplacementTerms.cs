using System.Net;
using FluentAssertions;
using Meridian.Execution.Sdk;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class AlpacaBrokerageGatewayTests
{
    [Theory]
    [InlineData("20", "149", ExecutionReportType.Modified)]            // the linked successor is this amendment
    [InlineData("25", "149", ExecutionReportType.ModificationPending)] // a different quantity is live
    [InlineData("20", "140", ExecutionReportType.ModificationPending)] // a different limit is live
    public async Task Amendment_LinkedSuccessor_ConfirmsOnlyTheRequestedTerms(
        string successorQty, string successorLimit, ExecutionReportType expected)
    {
        var replaced = false;
        using var handler = new Meridian.Tests.TestHelpers.StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v2/account")
                return new(HttpStatusCode.OK) { Content = BuildAccountResponse() };
            if (request.Method == HttpMethod.Patch)
            {
                replaced = true; // another replacement won the race
                return new((HttpStatusCode)422) { Content = new StringContent("{\"message\":\"order already replaced\"}") };
            }
            if (request.RequestUri.AbsolutePath == "/v2/orders/candidate")
                return new(HttpStatusCode.OK)
                {
                    Content = BuildOrderResponse("candidate", "new", clientOrderId: "candidate-client",
                        qty: successorQty, replaces: "original", limitPrice: successorLimit)
                };
            return new(HttpStatusCode.OK)
            {
                Content = BuildOrderResponse("original", replaced ? "replaced" : "accepted",
                    clientOrderId: "local", qty: "10", replacedBy: replaced ? "candidate" : null, limitPrice: "150")
            };
        });
        await using var gateway = CreateSut(handler);
        await gateway.ConnectAsync();

        var report = await gateway.ModifyOrderAsync(
            new OrderCancellationIdentifier("original", OrderCancellationIdentifierKind.BrokerOrderId),
            new OrderModification { NewQuantity = 20m, NewLimitPrice = 149m });

        report.ReportType.Should().Be(expected,
            "linkage proves a replacement exists, not that the live successor carries this amendment's terms");
    }
}
