using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Risk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Risk;

/// <summary>
/// Contract tests for <see cref="KillSwitchSweepTripHandler"/>: an automated breaker trip must
/// run the same kill-switch cancel-all the operator endpoint runs, and must leave the same
/// <c>controls/CircuitBreakerCancelAll</c> audit evidence — outcome-based, never
/// invocation-based — while a sweep fault stays contained in the handler's own reporting.
/// </summary>
public sealed class KillSwitchSweepTripHandlerTests
{
    [Fact]
    public async Task OnCircuitBreakerTripped_SweepsOnceAndAuditsTheOutcome()
    {
        var orderManager = new StubOrderManager
        {
            SweepResult = KillSwitchSweepResult.From(2, 2, []),
        };
        await using var auditTrail = CreateAuditTrail();
        var handler = new KillSwitchSweepTripHandler(
            () => orderManager,
            NullLogger<KillSwitchSweepTripHandler>.Instance,
            auditTrail);

        await handler.OnCircuitBreakerTrippedAsync(
            "Tripped by critical risk rule 'gross-exposure': book over ceiling",
            "risk-engine/gross-exposure",
            CancellationToken.None);

        orderManager.CancelAllCalls.Should().Be(1, "the trip reuses the OMS kill-switch sweep, not a second sweep");

        var entries = await auditTrail.GetRecentAsync(10);
        var entry = entries.Should().ContainSingle().Subject;
        entry.Category.Should().Be("controls");
        entry.Action.Should().Be("CircuitBreakerCancelAll", "the automated trip records the same evidence surface as the operator endpoint");
        entry.Outcome.Should().Be(nameof(KillSwitchSweepOutcome.Completed));
        entry.Actor.Should().Be("risk-engine/gross-exposure");
        entry.Message.Should().Contain("cancelled 2 of 2");
    }

    [Fact]
    public async Task OnCircuitBreakerTripped_SurvivingOrders_AreAuditedAsTheOutcomeNotAsSuccess()
    {
        var orderManager = new StubOrderManager
        {
            SweepResult = KillSwitchSweepResult.From(
                2,
                1,
                [new KillSwitchSweepFailure("MDN-1", "AAPL", "Broker refused the cancellation.")]),
        };
        await using var auditTrail = CreateAuditTrail();
        var handler = new KillSwitchSweepTripHandler(
            () => orderManager,
            NullLogger<KillSwitchSweepTripHandler>.Instance,
            auditTrail);

        await handler.OnCircuitBreakerTrippedAsync("halt", "risk-engine/gross-exposure", CancellationToken.None);

        var entries = await auditTrail.GetRecentAsync(10);
        var entry = entries.Should().ContainSingle().Subject;
        entry.Outcome.Should().Be(
            nameof(KillSwitchSweepOutcome.Partial),
            "a broker refusing a cancellation must never be audited as a completed kill switch");
        entry.Message.Should().Contain("MDN-1", "the operator acting on the audit needs the surviving order named");
    }

    [Fact]
    public async Task OnCircuitBreakerTripped_WhenSweepThrows_DoesNotThrowAndAuditsFailure()
    {
        var orderManager = new StubOrderManager
        {
            SweepFailure = new InvalidOperationException("broker unreachable"),
        };
        await using var auditTrail = CreateAuditTrail();
        var handler = new KillSwitchSweepTripHandler(
            () => orderManager,
            NullLogger<KillSwitchSweepTripHandler>.Instance,
            auditTrail);

        var act = () => handler.OnCircuitBreakerTrippedAsync("halt", "risk-engine/gross-exposure", CancellationToken.None);

        await act.Should().NotThrowAsync("a failed sweep is reported, never thrown back toward the risk path");
        var entries = await auditTrail.GetRecentAsync(10);
        var entry = entries.Should().ContainSingle().Subject;
        entry.Outcome.Should().Be("Failed");
        entry.Reason.Should().Contain("broker unreachable");
        entry.Message.Should().Contain("manual cancellation is required");
    }

    [Fact]
    public async Task OnCircuitBreakerTripped_WithoutAnOrderManager_ReturnsWithoutSweepingOrAuditing()
    {
        await using var auditTrail = CreateAuditTrail();
        var handler = new KillSwitchSweepTripHandler(
            () => null,
            NullLogger<KillSwitchSweepTripHandler>.Instance,
            auditTrail);

        var act = () => handler.OnCircuitBreakerTrippedAsync("halt", "risk-engine/gross-exposure", CancellationToken.None);

        await act.Should().NotThrowAsync("a host without an OMS has no book to sweep");
        (await auditTrail.GetRecentAsync(10)).Should().BeEmpty();
    }

    private static ExecutionAuditTrailService CreateAuditTrail() => new(
        new ExecutionAuditTrailOptions(
            Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"trip-audit-{Guid.NewGuid():N}", "audit")),
        NullLogger<ExecutionAuditTrailService>.Instance);

    private sealed class StubOrderManager : IOrderManager
    {
        public int CancelAllCalls { get; private set; }

        public KillSwitchSweepResult SweepResult { get; init; } = KillSwitchSweepResult.Empty;

        public Exception? SweepFailure { get; init; }

        public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(new OrderResult { Success = true, OrderId = request.ClientOrderId ?? "stub-order" });

        public Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            Task.FromResult(new OrderResult { Success = true, OrderId = orderId });

        public Task<OrderResult> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) =>
            Task.FromResult(new OrderResult { Success = true, OrderId = orderId });

        public IReadOnlyList<OrderState> GetOpenOrders() => [];

        public OrderState? GetOrder(string orderId) => null;

        public Task<KillSwitchSweepResult> CancelAllAsync(CancellationToken ct = default)
        {
            CancelAllCalls++;
            return SweepFailure is null
                ? Task.FromResult(SweepResult)
                : Task.FromException<KillSwitchSweepResult>(SweepFailure);
        }

        public IReadOnlyList<OrderState> GetCompletedOrders(int take = 20) => [];
    }
}
