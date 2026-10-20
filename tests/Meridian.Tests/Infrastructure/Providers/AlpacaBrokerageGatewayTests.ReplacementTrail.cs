using System.Net;
using FluentAssertions;
using Meridian.Execution.Sdk;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class AlpacaBrokerageGatewayTests
{
    [Theory]
    [InlineData("1.5", "98.00", ExecutionReportType.Modified)]            // the linked successor trails by the requested amount
    [InlineData("1.75", "98.00", ExecutionReportType.ModificationPending)] // a different trail is live
    [InlineData(null, "98.00", ExecutionReportType.ModificationPending)]   // the snapshot does not say what it trails by
    [InlineData("1.5", "98.50", ExecutionReportType.Modified)]            // the live stop moved with the high-water mark
    public async Task TrailOnlyAmendment_LinkedSuccessor_ConfirmsOnlyTheRequestedTrail(
        string? successorTrail, string successorStop, ExecutionReportType expected)
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
                // A trailing stop's live stop is derived; only the trail is a requested term.
                return new(HttpStatusCode.OK)
                {
                    Content = successorTrail is null
                        ? TrailingStopResponse("candidate", "new", "candidate-client", stopPrice: successorStop, replaces: "original")
                        : TrailingStopResponse("candidate", "new", "candidate-client", stopPrice: successorStop, replaces: "original",
                            trailPrice: successorTrail)
                };
            return new(HttpStatusCode.OK)
            {
                Content = TrailingStopResponse("original", replaced ? "replaced" : "accepted", "local", stopPrice: "98.00",
                    replacedBy: replaced ? "candidate" : null, trailPrice: "2")
            };
        });
        await using var gateway = CreateSut(handler);
        await gateway.ConnectAsync();

        var report = await gateway.ModifyOrderAsync(
            new OrderCancellationIdentifier("original", OrderCancellationIdentifierKind.BrokerOrderId),
            new OrderModification { NewTrail = 1.5m });

        report.ReportType.Should().Be(expected,
            "a trail-only amendment is confirmed by the successor's trail, never by quantity and prices alone");
    }

    private static StringContent TrailingStopResponse(
        string id, string status, string clientOrderId, string stopPrice,
        string? replaces = null, string? replacedBy = null, string? trailPrice = null)
        => trailPrice is null
            ? BuildJson(new
            {
                id,
                client_order_id = clientOrderId,
                symbol = "AAPL",
                side = "sell",
                type = "trailing_stop",
                qty = "10",
                filled_qty = "0",
                replaces,
                replaced_by = replacedBy,
                limit_price = (string?)null,
                stop_price = stopPrice,
                status,
                created_at = "2024-01-15T10:00:00Z",
                updated_at = "2024-01-15T10:01:00Z"
            })
            : BuildJson(new
            {
                id,
                client_order_id = clientOrderId,
                symbol = "AAPL",
                side = "sell",
                type = "trailing_stop",
                qty = "10",
                filled_qty = "0",
                replaces,
                replaced_by = replacedBy,
                limit_price = (string?)null,
                stop_price = stopPrice,
                trail_price = trailPrice,
                status,
                created_at = "2024-01-15T10:00:00Z",
                updated_at = "2024-01-15T10:01:00Z"
            });
}
