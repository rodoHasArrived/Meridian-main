using System.Net;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Tests.TestHelpers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class ExecutionWriteEndpointsTests
{
    [Theory]
    [InlineData(UiApiRoutes.ExecutionPositionActionClose, 0)]
    [InlineData(UiApiRoutes.ExecutionPositionActionClose, -1)]
    [InlineData(UiApiRoutes.ExecutionPositionActionClose, 1)]
    [InlineData(UiApiRoutes.ExecutionPositionActionUpsize, 0)]
    [InlineData(UiApiRoutes.ExecutionPositionActionUpsize, -1)]
    [InlineData(UiApiRoutes.ExecutionPositionActionUpsize, 1)]
    public async Task PositionAction_RequiresAuthenticatedActorBeforeQuantityValidation(string path, int quantity)
    {
        var manager = Substitute.For<IOrderManager>();
        await using var app = await CreateAppAsync(services =>
        {
            RegisterMinimalOms(services, new ExecutionPosition("AAPL", 5, 180m, 10m, 0m));
            services.AddSingleton(manager);
        }, currentUser: null);

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Meridian-Actor", "untrusted-caller");
        var response = await client.PostAsync(path, JsonContent(new ExecutionPositionActionRequest("AAPL", Quantity: quantity)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await manager.DidNotReceive().PlaceOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(UiApiRoutes.ExecutionPositionActionClose, 0)]
    [InlineData(UiApiRoutes.ExecutionPositionActionClose, -1)]
    [InlineData(UiApiRoutes.ExecutionPositionActionUpsize, 0)]
    [InlineData(UiApiRoutes.ExecutionPositionActionUpsize, -1)]
    public async Task PositionAction_AuthenticatedNonpositiveQuantityRemainsRejectedWithoutDispatch(string path, int quantity)
    {
        var manager = Substitute.For<IOrderManager>();
        await using var app = await CreateAppAsync(services =>
        {
            RegisterMinimalOms(services, new ExecutionPosition("AAPL", 5, 180m, 10m, 0m));
            services.AddSingleton(manager);
        });

        var response = await app.GetTestClient().PostAsync(path,
            JsonContent(new ExecutionPositionActionRequest("AAPL", Quantity: quantity)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await ReadActionResultAsync(response);
        result.Status.Should().Be("Rejected");
        result.Message.Should().Contain("positive quantity");
        await manager.DidNotReceive().PlaceOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelOrder_LogsOnlyGeneratedCorrelationWhilePreservingOperatorResult(bool success)
    {
        const string orderId = "private-order-marker";
        const string rejection = "private-broker-marker\r\nFORGED operational event\u0085NEXT\u2028LINE\u2029END";
        var manager = Substitute.For<IOrderManager>();
        manager.CancelOrderAsync(orderId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new OrderResult
        {
            Success = success,
            OrderId = orderId,
            ErrorMessage = rejection
        }));
        var logger = new RecordingLogger<ExecutionWriteEndpointsTests>();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(manager);
            services.AddLogging(logging => logging.AddProvider(new EndpointActionLoggerProvider(logger)));
        });

        var response = await app.GetTestClient().PostAsync($"/api/execution/orders/{orderId}/cancel", null);

        response.StatusCode.Should().Be(success ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        var result = await ReadActionResultAsync(response);
        result.Message.Should().Be(success ? $"Order {orderId} cancelled." : rejection);
        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.LogLevel.Should().Be(success ? LogLevel.Information : LogLevel.Warning);
        entry.Message.Should().Be($"Trading action {result.ActionId}: cancel order — {(success ? "succeeded" : "rejected")}");
        entry.Message.Should().NotContain(orderId).And.NotContain("private-broker-marker").And.NotContain("FORGED");
        entry.Message.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']).Should().Be(-1);
        entry.Exception.Should().BeNull();
        await manager.Received(1).CancelOrderAsync(orderId, Arg.Any<CancellationToken>());
    }

    private sealed class EndpointActionLoggerProvider(ILogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
            => categoryName == "Meridian.Ui.Shared.Endpoints.ExecutionEndpoints" ? logger : NullLogger.Instance;

        public void Dispose() { }
    }
}
