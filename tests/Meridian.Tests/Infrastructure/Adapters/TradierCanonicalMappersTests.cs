using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Tradier;

namespace Meridian.Tests.Infrastructure.Adapters;

public sealed class TradierCanonicalMappersTests
{
    [Fact]
    public void NormalizeEquityPayload_NormalizesSlashSymbolAndExchange()
    {
        var payload = new TradierEquityPayload("brk/b", "nyse", 501.23m, 501.20m, 501.25m, DateTimeOffset.Parse("2026-05-19T12:00:00Z"));

        var result = TradierCanonicalMappers.NormalizeEquityPayload(payload);

        result.Symbol.Should().Be("BRK.B");
        result.Exchange.Should().Be("NYSE");
        result.SourceSymbol.Should().Be("brk/b");
    }

    [Fact]
    public void NormalizeOptionPayload_BuildsOccCanonicalContract()
    {
        var payload = new TradierOptionPayload("spy", "legacy", new DateOnly(2026, 12, 18), 650m, true, 2.1m, 2.2m, 2.15m, 1234, 4567);

        var result = TradierCanonicalMappers.NormalizeOptionPayload(payload);

        result.UnderlyingSymbol.Should().Be("SPY");
        result.ContractSymbol.Should().Be("SPY261218C00650000");
        result.SourceContractSymbol.Should().Be("legacy");
    }

    [Theory]
    [InlineData("partially_filled", 100, 40, OrderStatus.PartiallyFilled, ExecutionReportType.PartialFill)]
    [InlineData("canceled", 100, 40, OrderStatus.Cancelled, ExecutionReportType.Cancelled)]
    [InlineData("rejected", 100, 0, OrderStatus.Rejected, ExecutionReportType.Rejected)]
    [InlineData("open", 100, 40, OrderStatus.PartiallyFilled, ExecutionReportType.PartialFill)]
    public void MapOrderTransition_MapsStatusTransitions(
        string status,
        decimal quantity,
        decimal filled,
        OrderStatus expectedStatus,
        ExecutionReportType expectedType)
    {
        var transition = TradierCanonicalMappers.MapOrderTransition(new TradierOrderStatusPayload(status, quantity, filled));

        transition.Status.Should().Be(expectedStatus);
        transition.ReportType.Should().Be(expectedType);
    }

    [Fact]
    public void MapBrokerError_MapsRateLimitAsRetriableOperationalError()
    {
        var payload = new TradierBrokerErrorPayload(429, "rate_limit", "Too many requests", "{...}");

        var error = TradierCanonicalMappers.MapBrokerError(payload);

        error.Category.Should().Be("RateLimit");
        error.IsRetriable.Should().BeTrue();
        error.BrokerCode.Should().Be("rate_limit");
        error.Message.Should().Be("Too many requests");
    }
}
