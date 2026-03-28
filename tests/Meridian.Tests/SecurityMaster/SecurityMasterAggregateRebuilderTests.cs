using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Unit tests verifying that <see cref="SecurityMasterAggregateRebuilder"/> correctly folds
/// <c>CorpActEvent</c> records from the event store into the aggregate view of a security.
/// </summary>
public sealed class SecurityMasterAggregateRebuilderTests
{
    private readonly ISecurityMasterEventStore _eventStore = Substitute.For<ISecurityMasterEventStore>();
    private readonly ISecurityMasterSnapshotStore _snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
    private readonly SecurityMasterAggregateRebuilder _rebuilder;

    public SecurityMasterAggregateRebuilderTests()
    {
        _rebuilder = new SecurityMasterAggregateRebuilder(_eventStore, _snapshotStore);
    }

    [Fact]
    public async Task GetCorporateActionsAsync_WhenNoActionsExist_ReturnsEmptyList()
    {
        var securityId = Guid.NewGuid();
        _eventStore.LoadCorporateActionsAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CorporateActionDto>());

        var result = await _rebuilder.GetCorporateActionsAsync(securityId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCorporateActionsAsync_FoldsAllCorpActEventTypes_IntoAggregate()
    {
        var securityId = Guid.NewGuid();
        var actions = new[]
        {
            new CorporateActionDto(
                CorpActId: Guid.NewGuid(),
                SecurityId: securityId,
                EventType: "Dividend",
                ExDate: new DateOnly(2024, 2, 1),
                PayDate: new DateOnly(2024, 2, 15),
                DividendPerShare: 1.00m,
                Currency: "USD",
                SplitRatio: null,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null),
            new CorporateActionDto(
                CorpActId: Guid.NewGuid(),
                SecurityId: securityId,
                EventType: "StockSplit",
                ExDate: new DateOnly(2024, 6, 1),
                PayDate: null,
                DividendPerShare: null,
                Currency: null,
                SplitRatio: 2m,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null),
        };

        _eventStore.LoadCorporateActionsAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(actions);

        var result = await _rebuilder.GetCorporateActionsAsync(securityId);

        result.Should().HaveCount(2);
        result[0].EventType.Should().Be("Dividend");
        result[0].DividendPerShare.Should().Be(1.00m);
        result[1].EventType.Should().Be("StockSplit");
        result[1].SplitRatio.Should().Be(2m);
    }

    [Fact]
    public async Task GetCorporateActionsAsync_ReturnsActionsInAscendingExDateOrder()
    {
        var securityId = Guid.NewGuid();

        // Store returns already-sorted list (the store contract guarantees ex_date order)
        var actions = new[]
        {
            new CorporateActionDto(
                CorpActId: Guid.NewGuid(),
                SecurityId: securityId,
                EventType: "Dividend",
                ExDate: new DateOnly(2023, 12, 1),
                PayDate: null,
                DividendPerShare: 0.50m,
                Currency: "USD",
                SplitRatio: null,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null),
            new CorporateActionDto(
                CorpActId: Guid.NewGuid(),
                SecurityId: securityId,
                EventType: "StockSplit",
                ExDate: new DateOnly(2024, 3, 1),
                PayDate: null,
                DividendPerShare: null,
                Currency: null,
                SplitRatio: 4m,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null),
        };

        _eventStore.LoadCorporateActionsAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(actions);

        var result = await _rebuilder.GetCorporateActionsAsync(securityId);

        result.Should().HaveCount(2);
        result[0].ExDate.Should().BeBefore(result[1].ExDate,
            "corporate actions must be returned in ascending ex-date order");
    }

    [Fact]
    public async Task GetCorporateActionsAsync_ForwardsSecurityIdToEventStore()
    {
        var securityId = Guid.NewGuid();
        _eventStore.LoadCorporateActionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CorporateActionDto>());

        await _rebuilder.GetCorporateActionsAsync(securityId);

        await _eventStore.Received(1).LoadCorporateActionsAsync(securityId, Arg.Any<CancellationToken>());
    }
}
