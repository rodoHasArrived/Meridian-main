using System.Runtime.CompilerServices;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class TypedOrderEntryTests
{
    [Theory]
    [InlineData("symbol")]
    [InlineData("side")]
    [InlineData("type")]
    [InlineData("time-in-force")]
    [InlineData("limit")]
    [InlineData("stop")]
    [InlineData("trail")]
    public async Task MalformedRequest_IsAuditedBeforeSizingRiskOrDispatch(string invalidField)
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var audit = new ExecutionAuditTrailService(
                new ExecutionAuditTrailOptions(root), NullLogger<ExecutionAuditTrailService>.Instance);
            var gateway = new RecordingGateway();
            var risk = new RecordingTypedValidator();
            using var oms = CreateOms(gateway, risk, audit);
            await oms.StartAsync();

            var request = invalidField switch
            {
                "symbol" => ValidRequest() with { Symbol = " " },
                "side" => ValidRequest() with { Side = (OrderSide)999 },
                "type" => ValidRequest() with { Type = (OrderType)999 },
                "time-in-force" => ValidRequest() with { TimeInForce = (TimeInForce)999 },
                "limit" => ValidRequest() with { LimitPrice = null },
                "stop" => ValidRequest() with { Type = OrderType.StopLimit, StopPrice = null },
                _ => ValidRequest() with { Type = OrderType.TrailingStop, TrailPrice = 1m, TrailPercent = 2m }
            };

            var result = await oms.PlaceOrderAsync(request);

            result.Success.Should().BeFalse();
            result.OrderState!.Status.Should().Be(OrderStatus.Rejected);
            gateway.FaceValueQueries.Should().Be(0);
            gateway.NotionalQueries.Should().Be(0);
            gateway.Submitted.Should().BeEmpty();
            risk.Received.Should().BeNull();
            risk.LegacyCalls.Should().Be(0);
            var entries = await audit.GetRecentAsync();
            entries.Should().ContainSingle(entry => entry.Action == "OrderRejected"
                && entry.OrderId == result.OrderId && entry.Reason == "ORDER_REQUEST_INVALID");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedRequest_IsRejectedWhenNoRiskValidatorIsConfigured()
    {
        var gateway = new RecordingGateway();
        using var oms = CreateOms(gateway);
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(ValidRequest() with { LimitPrice = null });

        result.Success.Should().BeFalse();
        gateway.FaceValueQueries.Should().Be(0);
        gateway.Submitted.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task NewSubmission_UsesTypedRiskEntryAndGatewayResolvedSizing(bool faceValue, bool notional)
    {
        var gateway = new RecordingGateway { FaceValue = faceValue, RoutesNotional = notional };
        var risk = new RecordingTypedValidator();
        using var oms = CreateOms(gateway, risk);
        await oms.StartAsync();
        var request = ValidRequest() with
        {
            Metadata = notional ? new Dictionary<string, string> { ["notional"] = "2500" } : null
        };

        var result = await oms.PlaceOrderAsync(request);

        result.Success.Should().BeTrue();
        risk.LegacyCalls.Should().Be(0);
        risk.Received.Should().NotBeNull();
        gateway.FaceValueQueries.Should().Be(1);
        if (faceValue)
        {
            risk.Received!.Sizing.Should().BeOfType<OrderSizing.FaceValuePercentageOfPar>();
            result.OrderState!.UsesFaceValuePercentageOfPar.Should().BeTrue();
        }
        else if (notional)
        {
            risk.Received!.Sizing.Should().BeOfType<OrderSizing.BrokerNotional>()
                .Which.Amount.Should().Be(2500m);
            result.OrderState!.RoutedNotional.Should().Be(2500m);
        }
        else
        {
            risk.Received!.Sizing.Should().BeOfType<OrderSizing.Quantity>();
            result.OrderState!.RoutedNotional.Should().BeNull();
        }

        var submitted = gateway.Submitted.Should().ContainSingle().Which;
        OrderSizingMetadata.UsesFaceValuePercentageOfPar(submitted.Metadata).Should().BeFalse();
        submitted.Symbol.Should().Be(request.Symbol);
        submitted.Quantity.Should().Be(request.Quantity);
    }

    [Fact]
    public async Task LegacyCustomValidator_ReceivesResolvedSizingThroughDefaultInterfaceAdapter()
    {
        var gateway = new RecordingGateway { FaceValue = true };
        var risk = new LegacyValidator();
        using var oms = CreateOms(gateway, risk);
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(ValidRequest());

        result.Success.Should().BeTrue();
        risk.Calls.Should().Be(1);
        OrderSizingMetadata.UsesFaceValuePercentageOfPar(risk.Received!.Metadata).Should().BeTrue();
        OrderSizingMetadata.UsesFaceValuePercentageOfPar(gateway.Submitted.Single().Metadata).Should().BeFalse();
    }

    [Theory]
    [InlineData("Notional", "2500", "1", 2500)]
    [InlineData("notional", "false", "1", 0)]
    [InlineData("notional", "invalid", "1", 0)]
    [InlineData("notional", "", "125", 125)]
    [InlineData("notional", "yes", "1", 10)]
    [InlineData("notional", "true", "1", 10)]
    public async Task NativeNotionalSizing_PreservesGatewayAliasPrecedenceAndBooleanQuantityFlag(
        string firstKey, string firstValue, string secondValue, int expectedAmount)
    {
        var gateway = new RecordingGateway { RoutesNotional = true };
        var risk = new RecordingTypedValidator();
        using var oms = CreateOms(gateway, risk);
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(ValidRequest() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [firstKey] = firstValue,
                ["alpaca:notional"] = secondValue
            }
        });

        result.Success.Should().BeTrue();
        if (expectedAmount == 0)
        {
            risk.Received!.Sizing.Should().BeOfType<OrderSizing.Quantity>();
            result.OrderState!.RoutedNotional.Should().BeNull();
            gateway.NotionalQueries.Should().Be(0);
        }
        else
        {
            risk.Received!.Sizing.Should().BeOfType<OrderSizing.BrokerNotional>()
                .Which.Amount.Should().Be(expectedAmount);
            result.OrderState!.RoutedNotional.Should().Be(expectedAmount);
            gateway.NotionalQueries.Should().Be(1);
        }
    }

    [Fact]
    public async Task UnsupportedNativeNotional_IsRejectedBeforeRiskAndDispatch()
    {
        var gateway = new RecordingGateway();
        var risk = new RecordingTypedValidator();
        using var oms = CreateOms(gateway, risk);
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(ValidRequest() with
        {
            Metadata = new Dictionary<string, string> { ["Notional"] = "2500" }
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("routes order quantity");
        gateway.NotionalQueries.Should().Be(1);
        gateway.FaceValueQueries.Should().Be(0);
        gateway.Submitted.Should().BeEmpty();
        risk.Received.Should().BeNull();
        risk.LegacyCalls.Should().Be(0);
    }

    [Fact]
    public async Task FaceValueSizing_OverridesNativeNotionalWhenGatewaySupportsBoth()
    {
        var gateway = new RecordingGateway { FaceValue = true, RoutesNotional = true };
        var risk = new RecordingTypedValidator();
        using var oms = CreateOms(gateway, risk);
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(ValidRequest() with
        {
            Quantity = 5000m,
            LimitPrice = 98m,
            Metadata = new Dictionary<string, string> { ["notional"] = "1" }
        });

        result.Success.Should().BeTrue();
        risk.Received!.Sizing.Should().BeOfType<OrderSizing.FaceValuePercentageOfPar>();
        result.OrderState!.UsesFaceValuePercentageOfPar.Should().BeTrue();
        result.OrderState.RoutedNotional.Should().BeNull();
        OrderSizingMetadata.UsesFaceValuePercentageOfPar(risk.Received.ToRiskRequest().Metadata).Should().BeTrue();
        gateway.NotionalQueries.Should().Be(1);
        gateway.FaceValueQueries.Should().Be(1);
    }

    [Theory]
    [InlineData(false, "2500", 10, 100, 1500, false)]
    [InlineData(false, "250", 10, 100, 500, true)]
    [InlineData(true, "1", 5000, 98, 5000, true)]
    [InlineData(true, "1", 5000, 102, 5000, false)]
    public async Task ConfiguredNotionalLimit_UsesResolvedSizingInsteadOfRawQuantityTimesPrice(
        bool faceValue, string notional, int quantity, int price, int ceiling, bool expectedSuccess)
    {
        var gateway = new RecordingGateway { FaceValue = faceValue, RoutesNotional = true };
        var risk = new RecordingTypedValidator();
        using var oms = CreateOms(gateway, risk, configuration: Configuration(ceiling));
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(ValidRequest() with
        {
            Quantity = quantity,
            LimitPrice = price,
            Metadata = new Dictionary<string, string> { ["notional"] = notional }
        });

        result.Success.Should().Be(expectedSuccess);
        gateway.FaceValueQueries.Should().Be(1);
        gateway.NotionalQueries.Should().Be(1);
        if (expectedSuccess)
        {
            gateway.Submitted.Should().ContainSingle();
            risk.Received.Should().NotBeNull();
        }
        else
        {
            result.ErrorMessage.Should().Contain("configured maximum");
            gateway.Submitted.Should().BeEmpty();
            risk.Received.Should().BeNull();
        }
    }

    [Fact]
    public async Task BrokerBackedSubmission_RetainsGatewayResolvedFaceValueSizingDurably()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var gateway = new RecordingBroker { FaceValue = true };
            var risk = new RecordingTypedValidator();
            var store = new FileBrokerageOrderRecoveryStore(Path.Combine(root, "orders.json"), gateway.GatewayId);
            using var oms = CreateOms(gateway, risk, configuration: Configuration(5000m), recoveryStore: store);
            await oms.StartAsync();

            var result = await oms.PlaceOrderAsync(ValidRequest() with { Quantity = 5000m, LimitPrice = 98m });

            result.Success.Should().BeTrue();
            risk.Received!.Sizing.Should().BeOfType<OrderSizing.FaceValuePercentageOfPar>();
            gateway.Submitted.Should().ContainSingle();
            var reloaded = new FileBrokerageOrderRecoveryStore(Path.Combine(root, "orders.json"), gateway.GatewayId);
            reloaded.Load().Should().ContainSingle().Which.State.UsesFaceValuePercentageOfPar.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CallerCollections_CannotChangeValidatedRiskOrGatewayRequestWhileRiskAwaits()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var risk = new RecordingTypedValidator
        {
            OnValidate = async () =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        };
        var gateway = new RecordingGateway();
        using var oms = CreateOms(gateway, risk);
        await oms.StartAsync();
        var metadata = new Dictionary<string, string> { ["client-note"] = "original" };
        var legs = new List<OrderLeg>
        {
            new() { Symbol = "AAPL", Side = OrderSide.Buy, RatioQuantity = 1m }
        };
        var placement = oms.PlaceOrderAsync(ValidRequest() with { Metadata = metadata, Legs = legs });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            metadata["client-note"] = "changed";
            legs.Clear();
        }
        finally
        {
            release.TrySetResult();
        }

        var result = await placement;

        result.Success.Should().BeTrue();
        risk.Received!.Request.Metadata!["client-note"].Should().Be("original");
        risk.Received.Request.Legs.Should().ContainSingle().Which.RatioQuantity.Should().Be(1m);
        var submitted = gateway.Submitted.Single();
        submitted.Metadata!["client-note"].Should().Be("original");
        submitted.Legs.Should().ContainSingle();
    }

    [Fact]
    public async Task DuplicateId_IsRejectedBeforeMalformedReplacementCanAlterWorkingOrder()
    {
        var gateway = new RecordingGateway();
        using var oms = CreateOms(gateway);
        await oms.StartAsync();
        var request = ValidRequest() with { ClientOrderId = "typed-duplicate" };
        var first = await oms.PlaceOrderAsync(request);

        var duplicate = await oms.PlaceOrderAsync(request with { Symbol = " ", LimitPrice = null });

        first.Success.Should().BeTrue();
        duplicate.Success.Should().BeFalse();
        duplicate.ErrorMessage.Should().Contain("already");
        var retained = oms.GetOrder(first.OrderId)!;
        retained.Symbol.Should().Be("AAPL");
        retained.Status.Should().Be(OrderStatus.Accepted);
        gateway.Submitted.Should().ContainSingle();
        gateway.FaceValueQueries.Should().Be(1);
    }

    private static OrderRequest ValidRequest() => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 100m
    };

    private static OrderManagementSystem CreateOms(
        RecordingGateway gateway, IRiskValidator? risk = null, ExecutionAuditTrailService? audit = null,
        BrokerageConfiguration? configuration = null, FileBrokerageOrderRecoveryStore? recoveryStore = null) =>
        new(gateway, NullLogger<OrderManagementSystem>.Instance, riskValidator: risk, auditTrail: audit,
            brokerageConfiguration: configuration, recoveryStore: recoveryStore);

    private static BrokerageConfiguration Configuration(decimal maxNotional) => new()
    {
        Gateway = "typed-paper",
        MaxOrderNotional = maxNotional,
        BrokerFlows = new Dictionary<string, BrokerFlowFlags>
        {
            ["typed-paper"] = new() { PaperOrderFlowEnabled = true }
        }
    };

    private sealed class LegacyValidator : IRiskValidator
    {
        public int Calls { get; private set; }
        public OrderRequest? Received { get; private set; }

        public Task<RiskValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            Calls++;
            Received = request;
            return Task.FromResult(RiskValidationResult.Approved());
        }
    }

    private sealed class RecordingTypedValidator : IRiskValidator
    {
        public GatewayResolvedOrder? Received { get; private set; }
        public int LegacyCalls { get; private set; }
        public Func<Task>? OnValidate { get; init; }

        public Task<RiskValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            LegacyCalls++;
            throw new InvalidOperationException("A new submission must use the typed entry point.");
        }

        public async Task<RiskValidationResult> ValidateResolvedOrderAsync(
            GatewayResolvedOrder order, CancellationToken ct = default)
        {
            Received = order;
            if (OnValidate is not null)
                await OnValidate();
            return RiskValidationResult.Approved();
        }
    }

    private class RecordingGateway : IExecutionGateway, IExecutionGatewayModeProvider,
        IFaceValueOrderSizingGateway, INotionalOrderSizingGateway
    {
        public string GatewayId => "typed-paper";
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;
        public bool IsConnected => true;
        public bool FaceValue { get; init; }
        public bool RoutesNotional { get; init; }
        public int FaceValueQueries { get; private set; }
        public int NotionalQueries { get; private set; }
        public List<OrderRequest> Submitted { get; } = [];

        public bool UsesFaceValuePercentageOfPar(OrderRequest request)
        {
            FaceValueQueries++;
            return FaceValue;
        }

        public bool RoutesNotionalMetadata(OrderRequest request)
        {
            NotionalQueries++;
            return RoutesNotional;
        }

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            Submitted.Add(request);
            return Task.FromResult(new ExecutionReport
            {
                OrderId = request.ClientOrderId!,
                ClientOrderId = request.ClientOrderId,
                Symbol = request.Symbol,
                Side = request.Side,
                ReportType = ExecutionReportType.New,
                OrderStatus = OrderStatus.Accepted,
                OrderQuantity = request.Quantity,
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExecutionReport> ModifyOrderAsync(
            string orderId, OrderModification modification, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            yield break;
        }
    }

    private sealed class RecordingBroker : RecordingGateway, IBrokerageGateway, IBrokerageConnectionState
    {
        public string BrokerDisplayName => "Typed sandbox broker";
        public BrokerageCapabilities BrokerageCapabilities => BrokerageCapabilities.UsEquityAndFixedIncome();
        public long ConnectionGeneration => 1;
        public bool IsExecutionStreamHealthy => true;
        public string ScopeIdentity => "typed-paper:verified-sandbox-account";

        public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new AccountInfo { AccountId = "sandbox-account", Equity = 100_000m, Cash = 100_000m });

        public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BrokerPosition>>([]);

        public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BrokerOrder>>([]);

        public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(BrokerHealthStatus.Healthy());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
