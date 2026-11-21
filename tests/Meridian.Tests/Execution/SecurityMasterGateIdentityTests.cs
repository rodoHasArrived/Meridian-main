using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution;
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
        resolver.ResolveAsync(Arg.Any<ResolveSecurityRequest>(), Arg.Any<CancellationToken>()).Returns(identity);
        var gate = new SecurityMasterGate(resolver, NullLogger<SecurityMasterGate>.Instance);

        var decision = await gate.CheckAsync("AAPL");

        Assert.True(decision.IsApproved);
        Assert.Equal(identity, decision.SecurityId);
        await resolver.Received(1).ResolveAsync(Arg.Is<ResolveSecurityRequest>(request =>
            request.IdentifierKind == SecurityIdentifierKind.Ticker && request.IdentifierValue == "AAPL" && request.ActiveOnly),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptedPaperOrder_RetainsAdmissionIdentityThroughFillDelivery()
    {
        var identity = Guid.NewGuid();
        var security = Substitute.For<ISecurityMasterGate>();
        security.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SecurityMasterGateResult(true) { SecurityId = identity });
        await using var gateway = new PaperTradingGateway(NullLogger<PaperTradingGateway>.Instance,
            options: new Meridian.Execution.Adapters.PaperTradingGatewayOptions { AllowScaffoldMarketFills = true },
            costOptions: new Meridian.Execution.PaperMatching.PaperTradingCostOptions { CommissionRate = 0m });
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            securityMasterGate: security, portfolioState: new PaperTradingPortfolio(10_000m));
        await oms.StartAsync();

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = "admitted-identity",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = .5m,
            StrategyId = "identity-strategy",
            Metadata = new Dictionary<string, string> { ["runId"] = "admitted-run" }
        });

        Assert.True(result.Success);
        Assert.Equal(identity, Assert.IsType<OrderState>(result.OrderState).AccountingSecurityId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var delivered = await oms.ExecutionReports.ReadAsync(timeout.Token);
        Assert.Equal(identity, delivered.AccountingSecurityId);
        Assert.Equal("admitted-run", delivered.AccountingRunId);
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
                AccountingSecurityId = identity
            };
            var path = Path.Combine(root, "broker-orders.json");
            var store = new FileBrokerageOrderRecoveryStore(path, "synthetic-identity");
            store.BindScope("synthetic-paper-scope");
            store.Save(new RetainedBrokerageOrder(state, "broker-1", true));

            var restarted = new FileBrokerageOrderRecoveryStore(path, "synthetic-identity");
            Assert.Equal(identity, Assert.Single(restarted.Load()).State.AccountingSecurityId);
            var legacy = JsonSerializer.Serialize(state with { AccountingSecurityId = null }, ExecutionJsonContext.Default.OrderState);
            Assert.DoesNotContain("accountingSecurityId", legacy, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
