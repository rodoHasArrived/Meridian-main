using FluentAssertions;
using Meridian.Execution.Adapters;
using Meridian.Execution.Sdk;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging;

namespace Meridian.Tests.Execution;

public sealed class BaseBrokerageGatewayLoggingTests
{
    [Fact]
    public async Task CancelOrderAsync_SanitizesOnlyTheLog_AndPreservesTheBrokerRequestAndReport()
    {
        const string orderId = "client-order\r\nFORGED";
        var logger = new RecordingLogger<RecordingGateway>();
        await using var gateway = new RecordingGateway(logger);
        await gateway.ConnectAsync();

        var report = await gateway.CancelOrderAsync(orderId);

        gateway.CancelledOrderId.Should().Be(orderId);
        report.OrderId.Should().Be(orderId);
        logger.Entries.Should().Contain(entry => entry.Message.Contains("client-order__FORGED", StringComparison.Ordinal));
        logger.Entries.Should().OnlyContain(entry => !entry.Message.Contains('\r') && !entry.Message.Contains('\n'));
    }

    private sealed class RecordingGateway(ILogger logger) : BaseBrokerageGateway(logger)
    {
        public string? CancelledOrderId { get; private set; }
        public override string GatewayId => "recording";
        public override string BrokerDisplayName => "Recording gateway";
        public override BrokerageCapabilities BrokerageCapabilities => BrokerageCapabilities.UsEquity();
        public override Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) => throw new NotSupportedException();
        protected override Task ConnectCoreAsync(CancellationToken ct) => Task.CompletedTask;
        protected override Task DisconnectCoreAsync(CancellationToken ct) => Task.CompletedTask;
        protected override Task<ExecutionReport> SubmitOrderCoreAsync(OrderRequest request, CancellationToken ct) => throw new NotSupportedException();
        protected override Task<ExecutionReport> CancelOrderCoreAsync(string orderId, CancellationToken ct)
        {
            CancelledOrderId = orderId;
            return Task.FromResult(new ExecutionReport
            {
                OrderId = orderId,
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                ReportType = ExecutionReportType.Cancelled,
                OrderStatus = OrderStatus.Cancelled
            });
        }
    }
}
