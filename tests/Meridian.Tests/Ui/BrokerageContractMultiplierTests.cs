using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class BrokerageContractMultiplierTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(12.5)]
    [InlineData(150)]
    public async Task MiniAndAdjustedOption_UseRetainedContractMultiplierForConsistencyAndLegacyProjection(decimal multiplier)
    {
        var fixture = new Fixture(multiplier);

        var status = await fixture.SynchronizeAsync();

        status.IsReady.Should().BeTrue();
        fixture.Portfolio.Positions[Fixture.Symbol].ContractMultiplier.Should().Be(multiplier);
        fixture.Portfolio.Positions[Fixture.Symbol].ExactQuantity.Should().Be(2m);
        status.Snapshot.Should().BeSameAs(fixture.Snapshot, "the provider observation is retained without rewritten valuations");
        await fixture.SecurityMaster.Received(2).GetRecordedByIdentifierAsOfAsync(
            SecurityIdentifierKind.OccOptionSymbol, Fixture.Symbol, null, fixture.Now, fixture.Now, Arg.Any<CancellationToken>());
        await fixture.SecurityMaster.DidNotReceiveWithAnyArgs().GetByIdentifierAsync(default, default!, default, default, default);
    }

    public static TheoryData<decimal?> MissingOrNonpositiveMultipliers => new()
    {
        null,
        0m,
        -1m
    };

    [Theory]
    [MemberData(nameof(MissingOrNonpositiveMultipliers))]
    public async Task MissingOrNonpositiveContractMultiplier_BlocksWithoutInventingLegacyExposure(decimal? multiplier)
    {
        var fixture = new Fixture(multiplier);

        var status = await fixture.SynchronizeAsync();

        status.IsReady.Should().BeFalse();
        status.IsConsistent.Should().BeFalse();
        status.BlockingReasons.Should().Contain(reason => reason.Contains("contract multiplier is unresolved", StringComparison.Ordinal));
        var readPositions = () => fixture.Portfolio.Positions;
        readPositions.Should().Throw<InvalidOperationException>().WithMessage("*contract multiplier is unresolved*");
        fixture.Portfolio.GetPosition(Fixture.Symbol).Quantity.Should().Be(2m);
        fixture.Portfolio.Cash.Should().Be(1000m);
        status.Snapshot.Should().BeSameAs(fixture.Snapshot);
    }

    [Fact]
    public async Task MissingOrAmbiguousRetainedIdentity_DoesNotFallBackToCurrentSecurity()
    {
        var fixture = new Fixture(10m);
        fixture.SecurityMaster.GetRecordedByIdentifierAsOfAsync(
            SecurityIdentifierKind.OccOptionSymbol, Fixture.Symbol, null, fixture.Now, fixture.Now, Arg.Any<CancellationToken>())
            .Returns((SecurityDetailDto?)null);
        fixture.SecurityMaster.GetByIdentifierAsync(Arg.Any<SecurityIdentifierKind>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>()).Returns(fixture.Security(100m));

        var status = await fixture.SynchronizeAsync();

        status.IsReady.Should().BeFalse();
        status.BlockingReasons.Should().Contain(reason => reason.Contains("contract multiplier is unresolved", StringComparison.Ordinal));
        await fixture.SecurityMaster.DidNotReceiveWithAnyArgs().GetByIdentifierAsync(default, default!, default, default, default);
    }

    [Fact]
    public async Task LookupFailure_RevokesReadinessAndKeepsPriorObservationAndContractReadable()
    {
        var fixture = new Fixture(10m);
        (await fixture.SynchronizeAsync()).IsReady.Should().BeTrue();
        fixture.SecurityMaster.GetRecordedByIdentifierAsOfAsync(
            SecurityIdentifierKind.OccOptionSymbol, Fixture.Symbol, null, fixture.Now, fixture.Now, Arg.Any<CancellationToken>())
            .Returns<Task<SecurityDetailDto?>>(_ => throw new InvalidOperationException("Sensitive upstream detail"));

        var status = await fixture.SynchronizeAsync();

        status.IsReady.Should().BeFalse();
        status.BlockingReasons.Should().NotContain(reason => reason.Contains("Sensitive upstream detail", StringComparison.Ordinal));
        status.Snapshot.Should().BeSameAs(fixture.Snapshot);
        fixture.Portfolio.Positions[Fixture.Symbol].ContractMultiplier.Should().Be(10m);
        fixture.Portfolio.Cash.Should().Be(1000m);
    }

    [Fact]
    public async Task ContractRevisionChangingDuringObservation_BlocksSynchronization()
    {
        var fixture = new Fixture(10m);
        fixture.SecurityMaster.GetRecordedByIdentifierAsOfAsync(
            SecurityIdentifierKind.OccOptionSymbol, Fixture.Symbol, null, fixture.Now, fixture.Now, Arg.Any<CancellationToken>())
            .Returns(fixture.Security(10m), fixture.Security(10m) with { Version = 2 });

        var status = await fixture.SynchronizeAsync();

        status.IsReady.Should().BeFalse();
        status.BlockingReasons.Should().Contain("Security Master option contracts changed during synchronization; reconcile again.");
    }

    private sealed class Fixture
    {
        internal const string Symbol = "AAPL270115C00100000";
        internal Guid AccountId { get; } = Guid.NewGuid();
        internal DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
        internal ISecurityMasterQueryService SecurityMaster { get; } = Substitute.For<ISecurityMasterQueryService>();
        internal BrokeragePortfolioSnapshotDto Snapshot { get; }
        internal LiveBrokeragePortfolioSyncService Synchronization { get; }
        internal BrokeragePortfolioState Portfolio { get; }
        private readonly Guid _securityId = Guid.NewGuid();

        internal Fixture(decimal? multiplier)
        {
            var marketValue = 2m * 3m * (multiplier is > 0m ? multiplier.Value : 10m);
            Snapshot = new BrokeragePortfolioSnapshotDto(
                new("alpaca", "broker-a", "Paper account", "active", "USD", Now),
                new(1000m, 1000m + marketValue, 800m, "USD"),
                [new(Symbol, 2m, 2m, 3m, marketValue, 20m, "us_option", Currency: "USD")], Now,
                new("alpaca", "broker-a", Now, "USD", "active", BrokerageMarginRegime.RegulationT,
                    1000m, 1000m + marketValue, 800m), IsComplete: true);
            var gateway = Substitute.For<IBrokerageGateway, IBrokeragePortfolioSync>();
            gateway.GatewayId.Returns("alpaca");
            gateway.IsConnected.Returns(true);
            gateway.CheckHealthAsync(Arg.Any<CancellationToken>()).Returns(BrokerHealthStatus.Healthy());
            gateway.GetOpenOrdersAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<BrokerOrder>());
            ((IBrokeragePortfolioSync)gateway).GetPortfolioSnapshotAsync("broker-a", Arg.Any<CancellationToken>()).Returns(Snapshot);
            var oms = Substitute.For<IOrderManager>();
            oms.GetOpenOrders().Returns(Array.Empty<OrderState>());
            oms.GetExposureReservingOrders().Returns(Array.Empty<OrderState>());
            oms.GetCompletedOrders(Arg.Any<int>()).Returns(Array.Empty<OrderState>());
            SecurityMaster.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.OccOptionSymbol, Symbol, null,
                Now, Now, Arg.Any<CancellationToken>()).Returns(Security(multiplier));
            Synchronization = new LiveBrokeragePortfolioSyncService(() => gateway, () => oms,
                new BrokerageExecutionReconciliationService(NullLogger<BrokerageExecutionReconciliationService>.Instance),
                securityMaster: SecurityMaster);
            Portfolio = new BrokeragePortfolioState(Synchronization);
        }

        internal SecurityDetailDto Security(decimal? multiplier) => new(
            _securityId, "Option", SecurityStatusDto.Active, "Option contract", "USD",
            JsonSerializer.SerializeToElement(new { contractMultiplier = 100m }),
            JsonSerializer.SerializeToElement(new { multiplier }), [], [], 1, Now.AddYears(-1), null);

        internal Task<LiveBrokeragePortfolioStatus> SynchronizeAsync()
            => Synchronization.SynchronizeAsync(AccountId, "broker-a");
    }
}
