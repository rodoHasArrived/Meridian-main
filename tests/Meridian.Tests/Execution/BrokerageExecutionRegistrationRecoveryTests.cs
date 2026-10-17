using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class BrokerageExecutionRegistrationRecoveryTests
{
    [Fact]
    public async Task SelectedBrokerageWithoutRecoveryStore_CannotResolveOrderManager()
    {
        var gateway = new BrokerageFixture();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBrokerageGateway(gateway.GatewayId, _ => gateway);
        services.AddBrokerageExecution(config =>
        {
            config.Gateway = gateway.GatewayId;
            config.LiveExecutionEnabled = true;
        });
        await using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<IOrderManager>();

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(FileBrokerageOrderRecoveryStore)}*");
        gateway.SubmitCount.Should().Be(0);
    }

    [Fact]
    public async Task ExplicitSandboxBrokerageWithoutRecoveryStore_CannotResolveOrderManager()
    {
        var gateway = new BrokerageFixture();
        var services = SandboxServices(gateway);
        await using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<IOrderManager>();

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(FileBrokerageOrderRecoveryStore)}*");
        gateway.SubmitCount.Should().Be(0,
            "a sandbox also accepts broker orders that must survive a host restart");
    }

    [Fact]
    public async Task ConfiguredRecoveryStore_RetainsAmbiguousDispatchAcrossServiceProviderRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-di-recovery", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "orders.json");
        var request = Request("di-restart-ambiguous-order");
        var firstGateway = new BrokerageFixture();
        try
        {
            var services = SandboxServices(firstGateway);
            services.AddSingleton(new FileBrokerageOrderRecoveryStore(path, firstGateway.GatewayId));
            await using (var provider = services.BuildServiceProvider())
            {
                var oms = provider.GetRequiredService<IOrderManager>();
                var result = await oms.PlaceOrderAsync(request);

                result.Success.Should().BeFalse();
                result.OrderState.Status.Should().Be(OrderStatus.PendingNew);
                firstGateway.SubmitCount.Should().Be(1);
                provider.GetRequiredService<FileBrokerageOrderRecoveryStore>().Load()
                    .Should().ContainSingle(order => order.State.OrderId == request.ClientOrderId && order.RequiresRecovery);
            }

            var recoveredGateway = new BrokerageFixture();
            var recoveredServices = SandboxServices(recoveredGateway);
            recoveredServices.AddSingleton(new FileBrokerageOrderRecoveryStore(path, recoveredGateway.GatewayId));
            await using var recoveredProvider = recoveredServices.BuildServiceProvider();
            var recovered = recoveredProvider.GetRequiredService<IOrderManager>();

            recovered.GetOpenOrders().Should().ContainSingle(order =>
                order.OrderId == request.ClientOrderId && order.Quantity == request.Quantity);
            recovered.Should().BeOfType<OrderManagementSystem>().Subject.GetRecoveryOrders()
                .Should().ContainSingle(order => order.OrderId == request.ClientOrderId);
            (await recovered.PlaceOrderAsync(request)).Success.Should().BeFalse();
            recoveredGateway.SubmitCount.Should().Be(0,
                "resolving a new OMS must restore accepted identities before any retry can reach the broker");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PaperExecutionWithoutRecoveryStore_ResolvesAndAcceptsOrders()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBrokerageExecution(config =>
            config.BrokerFlows["paper"] = new BrokerFlowFlags { PaperOrderFlowEnabled = true });
        await using var provider = services.BuildServiceProvider();

        provider.GetService<FileBrokerageOrderRecoveryStore>().Should().BeNull();
        var gateway = provider.GetRequiredService<IExecutionGateway>();
        gateway.Should().BeOfType<PaperTradingGateway>();
        await gateway.ConnectAsync();
        var oms = provider.GetRequiredService<IOrderManager>();
        var result = await oms.PlaceOrderAsync(Request("paper-without-broker-recovery"));

        result.Success.Should().BeTrue();
        result.OrderState.Status.Should().Be(OrderStatus.Accepted);
        oms.GetOpenOrders().Should().ContainSingle(order => order.OrderId == result.OrderId);
    }

    private static ServiceCollection SandboxServices(BrokerageFixture gateway)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IExecutionGateway>(gateway);
        services.AddBrokerageExecution(config =>
        {
            config.Gateway = gateway.GatewayId;
            config.BrokerFlows[gateway.GatewayId] = new BrokerFlowFlags { PaperOrderFlowEnabled = true };
        });
        return services;
    }

    private static OrderRequest Request(string orderId) => new()
    {
        ClientOrderId = orderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 150m
    };

    private sealed class BrokerageFixture : IBrokerageGateway, IExecutionGatewayModeProvider, IBrokerageConnectionState
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();

        public int SubmitCount { get; private set; }
        public string GatewayId => "alpaca";
        public string BrokerDisplayName => "Brokerage recovery fixture";
        public BrokerageCapabilities BrokerageCapabilities => BrokerageCapabilities.UsEquity();
        public bool IsConnected => true;
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;
        public long ConnectionGeneration => 1;
        public bool IsExecutionStreamHealthy => true;
        public string ScopeIdentity => "alpaca|paper|account-a";

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            SubmitCount++;
            return Task.FromException<ExecutionReport>(new IOException("Broker accepted the order before disconnecting."));
        }

        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) =>
            _reports.Reader.ReadAllAsync(ct);

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(BrokerHealthStatus.Healthy());
    }
}
