using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class SecurityMasterGateIdentityTests
{
    [Fact]
    public async Task Gate_RetainsTheCanonicalResolverIdentity()
    {
        var identity = Guid.NewGuid();
        var resolver = Substitute.For<ISecurityResolver>();
        resolver.ResolveCanonicalAsync(Arg.Any<ResolveSecurityRequest>(), Arg.Any<CancellationToken>())
            .Returns(SecurityCanonicalResolution.Resolve(identity));
        var gate = new SecurityMasterGate(resolver, NullLogger<SecurityMasterGate>.Instance);

        var decision = await gate.CheckAsync("AAPL");

        Assert.True(decision.IsApproved);
        Assert.Equal(identity, decision.SecurityId);
        await resolver.Received(1).ResolveCanonicalAsync(Arg.Is<ResolveSecurityRequest>(request =>
            request.IdentifierKind == SecurityIdentifierKind.Ticker && request.IdentifierValue == "AAPL" && request.ActiveOnly),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LegacyPaperOrder_RetainsSecurityIdentityWithoutAuthorizingItsRunLabel()
    {
        var identity = Guid.NewGuid();
        var security = Substitute.For<ISecurityMasterGate>();
        security.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SecurityMasterGateResult(true) { SecurityId = identity });
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance,
            options: new Meridian.Execution.Adapters.PaperTradingGatewayOptions { AllowScaffoldMarketFills = true },
            costOptions: new Meridian.Execution.PaperMatching.PaperTradingCostOptions { CommissionRate = 0m });
        var publisher = new AccountingPublisher();
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            securityMasterGate: security, portfolioState: new PaperTradingPortfolio(10_000m), tradeEventPublisher: publisher);
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = "admitted-identity",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = .5m,
            StrategyId = "identity-strategy",
            Metadata = new Dictionary<string, string>
            {
                ["runId"] = "admitted-run",
                ["orderOrigin"] = "StrategyRun",
                ["accountingRunId"] = "admitted-run"
            }
        });

        Assert.True(result.Success);
        var state = Assert.IsType<OrderState>(result.OrderState);
        Assert.Equal(identity, state.AccountingSecurityId);
        Assert.Equal("admitted-run", state.RunId);
        Assert.Equal(OrderCommandOrigin.Unattributed, state.Origin);
        Assert.Null(state.AccountingRunId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var delivered = await oms.ExecutionReports.ReadAsync(timeout.Token);
        Assert.Equal(identity, delivered.AccountingSecurityId);
        Assert.Null(delivered.AccountingRunId);
        Assert.Equal("admitted-run", Assert.Single(publisher.Events).RunId);
        Assert.Null(Assert.Single(publisher.Events).AccountingRunId);
    }

    [Fact]
    public async Task TypedStrategyOrder_MintsAccountingRunAfterSuccessfulAdmission()
    {
        var identity = Guid.NewGuid();
        var account = Guid.NewGuid();
        var security = Substitute.For<ISecurityMasterGate>();
        security.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SecurityMasterGateResult(true) { SecurityId = identity });
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance,
            options: new Meridian.Execution.Adapters.PaperTradingGatewayOptions { AllowScaffoldMarketFills = true },
            costOptions: new Meridian.Execution.PaperMatching.PaperTradingCostOptions { CommissionRate = 0m });
        var publisher = new AccountingPublisher();
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            securityMasterGate: security, portfolioState: new PaperTradingPortfolio(10_000m), tradeEventPublisher: publisher);
        await oms.StartAsync();
        var authority = OrderSubmissionAuthority.ForStrategyRun("runtime",
            new ExecutionAccountGrant(account, "runtime", ExecutionAccountGrantSources.StrategyRun, DateTimeOffset.UtcNow),
            "authorized-run");

        var result = await oms.SubmitAsync(new OrderSubmitCommand(new OrderRequest
        {
            ClientOrderId = "typed-accounting-run",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = .5m,
            FundAccountId = account,
            StrategyId = "identity-strategy"
        }, authority));

        Assert.True(result.Success);
        var state = Assert.IsType<OrderState>(result.OrderState);
        Assert.Equal("authorized-run", state.AccountingRunId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var delivered = await oms.ExecutionReports.ReadAsync(timeout.Token);
        Assert.Equal(identity, delivered.AccountingSecurityId);
        Assert.Equal("authorized-run", delivered.AccountingRunId);
        Assert.Equal("authorized-run", Assert.Single(publisher.Events).AccountingRunId);
    }

    [Fact]
    public void ReportLineage_NullOmitsLegacyFieldsAndPreservesExecutionIdentity()
    {
        var legacy = new ExecutionReport
        {
            OrderId = "historical-fill",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            ReportType = ExecutionReportType.Fill,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 1m,
            FilledQuantity = 1m,
            FillPrice = 100m,
            Timestamp = new DateTimeOffset(2026, 1, 2, 15, 0, 0, TimeSpan.Zero)
        };
        var json = JsonSerializer.Serialize(legacy, ExecutionJsonContext.Default.ExecutionReport);

        Assert.DoesNotContain("accountingSecurityId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("accountingRunId", json, StringComparison.Ordinal);
        var restored = Assert.IsType<ExecutionReport>(JsonSerializer.Deserialize(json, ExecutionJsonContext.Default.ExecutionReport));
        Assert.Null(restored.AccountingSecurityId);
        Assert.Null(restored.AccountingRunId);
        Assert.Equal(PaperSessionFillRecord.ComputeCanonicalHash(legacy), PaperSessionFillRecord.ComputeCanonicalHash(restored));
        var attributed = legacy with { AccountingSecurityId = Guid.NewGuid(), AccountingRunId = "retained-run" };
        Assert.Equal(PaperSessionFillRecord.ComputeCanonicalFillId(legacy), PaperSessionFillRecord.ComputeCanonicalFillId(attributed));
        Assert.NotEqual(PaperSessionFillRecord.ComputeCanonicalHash(legacy), PaperSessionFillRecord.ComputeCanonicalHash(attributed));
    }

    [Fact]
    public void BrokerageRestart_RetainsKnownIdentityAndOmitsAbsentLegacyIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-admission-identity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var identity = Guid.NewGuid();
            var state = new OrderState
            {
                OrderId = "retained-identity",
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = OrderType.Limit,
                Quantity = 1m,
                LimitPrice = 100m,
                Status = OrderStatus.Accepted,
                AccountingSecurityId = identity,
                Origin = OrderCommandOrigin.StrategyRun,
                RunId = "retained-run",
                AccountingRunId = "retained-run"
            };
            var path = Path.Combine(root, "broker-orders.json");
            var store = new FileBrokerageOrderRecoveryStore(path, "synthetic-identity");
            store.BindScope("synthetic-paper-scope");
            store.Save(new RetainedBrokerageOrder(state, "broker-1", true));

            var restarted = new FileBrokerageOrderRecoveryStore(path, "synthetic-identity");
            var retained = Assert.Single(restarted.Load()).State;
            Assert.Equal(identity, retained.AccountingSecurityId);
            Assert.Equal("retained-run", retained.AccountingRunId);
            var legacy = JsonSerializer.Serialize(state with { AccountingSecurityId = null, AccountingRunId = null }, ExecutionJsonContext.Default.OrderState);
            Assert.DoesNotContain("accountingSecurityId", legacy, StringComparison.Ordinal);
            Assert.DoesNotContain("accountingRunId", legacy, StringComparison.Ordinal);
            var historical = Assert.IsType<OrderState>(JsonSerializer.Deserialize(legacy, ExecutionJsonContext.Default.OrderState));
            Assert.Equal(OrderCommandOrigin.StrategyRun, historical.Origin);
            Assert.Equal("retained-run", historical.RunId);
            Assert.Null(historical.AccountingRunId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class AccountingPublisher : ITradeEventPublisher
    {
        public System.Collections.Concurrent.ConcurrentQueue<TradeExecutedEvent> Events { get; } = new();
        public void Publish(TradeExecutedEvent tradeEvent) => Events.Enqueue(tradeEvent);
    }
}
