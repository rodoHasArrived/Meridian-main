using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for <see cref="IBBrokerageGateway"/>, including fixed income (bond/treasury) support.
/// Because the IB gateway communicates via TWS (not HTTP), these tests validate gateway
/// metadata and the stub order-submission path without requiring a live TWS connection.
/// </summary>
public sealed class IBBrokerageGatewayTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static IBBrokerageGateway CreateSut(bool paper = true) =>
        new(
            new IBOptions(Host: "127.0.0.1", Port: paper ? 7497 : 7496, ClientId: 1, UsePaperTrading: paper),
            NullLogger<IBBrokerageGateway>.Instance);

    /// <summary>Creates a connected gateway (skips the host/port validation side-effect).</summary>
    private static async Task<IBBrokerageGateway> CreateConnectedSutAsync(CancellationToken ct = default)
    {
        var sut = CreateSut();
        await sut.ConnectAsync(ct);
        return sut;
    }

    // ── Identity / metadata ────────────────────────────────────────────────

    [Fact]
    public void GatewayId_ReturnsIb()
    {
        var sut = CreateSut();
        sut.GatewayId.Should().Be("ib");
    }

    [Fact]
    public void BrokerDisplayName_ContainsInteractiveBrokers()
    {
        var sut = CreateSut();
        sut.BrokerDisplayName.Should().Contain("Interactive Brokers");
    }

    // ── Fixed income capabilities ─────────────────────────────────────────

    [Fact]
    public void BrokerageCapabilities_DeclaresEquityAndBond()
    {
        var sut = CreateSut();
        sut.BrokerageCapabilities.SupportedAssetClasses.Should().Contain("equity");
        sut.BrokerageCapabilities.SupportedAssetClasses.Should().Contain("bond");
    }

    [Fact]
    public void BrokerageCapabilities_SupportsFixedIncome()
    {
        var sut = CreateSut();
        sut.BrokerageCapabilities.Extensions.Should().ContainKey("supportsFixedIncome");
        sut.BrokerageCapabilities.Extensions["supportsFixedIncome"].Should().Be("true");
    }

    [Fact]
    public void BrokerageCapabilities_SupportsOrderModificationAndExtendedHours()
    {
        var sut = CreateSut();
        sut.BrokerageCapabilities.SupportsOrderModification.Should().BeTrue();
        sut.BrokerageCapabilities.SupportsExtendedHours.Should().BeTrue();
    }

    // ── ConnectAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_ValidOptions_SetsIsConnectedTrue()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.ConnectAsync(cts.Token);

        sut.IsConnected.Should().BeTrue();
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_MissingHost_ThrowsInvalidOperationException()
    {
        var sut = new IBBrokerageGateway(
            new IBOptions(Host: "", Port: 7497, ClientId: 1),
            NullLogger<IBBrokerageGateway>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = () => sut.ConnectAsync(cts.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Host*");
    }

    [Fact]
    public async Task ConnectAsync_InvalidPort_ThrowsInvalidOperationException()
    {
        var sut = new IBBrokerageGateway(
            new IBOptions(Host: "127.0.0.1", Port: 0, ClientId: 1),
            NullLogger<IBBrokerageGateway>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = () => sut.ConnectAsync(cts.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Port*");
    }

    // ── SubmitOrderAsync: equity orders ───────────────────────────────────

    [Fact]
    public async Task SubmitOrderAsync_EquityMarketBuy_ReturnsAcceptedReport()
    {
        await using var sut = await CreateConnectedSutAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var report = await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 100m,
        }, cts.Token);

        report.ReportType.Should().Be(ExecutionReportType.New);
        report.Symbol.Should().Be("AAPL");
        report.Side.Should().Be(OrderSide.Buy);
        report.OrderStatus.Should().Be(OrderStatus.Accepted);
        report.OrderQuantity.Should().Be(100m);
    }

    // ── SubmitOrderAsync: fixed income orders ─────────────────────────────

    [Fact]
    public async Task SubmitOrderAsync_BondOrder_SecTypeMetadata_ReturnsAcceptedReport()
    {
        // secType="BOND" signals a corporate bond contract to the TWS routing layer.
        await using var sut = await CreateConnectedSutAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var report = await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "459200HU8",   // IBM corporate bond CUSIP
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 5000m,       // face value (par amount) in USD
            Metadata = new Dictionary<string, string>
            {
                ["sec_type"] = "BOND",
            },
        }, cts.Token);

        report.ReportType.Should().Be(ExecutionReportType.New);
        report.Symbol.Should().Be("459200HU8");
        report.Side.Should().Be(OrderSide.Buy);
        report.OrderStatus.Should().Be(OrderStatus.Accepted);
        report.OrderQuantity.Should().Be(5000m);
    }

    [Fact]
    public async Task SubmitOrderAsync_TreasuryOrder_SecTypeGovt_ReturnsAcceptedReport()
    {
        // secType="GOVT" signals a US Treasury / government bond to the IB routing desk.
        await using var sut = await CreateConnectedSutAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var report = await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "912828YY0",   // 2-Year Treasury Note CUSIP
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10000m,      // face value in USD
            Metadata = new Dictionary<string, string>
            {
                ["sec_type"] = "GOVT",
            },
        }, cts.Token);

        report.ReportType.Should().Be(ExecutionReportType.New);
        report.Symbol.Should().Be("912828YY0");
        report.OrderStatus.Should().Be(OrderStatus.Accepted);
        report.OrderQuantity.Should().Be(10000m);
    }

    [Fact]
    public async Task SubmitOrderAsync_OrderWithoutSecTypeMetadata_StillSucceeds()
    {
        // No Metadata — standard equity order — sec_type defaults to STK in the TWS layer.
        await using var sut = await CreateConnectedSutAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var report = await sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "SPY",
            Side = OrderSide.Sell,
            Type = OrderType.Limit,
            Quantity = 50m,
        }, cts.Token);

        report.OrderStatus.Should().Be(OrderStatus.Accepted);
        report.Symbol.Should().Be("SPY");
    }

    // ── GetPositionsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPositionsAsync_ReturnsEmpty_InStubMode()
    {
        // In non-IBAPI stub mode, no real positions are returned.
        // In a full IBAPI build, bond/treasury positions include AccruedInterest.
        await using var sut = await CreateConnectedSutAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var positions = await sut.GetPositionsAsync(cts.Token);

        positions.Should().BeEmpty();
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitOrderAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = () => sut.SubmitOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
        }, cts.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not connected*");
    }

    [Fact]
    public async Task DisposeAsync_CompletesCleanly()
    {
        var sut = CreateSut();
        await sut.DisposeAsync();
        sut.IsConnected.Should().BeFalse();
    }
}
