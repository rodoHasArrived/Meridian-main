using FluentAssertions;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Covers the close-only exception a bypass override carries while the kill switch is open.
/// <para>
/// The kill switch has two halves: emptying the book, and stopping it refilling. A
/// <c>BypassOrderControls</c> override used to admit any order while the breaker was open, so the
/// second half did not hold — an approved override could route fresh risk behind the sweep that had
/// just cancelled everything. Revoking the override outright would have broken the first half a
/// different way, trapping the desk in the very positions the halt was raised over, so the
/// exception is narrowed to what the override is actually for rather than removed.
/// </para>
/// </summary>
public sealed class KillSwitchCloseOnlyTests : IDisposable
{
    private const string CloseOnlyCode = "CIRCUIT_BREAKER_CLOSE_ONLY";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-killswitch-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ClosingOrder_IsAdmittedSoTheDeskCanFlattenAHaltedBook()
    {
        var (controls, overrideId) = await HaltedDeskWithOverrideAsync();

        var decision = controls.EvaluateOrder(
            Order(OrderSide.Sell, quantity: 10m, overrideId),
            Portfolio(("AAPL", 10m)));

        decision.IsApproved.Should().BeTrue("flattening is what the override exists for");
    }

    [Fact]
    public async Task OpeningOrder_IsRefusedEvenWithAnOverride()
    {
        var (controls, overrideId) = await HaltedDeskWithOverrideAsync();

        var decision = controls.EvaluateOrder(
            Order(OrderSide.Buy, quantity: 10m, overrideId),
            Portfolio(("AAPL", 10m)));

        decision.IsApproved.Should().BeFalse("the kill switch must block new submissions");
        decision.RejectCode.Should().Be(CloseOnlyCode);
    }

    /// <summary>
    /// Selling more than is held closes the long and opens a short. That is new risk wearing a
    /// reduction's clothes, and the operator can send the exact position size instead.
    /// </summary>
    [Fact]
    public async Task OrderLargerThanThePosition_IsRefusedRatherThanPartlyAdmitted()
    {
        var (controls, overrideId) = await HaltedDeskWithOverrideAsync();

        var decision = controls.EvaluateOrder(
            Order(OrderSide.Sell, quantity: 15m, overrideId),
            Portfolio(("AAPL", 10m)));

        decision.IsApproved.Should().BeFalse();
        decision.RejectCode.Should().Be(CloseOnlyCode);
    }

    /// <summary>The mirror case, so the rule is about direction rather than about selling.</summary>
    [Fact]
    public async Task BuyingBackAShort_IsAdmitted_AndSellingDeeperIsNot()
    {
        var (controls, overrideId) = await HaltedDeskWithOverrideAsync();

        controls.EvaluateOrder(Order(OrderSide.Buy, 10m, overrideId), Portfolio(("AAPL", -10m)))
            .IsApproved.Should().BeTrue();
        controls.EvaluateOrder(Order(OrderSide.Sell, 1m, overrideId), Portfolio(("AAPL", -10m)))
            .IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task FlatSymbol_AdmitsNothing_BecauseThereIsNoExposureToReduce()
    {
        var (controls, overrideId) = await HaltedDeskWithOverrideAsync();

        controls.EvaluateOrder(Order(OrderSide.Sell, 1m, overrideId), Portfolio(("MSFT", 10m)))
            .IsApproved.Should().BeFalse("a sell in a symbol the desk does not hold opens a short");
    }

    /// <summary>
    /// Being unable to establish that an order reduces risk is not a reason to route it under an
    /// open breaker. This is the fail-closed leg of the exception.
    /// </summary>
    [Fact]
    public async Task WithoutPortfolioState_NothingIsAdmitted()
    {
        var (controls, overrideId) = await HaltedDeskWithOverrideAsync();

        controls.EvaluateOrder(Order(OrderSide.Sell, 1m, overrideId), portfolioState: null)
            .IsApproved.Should().BeFalse();
    }

    /// <summary>
    /// The exception is scoped to the breaker. With the desk running normally an override keeps
    /// its full meaning, so narrowing it must not leak into ordinary operation.
    /// </summary>
    [Fact]
    public async Task WithTheBreakerClosed_AnOverrideStillAdmitsAnOpeningOrder()
    {
        var controls = NewControls();
        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.BypassOrderControls,
            Reason: "Ordinary operation",
            CreatedBy: "ops",
            Symbol: "AAPL"));

        controls.EvaluateOrder(Order(OrderSide.Buy, 10m, manualOverride.OverrideId), Portfolio(("AAPL", 10m)))
            .IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutAnOverride_TheBreakerStillRefusesEvenAClosingOrder()
    {
        var controls = NewControls();
        await controls.SetCircuitBreakerAsync(isOpen: true, reason: "Operator halt", changedBy: "ops");

        var decision = controls.EvaluateOrder(Order(OrderSide.Sell, 10m, manualOverrideId: null), Portfolio(("AAPL", 10m)));

        decision.IsApproved.Should().BeFalse();
        decision.RejectCode.Should().Be("CIRCUIT_BREAKER_OPEN", "no override means the plain halt applies");
    }

    private async Task<(ExecutionOperatorControlService Controls, string OverrideId)> HaltedDeskWithOverrideAsync()
    {
        var controls = NewControls();
        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.BypassOrderControls,
            Reason: "Operator approved emergency close",
            CreatedBy: "ops",
            Symbol: "AAPL"));

        await controls.SetCircuitBreakerAsync(isOpen: true, reason: "Operator halt", changedBy: "ops");
        return (controls, manualOverride.OverrideId);
    }

    private ExecutionOperatorControlService NewControls() => new(
        new ExecutionOperatorControlOptions(Path.Combine(_root, Guid.NewGuid().ToString("N"))),
        NullLogger<ExecutionOperatorControlService>.Instance);

    private static OrderRequest Order(OrderSide side, decimal quantity, string? manualOverrideId) => new()
    {
        Symbol = "AAPL",
        Side = side,
        Type = OrderType.Market,
        Quantity = quantity,
        Metadata = manualOverrideId is null
            ? null
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["manualOverrideId"] = manualOverrideId
            }
    };

    private static StubPortfolioState Portfolio(params (string Symbol, decimal Quantity)[] positions) =>
        new(positions);

    private sealed class StubPortfolioState : IPortfolioState
    {
        public StubPortfolioState((string Symbol, decimal Quantity)[] positions)
            => Positions = positions.ToDictionary(
                static entry => entry.Symbol,
                static entry => (IPosition)new StubPosition(entry.Symbol, entry.Quantity),
                StringComparer.OrdinalIgnoreCase);

        public decimal Cash => 100_000m;

        public decimal PortfolioValue => 100_000m;

        public decimal UnrealisedPnl => 0m;

        public decimal RealisedPnl => 0m;

        public IReadOnlyDictionary<string, IPosition> Positions { get; }
    }

    /// <summary>
    /// Holds the unrounded quantity explicitly. <see cref="IPosition.ExactQuantity"/> is a default
    /// interface member derived from the rounded <see cref="IPosition.Quantity"/>, and the gate
    /// reads the exact one — so a double that only set the rounded value could not express the
    /// fractional holdings the close-only rule has to get right.
    /// </summary>
    private sealed record StubPosition(string Symbol, decimal Held) : IPosition
    {
        public long Quantity => (long)Held;

        public decimal ExactQuantity => Held;

        public decimal AverageCostBasis => 100m;

        public decimal UnrealizedPnl => 0m;

        public decimal RealizedPnl => 0m;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must not fail the test run.
        }
    }
}
