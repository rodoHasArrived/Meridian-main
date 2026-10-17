using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed partial class AlpacaBrokerageGatewayTests
{
    [Fact]
    public async Task SubmitOrder_ImmediateFill_RetainsQuantityAndPrice()
    {
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, () =>
            BuildOrderResponse(status: "filled", clientOrderId: "accepted-id", qty: "10", filledQty: "10", filledAvgPrice: "101.25")));
        MarkConnected(sut);
        var report = await sut.SubmitOrderAsync(new OrderRequest
        {
            ClientOrderId = "accepted-id",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            LimitPrice = 102m,
            Quantity = 10m
        });
        report.ReportType.Should().Be(ExecutionReportType.Fill);
        report.FilledQuantity.Should().Be(10m);
        report.FillPrice.Should().Be(101.25m);
    }

    [Fact]
    public async Task SubmitOrder_WrongAcknowledgedClientIdentity_RequiresRecovery()
    {
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, () => BuildOrderResponse(clientOrderId: "different-id")));
        MarkConnected(sut);
        var act = () => sut.SubmitOrderAsync(new OrderRequest
        {
            ClientOrderId = "requested-id",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            LimitPrice = 100m,
            Quantity = 1m
        });
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("active")]
    [InlineData("ACTIVE")]
    public async Task AccountHealth_AcceptsAlpacaStatusCasing(string status)
    {
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, () => BuildAccountResponse(status)));
        await sut.ConnectAsync();
        (await sut.CheckHealthAsync()).IsHealthy.Should().BeTrue();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[{\"symbol\":\"AAPL\",\"qty\":\"bad\",\"asset_class\":\"us_equity\"}]")]
    public async Task PortfolioSnapshot_NullOrMalformedHoldings_AreExplicitlyIncomplete(string positionsJson)
    {
        var sut = CreateSut(new CapturingStubHandler(_ => { }, request =>
            request.RequestUri!.AbsolutePath == "/v2/account"
                ? BuildAccountResponse()
                : new StringContent(positionsJson, Encoding.UTF8, "application/json")));
        var snapshot = await sut.GetPortfolioSnapshotAsync("TEST123");
        snapshot.IsComplete.Should().BeFalse();
        snapshot.CompletenessIssues.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PortfolioSnapshot_MissingBalances_CannotCertifyZeroCashOrBuyingPower()
    {
        var sut = CreateSut(new CapturingStubHandler(_ => { }, request =>
            request.RequestUri!.AbsolutePath == "/v2/account"
                ? BuildJson(new { id = "account-id", currency = "USD", status = "active" })
                : BuildJson(Array.Empty<object>())));
        var snapshot = await sut.GetPortfolioSnapshotAsync("account-id");
        snapshot.IsComplete.Should().BeFalse();
        snapshot.CompletenessIssues.Should().Contain(issue => issue.Contains("buying power"));
        snapshot.CompletenessIssues.Should().Contain(issue => issue.Contains("cash"));
    }

    [Fact]
    public async Task PortfolioSnapshot_CompleteFlatAccount_IsDistinctFromMissingPositions()
    {
        var sut = CreateSut(new CapturingStubHandler(_ => { }, request =>
            request.RequestUri!.AbsolutePath == "/v2/account" ? BuildAccountResponse() : BuildJson(Array.Empty<object>())));
        var snapshot = await sut.GetPortfolioSnapshotAsync("TEST123");
        snapshot.IsComplete.Should().BeTrue();
        snapshot.Positions.Should().BeEmpty();
    }

    [Fact]
    public async Task RecoveryLookup_ReturnsTerminalCumulativeFillWithoutSubmission()
    {
        var methods = new List<HttpMethod>();
        var paths = new List<string>();
        var sut = CreateSut(new CapturingStubHandler(request =>
        {
            methods.Add(request.Method);
            paths.Add(request.RequestUri!.PathAndQuery);
        }, request => request.RequestUri!.AbsolutePath == "/v2/account"
            ? BuildAccountResponse()
            : BuildOrderResponse(status: "filled", qty: "10", filledQty: "10", filledAvgPrice: "101.25")));
        await sut.ConnectAsync();
        var report = await sut.GetOrderForRecoveryAsync("client-1");
        report!.OrderStatus.Should().Be(OrderStatus.Filled);
        report.FilledQuantity.Should().Be(10m);
        report.FillPrice.Should().Be(101.25m);
        report.ClientOrderId.Should().Be("client-1");
        paths.Should().Contain("/v2/orders:by_client_order_id?client_order_id=client-1");
        methods.Should().OnlyContain(method => method == HttpMethod.Get);
        ((IBrokerageConnectionState)sut).ScopeIdentity.Should().EndWith(":TEST123");
        var generation = ((IBrokerageConnectionState)sut).ConnectionGeneration;
        await sut.DisconnectAsync();
        ((IBrokerageConnectionState)sut).ScopeIdentity.Should().BeNull();
        ((IBrokerageConnectionState)sut).ConnectionGeneration.Should().BeGreaterThan(generation);
    }

    [Theory]
    [InlineData("other-client", "10", "10", "100")]
    [InlineData("client-1", "10", "11", "100")]
    [InlineData("client-1", "10", "10", null)]
    public async Task RecoveryLookup_RejectsWrongIdentityOrIncompleteFillEconomics(
        string clientOrderId, string quantity, string filled, string? price)
    {
        var sut = CreateSut(new CapturingStubHandler(_ => { }, request =>
            request.RequestUri!.AbsolutePath == "/v2/account" ? BuildAccountResponse()
                : BuildOrderResponse(status: "filled", clientOrderId: clientOrderId,
                    qty: quantity, filledQty: filled, filledAvgPrice: price)));
        await sut.ConnectAsync();
        var act = () => sut.GetOrderForRecoveryAsync("client-1");
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(503)]
    [InlineData(408)]
    [InlineData(409)]
    [InlineData(422)]
    public async Task SubmitOrder_AmbiguousHttpResponse_ThrowsInsteadOfReleasingExposure(int statusCode)
    {
        var sut = CreateSut(new ConstantStubHandler((HttpStatusCode)statusCode, BuildJson(new { message = "uncertain" })));
        MarkConnected(sut);
        var act = () => sut.SubmitOrderAsync(new OrderRequest
        {
            ClientOrderId = "retained-order",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            LimitPrice = 100m,
            Quantity = 1m
        });
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SubmitOrder_EmptySuccessResponse_RequiresRecovery()
    {
        var sut = CreateSut(new ConstantStubHandler(HttpStatusCode.OK, BuildJson(new { })));
        MarkConnected(sut);
        var act = () => sut.SubmitOrderAsync(new OrderRequest
        {
            ClientOrderId = "retained-order",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            LimitPrice = 100m,
            Quantity = 1m
        });
        await act.Should().ThrowAsync<InvalidDataException>();
    }

}
