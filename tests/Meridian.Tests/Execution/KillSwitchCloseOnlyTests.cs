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


    /// <summary>
    /// Two closes against one position. Each is a valid reduction on its own and together they
    /// cross through flat into a short, reopening risk behind the kill switch — so admission
    /// compares the settled position against committed reduction, not against this order alone.
    /// </summary>
    [Fact]
    public async Task SecondClose_ThatWouldOvershootWithTheFirst_IsRefused()
    {
        var (controls, overrideId) = await HaltedDeskWithOverrideAsync(alreadyWorking: 6m);

        controls.EvaluateOrder(Order(OrderSide.Sell, 4m, overrideId), Portfolio(("AAPL", 10m)))
            .IsApproved.Should().BeTrue("6 working plus 4 exactly flattens the 10-share long");
        controls.EvaluateOrder(Order(OrderSide.Sell, 5m, overrideId), Portfolio(("AAPL", 10m)))
            .IsApproved.Should().BeFalse("6 working plus 5 would leave the account short 1");
    }

    /// <summary>
    /// A gate with no way to see working reductions cannot establish that an order is one, and
    /// under an open breaker that is not a reason to route it.
    /// </summary>
    [Fact]
    public async Task WithoutAWorkingReductionProbe_NothingIsAdmitted()
    {
        var controls = NewControls();
        controls.WorkingReductionQuantityProbe = null;
        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.BypassOrderControls,
            Reason: "Operator approved emergency close",
            CreatedBy: "ops",
            Symbol: "AAPL"));
        await controls.SetCircuitBreakerAsync(isOpen: true, reason: "Operator halt", changedBy: "ops");

        controls.EvaluateOrder(Order(OrderSide.Sell, 1m, manualOverride.OverrideId), Portfolio(("AAPL", 10m)))
            .IsApproved.Should().BeFalse();
    }

    /// <summary>
    /// An explicit opening intent settles the question whatever the arithmetic says: SellToOpen
    /// against a long is a new short, not a reduction.
    /// </summary>
    [Theory]
    [InlineData(PositionIntent.SellToOpen)]
    [InlineData(PositionIntent.BuyToOpen)]
    public async Task ExplicitOpeningIntent_IsRefused(PositionIntent intent)
    {
        var (controls, overrideId) = await HaltedDeskWithOverrideAsync();

        var order = Order(OrderSide.Sell, 1m, overrideId) with { PositionIntent = intent };

        controls.EvaluateOrder(order, Portfolio(("AAPL", 10m))).IsApproved.Should().BeFalse();
    }

    /// <summary>
    /// A package's parent fields are not what routes: the gateway replaces the parent symbol with
    /// the legs, so a close-looking parent can carry legs that open fresh exposure.
    /// </summary>
    [Fact]
    public async Task MultiLegOrder_IsRefused_BecauseItsLegsAreWhatRoute()
    {
        var (controls, overrideId) = await HaltedDeskWithOverrideAsync();

        var order = Order(OrderSide.Sell, 1m, overrideId) with
        {
            Legs = [new OrderLeg { Symbol = "AAPL_C1", Side = OrderSide.Buy, RatioQuantity = 1m }]
        };

        controls.EvaluateOrder(order, Portfolio(("AAPL", 10m))).IsApproved.Should().BeFalse();
    }

    /// <summary>
    /// A broker-native notional order routes dollars and the gateway discards Quantity, so a small
    /// placeholder quantity would pass while the routed amount crossed through flat.
    /// </summary>
    [Fact]
    public async Task BrokerNotionalOrder_IsRefused_BecauseQuantityIsNotAShareCount()
    {
        var (controls, overrideId) = await HaltedDeskWithOverrideAsync();

        var order = Order(OrderSide.Sell, 1m, overrideId) with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["manualOverrideId"] = overrideId,
                ["notional"] = "100000"
            }
        };

        controls.EvaluateOrder(order, Portfolio(("AAPL", 10m))).IsApproved.Should().BeFalse();
    }

    /// <summary>
    /// A shared book nets several funds onto one symbol. Measuring a fund-scoped close against that
    /// aggregate lets one fund sell against another's long and acquire a new short.
    /// </summary>
    [Fact]
    public async Task FundScopedClose_MeasuresTheRequestingFundsShare_NotTheNettedAggregate()
    {
        var (controls, overrideId) = await HaltedDeskWithOverrideAsync();
        var fundA = Guid.NewGuid();
        var fundB = Guid.NewGuid();

        // Aggregate 10 long, all of it fund A's.
        var portfolio = new StubPortfolioState(
            [("AAPL", 10m)],
            owners: new Dictionary<string, decimal> { [fundA.ToString("D")] = 10m });

        controls.EvaluateOrder(Order(OrderSide.Sell, 10m, overrideId) with { FundAccountId = fundA }, portfolio)
            .IsApproved.Should().BeTrue("fund A is closing its own long");
        controls.EvaluateOrder(Order(OrderSide.Sell, 10m, overrideId) with { FundAccountId = fundB }, portfolio)
            .IsApproved.Should().BeFalse("fund B holds nothing here, so this sell opens a short");
    }


    /// <summary>
    /// A fund can hold the opposite sign to the netted book. With fund A long 100 and fund B short
    /// 10 the aggregate is long 90, so deriving the reducing side from it looks for B's reductions
    /// among sells while B reduces by buying — leaving B's working buy-to-close uncounted and
    /// admitting a second buy that crosses B through flat into a long.
    /// </summary>
    [Fact]
    public async Task FundHoldingTheOppositeSignToTheBook_HasItsOwnReductionsCounted()
    {
        var fundShort = Guid.NewGuid();
        var (controls, overrideId) = await HaltedDeskWithOverrideAsync();

        // Aggregate long 90; this fund is short 10 and already has 6 working buys against it.
        controls.WorkingReductionQuantityProbe = (_, fund) => fund == fundShort ? 6m : 0m;

        var portfolio = new StubPortfolioState(
            [("AAPL", 90m)],
            owners: new Dictionary<string, decimal> { [fundShort.ToString("D")] = -10m });

        controls.EvaluateOrder(Order(OrderSide.Buy, 4m, overrideId) with { FundAccountId = fundShort }, portfolio)
            .IsApproved.Should().BeTrue("6 working plus 4 exactly closes the 10-share short");
        controls.EvaluateOrder(Order(OrderSide.Buy, 5m, overrideId) with { FundAccountId = fundShort }, portfolio)
            .IsApproved.Should().BeFalse("6 working plus 5 would leave this fund long 1");
    }

    private async Task<(ExecutionOperatorControlService Controls, string OverrideId)> HaltedDeskWithOverrideAsync(
        decimal alreadyWorking = 0m)
    {
        var controls = NewControls(alreadyWorking);
        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.BypassOrderControls,
            Reason: "Operator approved emergency close",
            CreatedBy: "ops",
            Symbol: "AAPL"));

        await controls.SetCircuitBreakerAsync(isOpen: true, reason: "Operator halt", changedBy: "ops");
        return (controls, manualOverride.OverrideId);
    }

    /// <summary>
    /// Every close-only case needs a working-reduction probe, because an unset one fails closed:
    /// a gate that cannot see committed reductions cannot establish that this order is one.
    /// </summary>
    private ExecutionOperatorControlService NewControls(decimal alreadyWorking = 0m)
    {
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(_root, Guid.NewGuid().ToString("N"))),
            NullLogger<ExecutionOperatorControlService>.Instance);
        controls.WorkingReductionQuantityProbe = (_, _) => alreadyWorking;
        return controls;
    }

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
        public StubPortfolioState(
            (string Symbol, decimal Quantity)[] positions,
            IReadOnlyDictionary<string, decimal>? owners = null)
            => Positions = positions.ToDictionary(
                static entry => entry.Symbol,
                entry => (IPosition)new StubPosition(entry.Symbol, entry.Quantity, owners ?? new Dictionary<string, decimal>()),
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
    private sealed record StubPosition(
        string Symbol,
        decimal Held,
        IReadOnlyDictionary<string, decimal> Owners) : IPosition
    {
        public IReadOnlyDictionary<string, decimal> OwnerQuantities => Owners;

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
