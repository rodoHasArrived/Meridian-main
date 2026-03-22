using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Risk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Risk;

public sealed class CompositeRiskValidatorTests
{
    [Fact]
    public async Task ValidateOrderAsync_WithRejectedRule_ReturnsRejectedResult()
    {
        var validator = new CompositeRiskValidator(
            new IRiskRule[]
            {
                new StubRiskRule("first", RiskValidationResult.Approved()),
                new StubRiskRule("second", RiskValidationResult.Rejected("blocked")),
            },
            NullLogger<CompositeRiskValidator>.Instance);

        var result = await validator.ValidateOrderAsync(CreateOrder());

        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Be("blocked");
    }

    private static OrderRequest CreateOrder() => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 10m,
    };

    private sealed class StubRiskRule(string ruleName, RiskValidationResult result) : IRiskRule
    {
        public string RuleName => ruleName;

        public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(result);
    }
}
