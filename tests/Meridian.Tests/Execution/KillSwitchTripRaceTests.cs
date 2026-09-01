using System.Runtime.CompilerServices;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Covers the race between a circuit-breaker trip and a submission already past the
/// operator-control gate. <c>PlaceOrderAsync</c> consults the controls, then validates, reserves,
/// and dispatches; a trip in that window used to run its cancel-all sweep against a book the
/// order was not yet in, and the order then reached the broker after the sweep had reported an
/// empty book. Two things close it: the controls are consulted again at the point of dispatch,
/// and the sweep waits for dispatches already in flight before it looks at the book.
/// </summary>
public sealed class KillSwitchTripRaceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-killswitch-race-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PlaceOrderAsync_WhenTheBreakerTripsBetweenTheGateAndDispatch_NeverReachesTheGateway()
    {
        var controls = CreateControls();
        await using var gateway = new RaceBrokerageGateway();
        // The validator runs after the operator-control gate and before dispatch, so a trip
        // from inside it is exactly the window under test.
        var validator = new TrippingRiskValidator(controls);
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            riskValidator: validator,
            operatorControls: controls);

        var result = await oms.PlaceOrderAsync(Order("AAPL"));

        result.Success.Should().BeFalse("the halt was established before the order was dispatched");
        result.ErrorMessage.Should().Contain("halt", "the caller must learn the breaker refused it");
        result.OrderState!.Status.Should().Be(OrderStatus.Rejected);
        gateway.SubmittedOrderIds.Should().BeEmpty("passing the gate a moment before the trip is not permission to reach the broker after it");
        oms.GetOpenOrders().Should().BeEmpty("a rejected submission must not linger as a working order");
    }

    [Fact]
    public async Task CancelAllAsync_WaitsForAnInFlightSubmission_AndSweepsItOnceAcknowledged()
    {
        var controls = CreateControls();
        await using var gateway = new RaceBrokerageGateway { HoldSubmissions = true };
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            operatorControls: controls);

        // The submission passes every gate and is parked inside the gateway's SubmitOrderAsync,
        // acknowledged by nothing yet — the shape of an order in flight when a trip arrives.
        var placing = oms.PlaceOrderAsync(Order("AAPL"));
        await gateway.SubmissionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await controls.SetCircuitBreakerAsync(isOpen: true, "trip during dispatch", "operator");
        var sweeping = oms.CancelAllAsync();

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        sweeping.IsCompleted.Should().BeFalse("the sweep must wait for the in-flight acknowledgement rather than snapshot an empty book");

        gateway.ReleaseSubmissions();
        var placed = await placing.WaitAsync(TimeSpan.FromSeconds(5));
        var sweep = await sweeping.WaitAsync(TimeSpan.FromSeconds(5));

        placed.Success.Should().BeTrue("the order was already at the broker when the breaker opened");
        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Completed);
        sweep.Requested.Should().Be(1, "the order acknowledged during the wait is in the book the sweep reads");
        sweep.Cancelled.Should().Be(1);
        gateway.CancelledOrderIds.Should().ContainSingle().Which.Should().Be(placed.OrderId);
        oms.GetOpenOrders().Should().BeEmpty();
    }

    [Fact]
    public async Task CancelAllAsync_WhenAnInFlightSubmissionNeverSettles_DoesNotReportAnEmptiedBook()
    {
        var controls = CreateControls();
        await using var gateway = new RaceBrokerageGateway { HoldSubmissions = true, RefuseCancelOfUnacknowledged = true };
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            operatorControls: controls,
            options: new OrderManagementSystemOptions
            {
                CancelAllInFlightSettleTimeout = TimeSpan.FromMilliseconds(100)
            });

        var placing = oms.PlaceOrderAsync(Order("AAPL"));
        await gateway.SubmissionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var sweep = await oms.CancelAllAsync().WaitAsync(TimeSpan.FromSeconds(5));

        sweep.Outcome.Should().NotBe(KillSwitchSweepOutcome.Completed,
            "a submission the broker has not acknowledged may still land after every cancellation was sent");
        sweep.RequiresOperatorAction.Should().BeTrue();
        sweep.StillWorking.Should().ContainSingle()
            .Which.Symbol.Should().Be("AAPL");

        gateway.ReleaseSubmissions();
        await placing.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelAllAsync_WithNothingInFlight_DoesNotWait()
    {
        var controls = CreateControls();
        await using var gateway = new RaceBrokerageGateway();
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            operatorControls: controls,
            options: new OrderManagementSystemOptions
            {
                CancelAllInFlightSettleTimeout = TimeSpan.FromSeconds(30)
            });

        var sweep = await oms.CancelAllAsync().WaitAsync(TimeSpan.FromSeconds(2));

        sweep.Should().BeSameAs(KillSwitchSweepResult.Empty);
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
        }
    }

    private ExecutionOperatorControlService CreateControls() => new(
        new ExecutionOperatorControlOptions(Path.Combine(_root, Guid.NewGuid().ToString("N"))),
        NullLogger<ExecutionOperatorControlService>.Instance);

    private static OrderRequest Order(string symbol) => new()
    {
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 150m
    };

    /// <summary>Approves every order, and opens the breaker while doing so.</summary>
    private sealed class TrippingRiskValidator(ExecutionOperatorControlService controls) : IRiskValidator
    {
        public async Task<RiskValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            await controls.SetCircuitBreakerAsync(isOpen: true, "halt raised during validation", "risk-engine", ct: ct);
            return RiskValidationResult.Approved();
        }
    }

    /// <summary>
    /// Gateway double whose submissions can be held open, standing in for a broker that has
    /// received an order but not yet acknowledged it.
    /// </summary>
    private sealed class RaceBrokerageGateway : IExecutionGateway, IExecutionGatewayModeProvider, IAsyncDisposable
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<string> _acknowledged = new(StringComparer.Ordinal);
        private readonly List<string> _submitted = new();
        private readonly List<string> _cancelled = new();

        public bool HoldSubmissions { get; init; }

        public bool RefuseCancelOfUnacknowledged { get; init; }

        public TaskCompletionSource SubmissionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> SubmittedOrderIds
        {
            get { lock (_submitted) return _submitted.ToList(); }
        }

        public IReadOnlyList<string> CancelledOrderIds
        {
            get { lock (_cancelled) return _cancelled.ToList(); }
        }

        public string GatewayId => "race-test-gateway";

        public bool IsConnected => true;

        public ExecutionMode ExecutionMode => ExecutionMode.Paper;

        public void ReleaseSubmissions() => _release.TrySetResult();

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            var orderId = request.ClientOrderId ?? Guid.NewGuid().ToString("N");
            lock (_submitted)
                _submitted.Add(orderId);
            SubmissionStarted.TrySetResult();
            if (HoldSubmissions)
            {
                await _release.Task.ConfigureAwait(false);
            }

            lock (_acknowledged)
                _acknowledged.Add(orderId);
            return new ExecutionReport
            {
                OrderId = orderId,
                ClientOrderId = orderId,
                ReportType = ExecutionReportType.New,
                Symbol = request.Symbol,
                Side = request.Side,
                OrderStatus = OrderStatus.Accepted,
                OrderQuantity = request.Quantity,
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
        {
            lock (_cancelled)
                _cancelled.Add(orderId);
            bool acknowledged;
            lock (_acknowledged)
                acknowledged = _acknowledged.Contains(orderId);
            if (RefuseCancelOfUnacknowledged && !acknowledged)
            {
                return Task.FromResult(new ExecutionReport
                {
                    OrderId = orderId,
                    ClientOrderId = orderId,
                    ReportType = ExecutionReportType.Rejected,
                    Symbol = "AAPL",
                    Side = OrderSide.Buy,
                    OrderStatus = OrderStatus.Accepted,
                    RejectReason = "order not found",
                    Timestamp = DateTimeOffset.UtcNow
                });
            }

            return Task.FromResult(new ExecutionReport
            {
                OrderId = orderId,
                ClientOrderId = orderId,
                ReportType = ExecutionReportType.Cancelled,
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Cancelled,
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            yield break;
        }

        public ValueTask DisposeAsync()
        {
            _release.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
